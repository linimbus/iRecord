using System.Buffers;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using iRecord.App;

namespace iRecord.UI;

public sealed class ImageEditorForm : Form
{
    private static readonly Color BorderColor = Color.FromArgb(208, 214, 222);
    private static readonly Color BarColor = Color.FromArgb(38, 42, 50);
    private static readonly Color HoverColor = Color.FromArgb(54, 60, 70);
    private static readonly Color PressedColor = Color.FromArgb(30, 34, 40);
    private static readonly Color IconColor = Color.FromArgb(224, 232, 242);
    private static readonly Color IconActiveColor = Color.FromArgb(120, 190, 255);

    private enum EditorTool
    {
        Rectangle,
        Square,
        Text,
        Number,
        Arrow,
        ArrowDashed,
        Mosaic
    }

    private enum LineThickness
    {
        Thin,
        Medium,
        Thick,
        ExtraThick
    }

    private enum EditorIcon
    {
        Text,
        Rectangle,
        Square,
        Number,
        Arrow,
        ArrowDashed,
        Mosaic,
        Color,
        Undo,
        Save,
        Copy,
        Close
    }

    private Bitmap _image;
    private string _path;
    private readonly List<Bitmap> _undo = new();
    private readonly EditorCanvas _canvas;
    private readonly Label _toast = new();
    private readonly System.Windows.Forms.Timer _toastTimer;
    private readonly int _displayWidth;
    private readonly int _displayHeight;
    private readonly double _displayScaleX;
    private readonly double _displayScaleY;
    private EditorToolbarForm? _toolbar;

    private EditorTool _tool = EditorTool.Rectangle;
    private Color _color = Color.FromArgb(255, 64, 64);
    private string _textFontFamily = "Segoe UI";
    private FontStyle _textFontStyle = FontStyle.Bold;
    private int _textFontSize;
    private LineThickness _lineThickness = LineThickness.Medium;
    private int _nextNumber = 1;
    private bool _dragging;
    private bool _closing;
    private bool _windowDragging;
    private Point _windowDragStartCursor;
    private Point _windowDragStartLocation;
    private Point _startImage;
    private Point _currentImage;
    private TextBox? _inlineInput;
    private InlineTextOverlay? _inlineOverlay;
    private Point _inlineImagePoint;
    private bool _inlineClosing;
    private bool _inlineCaretOn = true;
    private readonly System.Windows.Forms.Timer _inlineCaretTimer;

    public ImageEditorForm(Bitmap image, string path)
    {
        _image = image;
        _path = path;
        _textFontSize = Math.Clamp((int)Math.Round(Math.Min(image.Width, image.Height) / 20.0), 20, 120);

        Text = Localization.T("editor_title");
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        BackColor = BorderColor;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        ShowInTaskbar = true;

        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, image.Width, image.Height);
        int maxWidth = Math.Max(1, area.Width - Dpi.S(40));
        int maxHeight = Math.Max(1, area.Height - Dpi.S(120));
        double scale = Math.Min(1.0, Math.Min(maxWidth / (double)image.Width, maxHeight / (double)image.Height));
        if (scale <= 0) scale = 1;

        _displayWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
        _displayHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
        _displayScaleX = _displayWidth / (double)image.Width;
        _displayScaleY = _displayHeight / (double)image.Height;

        int frame = Dpi.S(6);
        Padding = new Padding(frame);
        ClientSize = new Size(_displayWidth + 2 + frame * 2, _displayHeight + 2 + frame * 2);

        int left = area.Left + (area.Width - Width) / 2;
        int top = area.Top + (area.Height - Height) / 2;
        Location = new Point(left, top);

        _canvas = new EditorCanvas(this)
        {
            Dock = DockStyle.Fill,
            BackColor = BorderColor,
            Cursor = Cursors.Default
        };
        Controls.Add(_canvas);

        _toast.AutoSize = true;
        _toast.BackColor = Color.FromArgb(220, 30, 32, 36);
        _toast.ForeColor = Color.White;
        _toast.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _toast.Padding = new Padding(Dpi.S(10), Dpi.S(6), Dpi.S(10), Dpi.S(6));
        _toast.Visible = false;
        _toast.Cursor = Cursors.Default;
        Controls.Add(_toast);
        _toast.BringToFront();

