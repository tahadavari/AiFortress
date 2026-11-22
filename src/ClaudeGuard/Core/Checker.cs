using System.Net;
using System.Text.Json;

namespace ClaudeGuard;

internal enum CheckState { Unknown, Ok, Violation }

internal sealed record CheckResult(
    CheckState IpState = CheckState.Unknown,
    CheckState TimeZoneState = CheckState.Unknown,
    string Ip = "",
    string Country = "",
    string TimeZone = "")
{
    public bool ShouldBlock(AppConfig config) => config.ProtectionEnabled &&
        (IpState == CheckState.Violation || TimeZoneState == CheckState.Violation ||
         (config.FailClosed && IpState == CheckState.Unknown));
}

internal sealed class Checker(HttpClient http, Func<string> readTimeZone)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private static readonly string[] Providers = ["https://api.country.is/", "https://ipinfo.io/json"];

    public static Checker Create() => new(Http, WindowsTimeZone.CurrentId);

    public async Task<CheckResult> RunAsync(AppConfig config, CancellationToken cancellationToken)
    {
        var location = await LookupAsync(cancellationToken).ConfigureAwait(false);
        var timeZone = readTimeZone();
        var timeZoneState = config.AllowedTimezones.Contains(timeZone, StringComparer.OrdinalIgnoreCase)
            ? CheckState.Ok : CheckState.Violation;
        if (location is not { } found)
            return new CheckResult(TimeZoneState: timeZoneState, TimeZone: timeZone);

        var ipState = config.CountryCode.Equals(found.Country, StringComparison.OrdinalIgnoreCase)
            ? CheckState.Ok : CheckState.Violation;
        return new CheckResult(ipState, timeZoneState, found.Ip, found.Country, timeZone);
    }

    private async Task<(string Ip, string Country)?> LookupAsync(CancellationToken cancellationToken)
    {
        foreach (var provider in Providers)
        {
            try
            {
                var json = await http.GetStringAsync(provider, cancellationToken).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    continue;

                var ip = GetString(document.RootElement, "ip");
                var country = GetString(document.RootElement, "country");
                if (IPAddress.TryParse(ip, out _) && country is { Length: 2 } && country.All(char.IsAsciiLetter))
                    return (ip!, country.ToUpperInvariant());
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException ||
                ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // Use the next provider when a request fails or returns malformed data.
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}
