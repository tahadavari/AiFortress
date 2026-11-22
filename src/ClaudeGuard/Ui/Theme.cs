using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ClaudeGuard;

internal static class Theme
{
    private const int UseImmersiveDarkMode = 20;

    public static readonly Color Background = Color.FromArgb(16, 23, 38);
    public static readonly Color Surface = Color.FromArgb(25, 35, 54);
    public static readonly Color Input = Color.FromArgb(34, 46, 67);
    public static readonly Color Border = Color.FromArgb(53, 66, 91);
    public static readonly Color Text = Color.FromArgb(244, 246, 253);
    public static readonly Color Muted = Color.FromArgb(179, 191, 213);
    public static readonly Color Accent = Color.FromArgb(181, 165, 255);
    public static readonly Color Green = Color.FromArgb(114, 225, 179);
    public static readonly Color Red = Color.FromArgb(255, 149, 159);
    public static readonly Color Amber = Color.FromArgb(255, 203, 123);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, in int value, int size);

    public static void ApplyWindowFrame(IntPtr handle)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var enabled = 1;
            _ = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, in enabled, sizeof(int));
        }
    }
}

internal static class ShieldIcon
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
            using var brush = new SolidBrush(color);
            Point[] shield = [new(16, 2), new(28, 7), new(27, 18),
                new(23, 24), new(16, 30), new(9, 24), new(5, 18), new(4, 7)];
            graphics.FillPolygon(brush, shield);
            using var pen = new Pen(Theme.Background, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            Point[] check = [new(10, 15), new(14, 19), new(22, 11)];
            graphics.DrawLines(pen, check);
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
}
