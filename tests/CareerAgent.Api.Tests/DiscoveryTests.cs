using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CareerAgent.Api.Data;
using CareerAgent.Api.Discovery;
using CareerAgent.Api.Jobs;
using CareerAgent.Api.Matching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CareerAgent.Api.Tests;
public sealed class DiscoveryTests
{
    public static SearchProfile Profile => new("Synthetic graduate profile. Relevant paid backend experience; duration unknown.", ["C#", ".NET", "REST", "API", "SQL", "React", "TypeScript"], ["software", "developer", "udvikler", "backend"], ["aarhus", "århus"], ["horsens", "randers"], ["aalborg", "vejle"])
    { Experience = [new("Example Bank", "Professional", ["C#", ".NET", "API", "SQL"], "Professional backend experience; duration not specified."), new("Example Studio", "Internship", ["React", "TypeScript"], "Frontend and backend internship experience.")] };
    private static SourceDefinition Source(string id = "test") => new(id, "Example", "Greenhouse", "example", "Global", true, "https://example.com", "Synthetic fixture", "Test only");
    private static ImportedJob Posting(string id = "1", string url = "https://example.com/jobs/1") => new(id, "Software Developer", "Example", "Aarhus", url, "Develop C# and .NET APIs using SQL. Full-time. Graduates welcome.", "Full-time", "Hybrid", "DK", DateTime.UtcNow, null);

