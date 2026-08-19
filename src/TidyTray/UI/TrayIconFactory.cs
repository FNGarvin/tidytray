namespace TidyTray.UI;

/// <summary>
/// Draws a tiny placeholder icon in code so the app has an identity from
/// first run without needing a checked-in .ico asset. Swap for a real
/// designed icon whenever one exists -- nothing else in the app depends on
/// this being programmatic.
/// </summary>
internal static class TrayIconFactory
{
    private static Icon? _cached;

    public static Icon CreateAppIcon()
    {
        if (_cached is not null)
            return _cached;

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var backBrush = new SolidBrush(Color.FromArgb(255, 45, 90, 160));
            g.FillEllipse(backBrush, 1, 1, 30, 30);

            using var pen = new Pen(Color.White, 2.5f);
            // Simple upward chevron/checkmark-ish glyph -- "tidied".
            g.DrawLines(pen,
            [
                new PointF(8, 17),
                new PointF(14, 23),
                new PointF(24, 9),
            ]);
        }

        // Keep the icon alive for the app's lifetime; the process teardown reclaims the native handle.
        var hIcon = bitmap.GetHicon();
        _cached = Icon.FromHandle(hIcon);
        return _cached;
    }
}
