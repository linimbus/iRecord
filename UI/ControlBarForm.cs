using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using iRecord.App;

namespace iRecord.UI;

public sealed class ControlBarForm : Form
{
    private const int MeterSegments = 10;

    private static readonly Color BackgroundColor = Color.FromArgb(26, 28, 32);
    private static readonly Color MeterLowColor = Color.FromArgb(96, 204, 128);
    private static readonly Color MeterMidColor = Color.FromArgb(240, 190, 70);
    private static readonly Color MeterHighColor = Color.FromArgb(232, 84, 80);
    private static readonly Color MeterEmptyColor = Color.FromArgb(48, 52, 60);
    private static readonly Color MicMeterColor = Color.FromArgb(86, 186, 255);
    private static readonly Color MicMeterHighColor = Color.FromArgb(150, 225, 255);
    private static readonly Color MicAccentColor = Color.FromArgb(96, 176, 255);
    private static readonly Color MicLabelColor = Color.FromArgb(130, 210, 255);

    private readonly Func<TimeSpan> _elapsedProvider;
    private readonly Func<(float System, float Mic)> _levelProvider;
    private readonly bool _showSystemLevel;
    private readonly bool _showMicLevel;
    private readonly float _systemLevelGain;
    private readonly float _micLevelGain;
    private readonly Action<int>? _micVolumeChanged;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Label _dot;
    private readonly Label _time;
    private readonly BarButton _pause;
    private readonly BarButton _stop;
    private readonly Label? _micLabel;
    private readonly SegmentSlider? _micSlider;
    private readonly BadgeLabel? _micValue;