    [Theory]
    [InlineData("Internship Software Developer", "Aarhus", "", "Excluded")]
    [InlineData("Working Student Developer", "Aarhus", "", "Excluded")]
    [InlineData("Software Developer", "Aarhus", "At least 5 years of experience required.", "Excluded")]
    [InlineData("Software Developer", "Aarhus", "5+ years experience required.", "Excluded")]
    [InlineData("Software Developer", "Aarhus", "A minimum of 6 years of work experience is required.", "Excluded")]
    [InlineData("Software Developer", "Aarhus", "Minimum 5 years preferred, graduates are welcome.", "Review")]
    [InlineData("Junior Software Developer", "Aarhus", "Full-time Java development.", "Review")]
    [InlineData("Software Developer", "Aarhus", "Full-time Python development.", "Review")]
    [InlineData("Software Engineer", "Copenhagen, Denmark", "", "Review")]
    [InlineData("Software Developer", "New York", "", "Excluded")]
    [InlineData("Sales Manager", "Aarhus", "", "Excluded")]
    [InlineData("Software Developer", "", "", "Review")]
    [InlineData("Senior Software Developer", "Aarhus", "Experience preferred.", "Review")]
    public void Rules_distinguish_exclusions_and_unknowns(string title, string location, string text, string eligibility)
    {
        var result = RuleMatcher.Evaluate(new Job { Title = title, Location = location, Description = text }, Profile, DateTime.UtcNow);
        Assert.Equal(eligibility, result.Eligibility); Assert.InRange(result.Score, 0, 100);
    }
    [Fact]
    public void Aarhus_has_higher_priority_and_unknown_hours_are_not_invented()
    {
        var a = RuleMatcher.Evaluate(new Job { Title = "Software Developer", Location = "Aarhus" }, Profile, DateTime.UtcNow);
        var b = RuleMatcher.Evaluate(new Job { Title = "Software Developer", Location = "Copenhagen, Denmark" }, Profile, DateTime.UtcNow);
        Assert.True(a.Score > b.Score); Assert.Contains(a.Uncertainties, x => x.Contains("timeta"));
    }
    [Fact]
    public void Remote_europe_requires_denmark_confirmation()
    {
        var result = RuleMatcher.Evaluate(new Job { Title = "Software Developer", Location = "Remote - Europe", WorkplaceType = "Remote" }, Profile, DateTime.UtcNow);
        Assert.Equal("Review", result.Eligibility); Assert.Contains(result.Uncertainties, x => x.Contains("Danmark"));
    }
    [Fact]
    public void Weighted_score_uses_all_five_categories_and_does_not_exclude_three_year_requests()
    {
        var job = new Job { Title = "Backend Software Engineer", Location = "Aarhus", Country = "DK", EmploymentType = "Full-time",
            Description = "We ask for 3 years of experience. Build Java services, Spring Boot REST APIs and PostgreSQL. Mentorship and training are provided." };
        var result = RuleMatcher.Evaluate(job, Profile, DateTime.UtcNow);
        Assert.Equal("Review", result.Eligibility);
        Assert.Equal(20, result.Role);
        Assert.Equal(17, result.Experience);
        Assert.Equal(15, result.Geography);
        Assert.Equal(10, result.Development);
        Assert.Contains(result.TransferableSkills, s => s.Contains("java", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.TransferableSkills, s => s.Contains("Spring Boot", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.MatchedSkills, s => s.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(result.Skills + result.Experience + result.Role + result.Geography + result.Development, result.Score);
        Assert.InRange(result.Score, 0, 100);
    }
    [Fact]
    public void Senior_title_is_a_strong_caution_even_with_relevant_professional_experience()
    {
        var result = RuleMatcher.Evaluate(new Job { Title = "Senior Software Developer", Location = "Aarhus", Description = "Develop C# and REST APIs." }, Profile, DateTime.UtcNow);
        Assert.Equal("Review", result.Eligibility);
        Assert.Equal(8, result.Experience);
        Assert.Contains("senioritet", string.Join(" ", result.Uncertainties), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Mulig", result.Realism);
    }
    [Fact]
    public void Expired_jobs_and_part_time_are_excluded()
    {
        var job = new Job { Title = "Software Developer", Location = "Aarhus", Deadline = DateTime.UtcNow.AddDays(-1) };
        Assert.Equal("Excluded", RuleMatcher.Evaluate(job, Profile, DateTime.UtcNow).Eligibility);
        job.Deadline = null; job.EmploymentType = "Part-time";
        Assert.Equal("Excluded", RuleMatcher.Evaluate(job, Profile, DateTime.UtcNow).Eligibility);
    }
    [Fact]
    public async Task Reimport_is_idempotent_and_preserves_user_state()
    {
        using var app = new ApiFactory(); using var client = app.OpenClient(); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JobDbContext>(); var importer = new ImportService(db);
        await importer.Import(Source(), new([Posting()], true), Profile, "v1", default);
        var job = db.Jobs.Single(); job.Status = JobStatus.Applied; job.SeenAt = DateTime.UtcNow; db.SaveChanges();
        var result = await importer.Import(Source(), new([Posting()], true), Profile, "v1", default);
        Assert.Equal(0, result.Added); Assert.Single(db.Jobs); Assert.Single(db.JobOccurrences);
        Assert.Equal(JobStatus.Applied, db.Jobs.Single().Status); Assert.NotNull(db.Jobs.Single().SeenAt); Assert.NotNull(db.Jobs.Single().StatusVerifiedAt);
    }
    [Fact]
    public async Task Same_url_across_sources_merges_and_preserves_source_provenance()
    {
        using var app = new ApiFactory(); using var client = app.OpenClient(); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JobDbContext>(); var importer = new ImportService(db);
        await importer.Import(Source("a"), new([Posting()], true), Profile, "v1", default);
        await importer.Import(Source("b"), new([Posting("other", "https://example.com/jobs/1?utm_source=test")], true), Profile, "v1", default);
        Assert.Single(db.Jobs); Assert.Equal(2, db.JobOccurrences.Count());
        await importer.Import(Source("a"), new([], true), Profile, "v1", default);
        Assert.True(db.Jobs.Single().IsActive);
        await importer.Import(Source("b"), new([], true), Profile, "v1", default);
        Assert.False(db.Jobs.Single().IsActive); Assert.Equal("Excluded", db.Jobs.Single().Eligibility);
    }
    [Fact]
    public async Task Identical_posting_across_sources_merges_even_when_tracking_urls_differ()
    {
        using var app = new ApiFactory(); using var client = app.OpenClient(); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JobDbContext>(); var importer = new ImportService(db);
        await importer.Import(Source("first"), new([Posting()], true), Profile, "v1", default);
        await importer.Import(Source("second"), new([Posting("copy", "https://mirror.example/jobs/1")], true), Profile, "v1", default);
        Assert.Single(db.Jobs);
        Assert.Equal(2, db.JobOccurrences.Count());
        Assert.Equal("https://mirror.example/jobs/1", db.JobOccurrences.Single(x => x.SourceId == "second").Url);
    }
    [Fact]
    public async Task Similar_title_at_different_url_is_flagged_not_merged()
    {
        using var app = new ApiFactory(); using var client = app.OpenClient(); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JobDbContext>(); var importer = new ImportService(db);
        await importer.Import(Source(), new([Posting(), Posting("2", "https://example.com/jobs/2")], true), Profile, "v1", default);
        Assert.Equal(2, db.Jobs.Count()); Assert.Single(db.Jobs.Where(x => x.PossibleDuplicateOf != null));
    }
    [Fact]
    public async Task Incomplete_snapshot_does_not_expire_or_change_existing_jobs()
    {
        using var app = new ApiFactory(); using var client = app.OpenClient(); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JobDbContext>(); var importer = new ImportService(db);
        await importer.Import(Source(), new([Posting()], true), Profile, "v1", default);
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.Import(Source(), new([], false), Profile, "v1", default));
        Assert.True(db.Jobs.Single().IsActive); Assert.True(db.JobOccurrences.Single().Active);
    }
    [Fact]
    public void Profile_and_content_changes_invalidate_ai_cache()
    {
        var job = new Job { Title = "Software Developer", Location = "Aarhus", ContentHash = "abc" };
        RuleMatcher.Apply(job, Profile, "v1"); job.AiAssessment = "cached";
        RuleMatcher.Apply(job, Profile, "v1"); Assert.Equal("cached", job.AiAssessment);
        RuleMatcher.Apply(job, Profile, "v2"); Assert.Null(job.AiAssessment);
        job.AiAssessment = "cached"; job.ContentHash = "changed";
        RuleMatcher.Apply(job, Profile, "v2"); Assert.Null(job.AiAssessment);
    }
    [Fact]
    public void Ai_output_requires_verifiable_evidence()
    {
        var json = """{"summary":"Example","requirementsMet":["REST API"],"transferableSkills":["Java"],"gaps":["AWS"],"realism":"Mulig – afklar krav og praktiske forhold","uncertainties":[],"evidence":[{"quote":"C#","claim":"Uses C#"}]}""";
        Assert.NotNull(CodexMatcher.Validate(json, "We use C#"));
        Assert.Throws<InvalidDataException>(() => CodexMatcher.Validate(json, "We use Java"));
        Assert.Throws<InvalidDataException>(() => CodexMatcher.Validate(json.Replace("Mulig – afklar krav og praktiske forhold", "Guaranteed hire", StringComparison.Ordinal), "We use C#"));
        Assert.ThrowsAny<Exception>(() => CodexMatcher.Validate("{}", "text"));
        Assert.ThrowsAny<Exception>(() => CodexMatcher.Validate("invalid", "text"));
    }
    [Fact]
    public async Task Source_catalog_is_readable_and_seen_does_not_change_saved_status()
    {
        using var app = new ApiFactory(); using var client = app.OpenClient();
        var sources = await client.GetAsync("/api/sources"); Assert.Equal(HttpStatusCode.OK, sources.StatusCode);
        var created = await client.PostAsJsonAsync("/api/jobs", new { title = "Example", company = "Example" });
        var job = await created.Content.ReadFromJsonAsync<JsonElement>();
        var response = await client.PatchAsync($"/api/jobs/{job.GetProperty("id").GetString()}/seen", null);
        var seen = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Saved", seen.GetProperty("status").GetString()); Assert.Equal(DateTimeKind.Utc, seen.GetProperty("seenAt").GetDateTime().Kind);
    }
    [Theory]
    [InlineData("Greenhouse", "{\"jobs\":[{\"id\":1,\"title\":\"Software Developer\",\"absolute_url\":\"https://example.com/1\",\"location\":{\"name\":\"Aarhus\"},\"content\":\"&lt;p&gt;C#&lt;/p&gt;\"}],\"meta\":{\"total\":1}}")]
    [InlineData("Lever", "[{\"id\":\"1\",\"text\":\"Software Developer\",\"hostedUrl\":\"https://example.com/1\",\"categories\":{\"location\":\"Aarhus\",\"commitment\":\"Full-time\"},\"descriptionPlain\":\"C#\",\"lists\":[{\"text\":\"Required\",\"content\":\"<li>SQL</li>\"}]}]")]
    public async Task Adapters_parse_published_job_fields(string provider, string json)
    {
        using var http = new HttpClient(new FixtureHandler(json)); var source = Source() with { Provider = provider };
        var snapshot = await new JobSourceClient(http).Fetch(source, default);
        Assert.True(snapshot.Complete); var job = Assert.Single(snapshot.Jobs);
        Assert.Equal("Aarhus", job.Location); Assert.Contains("C#", job.Description); Assert.DoesNotContain("<p>", job.Description);
        if (provider == "Lever") Assert.Contains("SQL", job.Description);
    }
    private sealed class FixtureHandler(string json) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }); }
}
