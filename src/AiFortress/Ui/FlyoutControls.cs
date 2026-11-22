using System.Drawing.Drawing2D;

namespace AiFortress;

internal sealed class FlyoutSurface : Panel
{
    public FlyoutSurface()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        if (Width <= 2 || Height <= 2)
            return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), LogicalToDeviceUnits(16));
        using var fill = new SolidBrush(BackColor);
        using var border = new Pen(Theme.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }
}

internal class FlyoutButton : Button
{
    public bool Primary { get; init; }
    public bool Selected { get; set; }
    public bool Quiet { get; init; }
    protected bool Hovered { get; private set; }
    private bool _pressed;

    public FlyoutButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Margin = Padding.Empty;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 2 || Height <= 2)
            return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(1, 1, Width - 2, Height - 2);
        using var shape = Theme.Rounded(bounds, LogicalToDeviceUnits(10));
        var fillColor = !Enabled ? Theme.Surface
            : Primary ? Hovered && !_pressed ? Theme.PrimaryHover : Theme.Primary
            : (Hovered || Selected) && !_pressed ? Theme.Border : Quiet ? Color.Transparent : Theme.Input;
        using var fill = new SolidBrush(fillColor);
        e.Graphics.FillPath(fill, shape);
        if (Focused && ShowFocusCues)
        {
            using var outline = new Pen(Theme.Accent, 2);
            e.Graphics.DrawPath(outline, shape);
        }
        var flow = RightToLeft == RightToLeft.Yes ? TextFormatFlags.RightToLeft : TextFormatFlags.Default;
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.Muted,
            flow | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Hovered = false; _pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
}

internal sealed class ProtectionButton : FlyoutButton
{
    public Color StateColor { get; set; } = Theme.Accent;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Min(Width, Height);
        if (size < 12)
            return;
        var center = new PointF(Width / 2f, Height / 2f);
        var outline = Centered(size * .96f);
        using var glowPath = new GraphicsPath();
        glowPath.AddEllipse(outline);
        using var glow = new PathGradientBrush(glowPath)
        {
            CenterColor = Color.FromArgb(Hovered ? 92 : 64, StateColor),
            SurroundColors = [Color.FromArgb(0, StateColor)]
        };
        e.Graphics.FillEllipse(glow, outline);

        using var ring = new Pen(Color.FromArgb(62, StateColor));
        e.Graphics.DrawEllipse(ring, Centered(size * .91f));
        ring.Color = Color.FromArgb(110, StateColor);
        e.Graphics.DrawEllipse(ring, Centered(size * .76f));

        var core = Centered(size * .61f);
        using var disk = new LinearGradientBrush(core, Theme.Input, Theme.Background, 115f);
        e.Graphics.FillEllipse(disk, core);
        ring.Color = Color.FromArgb(190, StateColor);
        ring.Width = Focused && ShowFocusCues ? 3 : 1.4f;
        e.Graphics.DrawEllipse(ring, core);
        FortressIcon.Draw(e.Graphics, Centered(size * .32f), StateColor);

        RectangleF Centered(float diameter) => new(center.X - diameter / 2, center.Y - diameter / 2, diameter, diameter);
    }
}

internal sealed class FlyoutToggle : CheckBox
{
    private bool _hovered;

    public FlyoutToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var rtl = RightToLeft == RightToLeft.Yes;
        var width = LogicalToDeviceUnits(41);
        var height = LogicalToDeviceUnits(22);
        if (Width <= width || Height < height)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var groove = new RectangleF(rtl ? 1 : Width - width - 1, (Height - height) / 2f, width, height);
        using var shape = Theme.Rounded(groove, height / 2f);
        using var track = new SolidBrush(Checked ? _hovered ? Theme.Accent : Theme.SwitchOn : Theme.Field);
        using var edge = new Pen(Focused && ShowFocusCues ? Theme.Text : Checked ? Theme.SwitchOn : Theme.FieldBorder);
        e.Graphics.FillPath(track, shape);
        e.Graphics.DrawPath(edge, shape);
        var inset = LogicalToDeviceUnits(4);
        var diameter = height - inset * 2;
        var knobX = Checked != rtl ? groove.Right - inset - diameter : groove.Left + inset;
        using var thumb = new SolidBrush(Checked ? Theme.Text : Theme.Muted);
        e.Graphics.FillEllipse(thumb, knobX, groove.Top + inset, diameter, diameter);

        var shift = width + LogicalToDeviceUnits(10);
        var textArea = new Rectangle(rtl ? shift : 0, 0, Width - shift, Height);
        var direction = rtl ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left;
        TextRenderer.DrawText(e.Graphics, Text, Font, textArea, Enabled ? Theme.Text : Theme.Muted,
            direction | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; Invalidate(); }
}
