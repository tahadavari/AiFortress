namespace AiFortress;

internal sealed record ServiceProfile(
    string Name,
    IReadOnlyList<string> NetworkDomains,
    IReadOnlyList<string> BrowserDomains,
    Func<string, string, bool>? MatchesProcess = null);
