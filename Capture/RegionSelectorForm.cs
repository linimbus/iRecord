using System.Drawing;
using System.Drawing.Drawing2D;
using iRecord.App;

namespace iRecord.Capture;

public sealed class RegionSelectorForm : Form
{
    private readonly Rectangle _virtualScreen;
    private readonly Bitmap? _background;
    private Point _start;
    private Point _current;
    private bool _dragging;
    private Rectangle _selection;

    public Rectangle SelectedRegion { get; private set; }

    public RegionSelectorForm()
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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_background != null)
            g.DrawImageUnscaled(_background, 0, 0);
        else
            g.Clear(Color.FromArgb(20, 20, 20));

        var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0));

        if (_dragging || _selection.Width > 0)
        {
            var sel = _selection;
            g.FillRectangle(dim, 0, 0, Width, sel.Top);
            g.FillRectangle(dim, 0, sel.Bottom, Width, Height - sel.Bottom);
            g.FillRectangle(dim, 0, sel.Top, sel.Left, sel.Height);
            g.FillRectangle(dim, sel.Right, sel.Top, Width - sel.Right, sel.Height);

            using var pen = new Pen(Color.FromArgb(255, 0, 174, 255), 2);
            g.DrawRectangle(pen, sel);

            string text = $"{sel.Width} × {sel.Height}";
            using var font = new Font("Segoe UI", 10f, FontStyle.Bold);
            var size = g.MeasureString(text, font);
            float tx = Math.Min(sel.Left, Width - size.Width - 6);
            float ty = sel.Top - size.Height - 6;
            if (ty < 0) ty = sel.Top + 6;
            using var back = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
            g.FillRectangle(back, tx - 3, ty - 1, size.Width + 6, size.Height + 2);
            g.DrawString(text, font, Brushes.White, tx, ty);
        }
        else
        {
            g.FillRectangle(dim, ClientRectangle);
        }

        dim.Dispose();

        using var hintFont = new Font("Segoe UI", 13f, FontStyle.Regular);
        string hint = Localization.T("picker_region_hint");
        var hintSize = g.MeasureString(hint, hintFont);
        float hx = (Width - hintSize.Width) / 2f;
        float hy = Math.Max(20, Height * 0.08f);
        using var hintBack = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
        g.FillRectangle(hintBack, hx - 12, hy - 8, hintSize.Width + 24, hintSize.Height + 16);
        g.DrawString(hint, hintFont, Brushes.White, hx, hy);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        _dragging = true;
        _start = e.Location;
        _current = e.Location;
        _selection = new Rectangle(e.Location, Size.Empty);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        _current = e.Location;
        _selection = Normalize(_start, _current);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_dragging) return;
        _dragging = false;

        var rect = Normalize(_start, e.Location);
        if (rect.Width >= 8 && rect.Height >= 8)
        {
            var screenTopLeft = PointToScreen(rect.Location);
            SelectedRegion = new Rectangle(screenTopLeft, rect.Size);
            DialogResult = DialogResult.OK;
            Close();
        }
        else
        {
            _selection = Rectangle.Empty;
            Invalidate();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    private static Rectangle Normalize(Point a, Point b)
    {
        int x = Math.Min(a.X, b.X);
        int y = Math.Min(a.Y, b.Y);
        int w = Math.Abs(a.X - b.X);
        int h = Math.Abs(a.Y - b.Y);
        return new Rectangle(x, y, w, h);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _background?.Dispose();
        base.Dispose(disposing);
    }
}
