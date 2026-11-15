using System.Text.Json;

namespace CareerAgent.Api.Documents;

// Candidate-confirmed facts, stored outside the repository. References never become facts automatically.
public sealed class DocumentProfileStore(DocumentStorage storage)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static DocumentProfile Empty => new(new("", "", "", "", ""), "", "", "", "");
    private string FileName => Path.Combine(storage.Root, "document-profile.json");
    public DocumentProfile Read() => File.Exists(FileName)
        ? JsonSerializer.Deserialize<DocumentProfile>(File.ReadAllText(FileName), Options) ?? Empty : Empty;
    public void Save(DocumentProfile profile)
    {
        // Ensure owner-only directory using the same storage policy as reference files.
        _ = storage.ReferenceDirectory;
        var temporary = Path.Combine(storage.Root, $"document-profile-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(profile, Options));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, FileName, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static bool Valid(DocumentProfile? p) => p?.Identity is { } id &&
        new[] { id.FullName, id.Email, id.Phone, id.Location }.All(s => s is not null && s.Length <= 200 && !s.Contains('\n') && !s.Contains('\r')) &&
        id.Links is not null && id.Links.Length <= 1000 &&
        new[] { p.Experience, p.Education, p.Projects, p.Motivation }.All(s => s is not null && s.Length <= 4000);
}
