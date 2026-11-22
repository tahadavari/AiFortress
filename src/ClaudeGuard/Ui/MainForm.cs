namespace ClaudeGuard;

internal sealed class MainForm : Form
{
    private sealed record Option(string Id, string Text)
    {
        public override string ToString() => Text;
    }

    private AppConfig _config;
    private Localization _language;
    private readonly Func<Task> _checkNow;
    private readonly Func<AppConfig, bool> _save;
    private readonly Func<string, Task<bool>> _applyTimeZone;
    private readonly ToolTip _toolTip = new();
    private readonly Dictionary<Control, string> _labels = [];
    private readonly Dictionary<CheckBox, Func<AppConfig, bool>> _toggles = [];

    private readonly ComboBox _languages = Picker("Language");
    private readonly ComboBox _countries = Picker("Country");
    private readonly ComboBox _zones = Picker("TimeZone");
    private readonly NumericUpDown _interval = new()
    {
        Name = "CheckInterval", Minimum = AppConfig.MinimumInterval, Maximum = AppConfig.MaximumInterval,
        BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Input, ForeColor = Theme.Text
    };
    private readonly TextBox _allowed = new()
    {
        Name = "AllowedTimeZones", Multiline = true, ScrollBars = ScrollBars.Vertical, WordWrap = false,
        RightToLeft = RightToLeft.No, BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle
    };
    private readonly ListBox _activity = new()
    {
        BackColor = Theme.Background, ForeColor = Theme.Muted, BorderStyle = BorderStyle.None,
        IntegralHeight = false, HorizontalScrollbar = true, TabStop = false
    };
    private readonly Label _summary = MakeLabel(18, FontStyle.Bold);
    private readonly Label _ip = MakeLabel(14, FontStyle.Bold);
    private readonly Label _ipStatus = MakeLabel(9, FontStyle.Bold);
    private readonly Label _zone = MakeLabel(13, FontStyle.Bold);
    private readonly Label _zoneStatus = MakeLabel(9, FontStyle.Bold);
    private readonly Label _notice = MakeLabel(9, FontStyle.Regular);
    private readonly Button _check = MakeButton("CheckNow", primary: true);
    private readonly Button _apply = MakeButton("SetTimeZone");
    private readonly Button _saveZones = MakeButton("SaveTimeZones");
    private readonly Icon _windowIcon = ShieldIcon.Create(Theme.Accent);

    private IReadOnlyList<ActivityEntry> _entries = [];
    private bool _populating;
    private bool _checking;
    private bool _changingZone;
    private string? _noticeKey;

    public MainForm(AppConfig config, Func<Task> checkNow, Func<AppConfig, bool> save, Func<string, Task<bool>> applyTimeZone)
    {
        _config = config;
        _language = Localization.ForLanguage(config.Language);
        _checkNow = checkNow;
        _save = save;
        _applyTimeZone = applyTimeZone;
        Text = "ClaudeGuard";
        Icon = _windowIcon;
        ClientSize = new Size(904, 718);
        MinimumSize = new Size(815, 670);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10f);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Controls.Add(BuildLayout());
        BindEvents();
        ApplyLanguage();
        WriteSettings(refreshZones: true);
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
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var page = Table(1, 5);
        page.Dock = DockStyle.Top;
        page.Height = 718;
        page.Padding = new Padding(24, 14, 24, 20);
        foreach (var height in new[] { 56, 75, 93, 312, 110 })
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        scroll.Controls.Add(page);

