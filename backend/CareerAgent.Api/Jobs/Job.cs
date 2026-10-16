namespace CareerAgent.Api.Jobs;

public enum JobStatus { Saved, Applied, Interview, Offer, Rejected }
public sealed class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string Location { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public JobStatus Status { get; set; } = JobStatus.Saved;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public sealed record CreateJobRequest(string? Title, string? Company, string? Location, string? SourceUrl);
public sealed record UpdateStatusRequest(string? Status);
