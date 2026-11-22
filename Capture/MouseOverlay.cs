using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace iRecord.Capture;

internal sealed class MouseOverlay
{
    private const double ClickDurationMs = 420;

    private readonly bool _showCursor;
    private readonly bool _showClicks;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<ClickEffect> _effects = new();

    private bool _leftDown;
    private bool _rightDown;
    private bool _middleDown;

    private sealed class ClickEffect
    {
        public Point ScreenPosition;
        public double StartMs;
        public Color Color = Color.White;
    }

    public MouseOverlay(string? mode)
    {
        string value = string.IsNullOrWhiteSpace(mode) ? "clicks" : mode.Trim().ToLowerInvariant();
        _showCursor = value != "off";
        _showClicks = value == "clicks";
    }

    public bool Enabled => _showCursor || _showClicks;

    public void Draw(Graphics graphics, Rectangle sourceRect)
    {
        if (!Enabled) return;

        if (_showClicks) PollClicks();

        if (_showCursor)
            DrawCursor(graphics, sourceRect);

        if (_showClicks && _effects.Count > 0)
            DrawEffects(graphics, sourceRect);
    }

    private void PollClicks()
    {
        double now = _clock.Elapsed.TotalMilliseconds;

        TrackButton(NativeMethods.VK_LBUTTON, ref _leftDown, now, Color.FromArgb(255, 255, 82, 82));
        TrackButton(NativeMethods.VK_RBUTTON, ref _rightDown, now, Color.FromArgb(255, 82, 150, 255));
        TrackButton(NativeMethods.VK_MBUTTON, ref _middleDown, now, Color.FromArgb(255, 90, 220, 120));

        if (_effects.Count > 0)
            _effects.RemoveAll(e => now - e.StartMs > ClickDurationMs);
    }

    private void TrackButton(int key, ref bool wasDown, double now, Color color)
    {
        bool down = (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
        if (down && !wasDown && NativeMethods.GetCursorPos(out var point))
        {
            _effects.Add(new ClickEffect
            {
                ScreenPosition = new Point(point.X, point.Y),
                StartMs = now,
                Color = color
            });
        }

        wasDown = down;
    }

    private static void DrawCursor(Graphics graphics, Rectangle sourceRect)
    {
        var info = new NativeMethods.CURSORINFO { cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>() };
        if (!NativeMethods.GetCursorInfo(ref info) ||
            (info.flags & NativeMethods.CURSOR_SHOWING) == 0 ||
            info.hCursor == IntPtr.Zero)
            return;

        int x = info.ptScreenPos.X - sourceRect.X;
        int y = info.ptScreenPos.Y - sourceRect.Y;
        if (x < -64 || y < -64 || x > sourceRect.Width + 64 || y > sourceRect.Height + 64)
            return;

        IntPtr hdc = graphics.GetHdc();
        try
        {
            NativeMethods.DrawIconEx(hdc, x, y, info.hCursor, 0, 0, 0, IntPtr.Zero,
                NativeMethods.DI_NORMAL);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
    }

    private void DrawEffects(Graphics graphics, Rectangle sourceRect)
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        var previousMode = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        try
        {
            foreach (var effect in _effects)
            {
                double progress = (now - effect.StartMs) / ClickDurationMs;
                if (progress < 0 || progress > 1) continue;

                int x = effect.ScreenPosition.X - sourceRect.X;
                int y = effect.ScreenPosition.Y - sourceRect.Y;
                int alpha = (int)(210 * (1 - progress));
                if (alpha <= 0) continue;

                int radius = 6 + (int)(20 * progress);
                using (var pen = new Pen(Color.FromArgb(alpha, effect.Color), 3f))
                    graphics.DrawEllipse(pen, x - radius, y - radius, radius * 2, radius * 2);

                int dot = 3 + (int)(3 * (1 - progress));
                using (var brush = new SolidBrush(Color.FromArgb(alpha, effect.Color)))
                    graphics.FillEllipse(brush, x - dot, y - dot, dot * 2, dot * 2);
            }
        }
        finally
        {
            graphics.SmoothingMode = previousMode;
        }
    }
}
