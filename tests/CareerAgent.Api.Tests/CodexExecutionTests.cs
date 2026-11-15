using System.Text.Json;
using CareerAgent.Api.Documents;
using CareerAgent.Api.Matching;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CareerAgent.Api.Tests;

public sealed class CodexExecutionTests
{
    private const string Advisory = """{"type":"item.completed","item":{"type":"error","message":"Code Mode is unavailable because code-mode host is disabled. Code mode will fail closed; enable features.code_mode_host."}}""";
    [Fact]
    public void Successful_text_run_accepts_disabled_code_mode_advisory()
    {
        CodexExecution.ValidateCompletedRun(0, true, Advisory + "\n" + """{"type":"item.completed","item":{"type":"agent_message","text":"synthetic"}}""", "");
    }
    [Theory]
    [InlineData("command_execution")]
    [InlineData("mcp_tool_call")]
    [InlineData("web_search")]
    [InlineData("file_change")]
    public void Executed_tools_remain_rejected_even_with_a_valid_output_file(string type)
    {
        var events = Advisory + "\n" + JsonSerializer.Serialize(new { type = "item.completed", item = new { type } });
        Assert.Throws<InvalidDataException>(() => CodexExecution.ValidateCompletedRun(0, true, events, ""));
    }
    [Theory]
    [InlineData("failed to initialize in-process app-server client: Operation not permitted (os error 1)", "codex_start_blocked")]
    [InlineData("usage_limit_reached", "codex_quota")]
    [InlineData("HTTP 401 Unauthorized", "codex_auth")]
    [InlineData("stream disconnected", "codex_connection")]
    [InlineData("invalid_json_schema", "codex_configuration")]
    public void Failures_return_specific_safe_messages_without_private_diagnostics(string diagnostic, string code)
    {
        var error = Assert.Throws<CodexExecutionException>(() => CodexExecution.ValidateCompletedRun(1, false, "", diagnostic + " private-test-secret"));
        Assert.Equal(code, error.Code);
        Assert.DoesNotContain("private-test-secret", error.Message);
    }
    [Fact]
    public void Failed_turn_and_missing_output_are_not_accepted_as_success()
    {
        Assert.Throws<CodexExecutionException>(() => CodexExecution.ValidateCompletedRun(0, false, Advisory, ""));
        Assert.Throws<CodexExecutionException>(() => CodexExecution.ValidateCompletedRun(0, true, """{"type":"turn.failed","error":{"message":"usage_limit_reached"}}""", ""));
        Assert.Throws<CodexExecutionException>(() => CodexExecution.ValidateCompletedRun(0, true, """{"type":"item.completed","item":{"type":"error","message":"Unexpected runtime failure"}}""", ""));
    }
    [Fact]
    public async Task Document_generator_accepts_realistic_process_events_and_validates_both_documents()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Directory.CreateTempSubdirectory("career-agent-synthetic-cli-").FullName;
        try
        {
            var content = new { title = "Synthetic document", subtitle = (string?)null, sections = new[] {
                new { heading = "Profile", blocks = new[] { new { type = "paragraph", text = "Synthetic profile.",
                    evidence = new[] { new { id = "profile:summary", quote = DiscoveryTests.Profile.Summary } } } } } } };
            var json = JsonSerializer.Serialize(new { Cv = content, Application = content });
            var executable = Path.Combine(directory, "codex-fixture");
            var script = "#!/bin/sh\nwhile [ \"$#\" -gt 0 ]; do\nif [ \"$1\" = \"--output-last-message\" ]; then shift; output=\"$1\"; fi\nshift\ndone\n" +
                "cat >/dev/null\nprintf '%s' '" + json.Replace("'", "'\\''") + "' > \"$output\"\nprintf '%s\\n' '" + Advisory + "'\n";
            await File.WriteAllTextAsync(executable, script);
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CareerAgent:CodexPath"] = executable }).Build();
            var generator = new CodexDocumentGenerator(configuration);
            var documents = await generator.Generate(new() { Title = "Synthetic Software Developer", Description = "Synthetic job description." }, DiscoveryTests.Profile, [], "Dansk", "Both", default);
            Assert.Equal(2, documents.Count);
            Assert.Equal("Synthetic document", documents["Cv"].Title);
            Assert.Equal("Synthetic document", documents["Application"].Title);
        }
        finally { Directory.Delete(directory, true); }
    }
}
