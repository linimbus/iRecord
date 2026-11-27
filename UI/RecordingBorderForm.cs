// iRecord - click-through recording border.
// Tracks a provider rectangle, stays topmost, and is excluded from captures via WDA.
using System.Drawing;

namespace iRecord.UI;

/// <summary>
/// Transparent red frame around the active capture area. Used by both tray and headless recordings.
/// </summary>
public sealed class RecordingBorderForm : Form
{
    private static readonly Color KeyColor = Color.FromArgb(255, 0, 255);
    private static readonly Color BorderColor = Color.FromArgb(255, 34, 34);

    private readonly Func<Rectangle> _rectProvider;
    private readonly System.Windows.Forms.Timer _timer;

    /// <summary>
    /// Creates a transparent, non-activating frame whose provider is polled every 50 ms.
    /// Window display affinity is set on load so the frame never appears in recordings.
    /// </summary>
    public RecordingBorderForm(Func<Rectangle> rectProvider)
    {
        _rectProvider = rectProvider;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        BackColor = KeyColor;
        TransparencyKey = KeyColor;
        Size = new Size(Dpi.S(2), Dpi.S(2));

        _timer = new System.Windows.Forms.Timer { Interval = 50 };
        _timer.Tick += (_, _) => SyncBounds();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW
                          | NativeMethods.WS_EX_TRANSPARENT
                          | NativeMethods.WS_EX_LAYERED
                          | NativeMethods.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        SyncBounds();
        _timer.Start();

        try { NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE); }
        catch { }
    }

    private void SyncBounds()
    {
        var rect = _rectProvider();
        if (rect.Width < 2 || rect.Height < 2)
            return;

        if (Bounds != rect)
            Bounds = rect;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(KeyColor);

        int thickness = Math.Max(2, Dpi.S(3));
        using var pen = new Pen(BorderColor, thickness);
        g.DrawRectangle(pen, thickness / 2f, thickness / 2f,
            Width - thickness, Height - thickness);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}
