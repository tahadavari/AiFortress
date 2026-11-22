using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AiFortress;

internal static class Theme
{
    private const int CornerPreference = 33;
    private const int RoundCorners = 2;

    public const string Typeface = "Segoe UI";
    public static readonly Color Background = Color.FromArgb(12, 17, 29);
    public static readonly Color Surface = Color.FromArgb(19, 28, 45);
    public static readonly Color Input = Color.FromArgb(26, 38, 59);
    public static readonly Color Border = Color.FromArgb(40, 55, 82);
    public static readonly Color Text = Color.FromArgb(242, 246, 255);
    public static readonly Color Muted = Color.FromArgb(160, 176, 205);
    public static readonly Color Accent = Color.FromArgb(152, 174, 255);
    public static readonly Color Primary = Color.FromArgb(52, 81, 195);
    public static readonly Color PrimaryHover = Color.FromArgb(63, 93, 208);
    public static readonly Color Field = Color.FromArgb(14, 22, 37);
    public static readonly Color FieldHover = Color.FromArgb(22, 35, 57);
    public static readonly Color FieldBorder = Color.FromArgb(53, 71, 103);
    public static readonly Color Menu = Color.FromArgb(20, 29, 46);
    public static readonly Color SwitchOn = Color.FromArgb(101, 130, 255);
    public static readonly Color Green = Color.FromArgb(127, 229, 178);
    public static readonly Color Red = Color.FromArgb(255, 150, 167);
    public static readonly Color Amber = Color.FromArgb(255, 204, 131);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, in int value, int size);

    public static void ApplyWindowFrame(IntPtr handle)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var round = RoundCorners;
            _ = DwmSetWindowAttribute(handle, CornerPreference, in round, sizeof(int));
        }
    }

    public static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void PaintField(Graphics graphics, Size size, int radius, bool highlighted, bool hovered)
    {
        if (size.Width < 3 || size.Height < 3)
            return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var outline = Rounded(new RectangleF(.5f, .5f, size.Width - 1, size.Height - 1), radius);
        using var fill = new SolidBrush(hovered ? FieldHover : Field);
        using var stroke = new Pen(highlighted ? Accent : FieldBorder);
        graphics.FillPath(fill, outline);
        graphics.DrawPath(stroke, outline);
    }
}

internal static class FortressIcon
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Draw(graphics, new RectangleF(0, 0, 32, 32), color);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public static void Draw(Graphics graphics, RectangleF bounds, Color color)
    {
        var state = graphics.Save();
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 32, bounds.Height / 32);
        using var shape = new GraphicsPath(FillMode.Alternate);
        shape.AddPolygon(new Point[]
        {
            new(3, 5), new(9, 5), new(9, 10), new(13, 10), new(13, 5), new(19, 5),
            new(19, 10), new(23, 10), new(23, 5), new(29, 5), new(29, 22), new(16, 30), new(3, 22)
        });
        shape.AddPolygon(new Point[] { new(12, 24), new(12, 17), new(16, 14), new(20, 17), new(20, 24), new(16, 27) });
        using var fill = new SolidBrush(color);
        graphics.FillPath(fill, shape);
        graphics.Restore(state);
    }
}
