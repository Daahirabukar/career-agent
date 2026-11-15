using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CareerAgent.Api.Jobs;
using CareerAgent.Api.Matching;

namespace CareerAgent.Api.Documents;

public interface IDocumentGenerator
{
    Task<IReadOnlyDictionary<string, DocumentContent>> Generate(Job job, SearchProfile profile,
        IReadOnlyList<(ReferenceDocument Reference, string Text)> references, string language, string kind, CancellationToken ct);
}

public sealed class CodexDocumentGenerator(IConfiguration configuration) : IDocumentGenerator
{
    public const string Version = "codex-documents-4";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record ProfileFact(string Id, string Text);

    public async Task<IReadOnlyDictionary<string, DocumentContent>> Generate(Job job, SearchProfile profile,
        IReadOnlyList<(ReferenceDocument Reference, string Text)> references, string language, string kind, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(180));
        var work = Directory.CreateTempSubdirectory("career-agent-doc-").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(work, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var schema = Path.Combine(work, "schema.json"); var output = Path.Combine(work, "result.json");
            await File.WriteAllTextAsync(schema, SchemaFor(kind), timeout.Token);
            var evidence = BuildProfileEvidence(profile).Select(x => x with { Text = RedactContactDetails(x.Text) }).ToArray();
            var jobText = (job.Description ?? "").Trim();
            if (jobText.Length > 18000) jobText = jobText[..18000];
            var referenceContext = job.Title + " " + jobText + " " + profile.Summary + " " + string.Join(' ', profile.Skills)
                + " " + string.Join(' ', profile.Experience.SelectMany(x => x.Skills.Append(x.Summary)));
            var refs = references.Select(r => new
            {
                r.Reference.Id,
                kind = r.Reference.Kind,
                // Select excerpts locally so unrelated personal information is not sent for each generation.
                text = SelectReferenceExcerpt(r.Text, referenceContext)
            }).ToArray();
            var jobEvidence = new { id = "job:description", title = job.Title, company = job.Company, location = job.Location, text = jobText };
            var payload = JsonSerializer.Serialize(new { language, documentKinds = kind == "Both" ? new[] { "Cv", "Application" } : new[] { kind }, profileEvidence = evidence, job = jobEvidence, references = refs }, JsonOptions);
            var prompt = DocumentWritingGuide.Instructions + "\nDATA:\n" + payload;
            var start = new ProcessStartInfo(configuration["CareerAgent:CodexPath"] ?? "codex")
            { WorkingDirectory = work, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in new[] { "exec", "--ignore-user-config", "--ignore-rules", "--ephemeral", "--skip-git-repo-check", "--sandbox", "read-only", "--json", "--output-schema", schema, "--output-last-message", output,
                "-c", "forced_login_method=\"chatgpt\"", "-c", "approval_policy=\"never\"", "-c", "web_search=\"disabled\"" }) start.ArgumentList.Add(argument);
            foreach (var feature in new[] { "shell_tool", "view_image", "apps", "plugins", "hooks", "browser_use", "browser_use_external", "computer_use", "image_generation", "multi_agent", "multi_agent_v2", "memories", "skill_search", "skill_mcp_dependency_install", "code_mode", "code_mode_host" })
            { start.ArgumentList.Add("--disable"); start.ArgumentList.Add(feature); }
            start.ArgumentList.Add("-");
            var inherited = start.Environment.ToDictionary(x => x.Key, x => x.Value);
            start.Environment.Clear();
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            start.Environment["HOME"] = home;
            start.Environment["PATH"] = "/usr/local/bin:/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:" + Path.Combine(home, ".npm-global", "bin");
            if (inherited.TryGetValue("CODEX_HOME", out var codexHome) && !string.IsNullOrWhiteSpace(codexHome)) start.Environment["CODEX_HOME"] = codexHome;
            if (inherited.TryGetValue("TMPDIR", out var tempDir) && !string.IsNullOrWhiteSpace(tempDir)) start.Environment["TMPDIR"] = tempDir;
            if (inherited.TryGetValue("LANG", out var lang)) start.Environment["LANG"] = lang;
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Codex kunne ikke startes.");
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.StandardInput.WriteAsync(prompt.AsMemory(), timeout.Token); process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token);
                var eventStream = await stdout; var diagnostic = await stderr;
                CodexExecution.ValidateCompletedRun(process.ExitCode, File.Exists(output), eventStream, diagnostic);
                var result = JsonSerializer.Deserialize<Dictionary<string, DocumentContent>>(await File.ReadAllTextAsync(output, timeout.Token), JsonOptions)
                    ?? throw new InvalidDataException("Codex returnerede ikke et dokument.");
                var allowedEvidence = evidence.ToDictionary(x => x.Id, x => x.Text, StringComparer.Ordinal);
                allowedEvidence["job:description"] = jobText;
                Validate(result, kind, allowedEvidence);
                return result;
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    private static ProfileFact[] BuildProfileEvidence(SearchProfile profile)
    {
        var result = new List<ProfileFact> { new("profile:summary", profile.Summary) };
        for (var i = 0; i < profile.Skills.Length; i++) result.Add(new($"profile:skill:{i}", profile.Skills[i]));
        for (var i = 0; i < profile.Experience.Length; i++)
        {
            var experience = profile.Experience[i];
            result.Add(new($"profile:experience:{i}:employer", experience.Employer + " — " + experience.Type));
            result.Add(new($"profile:experience:{i}:summary", experience.Summary));
            for (var j = 0; j < experience.Skills.Length; j++) result.Add(new($"profile:experience:{i}:skill:{j}", experience.Skills[j]));
        }
        for (var i = 0; i < profile.DocumentFacts.Length; i++)
            result.Add(new($"profile:document:{i}", profile.DocumentFacts[i]));
        return result.ToArray();
    }
    private static void Validate(Dictionary<string, DocumentContent> result, string kind, IReadOnlyDictionary<string, string> allowed)
    {
        var wanted = kind == "Both" ? new[] { "Cv", "Application" } : new[] { kind };
        if (result.Keys.Any(k => !wanted.Contains(k, StringComparer.Ordinal)) || wanted.Any(k => !result.ContainsKey(k))) throw new InvalidDataException("Dokumenttyperne fra AI matcher ikke forespørgslen.");
        foreach (var (documentKind, content) in result)
        {
                if (string.IsNullOrWhiteSpace(content.Title) || content.Title.Length > 180 || (content.Subtitle?.Length ?? 0) > 180 || content.Sections is null || content.Sections.Length is < 1 or > 12)
                throw new InvalidDataException("Dokumentets struktur er ugyldig.");
            var total = content.Title.Length + (content.Subtitle?.Length ?? 0);
            foreach (var section in content.Sections)
            {
                if (string.IsNullOrWhiteSpace(section.Heading) || section.Heading.Length > 100 || section.Page is not (null or 1 or 2) || section.Blocks is null || section.Blocks.Length is < 1 or > 16) throw new InvalidDataException("Dokumentsektionen er ugyldig.");
                foreach (var block in section.Blocks)
                {
                    total += (block.Text?.Length ?? 0) + (block.Label?.Length ?? 0) + (block.Period?.Length ?? 0) + (block.Technologies?.Length ?? 0);
                    if ((block.Label?.Length ?? 0) > 200 || (block.Period?.Length ?? 0) > 100 || (block.Technologies?.Length ?? 0) > 400)
                        throw new InvalidDataException("CV-felterne er for lange.");
                    if (documentKind == "Cv" && block.Evidence?.Any(e => e.Id.StartsWith("profile:", StringComparison.Ordinal)) != true)
                        throw new InvalidDataException("CV-påstande kræver profilgrundlag; jobteksten kan ikke dokumentere kandidatens erfaring.");
                    if (string.IsNullOrWhiteSpace(block.Text) || block.Text.Length > 1600 || block.Type is not ("paragraph" or "bullet" or "entry" or "subheading") || block.Evidence is null || block.Evidence.Length is < 1 or > 8
                        || block.Evidence.Any(e => !allowed.TryGetValue(e.Id, out var source) || string.IsNullOrWhiteSpace(e.Quote) || e.Quote.Length > 400 || !source.Contains(e.Quote, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException("En tekstblok mangler gyldig kildehenvisning eller har ugyldig tekst.");
                }
            }
            if (total > 14_000) throw new InvalidDataException("Dokumentudkastet er for langt.");
        }
    }
    private static string RedactContactDetails(string text)
    {
        text = Regex.Replace(text, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", "[e-mail udeladt]", RegexOptions.IgnoreCase);
        return Regex.Replace(text, @"(?<!\w)(?:\+?\d[\d ()-]{7,}\d)(?!\w)", match => Regex.IsMatch(match.Value, @"^(?:19|20)\d{2}\s*[-–]\s*(?:19|20)\d{2}$") ? match.Value : "[telefon udeladt]");
    }
    private static string SelectReferenceExcerpt(string text, string context)
    {
        var terms = Regex.Matches(context.ToLowerInvariant(), @"[a-zæøå0-9+#.]{3,}")
            .Select(m => m.Value).Where(x => x.Length >= 4 || x is "c#" or ".net" or "sql" or "api")
            .ToHashSet(StringComparer.Ordinal);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((line, index) => new { Line = line, Index = index })
            .Where(x => x.Index >= 3 && x.Line.Length is >= 25 and <= 1200
                && !Regex.IsMatch(x.Line, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}|(?<!\w)(?:\+?\d[\d ()-]{7,}\d)(?!\w)|\b\d{4}\s+[A-ZÆØÅ][a-zæøå]+\b", RegexOptions.IgnoreCase))
            .Select(x => new { x.Line, x.Index, Score = Regex.Matches(x.Line.ToLowerInvariant(), @"[a-zæøå0-9+#.]{3,}").Select(m => m.Value).Distinct().Count(term => terms.Contains(term)) })
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenBy(x => x.Index).Take(10).OrderBy(x => x.Index);
        var excerpt = string.Join('\n', lines.Select(x => x.Line));
        excerpt = RedactContactDetails(excerpt);
        return excerpt.Length > 4000 ? excerpt[..4000] : excerpt;
    }
    private static string SchemaFor(string kind)
    {
        var kinds = kind == "Both" ? new[] { "Cv", "Application" } : [kind];
        var block = new JsonObject
        {
            ["type"] = "object", ["additionalProperties"] = false,
            ["required"] = new JsonArray("type", "text", "evidence", "label", "period", "technologies"),
            ["properties"] = new JsonObject
            {
                ["type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("paragraph", "bullet", "entry", "subheading") },
                ["text"] = new JsonObject { ["type"] = "string" },
                ["label"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                ["period"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                ["technologies"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                ["evidence"] = new JsonObject
                {
                    ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 8,
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object", ["additionalProperties"] = false,
                        ["required"] = new JsonArray("id", "quote"),
                        ["properties"] = new JsonObject
                        { ["id"] = new JsonObject { ["type"] = "string" }, ["quote"] = new JsonObject { ["type"] = "string" } }
                    }
                }
            }
        };
        var section = new JsonObject
        {
            ["type"] = "object", ["additionalProperties"] = false,
            ["required"] = new JsonArray("heading", "blocks", "page"),
            ["properties"] = new JsonObject
            {
                ["heading"] = new JsonObject { ["type"] = "string" },
                ["page"] = new JsonObject { ["type"] = new JsonArray("integer", "null"), ["enum"] = new JsonArray(1, 2, null) },
                ["blocks"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 16, ["items"] = block }
            }
        };
        var document = new JsonObject
        {
            ["type"] = "object", ["additionalProperties"] = false,
            ["required"] = new JsonArray("title", "subtitle", "sections"),
            ["properties"] = new JsonObject
            {
                ["title"] = new JsonObject { ["type"] = "string" },
                ["subtitle"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                ["sections"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 12, ["items"] = section }
            }
        };
        var properties = new JsonObject();
        foreach (var documentKind in kinds) properties[documentKind] = document.DeepClone();
        return new JsonObject
        {
            ["type"] = "object", ["additionalProperties"] = false,
            ["required"] = new JsonArray(kinds.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["properties"] = properties
        }.ToJsonString();
    }
}
