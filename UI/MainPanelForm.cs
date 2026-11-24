using System.Drawing;
using System.Drawing.Drawing2D;
using iRecord.App;

namespace iRecord.UI;

public sealed class MainPanelForm : Form
{
    private static readonly Color PanelColor = Color.FromArgb(26, 28, 32);
    private static readonly Color ButtonHoverColor = Color.FromArgb(54, 60, 70);
    private static readonly Color ButtonPressedColor = Color.FromArgb(30, 34, 40);
    private static readonly Color BorderColor = Color.FromArgb(64, 255, 255, 255);
    private static readonly Color IconColor = Color.FromArgb(224, 232, 242);
    private static readonly Color IconHoverColor = Color.FromArgb(120, 190, 255);
    private static readonly Color DisabledColor = Color.FromArgb(112, 120, 132);

    private readonly IconButton _pin;
    private readonly IconButton _screenshotRegion;
    private readonly IconButton _screenshotWindow;
    private readonly IconButton _screenshotScroll;
    private readonly IconButton _fullscreen;
    private readonly IconButton _region;
    private readonly IconButton _window;
    private readonly IconButton _settings;
    private readonly CloseButton _close;
    private readonly ToolTip _toolTip = new()
    {
        AutoPopDelay = 6000,
        InitialDelay = 300,
        ReshowDelay = 100,
        ShowAlways = true
    };

    private readonly List<int> _dividerXs = new();
    private bool _pinned;

    private Point _dragStartScreen;
    private Point _dragStartForm;
    private bool _dragging;

    public event Action? ScreenshotRegionRequested;
    public event Action? ScreenshotWindowRequested;
    public event Action? ScreenshotScrollRequested;
    public event Action? FullscreenRequested;
    public event Action? RegionRequested;
    public event Action? WindowRequested;
    public event Action? SettingsRequested;

    public MainPanelForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        BackColor = PanelColor;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        Size = new Size(Dpi.S(328), Dpi.S(56));

        _pin = CreateButton(IconKind.Pin, "panel_pin");
        _screenshotRegion = CreateButton(IconKind.ScreenshotRegion, "screenshot_region");
        _screenshotWindow = CreateButton(IconKind.ScreenshotWindow, "screenshot_window");
        _screenshotScroll = CreateButton(IconKind.ScreenshotScroll, "screenshot_scroll");
        _fullscreen = CreateButton(IconKind.FullScreen, "panel_fullscreen");
        _region = CreateButton(IconKind.Region, "panel_region");
        _window = CreateButton(IconKind.Window, "panel_window");
        _settings = CreateButton(IconKind.Settings, "panel_settings");

        _pin.Click += (_, _) => TogglePinned();
        _screenshotRegion.Click += (_, _) => ScreenshotRegionRequested?.Invoke();
        _screenshotWindow.Click += (_, _) => ScreenshotWindowRequested?.Invoke();
        _screenshotScroll.Click += (_, _) => ScreenshotScrollRequested?.Invoke();
        _fullscreen.Click += (_, _) => FullscreenRequested?.Invoke();
        _region.Click += (_, _) => RegionRequested?.Invoke();
        _window.Click += (_, _) => WindowRequested?.Invoke();
        _settings.Click += (_, _) => SettingsRequested?.Invoke();

        _close = new CloseButton();
        _close.Click += (_, _) => Hide();

        _toolTip.SetToolTip(_pin, Localization.T("panel_pin"));
        _toolTip.SetToolTip(_screenshotRegion, Localization.T("screenshot_region"));
        _toolTip.SetToolTip(_screenshotWindow, Localization.T("screenshot_window"));
        _toolTip.SetToolTip(_screenshotScroll, Localization.T("screenshot_scroll"));
        _toolTip.SetToolTip(_fullscreen, Localization.Format("panel_record_tip", Localization.T("panel_fullscreen")));
        _toolTip.SetToolTip(_region, Localization.Format("panel_record_tip", Localization.T("panel_region")));
        _toolTip.SetToolTip(_window, Localization.Format("panel_record_tip", Localization.T("panel_window")));
        _toolTip.SetToolTip(_settings, Localization.T("panel_settings"));
        _toolTip.SetToolTip(_close, Localization.T("panel_close"));

        Controls.AddRange(new Control[]
        {
            _pin, _screenshotRegion, _screenshotWindow, _screenshotScroll,
            _fullscreen, _region, _window, _settings, _close
        });

        LayoutButtons();

