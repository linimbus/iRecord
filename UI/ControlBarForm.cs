// iRecord - on-screen recording controls.
// Shows elapsed LED time, live SYS/MIC meters, and pause/stop/hide buttons.
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using iRecord.App;

namespace iRecord.UI;

/// <summary>
/// Floating, draggable recording bar. It polls elapsed/level providers and is excluded from capture.
/// </summary>
public sealed class ControlBarForm : Form
{
    private const int MeterSegments = 5;
    private const int BlinkCycleMs = 2000;
    private const int BlinkOffMs = 1000;

    private enum BarIcon
    {
        None,
        Pause,
        Play,
        Stop,
        Close
    }

    private static readonly Color BackgroundColor = Color.FromArgb(26, 28, 32);
    private static readonly Color MeterEmptyColor = Color.FromArgb(48, 52, 60);
    private static readonly Color MicMeterColor = Color.FromArgb(86, 186, 255);
    private static readonly Color MicMeterHighColor = Color.FromArgb(150, 225, 255);

    private readonly Func<TimeSpan> _elapsedProvider;
    private readonly Func<(float System, float Mic)> _levelProvider;
    private readonly bool _showSystemLevel;
    private readonly bool _showMicLevel;
    private readonly float _systemLevelGain;
    private readonly float _micLevelGain;
    private readonly double _idleOpacity;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Label _dot;
    private readonly LedTimeDisplay _time;
    private readonly BarButton _pause;
    private readonly BarButton _stop;
    private readonly BarButton _hide;
    private readonly ToolTip _toolTip = new()
    {
        InitialDelay = 300,
        ReshowDelay = 100
    };

    private Rectangle _meterBounds;
    private bool _paused;
    private long _blinkStart;
    private Point _dragStartScreen;
    private Point _dragStartForm;
    private bool _dragging;
    private float _displaySystem;
    private float _displayMic;

    public event Action? PauseClicked;
    public event Action? StopClicked;

    /// <summary>
    /// Builds the compact bar from providers instead of owning the session. A 50 ms timer
    /// refreshes elapsed time, animated meters, blink state, and idle transparency.
    /// </summary>
    public ControlBarForm(
        Func<TimeSpan> elapsedProvider,
        bool showSystemLevel,
        bool showMicLevel,
        Func<(float System, float Mic)> levelProvider,
        int systemLevelGain,
        int micLevelGain,
        int idleOpacityPercent)
    {
        _elapsedProvider = elapsedProvider;
        _showSystemLevel = showSystemLevel;
        _showMicLevel = showMicLevel;
        _levelProvider = levelProvider;
        _systemLevelGain = Math.Clamp(systemLevelGain, 1, 10);
        _micLevelGain = Math.Clamp(micLevelGain, 1, 10);
        _idleOpacity = Math.Clamp(idleOpacityPercent, 20, 100) / 100.0;

        bool hasMeter = _showSystemLevel || _showMicLevel;
        bool bothMeters = _showSystemLevel && _showMicLevel;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = BackgroundColor;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.None;

        const int dotW = 18;
        const int timeW = 104;
        const int buttonW = 32;
        const int closeW = 32;
        const int gap = 8;
        const int timeGap = 4;
        const int groupGap = 6;
        const int buttonGap = 8;
        const int padding = 8;
        int meterW = hasMeter ? (bothMeters ? 40 : 16) : 0;

        int contentW = dotW + timeGap + timeW;
        if (hasMeter) contentW += groupGap + meterW;
        contentW += buttonGap + buttonW + gap + buttonW + gap + closeW;

        Size = new Size(Dpi.S(contentW + padding * 2), Dpi.S(buttonW + padding * 2));

        _dot = new Label
        {
            Text = "●",
            ForeColor = Color.FromArgb(232, 54, 51),
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        };

        _time = new LedTimeDisplay
        {
            Text = "00:00:00",
            ForeColor = Color.White,
            BackColor = BackgroundColor
        };

        _pause = new BarButton
        {
            Icon = BarIcon.Pause,
            NormalBackColor = Color.FromArgb(58, 62, 70),
            HoverBackColor = Color.FromArgb(74, 80, 90),
            PressedBackColor = Color.FromArgb(46, 50, 58)
        };
        _pause.Click += (_, _) => PauseClicked?.Invoke();
        _toolTip.SetToolTip(_pause, Localization.T("bar_pause"));

        _stop = new BarButton
        {
            Icon = BarIcon.Stop,
            NormalBackColor = Color.FromArgb(198, 58, 56),
            HoverBackColor = Color.FromArgb(218, 74, 72),
            PressedBackColor = Color.FromArgb(168, 44, 42)
        };
        _stop.Click += (_, _) => StopClicked?.Invoke();
        _toolTip.SetToolTip(_stop, Localization.T("bar_stop"));

        _hide = new BarButton
        {
            Icon = BarIcon.Close,
            NormalBackColor = Color.FromArgb(52, 56, 64),
            HoverBackColor = Color.FromArgb(70, 76, 86),
            PressedBackColor = Color.FromArgb(42, 46, 54)
        };
        _hide.Click += (_, _) => Hide();
        _toolTip.SetToolTip(_hide, Localization.T("bar_hide"));

        Controls.Add(_dot);
        Controls.Add(_time);
        Controls.Add(_pause);
        Controls.Add(_stop);
        Controls.Add(_hide);

        LayoutContent();

        foreach (Control c in new Control[] { this, _dot, _time })
        {
            c.MouseDown += DragMouseDown;
            c.MouseMove += DragMouseMove;
            c.MouseUp += DragMouseUp;
        }

        _timer = new System.Windows.Forms.Timer { Interval = 60 };
        _timer.Tick += (_, _) => RefreshDynamic();
    }

