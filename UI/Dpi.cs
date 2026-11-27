// iRecord - process-wide DPI scaling helper.
// Use S/R when laying out WinForms controls or drawing fixed-size UI elements.
using System.Drawing;

namespace iRecord.UI;

/// <summary>
/// Cached scale factor (96 DPI = 1.0) for fixed-size layout and painting.
/// </summary>
internal static class Dpi
{
    public static float Scale { get; } = GetScale();

    /// <summary>Scales a fixed pixel size for the current DPI.</summary>
    public static int S(int value) => (int)Math.Round(value * Scale);

    /// <summary>Scales a fixed-position rectangle for the current DPI.</summary>
    public static Rectangle R(int x, int y, int width, int height)
        => new(S(x), S(y), S(width), S(height));

    private static float GetScale()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return Math.Max(1f, g.DpiX / 96f);
        }
        catch
        {
            return 1f;
        }
    }
}
