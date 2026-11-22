using System.Drawing.Drawing2D;
using System.Globalization;

namespace AiFortress;

internal sealed record PickerOption(string Id, string Label);

internal sealed class FlyoutPicker : FlyoutButton
{
    private readonly ContextMenuStrip _menu = new()
    {
        ShowImageMargin = false, ShowCheckMargin = false, BackColor = Theme.Menu,
        ForeColor = Theme.Text, Renderer = new PickerRenderer(), DropShadowEnabled = true
    };
    private PickerOption[] _options = [];

    internal ContextMenuStrip Menu => _menu;
    public PickerOption? Selection { get; private set; }
    public bool DroppedDown => _menu.Visible;
    public event EventHandler? SelectionChanged;

    public FlyoutPicker(string name)
    {
        Name = name;
        AccessibleRole = AccessibleRole.ComboBox;
        Dock = DockStyle.Fill;
        _menu.Closed += (_, _) => Invalidate();
        _menu.SizeChanged += (_, _) =>
        {
            if (_menu.Width <= 2 || _menu.Height <= 2)
                return;
            using var outline = Theme.Rounded(_menu.ClientRectangle, LogicalToDeviceUnits(9));
            _menu.Region = new Region(outline);
        };
    }

    public void SetOptions(IEnumerable<PickerOption> options, string? selectedId)
    {
        _options = options.ToArray();
        SelectOption(selectedId);
    }

    public void SelectOption(string? id)
    {
        Selection = _options.FirstOrDefault(option => option.Id == id) ?? _options.FirstOrDefault();
        Text = Selection?.Label ?? "—";
        AccessibleDescription = Text;
        Enabled = _options.Length > 0;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.PaintField(e.Graphics, ClientSize, LogicalToDeviceUnits(10),
            DroppedDown || Focused && ShowFocusCues, Enabled && Hovered);
        var rtl = RightToLeft == RightToLeft.Yes;
        var leading = LogicalToDeviceUnits(9);
        var trailing = LogicalToDeviceUnits(32);
        var text = new Rectangle(rtl ? trailing : leading, 0, Math.Max(0, Width - trailing - leading), Height);
        var flow = rtl ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left;
        TextRenderer.DrawText(e.Graphics, Text, Font, text, Enabled ? Theme.Text : Theme.Muted,
            flow | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var x = (float)(rtl ? LogicalToDeviceUnits(15) : Width - LogicalToDeviceUnits(15));
        float y = Height / 2f;
        float offset = LogicalToDeviceUnits(4);
        using var pen = new Pen(DroppedDown ? Theme.Accent : Theme.Muted, LogicalToDeviceUnits(2))
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var delta = DroppedDown ? -offset : offset;
        PointF[] chevron = [new(x - offset, y - delta / 2), new(x, y + delta / 2), new(x + offset, y - delta / 2)];
        e.Graphics.DrawLines(pen, chevron);
    }

    protected override void OnClick(EventArgs e)
    {
        if (DroppedDown)
            _menu.Close();
        else
            OpenChoices();
        base.OnClick(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.F4 or (Keys.Alt | Keys.Down) or Keys.Down or Keys.Up)
        {
            OpenChoices();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OpenChoices()
    {
        if (DroppedDown || _options.Length == 0 || !Visible)
            return;

        var oldItems = _menu.Items.Cast<ToolStripItem>().ToArray();
        _menu.Items.Clear();
        foreach (var item in oldItems)
            item.Dispose();
        _menu.Font = Font;
        var screen = Screen.FromControl(this).WorkingArea;
        var width = Math.Min(screen.Width - LogicalToDeviceUnits(6), Math.Max(Width - LogicalToDeviceUnits(4),
            _options.Max(option => TextRenderer.MeasureText(option.Label, Font).Width) + LogicalToDeviceUnits(40)));
        _menu.RightToLeft = RightToLeft.No;
        _menu.MinimumSize = new Size(width + LogicalToDeviceUnits(4), 0);
        ToolStripMenuItem? current = null;
        foreach (var option in _options)
        {
            var item = new ToolStripMenuItem(option.Label)
            {
                Name = option.Id, AutoSize = false, Checked = option == Selection, ForeColor = Theme.Text,
                RightToLeft = RightToLeft,
                Size = new Size(width, LogicalToDeviceUnits(30))
            };
            if (item.Checked)
                current = item;
            item.Click += (_, _) =>
            {
                _menu.Close(ToolStripDropDownCloseReason.ItemClicked);
                Focus();
                if (Selection?.Id != option.Id)
                {
                    SelectOption(option.Id);
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            };
            _menu.Items.Add(item);
        }

        var anchor = RectangleToScreen(ClientRectangle);
        var preferred = _menu.GetPreferredSize(Size.Empty);
        var menuWidth = Math.Max(preferred.Width, _menu.MinimumSize.Width);
        var x = RightToLeft == RightToLeft.Yes ? anchor.Right - menuWidth : anchor.Left;
        x = Math.Clamp(x, screen.Left, Math.Max(screen.Left, screen.Right - menuWidth));
        var gap = LogicalToDeviceUnits(5);
        var y = anchor.Bottom + gap + preferred.Height <= screen.Bottom
            ? anchor.Bottom + gap : Math.Max(screen.Top, anchor.Top - gap - preferred.Height);
        _menu.Show(this, PointToClient(new Point(x, y)), ToolStripDropDownDirection.BelowRight);
        current?.Select();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _menu.Dispose();
        base.Dispose(disposing);
    }

    private sealed class PickerRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Theme.Menu);

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var edge = new Pen(Theme.FieldBorder);
            using var shape = Theme.Rounded(new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 9);
            e.Graphics.DrawPath(edge, shape);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item is not ToolStripMenuItem item || (!item.Selected && !item.Checked))
                return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var shape = Theme.Rounded(new RectangleF(3, 1, item.Width - 6, item.Height - 2), 6);
            using var fill = new SolidBrush(item.Selected ? Theme.FieldHover : Theme.Field);
            e.Graphics.FillPath(fill, shape);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var rtl = e.Item.RightToLeft == RightToLeft.Yes;
            var width = Math.Min(e.Item.Width, (e.ToolStrip?.ClientSize.Width ?? e.Item.Width) - 4);
            var selected = e.Item is ToolStripMenuItem { Checked: true };
            var area = new Rectangle(rtl ? 28 : 12, 0, Math.Max(0, width - 40), e.Item.Height);
            var color = selected ? Theme.Accent : Theme.Text;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                (rtl ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left);
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, area, color, flags);
            if (selected)
            {
                var indicator = new Rectangle(rtl ? 9 : width - 23, 0, 14, e.Item.Height);
                TextRenderer.DrawText(e.Graphics, "✓", e.TextFont, indicator, color,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            }
        }
    }
}

