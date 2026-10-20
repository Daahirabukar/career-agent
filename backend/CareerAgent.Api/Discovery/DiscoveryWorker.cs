using System.Text.Json;
using CareerAgent.Api.Data;
using CareerAgent.Api.Matching;
using Microsoft.EntityFrameworkCore;
namespace CareerAgent.Api.Discovery;

// This worker only processes explicitly queued requests. It never schedules searches.
public sealed class DiscoveryWorker(IServiceScopeFactory scopes, SourceCatalog catalog, IConfiguration configuration, ILogger<DiscoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("CareerAgent:WorkerEnabled", true)) return;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JobDbContext>();
            foreach (var run in await db.DiscoveryRuns.Where(x => x.State == "Running").ToListAsync(stoppingToken))
            { run.State = "Interrupted"; run.Error = "Kørslen blev afbrudt ved genstart. Start en ny søgning."; run.FinishedAt = DateTime.UtcNow; }
            await db.SaveChangesAsync(stoppingToken);
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<JobDbContext>();
            var run = await db.DiscoveryRuns.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(x => x.State == "Queued", stoppingToken);
            if (run is null) { await Task.Delay(1000, stoppingToken); continue; }
            run.State = "Running"; await db.SaveChangesAsync(stoppingToken);
            var results = new List<SourceRunResult>();
            try
            {
                var (profile, version) = scope.ServiceProvider.GetRequiredService<ProfileStore>().Read();
                foreach (var source in catalog.All.Where(x => x.Enabled))
                {
                    try
                    {
                        using var sourceScope = scopes.CreateScope();
                        var snapshot = await sourceScope.ServiceProvider.GetRequiredService<IJobSourceClient>().Fetch(source, stoppingToken);
                        var counts = await sourceScope.ServiceProvider.GetRequiredService<ImportService>().Import(source, snapshot, profile, version, stoppingToken);
                        results.Add(new(source.Id, "Success", snapshot.Jobs.Count, counts.Added, counts.Updated, ""));
                        run.Added += counts.Added; run.Updated += counts.Updated;
                    }
                    catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogWarning("Source {SourceId} failed ({ErrorType})", source.Id, e.GetType().Name);
                        results.Add(new(source.Id, "Failed", 0, 0, 0, "Kilden kunne ikke hentes/valideres. Eksisterende opslag er bevaret."));
                    }
                    run.Details = JsonSerializer.Serialize(results, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    await db.SaveChangesAsync(stoppingToken);
                    await Task.Delay(500, stoppingToken);
                }
                run.State = results.All(x => x.State == "Success") ? "Completed" : results.Any(x => x.State == "Success") ? "Partial" : "Failed";
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Discovery failed ({ErrorType})", e.GetType().Name);
                run.State = "Failed"; run.Error = "Kontrollér lokal søgeprofil og databasens opsætning.";
            }
            run.FinishedAt = DateTime.UtcNow; await db.SaveChangesAsync(stoppingToken);
        }
    }
}
