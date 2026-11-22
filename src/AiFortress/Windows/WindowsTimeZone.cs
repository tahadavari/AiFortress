namespace AiFortress;

internal static class WindowsTimeZone
{
    public static string CurrentId()
    {
        TimeZoneInfo.ClearCachedData();
        return TimeZoneInfo.Local.Id;
    }

    public static IReadOnlyList<TimeZoneInfo> Available(IEnumerable<string> allowed)
    {
        // Only native Windows IDs are accepted by tzutil.
        var zones = TimeZoneInfo.GetSystemTimeZones().ToDictionary(zone => zone.Id, StringComparer.OrdinalIgnoreCase);
        return allowed.Where(zones.ContainsKey).Select(id => zones[id]).ToArray();
    }

    public static async Task SetAsync(string id)
    {
        if (Available([id]).SingleOrDefault() is not { } zone)
            throw new TimeZoneNotFoundException(id);

        var result = await WindowsCommand.RunAsync("tzutil.exe", ["/s", zone.Id]).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(result.Message);

        var actual = CurrentId();
        if (!actual.Equals(zone.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(actual);
    }
}
