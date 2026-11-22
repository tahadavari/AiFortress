using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.Sockets;

namespace AiFortress;

internal sealed class Enforcer(IReadOnlyList<ServiceProfile> profiles)
{
    // Keep the existing identifiers so upgrades can clean up rules written by ClaudeGuard.
    private const string FirewallRuleName = "ClaudeGuard-Block";
    private const string HostsBegin = "# >>> ClaudeGuard";
    private const string HostsEnd = "# <<< ClaudeGuard";
    private static readonly string HostsPath = Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");
    private readonly string[] _networkDomains = profiles.SelectMany(profile => profile.NetworkDomains)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private readonly string[] _browserDomains = profiles.SelectMany(profile => profile.BrowserDomains)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private readonly Func<string, string, bool>[] _processMatchers =
        profiles.Select(profile => profile.MatchesProcess).OfType<Func<string, string, bool>>().ToArray();

    private bool? _firewall;
    private bool? _hosts;
    private bool? _browser;
    private bool _hostsNeedFlush;

    public async Task<bool> ReconcileAsync(AppConfig config, bool block, Action<string, object[]> log)
    {
        var firewall = block && config.BlockFirewall && _networkDomains.Length > 0;
        var hosts = block && config.BlockHosts && _networkDomains.Length > 0;
        var browser = block && config.BlockBrowserPolicy && _browserDomains.Length > 0;

        // Remove a disabled hosts layer before resolving addresses for the firewall.
        if (!hosts)
            await UpdateAsync("LayerHosts", false, _hosts, UpdateHostsAsync, value => _hosts = value, log);
        await UpdateAsync("LayerFirewall", firewall, _firewall, UpdateFirewallAsync, value => _firewall = value, log);
        if (hosts)
            await UpdateAsync("LayerHosts", true, _hosts, UpdateHostsAsync, value => _hosts = value, log);
        await UpdateAsync("LayerBrowser", browser, _browser, value =>
        {
            BrowserPolicy.Update(value, _browserDomains);
            return Task.CompletedTask;
        }, value => _browser = value, log);

        var processesOk = true;
        if (block && config.KillProcesses && _processMatchers.Length > 0)
        {
            try
            {
                var killed = CloseTargetProcesses();
                if (killed > 0)
                    log("ProcessesClosed", [killed]);
            }
            catch (Exception ex)
            {
                log("LayerError", ["LayerProcesses", ex.Message]);
                processesOk = false;
            }
        }

        return _firewall == firewall && _hosts == hosts && _browser == browser && processesOk;
    }

    private static async Task UpdateAsync(string layer, bool desired, bool? current,
        Func<bool, Task> update, Action<bool?> setState, Action<string, object[]> log)
    {
        if (current == desired)
            return;

        try
        {
            await update(desired);
            setState(desired);
            if (desired || current == true)
                log(desired ? "LayerEnabled" : "LayerDisabled", [layer]);
        }
        catch (Exception ex)
        {
            // An operation may have changed part of the layer before failing.
            setState(null);
            log("LayerError", [layer, ex.Message]);
        }
    }

    private int CloseTargetProcesses()
    {
        using var searcher = new ManagementObjectSearcher(
            new ManagementScope(@"root\cimv2"),
            new ObjectQuery("SELECT ProcessId, Name, CommandLine FROM Win32_Process"),
            new System.Management.EnumerationOptions { ReturnImmediately = true, Timeout = TimeSpan.FromSeconds(5) });
        using var results = searcher.Get();
        int killed = 0;
        foreach (ManagementObject entry in results)
        {
            using (entry)
            {
                var pid = Convert.ToInt32(entry["ProcessId"]);
                if (pid == Environment.ProcessId ||
                    !IsTargetProcess(entry["Name"] as string ?? "", entry["CommandLine"] as string ?? ""))
                    continue;

                try
                {
                    using var process = Process.GetProcessById(pid);
                    process.Kill();
                    killed++;
                }
                catch (ArgumentException) { } // The process exited before it could be opened.
                catch (InvalidOperationException) { }
            }
        }
        return killed;
    }

    internal bool IsTargetProcess(string name, string commandLine) =>
        _processMatchers.Any(matches => matches(name, commandLine));

    private async Task UpdateFirewallAsync(bool enabled)
    {
        var name = $"name={FirewallRuleName}";
        var existing = await FirewallCommand("show", "rule", name, "dir=out");
        if (!enabled)
        {
            if (existing.ExitCode == 0)
                EnsureSuccess(await FirewallCommand("delete", "rule", name, "dir=out"));
            else if (existing.ExitCode != 1)
                EnsureSuccess(existing);
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var addresses = (await Task.WhenAll(_networkDomains.Select(domain => ResolveAsync(domain, timeout.Token))))
            .SelectMany(ip => ip)
            .Where(ip => !IPAddress.IsLoopback(ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any))
            .Distinct().ToArray();
        if (addresses.Length == 0)
        {
            if (existing.ExitCode == 0)
                return; // A previous hosts block can hide DNS records after restart.
            throw new InvalidOperationException("DNS");
        }

        var parameters = new[] { "action=block", $"remoteip={string.Join<IPAddress>(",", addresses)}", "enable=yes" };
        var result = existing.ExitCode == 0
            ? await FirewallCommand(["set", "rule", name, "dir=out", "new", .. parameters])
            : await FirewallCommand(["add", "rule", name, "dir=out", "protocol=TCP", .. parameters]);
        EnsureSuccess(result);
    }

    private static async Task<IPAddress[]> ResolveAsync(string domain, CancellationToken token)
    {
        try { return await Dns.GetHostAddressesAsync(domain, token); }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException) { return []; }
    }

    private static Task<WindowsCommand.Result> FirewallCommand(params string[] arguments) =>
        WindowsCommand.RunAsync("netsh.exe", ["advfirewall", "firewall", .. arguments]);

    private async Task UpdateHostsAsync(bool enabled)
    {
        var original = File.ReadAllLines(HostsPath);
        var lines = WithoutOwnedHostsEntries(original);
        if (enabled)
        {
            lines.Add(HostsBegin);
            lines.AddRange(_networkDomains.Select(domain => $"0.0.0.0 {domain}"));
            lines.Add(HostsEnd);
        }
        if (!original.SequenceEqual(lines))
        {
            var temporary = HostsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(temporary, lines);
                File.Replace(temporary, HostsPath, null);
                _hostsNeedFlush = true;
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        if (_hostsNeedFlush)
        {
            EnsureSuccess(await WindowsCommand.RunAsync("ipconfig.exe", ["/flushdns"]));
            _hostsNeedFlush = false;
        }
    }

    internal static List<string> WithoutOwnedHostsEntries(string[] lines)
    {
        var result = new List<string>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].Trim() == HostsBegin)
            {
                var end = Array.FindIndex(lines, index + 1, line => line.Trim() == HostsEnd || line.Trim() == HostsBegin);
                if (end >= 0 && lines[end].Trim() == HostsEnd)
                {
                    index = end;
                    continue;
                }
            }
            result.Add(lines[index]);
        }
        return result;
    }

    private static void EnsureSuccess(WindowsCommand.Result result)
    {
        if (result.ExitCode != 0)
            throw new Win32Exception(result.ExitCode, result.Message);
    }
}