        var header = Table(2, 1);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166));
        header.Controls.Add(Mount(MakeLabel(21, FontStyle.Bold, "ClaudeGuard"), new Padding(0, 0, 0, 8)), 0, 0);
        var languageRow = Table(1, 2);
        languageRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
        languageRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        languageRow.Controls.Add(Caption("Language"), 0, 0);
        languageRow.Controls.Add(_languages, 0, 1);
        header.Controls.Add(languageRow, 1, 0);
        page.Controls.Add(header, 0, 0);

        var banner = Card(2, 1);
        banner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        banner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        banner.Controls.Add(_summary, 0, 0);
        banner.Controls.Add(Mount(_check, new Padding(0, 3, 0, 3)), 1, 0);
        page.Controls.Add(banner, 0, 1);

        var overview = Table(2, 1);
        overview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        overview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        overview.Controls.Add(StatusCard("PublicIp", _ip, _ipStatus, new Padding(0, 0, 5, 10)), 0, 0);
        overview.Controls.Add(StatusCard("WindowsTimeZone", _zone, _zoneStatus, new Padding(5, 0, 0, 10)), 1, 0);
        page.Controls.Add(overview, 0, 2);

        var settings = Table(2, 1);
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        settings.Controls.Add(BuildLocationCard(), 0, 0);
        settings.Controls.Add(BuildProtectionCard(), 1, 0);
        page.Controls.Add(settings, 0, 3);

        var activity = Card(1, 2);
        activity.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        activity.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        activity.Controls.Add(Title("Activity"), 0, 0);
        _activity.Dock = DockStyle.Fill;
        activity.Controls.Add(_activity, 0, 1);
        page.Controls.Add(activity, 0, 4);
        return scroll;
    }

    private Control BuildLocationCard()
    {
        var card = Card(1, 8);
        card.Margin = new Padding(0, 0, 5, 10);
        foreach (var height in new[] { 30, 21, 32, 26, 63, 35, 38, 29 })
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        card.Controls.Add(Title("Location"), 0, 0);

        var captions = LocationColumns();
        captions.Controls.Add(Caption("RequiredCountry"), 0, 0);
        captions.Controls.Add(Caption("Interval"), 1, 0);
        card.Controls.Add(captions, 0, 1);

        var inputs = LocationColumns();
        inputs.Controls.Add(_countries, 0, 0);
        _interval.Dock = DockStyle.Top;
        inputs.Controls.Add(_interval, 1, 0);
        card.Controls.Add(inputs, 0, 2);
        card.Controls.Add(Caption("AllowedTimeZones"), 0, 3);
        _allowed.Dock = DockStyle.Fill;
        card.Controls.Add(_allowed, 0, 4);

        var edit = Table(2, 1);
        edit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        edit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        edit.Controls.Add(Caption("AllowedTimeZonesHint"), 0, 0);
        edit.Controls.Add(Mount(_saveZones, new Padding(3, 3, 0, 3)), 1, 0);
        card.Controls.Add(edit, 0, 5);

        var chooseZone = Table(2, 1);
        chooseZone.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        chooseZone.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        chooseZone.Controls.Add(Mount(_zones, new Padding(0, 5, 6, 0)), 0, 0);
        chooseZone.Controls.Add(Mount(_apply, new Padding(0, 2, 0, 3)), 1, 0);
        card.Controls.Add(chooseZone, 0, 6);
        _notice.ForeColor = Theme.Muted;
        card.Controls.Add(_notice, 0, 7);
        return card;
    }

    private Control BuildProtectionCard()
    {
        var card = Card(1, 8);
        card.Margin = new Padding(5, 0, 0, 10);
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        for (var i = 0; i < 5; i++)
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(Title("Rules"), 0, 0);
        AddToggle(1, "Protection", config => config.ProtectionEnabled, (config, value) => config.ProtectionEnabled = value);
        AddToggle(2, "CloseProcesses", config => config.KillProcesses, (config, value) => config.KillProcesses = value);
        AddToggle(3, "Firewall", config => config.BlockFirewall, (config, value) => config.BlockFirewall = value);
        AddToggle(4, "Hosts", config => config.BlockHosts, (config, value) => config.BlockHosts = value);
        AddToggle(5, "Browser", config => config.BlockBrowserPolicy, (config, value) => config.BlockBrowserPolicy = value);
        AddToggle(6, "FailClosed", config => config.FailClosed, (config, value) => config.FailClosed = value);
        return card;

        void AddToggle(int row, string key, Func<AppConfig, bool> read, Action<AppConfig, bool> write)
        {
            var toggle = new CheckBox { Name = key, Dock = DockStyle.Fill, AutoEllipsis = true, Margin = new Padding(0, 0, 0, 1) };
            toggle.FlatStyle = FlatStyle.Flat;
            toggle.FlatAppearance.BorderColor = Theme.Border;
            toggle.FlatAppearance.CheckedBackColor = Theme.Input;
            _labels.Add(toggle, key);
            _toggles.Add(toggle, read);
            toggle.CheckedChanged += (_, _) => ChangeSettings(config => write(config, toggle.Checked));
            card.Controls.Add(toggle, 0, row);
        }
    }

    private Control StatusCard(string name, Label value, Label status, Padding margin)
    {
        var card = Card(1, 3);
        card.Margin = margin;
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        card.Controls.Add(Caption(name), 0, 0);
        card.Controls.Add(value, 0, 1);
        card.Controls.Add(status, 0, 2);
        return card;
    }

    private void BindEvents()
    {
        _languages.SelectedIndexChanged += (_, _) =>
        {
            if (_languages.SelectedItem is Option language)
                ChangeSettings(config => config.Language = language.Id);
        };
        _countries.SelectedIndexChanged += (_, _) =>
        {
            if (_countries.SelectedItem is not Option country)
                return;
            ChangeSettings(config =>
            {
                config.CountryCode = country.Id;
                config.AllowedTimezones = [.. AppConfig.Countries[country.Id]];
            });
        };
        _interval.ValueChanged += (_, _) => ChangeSettings(config => config.CheckIntervalSeconds = (int)_interval.Value);
        _allowed.TextChanged += (_, _) => SetZoneButtons();
        _zones.SelectedIndexChanged += (_, _) => SetZoneButtons();
        _saveZones.Click += (_, _) => SaveAllowedZones();
        _check.Click += async (_, _) => await _checkNow();
        _apply.Click += async (_, _) => await ApplyTimeZoneAsync();
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
        if (_zones.SelectedItem is not Option zone)
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

        _check.Text = _language["CheckNow"];
        _apply.Text = _language["SetTimeZone"];
        _saveZones.Text = _language["SaveTimeZones"];
        _languages.AccessibleName = _language["Language"];
        _countries.AccessibleName = _language["RequiredCountry"];
        _interval.AccessibleName = _language["Interval"];
        _allowed.AccessibleName = _language["AllowedTimeZones"];
        _zones.AccessibleName = _language["WindowsTimeZone"];
        _activity.AccessibleName = _language["Activity"];
        _toolTip.SetToolTip(_apply, _language["SetTimeZoneHint"]);
        _languages.DataSource = Localization.Available.Select(language => new Option(language.Code, language.Name)).ToArray();
        _countries.DataSource = AppConfig.Countries.Keys.Select(code => new Option(code, _language["Country" + code])).ToArray();
        _notice.Text = _noticeKey is null ? "" : _language[_noticeKey];
        RefreshActivity(_entries);
        ResumeLayout(true);
        _populating = false;
    }

    private void WriteSettings(bool refreshZones)
    {
        _populating = true;
        Select(_languages, _config.Language);
        Select(_countries, _config.CountryCode);
        _interval.Value = _config.CheckIntervalSeconds;
        foreach (var (toggle, read) in _toggles)
            toggle.Checked = read(_config);

        if (refreshZones)
        {
            _allowed.Lines = _config.AllowedTimezones.ToArray();
            var selected = (_zones.SelectedItem as Option)?.Id;
            _zones.DataSource = WindowsTimeZone.Available(_config.AllowedTimezones)
                .Select(zone => new Option(zone.Id, zone.Id)).ToArray();
            Select(_zones, selected);
        }

        _populating = false;
        SetZoneButtons();
    }

    private static void Select(ComboBox combo, string? id)
    {
        var items = (Option[])combo.DataSource!;
        var index = Array.FindIndex(items, item => item.Id == id);
        combo.SelectedIndex = index >= 0 ? index : items.Length > 0 ? 0 : -1;
    }

    internal void RefreshStatus(CheckResult? result, string key, Color color, bool checking)
    {
        _checking = checking;
        _summary.Text = _language[checking ? "Checking" : key];
        _summary.ForeColor = checking ? Theme.Accent : color;
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
        if (_activity.Items.Count > 0)
            _activity.TopIndex = _activity.Items.Count - 1;
        _activity.EndUpdate();
    }

    private void SetZoneButtons()
    {
        if (_populating)
            return;
        var hasEdits = HasZoneEdits;
        _saveZones.Enabled = hasEdits && !_changingZone;
        _apply.Enabled = !hasEdits && !_changingZone && _zones.SelectedItem != null;
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
        Text = text, Font = new Font("Segoe UI", size, style), Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, AutoEllipsis = true
    };

    private static Button MakeButton(string key, bool primary = false)
    {
        var button = new Button
        {
            Name = key, Dock = DockStyle.Fill, Margin = Padding.Empty, FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false, BackColor = primary ? Theme.Accent : Theme.Input,
            ForeColor = primary ? Theme.Background : Theme.Text
        };
        button.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(202, 191, 255) : Theme.Border;
        return button;
    }

    private static ComboBox Picker(string name)
    {
        var picker = new ComboBox
        {
            Name = name, Dock = DockStyle.Fill, Margin = Padding.Empty, DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Theme.Input, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, IntegralHeight = false,
            DrawMode = DrawMode.OwnerDrawFixed, DropDownHeight = 220, ItemHeight = 19
        };
        picker.DrawItem += (_, e) =>
        {
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var background = new SolidBrush(selected ? Theme.Border : Theme.Input);
            e.Graphics.FillRectangle(background, e.Bounds);
            if (e.Index < 0)
                return;

            var bounds = Rectangle.Inflate(e.Bounds, -4, 0);
            var direction = picker.RightToLeft == RightToLeft.Yes
                ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left;
            TextRenderer.DrawText(e.Graphics, picker.GetItemText(picker.Items[e.Index]), picker.Font, bounds,
                Theme.Text, direction | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        };
        return picker;
    }

    private static TableLayoutPanel LocationColumns()
    {
        var row = Table(2, 1);
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        return row;
    }

    private static TableLayoutPanel Card(int columns, int rows)
    {
        var card = Table(columns, rows);
        card.Padding = new Padding(15, 10, 15, 10);
        card.Margin = new Padding(0, 0, 0, 10);
        card.BackColor = Theme.Surface;
        return card;
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            _windowIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
