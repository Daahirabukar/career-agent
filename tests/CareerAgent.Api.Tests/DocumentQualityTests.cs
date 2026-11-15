using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using CareerAgent.Api.Documents;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Xunit;

namespace CareerAgent.Api.Tests;

public sealed class DocumentQualityTests
{
    [Fact]
    public async Task Candidate_facts_require_confirmation_and_identity_is_added_only_locally()
    {
        using var app = new ApiFactory(); using var client = app.OpenClient();
        var profile = new DocumentProfile(new("Example Candidate", "example@example.com", "12345678", "Aarhus", "https://example.com"),
            "Example Bank, software developer, 2025–2026. Built a synthetic API.", "Example University, 2026", "Synthetic project", "Backend development");
        var unconfirmed = await client.PutAsJsonAsync("/api/document-profile", new { profile, confirmed = false });
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Equal("", (await client.GetFromJsonAsync<DocumentProfile>("/api/document-profile"))!.Identity.FullName);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/document-profile", new { profile, confirmed = true })).StatusCode);
        Assert.Equal(profile, await client.GetFromJsonAsync<DocumentProfile>("/api/document-profile"));
        var generator = (FakeDocumentGenerator)app.Services.GetRequiredService<IDocumentGenerator>();
        Assert.Equal(0, generator.Calls);
        Assert.True(File.Exists(Path.Combine(app.Documents, "document-profile.json")));
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(app.Documents, "document-profile.json")));
        var created = await client.PostAsJsonAsync("/api/jobs", new { title = "Software Developer", company = "Example" });
        var job = await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var id = job.GetProperty("id").GetGuid();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CareerAgent.Api.Data.JobDbContext>();
            (await db.Jobs.FindAsync(id))!.Description = new string('X', 120); await db.SaveChangesAsync();
        }
        var response = await client.PostAsJsonAsync($"/api/jobs/{id}/documents/generate", new { kind = "Both", language = "English" });
        response.EnsureSuccessStatusCode();
        Assert.Contains(profile.Experience, generator.LastDocumentFacts);
        Assert.DoesNotContain(profile.Identity.Email, string.Join(" ", generator.LastDocumentFacts));
        var documents = (await response.Content.ReadFromJsonAsync<DocumentHistoryDto[]>())!;
        Assert.All(documents, d => { Assert.Equal(profile.Identity, d.Versions[0].Content.Identity); Assert.Equal("English", d.Versions[0].Content.Language); });
        // Updating the profile must not silently change an existing version's identity.
        await client.PutAsJsonAsync("/api/document-profile", new { profile = profile with { Identity = profile.Identity with { FullName = "New Example" } }, confirmed = true });
        documents = (await client.GetFromJsonAsync<DocumentHistoryDto[]>($"/api/jobs/{id}/documents"))!;
        Assert.All(documents, d => Assert.Equal("Example Candidate", d.Versions[0].Content.Identity!.FullName));
    }
    [Fact]
    public void Cv_cannot_use_the_job_description_as_evidence_of_candidate_experience()
    {
        var content = new DocumentContent("Software Developer", null, [new("Experience", [new("paragraph", "Invented experience", [new("job:description", "Requires Java")])])]);
        var method = typeof(CodexDocumentGenerator).GetMethod("Validate", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, [new Dictionary<string, DocumentContent> { ["Cv"] = content }, "Cv", new Dictionary<string, string> { ["job:description"] = "Requires Java" }]));
        Assert.IsType<InvalidDataException>(exception.InnerException);
    }
    [Theory]
    [InlineData("Cv")]
    [InlineData("Application")]
    public void Word_export_is_valid_and_uses_the_reference_cv_and_correct_letter_structure(string kind)
    {
        var content = new DocumentContent("Software Developer", "Example Company", [
            new("Experience", [new("paragraph", "Synthetic evidence of software experience.", []), new("bullet", "Implemented a synthetic API.", [])])])
            { Identity = new("Example Candidate", "example@example.com", "12345678", "Aarhus", "https://example.com"), Language = "English" };
        using var stream = new MemoryStream(DocumentExporter.Export(content, kind));
        using var word = WordprocessingDocument.Open(stream, false);
        var errors = new OpenXmlValidator().Validate(word).ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors.Select(e => e.Description + " " + e.Path?.XPath)));
        var main = word.MainDocumentPart!; var body = main.Document!.Body!;
        Assert.Contains("Example Candidate", body.InnerText);

        if (kind == "Application") Assert.All(body.Descendants<Color>(), color => Assert.Equal("000000", color.Val?.Value));
        else { Assert.Contains(body.Descendants<Color>(), color => color.Val?.Value == "267C85"); Assert.Equal((uint)11906, body.Descendants<PageSize>().Single().Width!.Value); Assert.NotEmpty(body.Descendants<PageBreakBefore>()); Assert.NotNull(main.FooterParts.Single().Footer); }
        Assert.NotNull(main.NumberingDefinitionsPart);
        Assert.Contains(body.Descendants<ParagraphStyleId>(), style => style.Val?.Value == "Title");
        if (kind == "Application") { Assert.DoesNotContain("Experience", body.InnerText); Assert.Contains("Kind regards", body.InnerText); }
        else { Assert.Contains("EXPERIENCE", body.InnerText); Assert.NotEmpty(body.Descendants<NumberingProperties>()); }
    }
    [Fact]
    public void Cv_entry_metadata_uses_date_tabs_and_keeps_education_on_the_first_page()
    {
        var entry = new DocumentBlock("entry", "Software Developer", []) { Label = "Synthetic Company", Period = "2025–2026", Technologies = "C# · .NET" };
        var content = new DocumentContent("Software Engineer", null, [new("Erhvervserfaring", [entry]) { Page = 1 }, new("Udvalgt projekt", [entry]) { Page = 2 }]);
        using var stream = new MemoryStream(DocumentExporter.Export(content, "Cv"));
        using var word = WordprocessingDocument.Open(stream, false);
        Assert.Empty(new OpenXmlValidator().Validate(word));
        Assert.Contains(word.MainDocumentPart!.Document.Descendants<TabStop>(), tab => tab.Val?.Value == TabStopValues.Right);
        Assert.Contains("2025–2026", word.MainDocumentPart!.Document.InnerText);
        Assert.Equal(1, DocumentExporter.CvPage(new("Uddannelse og projekt", [])));
        Assert.Equal(2, DocumentExporter.CvPage(new("Tekniske kompetencer", [])));
    }
}
