using System.Text.Json;
using CareerAgent.Api.Data;
using CareerAgent.Api.Discovery;
namespace CareerAgent.Api.Matching;
public static class MatchingEndpoints
{
    public static void MapMatching(this WebApplication app)
    {
        app.MapPost("/api/jobs/{id:guid}/analysis", async (Guid id, JobDbContext db, ProfileStore profiles, ICodexMatcher matcher, AnalysisGate gate, CancellationToken ct) =>
        {
            if (!await gate.Lock.WaitAsync(0, ct)) return Results.Problem(statusCode: 409, title: "En Codex-vurdering kører allerede.");
            try
            {
                var job = await db.Jobs.FindAsync([id], ct);
                if (job is null) return Results.Problem(statusCode: 404, title: "Jobbet blev ikke fundet.");
                var (profile, version) = profiles.Read();
                job.ContentHash = ImportService.Hash(job); RuleMatcher.Apply(job, profile, version);
                if (job.AiAssessment is not null && job.AiModel == CodexMatcher.Version) return Results.Ok(job);
                if (job.Description.Length < 80 || job.Eligibility == "Excluded")
                    return Results.Problem(statusCode: 409, title: "Vurderingen kræver et relevant job med tilstrækkelig jobtekst.");
                var expectedKey = job.AssessmentKey;
                var assessment = await matcher.Analyze(job, profile, ct);
                // Do not overwrite user changes or a newer source/profile version while Codex ran.
                await db.Entry(job).ReloadAsync(ct);
                var (_, currentVersion) = profiles.Read();
                if (ImportService.Hash(job) + ":" + currentVersion + ":" + RuleMatcher.Version != expectedKey)
                    return Results.Problem(statusCode: 409, title: "Job eller profil blev ændret. Start vurderingen igen.");
                RuleMatcher.Apply(job, profile, version);
                job.AiAssessment = JsonSerializer.Serialize(assessment, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                job.AiModel = CodexMatcher.Version;
                await db.SaveChangesAsync(ct); return Results.Ok(job);
            }
            catch (CodexExecutionException e)
            { return Results.Problem(statusCode: 503, title: e.Message, extensions: new Dictionary<string, object?> { ["code"] = e.Code }); }
            catch (Exception e) when (!ct.IsCancellationRequested && e is not OutOfMemoryException)
            { return Results.Problem(statusCode: 503, title: "Codex-vurderingen kunne ikke gennemføres. Kontrollér profil, ChatGPT-login og kvote. Lokal score er bevaret."); }
            finally { gate.Lock.Release(); }
        });
    }
}