    private Rectangle _meterBounds;
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
        int systemLevelGain,
        int micLevelGain,
        int micVolumePercent,
        Action<int>? micVolumeChanged = null)
    {
        _elapsedProvider = elapsedProvider;
        _showSystemLevel = showSystemLevel;
        _showMicLevel = showMicLevel;
        _levelProvider = levelProvider;
        _systemLevelGain = Math.Clamp(systemLevelGain, 1, 10);
        _micLevelGain = Math.Clamp(micLevelGain, 1, 10);
        _micVolumeChanged = micVolumeChanged;

        bool hasMic = _showMicLevel;
        bool hasMeter = _showSystemLevel || _showMicLevel;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = BackgroundColor;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.None;

        const int dotW = 18;
        const int timeW = 100;
        const int meterW = 190;
        const int buttonW = 84;
        const int gap = 8;
        const int groupGap = 14;
        const int padding = 16;

        int contentW = dotW + gap + timeW;
        if (hasMeter) contentW += groupGap + meterW;
        contentW += groupGap + buttonW + gap + buttonW;

        Size = new Size(Dpi.S(contentW + padding * 2), Dpi.S(hasMic ? 116 : 64));

        _dot = new Label
        {
            Text = "●",
            ForeColor = Color.FromArgb(232, 54, 51),
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        };

        _time = new Label
        {
            Text = "00:00:00",
            ForeColor = Color.White,
            Font = new Font("Consolas", 13f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };

        _pause = new BarButton
        {
            Text = Localization.T("bar_pause"),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            NormalBackColor = Color.FromArgb(58, 62, 70),
            HoverBackColor = Color.FromArgb(74, 80, 90),
            PressedBackColor = Color.FromArgb(46, 50, 58)
        };
        _pause.Click += (_, _) => PauseClicked?.Invoke();

        _stop = new BarButton
        {
            Text = Localization.T("bar_stop"),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            NormalBackColor = Color.FromArgb(198, 58, 56),
            HoverBackColor = Color.FromArgb(218, 74, 72),
            PressedBackColor = Color.FromArgb(168, 44, 42)
        };
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
                ForeColor = MicLabelColor,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };

            var micSlider = new SegmentSlider
            {
                Minimum = 0,
                Maximum = 200,
                Value = Math.Clamp(micVolumePercent, 0, 200)
            };

            var micValue = new BadgeLabel
            {
                Text = $"{micSlider.Value}%",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            micSlider.ValueChanged += (_, _) =>
            {
                micValue.Text = $"{micSlider.Value}%";
                _micVolumeChanged?.Invoke(micSlider.Value);
            };

            _micLabel = micLabel;
            _micSlider = micSlider;
            _micValue = micValue;

            Controls.Add(micLabel);
            Controls.Add(micSlider);
            Controls.Add(micValue);
        }

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
        int timeW = Dpi.S(100);
        int meterW = Dpi.S(190);
        int buttonW = Dpi.S(84);
        int buttonH = Dpi.S(32);
        int gap = Dpi.S(8);
        int groupGap = Dpi.S(14);
        bool hasMeter = _showSystemLevel || _showMicLevel;

        int contentW = dotW + gap + timeW;
        if (hasMeter) contentW += groupGap + meterW;
        contentW += groupGap + buttonW + gap + buttonW;

        int x = (ClientSize.Width - contentW) / 2;
        int topY = Dpi.S(16);

        _dot.Bounds = new Rectangle(x, topY, dotW, buttonH);
        x += dotW + gap;

        _time.Bounds = new Rectangle(x, topY, timeW, buttonH);
        x += timeW;

        if (hasMeter)
        {
            x += groupGap;
            int meterH = _showSystemLevel && _showMicLevel ? Dpi.S(44) : Dpi.S(28);
            _meterBounds = new Rectangle(x, Dpi.S(32) - meterH / 2, meterW, meterH);
            x += meterW;
        }
        else
        {
            _meterBounds = Rectangle.Empty;
        }

        x += groupGap;
        _pause.Bounds = new Rectangle(x, topY, buttonW, buttonH);
        x += buttonW + gap;
        _stop.Bounds = new Rectangle(x, topY, buttonW, buttonH);

        if (_micLabel != null && _micSlider != null && _micValue != null)
        {
            int micLabelW = Dpi.S(36);
            int micValueW = Dpi.S(56);
            int micGap = Dpi.S(8);
            int sliderW = Dpi.S(240);
            int micContentW = micLabelW + micGap + sliderW + micGap + micValueW;
            int mx = (ClientSize.Width - micContentW) / 2;
            int labelY = Dpi.S(78);
            int labelH = Dpi.S(24);

            _micLabel.Bounds = new Rectangle(mx, labelY, micLabelW, labelH);
            mx += micLabelW + micGap;
            _micSlider.Bounds = new Rectangle(mx, Dpi.S(77), sliderW, Dpi.S(26));
            mx += sliderW + micGap;
            _micValue.Bounds = new Rectangle(mx, labelY, micValueW, labelH);
        }
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
        using (var pen = new Pen(Color.FromArgb(64, 255, 255, 255), 1.2f * Dpi.Scale))
        using (var path = RoundedRect(new Rectangle(inset, inset, Width - inset * 2, Height - inset * 2), Dpi.S(12)))
            g.DrawPath(pen, path);

        if (_showMicLevel)
        {
            using var separator = new Pen(Color.FromArgb(38, 255, 255, 255), 1f);
            g.DrawLine(separator, Dpi.S(16), Dpi.S(64), Width - Dpi.S(16), Dpi.S(64));
        }

        if (_meterBounds.IsEmpty) return;

        int rowGap = Dpi.S(6);
        if (_showSystemLevel && _showMicLevel)
        {
            int rowH = (_meterBounds.Height - rowGap) / 2;
            DrawBatteryMeter(g, new Rectangle(_meterBounds.X, _meterBounds.Y, _meterBounds.Width, rowH), "SYS", _displaySystem, false);
            DrawBatteryMeter(g, new Rectangle(_meterBounds.X, _meterBounds.Y + rowH + rowGap, _meterBounds.Width, rowH), "MIC", _displayMic, true);
        }
        else if (_showSystemLevel)
        {
            DrawBatteryMeter(g, _meterBounds, "SYS", _displaySystem, false);
        }
        else if (_showMicLevel)
        {
            DrawBatteryMeter(g, _meterBounds, "MIC", _displayMic, true);
        }
    }

    private static void DrawBatteryMeter(Graphics g, Rectangle area, string label, float level, bool mic)
    {
        int labelW = Dpi.S(40);
        using (var font = new Font("Segoe UI", 8.5f, FontStyle.Bold))
        using (var textBrush = new SolidBrush(mic ? MicLabelColor : Color.FromArgb(200, 255, 255, 255)))
        using (var format = new StringFormat { LineAlignment = StringAlignment.Center })
            g.DrawString(label, font, textBrush, new RectangleF(area.X, area.Y, labelW, area.Height), format);

        int gap = Dpi.S(2);
        int barX = area.X + labelW;
        int barW = area.Width - labelW;
        int segW = (barW - gap * (MeterSegments - 1)) / MeterSegments;
        if (segW < 1) return;

        int h = Math.Min(Dpi.S(8), Math.Max(Dpi.S(4), area.Height - Dpi.S(2)));
        int top = area.Y + (area.Height - h) / 2;
        int lit = (int)Math.Ceiling(Math.Clamp(level, 0f, 1f) * MeterSegments);

        using var border = new Pen(Color.FromArgb(60, 255, 255, 255), 1f);

        for (int i = 0; i < MeterSegments; i++)
        {
            var rect = new Rectangle(barX + i * (segW + gap), top, segW, h);
            using var path = RoundedRect(rect, Dpi.S(2));
            using var fill = new SolidBrush(i < lit ? SegmentColor(i, mic) : MeterEmptyColor);
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }
    }

    private static Color SegmentColor(int index, bool mic)
    {
        if (mic) return index >= 7 ? MicMeterHighColor : MicMeterColor;
        return index switch
        {
            <= 4 => MeterLowColor,
            <= 7 => MeterMidColor,
            _ => MeterHighColor
        };
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
            _displaySystem = Math.Max(AmplifyLevel(levels.System, _systemLevelGain), _displaySystem * 0.8f);
            _displayMic = Math.Max(AmplifyLevel(levels.Mic, _micLevelGain), _displayMic * 0.8f);
            Invalidate(_meterBounds.IsEmpty ? ClientRectangle : _meterBounds);
        }
    }

    private static float AmplifyLevel(float value, float gain) => Math.Clamp(value * gain, 0f, 1f);

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

    private sealed class BadgeLabel : Control
    {
        public BadgeLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BackgroundColor;
            ForeColor = Color.White;
            TabStop = false;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var background = new SolidBrush(BackgroundColor))
                g.FillRectangle(background, ClientRectangle);

            var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using var path = RoundedRect(rect, Dpi.S(6));
            using (var fill = new SolidBrush(Color.FromArgb(42, 82, 138)))
                g.FillPath(fill, path);
            using (var border = new Pen(Color.FromArgb(150, 90, 176, 255), 1f))
                g.DrawPath(border, path);

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    private sealed class SegmentSlider : Control
    {
        private const int Cells = 10;

        private int _value = 100;
        private bool _dragging;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Minimum { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Maximum { get; set; } = 200;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => _value;
            set
            {
                int clamped = Math.Clamp(value, Minimum, Maximum);
                if (clamped == _value) return;
                _value = clamped;
                ValueChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }

        public event EventHandler? ValueChanged;

        public SegmentSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BackgroundColor;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var background = new SolidBrush(BackgroundColor))
                g.FillRectangle(background, ClientRectangle);

            int margin = Dpi.S(1);
            int gap = Dpi.S(3);
            int totalW = Width - margin * 2;
            if (totalW <= 0) return;

            int segW = (totalW - gap * (Cells - 1)) / Cells;
            if (segW < 1) return;

            int h = Math.Min(Dpi.S(6), Math.Max(Dpi.S(4), Height - Dpi.S(6)));
            int top = (Height - h) / 2;
            double fraction = (double)(Value - Minimum) / Math.Max(1, Maximum - Minimum);
            int filled = (int)Math.Round(Math.Clamp(fraction, 0d, 1d) * totalW);

            using var border = new Pen(Color.FromArgb(75, 255, 255, 255), 1f);

            for (int i = 0; i < Cells; i++)
            {
                int x = margin + i * (segW + gap);
                var rect = new Rectangle(x, top, segW, h);
                using var path = RoundedRect(rect, Dpi.S(3));

                using (var empty = new SolidBrush(MeterEmptyColor))
                    g.FillPath(empty, path);

                int overlap = Math.Clamp(filled - (x - margin), 0, segW);
                if (overlap > 0)
                {
                    var state = g.Save();
                    g.SetClip(path);
                    using (var active = new SolidBrush(MicAccentColor))
                        g.FillRectangle(active, rect.X, rect.Y, overlap, rect.Height);
                    using (var shine = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                        g.FillRectangle(shine, rect.X + Dpi.S(1), rect.Y + Dpi.S(1),
                            Math.Max(0, overlap - Dpi.S(2)), Dpi.S(2));
                    g.Restore(state);
                }

                g.DrawPath(border, path);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                Capture = true;
                SetFromX(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging) SetFromX(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            Capture = false;
            base.OnMouseUp(e);
        }

        private void SetFromX(int x)
        {
            int margin = Dpi.S(1);
            int totalW = Math.Max(1, Width - margin * 2);
            double fraction = Math.Clamp((x - margin) / (double)totalW, 0d, 1d);
            Value = (int)Math.Round(Minimum + fraction * (Maximum - Minimum));
        }
    }

    private sealed class BarButton : Button
    {
        public Color NormalBackColor = Color.FromArgb(58, 62, 70);
        public Color HoverBackColor = Color.FromArgb(74, 80, 90);
        public Color PressedBackColor = Color.FromArgb(46, 50, 58);

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

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
