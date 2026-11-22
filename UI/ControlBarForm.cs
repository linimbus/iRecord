using System.Drawing;
using System.Drawing.Drawing2D;
using iRecord.App;

namespace iRecord.UI;

public sealed class ControlBarForm : Form
{
    private readonly Func<TimeSpan> _elapsedProvider;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Label _dot;
    private readonly Label _time;
    private readonly Button _pause;
    private readonly Button _stop;

    private bool _paused;
    private Point _dragStartScreen;
    private Point _dragStartForm;
    private bool _dragging;

    public event Action? PauseClicked;
    public event Action? StopClicked;

    public ControlBarForm(Func<TimeSpan> elapsedProvider)
    {
        _elapsedProvider = elapsedProvider;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(28, 30, 34);
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.None;
        Size = new Size(Dpi.S(380), Dpi.S(64));

        _dot = new Label
        {
            Text = "●",
            ForeColor = Color.FromArgb(232, 54, 51),
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = Dpi.R(12, 14, 26, 36),
            BackColor = Color.Transparent
        };

        _time = new Label
        {
            Text = "00:00:00",
            ForeColor = Color.White,
            Font = new Font("Consolas", 14f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Bounds = Dpi.R(42, 14, 130, 36),
            BackColor = Color.Transparent
        };

        _pause = new Button
        {
            Text = Localization.T("bar_pause"),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(58, 62, 70),
            Bounds = Dpi.R(180, 16, 110, 32),
            TabStop = false
        };
        _pause.FlatAppearance.BorderSize = 0;
        _pause.Click += (_, _) => PauseClicked?.Invoke();

        _stop = new Button
        {
            Text = Localization.T("bar_stop"),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(200, 44, 42),
            Bounds = Dpi.R(298, 16, 70, 32),
            TabStop = false
        };
        _stop.FlatAppearance.BorderSize = 0;
        _stop.Click += (_, _) => StopClicked?.Invoke();

        Controls.Add(_dot);
        Controls.Add(_time);
        Controls.Add(_pause);
        Controls.Add(_stop);

        foreach (Control c in new Control[] { this, _dot, _time })
        {
            c.MouseDown += DragMouseDown;
            c.MouseMove += DragMouseMove;
            c.MouseUp += DragMouseUp;
        }

        _timer = new System.Windows.Forms.Timer { Interval = 250 };
        _timer.Tick += (_, _) => UpdateTime();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - 48);
        ApplyRoundCorners();
        UpdateTime();
        _timer.Start();

        try { NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE); }
        catch { }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int inset = Dpi.S(1);
        using var pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1.4f * Dpi.Scale);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(new Rectangle(inset, inset, Width - inset * 2, Height - inset * 2), Dpi.S(12));
        e.Graphics.DrawPath(pen, path);
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        _pause.Text = Localization.T(paused ? "bar_resume" : "bar_pause");
        _dot.ForeColor = paused ? Color.FromArgb(240, 190, 60) : Color.FromArgb(232, 54, 51);
    }

    public void RefreshLanguage()
    {
        _pause.Text = Localization.T(_paused ? "bar_resume" : "bar_pause");
        _stop.Text = Localization.T("bar_stop");
        UpdateTime();
    }

    private void UpdateTime()
    {
        var t = _elapsedProvider();
        _time.Text = $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    private void DragMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragStartScreen = Cursor.Position;
        _dragStartForm = Location;
    }

    private void DragMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = Cursor.Position;
        Location = new Point(
            _dragStartForm.X + (p.X - _dragStartScreen.X),
            _dragStartForm.Y + (p.Y - _dragStartScreen.Y));
    }

    private void DragMouseUp(object? sender, MouseEventArgs e) => _dragging = false;

    private void ApplyRoundCorners()
    {
        using var path = RoundedRect(new Rectangle(0, 0, Width, Height), Dpi.S(12));
        Region = new Region(path);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
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
