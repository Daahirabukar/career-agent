using System.Text.Json;
using System.Text.RegularExpressions;
using CareerAgent.Api.Jobs;

namespace CareerAgent.Api.Matching;

public sealed record MatchResult(
    int Score, string Eligibility, string Realism, string Reason,
    string[] Uncertainties, string[] MatchedSkills, string[] TransferableSkills, string[] MissingSkills,
    string[] MetRequirements, int Skills, int Experience, int Role, int Geography, int Development);

public static class RuleMatcher
{
    public const string Version = "weighted-rules-3";

    // The weights sum to 100 and are kept explicit for auditability.
    public const int SkillsWeight = 30;
    public const int ExperienceWeight = 25;
    public const int JobTypeWeight = 20;
    public const int GeographyWeight = 15;
    public const int DevelopmentWeight = 10;

    private sealed record SkillFamily(string Name, string[] JobTerms, string[] ProfileTerms, string[] TransferableProfileTerms);

    private static readonly SkillFamily[] SkillFamilies =
    [
        new("Backend-sprog", ["c#", "csharp", "c++", ".net", "java", "python", "golang", "go language", "ruby", "php", "kotlin"], ["c#", "csharp", "c++", ".net", "java", "python"], ["rest", "api", "backend"]),
        new("Backend-framework", ["asp.net", "spring boot", "django", "fastapi", "flask", "express.js", "nestjs", "laravel"], ["asp.net", ".net"], ["api", "backend", "typescript"]),
        new("API'er og integration", ["rest api", "restful", "web api", "graphql", "api integration", "microservices", "microservices"], ["rest", "api"], [".net", "c#", "asp.net"]),
        new("Databaser", ["sql", "postgresql", "postgres", "mysql", "sql server", "mongodb", "database", "databases"], ["sql"], ["api", ".net", "c#"]),
        new("Frontend", ["react", "angular", "vue", "svelte", "typescript", "javascript", "next.js"], ["react", "angular", "typescript", "next.js"], ["c#", "api"]),
        new("Cloud og drift", ["aws", "azure", "gcp", "kubernetes", "docker", "terraform", "ci/cd", "devops"], [], []),
        new("Cybersikkerhed", ["cybersecurity", "cyber security", "information security", "application security", "penetration testing", "threat modeling"], [], ["api", ".net", "c#"]),
        new("Test og kvalitet", ["automated testing", "unit testing", "integration testing", "test automation", "quality assurance"], [], ["c#", ".net", "react", "typescript"])
    ];

