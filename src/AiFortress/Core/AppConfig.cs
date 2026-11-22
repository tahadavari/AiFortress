using System.Text.Json;

namespace AiFortress;

internal sealed class AppConfig
{
    public const int MinimumInterval = 5;
    public const int MaximumInterval = 3600;

    public string CountryCode { get; set; } = "US";
    public string Language { get; set; } = "en";
    public int CheckIntervalSeconds { get; set; } = 30;
    public bool ProtectionEnabled { get; set; } = true;
    public bool BlockFirewall { get; set; } = true;
    public bool BlockHosts { get; set; } = true;
    public bool BlockBrowserPolicy { get; set; } = true;
    public bool KillProcesses { get; set; } = true;
    public bool FailClosed { get; set; } = false;
    public List<string> AllowedTimezones { get; set; } = new();

    public static IReadOnlyDictionary<string, string[]> Countries { get; } = new Dictionary<string, string[]>
    {
        ["US"] = [
            "Eastern Standard Time", "Central Standard Time", "Mountain Standard Time",
            "Pacific Standard Time", "Alaskan Standard Time", "Hawaiian Standard Time",
            "US Eastern Standard Time", "US Mountain Standard Time", "Aleutian Standard Time"],
        ["CA"] = [
            "Eastern Standard Time", "Central Standard Time", "Mountain Standard Time",
            "Pacific Standard Time", "Atlantic Standard Time", "Newfoundland Standard Time"],
        ["GB"] = ["GMT Standard Time"],
        ["DE"] = ["W. Europe Standard Time", "Central Europe Standard Time"],
        ["NL"] = ["W. Europe Standard Time"],
        ["FR"] = ["Romance Standard Time", "W. Europe Standard Time"],
        ["JP"] = ["Tokyo Standard Time"],
        ["SG"] = ["Singapore Standard Time"],
        ["AU"] = ["AUS Eastern Standard Time", "AUS Central Standard Time", "W. Australia Standard Time"],
    };

    public static List<string> ParseTimezones(IEnumerable<string?> lines) => lines
        .Select(line => line?.Trim() ?? "")
        .Where(line => line.Length != 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public AppConfig Snapshot()
    {
        var copy = (AppConfig)MemberwiseClone();
        copy.AllowedTimezones = [.. AllowedTimezones];
        return copy;
    }

    internal void Normalize()
    {
        CountryCode = CountryCode?.Trim().ToUpperInvariant() ?? "US";
        if (!Countries.ContainsKey(CountryCode))
            CountryCode = "US";

        Language = Localization.ForLanguage(Language).Code;
        CheckIntervalSeconds = Math.Clamp(CheckIntervalSeconds, MinimumInterval, MaximumInterval);
        AllowedTimezones = ParseTimezones(AllowedTimezones ?? []);
        if (AllowedTimezones.Count == 0)
            AllowedTimezones = [.. Countries[CountryCode]];
    }
}

internal sealed class ConfigStore(string path, string? previousPath = null)
{
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    public static string DefaultPath { get; } = Path.Combine(
        ProgramData, "AiFortress", "config.json");
    public static string LegacyPath { get; } = Path.Combine(ProgramData, "ClaudeGuard", "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppConfig Load(out string? error)
    {
        var config = new AppConfig();
        error = null;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(Read()) ?? config;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // First run.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = ex.Message;
        }

        config.Normalize();
        return config;
    }

    private string Read()
    {
        try { return File.ReadAllText(path); }
        catch (Exception ex) when (previousPath != null && (ex is FileNotFoundException or DirectoryNotFoundException))
        {
            return File.ReadAllText(previousPath);
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(config, JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
