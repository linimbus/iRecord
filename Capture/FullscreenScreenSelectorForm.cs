// iRecord - multi-monitor fullscreen picker.
// Shows a dimmed virtual-desktop snapshot and follows the mouse with a red monitor frame.
using System.Drawing;
using System.Drawing.Drawing2D;
using iRecord.UI;

namespace iRecord.Capture;

/// <summary>
/// Lets the user choose a monitor for fullscreen recording; click selects, Esc/right-click cancels.
/// </summary>
public sealed class FullscreenScreenSelectorForm : Form
{
    private static readonly Color BorderColor = Color.FromArgb(255, 34, 34);

    private readonly Rectangle _virtualScreen;
    private readonly Bitmap? _background;
    private Screen _selected;

    public Rectangle SelectedScreenBounds { get; private set; }

    public FullscreenScreenSelectorForm()
    {
        _virtualScreen = SystemInformation.VirtualScreen;
        _selected = Screen.FromPoint(Cursor.Position)
                    ?? Screen.PrimaryScreen
                    ?? Screen.AllScreens[0];

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = _virtualScreen;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        BackColor = Color.Black;
        KeyPreview = true;

        try
        {
            _background = new Bitmap(_virtualScreen.Width, _virtualScreen.Height);
            using var g = Graphics.FromImage(_background);
            g.CopyFromScreen(_virtualScreen.Left, _virtualScreen.Top, 0, 0,
                _virtualScreen.Size, CopyPixelOperation.SourceCopy);
        }
        catch
        {
            _background = null;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateSelectedScreen(Cursor.Position);
    }

    /// <summary>Renders the desktop snapshot, dims all monitors except the hovered one, and outlines it in red.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_background != null)
            g.DrawImageUnscaled(_background, 0, 0);
        else
            g.Clear(Color.FromArgb(20, 20, 20));

        var local = new Rectangle(
            _selected.Bounds.X - _virtualScreen.X,
            _selected.Bounds.Y - _virtualScreen.Y,
            _selected.Bounds.Width,
            _selected.Bounds.Height);
        local.Intersect(new Rectangle(0, 0, Width, Height));

        if (local.Width <= 0 || local.Height <= 0)
            return;

        using (var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
        {
            g.FillRectangle(dim, 0, 0, Width, local.Top);
            g.FillRectangle(dim, 0, local.Bottom, Width, Height - local.Bottom);
            g.FillRectangle(dim, 0, local.Top, local.Left, local.Height);
            g.FillRectangle(dim, local.Right, local.Top, Width - local.Right, local.Height);
        }

        int thickness = Math.Max(3, Dpi.S(4));
        using var pen = new Pen(BorderColor, thickness)
        {
            Alignment = PenAlignment.Inset
        };
        g.DrawRectangle(pen, local);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateSelectedScreen(Cursor.Position);
    }

    /// <summary>Left click confirms the monitor under the cursor; right click cancels.</summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Right)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (e.Button != MouseButtons.Left)
            return;

        _selected = Screen.FromPoint(Cursor.Position) ?? _selected;
        SelectedScreenBounds = _selected.Bounds;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    private void UpdateSelectedScreen(Point screenPoint)
    {
        var screen = Screen.FromPoint(screenPoint);
        if (screen == null ||
            string.Equals(screen.DeviceName, _selected.DeviceName, StringComparison.OrdinalIgnoreCase))
            return;

        _selected = screen;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _background?.Dispose();

        base.Dispose(disposing);
    }
}
