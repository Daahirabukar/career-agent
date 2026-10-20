using System.Text.Json;
namespace CareerAgent.Api.Discovery;
public sealed record SourceDefinition(string Id, string Company, string Provider, string Board, string Region, bool Enabled, string CareerUrl, string Evidence, string Note)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public Uri Endpoint => Provider switch
    {
        "Greenhouse" => new($"https://boards-api.greenhouse.io/v1/boards/{Uri.EscapeDataString(Board)}/jobs?content=true"),
        "Lever" => new($"https://{(Region == "EU" ? "api.eu.lever.co" : "api.lever.co")}/v0/postings/{Uri.EscapeDataString(Board)}?mode=json&skip=0&limit=1000"),
        _ => throw new InvalidOperationException("Kilden har ingen understøttet integration.")
    };
}
public sealed class SourceCatalog(IWebHostEnvironment environment)
{
    public IReadOnlyList<SourceDefinition> All { get; } = JsonSerializer.Deserialize<SourceDefinition[]>(
        File.ReadAllText(Path.Combine(environment.ContentRootPath, "Discovery", "sources.json")),
        new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
}
public sealed record ImportedJob(string ExternalId, string Title, string Company, string Location, string Url,
    string Description, string EmploymentType, string WorkplaceType, string Country, DateTime? PublishedAt, DateTime? Deadline);
public sealed record SourceSnapshot(IReadOnlyList<ImportedJob> Jobs, bool Complete);
public interface IJobSourceClient { Task<SourceSnapshot> Fetch(SourceDefinition source, CancellationToken ct); }
