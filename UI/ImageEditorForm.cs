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
    private static readonly Color WorkspaceColor = Color.FromArgb(54, 58, 66);
    private static readonly Color BarColor = Color.FromArgb(38, 42, 50);
    private static readonly Color HoverColor = Color.FromArgb(54, 60, 70);
    private static readonly Color PressedColor = Color.FromArgb(30, 34, 40);
    private static readonly Color IconColor = Color.FromArgb(224, 232, 242);
    private static readonly Color IconActiveColor = Color.FromArgb(120, 190, 255);
    private static readonly Lazy<Icon> EditorWindowIcon = new(CreateEditorWindowIcon);

    private enum EditorTool
    {
        Rectangle,
        Square,
        Circle,
        Ellipse,
        Star,
        Heart,
        Text,
        Number,
        Arrow,
        ArrowDashed,
        Freeform,
        Mosaic,
        Move,
        Pan
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
        Circle,
        Ellipse,
        Star,
        Heart,
        Number,
        Arrow,
        ArrowDashed,
        Freeform,
        Mosaic,
        Move,
        Pan,
        Color,
        Undo,
        Save,
        Copy,
        Resize,
        Ocr,
        Paint,
        ZoomIn,
        ZoomOut,
        Close
    }

    private Bitmap _image;
    private string _path;
    private readonly List<Bitmap> _undo = new();
    private readonly List<ShapeAnnotation> _shapes = new();
    private readonly List<Point> _freeformPoints = new();
    private Bitmap? _shapeBase;
    private ShapeAnnotation? _movingShape;
    private ShapeAnnotation? _moveOriginal;
    private Point _moveStartImage;
    private bool _moveUndoPushed;
    private bool _filledShapes;
    private double _zoom = 1.0;
    private double _viewOffsetX;
    private double _viewOffsetY;
    private bool _panning;
    private Point _panStart;
    private double _panStartOffsetX;
    private double _panStartOffsetY;
    private bool _openPaintAfterClose;
    private bool _skipSaveOnClose;
    private readonly EditorCanvas _canvas;
    private readonly Label _statusLabel = new();
    private readonly Label _toast = new();
    private readonly System.Windows.Forms.Timer _toastTimer;
    private readonly int _displayWidth;
    private readonly int _displayHeight;
    private readonly double _displayScaleX;
    private readonly double _displayScaleY;
    private EditorToolbarPanel? _toolbar;
    private bool _viewInitialized;

    private EditorTool _tool = EditorTool.Rectangle;
    private Color _color = Color.FromArgb(255, 64, 64);
    private string _textFontFamily = "Segoe UI";
    private FontStyle _textFontStyle = FontStyle.Regular;
    private int _textFontSize = 24;
    private LineThickness _lineThickness = LineThickness.Medium;
    private int _nextNumber = 1;
    private bool _dragging;
    private Point _startImage;
    private Point _currentImage;
    private TextBox? _inlineInput;
    private InlineTextOverlay? _inlineOverlay;
    private Point _inlineImagePoint;
    private bool _inlineClosing;
    private bool _inlineCaretOn = true;
    private readonly System.Windows.Forms.Timer _inlineCaretTimer;

    private double ViewScaleX => _displayScaleX * _zoom;
    private double ViewScaleY => _displayScaleY * _zoom;
    private int EffectiveTextFontSize => Math.Max(1,
        (int)Math.Round(_textFontSize * (double)Dpi.Scale));

    public ImageEditorForm(Bitmap image, string path)
    {
        _image = image;
        _path = path;

        Text = Localization.T("editor_title");
        Icon = EditorWindowIcon.Value;
        ShowIcon = true;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = WorkspaceColor;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        ShowInTaskbar = true;
        MinimizeBox = true;
        MaximizeBox = true;

        int toolbarHeight = Dpi.S(76);
        int statusHeight = Dpi.S(24);
        int margin = Dpi.S(20);
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, image.Width, image.Height);
        int maxWidth = Math.Max(1, area.Width - Dpi.S(80) - margin * 2);
        int maxHeight = Math.Max(1, area.Height - Dpi.S(80) - toolbarHeight - statusHeight - margin * 2);
        double scale = Math.Min(1.0, Math.Min(maxWidth / (double)image.Width, maxHeight / (double)image.Height));
        if (scale <= 0) scale = 1;

        _displayWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
        _displayHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
        _displayScaleX = _displayWidth / (double)image.Width;
        _displayScaleY = _displayHeight / (double)image.Height;

        _toolbar = new EditorToolbarPanel(this) { Margin = Padding.Empty };
        _toolbar.SetActiveTool(_tool);
        _toolbar.SetColor(_color);

        int clientWidth = Math.Max(_displayWidth + margin * 2, _toolbar.PreferredWidth);
        int clientHeight = toolbarHeight + _displayHeight + margin * 2 + statusHeight;
        ClientSize = new Size(clientWidth, clientHeight);
        MinimumSize = SizeFromClientSize(new Size(
            Math.Max(Dpi.S(600), _toolbar.PreferredWidth), Dpi.S(320)));

        _canvas = new EditorCanvas(this)
        {
            Dock = DockStyle.Fill,
            BackColor = WorkspaceColor,
            Cursor = Cursors.Default,
            Margin = Padding.Empty
        };

        _statusLabel.AutoSize = false;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = IconColor;
        _statusLabel.BackColor = BarColor;
        _statusLabel.Padding = new Padding(Dpi.S(10), 0, Dpi.S(10), 0);
        _statusLabel.Font = new Font("Segoe UI", 9f);
        _statusLabel.Margin = Padding.Empty;
        UpdateStatus();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = WorkspaceColor,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, toolbarHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, statusHeight));
        root.Controls.Add(_toolbar, 0, 0);
        root.Controls.Add(_canvas, 0, 1);
        root.Controls.Add(_statusLabel, 0, 2);
        Controls.Add(root);

        _toast.AutoSize = true;
        _toast.BackColor = Color.FromArgb(220, 30, 32, 36);
        _toast.ForeColor = Color.White;
        _toast.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _toast.Padding = new Padding(Dpi.S(10), Dpi.S(6), Dpi.S(10), Dpi.S(6));
        _toast.Visible = false;
        _toast.Cursor = Cursors.Default;
        _canvas.Controls.Add(_toast);
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

        _canvas.Resize += (_, _) =>
        {
            if (!_viewInitialized)
                return;
            ClampViewOffset();
            _canvas.Invalidate();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplySystemWindowTheme();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        CenterView();
        _viewInitialized = true;
        UpdateStatus();
        _canvas.Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S))
        {
            SaveFromToolbar();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Z))
        {
            Undo();
            return true;
        }

        if (keyData == (Keys.Control | Keys.C))
        {
            CopyFromToolbar();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ApplySystemWindowTheme()
    {
        int useDark = SystemUsesDarkTheme() ? 1 : 0;
        try
        {
            NativeMethods.DwmSetWindowAttribute(Handle,
                NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }
        catch
        {
        }
    }

    private static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }

    private void SelectTool(EditorTool tool)
    {
        CommitInlineTextIfAny();
        _tool = tool;
        _canvas.Cursor = tool == EditorTool.Pan ? Cursors.Hand : Cursors.Default;
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
        int width = Math.Clamp((int)Math.Round(medium * factor), 1, 32);
        return Math.Clamp((int)Math.Round(width * (double)Dpi.Scale), 1, 96);
    }

    private float PreviewLineWidth()
    {
        float scale = (float)Math.Min(ViewScaleX, ViewScaleY);
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
        if (e.Button == MouseButtons.Middle)
        {
            BeginPan(canvas, e);
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        if (_inlineOverlay != null)
        {
            CommitInlineText();
            return;
        }

        if (!TryMapPoint(canvas.ClientSize, e.Location, out var point)) return;

        switch (_tool)
        {
            case EditorTool.Pan:
                BeginPan(canvas, e);
                return;
            case EditorTool.Move:
            {
                var shape = FindShape(point);
                if (shape != null)
                {
                    _movingShape = shape;
                    _moveOriginal = shape.Clone();
                    _moveStartImage = point;
                    _moveUndoPushed = false;
                    canvas.Capture = true;
                    canvas.Cursor = Cursors.SizeAll;
                }
                return;
            }
            case EditorTool.Text:
                BeginInlineText(point, e.Location);
                return;
            case EditorTool.Number:
                BakeShapes();
                PushUndo();
                using (var g = Graphics.FromImage(_image))
                    DrawNumber(g, _nextNumber++, point);
                canvas.Invalidate();
                return;
            case EditorTool.Freeform:
                _dragging = true;
                _freeformPoints.Clear();
                _freeformPoints.Add(point);
                _startImage = point;
                _currentImage = point;
                canvas.Capture = true;
                canvas.Invalidate();
                return;
            default:
                _dragging = true;
                _startImage = point;
                _currentImage = point;
                canvas.Capture = true;
                canvas.Invalidate();
                return;
        }
    }

    private void CanvasMouseMove(EditorCanvas canvas, MouseEventArgs e)
    {
        if (_panning)
        {
            _viewOffsetX = _panStartOffsetX + e.X - _panStart.X;
            _viewOffsetY = _panStartOffsetY + e.Y - _panStart.Y;
            ClampViewOffset();
            canvas.Invalidate();
            return;
        }

        if (_movingShape != null && _moveOriginal != null)
        {
            int moveX = (int)Math.Round((e.X - _viewOffsetX) / ViewScaleX);
            int moveY = (int)Math.Round((e.Y - _viewOffsetY) / ViewScaleY);
            var movePoint = ClampToImage(new Point(moveX, moveY));
            int dx = movePoint.X - _moveStartImage.X;
            int dy = movePoint.Y - _moveStartImage.Y;
            if (dx == 0 && dy == 0)
                return;

            if (!_moveUndoPushed)
            {
                PushUndo();
                _moveUndoPushed = true;
            }

            _movingShape.ApplyOffset(_moveOriginal, dx, dy);
            RenderShapes();
            canvas.Invalidate();
            return;
        }

        if (!_dragging)
        {
            UpdateCanvasCursor(canvas, e.Location);
            return;
        }

        int x = (int)Math.Round((e.X - _viewOffsetX) / ViewScaleX);
        int y = (int)Math.Round((e.Y - _viewOffsetY) / ViewScaleY);
        _currentImage = ClampToImage(new Point(x, y));

        if (_tool == EditorTool.Freeform)
        {
            var last = _freeformPoints.Count > 0 ? _freeformPoints[^1] : _currentImage;
            int dx = _currentImage.X - last.X;
            int dy = _currentImage.Y - last.Y;
            if (dx * dx + dy * dy >= 1)
                _freeformPoints.Add(_currentImage);
        }

        canvas.Invalidate();
    }

    private void CanvasMouseUp(EditorCanvas canvas, MouseEventArgs e)
    {
        if (_panning && (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle))
        {
            _panning = false;
            canvas.Capture = false;
            UpdateCanvasCursor(canvas, e.Location);
            canvas.Invalidate();
            return;
        }

        if (_movingShape != null && e.Button == MouseButtons.Left)
        {
            _movingShape = null;
            _moveOriginal = null;
            _moveUndoPushed = false;
            canvas.Capture = false;
            UpdateCanvasCursor(canvas, e.Location);
            canvas.Invalidate();
            return;
        }

        if (!_dragging || e.Button != MouseButtons.Left) return;
        _dragging = false;

        if (_tool == EditorTool.Freeform)
        {
            AddFreeformShape();
            canvas.Capture = false;
            UpdateCanvasCursor(canvas, e.Location);
            canvas.Invalidate();
            return;
        }

        var rect = Rectangle.FromLTRB(
            Math.Min(_startImage.X, _currentImage.X),
            Math.Min(_startImage.Y, _currentImage.Y),
            Math.Max(_startImage.X, _currentImage.X),
            Math.Max(_startImage.Y, _currentImage.Y));
        rect.Intersect(new Rectangle(0, 0, _image.Width, _image.Height));

        if (_tool is EditorTool.Arrow or EditorTool.ArrowDashed)
        {
            int dx = _currentImage.X - _startImage.X;
            int dy = _currentImage.Y - _startImage.Y;
            if (dx * dx + dy * dy < 4)
            {
                canvas.Capture = false;
                canvas.Invalidate();
                return;
            }
        }
        else if (rect.Width < 2 || rect.Height < 2)
        {
            canvas.Capture = false;
            canvas.Invalidate();
            return;
        }

        if (IsShapeTool(_tool))
        {
            AddShape(_tool, _startImage, _currentImage, null);
            canvas.Capture = false;
            UpdateCanvasCursor(canvas, e.Location);
            return;
        }

        if (_tool == EditorTool.Mosaic)
        {
            BakeShapes();
            PushUndo();
            var block = Math.Clamp(Math.Min(rect.Width, rect.Height) / 18, 6, 24);
            Pixelate(rect, block);
            canvas.Capture = false;
            canvas.Invalidate();
        }
    }

    private void CanvasMouseWheel(EditorCanvas canvas, MouseEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        ZoomBy(factor, e.Location, true);
    }

    private void UpdateCanvasCursor(EditorCanvas canvas, Point displayPoint)
    {
        if (_tool == EditorTool.Pan)
        {
            canvas.Cursor = Cursors.Hand;
            return;
        }

        if (_tool == EditorTool.Move)
        {
            bool overShape = TryMapPoint(canvas.ClientSize, displayPoint, out var point) &&
                             FindShape(point) != null;
            canvas.Cursor = overShape ? Cursors.SizeAll : Cursors.Default;
            return;
        }

        canvas.Cursor = Cursors.Default;
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
            BackColor = WorkspaceColor,
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
        using var font = new Font(_textFontFamily, EffectiveTextFontSize * (float)ViewScaleY,
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

        using (var background = new SolidBrush(WorkspaceColor))
            g.FillRectangle(background, 0, 0, overlay.Width, overlay.Height);

        var overlayRect = new RectangleF(overlay.Left, overlay.Top, overlay.Width, overlay.Height);
        var imageRect = new RectangleF(
            (float)_viewOffsetX,
            (float)_viewOffsetY,
            (float)(_image.Width * ViewScaleX),
            (float)(_image.Height * ViewScaleY));
        var intersect = RectangleF.Intersect(overlayRect, imageRect);
        if (intersect.Width > 0 && intersect.Height > 0)
        {
            var source = new RectangleF(
                (float)((intersect.Left - _viewOffsetX) / ViewScaleX),
                (float)((intersect.Top - _viewOffsetY) / ViewScaleY),
                (float)(intersect.Width / ViewScaleX),
                (float)(intersect.Height / ViewScaleY));
            var destination = new RectangleF(
                intersect.Left - overlay.Left,
                intersect.Top - overlay.Top,
                intersect.Width,
                intersect.Height);
            g.DrawImage(_image, destination, source, GraphicsUnit.Pixel);
        }

        string text = (_inlineInput?.Text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = text.Split('\n');
        int imageFont = EffectiveTextFontSize;
        using var font = new Font(_textFontFamily, imageFont * (float)ViewScaleY,
            _textFontStyle, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(_color);
        using var shadow = new SolidBrush(Color.FromArgb(220, 0, 0, 0));
        int offset = Math.Max(1, (int)Math.Round(Math.Max(1, imageFont / 14.0) * ViewScaleY));

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
            BakeShapes();
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

        using (var background = new SolidBrush(WorkspaceColor))
            g.FillRectangle(background, 0, 0, size.Width, size.Height);

        float viewWidth = (float)(_image.Width * ViewScaleX);
        float viewHeight = (float)(_image.Height * ViewScaleY);
        var page = new RectangleF(
            (float)_viewOffsetX, (float)_viewOffsetY, viewWidth, viewHeight);

        int shadow = Dpi.S(4);
        using (var shadowBrush = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            g.FillRectangle(shadowBrush, page.X + shadow, page.Y + shadow, page.Width, page.Height);

        g.DrawImage(_image, page.X, page.Y, page.Width, page.Height);

        using (var pagePen = new Pen(Color.FromArgb(172, 180, 192), 1f * Dpi.Scale))
            g.DrawRectangle(pagePen, page.X, page.Y, page.Width, page.Height);

        if (_dragging)
            DrawPreview(g);
    }

    private void DrawPreview(Graphics g)
    {
        PointF ToDisplay(Point p) => new(
            (float)(_viewOffsetX + p.X * ViewScaleX),
            (float)(_viewOffsetY + p.Y * ViewScaleY));

        PointF a = ToDisplay(_startImage);
        PointF b = ToDisplay(_currentImage);

        if (_tool == EditorTool.Mosaic)
        {
            using var pen = new Pen(_color, PreviewLineWidth()) { DashStyle = DashStyle.Dash };
            var rect = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            return;
        }

        if (_tool == EditorTool.Freeform)
        {
            var points = _freeformPoints.Select(ToDisplay).ToList();
            DrawShape(g, _tool, a, b, points, _color, PreviewLineWidth(), false, true);
            return;
        }

        if (IsShapeTool(_tool))
        {
            DrawShape(g, _tool, a, b, null, _color, PreviewLineWidth(), _filledShapes, true);
        }
    }

    private bool TryMapPoint(Size size, Point canvasPoint, out Point imagePoint)
    {
        imagePoint = default;
        int x = (int)Math.Round((canvasPoint.X - _viewOffsetX) / ViewScaleX);
        int y = (int)Math.Round((canvasPoint.Y - _viewOffsetY) / ViewScaleY);
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
        int fontSize = EffectiveTextFontSize;
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

        ClearShapeState();
        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        var oldSize = _image.Size;
        _image.Dispose();
        _image = previous;
        if (_image.Width != oldSize.Width || _image.Height != oldSize.Height)
        {
            CenterView();
            UpdateStatus();
        }
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

    private void ResizeImage()
    {
        CommitInlineTextIfAny();
        using var dialog = new ImageResizeForm(_image.Width, _image.Height);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        ApplyResize(dialog.TargetWidth, dialog.TargetHeight);
    }

    private void ApplyResize(int width, int height)
    {
        if (width == _image.Width && height == _image.Height)
            return;

        Bitmap resized;
        try
        {
            resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(resized);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.DrawImage(_image, 0, 0, width, height);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Localization.Format("editor_resize_failed", ex.Message), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        PushUndo();
        ClearShapeState();
        var old = _image;
        _image = resized;
        old.Dispose();
        CenterView();
        UpdateStatus();
        _canvas.Invalidate();
        ShowToast(Localization.T("editor_status_resized"));
    }

    private void RecognizeText()
    {
        CommitInlineTextIfAny();
        _ = RecognizeTextAsync();
    }

    private async Task RecognizeTextAsync()
    {
        try
        {
            using var snapshot = new Bitmap(_image);
            string text = await WindowsOcr.RecognizeAsync(snapshot);
            if (IsDisposed || Disposing)
                return;

            using var dialog = new OcrResultForm(text);
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Localization.Format("editor_ocr_failed", ex.Message), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenInPaint()
    {
        CommitInlineTextIfAny();
        _openPaintAfterClose = true;
        try
        {
            if (!SaveCurrentImage())
            {
                _openPaintAfterClose = false;
                return;
            }
        }
        catch (Exception ex)
        {
            _openPaintAfterClose = false;
            MessageBox.Show(this, Localization.Format("editor_error_save", ex.Message), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _skipSaveOnClose = true;
        Close();
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
        var canvasSize = _canvas.ClientSize;
        _toast.Location = new Point((canvasSize.Width - _toast.Width) / 2,
            canvasSize.Height - _toast.Height - Dpi.S(12));
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        CommitInlineTextIfAny();
        if (!_skipSaveOnClose)
        {
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
        }

        _toastTimer.Stop();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        if (_openPaintAfterClose && !string.IsNullOrWhiteSpace(_path))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "mspaint.exe",
                    Arguments = "\"" + _path + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(Localization.Format("editor_paint_failed", ex.Message), Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
            ClearShapeState();
            _image.Dispose();
            foreach (var bitmap in _undo)
                bitmap.Dispose();
            _undo.Clear();
        }
        base.Dispose(disposing);
    }

    private static Icon CreateEditorWindowIcon()
    {
        using var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var backgroundPath = RoundedRect(new Rectangle(2, 2, 60, 60), 13);
            using (var background = new SolidBrush(BarColor))
                g.FillPath(background, backgroundPath);
            using (var edge = new Pen(Color.FromArgb(70, 255, 255, 255), 2f))
                g.DrawPath(edge, backgroundPath);

            using var line = new Pen(IconColor, 3.4f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            using (var frame = RoundedRect(new Rectangle(12, 12, 33, 30), 3))
                g.DrawPath(line, frame);
            g.DrawEllipse(line, 17, 17, 7, 7);
            g.DrawLine(line, 15, 37, 25, 24);
            g.DrawLine(line, 25, 24, 35, 37);
            g.DrawLine(line, 29, 37, 36, 28);
            g.DrawLine(line, 36, 28, 43, 37);

            using var pencil = new Pen(IconActiveColor, 4.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawLine(pencil, 42, 53, 56, 39);
            g.DrawLine(line, 56, 39, 59, 36);
            g.DrawLine(line, 53, 36, 59, 42);
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
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
            case EditorIcon.Circle:
            {
                int side = Math.Min(inner.Width, inner.Height);
                g.DrawEllipse(pen, cx - side / 2, cy - side / 2, side, side);
                break;
            }
            case EditorIcon.Ellipse:
                g.DrawEllipse(pen, inner.Left, inner.Top + Dpi.S(2), inner.Width, inner.Height - Dpi.S(4));
                break;
            case EditorIcon.Star:
            {
                using var path = StarPath(new RectangleF(inner.Left, inner.Top, inner.Width, inner.Height));
                g.DrawPath(pen, path);
                break;
            }
            case EditorIcon.Heart:
            {
                using var path = HeartPath(new RectangleF(inner.Left, inner.Top, inner.Width, inner.Height));
                g.DrawPath(pen, path);
                break;
            }
            case EditorIcon.Freeform:
            {
                var points = new[]
                {
                    new PointF(inner.Left, inner.Bottom - Dpi.S(2)),
                    new PointF(inner.Left + Dpi.S(4), inner.Top + Dpi.S(3)),
                    new PointF(cx, cy),
                    new PointF(inner.Right - Dpi.S(4), inner.Bottom - Dpi.S(4)),
                    new PointF(inner.Right, inner.Top + Dpi.S(2))
                };
                g.DrawCurve(pen, points);
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
            case EditorIcon.Move:
            {
                g.DrawLine(pen, inner.Left, cy, inner.Right, cy);
                g.DrawLine(pen, cx, inner.Top, cx, inner.Bottom);
                int head = Dpi.S(3);
                g.DrawLine(pen, inner.Left, cy, inner.Left + head, cy - head);
                g.DrawLine(pen, inner.Left, cy, inner.Left + head, cy + head);
                g.DrawLine(pen, inner.Right, cy, inner.Right - head, cy - head);
                g.DrawLine(pen, inner.Right, cy, inner.Right - head, cy + head);
                g.DrawLine(pen, cx, inner.Top, cx - head, inner.Top + head);
                g.DrawLine(pen, cx, inner.Top, cx + head, inner.Top + head);
                g.DrawLine(pen, cx, inner.Bottom, cx - head, inner.Bottom - head);
                g.DrawLine(pen, cx, inner.Bottom, cx + head, inner.Bottom - head);
                break;
            }
            case EditorIcon.Pan:
            {
                var palm = Rectangle.FromLTRB(cx - Dpi.S(4), cy - Dpi.S(1),
                    cx + Dpi.S(4), cy + Dpi.S(7));
                using var hand = RoundedRect(palm, Dpi.S(2));
                g.DrawPath(pen, hand);
                g.DrawLine(pen, cx - Dpi.S(3), cy - Dpi.S(1), cx - Dpi.S(3), cy - Dpi.S(6));
                g.DrawLine(pen, cx, cy - Dpi.S(1), cx, cy - Dpi.S(8));
                g.DrawLine(pen, cx + Dpi.S(3), cy - Dpi.S(1), cx + Dpi.S(3), cy - Dpi.S(6));
                g.DrawLine(pen, cx - Dpi.S(4), cy + Dpi.S(2), cx - Dpi.S(7), cy - Dpi.S(2));
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
            case EditorIcon.Resize:
            {
                var frame = new Rectangle(inner.Left, inner.Top + Dpi.S(3),
                    Math.Max(Dpi.S(8), inner.Width - Dpi.S(4)), Math.Max(Dpi.S(8), inner.Height - Dpi.S(7)));
                g.DrawRectangle(pen, frame);
                int startX = frame.Right - Dpi.S(5);
                int startY = frame.Bottom - Dpi.S(3);
                g.DrawLine(pen, startX, startY, inner.Right, inner.Top + Dpi.S(1));
                g.DrawLine(pen, inner.Right, inner.Top + Dpi.S(1),
                    inner.Right - Dpi.S(4), inner.Top + Dpi.S(1));
                g.DrawLine(pen, inner.Right, inner.Top + Dpi.S(1),
                    inner.Right, inner.Top + Dpi.S(5));
                break;
            }
            case EditorIcon.Ocr:
            {
                int inset = Dpi.S(1);
                int corner = Dpi.S(4);
                g.DrawLine(pen, inner.Left, inner.Top + inset, inner.Left + corner, inner.Top + inset);
                g.DrawLine(pen, inner.Left + inset, inner.Top, inner.Left + inset, inner.Top + corner);
                g.DrawLine(pen, inner.Right - corner, inner.Top + inset, inner.Right, inner.Top + inset);
                g.DrawLine(pen, inner.Right - inset, inner.Top, inner.Right - inset, inner.Top + corner);
                g.DrawLine(pen, inner.Left, inner.Bottom - inset, inner.Left + corner, inner.Bottom - inset);
                g.DrawLine(pen, inner.Left + inset, inner.Bottom - corner, inner.Left + inset, inner.Bottom);
                g.DrawLine(pen, inner.Right - corner, inner.Bottom - inset, inner.Right, inner.Bottom - inset);
                g.DrawLine(pen, inner.Right - inset, inner.Bottom - corner, inner.Right - inset, inner.Bottom);
                g.DrawLine(pen, cx - Dpi.S(3), cy + Dpi.S(2), cx, cy - Dpi.S(2));
                g.DrawLine(pen, cx, cy - Dpi.S(2), cx + Dpi.S(3), cy + Dpi.S(2));
                g.DrawLine(pen, cx - Dpi.S(2), cy + Dpi.S(1), cx + Dpi.S(2), cy + Dpi.S(1));
                break;
            }
            case EditorIcon.Paint:
            {
                var palette = new Rectangle(inner.Left, inner.Top + Dpi.S(2), inner.Width, inner.Height - Dpi.S(3));
                g.DrawEllipse(pen, palette);
                using var dot = new SolidBrush(color);
                g.FillEllipse(dot, palette.Left + Dpi.S(3), palette.Top + Dpi.S(3), Dpi.S(2), Dpi.S(2));
                g.FillEllipse(dot, palette.Left + Dpi.S(7), palette.Top + Dpi.S(2), Dpi.S(2), Dpi.S(2));
                g.FillEllipse(dot, palette.Left + Dpi.S(10), palette.Top + Dpi.S(5), Dpi.S(2), Dpi.S(2));
                g.DrawLine(pen, cx + Dpi.S(2), cy + Dpi.S(2), inner.Right, inner.Top);
                g.DrawLine(pen, cx + Dpi.S(2), cy + Dpi.S(2), cx + Dpi.S(4), cy + Dpi.S(4));
                break;
            }
            case EditorIcon.ZoomIn:
            case EditorIcon.ZoomOut:
            {
                var lens = new Rectangle(inner.Left, inner.Top, Math.Max(Dpi.S(8), inner.Width - Dpi.S(5)),
                    Math.Max(Dpi.S(8), inner.Height - Dpi.S(5)));
                g.DrawEllipse(pen, lens);
                g.DrawLine(pen, lens.Right - Dpi.S(2), lens.Bottom - Dpi.S(2), inner.Right, inner.Bottom);
                int mx = lens.Left + lens.Width / 2;
                int my = lens.Top + lens.Height / 2;
                g.DrawLine(pen, mx - Dpi.S(3), my, mx + Dpi.S(3), my);
                if (icon == EditorIcon.ZoomIn)
                    g.DrawLine(pen, mx, my - Dpi.S(3), mx, my + Dpi.S(3));
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

    private sealed class ShapeAnnotation
    {
        public EditorTool Tool { get; init; }
        public Point Start { get; set; }
        public Point End { get; set; }
        public List<Point>? Points { get; set; }
        public Color Color { get; init; }
        public int Width { get; init; }
        public bool Filled { get; init; }

        public ShapeAnnotation Clone() => new()
        {
            Tool = Tool,
            Start = Start,
            End = End,
            Points = Points?.ToList(),
            Color = Color,
            Width = Width,
            Filled = Filled
        };

        public void ApplyOffset(ShapeAnnotation original, int dx, int dy)
        {
            Start = new Point(original.Start.X + dx, original.Start.Y + dy);
            End = new Point(original.End.X + dx, original.End.Y + dy);
            if (original.Points != null)
                Points = original.Points.Select(p => new Point(p.X + dx, p.Y + dy)).ToList();
        }

        public Rectangle GetBounds()
        {
            if (Points is { Count: > 0 })
            {
                int minX = Points[0].X;
                int minY = Points[0].Y;
                int maxX = minX;
                int maxY = minY;
                foreach (var p in Points)
                {
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                }
                return Rectangle.FromLTRB(minX, minY, maxX, maxY);
            }

            return Rectangle.FromLTRB(
                Math.Min(Start.X, End.X),
                Math.Min(Start.Y, End.Y),
                Math.Max(Start.X, End.X),
                Math.Max(Start.Y, End.Y));
        }

        public bool HitTest(Point point)
        {
            var bounds = GetBounds();
            int padding = Math.Max(10, Width + 6);
            bounds.Inflate(padding, padding);
            return bounds.Contains(point);
        }
    }

    private static RectangleF NormalizedRect(PointF a, PointF b) => RectangleF.FromLTRB(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    private static RectangleF SquareRect(PointF a, PointF b)
    {
        float dx = b.X - a.X;
        float dy = b.Y - a.Y;
        float side = Math.Min(Math.Abs(dx), Math.Abs(dy));
        float x = dx < 0 ? a.X - side : a.X;
        float y = dy < 0 ? a.Y - side : a.Y;
        return new RectangleF(x, y, side, side);
    }

    private static GraphicsPath StarPath(RectangleF rect)
    {
        var path = new GraphicsPath();
        float cx = rect.Left + rect.Width / 2f;
        float cy = rect.Top + rect.Height / 2f;
        float outer = Math.Min(rect.Width, rect.Height) / 2f;
        float inner = outer * 0.45f;
        PointF[] points = new PointF[10];
        for (int i = 0; i < 10; i++)
        {
            double angle = -Math.PI / 2 + i * Math.PI / 5;
            float radius = i % 2 == 0 ? outer : inner;
            points[i] = new PointF(
                cx + (float)Math.Cos(angle) * radius,
                cy + (float)Math.Sin(angle) * radius);
        }
        path.AddPolygon(points);
        return path;
    }

    private static GraphicsPath HeartPath(RectangleF rect)
    {
        float x = rect.Left;
        float y = rect.Top;
        float w = rect.Width;
        float h = rect.Height;
        var path = new GraphicsPath();
        path.StartFigure();
        path.AddBezier(
            x + w * 0.5f, y + h * 0.24f,
            x + w * 0.16f, y - h * 0.02f,
            x - w * 0.02f, y + h * 0.34f,
            x + w * 0.5f, y + h * 0.95f);
        path.AddBezier(
            x + w * 0.5f, y + h * 0.95f,
            x + w * 1.02f, y + h * 0.34f,
            x + w * 0.84f, y - h * 0.02f,
            x + w * 0.5f, y + h * 0.24f);
        path.CloseFigure();
        return path;
    }

    private static bool IsShapeTool(EditorTool tool) => tool is
        EditorTool.Rectangle or EditorTool.Square or EditorTool.Circle or EditorTool.Ellipse or
        EditorTool.Star or EditorTool.Heart or EditorTool.Arrow or EditorTool.ArrowDashed or
        EditorTool.Freeform;

    private void DrawShape(Graphics g, EditorTool tool, PointF a, PointF b,
        IReadOnlyList<PointF>? points, Color color, float width, bool filled, bool preview)
    {
        using var pen = new Pen(color, Math.Max(1f, width))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        if (preview && tool is not EditorTool.Arrow and not EditorTool.ArrowDashed)
            pen.DashStyle = DashStyle.Dash;
        if (tool == EditorTool.ArrowDashed)
            pen.DashStyle = DashStyle.Dash;

        using var fillBrush = new SolidBrush(preview ? Color.FromArgb(90, color) : color);

        switch (tool)
        {
            case EditorTool.Rectangle:
            case EditorTool.Square:
            case EditorTool.Circle:
            case EditorTool.Ellipse:
            case EditorTool.Star:
            case EditorTool.Heart:
            {
                var rect = tool switch
                {
                    EditorTool.Square or EditorTool.Circle => SquareRect(a, b),
                    _ => NormalizedRect(a, b)
                };
                if (rect.Width < 1 || rect.Height < 1)
                    break;

                if (filled)
                {
                    if (tool is EditorTool.Rectangle or EditorTool.Square)
                        g.FillRectangle(fillBrush, rect);
                    else if (tool is EditorTool.Circle or EditorTool.Ellipse)
                        g.FillEllipse(fillBrush, rect);
                    else
                    {
                        using var fillPath = tool == EditorTool.Star ? StarPath(rect) : HeartPath(rect);
                        g.FillPath(fillBrush, fillPath);
                    }
                }

                if (tool is EditorTool.Rectangle or EditorTool.Square)
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                else if (tool is EditorTool.Circle or EditorTool.Ellipse)
                    g.DrawEllipse(pen, rect);
                else
                {
                    using var path = tool == EditorTool.Star ? StarPath(rect) : HeartPath(rect);
                    g.DrawPath(pen, path);
                }
                break;
            }
            case EditorTool.Arrow:
            case EditorTool.ArrowDashed:
            {
                pen.CustomEndCap = new AdjustableArrowCap(4f, 5f, true);
                g.DrawLine(pen, a, b);
                break;
            }
            case EditorTool.Freeform:
            {
                if (points is { Count: > 1 })
                {
                    var path = new GraphicsPath();
                    path.AddLines(points.ToArray());
                    g.DrawPath(pen, path);
                    path.Dispose();
                }
                break;
            }
        }
    }

    private void DrawShapeToImage(Graphics g, ShapeAnnotation shape)
    {
        var points = shape.Points?.Select(p => (PointF)p).ToList();
        DrawShape(g, shape.Tool, shape.Start, shape.End, points,
            shape.Color, shape.Width, shape.Filled, false);
    }

    private void EnsureShapeBase()
    {
        _shapeBase ??= new Bitmap(_image);
    }

    private void RenderShapes()
    {
        if (_shapeBase == null)
            return;

        var next = new Bitmap(_shapeBase);
        using (var g = Graphics.FromImage(next))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var shape in _shapes)
                DrawShapeToImage(g, shape);
        }

        var old = _image;
        _image = next;
        old.Dispose();
    }

    private void ClearShapeState()
    {
        _shapes.Clear();
        _shapeBase?.Dispose();
        _shapeBase = null;
        _movingShape = null;
        _moveOriginal = null;
    }

    private void BakeShapes()
    {
        if (_shapes.Count == 0)
            return;

        ClearShapeState();
    }

    private ShapeAnnotation? FindShape(Point imagePoint)
    {
        for (int i = _shapes.Count - 1; i >= 0; i--)
        {
            if (_shapes[i].HitTest(imagePoint))
                return _shapes[i];
        }
        return null;
    }

    private void AddShape(EditorTool tool, Point start, Point end, List<Point>? points)
    {
        PushUndo();
        EnsureShapeBase();
        _shapes.Add(new ShapeAnnotation
        {
            Tool = tool,
            Start = start,
            End = end,
            Points = points,
            Color = _color,
            Width = LineWidth(),
            Filled = _filledShapes
        });
        RenderShapes();
        _canvas.Invalidate();
    }

    private void AddFreeformShape()
    {
        if (_freeformPoints.Count < 2)
        {
            _freeformPoints.Clear();
            return;
        }

        var points = _freeformPoints.ToList();
        var start = points[0];
        var end = points[^1];
        _freeformPoints.Clear();
        AddShape(EditorTool.Freeform, start, end, points);
    }

    private void SetFilled(bool filled) => _filledShapes = filled;

    private void ZoomIn() => ZoomBy(1.25, null, true);

    private void ZoomOut() => ZoomBy(1 / 1.25);

    private void ZoomBy(double factor, Point? focus = null, bool activatePan = false)
    {
        CommitInlineTextIfAny();
        double oldZoom = _zoom;
        double nextZoom = Math.Clamp(oldZoom * factor, 0.25, 4.0);
        if (Math.Abs(nextZoom - oldZoom) < 0.0001)
            return;

        double viewportCx = focus?.X ?? _canvas.ClientSize.Width / 2.0;
        double viewportCy = focus?.Y ?? _canvas.ClientSize.Height / 2.0;
        double imageX = (viewportCx - _viewOffsetX) / (_displayScaleX * oldZoom);
        double imageY = (viewportCy - _viewOffsetY) / (_displayScaleY * oldZoom);

        _zoom = nextZoom;
        _viewOffsetX = viewportCx - imageX * ViewScaleX;
        _viewOffsetY = viewportCy - imageY * ViewScaleY;
        ClampViewOffset();
        UpdateStatus();
        _canvas.Invalidate();

        bool largerThanViewport = _image.Width * ViewScaleX > _canvas.ClientSize.Width + 1 ||
                                  _image.Height * ViewScaleY > _canvas.ClientSize.Height + 1;
        if (activatePan && nextZoom > oldZoom && largerThanViewport && _tool != EditorTool.Pan)
            SelectTool(EditorTool.Pan);
    }

    private void ClampViewOffset()
    {
        int viewportW = _canvas.ClientSize.Width;
        int viewportH = _canvas.ClientSize.Height;
        if (viewportW <= 0 || viewportH <= 0)
            return;

        double viewW = _image.Width * ViewScaleX;
        double viewH = _image.Height * ViewScaleY;

        if (viewW <= viewportW)
            _viewOffsetX = (viewportW - viewW) / 2;
        else
            _viewOffsetX = Math.Clamp(_viewOffsetX, viewportW - viewW, 0);

        if (viewH <= viewportH)
            _viewOffsetY = (viewportH - viewH) / 2;
        else
            _viewOffsetY = Math.Clamp(_viewOffsetY, viewportH - viewH, 0);
    }

    private void CenterView()
    {
        int viewportW = _canvas.ClientSize.Width;
        int viewportH = _canvas.ClientSize.Height;
        _viewOffsetX = (viewportW - _image.Width * ViewScaleX) / 2.0;
        _viewOffsetY = (viewportH - _image.Height * ViewScaleY) / 2.0;
    }

    private void UpdateStatus()
    {
        if (_statusLabel.IsDisposed)
            return;

        _statusLabel.Text = $"{Math.Round(ViewScaleX * 100)}%    {_image.Width} × {_image.Height}";
    }

    private void BeginPan(EditorCanvas canvas, MouseEventArgs e)
    {
        CommitInlineTextIfAny();
        _panning = true;
        _panStart = e.Location;
        _panStartOffsetX = _viewOffsetX;
        _panStartOffsetY = _viewOffsetY;
        canvas.Capture = true;
        canvas.Cursor = Cursors.Hand;
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

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _owner.CanvasMouseWheel(this, e);
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

    private sealed class EditorToolbarPanel : Panel
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
        private readonly CheckBox _fillCheck = new();
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

        public int PreferredWidth { get; private set; }

        public EditorToolbarPanel(ImageEditorForm editor)
        {
            _editor = editor;

            BackColor = BarColor;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9f);
            Dock = DockStyle.Fill;

            AddTool(EditorTool.Text, EditorIcon.Text, "editor_tool_text");
            AddTool(EditorTool.Rectangle, EditorIcon.Rectangle, "editor_tool_rect");
            AddTool(EditorTool.Square, EditorIcon.Square, "editor_tool_square");
            AddTool(EditorTool.Circle, EditorIcon.Circle, "editor_tool_circle");
            AddTool(EditorTool.Ellipse, EditorIcon.Ellipse, "editor_tool_ellipse");
            AddTool(EditorTool.Star, EditorIcon.Star, "editor_tool_star");
            AddTool(EditorTool.Heart, EditorIcon.Heart, "editor_tool_heart");
            AddTool(EditorTool.Number, EditorIcon.Number, "editor_tool_number");
            AddTool(EditorTool.Arrow, EditorIcon.Arrow, "editor_tool_arrow");
            AddTool(EditorTool.ArrowDashed, EditorIcon.ArrowDashed, "editor_tool_arrow_dashed");
            AddTool(EditorTool.Freeform, EditorIcon.Freeform, "editor_tool_freeform");
            AddTool(EditorTool.Mosaic, EditorIcon.Mosaic, "editor_tool_mosaic");
            AddTool(EditorTool.Move, EditorIcon.Move, "editor_tool_move");
            AddTool(EditorTool.Pan, EditorIcon.Pan, "editor_tool_pan");

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

            _fillCheck.Text = Localization.T("editor_fill");
            _fillCheck.Font = new Font("Segoe UI", 9f);
            _fillCheck.AutoSize = false;
            _fillCheck.BackColor = BarColor;
            _fillCheck.ForeColor = IconColor;
            _fillCheck.FlatStyle = FlatStyle.Flat;
            _fillCheck.FlatAppearance.BorderColor = HoverColor;
            _fillCheck.TextAlign = ContentAlignment.MiddleLeft;
            _fillCheck.Checked = _editor._filledShapes;
            _fillCheck.CheckedChanged += (_, _) => _editor.SetFilled(_fillCheck.Checked);
            _toolTip.SetToolTip(_fillCheck, Localization.T("editor_fill"));
            Controls.Add(_fillCheck);

            _colorButton.Click += (_, _) => _editor.PickColor();
            _toolTip.SetToolTip(_colorButton, Localization.T("editor_color"));
            Controls.Add(_colorButton);
            _actionIconButtons.Add(_colorButton);

            AddAction(EditorIcon.Undo, "editor_undo", _editor.Undo);
            AddAction(EditorIcon.Save, "editor_save", _editor.SaveFromToolbar);
            AddAction(EditorIcon.Copy, "editor_copy", _editor.CopyFromToolbar);
            AddAction(EditorIcon.Resize, "editor_resize_image", _editor.ResizeImage);
            AddAction(EditorIcon.Ocr, "editor_ocr", _editor.RecognizeText);
            AddAction(EditorIcon.Paint, "editor_paint", _editor.OpenInPaint);
            AddAction(EditorIcon.ZoomOut, "editor_zoom_out", _editor.ZoomOut);
            AddAction(EditorIcon.ZoomIn, "editor_zoom_in", _editor.ZoomIn);
            AddAction(EditorIcon.Close, "editor_close", _editor.Close);

            LayoutButtons();
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
            int iconY = Dpi.S(8);
            int controlY = Dpi.S(44);
            int x = padding;

            foreach (var button in _toolIconButtons)
            {
                button.Bounds = new Rectangle(x, iconY, size, size);
                x += size + gap;
            }

            x += Dpi.S(8);
            foreach (var button in _actionIconButtons)
            {
                button.Bounds = new Rectangle(x, iconY, size, size);
                x += size + gap;
            }

            int row1Width = x - gap + padding;

            x = padding;
            _fontCombo.Bounds = new Rectangle(x, controlY, Dpi.S(130), controlHeight);
            x += Dpi.S(130) + Dpi.S(6);
            _sizeBox.Bounds = new Rectangle(x, controlY, Dpi.S(54), controlHeight);
            x += Dpi.S(54) + Dpi.S(8);
            _styleCombo.Bounds = new Rectangle(x, controlY, Dpi.S(96), controlHeight);
            x += Dpi.S(96) + Dpi.S(8);
            _lineWidthCombo.Bounds = new Rectangle(x, controlY, Dpi.S(84), controlHeight);
            x += Dpi.S(84) + Dpi.S(8);
            _fillCheck.Bounds = new Rectangle(x, controlY + Dpi.S(1), Dpi.S(82), controlHeight - Dpi.S(2));
            x += Dpi.S(82) + Dpi.S(8);

            int row2Width = x - Dpi.S(8) + padding;
            PreferredWidth = Math.Max(row1Width, row2Width);
            Height = Dpi.S(76);
        }

        public void SetActiveTool(EditorTool tool)
        {
            foreach (var pair in _toolButtons)
                pair.Value.SetActive(pair.Key == tool);
        }

        public void SetColor(Color color) => _colorButton.SetSwatch(color);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Color.FromArgb(54, 255, 255, 255), 1f * Dpi.Scale);
            e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
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
