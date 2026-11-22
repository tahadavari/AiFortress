namespace AiFortress;

internal static class ServiceCatalog
{
    internal static ServiceProfile Claude { get; } = new(
        "Claude",
        [
            "claude.ai", "www.claude.ai", "api.claude.ai", "claude.com", "www.claude.com",
            "anthropic.com", "www.anthropic.com", "api.anthropic.com", "console.anthropic.com",
            "claudeusercontent.com", "www.claudeusercontent.com", "statsig.anthropic.com"
        ],
        ["claude.ai", "claude.com", "anthropic.com", "claudeusercontent.com"],
        IsClaudeProcess);

    internal static IReadOnlyList<ServiceProfile> Supported { get; } = [Claude];

    private static bool IsClaudeProcess(string name, string commandLine) =>
        name.Equals("claude.exe", StringComparison.OrdinalIgnoreCase) ||
        (name.StartsWith("claude-", StringComparison.OrdinalIgnoreCase) &&
         name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) ||
        (name.Equals("node.exe", StringComparison.OrdinalIgnoreCase) &&
         (commandLine.Contains(@"@anthropic-ai\claude-code", StringComparison.OrdinalIgnoreCase) ||
          commandLine.Contains("@anthropic-ai/claude-code", StringComparison.OrdinalIgnoreCase)));
}
