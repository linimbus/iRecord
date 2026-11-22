using iRecord.App;
using iRecord.Capture;

namespace iRecord.UI;

public sealed class SettingsForm : Form
{
    private readonly AppSettings _result;

    private readonly Label _languageLabel = new();
    private readonly Label _formatLabel = new();
    private readonly Label _fpsLabel = new();
    private readonly Label _gifFpsLabel = new();
    private readonly Label _qualityLabel = new();
    private readonly Label _audioLabel = new();
    private readonly Label _mouseLabel = new();
    private readonly Label _outputLabel = new();
    private readonly Label _ffmpegLabel = new();
    private readonly Label _hotkeysHeader = new();
    private readonly Label _hotkeyStartLabel = new();
    private readonly Label _hotkeyStopLabel = new();
    private readonly Label _hotkeyPauseLabel = new();
    private readonly Label _modeLabel = new();
    private readonly Label _hint = new();

    private readonly ComboBox _languageCombo = new();
    private readonly ComboBox _formatCombo = new();
    private readonly ComboBox _qualityCombo = new();
    private readonly ComboBox _audioCombo = new();
    private readonly ComboBox _mouseCombo = new();
    private readonly ComboBox _modeCombo = new();
    private readonly NumericUpDown _fps = new();
    private readonly NumericUpDown _gifFps = new();
    private readonly TextBox _outputText = new();
    private readonly TextBox _ffmpegText = new();
    private readonly HotkeyBox _hotStart = new();
    private readonly HotkeyBox _hotStop = new();
    private readonly HotkeyBox _hotPause = new();
    private readonly Button _save = new();
    private readonly Button _cancel = new();

    public AppSettings Result => _result;

    private sealed class ComboItem
    {
        public string Value { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
        public override string ToString() => Text;
    }

    public SettingsForm(AppSettings settings)
    {
        _result = settings.Clone();

        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.None;

        int width = Dpi.S(600);
        int height = Dpi.S(512);
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, width, height);
        int margin = Dpi.S(40);
        if (height > area.Height - margin) height = Math.Max(Dpi.S(240), area.Height - margin);
        if (width > area.Width - margin) width = Math.Max(Dpi.S(320), area.Width - margin);

        ClientSize = new Size(width, height);
        AutoScroll = true;

        BuildUi();
        Populate();
    }

    private void BuildUi()
    {
        const int labelX = 16;
        const int fieldX = 198;
        const int fieldW = 218;
        const int rowStart = 14;
        const int rowH = 30;
        const int controlH = 26;

        int RowY(int row) => rowStart + row * rowH;

        foreach (var label in new[]
                 {
                     _languageLabel, _formatLabel, _fpsLabel, _gifFpsLabel, _qualityLabel, _audioLabel,
                     _mouseLabel, _outputLabel, _ffmpegLabel, _hotkeyStartLabel, _hotkeyStopLabel,
                     _hotkeyPauseLabel, _modeLabel
                 })
        {
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Bounds = Dpi.R(labelX, 0, 176, rowH);
        }

        _languageLabel.Top = Dpi.S(RowY(0));
        _formatLabel.Top = Dpi.S(RowY(1));
        _fpsLabel.Top = Dpi.S(RowY(2));
        _gifFpsLabel.Top = Dpi.S(RowY(3));
        _qualityLabel.Top = Dpi.S(RowY(4));
        _audioLabel.Top = Dpi.S(RowY(5));
        _mouseLabel.Top = Dpi.S(RowY(6));
        _outputLabel.Top = Dpi.S(RowY(7));
        _ffmpegLabel.Top = Dpi.S(RowY(8));
        _hotkeyStartLabel.Top = Dpi.S(RowY(10));
        _hotkeyStopLabel.Top = Dpi.S(RowY(11));
        _hotkeyPauseLabel.Top = Dpi.S(RowY(12));
        _modeLabel.Top = Dpi.S(RowY(13));

        _languageCombo.Bounds = Dpi.R(fieldX, RowY(0), fieldW, controlH);
        _formatCombo.Bounds = Dpi.R(fieldX, RowY(1), fieldW, controlH);
        _fps.Bounds = Dpi.R(fieldX, RowY(2), 100, controlH);
        _gifFps.Bounds = Dpi.R(fieldX, RowY(3), 100, controlH);
        _qualityCombo.Bounds = Dpi.R(fieldX, RowY(4), fieldW, controlH);
        _audioCombo.Bounds = Dpi.R(fieldX, RowY(5), fieldW, controlH);
        _mouseCombo.Bounds = Dpi.R(fieldX, RowY(6), fieldW, controlH);
        _outputText.Bounds = Dpi.R(fieldX, RowY(7), fieldW, controlH);
        _ffmpegText.Bounds = Dpi.R(fieldX, RowY(8), fieldW, controlH);
        _hotStart.Bounds = Dpi.R(fieldX, RowY(10), 200, controlH);
        _hotStop.Bounds = Dpi.R(fieldX, RowY(11), 200, controlH);
        _hotPause.Bounds = Dpi.R(fieldX, RowY(12), 200, controlH);
        _modeCombo.Bounds = Dpi.R(fieldX, RowY(13), fieldW, controlH);

        foreach (var combo in new[] { _languageCombo, _formatCombo, _qualityCombo, _audioCombo, _mouseCombo, _modeCombo })
        {
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.FlatStyle = FlatStyle.System;
        }

        _fps.Minimum = 5;
        _fps.Maximum = 60;
        _gifFps.Minimum = 5;
        _gifFps.Maximum = 30;

        _hotkeysHeader.AutoSize = false;
        _hotkeysHeader.Bounds = Dpi.R(labelX, RowY(9) - 4, 564, rowH);
        _hotkeysHeader.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);

