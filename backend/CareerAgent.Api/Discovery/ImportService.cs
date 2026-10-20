using System.Security.Cryptography;
using System.Text;
using CareerAgent.Api.Data;
using CareerAgent.Api.Jobs;
using CareerAgent.Api.Matching;
using Microsoft.EntityFrameworkCore;
namespace CareerAgent.Api.Discovery;

public sealed class ImportService(JobDbContext db)
{
    public static string Canonical(string url)
    {
        var uri = new UriBuilder(url) { Fragment = "" };
        var parameters = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("gh_src=", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("source=", StringComparison.OrdinalIgnoreCase));
        uri.Query = string.Join("&", parameters.OrderBy(p => p, StringComparer.Ordinal));
        return uri.Uri.AbsoluteUri.TrimEnd('/');
    }
    public static string Hash(Job job) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", job.Title, job.Company, job.Location, job.Description, job.EmploymentType, job.WorkplaceType, job.Country, job.Deadline?.ToString("O"), job.IsActive))));
    public async Task<(int Added, int Updated)> Import(SourceDefinition source, SourceSnapshot snapshot, SearchProfile profile, string version, CancellationToken ct)
    {
        if (!snapshot.Complete) throw new InvalidDataException("Ufuldstændigt kildesvar; ingen ændringer blev gemt.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var jobs = await db.Jobs.ToListAsync(ct);
        var occurrences = await db.JobOccurrences.ToListAsync(ct);
        var seen = new HashSet<string>(); int added = 0, updated = 0;
        foreach (var input in snapshot.Jobs)
        {
            seen.Add(input.ExternalId);
            var occurrence = occurrences.SingleOrDefault(x => x.SourceId == source.Id && x.ExternalId == input.ExternalId);
            var canonical = Canonical(input.Url);
            var incoming = new Job
            {
                Title = input.Title, Company = input.Company, Location = input.Location, Description = input.Description,
                EmploymentType = input.EmploymentType, WorkplaceType = input.WorkplaceType, Country = input.Country,
                Deadline = input.Deadline, IsActive = true
            };
            var sourceJobIds = occurrences.Where(x => x.SourceId != source.Id).Select(x => x.JobId).ToHashSet();
            var job = occurrence is null
                ? jobs.FirstOrDefault(x => x.CanonicalUrl == canonical || x.SourceUrl == input.Url)
                    ?? jobs.FirstOrDefault(x => sourceJobIds.Contains(x.Id) && x.ContentHash == Hash(incoming))
                : jobs.Single(x => x.Id == occurrence.JobId);
            if (job is null)
            {
                job = new Job { Status = JobStatus.New, IsImported = true };
                var possible = jobs.FirstOrDefault(x => x.Title.Equals(input.Title, StringComparison.OrdinalIgnoreCase) && x.Company.Equals(input.Company, StringComparison.OrdinalIgnoreCase) && x.Location.Equals(input.Location, StringComparison.OrdinalIgnoreCase));
                job.PossibleDuplicateOf = possible?.Id.ToString();
                db.Jobs.Add(job); jobs.Add(job); added++;
            }
            else updated++;
            if (occurrence is null)
            {
                occurrence = new JobOccurrence { JobId = job.Id, SourceId = source.Id, ExternalId = input.ExternalId };
                db.JobOccurrences.Add(occurrence); occurrences.Add(occurrence);
            }
            occurrence.Active = true; occurrence.LastSeenAt = now; occurrence.Url = input.Url;
            job.Title = input.Title; job.Company = input.Company; job.Location = input.Location;
            job.SourceUrl = input.Url; job.CanonicalUrl = canonical; job.Description = input.Description;
            job.EmploymentType = input.EmploymentType; job.WorkplaceType = input.WorkplaceType; job.Country = input.Country;
            job.PublishedAt = input.PublishedAt; job.Deadline = input.Deadline; job.IsActive = true;
            job.StatusVerifiedAt = now;
            job.ContentHash = Hash(job); RuleMatcher.Apply(job, profile, version);
        }
        foreach (var occurrence in occurrences.Where(x => x.SourceId == source.Id && !seen.Contains(x.ExternalId))) occurrence.Active = false;
        foreach (var job in jobs.Where(x => x.IsImported))
        {
            job.IsActive = occurrences.Any(x => x.JobId == job.Id && x.Active);
            job.StatusVerifiedAt = job.IsActive
                ? occurrences.Where(x => x.JobId == job.Id && x.Active).Max(x => (DateTime?)x.LastSeenAt)
                : now;
            job.ContentHash = Hash(job); RuleMatcher.Apply(job, profile, version);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return (added, updated);
    }
}
