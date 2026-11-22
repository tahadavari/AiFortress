using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace ClaudeGuard;

internal sealed class Localization(string code, string name, bool rightToLeft, Dictionary<string, string> messages)
{
    private const string ResourcePrefix = "ClaudeGuard.Localization.";
    private static readonly IReadOnlyList<Localization> Catalogs = LoadCatalogs();

    public static IReadOnlyList<Localization> Available => Catalogs;
    public string Code { get; } = code;
    public string Name { get; } = name;
    public bool RightToLeft { get; } = rightToLeft;

    public static Localization ForLanguage(string? code) =>
        Catalogs.FirstOrDefault(language => language.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
        ?? Catalogs[0];

    public string this[string key] =>
        messages.GetValueOrDefault(key) ?? Catalogs[0].MessageOrKey(key);

    public string Format(string key, params object[] values) =>
        string.Format(CultureInfo.InvariantCulture, this[key], values);

    private string MessageOrKey(string key) => messages.GetValueOrDefault(key, key);

    private static IReadOnlyList<Localization> LoadCatalogs()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceNames()
            .Where(resource => resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) && resource.EndsWith(".json"))
            .Select(resource =>
            {
                using var stream = assembly.GetManifestResourceStream(resource)!;
                var content = JsonSerializer.Deserialize<Catalog>(stream)!;
                var code = resource[ResourcePrefix.Length..^5];
                return new Localization(code, content.Name, content.RightToLeft, content.Messages);
            })
            .OrderBy(language => language.Code != "en")
            .ThenBy(language => language.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed record Catalog(string Name, bool RightToLeft, Dictionary<string, string> Messages);
}