    private void LayoutContent()
    {
        int dotW = Dpi.S(18);
        int timeW = Dpi.S(104);
        int buttonW = Dpi.S(32);
        int closeW = Dpi.S(32);
        int buttonH = Dpi.S(32);
        int gap = Dpi.S(8);
        int timeGap = Dpi.S(4);
        int groupGap = Dpi.S(6);
        int buttonGap = Dpi.S(8);
        bool hasMeter = _showSystemLevel || _showMicLevel;
        bool bothMeters = _showSystemLevel && _showMicLevel;
        int meterW = hasMeter ? Dpi.S(bothMeters ? 40 : 16) : 0;

        int contentW = dotW + timeGap + timeW;
        if (hasMeter) contentW += groupGap + meterW;
        contentW += buttonGap + buttonW + gap + buttonW + gap + closeW;

        int x = (ClientSize.Width - contentW) / 2;
        int topY = (ClientSize.Height - buttonH) / 2;

        _dot.Bounds = new Rectangle(x, topY, dotW, buttonH);
        x += dotW + timeGap;

        _time.Bounds = new Rectangle(x, topY, timeW, buttonH);
        x += timeW;

        if (hasMeter)
        {
            x += groupGap;
            int meterH = Dpi.S(MeterSegments * 4 + (MeterSegments - 1) * 2);
            _meterBounds = new Rectangle(x, (ClientSize.Height - meterH) / 2, meterW, meterH);
            x += meterW;
        }
        else
        {
            _meterBounds = Rectangle.Empty;
        }

        x += buttonGap;
        _pause.Bounds = new Rectangle(x, topY, buttonW, buttonH);
        x += buttonW + gap;
        _stop.Bounds = new Rectangle(x, topY, buttonW, buttonH);
        x += buttonW + gap;
        _hide.Bounds = new Rectangle(x, topY, closeW, buttonH);
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
        Opacity = _idleOpacity;
        _blinkStart = Environment.TickCount64;
        UpdateBlink();
        UpdateOpacity();
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
        using (var pen = new Pen(Color.FromArgb(64, 255, 255, 255), 1.2f * Dpi.Scale))
        using (var path = RoundedRect(new Rectangle(inset, inset, Width - inset * 2, Height - inset * 2), Dpi.S(12)))
            g.DrawPath(pen, path);

        if (_meterBounds.IsEmpty) return;

        int count = (_showSystemLevel ? 1 : 0) + (_showMicLevel ? 1 : 0);
        int columnGap = Dpi.S(8);
        int columnW = (int)Math.Round((_meterBounds.Width - columnGap * (count - 1)) / (double)count);
        int x = _meterBounds.X;

        if (_showSystemLevel)
        {
            DrawVerticalMeter(g, new Rectangle(x, _meterBounds.Y, columnW, _meterBounds.Height),
                _displaySystem);
            x += columnW + columnGap;
        }

        if (_showMicLevel)
        {
            DrawVerticalMeter(g, new Rectangle(x, _meterBounds.Y, columnW, _meterBounds.Height),
                _displayMic);
        }
    }

    /// <summary>Draws the five-segment vertical level meter used for SYS and MIC.</summary>
    private static void DrawVerticalMeter(Graphics g, Rectangle area, float level)
    {
        int segH = Dpi.S(4);
        int gap = Dpi.S(2);
        int barW = Math.Max(1, Math.Min(Dpi.S(16), area.Width));
        int x = area.X + (area.Width - barW) / 2;
        int bottom = area.Bottom;
        int lit = (int)Math.Ceiling(Math.Clamp(level, 0f, 1f) * MeterSegments);

        using var border = new Pen(Color.FromArgb(60, 255, 255, 255), 1f);

        for (int i = 0; i < MeterSegments; i++)
        {
            int y = bottom - (i + 1) * segH - i * gap;
            var rect = new Rectangle(x, y, barW, segH);
            using var path = RoundedRect(rect, Dpi.S(1));
            using var fill = new SolidBrush(i < lit ? SegmentColor(i) : MeterEmptyColor);
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }
    }

    private static Color SegmentColor(int index) =>
        index >= 4 ? MicMeterHighColor : MicMeterColor;