    private static bool Has(string text, params string[] terms) => terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
    private static bool Skill(string text, string term) => Regex.IsMatch(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(term) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    private static bool Mentions(string text, string term) => term.Contains(' ') || term.Contains('.') || term.Contains('#') || term.Contains('/')
        ? text.Contains(term, StringComparison.OrdinalIgnoreCase)
        : Skill(text, term);

    public static MatchResult Evaluate(Job job, SearchProfile profile, DateTime now)
    {
        var title = job.Title.ToLowerInvariant();
        var text = (job.Title + " " + job.Description).ToLowerInvariant();
        var place = job.Location.ToLowerInvariant();
        var uncertainties = new List<string>();
        var exclusions = new List<string>();

        if (Has(title, "internship", "intern ", "praktik", "student", "studiejob", "working student") ||
            Has(job.EmploymentType, "intern", "part-time", "part time", "deltid", "contract", "freelance"))
            exclusions.Add("Praktik, studiejob, deltid eller kontraktarbejde passer ikke til fuldtidsønsket.");

        var requiredYears = RequiredYears(job.Description);
        if (requiredYears >= 5 && IsMandatory(job.Description, requiredYears))
            exclusions.Add($"Opslaget angiver et ufravigeligt krav om mindst {requiredYears} års erfaring.");

        if ((job.Deadline is not null && job.Deadline < now) || !job.IsActive)
            exclusions.Add("Opslaget er udløbet eller ikke længere aktivt.");

        var jobType = JobTypePoints(title, profile);
        if (jobType == 0) exclusions.Add("Stillingen er uden for de valgte udviklings- og IT-områder.");

        var local = profile.PreferredCities.Any(c => Has(place, c));
        var east = profile.EastJutlandCities.Any(c => Has(place, c));
        var jutland = profile.JutlandCities.Any(c => Has(place, c));
        var danish = local || east || jutland || Has(place, "denmark", "danmark", "copenhagen", "københavn", "odense", "roskilde", "lyngby", "ballerup") || job.Country.Equals("DK", StringComparison.OrdinalIgnoreCase);
        var remote = Has(job.WorkplaceType, "remote", "hybrid") || Has(place, "remote");
        var remoteDk = remote && (danish || Has(place, "europe", "european union", "emea", "worldwide"));
        if (!danish && !remoteDk && place.Length > 0) exclusions.Add("Arbejdsstedet er ikke identificeret som Danmark eller relevant remote.");
        if (remoteDk && !danish) uncertainties.Add("Remote: ansættelse fra Danmark skal bekræftes.");
        if (place.Length == 0) uncertainties.Add("Arbejdssted er ikke oplyst.");
        if (job.StatusVerifiedAt is null)
            uncertainties.Add("Opslagets aktuelle status kan ikke verificeres fra kildedata.");
        else if (now - job.StatusVerifiedAt.Value > TimeSpan.FromDays(14))
            uncertainties.Add($"Opslagets status blev sidst kontrolleret {job.StatusVerifiedAt.Value:yyyy-MM-dd}; kontrollér at det stadig er åbent.");
        if (job.PublishedAt is null) uncertainties.Add("Offentliggørelsesdatoen er ikke oplyst.");
        if (job.Deadline is null) uncertainties.Add("Ansøgningsfrist er ikke oplyst.");
        var geography = local ? 15 : east ? 13 : jutland ? 11 : danish ? 8 : remoteDk ? 7 : 0;
        if (Has(job.WorkplaceType, "hybrid") && geography > 0) geography = Math.Min(15, geography + 1);

        var experience = ExperiencePoints(title, job.Description, requiredYears, profile, uncertainties);
        var (skills, matched, transferable, missing, met) = SkillAssessment(text, profile);
        var development = DevelopmentPoints(job.Description, uncertainties);

        if (!Has(job.EmploymentType + " " + job.Description, "full-time", "full time", "fulltime", "fuldtid", "37 timer", "37 hours"))
            uncertainties.Add("Fuldtid/ugentligt timetal er ikke bekræftet.");
        if (!Has(job.Description, "danish", "dansk", "english", "engelsk"))
            uncertainties.Add("Dansk- eller engelsksproget arbejdsmiljø er ikke bekræftet.");

        var eligibility = exclusions.Count > 0 ? "Excluded" : "Review";
        var score = exclusions.Count > 0 ? 0 : Math.Clamp(skills + experience + jobType + geography + development, 0, 100);
        var senioritySignal = Has(title, "senior", "staff", "principal", "lead", "manager", "head of");
        var realism = Realism(exclusions, requiredYears, experience, jobType, uncertainties, senioritySignal);
        var reason = exclusions.Count > 0
            ? string.Join(" ", exclusions)
            : $"Kompetencer {skills}/30, erfaring {experience}/25, jobtype {jobType}/20, geografi {geography}/15 og udviklingsmuligheder {development}/10. " +
              (matched.Length > 0 ? $"Direkte match: {string.Join(", ", matched)}. " : "") +
              (transferable.Length > 0 ? $"Overførbart: {string.Join(", ", transferable)}. " : "") +
              (missing.Length > 0 ? $"Mulige kompetencegab: {string.Join(", ", missing)}." : "Ingen tydelige teknologigab fundet i de registrerede kompetencer.");

        return new(score, eligibility, realism, reason, uncertainties.Distinct().ToArray(), matched, transferable, missing, met,
            skills, experience, jobType, geography, development);
    }

    public static void Apply(Job job, SearchProfile profile, string profileVersion)
    {
        var result = Evaluate(job, profile, DateTime.UtcNow);
        var key = job.ContentHash + ":" + profileVersion + ":" + Version;
        if (job.AssessmentKey != key) { job.AiAssessment = null; job.AiModel = null; }
        job.AssessmentKey = key; job.ProfileVersion = profileVersion; job.Score = result.Score;
        job.Eligibility = result.Eligibility; job.MatchReason = result.Reason;
        job.MatchDetails = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        job.ScoredAt = DateTime.UtcNow;
    }

    private static int JobTypePoints(string title, SearchProfile profile)
    {
        if (profile.RoleTerms.Any(t => !string.IsNullOrWhiteSpace(t) && Mentions(title, t)) ||
            Regex.IsMatch(title, @"software|udvikler|programmør|programmer|back.?end|front.?end|full.?stack|\.net|c#", RegexOptions.IgnoreCase)) return 20;
        return 0;
    }

    private static int ExperiencePoints(string title, string description, int years, SearchProfile profile, List<string> uncertainties)
    {
        if (Has(title, "graduate", "junior", "entry level", "entry-level", "new grad", "nyuddannet")) return 25;
        var seniorTitle = Has(title, "senior", "staff", "principal", "lead", "manager", "head of");
        if (seniorTitle)
            uncertainties.Add("Titlen signalerer højere senioritet; vurderingen bygger også på de konkrete krav.");

        if (years is 2 or 3)
        {
            uncertainties.Add($"Opslaget nævner {years} års erfaring; kravet er vurderet som et signal, ikke automatisk frasorteret.");
            return seniorTitle ? (years == 2 ? 13 : 10) : (years == 2 ? 20 : 17);
        }
        if (years >= 4)
        {
            uncertainties.Add($"Opslaget nævner {years} års erfaring; kontrollér om kravet reelt er ufravigeligt.");
            return seniorTitle ? 7 : 10;
        }
        if (seniorTitle) return 8;
        if (profile.Experience?.Any(x => x.Type.Equals("Professional", StringComparison.OrdinalIgnoreCase)) == true ||
            Has(profile.Summary, "professionel backend-erfaring", "professional experience", "professionel erhvervserfaring"))
            return 22; // The profile includes paid, relevant experience; no duration is inferred.
        if (Has(description, "experience preferred", "experience is a plus", "erfaring er en fordel")) return 18;
        uncertainties.Add("Jobopslaget bekræfter ikke, om niveauet passer til en nyuddannet; relevant erhvervserfaring indgår i vurderingen.");
        return 19;
    }

    private static (int Score, string[] Matched, string[] Transferable, string[] Missing, string[] Met) SkillAssessment(string text, SearchProfile profile)
    {
        var matched = new List<string>();
        var transferable = new List<string>();
        var missing = new List<string>();
        var met = new List<string>();
        var applicable = new List<SkillFamily>();
        var profileTerms = profile.Skills.Select(x => x.ToLowerInvariant()).ToArray();
        foreach (var family in SkillFamilies)
        {
            if (!family.JobTerms.Any(t => Mentions(text, t))) continue;
            applicable.Add(family);
            var directTerm = family.JobTerms.FirstOrDefault(term => profileTerms.Contains(term) && Mentions(text, term));
            if (directTerm is null && family.Name == "API'er og integration" && profileTerms.Contains("rest") && profileTerms.Contains("api"))
                directTerm = new[] { "rest api", "restful", "web api" }.FirstOrDefault(term => Mentions(text, term));
            if (directTerm is not null)
            {
                matched.Add(Display(directTerm));
                met.Add($"{family.Name}: profilen dokumenterer relevant erfaring med {Display(directTerm)}.");
            }
            else if (family.TransferableProfileTerms.Any(t => profileTerms.Contains(t)) || family.ProfileTerms.Any(t => profileTerms.Contains(t)))
            {
                var found = family.JobTerms.FirstOrDefault(t => Mentions(text, t));
                transferable.Add(found is null ? family.Name : $"{Display(found)} (overførbart fra {family.Name})");
            }
            else
            {
                missing.Add(family.Name);
            }
        }

        // The skill component measures evidence coverage, giving direct matches full credit and
        // adjacent experience partial credit. Unknown job texts stay neutral rather than penalized.
        var raw = applicable.Count == 0 ? 15 :
            (matched.Count + transferable.Count * 0.65) / applicable.Count * SkillsWeight;
        var score = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        var asksExperience = Has(text, "experience", "years", "erfaring", "erhvervserfaring");
        if (asksExperience && profile.Experience?.Any(x => x.Type.Equals("Professional", StringComparison.OrdinalIgnoreCase)) == true)
            met.Add("Profilen dokumenterer professionel erfaring (varighed i år er ikke verificeret).");
        return (score, matched.Distinct().ToArray(), transferable.Distinct().ToArray(), missing.Distinct().ToArray(), met.Distinct().ToArray());
    }

    private static int DevelopmentPoints(string description, List<string> uncertainties)
    {
        var signals = new[]
        {
            "mentorship", "mentor", "mentoring", "training", "onboarding", "professional development",
            "career development", "learning opportunities", "graduate programme", "graduate program",
            "early careers", "early career", "learn and grow", "uddannelse", "oplæring", "mentorordning", "graduateforløb"
        };
        var found = signals.Count(term => description.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (found == 0) uncertainties.Add("Muligheder for oplæring, mentorstøtte eller faglig udvikling er ikke beskrevet.");
        return found == 0 ? 3 : found == 1 ? 7 : 10;
    }

    private static string Realism(List<string> exclusions, int years, int experience, int role, List<string> uncertainties, bool senioritySignal)
    {
        if (exclusions.Count > 0) return "Ikke realistisk ud fra de oplyste krav";
        if (years >= 4 || senioritySignal || (experience < 15) || role < 15) return "Mulig, men med væsentlige forbehold";
        if (years is 2 or 3 || uncertainties.Any(x => x.Contains("ikke bekræftet", StringComparison.OrdinalIgnoreCase)))
            return "Mulig – afklar krav og praktiske forhold";
        return "Realistisk at søge";
    }

    private static string Display(string term) => term switch
    {
        "rest api" or "restful" or "web api" => "REST API'er",
        "asp.net" => "ASP.NET",
        "spring boot" => "Spring Boot",
        "sql server" => "SQL Server",
        "postgresql" or "postgres" => "PostgreSQL",
        "next.js" => "Next.js",
        "c#" or "csharp" => "C#",
        _ => term
    };

    private static int RequiredYears(string text)
    {
        var match = Regex.Match(text, @"\b(?<years>[1-9]\d?)\s*\+?\s*(?:years?|yrs?|år)\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return match.Success && int.TryParse(match.Groups["years"].Value, out var years) ? years : 0;
    }

    private static bool IsMandatory(string text, int years)
    {
        if (years == 0) return false;
        var pattern = $@"(?:at least|minimum(?: of)?|must have|mindst|minimum)\s+{years}\s*\+?\s*(?:years|års|år)\b[^.\n]{{0,80}}\b(?:required|minimum|at least|must|krav|påkrævet)\b" +
                      $@"|\b{years}\s*\+?\s*(?:years|års|år)\b[^.\n]{{0,80}}\b(?:required|minimum|at least|must|krav|påkrævet)\b";
        if (!Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))) return false;
        var nearby = text.ToLowerInvariant();
        return !Regex.IsMatch(nearby, @"(?:preferred|nice.to.have|desired|advantage|ønskeligt|en fordel)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }
}
