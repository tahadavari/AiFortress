namespace AiFortress;

internal static class FlyoutPlacement
{
    public static Rectangle Calculate(Rectangle display, Rectangle work, Size desired, int gap)
    {
        // Auto-hidden taskbars still get the same edge gap as a visible taskbar.
        var width = Math.Min(desired.Width, Math.Max(1, work.Width - gap * 2));
        var height = Math.Min(desired.Height, Math.Max(1, work.Height - gap * 2));
        var left = work.Left > display.Left ? work.Left + gap : work.Right - width - gap;
        var top = work.Top > display.Top ? work.Top + gap : work.Bottom - height - gap;
        return new Rectangle(left, top, width, height);
    }
}
