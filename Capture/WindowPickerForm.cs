using System.Drawing;
using System.Drawing.Drawing2D;
using iRecord.App;

namespace iRecord.Capture;

public readonly record struct WindowHit(IntPtr Handle, string Title, Rectangle ScreenRect);

public sealed class WindowPickerForm : Form
{
    private readonly Rectangle _virtualScreen;
    private readonly Bitmap? _background;
    private WindowHit? _hit;

    public IntPtr SelectedHandle { get; private set; }
    public string SelectedTitle { get; private set; } = string.Empty;
    public Rectangle SelectedRect { get; private set; }

    public WindowPickerForm()
    {
        _virtualScreen = SystemInformation.VirtualScreen;

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
            g.CopyFromScreen(_virtualScreen.Left, _virtualScreen.Top, 0, 0, _virtualScreen.Size,
                CopyPixelOperation.SourceCopy);
        }
        catch
        {
            _background = null;
        }
    }

    public static WindowHit? FindAt(Point screenPoint)
    {
        WindowHit? result = null;
        uint myPid = (uint)Environment.ProcessId;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (result != null) return false;
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            if (NativeMethods.IsIconic(hwnd)) return true;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == myPid) return true;

            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;

            if (NativeMethods.IsCloaked(hwnd)) return true;

            string title = NativeMethods.GetWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title)) return true;

            if (!NativeMethods.GetWindowRect(hwnd, out var r)) return true;
            var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (rect.Width < 16 || rect.Height < 16) return true;
            if (!rect.Contains(screenPoint)) return true;

            result = new WindowHit(hwnd, title, rect);
            return false;
        }, IntPtr.Zero);

        return result;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_background != null)
            g.DrawImageUnscaled(_background, 0, 0);
        else
            g.Clear(Color.FromArgb(20, 20, 20));

        using (var dim = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            g.FillRectangle(dim, ClientRectangle);

        if (_hit is { } hit)
        {
            var rect = new Rectangle(
                hit.ScreenRect.X - _virtualScreen.Left,
                hit.ScreenRect.Y - _virtualScreen.Top,
                hit.ScreenRect.Width,
                hit.ScreenRect.Height);

            using var pen = new Pen(Color.FromArgb(255, 0, 174, 255), 3);
            g.DrawRectangle(pen, rect);

            using var font = new Font("Segoe UI", 10f, FontStyle.Bold);
            string title = hit.Title;
            float maxTitleWidth = Math.Max(16, Width - 16);
            var textSize = g.MeasureString(title, font);

            if (textSize.Width > maxTitleWidth)
            {
                int lo = 0;
                int hi = title.Length;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    var candidate = title[..mid] + "…";
                    if (g.MeasureString(candidate, font).Width <= maxTitleWidth)
                        lo = mid;
                    else
                        hi = mid - 1;
                }

                title = lo > 0 ? title[..lo] + "…" : "…";
                textSize = g.MeasureString(title, font);
            }

            float tx = rect.Left;
            float ty = rect.Top - textSize.Height - 8;
            if (ty < 0) ty = rect.Top + 6;

            float maxTx = Math.Max(2, Width - textSize.Width - 8);
            tx = Math.Clamp(tx, 2, maxTx);

            using var back = new SolidBrush(Color.FromArgb(210, 0, 0, 0));
            g.FillRectangle(back, tx - 4, ty - 2, textSize.Width + 8, textSize.Height + 4);
            g.DrawString(title, font, Brushes.White, tx, ty);
        }

        using var hintFont = new Font("Segoe UI", 13f);
        string hint = Localization.T("picker_window_hint");
        var hintSize = g.MeasureString(hint, hintFont);
        float hx = (Width - hintSize.Width) / 2f;
        float hy = Math.Max(20, Height * 0.08f);
        using var hintBack = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
        g.FillRectangle(hintBack, hx - 12, hy - 8, hintSize.Width + 24, hintSize.Height + 16);
        g.DrawString(hint, hintFont, Brushes.White, hx, hy);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var screenPoint = PointToScreen(e.Location);
        var hit = FindAt(screenPoint);
        if (!Nullable.Equals(hit, _hit))
        {
            _hit = hit;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (e.Button != MouseButtons.Left || _hit is not { } hit)
            return;

        SelectedHandle = hit.Handle;
        SelectedTitle = hit.Title;
        SelectedRect = hit.ScreenRect;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _background?.Dispose();
        base.Dispose(disposing);
    }
}
