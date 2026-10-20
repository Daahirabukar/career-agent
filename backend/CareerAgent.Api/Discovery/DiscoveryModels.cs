namespace CareerAgent.Api.Discovery;
public sealed class JobOccurrence
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public string SourceId { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime LastSeenAt { get; set; }
    public bool Active { get; set; } = true;
}
public sealed class DiscoveryRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string State { get; set; } = "Queued";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public string Details { get; set; } = "[]";
    public string Error { get; set; } = "";
}
public sealed record SourceRunResult(string SourceId, string State, int Fetched, int Added, int Updated, string Message);