        _hint.AutoSize = false;
        _hint.Bounds = Dpi.R(labelX, RowY(14), 564, rowH);
        _hint.ForeColor = Color.DimGray;

        var browseOutput = new Button { Bounds = Dpi.R(424, RowY(7), 72, controlH) };
        browseOutput.Click += (_, _) => BrowseOutput();
        var browseFfmpeg = new Button { Bounds = Dpi.R(424, RowY(8), 72, controlH) };
        browseFfmpeg.Click += (_, _) => BrowseFfmpeg();
        var detect = new Button { Bounds = Dpi.R(502, RowY(8), 80, controlH) };
        detect.Click += (_, _) => DetectFfmpeg();
        _browseOutput = browseOutput;
        _browseFfmpeg = browseFfmpeg;
        _detectFfmpeg = detect;

        _save.Bounds = Dpi.R(366, RowY(15), 120, 34);
        _save.Click += (_, _) => Save();
        _cancel.Bounds = Dpi.R(494, RowY(15), 88, 34);
        _cancel.DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[]
        {
            _languageLabel, _formatLabel, _fpsLabel, _gifFpsLabel, _qualityLabel, _audioLabel,
            _mouseLabel, _outputLabel, _ffmpegLabel, _hotkeysHeader, _hotkeyStartLabel, _hotkeyStopLabel,
            _hotkeyPauseLabel, _modeLabel, _hint,
            _languageCombo, _formatCombo, _fps, _gifFps, _qualityCombo, _audioCombo, _mouseCombo,
            _outputText, _ffmpegText, _hotStart, _hotStop, _hotPause, _modeCombo,
            browseOutput, browseFfmpeg, detect, _save, _cancel
        });

        AcceptButton = _save;
        CancelButton = _cancel;
    }

    private Button _browseOutput = null!;
    private Button _browseFfmpeg = null!;
    private Button _detectFfmpeg = null!;

    private void Populate()
    {
        Text = Localization.T("settings_title");
        _languageLabel.Text = Localization.T("settings_language");
        _formatLabel.Text = Localization.T("settings_format");
        _fpsLabel.Text = Localization.T("settings_fps");
        _gifFpsLabel.Text = Localization.T("settings_gif_fps");
        _qualityLabel.Text = Localization.T("settings_quality");
        _audioLabel.Text = Localization.T("settings_audio");
        _mouseLabel.Text = Localization.T("settings_mouse");
        _outputLabel.Text = Localization.T("settings_output");
        _ffmpegLabel.Text = Localization.T("settings_ffmpeg");
        _hotkeysHeader.Text = Localization.T("settings_hotkeys");
        _hotkeyStartLabel.Text = Localization.T("settings_hotkey_start");
        _hotkeyStopLabel.Text = Localization.T("settings_hotkey_stop");
        _hotkeyPauseLabel.Text = Localization.T("settings_hotkey_pause");
        _modeLabel.Text = Localization.T("settings_start_mode");
        _hint.Text = Localization.T("settings_hint_hotkey");
        _save.Text = Localization.T("settings_save");
        _cancel.Text = Localization.T("settings_cancel");
        _browseOutput.Text = Localization.T("settings_browse");
        _browseFfmpeg.Text = Localization.T("settings_browse");
        _detectFfmpeg.Text = Localization.T("settings_detect");

        FillCombo(_languageCombo, Localization.Languages.Select(l => (l.Code, l.Name)), _result.Language);
        FillCombo(_formatCombo, new[]
        {
            ("mp4", Localization.T("format_mp4")),
            ("mkv", Localization.T("format_mkv")),
            ("mov", Localization.T("format_mov")),
            ("webm", Localization.T("format_webm")),
            ("gif", Localization.T("format_gif"))
        }, _result.Format);
        FillCombo(_qualityCombo, new[]
        {
            ("low", Localization.T("settings_quality_low")),
            ("medium", Localization.T("settings_quality_medium")),
            ("high", Localization.T("settings_quality_high"))
        }, _result.Quality);
        FillCombo(_audioCombo, new[]
        {
            ("none", Localization.T("settings_audio_none")),
            ("system", Localization.T("settings_audio_system")),
            ("mic", Localization.T("settings_audio_mic")),
            ("both", Localization.T("settings_audio_both"))
        }, _result.Audio);
        FillCombo(_mouseCombo, new[]
        {
            ("off", Localization.T("settings_mouse_off")),
            ("cursor", Localization.T("settings_mouse_cursor")),
            ("clicks", Localization.T("settings_mouse_clicks"))
        }, _result.MouseMode);
        FillCombo(_modeCombo, new[]
        {
            ("fullscreen", Localization.T("settings_mode_fullscreen")),
            ("region", Localization.T("settings_mode_region")),
            ("window", Localization.T("settings_mode_window"))
        }, _result.StartMode);

        _fps.Value = Math.Clamp(_result.Fps, 5, 60);
        _gifFps.Value = Math.Clamp(_result.GifFps, 5, 30);
        _outputText.Text = _result.OutputFolder;
        _ffmpegText.Text = _result.FfmpegPath;
        _hotStart.Text = _result.HotkeyStart;
        _hotStop.Text = _result.HotkeyStop;
        _hotPause.Text = _result.HotkeyPause;
    }

    private static void FillCombo(ComboBox combo, IEnumerable<(string Value, string Text)> items, string? selected)
    {
        combo.BeginUpdate();
        combo.Items.Clear();
        int index = 0, selectedIndex = 0;
        foreach (var (value, text) in items)
        {
            combo.Items.Add(new ComboItem { Value = value, Text = text });
            if (string.Equals(value, selected, StringComparison.OrdinalIgnoreCase))
                selectedIndex = index;
            index++;
        }
        combo.SelectedIndex = combo.Items.Count > 0 ? selectedIndex : -1;
        combo.EndUpdate();
    }

    private static string? SelectedValue(ComboBox combo) => (combo.SelectedItem as ComboItem)?.Value;

    private void BrowseOutput()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = Localization.T("dialog_select_output"),
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_outputText.Text) ? _outputText.Text : string.Empty
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _outputText.Text = dialog.SelectedPath;
    }

    private void BrowseFfmpeg()
    {
        using var dialog = new OpenFileDialog
        {
            Title = Localization.T("dialog_select_ffmpeg"),
            Filter = "ffmpeg.exe|ffmpeg.exe|Executables (*.exe)|*.exe|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _ffmpegText.Text = dialog.FileName;
    }

    private void DetectFfmpeg()
    {
        var path = FfmpegTools.Detect();
        if (path != null)
        {
            _ffmpegText.Text = path;
            MessageBox.Show(this, Localization.Format("dialog_ffmpeg_detected", path),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(this, Localization.T("dialog_ffmpeg_not_detected"),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Save()
    {
        _result.Language = SelectedValue(_languageCombo) ?? "en";
        _result.Format = SelectedValue(_formatCombo) ?? "mp4";
        _result.Quality = SelectedValue(_qualityCombo) ?? "high";
        _result.Audio = SelectedValue(_audioCombo) ?? "system";
        _result.MouseMode = SelectedValue(_mouseCombo) ?? "clicks";
        _result.StartMode = SelectedValue(_modeCombo) ?? "fullscreen";
        _result.Fps = (int)_fps.Value;
        _result.GifFps = (int)_gifFps.Value;
        _result.OutputFolder = _outputText.Text.Trim();
        _result.FfmpegPath = _ffmpegText.Text.Trim();

        if (!ValidateHotkey(_hotStart.Text, "settings_hotkey_start")) return;
        if (!ValidateHotkey(_hotStop.Text, "settings_hotkey_stop")) return;
        if (!ValidateHotkey(_hotPause.Text, "settings_hotkey_pause")) return;

        _result.HotkeyStart = _hotStart.Text.Trim();
        _result.HotkeyStop = _hotStop.Text.Trim();
        _result.HotkeyPause = _hotPause.Text.Trim();

        if (string.IsNullOrWhiteSpace(_result.OutputFolder))
            _result.OutputFolder = SettingsStore.CreateDefault().OutputFolder;

        if (string.IsNullOrWhiteSpace(_result.FfmpegPath))
            _result.FfmpegPath = FfmpegTools.Detect() ?? string.Empty;

        try
        {
            Directory.CreateDirectory(_result.OutputFolder);
            SettingsStore.Save(_result);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Localization.Format("msg_save_failed", ex.Message),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Localization.SetLanguage(_result.Language);
        DialogResult = DialogResult.OK;
        Close();
    }

    private bool ValidateHotkey(string text, string labelKey)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (HotkeyParser.TryParse(text, out _)) return true;

        MessageBox.Show(this, Localization.Format("dialog_invalid_hotkey", text),
            Localization.T(labelKey), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }
}