        MouseDown += DragMouseDown;
        MouseMove += DragMouseMove;
        MouseUp += DragMouseUp;
    }

    private IconButton CreateButton(IconKind icon, string labelKey) => new(icon)
    {
        Text = Localization.T(labelKey)
    };

    private void LayoutButtons()
    {
        int size = Dpi.S(20);
        int gap = Dpi.S(8);
        int dividerGap = Dpi.S(10);
        int padding = Dpi.S(16);
        int y = (ClientSize.Height - size) / 2;

        _dividerXs.Clear();
        int x = padding;

        void Place(Control control)
        {
            control.Bounds = new Rectangle(x, y, size, size);
            x += size + gap;
        }

        int AddDivider()
        {
            x -= gap;
            int dividerX = x + dividerGap;
            _dividerXs.Add(dividerX);
            x = dividerX + Dpi.S(1) + dividerGap;
            return dividerX;
        }

        Place(_pin);
        AddDivider();

        Place(_screenshotRegion);
        Place(_screenshotWindow);
        Place(_screenshotScroll);
        AddDivider();

        Place(_fullscreen);
        Place(_region);
        Place(_window);
        AddDivider();

        Place(_settings);
        AddDivider();

        _close.Bounds = new Rectangle(x, y, size, size);
    }

    private void TogglePinned()
    {
        _pinned = !_pinned;
        TopMost = _pinned;
        _pin.SetActive(_pinned);
    }

    public void RefreshLanguage()
    {
        _pin.Text = Localization.T("panel_pin");
        _screenshotRegion.Text = Localization.T("screenshot_region");
        _screenshotWindow.Text = Localization.T("screenshot_window");
        _screenshotScroll.Text = Localization.T("screenshot_scroll");
        _fullscreen.Text = Localization.T("panel_fullscreen");
        _region.Text = Localization.T("panel_region");
        _window.Text = Localization.T("panel_window");
        _settings.Text = Localization.T("panel_settings");

        _toolTip.SetToolTip(_pin, Localization.T("panel_pin"));
        _toolTip.SetToolTip(_screenshotRegion, Localization.T("screenshot_region"));
        _toolTip.SetToolTip(_screenshotWindow, Localization.T("screenshot_window"));
        _toolTip.SetToolTip(_screenshotScroll, Localization.T("screenshot_scroll"));
        _toolTip.SetToolTip(_fullscreen, Localization.Format("panel_record_tip", Localization.T("panel_fullscreen")));
        _toolTip.SetToolTip(_region, Localization.Format("panel_record_tip", Localization.T("panel_region")));
        _toolTip.SetToolTip(_window, Localization.Format("panel_record_tip", Localization.T("panel_window")));
        _toolTip.SetToolTip(_settings, Localization.T("panel_settings"));
        _toolTip.SetToolTip(_close, Localization.T("panel_close"));
    }

    public void SetRecording(bool recording)
    {
        _screenshotRegion.Enabled = !recording;
        _screenshotWindow.Enabled = !recording;
        _screenshotScroll.Enabled = !recording;
        _fullscreen.Enabled = !recording;
        _region.Enabled = !recording;
        _window.Enabled = !recording;
        _settings.Enabled = !recording;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyRoundCorners();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int inset = Dpi.S(1);
        using (var pen = new Pen(BorderColor, 1.2f * Dpi.Scale))
        using (var path = RoundedRect(new Rectangle(inset, inset, Width - inset * 2, Height - inset * 2), Dpi.S(12)))
            g.DrawPath(pen, path);

        if (_dividerXs.Count > 0)
        {
            using var divider = new Pen(Color.FromArgb(55, 255, 255, 255), 1.2f * Dpi.Scale);
            foreach (int dividerX in _dividerXs)
                g.DrawLine(divider, dividerX, Dpi.S(16), dividerX, Height - Dpi.S(16));
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Hide();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _toolTip.Dispose();
        base.Dispose(disposing);
    }

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

    private static void DrawIcon(Graphics g, Rectangle r, IconKind kind, Color color, Color background, bool active)
    {
        using var pen = new Pen(color, 2f * Dpi.Scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        switch (kind)
        {
            case IconKind.Pin:
            {
                var state = g.Save();
                g.TranslateTransform(r.Left + r.Width / 2f, r.Top + r.Height / 2f);
                g.RotateTransform(45f);

                int top = -Dpi.S(8);
                int neck = -Dpi.S(2);
                int needleEnd = Dpi.S(8);

                var head = new[]
                {
                    new PointF(-Dpi.S(5), top),
                    new PointF(Dpi.S(5), top),
                    new PointF(Dpi.S(2), neck),
                    new PointF(-Dpi.S(2), neck)
                };

                if (active)
                {
                    using var brush = new SolidBrush(color);
                    g.FillPolygon(brush, head);
                }
                else
                {
                    g.DrawLine(pen, head[0].X, head[0].Y, head[1].X, head[1].Y);
                    g.DrawLine(pen, -Dpi.S(4), top, -Dpi.S(2), neck);
                    g.DrawLine(pen, Dpi.S(4), top, Dpi.S(2), neck);
                    g.DrawLine(pen, -Dpi.S(2), neck, Dpi.S(2), neck);
                }

                using (var needlePen = new Pen(color, active ? 1.9f * Dpi.Scale : 1.4f * Dpi.Scale)
                       {
                           StartCap = LineCap.Round,
                           EndCap = LineCap.Round
                       })
                    g.DrawLine(needlePen, 0, neck, 0, needleEnd);

                g.Restore(state);
                break;
            }
            case IconKind.ScreenshotRegion:
            {
                var b = Rectangle.Inflate(r, -Dpi.S(2), -Dpi.S(2));
                using (var dash = new Pen(color, 1.6f * Dpi.Scale)
                       {
                           DashStyle = DashStyle.Dash,
                           DashPattern = new[] { 2.5f, 2f }
                       })
                    g.DrawRectangle(dash, b);

                int d = Dpi.S(7);
                int cx = r.Left + r.Width / 2;
                int cy = r.Top + r.Height / 2;
                g.DrawEllipse(pen, cx - d / 2, cy - d / 2, d, d);

                int dot = Math.Max(1, Dpi.S(2));
                using var brush = new SolidBrush(color);
                g.FillEllipse(brush, cx - dot / 2, cy - dot / 2, dot, dot);
                break;
            }
            case IconKind.ScreenshotWindow:
            {
                var body = new Rectangle(r.Left + Dpi.S(2), r.Top + Dpi.S(6), r.Width - Dpi.S(4), r.Height - Dpi.S(10));
                using (var path = RoundedRect(body, Dpi.S(2)))
                    g.DrawPath(pen, path);

                g.DrawLine(pen, r.Left + r.Width / 2 - Dpi.S(3), r.Top + Dpi.S(4),
                    r.Left + r.Width / 2 + Dpi.S(3), r.Top + Dpi.S(4));

                int d = Dpi.S(6);
                g.DrawEllipse(pen, r.Left + (r.Width - d) / 2, body.Top + (body.Height - d) / 2, d, d);
                break;
            }
            case IconKind.ScreenshotScroll:
            {
                var page = new Rectangle(r.Left + r.Width / 4, r.Top + Dpi.S(2), r.Width / 2, r.Height - Dpi.S(4));
                using (var path = RoundedRect(page, Dpi.S(2)))
                    g.DrawPath(pen, path);

                int cx = r.Left + r.Width / 2;
                int top = page.Top + Dpi.S(4);
                int bottom = page.Bottom - Dpi.S(4);
                g.DrawLine(pen, cx, top, cx, bottom);
                g.DrawLine(pen, cx - Dpi.S(2), bottom - Dpi.S(2), cx, bottom);
                g.DrawLine(pen, cx + Dpi.S(2), bottom - Dpi.S(2), cx, bottom);
                break;
            }
            case IconKind.FullScreen:
            {
                var b = Rectangle.Inflate(r, -Dpi.S(1), -Dpi.S(1));
                int len = b.Width / 3;
                g.DrawLine(pen, b.Left, b.Top, b.Left + len, b.Top);
                g.DrawLine(pen, b.Left, b.Top, b.Left, b.Top + len);
                g.DrawLine(pen, b.Right, b.Top, b.Right - len, b.Top);
                g.DrawLine(pen, b.Right, b.Top, b.Right, b.Top + len);
                g.DrawLine(pen, b.Left, b.Bottom, b.Left + len, b.Bottom);
                g.DrawLine(pen, b.Left, b.Bottom, b.Left, b.Bottom - len);
                g.DrawLine(pen, b.Right, b.Bottom, b.Right - len, b.Bottom);
                g.DrawLine(pen, b.Right, b.Bottom, b.Right, b.Bottom - len);
                break;
            }
            case IconKind.Region:
            {
                var b = Rectangle.Inflate(r, -Dpi.S(1), -Dpi.S(1));
                using (var dash = new Pen(color, 1.8f * Dpi.Scale)
                       {
                           DashStyle = DashStyle.Dash,
                           DashPattern = new[] { 3f, 2f }
                       })
                    g.DrawRectangle(dash, b);

                int h = Math.Max(Dpi.S(2), r.Width / 12);
                using var brush = new SolidBrush(color);
                foreach (var p in new[]
                         {
                             new Point(b.Left, b.Top), new Point(b.Right, b.Top),
                             new Point(b.Left, b.Bottom), new Point(b.Right, b.Bottom)
                         })
                    g.FillRectangle(brush, p.X - h / 2, p.Y - h / 2, h, h);
                break;
            }
            case IconKind.Window:
            {
                var back = new Rectangle(r.Left + r.Width / 6, r.Top + r.Height / 6, r.Width * 3 / 5, r.Height * 2 / 5);
                var front = new Rectangle(r.Left + r.Width / 4, r.Top + r.Height * 2 / 5, r.Width * 3 / 5, r.Height * 2 / 5);
                using (var path = RoundedRect(back, Dpi.S(3)))
                    g.DrawPath(pen, path);
                using (var path = RoundedRect(front, Dpi.S(3)))
                    g.DrawPath(pen, path);
                break;
            }
            case IconKind.Settings:
            {
                int left = r.Left + r.Width / 7;
                int right = r.Right - r.Width / 7;
                int rad = Math.Max(Dpi.S(2), r.Width / 10);

                void Slider(int y, float position)
                {
                    g.DrawLine(pen, left, y, right, y);
                    int cx = left + (int)((right - left) * position);
                    using (var fill = new SolidBrush(background))
                        g.FillEllipse(fill, cx - rad, y - rad, rad * 2, rad * 2);
                    g.DrawEllipse(pen, cx - rad, y - rad, rad * 2, rad * 2);
                }

                Slider(r.Top + r.Height / 4, 0.65f);
                Slider(r.Top + r.Height / 2, 0.35f);
                Slider(r.Top + r.Height * 3 / 4, 0.7f);
                break;
            }
        }
    }

    private enum IconKind
    {
        Pin,
        ScreenshotRegion,
        ScreenshotWindow,
        ScreenshotScroll,
        FullScreen,
        Region,
        Window,
        Settings
    }

    private sealed class IconButton : Button
    {
        private readonly IconKind _icon;
        private bool _hover;
        private bool _pressed;
        private bool _active;

        public void SetActive(bool active)
        {
            if (_active == active) return;
            _active = active;
            Invalidate();
        }

        public IconButton(IconKind icon)
        {
            _icon = icon;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = PanelColor;
            Cursor = Cursors.Hand;
            TabStop = true;
            Font = new Font("Segoe UI", 9f);
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

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var background = new SolidBrush(PanelColor))
                g.FillRectangle(background, ClientRectangle);

            bool enabled = Enabled;
            var back = enabled
                ? _pressed ? ButtonPressedColor : (_hover || _active) ? ButtonHoverColor : PanelColor
                : PanelColor;

            var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            if (back != PanelColor)
            {
                using var path = RoundedRect(rect, Dpi.S(5));
                using var fill = new SolidBrush(back);
                g.FillPath(fill, path);
            }

            if (Focused && enabled)
            {
                using var focus = new Pen(Color.FromArgb(90, 120, 190, 255), 1.2f * Dpi.Scale);
                using var path = RoundedRect(rect, Dpi.S(5));
                g.DrawPath(focus, path);
            }

            var iconColor = !enabled ? DisabledColor : (_hover || _active) ? IconHoverColor : IconColor;
            DrawIcon(g, new Rectangle(0, 0, Width, Height), _icon, iconColor, back, _active);
        }
    }

    private sealed class CloseButton : Button
    {
        private bool _hover;

        public CloseButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = PanelColor;
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
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var background = new SolidBrush(PanelColor))
                g.FillRectangle(background, ClientRectangle);

            if (_hover)
            {
                using var path = RoundedRect(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), Dpi.S(5));
                using var fill = new SolidBrush(Color.FromArgb(45, 255, 255, 255));
                g.FillPath(fill, path);
            }

            var color = _hover ? Color.White : Color.FromArgb(170, 182, 198);
            using var pen = new Pen(color, 1.6f * Dpi.Scale)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            int inset = Dpi.S(5);
            g.DrawLine(pen, inset, inset, Width - inset, Height - inset);
            g.DrawLine(pen, Width - inset, inset, inset, Height - inset);
        }
    }
}
