using CareerAgent.Api.Data;
using CareerAgent.Api.Discovery;
using CareerAgent.Api.Matching;
using Microsoft.EntityFrameworkCore;
namespace CareerAgent.Api.Jobs;

public static class JobEndpoints
{
    public static void MapJobEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/jobs");
        group.MapGet("", async (JobDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Jobs.AsNoTracking().OrderByDescending(j => j.Score).ThenByDescending(j => j.IsActive)
                .ThenByDescending(j => j.StatusVerifiedAt).ThenByDescending(j => j.CreatedAt).ToListAsync(ct)));
        group.MapPost("", async (CreateJobRequest request, JobDbContext db, ProfileStore profiles, CancellationToken ct) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var job = new Job { Title = request.Title!.Trim(), Company = request.Company!.Trim(),
                Location = request.Location?.Trim() ?? "", SourceUrl = request.SourceUrl?.Trim() ?? "",
                CanonicalUrl = string.IsNullOrWhiteSpace(request.SourceUrl) ? "" : ImportService.Canonical(request.SourceUrl.Trim()) };
            try
            {
                var (profile, version) = profiles.Read();
                job.ContentHash = ImportService.Hash(job);
                RuleMatcher.Apply(job, profile, version);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or System.Text.Json.JsonException)
            {
                // Keep manual capture available if the private profile is temporarily unavailable.
            }
            db.Jobs.Add(job);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/jobs/{job.Id}", job);
        });
        group.MapPatch("/{id:guid}/status", async (Guid id, UpdateStatusRequest request, JobDbContext db, CancellationToken ct) =>
        {
            // Match named values exactly: numeric enum values are not accepted.
            if (!Enum.GetNames<JobStatus>().Contains(request.Status))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Vælg en gyldig status."] });
            var job = await db.Jobs.FindAsync([id], ct);
            if (job is null) return Results.Problem(statusCode: 404, title: "Jobbet blev ikke fundet.");
            job.Status = Enum.Parse<JobStatus>(request.Status!);
            await db.SaveChangesAsync(ct);
            return Results.Ok(job);
        });
    }
    private static Dictionary<string, string[]> Validate(CreateJobRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200)
            errors["title"] = ["Titel skal indeholde 1–200 tegn."];
        if (string.IsNullOrWhiteSpace(request.Company) || request.Company.Trim().Length > 200)
            errors["company"] = ["Virksomhed skal indeholde 1–200 tegn."];
        if (request.Location?.Trim().Length > 200) errors["location"] = ["Sted må højst indeholde 200 tegn."];
        if (!string.IsNullOrWhiteSpace(request.SourceUrl) &&
            (request.SourceUrl.Trim().Length > 2048 || !Uri.TryCreate(request.SourceUrl.Trim(), UriKind.Absolute, out var uri)
             || (uri.Scheme != "https" && uri.Scheme != "http") || string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length > 0))
            errors["sourceUrl"] = ["Angiv en gyldig HTTP- eller HTTPS-adresse uden loginoplysninger."];
        return errors;
    }
}
