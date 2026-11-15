namespace CareerAgent.Api.Documents;

public sealed class ReferenceDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "Cv";
    public string DisplayName { get; set; } = "";
    public string FileName { get; set; } = "";
    public string TextFileName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class GeneratedDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public string Kind { get; set; } = "Cv";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class GeneratedDocumentVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public int Number { get; set; }
    public string ContentFileName { get; set; } = "";
    public string ProfileVersion { get; set; } = "";
    public string ReferenceIdsJson { get; set; } = "[]";
    public string Engine { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed record DocumentEvidence(string Id, string Quote);
public sealed record DocumentBlock(string Type, string Text, DocumentEvidence[] Evidence)
{
    public string? Label { get; init; }
    public string? Period { get; init; }
    public string? Technologies { get; init; }
}
public sealed record DocumentSection(string Heading, DocumentBlock[] Blocks)
{
    public int? Page { get; init; }
}
public sealed record DocumentIdentity(string FullName, string Email, string Phone, string Location, string Links);
public sealed record DocumentProfile(DocumentIdentity Identity, string Experience, string Education, string Projects, string Motivation);
public sealed record SaveDocumentProfileRequest(DocumentProfile? Profile, bool Confirmed);
public sealed record DocumentContent(string Title, string? Subtitle, DocumentSection[] Sections)
{
    public DocumentIdentity? Identity { get; init; }
    public string? Language { get; init; }
}
public sealed record ReferenceDto(Guid Id, string Kind, string DisplayName, string FileType, long SizeBytes, DateTime CreatedAt);
public sealed record DocumentVersionDto(Guid Id, int Number, DateTime CreatedAt, string Engine, Guid[] ReferenceIds, DocumentContent Content);
public sealed record DocumentHistoryDto(Guid Id, Guid JobId, string Kind, DocumentVersionDto[] Versions);
public sealed record GenerateDocumentsRequest(string? Kind, Guid[]? ReferenceIds, string? Language);
public sealed record SaveDocumentRequest(DocumentContent? Content);

public sealed class DocumentGenerationGate { public SemaphoreSlim Lock { get; } = new(1, 1); }
