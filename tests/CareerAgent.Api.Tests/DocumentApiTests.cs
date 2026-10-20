using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CareerAgent.Api.Documents;
using CareerAgent.Api.Data;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CareerAgent.Api.Tests;

public sealed class DocumentApiTests
{
    [Theory]
    [InlineData("Cv", "Cv")]
    [InlineData("Application", "Application")]
    [InlineData("Both", "Application")]
    public void Codex_schema_is_valid_and_only_requires_requested_documents(string kind, string expectedKey)
    {
        var method = typeof(CodexDocumentGenerator).GetMethod("SchemaFor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        using var schema = JsonDocument.Parse((string)method.Invoke(null, [kind])!);
        Assert.True(schema.RootElement.GetProperty("properties").TryGetProperty(expectedKey, out _));
        Assert.Equal(kind == "Both" ? 2 : 1, schema.RootElement.GetProperty("required").GetArrayLength());
    }

    [Fact]
    public async Task Documents_are_only_generated_after_explicit_request_and_keep_versions_locally()
    {
        using var app = new ApiFactory(); using var client = app.OpenClient();
        var created = await client.PostAsJsonAsync("/api/jobs", new { title = "Software engineer", company = "Example", description = new string('J', 120) });
        created.EnsureSuccessStatusCode();
        var jobId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await AddJobDescription(app, jobId);
        var generator = (FakeDocumentGenerator)app.Services.GetRequiredService<IDocumentGenerator>();

        Assert.Empty(await client.GetFromJsonAsync<DocumentHistoryDto[]>($"/api/jobs/{jobId}/documents") ?? []);
        Assert.Equal(0, generator.Calls);

        using var file = CreateReferenceDocx();
        using var multipart = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(file.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        multipart.Add(fileContent, "file", "reference.docx"); multipart.Add(new StringContent("Cv"), "kind"); multipart.Add(new StringContent("Eksempel-CV"), "displayName");
        var uploaded = await client.PostAsync("/api/references", multipart);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var reference = await uploaded.Content.ReadFromJsonAsync<ReferenceDto>();

        var generated = await client.PostAsJsonAsync($"/api/jobs/{jobId}/documents/generate", new { kind = "Both", referenceIds = new[] { reference!.Id }, language = "Dansk" });
        Assert.Equal(HttpStatusCode.OK, generated.StatusCode);
        Assert.Equal(1, generator.Calls);
        Assert.Equal(reference.Id, Assert.Single(generator.LastReferenceIds));
        var docs = await generated.Content.ReadFromJsonAsync<DocumentHistoryDto[]>();
        Assert.Equal(2, docs!.Length);
        var cv = docs.Single(x => x.Kind == "Cv");
        Assert.Equal(1, Assert.Single(cv.Versions).Number);
        Assert.Equal(reference.Id, Assert.Single(cv.Versions[0].ReferenceIds));
        Assert.NotEmpty(Directory.GetFiles(app.Documents, "*.json", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Path.Combine(AppContext.BaseDirectory, "documents")));

        var edited = cv.Versions[0].Content with { Title = "Redigeret CV" };
        var saved = await client.PutAsJsonAsync($"/api/documents/{cv.Id}", new SaveDocumentRequest(edited));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var updated = (await saved.Content.ReadFromJsonAsync<DocumentHistoryDto[]>())!.Single(x => x.Id == cv.Id);
        Assert.Equal(new[] { 1, 2 }, updated.Versions.Select(x => x.Number));
        Assert.Equal("manual-edit", updated.Versions[^1].Engine);

        var export = await client.GetAsync($"/api/documents/{cv.Id}/export/docx");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", export.Content.Headers.ContentType?.MediaType);
        using var archive = new ZipArchive(await export.Content.ReadAsStreamAsync());
        Assert.Contains(archive.Entries, x => x.FullName == "word/document.xml");
        var oldVersionExport = await client.GetAsync($"/api/documents/{cv.Id}/export/docx?version=1");
        Assert.Equal(HttpStatusCode.OK, oldVersionExport.StatusCode);
        Assert.Contains("v1.docx", oldVersionExport.Content.Headers.ContentDisposition?.FileName);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/references/{reference.Id}")).StatusCode);
        Assert.Empty(await client.GetFromJsonAsync<ReferenceDto[]>("/api/references") ?? []);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(app.Documents));
    }

    [Fact]
    public async Task Reference_upload_rejects_unsupported_files_and_bad_generation_requests()
    {
        using var app = new ApiFactory(); using var client = app.OpenClient();
        using var multipart = new MultipartFormDataContent();
        using var data = new ByteArrayContent("private data"u8.ToArray());
        multipart.Add(data, "file", "profile.txt"); multipart.Add(new StringContent("Cv"), "kind"); multipart.Add(new StringContent("Bad file"), "displayName");
        var upload = await client.PostAsync("/api/references", multipart);
        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        Assert.Empty(await client.GetFromJsonAsync<ReferenceDto[]>("/api/references") ?? []);

        var job = await client.PostAsJsonAsync("/api/jobs", new { title = "Engineer", company = "Example", description = new string('J', 100) });
        var id = (await job.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await AddJobDescription(app, id);
        var invalid = await client.PostAsJsonAsync($"/api/jobs/{id}/documents/generate", new { kind = "Both", referenceIds = new Guid[5], language = "Dansk" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(0, ((FakeDocumentGenerator)app.Services.GetRequiredService<IDocumentGenerator>()).Calls);
    }

    private static MemoryStream CreateReferenceDocx()
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var part = document.AddMainDocumentPart();
            part.Document = new Document(new Body(new Paragraph(new Run(new Text("Profile and software experience")))));
            part.Document.Save();
        }
        stream.Position = 0;
        return stream;
    }
    private static async Task AddJobDescription(ApiFactory app, Guid id)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JobDbContext>();
        var job = await db.Jobs.FindAsync(id);
        job!.Description = new string('J', 120);
        await db.SaveChangesAsync();
    }
}
