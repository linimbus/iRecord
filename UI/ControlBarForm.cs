using System.Drawing;
using System.Drawing.Drawing2D;
using iRecord.App;

namespace iRecord.UI;

public sealed class ControlBarForm : Form
{
    private readonly Func<TimeSpan> _elapsedProvider;
    private readonly Func<(float System, float Mic)> _levelProvider;
    private readonly bool _showSystemLevel;
    private readonly bool _showMicLevel;
    private readonly Action<int>? _micVolumeChanged;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Label _dot;
    private readonly Label _time;
    private readonly Button _pause;
    private readonly Button _stop;

    private bool _paused;
    private Point _dragStartScreen;
    private Point _dragStartForm;
    private bool _dragging;
    private float _displaySystem;
    private float _displayMic;

    public event Action? PauseClicked;
    public event Action? StopClicked;

    public ControlBarForm(
        Func<TimeSpan> elapsedProvider,
        bool showSystemLevel,
        bool showMicLevel,
        Func<(float System, float Mic)> levelProvider,
        int micVolumePercent,
        Action<int>? micVolumeChanged = null)
    {
        _elapsedProvider = elapsedProvider;
        _showSystemLevel = showSystemLevel;
        _showMicLevel = showMicLevel;
        _levelProvider = levelProvider;
        _micVolumeChanged = micVolumeChanged;

        bool hasMic = _showMicLevel;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(28, 30, 34);
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.None;
        Size = new Size(Dpi.S(474), Dpi.S(hasMic ? 112 : 64));

        _dot = new Label
        {
            Text = "●",
            ForeColor = Color.FromArgb(232, 54, 51),
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = Dpi.R(10, 14, 24, 36),
            BackColor = Color.Transparent
        };

        _time = new Label
        {
            Text = "00:00:00",
            ForeColor = Color.White,
            Font = new Font("Consolas", 14f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Bounds = Dpi.R(38, 14, 110, 36),
            BackColor = Color.Transparent
        };

        _pause = new Button
        {
            Text = Localization.T("bar_pause"),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(58, 62, 70),
            Bounds = Dpi.R(290, 16, 96, 32),
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
            Bounds = Dpi.R(394, 16, 68, 32),
            TabStop = false
        };
        _stop.FlatAppearance.BorderSize = 0;
        _stop.Click += (_, _) => StopClicked?.Invoke();

        Controls.Add(_dot);
        Controls.Add(_time);
        Controls.Add(_pause);
        Controls.Add(_stop);

        if (hasMic)
        {
            var micLabel = new Label
            {
                Text = "MIC",
                ForeColor = Color.FromArgb(160, 168, 180),
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Bounds = Dpi.R(12, 74, 34, 22),
                BackColor = Color.Transparent
            };

            var slider = new TrackBar
            {
                Minimum = 0,
                Maximum = 200,
                TickFrequency = 25,
                SmallChange = 5,
                LargeChange = 25,
                Value = Math.Clamp(micVolumePercent, 0, 200),
                Bounds = Dpi.R(50, 67, 300, 34),
                TabStop = false,
                BackColor = Color.FromArgb(28, 30, 34)
            };

            var valueLabel = new Label
            {
                Text = $"{slider.Value}%",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Bounds = Dpi.R(358, 74, 90, 22),
                BackColor = Color.Transparent
            };

            slider.ValueChanged += (_, _) =>
            {
                valueLabel.Text = $"{slider.Value}%";
                _micVolumeChanged?.Invoke(slider.Value);
            };

            Controls.Add(micLabel);
            Controls.Add(slider);
            Controls.Add(valueLabel);
        }

        foreach (Control c in new Control[] { this, _dot, _time })
        {
            c.MouseDown += DragMouseDown;
            c.MouseMove += DragMouseMove;
            c.MouseUp += DragMouseUp;
        }

        _timer = new System.Windows.Forms.Timer { Interval = 60 };
        _timer.Tick += (_, _) => RefreshDynamic();
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
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int inset = Dpi.S(1);
        using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1.4f * Dpi.Scale))
        using (var path = RoundedRect(new Rectangle(inset, inset, Width - inset * 2, Height - inset * 2), Dpi.S(12)))
            g.DrawPath(pen, path);

        if (_showMicLevel)
        {
            using var separator = new Pen(Color.FromArgb(45, 255, 255, 255), 1f);
            g.DrawLine(separator, Dpi.S(12), Dpi.S(62), Width - Dpi.S(12), Dpi.S(62));
        }

        var area = Dpi.R(154, 10, 124, 44);
        if (_showSystemLevel && _showMicLevel)
        {
            int h = Dpi.S(18);
            int top = area.Y + (area.Height - h * 2 - Dpi.S(4)) / 2;
            DrawMeter(g, new Rectangle(area.X, top, area.Width, h), "SYS",
                _displaySystem, Color.FromArgb(96, 204, 128));
            DrawMeter(g, new Rectangle(area.X, top + h + Dpi.S(4), area.Width, h), "MIC",
                _displayMic, Color.FromArgb(96, 160, 255));
        }
        else if (_showSystemLevel)
        {
            int h = Dpi.S(18);
            DrawMeter(g, new Rectangle(area.X, area.Y + (area.Height - h) / 2, area.Width, h), "SYS",
                _displaySystem, Color.FromArgb(96, 204, 128));
        }
        else if (_showMicLevel)
        {
            int h = Dpi.S(18);
            DrawMeter(g, new Rectangle(area.X, area.Y + (area.Height - h) / 2, area.Width, h), "MIC",
                _displayMic, Color.FromArgb(96, 160, 255));
        }
    }

    private static void DrawMeter(Graphics g, Rectangle area, string label, float level, Color color)
    {
        int labelWidth = Dpi.S(30);
        using (var font = new Font("Segoe UI", 7.5f, FontStyle.Bold))
        using (var textBrush = new SolidBrush(Color.FromArgb(170, 255, 255, 255)))
        using (var format = new StringFormat { LineAlignment = StringAlignment.Center })
            g.DrawString(label, font, textBrush, new RectangleF(area.X, area.Y, labelWidth, area.Height), format);

        var bar = new Rectangle(area.X + labelWidth, area.Y + area.Height / 2 - Dpi.S(4),
            Math.Max(1, area.Width - labelWidth), Dpi.S(8));

        using (var background = new SolidBrush(Color.FromArgb(48, 52, 60)))
            g.FillRectangle(background, bar);

        int fill = (int)Math.Round(bar.Width * Math.Clamp(level, 0f, 1f));
        if (fill > 0)
        {
            using var foreground = new SolidBrush(color);
            g.FillRectangle(foreground, bar.X, bar.Y, fill, bar.Height);
        }

        using var border = new Pen(Color.FromArgb(70, 255, 255, 255), 1f);
        g.DrawRectangle(border, bar);
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

    private void RefreshDynamic()
    {
        UpdateTime();

        if (_showSystemLevel || _showMicLevel)
        {
            var levels = _levelProvider();
            _displaySystem = Math.Max(Math.Clamp(levels.System, 0f, 1f), _displaySystem * 0.8f);
            _displayMic = Math.Max(Math.Clamp(levels.Mic, 0f, 1f), _displayMic * 0.8f);
            Invalidate();
        }
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
