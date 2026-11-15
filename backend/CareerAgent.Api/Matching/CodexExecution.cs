using System.Text.Json;

namespace CareerAgent.Api.Matching;

public sealed class CodexExecutionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class CodexExecution
{
    // Raw process output may contain private text. Only return fixed, safe messages.
    public static CodexExecutionException Failure(string diagnostic)
    {
        bool Has(params string[] terms) => terms.Any(t => diagnostic.Contains(t, StringComparison.OrdinalIgnoreCase));
        if (Has("Operation not permitted", "Permission denied", "os error 1", "os error 13"))
            return new("codex_start_blocked", "Mac'en eller det miljø, backend er startet i, blokerer Codex. Start backend fra en almindelig terminal, eller giv den nødvendige tilladelse i Codex og genstart backend.");
        if (Has("usage_limit", "quota", "usage limit", "rate limit", "429"))
            return new("codex_quota", "Codex-kvoten eller hastighedsgrænsen er nået. Kontrollér dit forbrug i ChatGPT, og prøv senere.");
        if (Has("unauthorized", "401", "not logged in", "refresh token", "authentication", "login required"))
            return new("codex_auth", "Codex-login kunne ikke bruges. Log ind i Codex med ChatGPT på denne Mac, og prøv igen.");
        if (Has("invalid_json_schema", "invalid schema", "unknown feature", "unexpected argument", "not supported"))
            return new("codex_configuration", "Codex afviste appens konfiguration eller outputformat. Integrationen skal kontrolleres; dine dokumenter er ikke ændret.");
        if (Has("error sending request", "stream disconnected", "connection", "resolve", "network", "certificate"))
            return new("codex_connection", "Forbindelsen til Codex blev afbrudt. Kontrollér internetforbindelsen, og prøv igen.");
        return new("codex_failed", "Codex kunne ikke færdiggøre kørslen. Prøv igen; hvis fejlen fortsætter, skal Codex-forbindelsen undersøges.");
    }

    public static void ValidateCompletedRun(int exitCode, bool outputExists, string events, string stderr)
    {
        if (exitCode != 0 || !outputExists) throw Failure(stderr + "\n" + events);
        foreach (var line in events.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var eventType) && eventType.GetString() is "turn.failed" or "error")
                throw Failure(line);
            if (!root.TryGetProperty("item", out var item) || !item.TryGetProperty("type", out var type)) continue;
            if (type.GetString() is "agent_message" or "reasoning") continue;
            // Codex 0.167 reports this advisory as an error item even when tools are intentionally
            // disabled and a text-only turn succeeds. It is not an executed tool or a failed turn.
            if (type.GetString() == "error" && item.TryGetProperty("message", out var message) &&
                message.GetString()?.StartsWith("Code Mode is unavailable because code-mode host is disabled.", StringComparison.Ordinal) == true)
                continue;
            if (type.GetString() == "error") throw Failure(line);
            throw new InvalidDataException("AI-kørslen forsøgte en anden handling end tekstdannelse. Svaret blev afvist.");
        }
    }
}
