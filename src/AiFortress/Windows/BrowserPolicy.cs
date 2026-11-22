using System.Globalization;
using Microsoft.Win32;

namespace AiFortress;

internal static class BrowserPolicy
{
    private const string OwnershipRoot = @"SOFTWARE\ClaudeGuard\BrowserPolicy";
    private static readonly (string Name, string Path)[] Browsers =
    [
        ("Chrome", @"SOFTWARE\Policies\Google\Chrome\URLBlocklist"),
        ("Edge", @"SOFTWARE\Policies\Microsoft\Edge\URLBlocklist")
    ];
    public static void Update(bool enabled, IReadOnlyList<string> entries)
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        foreach (var (name, path) in Browsers)
        {
            var ownedPath = $@"{OwnershipRoot}\{name}";
            using var owned = enabled ? machine.CreateSubKey(ownedPath) : machine.OpenSubKey(ownedPath, writable: true);
            if (owned == null)
                continue;

            using var policy = enabled ? machine.CreateSubKey(path) : machine.OpenSubKey(path, writable: true);
            foreach (var slot in owned.GetValueNames())
            {
                var entry = owned.GetValue(slot) as string;
                var stillOwned = entry != null && Equals(policy?.GetValue(slot), entry);
                if (enabled && stillOwned && entries.Contains(entry!, StringComparer.OrdinalIgnoreCase))
                    continue;
                if (stillOwned)
                    policy!.DeleteValue(slot, throwOnMissingValue: false);
                owned.DeleteValue(slot, throwOnMissingValue: false);
            }

            if (!enabled)
                continue;

            var occupied = policy!.GetValueNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existing = occupied.Select(slot => policy.GetValue(slot)).OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var next = 1;
            foreach (var entry in entries.Where(entry => !existing.Contains(entry)))
            {
                string slot;
                do { slot = (next++).ToString(CultureInfo.InvariantCulture); } while (!occupied.Add(slot));
                // Record ownership first so an interrupted update is safe to retry.
                owned.SetValue(slot, entry, RegistryValueKind.String);
                policy.SetValue(slot, entry, RegistryValueKind.String);
            }
        }
    }
}
