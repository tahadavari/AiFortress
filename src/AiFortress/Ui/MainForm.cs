namespace AiFortress;

internal sealed class MainForm : Form
{
    private static readonly Size FlyoutSize = new(408, 620);

    private AppConfig _config;
    private Localization _language;
    private readonly Func<Task> _checkNow;
    private readonly Func<AppConfig, bool> _save;
    private readonly Func<string, Task<bool>> _applyTimeZone;
    private readonly IReadOnlyList<string> _serviceNames;
    private readonly ToolTip _toolTip = new();
    private readonly Dictionary<Control, string> _labels = [];
    private readonly Dictionary<CheckBox, Func<AppConfig, bool>> _toggles = [];

    private readonly FlyoutPicker _languages = new("Language");
    private readonly FlyoutPicker _countries = new("Country");
    private readonly FlyoutPicker _zones = new("TimeZone");
    private readonly IntervalEditor _interval = new();
    private readonly FlyoutTextEditor _allowed = new() { Name = "AllowedTimeZones" };
    private readonly ListBox _activity = new()
    {
        BackColor = Theme.Background, ForeColor = Theme.Muted, BorderStyle = BorderStyle.None,
        IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 18
    };
    private readonly Label _summary = MakeLabel(11, FontStyle.Regular);
    private readonly Label _protectionState = MakeLabel(17, FontStyle.Bold);
    private readonly Label _ip = MakeLabel(10, FontStyle.Bold);
    private readonly Label _ipStatus = MakeLabel(9, FontStyle.Bold);
    private readonly Label _zone = MakeLabel(10, FontStyle.Bold);
    private readonly Label _zoneStatus = MakeLabel(9, FontStyle.Bold);
    private readonly Label _notice = MakeLabel(9, FontStyle.Regular);
    private readonly Button _check = MakeButton("CheckNow", primary: true);
    private readonly Button _apply = MakeButton("SetTimeZone");
    private readonly Button _saveZones = MakeButton("SaveTimeZones");
    private readonly Icon _windowIcon = FortressIcon.Create(Theme.Accent);
    private Bitmap? _brandMark;
    private readonly ProtectionButton _protection = new() { Name = "ProtectionToggle", Dock = DockStyle.Fill };
    private readonly FlyoutButton[] _tabs = new[] { "Overview", "LocationTab", "Settings" }
        .Select(key => new FlyoutButton { Name = key, Dock = DockStyle.Fill, Quiet = true }).ToArray();
    private readonly FlyoutButton _close = new() { Name = "Hide", Text = "×", Dock = DockStyle.Fill, Quiet = true };
    private Control[] _pages = [];
    private Screen? _activeScreen;

    private IReadOnlyList<ActivityEntry> _entries = [];
    private bool _populating;
    private bool _checking;
    private bool _changingZone;
    private string? _noticeKey;
    private int _hoveredEntry = ListBox.NoMatches;

    public MainForm(AppConfig config, Func<Task> checkNow, Func<AppConfig, bool> save, Func<string, Task<bool>> applyTimeZone,
        IReadOnlyList<string> serviceNames)
    {
        _config = config;
        _language = Localization.ForLanguage(config.Language);
        _checkNow = checkNow;
        _save = save;
        _applyTimeZone = applyTimeZone;
        _serviceNames = serviceNames;
        Text = "AiFortress";
        Icon = _windowIcon;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = FlyoutSize;
        Font = new Font(Theme.Typeface, 10f);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Controls.Add(BuildLayout());
        BindEvents();
        ApplyLanguage();
        WriteSettings(refreshZones: true);
        ShowPage(0);
        FormClosing += (_, e) =>
        {
            if (e.CloseReason != CloseReason.UserClosing)
                return;
            e.Cancel = true;
            Hide();
        };
    }

