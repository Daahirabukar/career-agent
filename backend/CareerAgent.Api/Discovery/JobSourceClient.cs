using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
namespace CareerAgent.Api.Discovery;

public sealed class JobSourceClient(HttpClient client) : IJobSourceClient
{
    public async Task<SourceSnapshot> Fetch(SourceDefinition source, CancellationToken ct)
    {
        using var response = await client.GetAsync(source.Endpoint, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        // Bound download size; redirects are disabled by the registered handler.
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192]; int length;
        while ((length = await stream.ReadAsync(bytes, ct)) > 0)
        {
            if (buffer.Length + length > 12_000_000) throw new InvalidDataException("Kildesvar er for stort.");
            buffer.Write(bytes, 0, length);
        }
        if (source.Provider == "Teamtailor") return ParseTeamtailor(buffer.ToArray(), source);
        using var doc = JsonDocument.Parse(buffer.ToArray());
        var root = doc.RootElement;
        var rows = source.Provider == "Greenhouse" ? root.GetProperty("jobs") : root;
        var jobs = new List<ImportedJob>();
        foreach (var row in rows.EnumerateArray())
        {
            string Get(string key) => Text(row, key);
            var gh = source.Provider == "Greenhouse";
            var id = Get("id"); var title = Get(gh ? "title" : "text");
            var url = Get(gh ? "absolute_url" : "hostedUrl");
            if (id.Length == 0 || title.Length == 0 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0)
                throw new InvalidDataException("Kilden indeholder ugyldige jobfelter; fuld import afvist.");
            var location = gh ? Text(row.GetProperty("location"), "name") : Text(row.GetProperty("categories"), "location");
            var description = gh ? Plain(Get("content")) : string.Join("\n", new[] { Get("descriptionPlain"), Get("openingPlain"), Get("descriptionBodyPlain"), Get("additionalPlain") });
            if (!gh && row.TryGetProperty("lists", out var lists))
                description += "\n" + string.Join("\n", lists.EnumerateArray().Select(x => Text(x, "text") + "\n" + Plain(Text(x, "content"))));
            if (description.Length > 100_000 || title.Length > 200 || location.Length > 200 || url.Length > 2048)
                throw new InvalidDataException("Kilden indeholder for store jobfelter.");
            DateTime? published = gh ? Date(Get("first_published")) : long.TryParse(Get("createdAt"), out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null;
            jobs.Add(new(id, title, source.Company, location, url, description,
                gh ? "" : Text(row.GetProperty("categories"), "commitment"), Get("workplaceType"), Get("country"), published, Date(Get("application_deadline"))));
        }
        if (jobs.Select(x => x.ExternalId).Distinct().Count() != jobs.Count) throw new InvalidDataException("Gentagne kilde-ID’er i svaret.");
        var total = source.Provider == "Greenhouse" && root.TryGetProperty("meta", out var meta) && meta.TryGetProperty("total", out var t) ? t.GetInt32() : jobs.Count;
        return new(jobs, total == jobs.Count && (source.Provider != "Lever" || jobs.Count < 1000));
    }
    private static SourceSnapshot ParseTeamtailor(byte[] bytes, SourceDefinition source)
    {
        using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 12_000_000 });
        var document = XDocument.Load(reader);
        var channel = document.Root?.Name == "rss" ? document.Root.Element("channel") : null;
        if (channel is null) throw new InvalidDataException("Ugyldigt RSS-jobfeed.");
        XNamespace tt = "https://teamtailor.com/locations";
        var jobs = channel.Elements("item").Select(item =>
        {
            string Get(string key) => item.Element(key)?.Value.Trim() ?? "";
            var id = Get("guid"); var title = Get("title"); var url = Get("link");
            var locations = item.Descendants(tt + "location").ToArray();
            var place = string.Join("; ", locations.Select(l => string.Join(", ",
                new[] { l.Element(tt + "city")?.Value, l.Element(tt + "country")?.Value }.Where(v => !string.IsNullOrWhiteSpace(v)))).Distinct());
            var description = Plain(Get("description"));
            if (id.Length == 0 || title.Length == 0 || title.Length > 200 || place.Length > 200 || description.Length > 100_000 ||
                url.Length > 2048 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0)
                throw new InvalidDataException("Ugyldige felter i RSS-jobfeed; fuld import afvist.");
            var remote = Get("remoteStatus") switch { "fully" or "temporary" => "Remote", "hybrid" => "Hybrid", _ => "Onsite" };
            var employment = Regex.IsMatch(title, @"\b(fuldtid|full.time)\b", RegexOptions.IgnoreCase) ? "Full-time" :
                Regex.IsMatch(title, @"\b(deltid|part.time)\b", RegexOptions.IgnoreCase) ? "Part-time" : "";
            return new ImportedJob(id, title, source.Company, place, url, description, employment, remote,
                locations.Select(l => l.Element(tt + "country")?.Value).FirstOrDefault() ?? "", Date(Get("pubDate")), null);
        }).ToArray();
        if (jobs.Select(j => j.ExternalId).Distinct().Count() != jobs.Length) throw new InvalidDataException("Gentagne kilde-ID’er.");
        // RSS may be capped at 100 items. Never expire existing jobs from a potentially truncated feed.
        return new(jobs, jobs.Length < 100 && !channel.Elements().Any(e => e.Name.LocalName == "link" && (string?)e.Attribute("rel") == "next"));
    }
    private static string Text(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "";
    private static DateTime? Date(string text) => DateTimeOffset.TryParse(text, out var value) ? value.UtcDateTime : null;
    public static string Plain(string text)
    {
        var decoded = WebUtility.HtmlDecode(text);
        decoded = Regex.Replace(decoded, @"<(script|style)\b[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return Regex.Replace(Regex.Replace(decoded, "<[^>]+>", " ", RegexOptions.None, TimeSpan.FromSeconds(1)), @"\s+", " ").Trim();
    }
}
