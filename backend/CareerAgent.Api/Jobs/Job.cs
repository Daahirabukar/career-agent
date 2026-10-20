namespace CareerAgent.Api.Jobs;

public enum JobStatus { Saved, Applied, Interview, Offer, Rejected, New, Dismissed }
public sealed class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string Location { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public JobStatus Status { get; set; } = JobStatus.Saved;
    public string Description { get; set; } = "";
    public string EmploymentType { get; set; } = "";
    public string WorkplaceType { get; set; } = "";
    public string Country { get; set; } = "";
    public string CanonicalUrl { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string? PossibleDuplicateOf { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsImported { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? Deadline { get; set; }
    public DateTime? StatusVerifiedAt { get; set; }
    public DateTime? SeenAt { get; set; }
    public int? Score { get; set; }
    public string Eligibility { get; set; } = "Unscored";
    public string MatchReason { get; set; } = "";
    public string MatchDetails { get; set; } = "{}";
    public string ProfileVersion { get; set; } = "";
    public string AssessmentKey { get; set; } = "";
    public DateTime? ScoredAt { get; set; }
    public string? AiAssessment { get; set; }
    public string? AiModel { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public sealed record CreateJobRequest(string? Title, string? Company, string? Location, string? SourceUrl);
public sealed record UpdateStatusRequest(string? Status);