    private Control BuildLayout()
    {
        var frame = Table(1, 3);
        frame.Padding = new Padding(16, 12, 16, 14);
        frame.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        frame.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        var header = Table(3, 1);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 37));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        _brandMark = _windowIcon.ToBitmap();
        header.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = _brandMark,
            SizeMode = PictureBoxSizeMode.CenterImage, Margin = new Padding(0, 0, 5, 5) }, 0, 0);
        header.Controls.Add(Mount(MakeLabel(17, FontStyle.Bold, "AiFortress"), new Padding(3, 0, 0, 5)), 1, 0);
        _close.Font = new Font(Theme.Typeface, 16);
        header.Controls.Add(Mount(_close, new Padding(0, 4, 0, 9)), 2, 0);
        frame.Controls.Add(header, 0, 0);

        var body = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _pages = [ScrollPage(BuildOverview()), ScrollPage(BuildLocation()), ScrollPage(BuildSettings())];
        body.Controls.AddRange(_pages);
        frame.Controls.Add(body, 0, 1);

        var navigation = Table(3, 1);
        for (var i = 0; i < _tabs.Length; i++)
        {
            var page = i;
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 1));
            navigation.Controls.Add(Mount(_tabs[i], new Padding(2)), i, 0);
            _tabs[i].Click += (_, _) => ShowPage(page);
        }
        frame.Controls.Add(Surface(navigation, new Padding(4), new Padding(0, 6, 0, 0)), 0, 2);
        return frame;
    }

    private static Panel ScrollPage(Control content)
    {
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        panel.Controls.Add(content);
        return panel;
    }

    private void ShowPage(int index)
    {
        for (var i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visible = i == index;
            _tabs[i].Selected = i == index;
            _tabs[i].ForeColor = i == index ? Theme.Text : Theme.Muted;
            _tabs[i].Invalidate();
        }
        _pages[index].BringToFront();
    }

    private Control BuildOverview()
    {
        var page = Page(232, 96, 72, 64);
        var hero = Table(1, 3);
        hero.RowStyles.Add(new RowStyle(SizeType.Absolute, 148));
        hero.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        hero.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        hero.Controls.Add(_protection, 0, 0);
        _protectionState.TextAlign = ContentAlignment.MiddleCenter;
        _summary.TextAlign = ContentAlignment.MiddleCenter;
        _summary.ForeColor = Theme.Muted;
        hero.Controls.Add(_protectionState, 0, 1);
        hero.Controls.Add(_summary, 0, 2);
        page.Controls.Add(Surface(hero, new Padding(12, 5, 12, 9)), 0, 0);

        var overview = Table(2, 1);
        overview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        overview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        overview.Controls.Add(StatusCard("PublicIp", _ip, _ipStatus, new Padding(0, 0, 4, 8)), 0, 0);
        overview.Controls.Add(StatusCard("WindowsTimeZone", _zone, _zoneStatus, new Padding(4, 0, 0, 8)), 1, 0);
        page.Controls.Add(overview, 0, 1);

        var services = Table(1, 2);
        services.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        services.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        services.Controls.Add(Caption("MonitoredServices"), 0, 0);
        var names = MakeLabel(11, FontStyle.Bold, string.Join("  ·  ", _serviceNames));
        _toolTip.SetToolTip(names, names.Text);
        services.Controls.Add(names, 0, 1);
        page.Controls.Add(Surface(services, new Padding(12, 8, 12, 8)), 0, 2);
        page.Controls.Add(Mount(_check, new Padding(0, 2, 0, 14)), 0, 3);
        return page;
    }

    private Control BuildLocation()
    {
        var page = Page(40, 72, 210, 111, 34);
        page.Controls.Add(Title("Location"), 0, 0);
        page.Controls.Add(Surface(Field("RequiredCountry", _countries), new Padding(12, 8, 12, 8)), 0, 1);

        var card = Table(1, 3);
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        card.Controls.Add(Title("AllowedTimeZones"), 0, 0);
        _allowed.Dock = DockStyle.Fill;
        _allowed.Margin = Padding.Empty;
        card.Controls.Add(new InputFrame(_allowed, new Padding(10, 8, 10, 8)), 0, 1);

        var edit = Table(2, 1);
        edit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        edit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        edit.Controls.Add(Caption("AllowedTimeZonesHint"), 0, 0);
        edit.Controls.Add(Mount(_saveZones, new Padding(4, 7, 0, 1)), 1, 0);
        card.Controls.Add(edit, 0, 2);
        page.Controls.Add(Surface(card, new Padding(12, 9, 12, 7)), 0, 2);

        var chooseZone = Table(1, 2);
        chooseZone.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
        chooseZone.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        chooseZone.Controls.Add(Field("WindowsTimeZone", _zones), 0, 0);
        chooseZone.Controls.Add(Mount(_apply, new Padding(0, 4, 0, 0)), 0, 1);
        page.Controls.Add(Surface(chooseZone, new Padding(12, 7, 12, 8)), 0, 3);
        _notice.ForeColor = Theme.Muted;
        page.Controls.Add(_notice, 0, 4);
        return page;
    }

    private Control BuildSettings()
    {
        var page = Page(40, 73, 228, 124);
        page.Controls.Add(Title("Settings"), 0, 0);
        var preferences = Table(2, 1);
        preferences.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 49));
        preferences.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 51));
        preferences.Controls.Add(Mount(Field("Language", _languages), new Padding(0, 0, 5, 0)), 0, 0);
        preferences.Controls.Add(Mount(Field("Interval", _interval), new Padding(5, 0, 0, 0)), 1, 0);
        page.Controls.Add(Surface(preferences, new Padding(12, 7, 12, 7)), 0, 1);

        var card = Table(1, 7);
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        for (var i = 0; i < 6; i++)
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 1));
        card.Controls.Add(Title("Rules"), 0, 0);
        AddToggle(1, "Protection", config => config.ProtectionEnabled, (config, value) => config.ProtectionEnabled = value);
        AddToggle(2, "CloseProcesses", config => config.KillProcesses, (config, value) => config.KillProcesses = value);
        AddToggle(3, "Firewall", config => config.BlockFirewall, (config, value) => config.BlockFirewall = value);
        AddToggle(4, "Hosts", config => config.BlockHosts, (config, value) => config.BlockHosts = value);
        AddToggle(5, "Browser", config => config.BlockBrowserPolicy, (config, value) => config.BlockBrowserPolicy = value);
        AddToggle(6, "FailClosed", config => config.FailClosed, (config, value) => config.FailClosed = value);
        page.Controls.Add(Surface(card, new Padding(12, 8, 12, 8)), 0, 2);

        var activity = Table(1, 2);
        activity.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        activity.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        activity.Controls.Add(Title("Activity"), 0, 0);
        _activity.Dock = DockStyle.Fill;
        _activity.Margin = Padding.Empty;
        activity.Controls.Add(_activity, 0, 1);
        page.Controls.Add(Surface(activity, new Padding(12, 7, 12, 9)), 0, 3);
        return page;

        void AddToggle(int row, string key, Func<AppConfig, bool> read, Action<AppConfig, bool> write)
        {
            var toggle = new FlyoutToggle { Name = key, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 1) };
            _labels.Add(toggle, key);
            _toggles.Add(toggle, read);
            toggle.CheckedChanged += (_, _) => ChangeSettings(config => write(config, toggle.Checked));
            card.Controls.Add(toggle, 0, row);
        }
    }

    private Control StatusCard(string name, Label value, Label status, Padding margin)
    {
        var card = Table(1, 3);
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        card.Controls.Add(Caption(name), 0, 0);
        card.Controls.Add(value, 0, 1);
        card.Controls.Add(status, 0, 2);
        return Surface(card, new Padding(12, 8, 12, 8), margin);
    }

    private Control Field(string label, Control editor)
    {
        var field = Table(1, 2);
        field.RowStyles.Add(new RowStyle(SizeType.Absolute, 17));
        field.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        field.Controls.Add(Caption(label), 0, 0);
        field.Controls.Add(editor, 0, 1);
        return field;
    }

    private void BindEvents()
    {
        _close.Click += (_, _) => Hide();
        _protection.Click += (_, _) => ChangeSettings(config => config.ProtectionEnabled = !config.ProtectionEnabled);
        _languages.SelectionChanged += (_, _) =>
        {
            if (_languages.Selection is { } language)
                ChangeSettings(config => config.Language = language.Id);
        };
        _countries.SelectionChanged += (_, _) =>
        {
            if (_countries.Selection is not { } country)
                return;
            ChangeSettings(config =>
            {
                config.CountryCode = country.Id;
                config.AllowedTimezones = [.. AppConfig.Countries[country.Id]];
            });
        };
        _interval.ValueChanged += (_, _) => ChangeSettings(config => config.CheckIntervalSeconds = _interval.Value);
        _allowed.TextChanged += (_, _) => SetZoneButtons();
        _zones.SelectionChanged += (_, _) => SetZoneButtons();
        _saveZones.Click += (_, _) => SaveAllowedZones();
        _check.Click += async (_, _) => await _checkNow();
        _apply.Click += async (_, _) => await ApplyTimeZoneAsync();
        _activity.DrawItem += DrawActivityEntry;
        _activity.DpiChangedAfterParent += (_, _) => _activity.ItemHeight = _activity.LogicalToDeviceUnits(18);
        _activity.MouseMove += (_, e) =>
        {
            var index = _activity.IndexFromPoint(e.Location);
            if (index == _hoveredEntry)
                return;
            _hoveredEntry = index;
            _toolTip.SetToolTip(_activity, index == ListBox.NoMatches ? "" : (string)_activity.Items[index]);
        };
    }

    private void ChangeSettings(Action<AppConfig> edit)
    {
        if (_populating)
            return;
        var next = _config.Snapshot();
        edit(next);
        if (_save(next))
        {
            if (_noticeKey == "SettingsNotSaved")
                SetNotice(null);
        }
        else
        {
            WriteSettings(refreshZones: false);
            SetNotice("SettingsNotSaved", Theme.Red);
        }
    }

    private bool HasZoneEdits => !AppConfig.ParseTimezones(_allowed.Lines)
        .SequenceEqual(_config.AllowedTimezones, StringComparer.OrdinalIgnoreCase);

    private void SaveAllowedZones()
    {
        var proposed = AppConfig.ParseTimezones(_allowed.Lines);
        var knownZones = WindowsTimeZone.Available(proposed);
        if (knownZones.Count == 0 || knownZones.Count != proposed.Count)
        {
            SetNotice("UnknownZoneIds", Theme.Red);
            _allowed.Focus();
            return;
        }

        ChangeSettings(config => config.AllowedTimezones = knownZones.Select(zone => zone.Id).ToList());
    }

    private async Task ApplyTimeZoneAsync()
    {
        if (_zones.Selection is not { } zone)
            return;

        _changingZone = true;
        SetZoneButtons();
        SetNotice("SettingTimeZone");
        try
        {
            var success = await _applyTimeZone(zone.Id);
            SetNotice(success ? "TimeZoneSet" : "TimeZoneNotSet", success ? Theme.Green : Theme.Red);
        }
        finally
        {
            _changingZone = false;
            SetZoneButtons();
        }
    }

    internal void UpdateSettings(AppConfig config)
    {
        var nextLanguage = Localization.ForLanguage(config.Language);
        var languageChanged = nextLanguage != _language;
        var zonesChanged = !_config.AllowedTimezones.SequenceEqual(config.AllowedTimezones, StringComparer.Ordinal);
        _config = config;
        if (languageChanged)
        {
            _language = nextLanguage;
            ApplyLanguage();
        }
        if (zonesChanged)
            SetNotice(null);
        WriteSettings(refreshZones: zonesChanged);
    }

    private void ApplyLanguage()
    {
        _populating = true;
        SuspendLayout();
        RightToLeft = _language.RightToLeft ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = _language.RightToLeft;
        foreach (var (control, key) in _labels)
            control.Text = _language[key];
        foreach (var tab in _tabs)
            tab.Text = _language[tab.Name];

        _close.AccessibleName = _language["Hide"];
        _toolTip.SetToolTip(_close, _language["Hide"]);
        _check.Text = _language["CheckNow"];
        _apply.Text = _language["SetTimeZone"];
        _saveZones.Text = _language["SaveTimeZones"];
        _languages.AccessibleName = _language["Language"];
        _countries.AccessibleName = _language["RequiredCountry"];
        _interval.SetLabels(_language["Interval"], _language["DecreaseInterval"], _language["IncreaseInterval"]);
        _allowed.AccessibleName = _language["AllowedTimeZones"];
        _zones.AccessibleName = _language["WindowsTimeZone"];
        _activity.AccessibleName = _language["Activity"];
        _toolTip.SetToolTip(_apply, _language["SetTimeZoneHint"]);
        _languages.SetOptions(Localization.Available.Select(language => new PickerOption(language.Code, language.Name)),
            _config.Language);
        _countries.SetOptions(AppConfig.Countries.Keys.Select(code => new PickerOption(code, _language["Country" + code])),
            _config.CountryCode);
        _notice.Text = _noticeKey is null ? "" : _language[_noticeKey];
        RefreshActivity(_entries);
        ResumeLayout(true);
        _populating = false;
    }

    private void WriteSettings(bool refreshZones)
    {
        _populating = true;
        _languages.SelectOption(_config.Language);
        _countries.SelectOption(_config.CountryCode);
        _interval.ShowValue(_config.CheckIntervalSeconds);
        foreach (var (toggle, read) in _toggles)
            toggle.Checked = read(_config);

        if (refreshZones)
        {
            _allowed.Lines = _config.AllowedTimezones.ToArray();
            _zones.SetOptions(WindowsTimeZone.Available(_config.AllowedTimezones)
                .Select(zone => new PickerOption(zone.Id, zone.Id)), _zones.Selection?.Id);
        }

        _populating = false;
        SetZoneButtons();
    }

    internal void RefreshStatus(CheckResult? result, string key, Color color, bool checking)
    {
        _checking = checking;
        _protectionState.Text = _language[_config.ProtectionEnabled ? "ProtectionActive" : "Disabled"];
        _summary.Text = _language[checking ? "Checking" : key == "Disabled" ? "ResumeProtection" : key];
        _toolTip.SetToolTip(_summary, _summary.Text);
        _protection.AccessibleName = _language[_config.ProtectionEnabled ? "PauseProtection" : "ResumeProtection"];
        _toolTip.SetToolTip(_protection, _protection.AccessibleName);
        _protection.StateColor = !_config.ProtectionEnabled ? Theme.Muted : checking || key == "Waiting" ? Theme.Accent : color;
        _protection.Invalidate();
        _check.Enabled = !checking && !_changingZone;
        _check.Text = _language[checking ? "CheckingButton" : "CheckNow"];
        _ip.Text = result is { Ip.Length: > 0 } ? $"{result.Ip}  ·  {result.Country}" : "—";
        _zone.Text = result is { TimeZone.Length: > 0 } ? result.TimeZone : "—";
        _toolTip.SetToolTip(_ip, _ip.Text);
        _toolTip.SetToolTip(_zone, _zone.Text);
        SetState(_ipStatus, result?.IpState);
        SetState(_zoneStatus, result?.TimeZoneState);
        SetZoneButtons();
    }

    private void SetState(Label label, CheckState? state)
    {
        (var key, var color) = state switch
        {
            CheckState.Ok => ("Allowed", Theme.Green),
            CheckState.Violation => ("OutsideRules", Theme.Red),
            CheckState.Unknown => ("Unavailable", Theme.Amber),
            _ => ("NotChecked", Theme.Muted)
        };
        label.Text = _language[key];
        label.ForeColor = color;
    }

    internal void RefreshActivity(IReadOnlyList<ActivityEntry> entries)
    {
        _entries = entries;
        _activity.BeginUpdate();
        _activity.Items.Clear();
        _activity.Items.AddRange(entries.Select(entry => (object)entry.Text(_language)).ToArray());
        _hoveredEntry = ListBox.NoMatches;
        _toolTip.SetToolTip(_activity, "");
        if (_activity.Items.Count > 0)
            _activity.TopIndex = _activity.Items.Count - 1;
        _activity.EndUpdate();
    }

    private void DrawActivityEntry(object? sender, DrawItemEventArgs e)
    {
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? Theme.Input : Theme.Background);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (e.Index < 0)
            return;
        var flow = _language.RightToLeft ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left;
        var bounds = Rectangle.Inflate(e.Bounds, -4, 0);
        TextRenderer.DrawText(e.Graphics, (string)_activity.Items[e.Index], _activity.Font, bounds,
            selected ? Theme.Text : Theme.Muted,
            flow | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        e.DrawFocusRectangle();
    }

    private void SetZoneButtons()
    {
        if (_populating)
            return;
        var hasEdits = HasZoneEdits;
        _saveZones.Enabled = hasEdits && !_changingZone;
        _apply.Enabled = !hasEdits && !_changingZone && _zones.Selection != null;
        _check.Enabled = !_checking && !_changingZone;
    }

    private void SetNotice(string? key, Color? color = null)
    {
        _noticeKey = key;
        _notice.Text = key is null ? "" : _language[key];
        _notice.ForeColor = color ?? Theme.Muted;
    }

    private Label Caption(string key) => LocalizedLabel(key, 9, FontStyle.Regular, Theme.Muted);
    private Label Title(string key) => LocalizedLabel(key, 11, FontStyle.Bold, Theme.Text);

    private Label LocalizedLabel(string key, float size, FontStyle style, Color color)
    {
        var label = MakeLabel(size, style);
        label.ForeColor = color;
        _labels.Add(label, key);
        return label;
    }

    private static Label MakeLabel(float size, FontStyle style, string text = "") => new()
    {
        Text = text, Font = new Font(Theme.Typeface, size, style), Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, AutoEllipsis = true
    };

    private static Button MakeButton(string key, bool primary = false) =>
        new FlyoutButton { Name = key, Dock = DockStyle.Fill, Primary = primary };

    private static TableLayoutPanel Page(params int[] heights)
    {
        var page = Table(1, heights.Length);
        page.Dock = DockStyle.Top;
        page.Height = heights.Sum();
        foreach (var height in heights)
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        return page;
    }

    private static Panel Surface(Control content, Padding padding, Padding? margin = null)
    {
        var panel = new FlyoutSurface
        {
            Dock = DockStyle.Fill, Padding = padding, Margin = margin ?? new Padding(0, 0, 0, 8)
        };
        panel.Controls.Add(content);
        return panel;
    }

    private static TableLayoutPanel Table(int columns, int rows)
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = columns, RowCount = rows, Dock = DockStyle.Fill, Margin = Padding.Empty,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        if (columns == 1)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (rows == 1)
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return table;
    }

    private static Panel Mount(Control control, Padding padding)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = padding };
        panel.Controls.Add(control);
        return panel;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindowFrame(Handle);
    }

    internal void ShowFlyout(Screen screen)
    {
        _activeScreen = screen;
        PositionFlyout();
        Show();
        PositionFlyout();
        Activate();
    }

    private void PositionFlyout()
    {
        if (_activeScreen is not { } screen)
            return;
        var size = new Size(LogicalToDeviceUnits(FlyoutSize.Width), LogicalToDeviceUnits(FlyoutSize.Height));
        Bounds = FlyoutPlacement.Calculate(screen.Bounds, screen.WorkingArea, size, LogicalToDeviceUnits(12));
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(PositionFlyout);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (ClientSize.Width <= 2 || ClientSize.Height <= 2)
            return;
        using var outline = Theme.Rounded(ClientRectangle, LogicalToDeviceUnits(18));
        Region = new Region(outline);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData != Keys.Escape || _languages.DroppedDown || _countries.DroppedDown || _zones.DroppedDown)
            return base.ProcessCmdKey(ref msg, keyData);
        Hide();
        return true;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (IsHandleCreated && !Disposing)
            BeginInvoke(() =>
            {
                if (!ContainsFocus && !_languages.DroppedDown && !_countries.DroppedDown && !_zones.DroppedDown)
                    Hide();
            });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            _windowIcon.Dispose();
            _brandMark?.Dispose();
        }
        base.Dispose(disposing);
    }
}