    /// <summary>Switches pause/play icon and freezes the recording indicator.</summary>
    public void SetPaused(bool paused)
    {
        _paused = paused;
        _pause.Icon = paused ? BarIcon.Play : BarIcon.Pause;
        _toolTip.SetToolTip(_pause, Localization.T(paused ? "bar_resume" : "bar_pause"));
        _dot.ForeColor = paused ? Color.FromArgb(240, 190, 60) : Color.FromArgb(232, 54, 51);
        _dot.Visible = true;
        if (!paused)
            _blinkStart = Environment.TickCount64;
    }

    public void RefreshLanguage()
    {
        _toolTip.SetToolTip(_pause, Localization.T(_paused ? "bar_resume" : "bar_pause"));
        _toolTip.SetToolTip(_stop, Localization.T("bar_stop"));
        _toolTip.SetToolTip(_hide, Localization.T("bar_hide"));
        UpdateTime();
    }

    private void RefreshDynamic()
    {
        UpdateOpacity();
        UpdateBlink();
        UpdateTime();

        if (_showSystemLevel || _showMicLevel)
        {
            var levels = _levelProvider();
            _displaySystem = Math.Max(AmplifyLevel(levels.System, _systemLevelGain), _displaySystem * 0.8f);
            _displayMic = Math.Max(AmplifyLevel(levels.Mic, _micLevelGain), _displayMic * 0.8f);
            Invalidate(_meterBounds.IsEmpty ? ClientRectangle : _meterBounds);
        }
    }

    private static float AmplifyLevel(float value, float gain) => Math.Clamp(value * gain, 0f, 1f);

    private void UpdateBlink()
    {
        if (_paused)
        {
            if (!_dot.Visible)
                _dot.Visible = true;
            return;
        }

        long phase = (Environment.TickCount64 - _blinkStart) % BlinkCycleMs;
        bool visible = phase < BlinkCycleMs - BlinkOffMs;
        if (_dot.Visible != visible)
            _dot.Visible = visible;
    }

    private void UpdateOpacity()
    {
        if (!Visible)
            return;

        double target = Bounds.Contains(Cursor.Position) ? 1.0 : _idleOpacity;
        if (Math.Abs(Opacity - target) > 0.001)
            Opacity = target;
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
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class BarButton : Button
    {
        public Color NormalBackColor = Color.FromArgb(58, 62, 70);
        public Color HoverBackColor = Color.FromArgb(74, 80, 90);
        public Color PressedBackColor = Color.FromArgb(46, 50, 58);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public BarIcon Icon { get; set; }

        private bool _hover;
        private bool _pressed;

        public BarButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            AutoSize = false;
            BackColor = BackgroundColor;
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var background = new SolidBrush(BackgroundColor))
                g.FillRectangle(background, ClientRectangle);

            var back = _pressed ? PressedBackColor : _hover ? HoverBackColor : NormalBackColor;
            using (var path = RoundedRect(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), Dpi.S(8)))
            using (var brush = new SolidBrush(back))
                g.FillPath(brush, path);

            if (Icon == BarIcon.None)
            {
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            else
            {
                DrawIcon(g, ClientRectangle, Icon, ForeColor);
            }
        }

        private static void DrawIcon(Graphics g, Rectangle area, BarIcon icon, Color color)
        {
            using var brush = new SolidBrush(color);
            int cx = area.Left + area.Width / 2;
            int cy = area.Top + area.Height / 2;

            int boxSize = Math.Min(area.Width, area.Height);
            int outerGap = Math.Max(Dpi.S(7), boxSize / 4);
            int inner = Math.Max(Dpi.S(6), boxSize - outerGap * 2);
            var box = new Rectangle(cx - inner / 2, cy - inner / 2, inner, inner);

            switch (icon)
            {
                case BarIcon.Stop:
                    g.FillRectangle(brush, box);
                    break;
                case BarIcon.Close:
                {
                    float thickness = Math.Max(1.5f, boxSize / 14f);
                    using var pen = new Pen(color, thickness)
                    {
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round
                    };
                    float inset = thickness / 2f;
                    g.DrawLine(pen, box.Left + inset, box.Top + inset, box.Right - inset, box.Bottom - inset);
                    g.DrawLine(pen, box.Right - inset, box.Top + inset, box.Left + inset, box.Bottom - inset);
                    break;
                }
                case BarIcon.Play:
                    g.FillPolygon(brush, new[]
                    {
                        new Point(box.Left, box.Top),
                        new Point(box.Left, box.Bottom),
                        new Point(box.Right, cy)
                    });
                    break;
                case BarIcon.Pause:
                {
                    int barW = Math.Max(Dpi.S(2), (int)Math.Round(box.Width * 0.3));
                    int gap = Math.Max(Dpi.S(2), box.Width - barW * 2);
                    if (barW * 2 + gap > box.Width)
                        barW = Math.Max(1, (box.Width - gap) / 2);
                    g.FillRectangle(brush, box.Left, box.Top, barW, box.Height);
                    g.FillRectangle(brush, box.Right - barW, box.Top, barW, box.Height);
                    break;
                }
            }
        }
    }
}
