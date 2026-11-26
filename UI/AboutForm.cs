using System.Diagnostics;
using System.Drawing;
using iRecord.App;

namespace iRecord.UI;

public sealed class AboutForm : Form
{
    private const string OfficialUrl = "https://github.com/linimbus/iRecordWindows";

    public AboutForm()
    {
        Text = Localization.T("about_title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(Dpi.S(560), Dpi.S(420));
        BackColor = Color.FromArgb(245, 247, 250);
        Padding = new Padding(Dpi.S(12));

        string text = Localization.T("about_text");
        string link = Localization.T("about_official_link") + OfficialUrl;
        text = text.Contains("© 2026 iRecord")
            ? text.Replace("© 2026 iRecord", link + "\n\n© 2026 iRecord")
            : text + "\n\n" + link;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.S(42)));

        var textBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Font = new Font("Segoe UI", 10f),
            TabStop = false,
            Text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n")
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, Dpi.S(6), 0, 0)
        };

        var close = new Button
        {
            Text = Localization.T("about_close"),
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(Dpi.S(6), 0, 0, 0)
        };
        var website = new Button
        {
            Text = Localization.T("about_open_website"),
            AutoSize = true,
            Margin = new Padding(Dpi.S(6), 0, 0, 0)
        };
        website.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(OfficialUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Localization.Format("about_website_failed", ex.Message), Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        buttons.Controls.Add(close);
        buttons.Controls.Add(website);
        layout.Controls.Add(textBox, 0, 0);
        layout.Controls.Add(buttons, 0, 1);
        Controls.Add(layout);

        AcceptButton = website;
        CancelButton = close;
        ActiveControl = website;
    }
}
