using System.Text.Json;
using CareerAgent.Api.Data;
using CareerAgent.Api.Jobs;
using CareerAgent.Api.Matching;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;

namespace CareerAgent.Api.Documents;

public static class DocumentEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] Kinds = ["Cv", "Application"];
    public static void MapDocumentEndpoints(this WebApplication app)
    {
        app.MapGet("/api/references", async (JobDbContext db, CancellationToken ct) => Results.Ok(
            (await db.ReferenceDocuments.AsNoTracking().OrderByDescending(x => x.CreatedAt).ToListAsync(ct)).Select(ToDto)));
        app.MapPost("/api/references", async (HttpRequest request, JobDbContext db, DocumentStorage storage, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Results.Problem(statusCode: 415, title: "Upload skal være multipart/form-data.");
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Vælg en PDF- eller DOCX-fil."] });
            var (document, error) = await storage.AddReference(file, form["kind"].ToString(), form["displayName"].ToString(), ct);
            if (document is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [error] });
            db.ReferenceDocuments.Add(document);
            try { await db.SaveChangesAsync(ct); }
            catch { storage.DeleteReference(document); throw; }
            return Results.Created($"/api/references/{document.Id}", ToDto(document));
        });
        app.MapDelete("/api/references/{id:guid}", async (Guid id, JobDbContext db, DocumentStorage storage, CancellationToken ct) =>
        {
            var reference = await db.ReferenceDocuments.FindAsync([id], ct);
            if (reference is null) return Results.Problem(statusCode: 404, title: "Referencen blev ikke fundet.");
            db.ReferenceDocuments.Remove(reference); await db.SaveChangesAsync(ct); storage.DeleteReference(reference);
            return Results.NoContent();
        });
        app.MapGet("/api/jobs/{jobId:guid}/documents", async (Guid jobId, JobDbContext db, DocumentStorage storage, CancellationToken ct) =>
        {
            if (!await db.Jobs.AnyAsync(x => x.Id == jobId, ct)) return Results.Problem(statusCode: 404, title: "Jobbet blev ikke fundet.");
            var docs = await db.GeneratedDocuments.AsNoTracking().Where(x => x.JobId == jobId).ToListAsync(ct);
            var versions = await db.GeneratedDocumentVersions.AsNoTracking().Where(v => docs.Select(d => d.Id).Contains(v.DocumentId)).OrderBy(v => v.Number).ToListAsync(ct);
            var response = new List<DocumentHistoryDto>();
            foreach (var doc in docs)
            {
                var values = new List<DocumentVersionDto>();
                foreach (var version in versions.Where(v => v.DocumentId == doc.Id).OrderBy(v => v.Number))
                {
                    var content = JsonSerializer.Deserialize<DocumentContent>(storage.ReadVersion(jobId, doc.Id, version.ContentFileName), JsonOptions);
                    if (content is null) continue;
                    values.Add(new(version.Id, version.Number, version.CreatedAt, version.Engine,
                        JsonSerializer.Deserialize<Guid[]>(version.ReferenceIdsJson, JsonOptions) ?? [], content));
                }
                response.Add(new(doc.Id, doc.JobId, doc.Kind, values.ToArray()));
            }
            return Results.Ok(response);
        });
        app.MapPost("/api/jobs/{jobId:guid}/documents/generate", async (Guid jobId, GenerateDocumentsRequest request,
            JobDbContext db, ProfileStore profiles, DocumentStorage storage, IDocumentGenerator generator, DocumentGenerationGate gate, CancellationToken ct) =>
        {
            if (!await gate.Lock.WaitAsync(0, ct)) return Results.Problem(statusCode: 409, title: "En dokumentgenerering kører allerede.");
            try
            {
                if (request.Kind is null || request.Kind is not ("Cv" or "Application" or "Both"))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["Vælg CV, ansøgning eller begge dokumenter."] });
                if (request.Language is not ("Dansk" or "English"))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["language"] = ["Vælg dansk eller engelsk."] });
                var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId, ct);
                if (job is null) return Results.Problem(statusCode: 404, title: "Jobbet blev ikke fundet.");
                if (job.Description.Length < 80) return Results.Problem(statusCode: 409, title: "Jobopslaget har ikke tilstrækkelig tekst til målrettet dokumentgenerering.");
                var ids = (request.ReferenceIds ?? []).Distinct().ToArray();
                if (ids.Length > 4) return Results.ValidationProblem(new Dictionary<string, string[]> { ["referenceIds"] = ["Vælg højst fire referencefiler."] });
                var references = await db.ReferenceDocuments.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
                if (references.Count != ids.Length) return Results.Problem(statusCode: 400, title: "En valgt referencefil findes ikke længere.");
                var lang = request.Language;
                var (profile, profileVersion) = profiles.Read();
                var context = references.Select(r => (r, storage.ReadReferenceText(r))).ToArray();
                var generated = await generator.Generate(job, profile, context, lang, request.Kind, ct);
                foreach (var (kind, content) in generated)
                {
                    var document = await db.GeneratedDocuments.SingleOrDefaultAsync(x => x.JobId == jobId && x.Kind == kind, ct);
                    if (document is null) { document = new GeneratedDocument { JobId = jobId, Kind = kind }; db.GeneratedDocuments.Add(document); }
                    var number = (await db.GeneratedDocumentVersions.Where(x => x.DocumentId == document.Id).Select(x => (int?)x.Number).MaxAsync(ct) ?? 0) + 1;
                    var json = JsonSerializer.Serialize(content, JsonOptions);
                    var filename = storage.WriteVersion(jobId, document.Id, number, json);
                    db.GeneratedDocumentVersions.Add(new GeneratedDocumentVersion { DocumentId = document.Id, Number = number,
                        ContentFileName = filename, ProfileVersion = profileVersion,
                        ReferenceIdsJson = JsonSerializer.Serialize(ids, JsonOptions), Engine = CodexDocumentGenerator.Version });
                }
                await db.SaveChangesAsync(ct);
                return Results.Ok(await LoadDocuments(jobId, db, storage, ct));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { return Results.Problem(statusCode: 504, title: "Codex brugte for lang tid på dokumentudkastet."); }
            catch (Exception e) when (!ct.IsCancellationRequested && e is not OutOfMemoryException)
            { return Results.Problem(statusCode: 503, title: "Dokumentudkastet kunne ikke oprettes. Kontrollér profil, valgte filer, ChatGPT-login og kvote."); }
            finally { gate.Lock.Release(); }
        });
        app.MapPut("/api/documents/{id:guid}", async (Guid id, SaveDocumentRequest request, JobDbContext db, DocumentStorage storage, CancellationToken ct) =>
        {
            var document = await db.GeneratedDocuments.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (document is null) return Results.Problem(statusCode: 404, title: "Dokumentet blev ikke fundet.");
            var errors = ValidateEdited(request.Content);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var latest = await db.GeneratedDocumentVersions.Where(x => x.DocumentId == id).OrderByDescending(x => x.Number).FirstAsync(ct);
            var number = latest.Number + 1;
            var filename = storage.WriteVersion(document.JobId, document.Id, number, JsonSerializer.Serialize(request.Content, JsonOptions));
            db.GeneratedDocumentVersions.Add(new GeneratedDocumentVersion { DocumentId = document.Id, Number = number,
                ContentFileName = filename, ProfileVersion = latest.ProfileVersion, ReferenceIdsJson = latest.ReferenceIdsJson,
                Engine = "manual-edit" });
            await db.SaveChangesAsync(ct);
            return Results.Ok(await LoadDocuments(document.JobId, db, storage, ct));
        });
        app.MapGet("/api/documents/{id:guid}/export/docx", async (Guid id, int? version, JobDbContext db, DocumentStorage storage, CancellationToken ct) =>
        {
            var document = await db.GeneratedDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (document is null) return Results.Problem(statusCode: 404, title: "Dokumentet blev ikke fundet.");
            var versions = db.GeneratedDocumentVersions.AsNoTracking().Where(x => x.DocumentId == id);
            var selected = version is null ? await versions.OrderByDescending(x => x.Number).FirstOrDefaultAsync(ct)
                : await versions.SingleOrDefaultAsync(x => x.Number == version, ct);
            if (selected is null) return Results.Problem(statusCode: 404, title: "Dokumentversionen blev ikke fundet.");
            var content = JsonSerializer.Deserialize<DocumentContent>(storage.ReadVersion(document.JobId, id, selected.ContentFileName), JsonOptions);
            if (content is null) return Results.Problem(statusCode: 500, title: "Dokumentet kunne ikke læses.");
            var bytes = ExportDocx(content);
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"career-agent-{document.Kind.ToLowerInvariant()}-v{selected.Number}.docx");
        });
    }

    private static ReferenceDto ToDto(ReferenceDocument x) => new(x.Id, x.Kind, x.DisplayName, Path.GetExtension(x.FileName).TrimStart('.').ToUpperInvariant(), x.SizeBytes, x.CreatedAt);
    private static async Task<DocumentHistoryDto[]> LoadDocuments(Guid jobId, JobDbContext db, DocumentStorage storage, CancellationToken ct)
    {
        var docs = await db.GeneratedDocuments.AsNoTracking().Where(x => x.JobId == jobId).ToListAsync(ct);
        var result = new List<DocumentHistoryDto>();
        foreach (var doc in docs)
        {
            var history = await db.GeneratedDocumentVersions.AsNoTracking().Where(x => x.DocumentId == doc.Id).OrderBy(x => x.Number).ToListAsync(ct);
            result.Add(new(doc.Id, jobId, doc.Kind, history.Select(v => new DocumentVersionDto(v.Id, v.Number, v.CreatedAt, v.Engine,
                JsonSerializer.Deserialize<Guid[]>(v.ReferenceIdsJson, JsonOptions) ?? [],
                JsonSerializer.Deserialize<DocumentContent>(storage.ReadVersion(jobId, doc.Id, v.ContentFileName), JsonOptions)!)).ToArray()));
        }
        return result.ToArray();
    }
    private static Dictionary<string, string[]> ValidateEdited(DocumentContent? content)
    {
        var errors = new Dictionary<string, string[]>();
        if (content is null || string.IsNullOrWhiteSpace(content.Title) || content.Title.Length > 180 || (content.Subtitle?.Length ?? 0) > 180 || content.Sections is null || content.Sections.Length is < 1 or > 12)
            errors["content"] = ["Dokumentets titel eller struktur er ugyldig."];
        else if (content.Sections.Any(s => string.IsNullOrWhiteSpace(s.Heading) || s.Heading.Length > 100 || s.Blocks is null || s.Blocks.Length is < 1 or > 16 || s.Blocks.Any(b => string.IsNullOrWhiteSpace(b.Text) || b.Text.Length > 1600 || b.Type is not ("paragraph" or "bullet") || b.Evidence is null || b.Evidence.Length is < 1 or > 8 || b.Evidence.Any(e => string.IsNullOrWhiteSpace(e.Id) || e.Id.Length > 100 || string.IsNullOrWhiteSpace(e.Quote) || e.Quote.Length > 400))))
            errors["content"] = ["En dokumentsektion indeholder ugyldige tekstblokke."];
        else if (content!.Sections.Sum(s => s.Heading.Length + s.Blocks.Sum(b => b.Text.Length)) + content.Title.Length + (content.Subtitle?.Length ?? 0) > 14_000)
            errors["content"] = ["Dokumentudkastet er for langt."];
        return errors;
    }
    private static byte[] ExportDocx(DocumentContent content)
    {
        using var stream = new MemoryStream();
        using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = word.AddMainDocumentPart(); var body = new Body(); main.Document = new Document(body);
            body.Append(StyledParagraph(content.Title, "Title"));
            if (!string.IsNullOrWhiteSpace(content.Subtitle)) body.Append(StyledParagraph(content.Subtitle!, "Subtitle"));
            foreach (var section in content.Sections)
            {
                body.Append(StyledParagraph(section.Heading, "Heading2"));
                foreach (var block in section.Blocks)
                    body.Append(StyledParagraph(block.Type == "bullet" ? "•  " + block.Text : block.Text, "Normal"));
            }
            var properties = new SectionProperties(new PageMargin { Top = 850, Bottom = 850, Left = 1000, Right = 1000 });
            body.Append(properties); main.Document.Save();
        }
        return stream.ToArray();
    }
    private static Paragraph StyledParagraph(string text, string style)
    {
        var title = style == "Title"; var subtitle = style == "Subtitle"; var heading = style == "Heading2";
        var paragraphProperties = new ParagraphProperties(new SpacingBetweenLines
        { Before = heading ? "300" : "0", After = title ? "80" : heading ? "100" : "140" });
        if (title) paragraphProperties.Append(new Justification { Val = JustificationValues.Center });
        var runProperties = new RunProperties(new RunFonts { Ascii = "Aptos", HighAnsi = "Aptos" },
            new FontSize { Val = title ? "34" : heading ? "24" : subtitle ? "22" : "21" },
            new Color { Val = title || heading ? "0F5B57" : subtitle ? "64748B" : "263238" });
        if (title || heading) runProperties.Append(new Bold());
        return new Paragraph(paragraphProperties, new Run(runProperties, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }
}