internal class InputFrame : Panel
{
    public InputFrame(Control editor, Padding padding)
    {
        BackColor = Theme.Field;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        Padding = padding;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Controls.Add(editor);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Surface);
        Theme.PaintField(e.Graphics, ClientSize, LogicalToDeviceUnits(10), ContainsFocus, false);
    }

    protected override void OnEnter(EventArgs e) { base.OnEnter(e); Invalidate(); }
    protected override void OnLeave(EventArgs e) { base.OnLeave(e); Invalidate(); }
}

internal sealed class FlyoutTextEditor : TextBox
{
    private const int MouseWheelMessage = 0x020A;
    private const int ScrollLinesMessage = 0x00B6;
    private int _unscrolledDelta;

    public FlyoutTextEditor()
    {
        Multiline = true;
        WordWrap = false;
        ScrollBars = ScrollBars.None;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Field;
        ForeColor = Theme.Text;
        RightToLeft = RightToLeft.No;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == MouseWheelMessage)
        {
            _unscrolledDelta += unchecked((short)(message.WParam.ToInt64() >> 16));
            var wheelUnits = SystemInformation.MouseWheelScrollDelta;
            var ticks = _unscrolledDelta / wheelUnits;
            _unscrolledDelta %= wheelUnits;
            var lines = SystemInformation.MouseWheelScrollLines;
            if (ticks != 0 && lines != 0)
            {
                if (lines < 0)
                    lines = Math.Max(1, ClientSize.Height / Font.Height - 1);
                var scroll = Message.Create(Handle, ScrollLinesMessage, IntPtr.Zero, new IntPtr(-lines * ticks));
                base.WndProc(ref scroll);
            }
            return;
        }
        base.WndProc(ref message);
    }
}

internal sealed class IntervalEditor : UserControl
{
    private readonly TextBox _number = new()
    {
        BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center, Dock = DockStyle.Fill,
        RightToLeft = RightToLeft.No, MaxLength = 4, BackColor = Theme.Field, ForeColor = Theme.Text
    };
    private readonly FlyoutButton _less = new() { Text = "−", Dock = DockStyle.Fill };
    private readonly FlyoutButton _more = new() { Text = "+", Dock = DockStyle.Fill };
    private int _value = AppConfig.MinimumInterval;
    public event EventHandler? ValueChanged;
    public int Value => _value;

    public IntervalEditor()
    {
        Name = "CheckInterval";
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 3, RowCount = 1,
            RightToLeft = RightToLeft.No, BackColor = Theme.Field
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.Controls.Add(_less, 0, 0);
        var editSlot = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(2, 4, 2, 0) };
        editSlot.Controls.Add(_number);
        grid.Controls.Add(editSlot, 1, 0);
        grid.Controls.Add(_more, 2, 0);
        Controls.Add(new InputFrame(grid, new Padding(6, 3, 6, 3)));

        _less.Click += (_, _) => Apply(ReadValue() - 1);
        _more.Click += (_, _) => Apply(ReadValue() + 1);
        _number.Leave += (_, _) => Apply(ReadValue());
        _number.KeyDown += (_, e) =>
        {
            if (e.KeyCode is not (Keys.Enter or Keys.Up or Keys.Down))
                return;
            Apply(ReadValue() + (e.KeyCode == Keys.Up ? 1 : e.KeyCode == Keys.Down ? -1 : 0));
            e.SuppressKeyPress = true;
        };
        ShowValue(_value);
    }

    public void SetLabels(string interval, string decrease, string increase)
    {
        _number.AccessibleName = interval;
        _less.AccessibleName = decrease;
        _more.AccessibleName = increase;
    }

    public void ShowValue(int value)
    {
        _value = value;
        _number.Text = value.ToString(CultureInfo.InvariantCulture);
        _less.Enabled = value > AppConfig.MinimumInterval;
        _more.Enabled = value < AppConfig.MaximumInterval;
    }

    private int ReadValue()
    {
        var text = _number.Text.Trim();
        if (text.Length == 0)
            return _value;
        var value = 0;
        foreach (var character in text)
        {
            var digit = CharUnicodeInfo.GetDecimalDigitValue(character);
            if (digit < 0)
                return _value;
            value = value * 10 + digit;
        }
        return value;
    }

    private void Apply(int input)
    {
        var previous = _value;
        ShowValue(Math.Clamp(input, AppConfig.MinimumInterval, AppConfig.MaximumInterval));
        if (previous != _value)
            ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}
