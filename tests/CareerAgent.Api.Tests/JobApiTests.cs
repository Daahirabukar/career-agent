using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CareerAgent.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CareerAgent.Api.Tests;

public sealed class ApiFactory(string? databasePath = null) : WebApplicationFactory<Program>
{
    private readonly string database = databasePath ?? Path.Combine(Path.GetTempPath(), $"career-agent-test-{Guid.NewGuid()}.db");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:Jobs"] = $"Data Source={database};Pooling=False" }));
    }
    public HttpClient OpenClient()
    {
        var client = CreateClient();
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<JobDbContext>().Database.Migrate();
        return client;
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && databasePath is null) foreach (var suffix in new[] { "", "-shm", "-wal" }) File.Delete(database + suffix);
    }
}

public sealed class JobApiTests
{
    [Theory]
    [InlineData("", "Company", "")]
    [InlineData("   ", "Company", "")]
    [InlineData("Developer", "", "")]
    [InlineData("Developer", "Company", "javascript:alert(1)")]
    [InlineData("Developer", "Company", "https://user:password@example.com")]
    public async Task Invalid_job_returns_problem_without_saving(string title, string company, string sourceUrl)
    {
        using var app = new ApiFactory(); using var client = app.OpenClient();
        var response = await client.PostAsJsonAsync("/api/jobs", new { title, company, sourceUrl });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await client.GetFromJsonAsync<JsonElement[]>("/api/jobs") ?? []);
    }
    [Fact]
    public async Task Jobs_survive_application_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"career-agent-restart-{Guid.NewGuid()}.db");
        try
        {
            using (var first = new ApiFactory(path))
            using (var client = first.OpenClient())
            {
                var response = await client.PostAsJsonAsync("/api/jobs", new { title = "EKSEMPEL restart", company = "EKSEMPEL" });
                var job = await response.Content.ReadFromJsonAsync<JsonElement>();
                var patched = await client.PatchAsJsonAsync($"/api/jobs/{job.GetProperty("id").GetString()}/status", new { status = "Interview" });
                patched.EnsureSuccessStatusCode();
            }
            using var restarted = new ApiFactory(path);
            using var secondClient = restarted.OpenClient();
            var jobs = await secondClient.GetFromJsonAsync<JsonElement[]>("/api/jobs");
            var stored = Assert.Single(jobs!);
            Assert.Equal("Interview", stored.GetProperty("status").GetString());
            Assert.Equal(DateTimeKind.Utc, stored.GetProperty("createdAt").GetDateTime().Kind);
        }
        finally { foreach (var suffix in new[] { "", "-shm", "-wal" }) File.Delete(path + suffix); }
    }
    [Fact]
    public async Task Overlong_input_is_rejected()
    {
        using var app = new ApiFactory(); using var client = app.OpenClient();
        var response = await client.PostAsJsonAsync("/api/jobs", new { title = new string('x', 201), company = "Example" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task Created_job_and_status_are_persisted_in_sqlite()
    {
        using var app = new ApiFactory(); using var client = app.OpenClient();
        var created = await client.PostAsJsonAsync("/api/jobs", new { title = " Example developer ", company = "Example only", location = "Aarhus", sourceUrl = "https://example.com/job" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var job = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = job.GetProperty("id").GetGuid();
        Assert.Equal("Saved", job.GetProperty("status").GetString());
        var patched = await client.PatchAsJsonAsync($"/api/jobs/{id}/status", new { status = "Interview" });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        // New DbContext reads from SQLite rather than the request's tracked entities.
        using var scope = app.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<JobDbContext>().Jobs.AsNoTracking().SingleAsync();
        Assert.Equal("Example developer", stored.Title);
        Assert.Equal(CareerAgent.Api.Jobs.JobStatus.Interview, stored.Status);
        Assert.Equal(DateTimeKind.Utc, job.GetProperty("createdAt").GetDateTime().Kind);
        var listed = await client.GetFromJsonAsync<JsonElement[]>("/api/jobs");
        Assert.Equal("Interview", Assert.Single(listed!).GetProperty("status").GetString());
    }
    [Theory]
    [InlineData("invalid")]
    [InlineData("1")]
    [InlineData("999")]
    [InlineData(null)]
    public async Task Invalid_status_cannot_mutate_job(string? status)
    {
        using var app = new ApiFactory(); using var client = app.OpenClient();
        var created = await client.PostAsJsonAsync("/api/jobs", new { title = "Example", company = "Example" });
        var job = await created.Content.ReadFromJsonAsync<JsonElement>();
        var response = await client.PatchAsJsonAsync($"/api/jobs/{job.GetProperty("id").GetString()}/status", new { status });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var jobs = await client.GetFromJsonAsync<JsonElement[]>("/api/jobs");
        Assert.Equal("Saved", Assert.Single(jobs!).GetProperty("status").GetString());
    }
    [Fact]
    public async Task Missing_job_returns_problem_details()
    {
        using var app = new ApiFactory(); using var client = app.OpenClient();
        var response = await client.PatchAsJsonAsync($"/api/jobs/{Guid.NewGuid()}/status", new { status = "Applied" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
    [Fact]
    public async Task Untrusted_browser_origin_cannot_write()
    {
        using var app = new ApiFactory(); using var client = app.OpenClient();
        client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        var response = await client.PostAsJsonAsync("/api/jobs", new { title = "Example", company = "Example" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
    [Fact]
    public async Task Local_frontend_preflight_is_allowed()
    {
        using var app = new ApiFactory(); using var client = app.OpenClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/jobs");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }
    [Fact]
    public async Task Health_and_empty_state_work()
    {
        using var app = new ApiFactory(); using var client = app.OpenClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/jobs"))!);
    }
    [Fact]
    public async Task Malformed_json_returns_problem_details()
    {
        using var app = new ApiFactory(); using var client = app.OpenClient();
        var response = await client.PostAsync("/api/jobs", new StringContent("{broken", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
