using System.Drawing;
using iRecord.App;

namespace iRecord.UI;

public sealed class ImageResizeForm : Form
{
    private const int MaxDimension = 20000;
    private const long MaxPixels = 80_000_000;

    private readonly int _originalWidth;
    private readonly int _originalHeight;
    private readonly NumericUpDown _width = new();
    private readonly NumericUpDown _height = new();
    private readonly NumericUpDown _percent = new();
    private readonly CheckBox _keepAspect = new();
    private bool _syncing;

    public int TargetWidth => (int)_width.Value;
    public int TargetHeight => (int)_height.Value;

    public ImageResizeForm(int width, int height)
    {
        _originalWidth = Math.Max(1, width);
        _originalHeight = Math.Max(1, height);

        Text = Localization.T("editor_resize_title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(Dpi.S(380), Dpi.S(258));

        var original = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 98, 110),
            Text = Localization.Format("editor_resize_original", _originalWidth, _originalHeight),
            Location = new Point(Dpi.S(20), Dpi.S(18))
        };
        var widthLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = Localization.T("editor_resize_width"),
            Bounds = Dpi.R(20, 52, 100, 26)
        };
        var heightLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = Localization.T("editor_resize_height"),
            Bounds = Dpi.R(20, 90, 100, 26)
        };
        var percentLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = Localization.T("editor_resize_percent"),
            Bounds = Dpi.R(20, 128, 100, 26)
        };

        _width.Bounds = Dpi.R(128, 52, 120, 26);
        _height.Bounds = Dpi.R(128, 90, 120, 26);
        _percent.Bounds = Dpi.R(128, 128, 120, 26);
        foreach (var numeric in new[] { _width, _height })
        {
            numeric.Minimum = 1;
            numeric.Maximum = MaxDimension;
            numeric.Increment = 1;
        }
        _percent.Minimum = 1;
        _percent.Maximum = 400;
        _percent.Increment = 5;

        _keepAspect.Bounds = Dpi.R(128, 164, 220, 24);
        _keepAspect.Text = Localization.T("editor_resize_keep_aspect");
        _keepAspect.Checked = true;

        var apply = new Button
        {
            Text = Localization.T("editor_resize_apply"),
            Bounds = Dpi.R(160, 208, 90, 32)
        };
        var cancel = new Button
        {
            Text = Localization.T("editor_resize_cancel"),
            Bounds = Dpi.R(262, 208, 90, 32),
            DialogResult = DialogResult.Cancel
        };

        _syncing = true;
        _width.Value = Math.Clamp(_originalWidth, 1, MaxDimension);
        _height.Value = Math.Clamp(_originalHeight, 1, MaxDimension);
        _percent.Value = 100;
        _syncing = false;

        _width.ValueChanged += (_, _) => SyncFromWidth();
        _height.ValueChanged += (_, _) => SyncFromHeight();
        _percent.ValueChanged += (_, _) => SyncFromPercent();
        _keepAspect.CheckedChanged += (_, _) =>
        {
            if (!_syncing && _keepAspect.Checked)
                SyncFromWidth();
        };
        apply.Click += (_, _) =>
        {
            long pixels = (long)_width.Value * (long)_height.Value;
            if (pixels > MaxPixels)
            {
                MessageBox.Show(this, Localization.T("editor_resize_too_large"), Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
        };

        Controls.AddRange(new Control[]
        {
            original, widthLabel, heightLabel, percentLabel,
            _width, _height, _percent, _keepAspect, apply, cancel
        });
        AcceptButton = apply;
        CancelButton = cancel;
    }

    private double Aspect => _originalWidth / (double)_originalHeight;

    private void SyncFromWidth()
    {
        if (_syncing)
            return;

        _syncing = true;
        if (_keepAspect.Checked)
            _height.Value = Math.Clamp((int)Math.Round((double)_width.Value / Aspect), 1, MaxDimension);
        _percent.Value = Math.Clamp((int)Math.Round((double)_width.Value * 100.0 / _originalWidth), 1, 400);
        _syncing = false;
    }

    private void SyncFromHeight()
    {
        if (_syncing)
            return;

        _syncing = true;
        if (_keepAspect.Checked)
            _width.Value = Math.Clamp((int)Math.Round((double)_height.Value * Aspect), 1, MaxDimension);
        _percent.Value = Math.Clamp((int)Math.Round((double)_width.Value * 100.0 / _originalWidth), 1, 400);
        _syncing = false;
    }

    private void SyncFromPercent()
    {
        if (_syncing)
            return;

        _syncing = true;
        _width.Value = Math.Clamp((int)Math.Round(_originalWidth * _percent.Value / 100m), 1, MaxDimension);
        _height.Value = Math.Clamp((int)Math.Round(_originalHeight * _percent.Value / 100m), 1, MaxDimension);
        _syncing = false;
    }
}
