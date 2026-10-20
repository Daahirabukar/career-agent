using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace CareerAgent.Api.Documents;

public sealed class DocumentStorage(IConfiguration configuration)
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxTextCharacters = 100_000;
    public string Root { get; } = Path.GetFullPath(configuration["CareerAgent:DocumentsPath"] ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "CareerAgent", "documents"));
    public string ReferenceDirectory => EnsureDirectory(Path.Combine(Root, "references"));
    public string GeneratedDirectory => EnsureDirectory(Path.Combine(Root, "generated"));

    public async Task<(ReferenceDocument Document, string Error)> AddReference(IFormFile file, string kind, string displayName, CancellationToken ct)
    {
        if (file.Length is <= 0 or > MaxFileBytes) return (null!, "Filen skal være mellem 1 byte og 10 MB.");
        if (kind is not ("Cv" or "Application")) return (null!, "Vælg CV eller ansøgning som referencetype.");
        displayName = displayName.Trim();
        if (displayName.Length is 0 or > 120) return (null!, "Navnet skal indeholde 1–120 tegn.");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".pdf" or ".docx")) return (null!, "Kun tekstbaserede PDF- og DOCX-filer understøttes.");

        var id = Guid.NewGuid();
        var storedName = id.ToString("N") + ext;
        var textName = id.ToString("N") + ".txt";
        var path = Path.Combine(ReferenceDirectory, storedName);
        var textPath = Path.Combine(ReferenceDirectory, textName);
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                await file.CopyToAsync(output, ct);
            RestrictFile(path);
            var text = ext == ".pdf" ? ExtractPdf(path) : ExtractDocx(path);
            text = CleanText(text);
            if (string.IsNullOrWhiteSpace(text))
                return (null!, "Dokumentet indeholder ingen udtrækkelig tekst. Scannede PDF'er understøttes ikke endnu.");
            if (text.Length > MaxTextCharacters) return (null!, "Dokumentet indeholder for meget tekst (maks. 100.000 tegn).");
            await File.WriteAllTextAsync(textPath, text, new UTF8Encoding(false), ct);
            RestrictFile(textPath);
            await using var input = File.OpenRead(path);
            var sha = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
            return (new ReferenceDocument { Id = id, Kind = kind, DisplayName = displayName, FileName = storedName,
                TextFileName = textName, Sha256 = sha, SizeBytes = file.Length }, "");
        }
        catch (OperationCanceledException)
        {
            TryDelete(path); TryDelete(textPath); throw;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            TryDelete(path); TryDelete(textPath);
            return (null!, "Filen kunne ikke læses som en gyldig PDF eller DOCX. Kontrollér filen og prøv igen.");
        }
        finally
        {
            if (!File.Exists(textPath)) TryDelete(path);
        }
    }

    public string ReadReferenceText(ReferenceDocument reference)
    {
        var path = SafePath(ReferenceDirectory, reference.TextFileName);
        return File.ReadAllText(path);
    }
    public void DeleteReference(ReferenceDocument reference)
    { TryDelete(SafePath(ReferenceDirectory, reference.FileName)); TryDelete(SafePath(ReferenceDirectory, reference.TextFileName)); }
    public string WriteVersion(Guid jobId, Guid documentId, int version, string content)
    {
        var directory = EnsureDirectory(Path.Combine(GeneratedDirectory, jobId.ToString("N"), documentId.ToString("N")));
        var filename = $"v{version}-{Guid.NewGuid():N}.json";
        var path = Path.Combine(directory, filename);
        File.WriteAllText(path, content, new UTF8Encoding(false)); RestrictFile(path);
        return filename;
    }
    public string ReadVersion(Guid jobId, Guid documentId, string fileName) => File.ReadAllText(SafePath(
        EnsureDirectory(Path.Combine(GeneratedDirectory, jobId.ToString("N"), documentId.ToString("N"))), fileName));
    public void DeleteVersion(Guid jobId, Guid documentId, string fileName) => TryDelete(SafePath(
        EnsureDirectory(Path.Combine(GeneratedDirectory, jobId.ToString("N"), documentId.ToString("N"))), fileName));

    private static string ExtractPdf(string path)
    {
        using var doc = PdfDocument.Open(path);
        if (doc.NumberOfPages > 30) throw new InvalidDataException("Too many PDF pages.");
        var output = new StringBuilder();
        foreach (var page in doc.GetPages())
        {
            output.AppendLine(ContentOrderTextExtractor.GetText(page, addDoubleNewline: true));
            if (output.Length > MaxTextCharacters) break;
        }
        return output.ToString();
    }
    private static string ExtractDocx(string path)
    {
        using (var zip = ZipFile.OpenRead(path))
        {
            if (zip.Entries.Count > 1000 || zip.Entries.Sum(x => x.Length) > 30 * 1024 * 1024)
                throw new InvalidDataException("DOCX package exceeds safe limits.");
            if (!zip.Entries.Any(x => x.FullName == "[Content_Types].xml") || !zip.Entries.Any(x => x.FullName == "word/document.xml"))
                throw new InvalidDataException("Not a DOCX document.");
        }
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document?.Body ?? throw new InvalidDataException("Missing Word body.");
        return string.Join("\n", body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
            .Select(p => string.Concat(p.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text))));
    }
    private static string CleanText(string text) => Regex.Replace(text.Replace('\0', ' '), @"[\t\r ]+", " ").Trim();
    private string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(Root);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
    private static void RestrictFile(string path)
    { if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    private static string SafePath(string directory, string filename)
    {
        if (Path.GetFileName(filename) != filename) throw new InvalidDataException("Invalid local document key.");
        var path = Path.GetFullPath(Path.Combine(directory, filename));
        if (!path.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("Invalid local document path.");
        return path;
    }
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
}
