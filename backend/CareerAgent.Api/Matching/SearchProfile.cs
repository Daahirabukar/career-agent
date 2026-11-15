using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace CareerAgent.Api.Matching;

public sealed record ProfileExperience(string Employer, string Type, string[] Skills, string Summary);
public sealed record SearchProfile(string Summary, string[] Skills, string[] RoleTerms, string[] PreferredCities, string[] EastJutlandCities, string[] JutlandCities)
{
    public string[] DocumentFacts { get; init; } = [];
    public ProfileExperience[] Experience { get; init; } = [];
}
public sealed class ProfileStore(IConfiguration configuration)
{
    public string PathName => configuration["CareerAgent:ProfilePath"] ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "CareerAgent", "profile.json");
    public (SearchProfile Profile, string Version) Read()
    {
        if (!File.Exists(PathName)) throw new InvalidOperationException("Lokal søgeprofil mangler. Se README for opsætning.");
        var text = File.ReadAllText(PathName);
        var profile = JsonSerializer.Deserialize<SearchProfile>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Søgeprofilen er ugyldig.");
        if (string.IsNullOrWhiteSpace(profile.Summary) || profile.Skills is null || profile.RoleTerms is null || profile.PreferredCities is null || profile.EastJutlandCities is null || profile.JutlandCities is null)
            throw new InvalidOperationException("Søgeprofilen mangler obligatoriske felter.");
        return (profile, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16]);
    }
}
