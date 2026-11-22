using System.Globalization;
using Microsoft.Win32;

namespace ClaudeGuard;

internal static class BrowserPolicy
{
    private const string OwnershipRoot = @"SOFTWARE\ClaudeGuard\BrowserPolicy";
    private static readonly (string Name, string Path)[] Browsers =
    [
        ("Chrome", @"SOFTWARE\Policies\Google\Chrome\URLBlocklist"),
        ("Edge", @"SOFTWARE\Policies\Microsoft\Edge\URLBlocklist")
    ];
    private static readonly string[] Entries = ["claude.ai", "claude.com", "anthropic.com", "claudeusercontent.com"];

    public static void Update(bool enabled)
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
                if (enabled && Equals(policy?.GetValue(slot), entry))
                    continue;
                if (!enabled && entry != null && Equals(policy?.GetValue(slot), entry))
                    policy!.DeleteValue(slot, throwOnMissingValue: false);
                owned.DeleteValue(slot, throwOnMissingValue: false);
            }

            if (!enabled)
                continue;

            var occupied = policy!.GetValueNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existing = occupied.Select(slot => policy.GetValue(slot)).OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var next = 1;
            foreach (var entry in Entries.Where(entry => !existing.Contains(entry)))
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