        _toastTimer = new System.Windows.Forms.Timer { Interval = 1800 };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            _toast.Visible = false;
        };

        _inlineCaretTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _inlineCaretTimer.Tick += (_, _) =>
        {
            _inlineCaretOn = !_inlineCaretOn;
            _inlineOverlay?.Invalidate();
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        _toolbar = new EditorToolbarForm(this) { Owner = this };
        _toolbar.Show();
        _toolbar.SetActiveTool(_tool);
        _toolbar.SetColor(_color);
        PositionToolbar();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateEditorCursor();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (!_windowDragging)
            Cursor = Cursors.Default;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _windowDragging = true;
            _windowDragStartCursor = Cursor.Position;
            _windowDragStartLocation = Location;
            Capture = true;
            Cursor = Cursors.SizeAll;
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_windowDragging)
        {
            var point = Cursor.Position;
            int x = _windowDragStartLocation.X + point.X - _windowDragStartCursor.X;
            int y = _windowDragStartLocation.Y + point.Y - _windowDragStartCursor.Y;
            var area = SystemInformation.VirtualScreen;
            x = Math.Clamp(x, area.Left - Width + Dpi.S(60), area.Right - Dpi.S(60));
            y = Math.Clamp(y, area.Top, area.Bottom - Dpi.S(40));

            int dx = x - Left;
            int dy = y - Top;
            Location = new Point(x, y);
            if (dx != 0 || dy != 0)
                _toolbar?.MoveBy(dx, dy);
            return;
        }

        UpdateEditorCursor();
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_windowDragging && e.Button == MouseButtons.Left)
        {
            _windowDragging = false;
            Capture = false;
            UpdateEditorCursor();
            return;
        }

        base.OnMouseUp(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture && _windowDragging)
        {
            _windowDragging = false;
            UpdateEditorCursor();
        }

        base.OnMouseCaptureChanged(e);
    }

    private void UpdateEditorCursor()
    {
        if (_windowDragging)
        {
            Cursor = Cursors.SizeAll;
            return;
        }

        var point = PointToClient(Cursor.Position);
        bool overFrame = ClientRectangle.Contains(point) && !_canvas.Bounds.Contains(point);
        Cursor = overFrame ? Cursors.SizeAll : Cursors.Default;
    }

    private void PositionToolbar()
    {
        if (_toolbar == null)
            return;

        var area = Screen.FromControl(this).WorkingArea;
        int x = Left + (Width - _toolbar.Width) / 2;
        int y = Top + Height + Dpi.S(8);

        if (y + _toolbar.Height > area.Bottom - Dpi.S(4))
            y = area.Bottom - _toolbar.Height - Dpi.S(4);

        x = Math.Clamp(x, area.Left + Dpi.S(4),
            Math.Max(area.Left + Dpi.S(4), area.Right - _toolbar.Width - Dpi.S(4)));
        _toolbar.Location = new Point(x, y);
    }

    private void SelectTool(EditorTool tool)
    {
        CommitInlineTextIfAny();
        _tool = tool;
        _toolbar?.SetActiveTool(tool);
    }

    private void SetTextFont(string family)
    {
        if (string.IsNullOrWhiteSpace(family) ||
            string.Equals(family, _textFontFamily, StringComparison.OrdinalIgnoreCase))
            return;

        _textFontFamily = family;
        RefreshInlineTextStyle();
    }

    private void SetTextSize(int size)
    {
        size = Math.Clamp(size, 8, 200);
        if (size == _textFontSize)
            return;

        _textFontSize = size;
        RefreshInlineTextStyle();
    }

    private void SetTextStyle(FontStyle style)
    {
        if (style == _textFontStyle)
            return;

        _textFontStyle = style;
        RefreshInlineTextStyle();
    }

    private void SetLineThickness(LineThickness thickness)
    {
        _lineThickness = thickness;
    }

    private int LineWidth()
    {
        int medium = Math.Clamp((int)Math.Round(Math.Min(_image.Width, _image.Height) / 220.0), 2, 10);
        float factor = _lineThickness switch
        {
            LineThickness.Thin => 0.6f,
            LineThickness.Thick => 1.6f,
            LineThickness.ExtraThick => 2.4f,
            _ => 1f
        };
        return Math.Clamp((int)Math.Round(medium * factor), 1, 32);
    }

    private float PreviewLineWidth()
    {
        float scale = (float)Math.Min(_displayScaleX, _displayScaleY);
        return Math.Max(1.5f, LineWidth() * scale);
    }

    private void RefreshInlineTextStyle()
    {
        if (_inlineInput == null || _inlineOverlay == null)
            return;

        UpdateInlineTextLayout();
        _inlineOverlay.Invalidate();
    }

    private void FocusInlineTextInput()
    {
        if (_inlineInput == null || _inlineInput.IsDisposed || _inlineClosing)
            return;

        int start = _inlineInput.SelectionStart;
        int length = _inlineInput.SelectionLength;
        if (!ContainsFocus)
            Activate();
        _inlineInput.Focus();
        _inlineInput.SelectionStart = start;
        _inlineInput.SelectionLength = length;
        _inlineOverlay?.Invalidate();
    }

    private void PickColor()
    {
        CommitInlineTextIfAny();
        using var dialog = new ColorDialog { Color = _color, FullOpen = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _color = dialog.Color;
            _toolbar?.SetColor(_color);
        }
    }

    private void CanvasMouseDown(EditorCanvas canvas, MouseEventArgs e)
    {
        if (_inlineOverlay != null)
        {
            CommitInlineText();
            return;
        }

        if (e.Button != MouseButtons.Left) return;
        if (!TryMapPoint(canvas.ClientSize, e.Location, out var point)) return;

        switch (_tool)
        {
            case EditorTool.Text:
            {
                BeginInlineText(point, e.Location);
                break;
            }
            case EditorTool.Number:
            {
                PushUndo();
                using var g = Graphics.FromImage(_image);
                DrawNumber(g, _nextNumber++, point);
                canvas.Invalidate();
                break;
            }
            default:
                _dragging = true;
                _startImage = point;
                _currentImage = point;
                canvas.Invalidate();
                break;
        }
    }

    private void CanvasMouseMove(EditorCanvas canvas, MouseEventArgs e)
    {
        if (!_dragging) return;

        int x = (int)Math.Round((e.X - 1) / _displayScaleX);
        int y = (int)Math.Round((e.Y - 1) / _displayScaleY);
        _currentImage = ClampToImage(new Point(x, y));
        canvas.Invalidate();
    }

    private void CanvasMouseUp(EditorCanvas canvas, MouseEventArgs e)
    {
        if (!_dragging || e.Button != MouseButtons.Left) return;
        _dragging = false;

        var rect = Rectangle.FromLTRB(
            Math.Min(_startImage.X, _currentImage.X),
            Math.Min(_startImage.Y, _currentImage.Y),
            Math.Max(_startImage.X, _currentImage.X),
            Math.Max(_startImage.Y, _currentImage.Y));
        rect.Intersect(new Rectangle(0, 0, _image.Width, _image.Height));

        if (rect.Width < 2 || rect.Height < 2)
        {
            canvas.Invalidate();
            return;
        }

        PushUndo();

        switch (_tool)
        {
            case EditorTool.Rectangle:
            {
                using var g = Graphics.FromImage(_image);
                using var pen = CreatePen(false);
                g.DrawRectangle(pen, rect);
                break;
            }
            case EditorTool.Square:
            {
                var square = SquareFromPoints(_startImage, _currentImage);
                square.Intersect(new Rectangle(0, 0, _image.Width, _image.Height));
                using var g = Graphics.FromImage(_image);
                using var pen = CreatePen(false);
                g.DrawRectangle(pen, square);
                break;
            }
            case EditorTool.Arrow:
            case EditorTool.ArrowDashed:
            {
                using var g = Graphics.FromImage(_image);
                using var pen = CreatePen(_tool == EditorTool.ArrowDashed);
                pen.CustomEndCap = new AdjustableArrowCap(4f, 5f, true);
                g.DrawLine(pen, _startImage, _currentImage);
                break;
            }
            case EditorTool.Mosaic:
            {
                var block = Math.Clamp(Math.Min(rect.Width, rect.Height) / 18, 6, 24);
                Pixelate(rect, block);
                break;
            }
        }

        canvas.Invalidate();
    }

    private void BeginInlineText(Point imagePoint, Point displayPoint)
    {
        CommitInlineTextIfAny();

        _inlineImagePoint = imagePoint;

        var input = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.None,
            WordWrap = false,
            BackColor = Color.White,
            ForeColor = Color.White,
            TabStop = true,
            Font = new Font("Segoe UI", 9f)
        };

        var overlay = new InlineTextOverlay(this)
        {
            Bounds = new Rectangle(displayPoint, new Size(Dpi.S(8), Dpi.S(20))),
            BackColor = BorderColor,
            Enabled = false
        };

        _canvas.Controls.Add(input);
        _canvas.Controls.Add(overlay);
        overlay.BringToFront();

        _inlineInput = input;
        _inlineOverlay = overlay;
        _inlineClosing = false;
        _inlineCaretOn = true;

        input.TextChanged += (_, _) => UpdateInlineTextLayout();
        input.KeyDown += InlineInputKeyDown;
        input.KeyUp += (_, _) =>
        {
            if (!overlay.IsDisposed)
                overlay.Invalidate();
        };
        input.MouseUp += (_, _) =>
        {
            if (!overlay.IsDisposed)
                overlay.Invalidate();
        };

        UpdateInlineTextLayout();
        input.Focus();
        _inlineCaretTimer.Start();
        overlay.Invalidate();
    }

    private void UpdateInlineTextLayout()
    {
        if (_inlineInput == null || _inlineOverlay == null)
            return;

        string text = _inlineInput.Text.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = text.Split('\n');
        using var g = _canvas.CreateGraphics();
        using var font = new Font(_textFontFamily, _textFontSize * (float)_displayScaleY,
            _textFontStyle, GraphicsUnit.Pixel);

        float maxWidth = 0;
        foreach (var line in lines)
            maxWidth = Math.Max(maxWidth, g.MeasureString(line, font).Width);

        float lineHeight = font.GetHeight(g);
        int width = Math.Max(Dpi.S(6), (int)Math.Ceiling(maxWidth) + Dpi.S(4));
        int height = Math.Max(Dpi.S(20), (int)Math.Ceiling(lineHeight * lines.Length) + Dpi.S(2));

        var location = _inlineOverlay.Location;
        _inlineOverlay.Bounds = new Rectangle(location, new Size(width, height));
        _inlineInput.Bounds = _inlineOverlay.Bounds;
        _inlineOverlay.BringToFront();
        _inlineOverlay.Invalidate();
    }

    private void RenderInlineOverlay(Graphics g, Control overlay)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        using (var background = new SolidBrush(BorderColor))
            g.FillRectangle(background, 0, 0, overlay.Width, overlay.Height);

        var overlayRect = new RectangleF(overlay.Left, overlay.Top, overlay.Width, overlay.Height);
        var imageRect = new RectangleF(1, 1, _displayWidth, _displayHeight);
        var intersect = RectangleF.Intersect(overlayRect, imageRect);
        if (intersect.Width > 0 && intersect.Height > 0)
        {
            var source = new RectangleF(
                (float)((intersect.Left - 1) / _displayScaleX),
                (float)((intersect.Top - 1) / _displayScaleY),
                (float)(intersect.Width / _displayScaleX),
                (float)(intersect.Height / _displayScaleY));
            var destination = new RectangleF(
                intersect.Left - overlay.Left,
                intersect.Top - overlay.Top,
                intersect.Width,
                intersect.Height);
            g.DrawImage(_image, destination, source, GraphicsUnit.Pixel);
        }

        string text = (_inlineInput?.Text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = text.Split('\n');
        int imageFont = _textFontSize;
        using var font = new Font(_textFontFamily, imageFont * (float)_displayScaleY,
            _textFontStyle, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(_color);
        using var shadow = new SolidBrush(Color.FromArgb(220, 0, 0, 0));
        int offset = Math.Max(1, (int)Math.Round(Math.Max(1, imageFont / 14.0) * _displayScaleY));

        float lineHeight = font.GetHeight(g);
        float y = 0;
        foreach (var line in lines)
        {
            foreach (var (dx, dy) in new[]
                     {
                         (-offset, 0), (offset, 0), (0, -offset), (0, offset),
                         (-offset, -offset), (offset, offset)
                     })
                g.DrawString(line, font, shadow, dx, y + dy);

            g.DrawString(line, font, brush, 0, y);
            y += lineHeight;
        }

        if (_inlineCaretOn && _inlineInput != null && _inlineInput.Focused)
        {
            int caret = Math.Clamp(_inlineInput.SelectionStart, 0, text.Length);
            string before = text[..caret];
            int lineIndex = before.Count(c => c == '\n');
            int newline = before.LastIndexOf('\n');
            string lineText = newline >= 0 ? before[(newline + 1)..] : before;
            float caretX = g.MeasureString(lineText, font).Width;
            float caretY = lineIndex * lineHeight;

            using var outline = new Pen(Color.FromArgb(210, 0, 0, 0), 3f * Dpi.Scale);
            g.DrawLine(outline, caretX, caretY, caretX, caretY + lineHeight);
            using var caretPen = new Pen(Color.White, 1f * Dpi.Scale);
            g.DrawLine(caretPen, caretX, caretY, caretX, caretY + lineHeight);
        }
    }

    private void InlineInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            BeginInvoke(new Action(CancelInlineText));
            return;
        }

        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.SuppressKeyPress = true;
            BeginInvoke(new Action(CommitInlineText));
        }
    }

    private void CommitInlineTextIfAny()
    {
        if (_inlineOverlay != null)
            CommitInlineText();
    }

    private void CommitInlineText()
    {
        if (_inlineClosing || _inlineInput == null || _inlineOverlay == null)
            return;

        _inlineClosing = true;
        string text = _inlineInput.Text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        _inlineCaretTimer.Stop();
        RemoveInlineControls();
        _inlineClosing = false;

        if (!string.IsNullOrWhiteSpace(text))
        {
            PushUndo();
            using var g = Graphics.FromImage(_image);
            DrawText(g, text, _inlineImagePoint);
            _canvas.Invalidate();
        }
    }

    private void CancelInlineText()
    {
        if (_inlineClosing)
            return;

        _inlineClosing = true;
        _inlineCaretTimer.Stop();
        RemoveInlineControls();
        _inlineClosing = false;
    }

    private void RemoveInlineControls()
    {
        var input = _inlineInput;
        var overlay = _inlineOverlay;
        _inlineInput = null;
        _inlineOverlay = null;

        if (input != null)
        {
            try { _canvas.Controls.Remove(input); } catch { }
            input.Dispose();
        }

        if (overlay != null)
        {
            try { _canvas.Controls.Remove(overlay); } catch { }
            overlay.Dispose();
        }
    }

    private void RenderCanvas(Graphics g, Size size)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        using (var background = new SolidBrush(BorderColor))
            g.FillRectangle(background, 0, 0, size.Width, size.Height);

        var destination = new Rectangle(1, 1, _displayWidth, _displayHeight);
        g.DrawImage(_image, destination,
            new Rectangle(0, 0, _image.Width, _image.Height), GraphicsUnit.Pixel);

        if (_dragging)
            DrawPreview(g);
    }

    private void DrawPreview(Graphics g)
    {
        PointF ToDisplay(Point p) => new(
            (float)(1 + p.X * _displayScaleX),
            (float)(1 + p.Y * _displayScaleY));

        PointF a = ToDisplay(_startImage);
        PointF b = ToDisplay(_currentImage);

        using var pen = new Pen(_color, PreviewLineWidth()) { DashStyle = DashStyle.Dash };

        switch (_tool)
        {
            case EditorTool.Rectangle:
            case EditorTool.Mosaic:
            {
                var rect = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                    Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                break;
            }
            case EditorTool.Square:
            {
                float dx = b.X - a.X;
                float dy = b.Y - a.Y;
                float side = Math.Min(Math.Abs(dx), Math.Abs(dy));
                float x = dx < 0 ? a.X - side : a.X;
                float y = dy < 0 ? a.Y - side : a.Y;
                g.DrawRectangle(pen, x, y, side, side);
                break;
            }
            case EditorTool.Arrow:
            case EditorTool.ArrowDashed:
                pen.DashStyle = _tool == EditorTool.ArrowDashed ? DashStyle.Dash : DashStyle.Solid;
                pen.CustomEndCap = new AdjustableArrowCap(4f, 5f, true);
                g.DrawLine(pen, a, b);
                break;
        }
    }

    private bool TryMapPoint(Size size, Point canvasPoint, out Point imagePoint)
    {
        imagePoint = default;
        int x = (int)Math.Round((canvasPoint.X - 1) / _displayScaleX);
        int y = (int)Math.Round((canvasPoint.Y - 1) / _displayScaleY);
        if (x < 0 || y < 0 || x >= _image.Width || y >= _image.Height)
            return false;

        imagePoint = new Point(x, y);
        return true;
    }

    private Point ClampToImage(Point point) => new(
        Math.Clamp(point.X, 0, _image.Width - 1),
        Math.Clamp(point.Y, 0, _image.Height - 1));

    private static Rectangle SquareFromPoints(Point start, Point end)
    {
        int dx = end.X - start.X;
        int dy = end.Y - start.Y;
        int size = Math.Min(Math.Abs(dx), Math.Abs(dy));
        int x = dx < 0 ? start.X - size : start.X;
        int y = dy < 0 ? start.Y - size : start.Y;
        return new Rectangle(x, y, size, size);
    }

    private Pen CreatePen(bool dashed)
    {
        int width = LineWidth();
        var pen = new Pen(_color, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        if (dashed)
            pen.DashStyle = DashStyle.Dash;
        return pen;
    }

    private void DrawText(Graphics g, string text, Point location)
    {
        int fontSize = _textFontSize;
        using var font = new Font(_textFontFamily, fontSize, _textFontStyle, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(_color);
        using var shadow = new SolidBrush(Color.FromArgb(220, 0, 0, 0));

        int offset = Math.Max(1, fontSize / 14);
        foreach (var (dx, dy) in new[]
                 {
                     (-offset, 0), (offset, 0), (0, -offset), (0, offset),
                     (-offset, -offset), (offset, offset)
                 })
            g.DrawString(text, font, shadow, location.X + dx, location.Y + dy);

        g.DrawString(text, font, brush, location.X, location.Y);
    }

    private void DrawNumber(Graphics g, int number, Point location)
    {
        int diameter = Math.Clamp((int)Math.Round(Math.Min(_image.Width, _image.Height) / 22.0), 24, 96);
        var rect = new Rectangle(location.X - diameter / 2, location.Y - diameter / 2, diameter, diameter);
        using var brush = new SolidBrush(_color);
        g.FillEllipse(brush, rect);

        using var font = new Font(_textFontFamily, diameter * 0.5f, _textFontStyle, GraphicsUnit.Pixel);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(number.ToString(), font, Brushes.White, rect, format);
    }

    private void Pixelate(Rectangle region, int block)
    {
        region.Intersect(new Rectangle(0, 0, _image.Width, _image.Height));
        if (region.Width <= 0 || region.Height <= 0)
            return;

        var data = _image.LockBits(new Rectangle(0, 0, _image.Width, _image.Height),
            ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        int length = data.Stride * _image.Height;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            Marshal.Copy(data.Scan0, bytes, 0, length);
            int stride = data.Stride;

            for (int y = region.Top; y < region.Bottom; y += block)
            {
                for (int x = region.Left; x < region.Right; x += block)
                {
                    int width = Math.Min(block, region.Right - x);
                    int height = Math.Min(block, region.Bottom - y);
                    long b = 0, g = 0, r = 0;
                    int count = 0;

                    for (int yy = y; yy < y + height; yy++)
                    {
                        int row = yy * stride;
                        for (int xx = x; xx < x + width; xx++)
                        {
                            int index = row + xx * 4;
                            b += bytes[index];
                            g += bytes[index + 1];
                            r += bytes[index + 2];
                            count++;
                        }
                    }

                    if (count == 0) continue;

                    byte avgB = (byte)(b / count);
                    byte avgG = (byte)(g / count);
                    byte avgR = (byte)(r / count);

                    for (int yy = y; yy < y + height; yy++)
                    {
                        int row = yy * stride;
                        for (int xx = x; xx < x + width; xx++)
                        {
                            int index = row + xx * 4;
                            bytes[index] = avgB;
                            bytes[index + 1] = avgG;
                            bytes[index + 2] = avgR;
                        }
                    }
                }
            }

            Marshal.Copy(bytes, 0, data.Scan0, length);
        }
        finally
        {
            _image.UnlockBits(data);
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    private void PushUndo()
    {
        _undo.Add((Bitmap)_image.Clone());
        if (_undo.Count > 20)
        {
            _undo[0].Dispose();
            _undo.RemoveAt(0);
        }
    }

    private void Undo()
    {
        CommitInlineTextIfAny();
        if (_undo.Count == 0)
            return;

        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _image.Dispose();
        _image = previous;
        ShowToast(Localization.T("editor_status_undo"));
        _canvas.Invalidate();
    }

    private void SaveFromToolbar()
    {
        CommitInlineTextIfAny();
        try
        {
            if (SaveCurrentImage())
                ShowToast(Localization.T("editor_status_saved"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Localization.Format("editor_error_save", ex.Message), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CopyFromToolbar()
    {
        CommitInlineTextIfAny();
        if (CopyToClipboard())
            ShowToast(Localization.T("editor_status_copied"));
    }

    private bool SaveCurrentImage()
    {
        if (string.IsNullOrWhiteSpace(_path))
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "PNG|*.png",
                FileName = "iRecord_edit.png"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return false;
            _path = dialog.FileName;
        }

        _image.Save(_path, ImageFormat.Png);
        CopyToClipboard();
        return true;
    }

    private bool CopyToClipboard()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Bitmap? copy = null;
            try
            {
                copy = new Bitmap(_image);
                Clipboard.SetImage(copy);
                copy = null;
                return true;
            }
            catch
            {
                Thread.Sleep(80);
            }
            finally
            {
                copy?.Dispose();
            }
        }

        return false;
    }

    private void ShowToast(string text)
    {
        _toast.Text = text;
        _toast.Visible = true;
        _toast.BringToFront();
        _toast.Location = new Point((ClientSize.Width - _toast.Width) / 2,
            ClientSize.Height - _toast.Height - Dpi.S(10));
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        CommitInlineTextIfAny();
        try
        {
            if (!SaveCurrentImage())
            {
                e.Cancel = true;
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Localization.Format("editor_error_save", ex.Message), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            e.Cancel = true;
            return;
        }

        _closing = true;
        _toastTimer.Stop();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        if (_toolbar != null)
        {
            try { _toolbar.Dispose(); } catch { }
            _toolbar = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toastTimer.Stop();
            _toastTimer.Dispose();
            _inlineCaretTimer.Stop();
            _inlineCaretTimer.Dispose();
            try { _inlineInput?.Dispose(); } catch { }
            _inlineInput = null;
            try { _inlineOverlay?.Dispose(); } catch { }
            _inlineOverlay = null;
            try { _toolbar?.Dispose(); } catch { }
            _toolbar = null;
            _image.Dispose();
            foreach (var bitmap in _undo)
                bitmap.Dispose();
            _undo.Clear();
        }
        base.Dispose(disposing);
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

    private static void DrawEditorIcon(Graphics g, Rectangle r, EditorIcon icon, Color color, Color swatch)
    {
        using var pen = new Pen(color, 1.8f * Dpi.Scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        var inner = new Rectangle(r.Left + Dpi.S(6), r.Top + Dpi.S(6),
            r.Width - Dpi.S(12), r.Height - Dpi.S(12));
        int cx = r.Left + r.Width / 2;
        int cy = r.Top + r.Height / 2;

        switch (icon)
        {
            case EditorIcon.Text:
            {
                int left = inner.Left + Dpi.S(1);
                int right = inner.Right - Dpi.S(1);
                int top = inner.Top + Dpi.S(1);
                int bottom = inner.Bottom - Dpi.S(1);
                g.DrawLine(pen, left, bottom, cx, top);
                g.DrawLine(pen, cx, top, right, bottom);
                int barY = top + (bottom - top) * 3 / 5;
                int barInset = (right - left) / 4;
                g.DrawLine(pen, left + barInset, barY, right - barInset, barY);
                break;
            }
            case EditorIcon.Rectangle:
                g.DrawRectangle(pen, inner.Left, inner.Top + Dpi.S(2), inner.Width, inner.Height - Dpi.S(4));
                break;
            case EditorIcon.Square:
            {
                int side = Math.Min(inner.Width, inner.Height);
                g.DrawRectangle(pen, cx - side / 2, cy - side / 2, side, side);
                break;
            }
            case EditorIcon.Number:
                g.DrawEllipse(pen, inner);
                g.DrawLine(pen, cx - Dpi.S(2), cy - Dpi.S(3), cx, cy - Dpi.S(5));
                g.DrawLine(pen, cx, cy - Dpi.S(5), cx, cy + Dpi.S(4));
                g.DrawLine(pen, cx - Dpi.S(2), cy + Dpi.S(4), cx + Dpi.S(2), cy + Dpi.S(4));
                break;
            case EditorIcon.Arrow:
            case EditorIcon.ArrowDashed:
            {
                if (icon == EditorIcon.ArrowDashed)
                    pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, inner.Left, inner.Bottom, inner.Right, inner.Top);
                g.DrawLine(pen, inner.Right, inner.Top, inner.Right - Dpi.S(4), inner.Top + Dpi.S(1));
                g.DrawLine(pen, inner.Right, inner.Top, inner.Right - Dpi.S(1), inner.Top + Dpi.S(4));
                break;
            }
            case EditorIcon.Mosaic:
            {
                int gap = Dpi.S(2);
                int cell = (Math.Min(inner.Width, inner.Height) - gap) / 2;
                int left = cx - cell - gap / 2;
                int top = cy - cell - gap / 2;
                using var brush = new SolidBrush(color);
                for (int row = 0; row < 2; row++)
                {
                    for (int col = 0; col < 2; col++)
                    {
                        var cellRect = new Rectangle(left + col * (cell + gap), top + row * (cell + gap), cell, cell);
                        if ((row + col) % 2 == 0)
                            g.FillRectangle(brush, cellRect);
                        else
                            g.DrawRectangle(pen, cellRect);
                    }
                }
                break;
            }
            case EditorIcon.Color:
            {
                using var brush = new SolidBrush(swatch);
                g.FillEllipse(brush, inner);
                g.DrawEllipse(pen, inner);
                break;
            }
            case EditorIcon.Undo:
            {
                var state = g.Save();
                g.TranslateTransform(cx, cy);
                g.RotateTransform(-28f);

                int w = Dpi.S(13);
                int h = Dpi.S(8);
                var body = new Rectangle(-w / 2, -h / 2, w, h);
                using (var path = RoundedRect(body, Dpi.S(2)))
                    g.DrawPath(pen, path);

                g.DrawLine(pen, body.Left + Dpi.S(4), body.Top, body.Left + Dpi.S(4), body.Bottom);
                g.DrawLine(pen, body.Right - Dpi.S(3), body.Top + Dpi.S(2),
                    body.Right - Dpi.S(3), body.Bottom - Dpi.S(2));

                g.Restore(state);
                break;
            }
            case EditorIcon.Save:
                g.DrawRectangle(pen, inner.Left + Dpi.S(1), inner.Top + Dpi.S(1), inner.Width - Dpi.S(2), inner.Height - Dpi.S(2));
                g.DrawRectangle(pen, inner.Left + Dpi.S(4), inner.Top + Dpi.S(1), inner.Width - Dpi.S(8), Dpi.S(4));
                g.DrawRectangle(pen, inner.Left + Dpi.S(3), inner.Bottom - Dpi.S(6), inner.Width - Dpi.S(6), Dpi.S(5));
                break;
            case EditorIcon.Copy:
            {
                var back = new Rectangle(inner.Left, inner.Top + Dpi.S(2), inner.Width - Dpi.S(3), inner.Height - Dpi.S(4));
                var front = new Rectangle(inner.Left + Dpi.S(3), inner.Top, inner.Width - Dpi.S(3), inner.Height - Dpi.S(4));
                g.DrawRectangle(pen, back);
                g.DrawRectangle(pen, front);
                break;
            }
            case EditorIcon.Close:
            {
                int inset = Dpi.S(2);
                g.DrawLine(pen, inner.Left + inset, inner.Top + inset, inner.Right - inset, inner.Bottom - inset);
                g.DrawLine(pen, inner.Right - inset, inner.Top + inset, inner.Left + inset, inner.Bottom - inset);
                break;
            }
        }
    }

    private sealed class EditorCanvas : Control
    {
        private readonly ImageEditorForm _owner;

        public EditorCanvas(ImageEditorForm owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            _owner.RenderCanvas(e.Graphics, ClientSize);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _owner.CanvasMouseDown(this, e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _owner.CanvasMouseMove(this, e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _owner.CanvasMouseUp(this, e);
        }
    }

    private sealed class InlineTextOverlay : Control
    {
        private readonly ImageEditorForm _owner;

        public InlineTextOverlay(ImageEditorForm owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            _owner.RenderInlineOverlay(e.Graphics, this);
        }
    }

    private sealed class EditorToolbarForm : Form
    {
        private readonly ImageEditorForm _editor;
        private readonly Dictionary<EditorTool, IconToolButton> _toolButtons = new();
        private readonly List<IconToolButton> _toolIconButtons = new();
        private readonly List<IconToolButton> _actionIconButtons = new();
        private readonly IconToolButton _colorButton = new(EditorIcon.Color);
        private readonly ComboBox _fontCombo = new();
        private readonly NumericUpDown _sizeBox = new();
        private readonly ComboBox _styleCombo = new();
        private readonly ComboBox _lineWidthCombo = new();
        private static readonly string[] LineWidthKeys =
        {
            "editor_line_thin",
            "editor_line_medium",
            "editor_line_thick",
            "editor_line_extra_thick"
        };
        private static readonly (string LabelKey, FontStyle Style)[] FontStyles =
        {
            ("editor_style_regular", FontStyle.Regular),
            ("editor_style_bold", FontStyle.Bold),
            ("editor_style_italic", FontStyle.Italic),
            ("editor_style_bold_italic", FontStyle.Bold | FontStyle.Italic)
        };
        private readonly ToolTip _toolTip = new()
        {
            AutoPopDelay = 6000,
            InitialDelay = 300,
            ReshowDelay = 100,
            ShowAlways = true
        };

        private bool _dragging;
        private Point _dragStartCursor;
        private Point _dragStartToolbar;

        public EditorToolbarForm(ImageEditorForm editor)
        {
            _editor = editor;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = BarColor;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.None;

            AddTool(EditorTool.Text, EditorIcon.Text, "editor_tool_text");
            AddTool(EditorTool.Rectangle, EditorIcon.Rectangle, "editor_tool_rect");
            AddTool(EditorTool.Square, EditorIcon.Square, "editor_tool_square");
            AddTool(EditorTool.Number, EditorIcon.Number, "editor_tool_number");
            AddTool(EditorTool.Arrow, EditorIcon.Arrow, "editor_tool_arrow");
            AddTool(EditorTool.ArrowDashed, EditorIcon.ArrowDashed, "editor_tool_arrow_dashed");
            AddTool(EditorTool.Mosaic, EditorIcon.Mosaic, "editor_tool_mosaic");

            _fontCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _fontCombo.Font = new Font("Segoe UI", 9f);
            _fontCombo.DropDownWidth = Dpi.S(240);
            using (var fonts = new InstalledFontCollection())
            {
                var names = fonts.Families.Select(f => f.Name).Distinct()
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray();
                _fontCombo.Items.AddRange(names);
                int selected = Array.FindIndex(names,
                    n => string.Equals(n, _editor._textFontFamily, StringComparison.OrdinalIgnoreCase));
                if (selected < 0 && names.Length > 0) selected = 0;
                _fontCombo.SelectedIndex = selected;
            }
            _toolTip.SetToolTip(_fontCombo, Localization.T("editor_font"));
            Controls.Add(_fontCombo);
            if (_fontCombo.SelectedItem is string family)
                _editor.SetTextFont(family);
            _fontCombo.SelectedIndexChanged += (_, _) =>
            {
                if (_fontCombo.SelectedItem is string selectedFamily)
                    _editor.SetTextFont(selectedFamily);
            };
            _fontCombo.DropDownClosed += (_, _) => _editor.FocusInlineTextInput();
            _fontCombo.KeyUp += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                    _editor.FocusInlineTextInput();
            };

            _sizeBox.Minimum = 8;
            _sizeBox.Maximum = 200;
            _sizeBox.Increment = 2;
            _sizeBox.Font = new Font("Segoe UI", 9f);
            _sizeBox.Value = Math.Clamp(_editor._textFontSize, 8, 200);
            _sizeBox.ValueChanged += (_, _) => _editor.SetTextSize((int)_sizeBox.Value);
            _sizeBox.MouseUp += (_, e) =>
            {
                if (e.X >= _sizeBox.ClientSize.Width - Dpi.S(20))
                    _editor.FocusInlineTextInput();
            };
            _sizeBox.KeyUp += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                    _editor.FocusInlineTextInput();
            };
            _toolTip.SetToolTip(_sizeBox, Localization.T("editor_font_size"));
            Controls.Add(_sizeBox);

            _styleCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _styleCombo.Font = new Font("Segoe UI", 9f);
            _styleCombo.DropDownWidth = Dpi.S(160);
            foreach (var entry in FontStyles)
                _styleCombo.Items.Add(Localization.T(entry.LabelKey));
            int styleIndex = Array.FindIndex(FontStyles, s => s.Style == _editor._textFontStyle);
            if (styleIndex < 0) styleIndex = 1;
            _styleCombo.SelectedIndex = styleIndex;
            _styleCombo.SelectedIndexChanged += (_, _) =>
            {
                int index = _styleCombo.SelectedIndex;
                if (index >= 0)
                    _editor.SetTextStyle(FontStyles[index].Style);
            };
            _styleCombo.DropDownClosed += (_, _) => _editor.FocusInlineTextInput();
            _styleCombo.KeyUp += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                    _editor.FocusInlineTextInput();
            };
            _toolTip.SetToolTip(_styleCombo, Localization.T("editor_font_style"));
            Controls.Add(_styleCombo);

            _lineWidthCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _lineWidthCombo.Font = new Font("Segoe UI", 9f);
            _lineWidthCombo.DropDownWidth = Dpi.S(140);
            foreach (var key in LineWidthKeys)
                _lineWidthCombo.Items.Add(Localization.T(key));
            _lineWidthCombo.SelectedIndex = (int)_editor._lineThickness;
            _lineWidthCombo.SelectedIndexChanged += (_, _) =>
            {
                int index = _lineWidthCombo.SelectedIndex;
                if (index >= 0)
                    _editor.SetLineThickness((LineThickness)index);
            };
            _lineWidthCombo.DropDownClosed += (_, _) => _editor.FocusInlineTextInput();
            _lineWidthCombo.KeyUp += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                    _editor.FocusInlineTextInput();
            };
            _toolTip.SetToolTip(_lineWidthCombo, Localization.T("editor_line_width"));
            Controls.Add(_lineWidthCombo);

            _colorButton.Click += (_, _) => _editor.PickColor();
            _toolTip.SetToolTip(_colorButton, Localization.T("editor_color"));
            Controls.Add(_colorButton);
            _actionIconButtons.Add(_colorButton);

            AddAction(EditorIcon.Undo, "editor_undo", _editor.Undo);
            AddAction(EditorIcon.Save, "editor_save", _editor.SaveFromToolbar);
            AddAction(EditorIcon.Copy, "editor_copy", _editor.CopyFromToolbar);
            AddAction(EditorIcon.Close, "editor_close", _editor.Close);

            LayoutButtons();

            MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _dragging = true;
                Capture = true;
                _dragStartCursor = Cursor.Position;
                _dragStartToolbar = Location;
            };
            MouseMove += (_, _) =>
            {
                if (!_dragging) return;
                var point = Cursor.Position;
                int dx = point.X - _dragStartCursor.X;
                int dy = point.Y - _dragStartCursor.Y;
                var area = Screen.FromControl(this).WorkingArea;
                int x = Math.Clamp(_dragStartToolbar.X + dx, area.Left,
                    Math.Max(area.Left, area.Right - Width));
                int y = Math.Clamp(_dragStartToolbar.Y + dy, area.Top,
                    Math.Max(area.Top, area.Bottom - Height));
                Location = new Point(x, y);
            };
            MouseUp += (_, _) =>
            {
                _dragging = false;
                Capture = false;
            };
        }

        private void AddTool(EditorTool tool, EditorIcon icon, string tooltipKey)
        {
            var button = new IconToolButton(icon);
            button.Click += (_, _) => _editor.SelectTool(tool);
            _toolTip.SetToolTip(button, Localization.T(tooltipKey));
            Controls.Add(button);
            _toolButtons[tool] = button;
            _toolIconButtons.Add(button);
        }

        private void AddAction(EditorIcon icon, string tooltipKey, Action action)
        {
            var button = new IconToolButton(icon);
            button.Click += (_, _) => action();
            _toolTip.SetToolTip(button, Localization.T(tooltipKey));
            Controls.Add(button);
            _actionIconButtons.Add(button);
        }

        private void LayoutButtons()
        {
            int padding = Dpi.S(8);
            int gap = Dpi.S(4);
            int size = Dpi.S(28);
            int controlHeight = Dpi.S(24);
            int iconY = (Dpi.S(44) - size) / 2;
            int controlY = (Dpi.S(44) - controlHeight) / 2;
            int x = padding;

            foreach (var button in _toolIconButtons)
            {
                button.Bounds = new Rectangle(x, iconY, size, size);
                x += size + gap;
            }

            x += Dpi.S(4);
            _fontCombo.Bounds = new Rectangle(x, controlY, Dpi.S(130), controlHeight);
            x += Dpi.S(130) + Dpi.S(6);
            _sizeBox.Bounds = new Rectangle(x, controlY, Dpi.S(54), controlHeight);
            x += Dpi.S(54) + Dpi.S(8);
            _styleCombo.Bounds = new Rectangle(x, controlY, Dpi.S(96), controlHeight);
            x += Dpi.S(96) + Dpi.S(8);
            _lineWidthCombo.Bounds = new Rectangle(x, controlY, Dpi.S(84), controlHeight);
            x += Dpi.S(84) + Dpi.S(8);

            foreach (var button in _actionIconButtons)
            {
                button.Bounds = new Rectangle(x, iconY, size, size);
                x += size + gap;
            }

            int width = x - gap + padding;
            ClientSize = new Size(width, Dpi.S(44));
        }

        public void MoveBy(int dx, int dy)
        {
            if (dx == 0 && dy == 0)
                return;

            var area = SystemInformation.VirtualScreen;
            int x = Math.Clamp(Location.X + dx, area.Left,
                Math.Max(area.Left, area.Right - Width));
            int y = Math.Clamp(Location.Y + dy, area.Top,
                Math.Max(area.Top, area.Bottom - Height));
            Location = new Point(x, y);
        }

        public void SetActiveTool(EditorTool tool)
        {
            foreach (var pair in _toolButtons)
                pair.Value.SetActive(pair.Key == tool);
        }

        public void SetColor(Color color) => _colorButton.SetSwatch(color);

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Dpi.S(12));
            using var fill = new SolidBrush(BarColor);
            g.FillPath(fill, path);
            using var border = new Pen(Color.FromArgb(64, 255, 255, 255), 1f * Dpi.Scale);
            g.DrawPath(border, path);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using var path = RoundedRect(new Rectangle(0, 0, Width, Height), Dpi.S(12));
            var old = Region;
            Region = new Region(path);
            old?.Dispose();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !_editor.IsDisposed && !_editor._closing)
            {
                e.Cancel = true;
                _editor.Close();
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _toolTip.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class IconToolButton : Button
    {
        private readonly EditorIcon _icon;
        private bool _hover;
        private bool _pressed;
        private bool _active;
        private Color _swatch = Color.FromArgb(255, 64, 64);

        public IconToolButton(EditorIcon icon)
        {
            _icon = icon;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = BarColor;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        public void SetActive(bool active)
        {
            if (_active == active) return;
            _active = active;
            Invalidate();
        }

        public void SetSwatch(Color color)
        {
            _swatch = color;
            Invalidate();
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
            using (var background = new SolidBrush(BarColor))
                g.FillRectangle(background, ClientRectangle);

            var back = _pressed ? PressedColor : (_hover || _active) ? HoverColor : BarColor;
            var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            if (back != BarColor)
            {
                using var path = RoundedRect(rect, Dpi.S(7));
                using var fill = new SolidBrush(back);
                g.FillPath(fill, path);
            }

            var color = _active || _hover ? IconActiveColor : IconColor;
            DrawEditorIcon(g, new Rectangle(0, 0, Width, Height), _icon, color, _swatch);
        }
    }
}
