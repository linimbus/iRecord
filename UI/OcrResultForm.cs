// iRecord - OCR result dialog.
// Shows recognized text with copy/close actions and an empty-result message.
using System.Drawing;
using iRecord.App;

namespace iRecord.UI;

/// <summary>
/// Read-only result window for OCR output.
/// </summary>
public sealed class OcrResultForm : Form
{
    private readonly TextBox _textBox;
    private readonly Label _status;

    /// <summary>Displays read-only OCR text with copy/close actions.</summary>
    public OcrResultForm(string text)
    {
        Text = Localization.T("editor_ocr_title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(Dpi.S(560), Dpi.S(400));
        BackColor = Color.FromArgb(245, 247, 250);
        Padding = new Padding(Dpi.S(12));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.S(42)));

        string displayText = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
        _textBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Font = new Font("Segoe UI", 10f),
            Text = displayText
        };

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, Dpi.S(6), 0, 0)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _status = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = Color.FromArgb(80, 90, 105),
            Text = string.IsNullOrWhiteSpace(text) ? Localization.T("editor_ocr_empty") : string.Empty,
            Margin = new Padding(Dpi.S(4), 0, 0, 0)
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        var copyButton = new Button
        {
            Text = Localization.T("editor_ocr_copy"),
            AutoSize = true,
            Enabled = !string.IsNullOrWhiteSpace(text),
            Margin = new Padding(Dpi.S(6), 0, 0, 0)
        };
        copyButton.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(_textBox.Text);
                _status.Text = Localization.T("editor_ocr_copied");
            }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
            }
        };
        var closeButton = new Button
        {
            Text = Localization.T("editor_ocr_close"),
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(Dpi.S(6), 0, 0, 0)
        };

        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(copyButton);
        bottom.Controls.Add(_status, 0, 0);
        bottom.Controls.Add(buttons, 1, 0);

        layout.Controls.Add(_textBox, 0, 0);
        layout.Controls.Add(bottom, 0, 1);
        Controls.Add(layout);

        AcceptButton = copyButton;
        CancelButton = closeButton;
    }
}
