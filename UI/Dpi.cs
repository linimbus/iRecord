using System.Drawing;

namespace iRecord.UI;

internal static class Dpi
{
    public static float Scale { get; } = GetScale();

    public static int S(int value) => (int)Math.Round(value * Scale);

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
