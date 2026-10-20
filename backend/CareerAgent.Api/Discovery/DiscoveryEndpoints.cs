using CareerAgent.Api.Data;
using CareerAgent.Api.Matching;
using Microsoft.EntityFrameworkCore;
namespace CareerAgent.Api.Discovery;
public sealed class DiscoveryGate { public SemaphoreSlim Lock { get; } = new(1, 1); }
public static class DiscoveryEndpoints
{
    public static void MapDiscovery(this WebApplication app)
    {
        app.MapGet("/api/sources", (SourceCatalog catalog) => Results.Ok(catalog.All));
        app.MapGet("/api/discovery/runs", async (JobDbContext db, CancellationToken ct) =>
            Results.Ok(await db.DiscoveryRuns.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(10).ToListAsync(ct)));
        app.MapPost("/api/discovery/runs", async (JobDbContext db, ProfileStore profiles, DiscoveryGate gate, CancellationToken ct) =>
        {
            try { profiles.Read(); } catch (Exception e) when (e is IOException or InvalidOperationException or System.Text.Json.JsonException)
            { return Results.Problem(statusCode: 409, title: "Lokal søgeprofil mangler eller er ugyldig. Se README."); }
            await gate.Lock.WaitAsync(ct);
            try
            {
                if (await db.DiscoveryRuns.AnyAsync(x => x.State == "Running" || x.State == "Queued", ct))
                    return Results.Problem(statusCode: 409, title: "En søgning kører allerede.");
                var latest = await db.DiscoveryRuns.OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
                if (latest is not null && latest.CreatedAt > DateTime.UtcNow.AddMinutes(-5))
                    return Results.Problem(statusCode: 429, title: "Vent fem minutter mellem kildeopdateringer.");
                var run = new DiscoveryRun(); db.DiscoveryRuns.Add(run); await db.SaveChangesAsync(ct);
                return Results.Accepted("/api/discovery/runs", run);
            }
            finally { gate.Lock.Release(); }
        });
        app.MapPost("/api/matching/recalculate", async (JobDbContext db, ProfileStore profiles, CancellationToken ct) =>
        {
            try
            {
                var (profile, version) = profiles.Read();
                foreach (var job in await db.Jobs.ToListAsync(ct)) { job.ContentHash = ImportService.Hash(job); RuleMatcher.Apply(job, profile, version); }
                await db.SaveChangesAsync(ct); return Results.Ok(new { status = "ok" });
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or System.Text.Json.JsonException)
            { return Results.Problem(statusCode: 409, title: "Lokal søgeprofil mangler eller er ugyldig."); }
        });
        app.MapPatch("/api/jobs/{id:guid}/seen", async (Guid id, JobDbContext db, CancellationToken ct) =>
        {
            var job = await db.Jobs.FindAsync([id], ct);
            if (job is null) return Results.Problem(statusCode: 404, title: "Jobbet blev ikke fundet.");
            job.SeenAt ??= DateTime.UtcNow; await db.SaveChangesAsync(ct); return Results.Ok(job);
        });
        app.MapGet("/api/jobs/{id:guid}/sources", async (Guid id, JobDbContext db, CancellationToken ct) =>
            Results.Ok(await db.JobOccurrences.AsNoTracking().Where(x => x.JobId == id).ToListAsync(ct)));
    }
}
