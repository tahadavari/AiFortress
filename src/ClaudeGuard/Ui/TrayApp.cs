using System.Drawing;

namespace ClaudeGuard;

internal sealed class TrayApp : ApplicationContext
{
    private readonly ConfigStore _store;
    private readonly Checker _checker;
    private readonly Func<AppConfig, bool, Action<string, object[]>, Task<bool>> _enforce;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 150 };
    private readonly List<ActivityEntry> _activity = [];
    private readonly CancellationTokenSource _stop = new();

    private AppConfig _config;
    private MainForm? _form;
    private CheckResult? _last;
    private Task? _check;
    private bool _queued;
    private bool _exiting;
    private bool _enforcementOk = true;
    private bool _checkFailed;
    private Icon? _icon;
    private Color _iconColor;
    private ToolStripMenuItem? _checkItem;

    private Localization Language => Localization.ForLanguage(_config.Language);

    internal TrayApp(ConfigStore store, Checker checker,
        Func<AppConfig, bool, Action<string, object[]>, Task<bool>> enforce)
    {
        _store = store;
        _checker = checker;
        _enforce = enforce;
        _config = store.Load(out var loadError);
        _tray = new NotifyIcon { Text = "ClaudeGuard", ContextMenuStrip = _menu };
        _tray.DoubleClick += (_, _) => Open();
        RebuildMenu();
        SetIcon(Theme.Muted);
        _tray.Visible = true;

        _timer.Tick += (_, _) =>
        {
            UpdateInterval();
            _ = CheckNowAsync();
        };
        _timer.Start();
        Log("Started");
        if (loadError is not null)
            Log("SettingsLoadError", loadError);
    }

    private void RebuildMenu()
    {
        var previousItems = _menu.Items.Cast<ToolStripItem>().ToArray();
        _menu.Items.Clear();
        foreach (var item in previousItems)
            item.Dispose();

        var language = Language;
        _menu.RightToLeft = language.RightToLeft ? RightToLeft.Yes : RightToLeft.No;
        _menu.Items.Add(language["Open"], null, (_, _) => Open());
        _checkItem = new ToolStripMenuItem(language["CheckNow"], null, (_, _) =>
        {
            Open();
            _ = CheckNowAsync();
        }) { Enabled = _check is null };
        _menu.Items.Add(_checkItem);
        _menu.Items.Add(new ToolStripSeparator());
        var toggle = new ToolStripMenuItem(language["Protection"])
        {
            Checked = _config.ProtectionEnabled
        };
        toggle.Click += (_, _) =>
        {
            var next = _config.Snapshot();
            next.ProtectionEnabled = !next.ProtectionEnabled;
            if (!SaveSettings(next))
                Open();
        };
        _menu.Items.Add(toggle);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(language["Exit"], null, async (_, _) => await ExitAsync());
    }

    internal MainForm Open()
    {
        _form ??= new MainForm(_config, CheckNowAsync, SaveSettings, ApplyTimeZoneAsync);
        RefreshPresentation();
        _form.RefreshActivity(_activity);
        _form.Show();
        _form.WindowState = FormWindowState.Normal;
        _form.Activate();
        return _form;
    }

    private void Log(string key, params object[] arguments)
    {
        _activity.Add(new ActivityEntry(DateTimeOffset.Now, key, arguments));
        if (_activity.Count > 60)
            _activity.RemoveAt(0);
        _form?.RefreshActivity(_activity);
    }

    private bool SaveSettings(AppConfig next)
    {
        if (_exiting)
            return false;

        try
        {
            _store.Save(next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log("SettingsSaveError", ex.Message);
            return false;
        }

        bool updateMenu = next.Language != _config.Language || next.ProtectionEnabled != _config.ProtectionEnabled;
        bool recheck = next.CountryCode != _config.CountryCode || next.ProtectionEnabled != _config.ProtectionEnabled ||
            next.BlockFirewall != _config.BlockFirewall || next.BlockHosts != _config.BlockHosts ||
            next.BlockBrowserPolicy != _config.BlockBrowserPolicy || next.KillProcesses != _config.KillProcesses ||
            next.FailClosed != _config.FailClosed ||
            !next.AllowedTimezones.SequenceEqual(_config.AllowedTimezones, StringComparer.OrdinalIgnoreCase);
        _config = next;
        UpdateInterval();
        _form?.UpdateSettings(next);
        if (updateMenu)
            RebuildMenu();
        RefreshPresentation();
        if (recheck)
        {
            _queued = _check is not null;
            _ = CheckNowAsync();
        }
        return true;
    }

    private void UpdateInterval() => _timer.Interval = _config.CheckIntervalSeconds * 1000;

    internal Task CheckNowAsync()
    {
        if (_exiting)
            return Task.CompletedTask;

        if (_check is not null)
            return _check;

        _check = RunChecksAsync();
        RefreshPresentation();
        return _check;
    }

    private async Task RunChecksAsync()
    {
        // Give the UI a chance to paint before the first check.
        await Task.Yield();
        try
        {
            do
            {
                _queued = false;
                var config = _config;
                var result = await _checker.RunAsync(config, _stop.Token);
                if (_queued || _exiting)
                    continue;

                var entries = new List<(string Key, object[] Arguments)>();
                var block = result.ShouldBlock(config);
                var enforcementOk = await Task.Run(() => _enforce(config, block,
                    (key, args) => entries.Add((key, args))));
                foreach (var entry in entries)
                    Log(entry.Key, entry.Arguments);

                if (_exiting)
                    break;

                _last = result;
                _enforcementOk = enforcementOk;
                _checkFailed = false;
                if (result.IpState == CheckState.Unknown)
                    Log("NetworkUnknown");
                else
                    Log("CheckCompleted", result.Country, result.TimeZone);
            } while (_queued && !_exiting);
        }
        catch (OperationCanceledException) when (_exiting) { }
        catch (Exception ex)
        {
            _checkFailed = true;
            Log("CheckError", ex.Message);
        }
        finally
        {
            _check = null;
            RefreshPresentation();
        }
    }

    private async Task<bool> ApplyTimeZoneAsync(string id)
    {
        try
        {
            await WindowsTimeZone.SetAsync(id);
            Log("TimeZoneChanged", id);
            if (_check is not null)
                _queued = true;
            await CheckNowAsync();
            return true;
        }
        catch (Exception ex)
        {
            Log("TimeZoneError", ex.Message);
            return false;
        }
    }

    private void RefreshPresentation()
    {
        var checking = _check is not null;
        if (_checkItem is not null)
            _checkItem.Enabled = !checking && !_exiting;

        var key = "Waiting";
        var color = Theme.Muted;
        if (!_config.ProtectionEnabled)
            key = "Disabled";
        else if (_checkFailed || !_enforcementOk)
            (key, color) = ("NeedsAttention", Theme.Amber);
        else if (_last?.ShouldBlock(_config) == true)
            (key, color) = ("Mismatch", Theme.Red);
        else if (_last?.IpState == CheckState.Ok && _last.TimeZoneState == CheckState.Ok)
            (key, color) = ("Matching", Theme.Green);
        else if (_last is not null)
            (key, color) = ("Unverified", Theme.Amber);

        SetIcon(color);
        var tip = $"ClaudeGuard · {Language[key]}";
        _tray.Text = tip[..Math.Min(tip.Length, 63)];
        _form?.RefreshStatus(_last, key, color, checking);
    }

    private void SetIcon(Color color)
    {
        if (_iconColor == color)
            return;
        var previous = _icon;
        _icon = ShieldIcon.Create(color);
        _tray.Icon = _icon;
        _iconColor = color;
        previous?.Dispose();
    }

    private async Task ExitAsync()
    {
        if (_exiting)
            return;
        _exiting = true;
        _timer.Stop();
        _stop.Cancel();
        if (_check is not null)
            await _check;

        try
        {
            var entries = new List<(string Key, object[] Arguments)>();
            var clean = await Task.Run(() => _enforce(_config, false, (key, args) => entries.Add((key, args))));
            if (!clean)
            {
                foreach (var entry in entries)
                    Log(entry.Key, entry.Arguments);
                MessageBox.Show(Language["CleanupError"], "ClaudeGuard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            ExitThread();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _stop.Dispose();
            _tray.Dispose();
            _icon?.Dispose();
            _menu.Dispose();
            _form?.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed record ActivityEntry(DateTimeOffset Time, string Key, object[] Arguments)
{
    public string Text(Localization language)
    {
        object[] arguments = Key is "LayerEnabled" or "LayerDisabled" or "LayerError"
            ? [language[(string)Arguments[0]], .. Arguments.Skip(1)]
            : Arguments;
        return $"[{Time:HH:mm:ss}]  {language.Format(Key, arguments)}";
    }
}
