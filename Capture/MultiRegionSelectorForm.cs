// iRecord - multi-region screenshot picker.
// Collects several screen rectangles (numbered) and merges them vertically into one image.
using System.Drawing;
using System.Drawing.Drawing2D;
using iRecord.App;
using iRecord.UI;

namespace iRecord.Capture;

/// <summary>
/// Drag multiple regions, undo the last one with Backspace/Delete, confirm with Enter/Space.
/// </summary>
public sealed class MultiRegionSelectorForm : Form
{
    private readonly Rectangle _virtualScreen;
    private readonly Bitmap? _background;
    private readonly List<Rectangle> _regions = new();

    private Point _start;
    private Point _current;
    private bool _dragging;

    public MultiRegionSelectorForm()
    {
        _virtualScreen = SystemInformation.VirtualScreen;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = _virtualScreen;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        BackColor = Color.Black;
        KeyPreview = true;

        try
        {
            _background = new Bitmap(_virtualScreen.Width, _virtualScreen.Height);
            using var g = Graphics.FromImage(_background);
            g.CopyFromScreen(_virtualScreen.Left, _virtualScreen.Top, 0, 0, _virtualScreen.Size,
                CopyPixelOperation.SourceCopy);
        }
        catch
        {
            _background = null;
        }
    }

    /// <summary>Clips the numbered regions to the virtual screen and merges them into one tall image.</summary>
    public Bitmap CreateMergedImage()
    {
        if (_background == null)
            throw new InvalidOperationException("Screen capture is not available.");

        var bitmapRegions = _regions
            .Select(r => new Rectangle(
                r.X - _virtualScreen.Left,
                r.Y - _virtualScreen.Top,
                r.Width,
                r.Height))
            .ToList();

        return ScreenshotService.MergeRegions(_background, bitmapRegions, Dpi.S(8));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_background != null)
            g.DrawImageUnscaled(_background, 0, 0);
        else
            g.Clear(Color.FromArgb(20, 20, 20));

        using (var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            g.FillRectangle(dim, ClientRectangle);

        for (int i = 0; i < _regions.Count; i++)
            DrawRegion(g, _regions[i], i + 1);

        if (_dragging)
        {
            var rect = Normalize(_start, _current);
            rect.Intersect(_virtualScreen);
            if (rect.Width > 0 && rect.Height > 0)
                DrawRegion(g, rect, 0, dashed: true);
        }

        DrawHint(g);
    }

    private void DrawRegion(Graphics g, Rectangle screenRect, int index, bool dashed = false)
    {
        var rect = new Rectangle(
            screenRect.X - _virtualScreen.Left,
            screenRect.Y - _virtualScreen.Top,
            screenRect.Width,
            screenRect.Height);

        if (_background != null && rect.Width > 0 && rect.Height > 0)
            g.DrawImage(_background, rect, rect, GraphicsUnit.Pixel);

        using var pen = new Pen(Color.FromArgb(255, 0, 174, 255), dashed ? 1.5f : 2f);
        if (dashed)
            pen.DashStyle = DashStyle.Dash;
        g.DrawRectangle(pen, rect);

        if (index > 0)
        {
            int badge = Dpi.S(20);
            var badgeRect = new Rectangle(rect.Left + Dpi.S(4), rect.Top + Dpi.S(4), badge, badge);
            using var brush = new SolidBrush(Color.FromArgb(230, 0, 150, 220));
            g.FillEllipse(brush, badgeRect);

            using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(index.ToString(), font, Brushes.White, badgeRect, format);
        }
        else
        {
            using var font = new Font("Segoe UI", 10f, FontStyle.Bold);
            string size = $"{screenRect.Width} × {screenRect.Height}";
            var textSize = g.MeasureString(size, font);
            float tx = rect.Left;
            float ty = rect.Top - textSize.Height - Dpi.S(6);
            if (ty < 0) ty = rect.Bottom + Dpi.S(6);

            using var back = new SolidBrush(Color.FromArgb(210, 0, 0, 0));
            g.FillRectangle(back, tx - Dpi.S(4), ty - Dpi.S(2), textSize.Width + Dpi.S(8), textSize.Height + Dpi.S(4));
            g.DrawString(size, font, Brushes.White, tx, ty);
        }
    }

    private void DrawHint(Graphics g)
    {
        using var font = new Font("Segoe UI", 13f);
        string hint = Localization.Format("picker_region_multi_hint", _regions.Count);
        var size = g.MeasureString(hint, font);
        float x = (Width - size.Width) / 2f;
        float y = Math.Max(Dpi.S(20), Height * 0.08f);

        using var back = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
        g.FillRectangle(back, x - Dpi.S(14), y - Dpi.S(8), size.Width + Dpi.S(28), size.Height + Dpi.S(16));
        g.DrawString(hint, font, Brushes.White, x, y);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (e.Button != MouseButtons.Left)
            return;

        _dragging = true;
        _start = PointToScreen(e.Location);
        _current = _start;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging)
            return;

        _current = PointToScreen(e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_dragging)
            return;

        _dragging = false;
        var rect = Normalize(_start, _current);
        rect.Intersect(_virtualScreen);
        if (rect.Width >= 8 && rect.Height >= 8)
            _regions.Add(rect);

        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (e.KeyCode is Keys.Back or Keys.Delete)
        {
            if (_regions.Count > 0)
                _regions.RemoveAt(_regions.Count - 1);
            Invalidate();
            return;
        }

        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            if (_regions.Count > 0)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }

    private static Rectangle Normalize(Point a, Point b) => Rectangle.FromLTRB(
        Math.Min(a.X, b.X),
        Math.Min(a.Y, b.Y),
        Math.Max(a.X, b.X),
        Math.Max(a.Y, b.Y));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _background?.Dispose();
        base.Dispose(disposing);
    }
}
