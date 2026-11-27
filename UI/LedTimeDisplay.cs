// iRecord - seven-segment LED time control.
// Custom-paints digits and colons without relying on installed fonts.
using System.Drawing;
using System.Drawing.Drawing2D;

namespace iRecord.UI;

/// <summary>
/// Lightweight seven-segment display used by the recording bar for elapsed time.
/// </summary>
internal sealed class LedTimeDisplay : Control
{
    private static readonly bool[][] Digits =
    {
        new[] { true, true, true, true, true, true, false },   // 0
        new[] { false, true, true, false, false, false, false }, // 1
        new[] { true, true, false, true, true, false, true },  // 2
        new[] { true, true, true, true, false, false, true },  // 3
        new[] { false, true, true, false, false, true, true }, // 4
        new[] { true, false, true, true, false, true, true },  // 5
        new[] { true, false, true, true, true, true, true },   // 6
        new[] { true, true, true, false, false, false, false },// 7
        new[] { true, true, true, true, true, true, true },    // 8
        new[] { true, true, true, true, false, true, true }    // 9
    };

    public LedTimeDisplay()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(26, 28, 32);
        ForeColor = Color.White;
        TabStop = false;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    /// <summary>Draws digits from the 7-segment table and auto-shrinks to fit the control width.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var background = new SolidBrush(BackColor))
            g.FillRectangle(background, ClientRectangle);

        string text = Text ?? string.Empty;
        if (text.Length == 0)
            return;

        int pad = Math.Max(1, Height / 16);
        int digitH = Math.Max(4, (int)Math.Round(Height * 0.76));
        int digitW = Math.Max(3, (int)Math.Round(digitH * 0.44));
        int colonW = Math.Max(2, (int)Math.Round(digitW * 0.5));
        int gap = Math.Max(1, (int)Math.Round(digitW * 0.3));
        int available = Math.Max(1, Width - pad * 2);
        int total = MeasureWidth(text, digitW, colonW, gap);

        if (total > available)
        {
            double factor = available / (double)total;
            digitW = Math.Max(2, (int)Math.Floor(digitW * factor));
            colonW = Math.Max(1, (int)Math.Floor(colonW * factor));
            gap = Math.Max(1, (int)Math.Round(gap * factor));
            total = MeasureWidth(text, digitW, colonW, gap);
            while (total > available && digitW > 2)
            {
                digitW--;
                colonW = Math.Max(1, (int)Math.Round(digitW * 0.5));
                total = MeasureWidth(text, digitW, colonW, gap);
            }
        }

        float thickness = Math.Max(1.5f, digitH / 9f);
        using var pen = new Pen(ForeColor, thickness)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var dot = new SolidBrush(ForeColor);

        int x = Math.Max(pad, (Width - total) / 2);
        int y = (Height - digitH) / 2;

        foreach (char c in text)
        {
            if (c == ':')
            {
                DrawColon(g, dot, x, colonW, y, digitH, thickness);
                x += colonW + gap;
            }
            else if (c is >= '0' and <= '9')
            {
                DrawDigit(g, pen, x, digitW, y, digitH, c - '0');
                x += digitW + gap;
            }
        }
    }

    private static int MeasureWidth(string text, int digitW, int colonW, int gap)
    {
        int total = 0;
        foreach (char c in text)
        {
            if (c == ':') total += colonW;
            else if (c is >= '0' and <= '9') total += digitW;
            total += gap;
        }
        return Math.Max(0, total - gap);
    }

    private static void DrawDigit(Graphics g, Pen pen, int x, int w, int y, int h, int digit)
    {
        float t = pen.Width;
        float left = x + t / 2;
        float right = x + w - t / 2;
        float top = y + t / 2;
        float bottom = y + h - t / 2;
        float middle = (top + bottom) / 2;
        float hInset = t * 0.8f;
        float vInset = t * 0.8f;
        var segments = Digits[Math.Clamp(digit, 0, 9)];

        if (segments[0])
            g.DrawLine(pen, left + hInset, top, right - hInset, top);
        if (segments[6])
            g.DrawLine(pen, left + hInset, middle, right - hInset, middle);
        if (segments[3])
            g.DrawLine(pen, left + hInset, bottom, right - hInset, bottom);
        if (segments[5])
            g.DrawLine(pen, left, top + vInset, left, middle - t * 0.35f);
        if (segments[1])
            g.DrawLine(pen, right, top + vInset, right, middle - t * 0.35f);
        if (segments[4])
            g.DrawLine(pen, left, middle + t * 0.35f, left, bottom - vInset);
        if (segments[2])
            g.DrawLine(pen, right, middle + t * 0.35f, right, bottom - vInset);
    }

    private static void DrawColon(Graphics g, Brush brush, int x, int w, int y, int h, float thickness)
    {
        float size = Math.Max(2f, thickness * 1.25f);
        float cx = x + w / 2f;
        float top = y + h * 0.34f;
        float bottom = y + h * 0.66f;
        g.FillEllipse(brush, cx - size / 2, top - size / 2, size, size);
        g.FillEllipse(brush, cx - size / 2, bottom - size / 2, size, size);
    }
}
