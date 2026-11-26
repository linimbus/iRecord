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
    private readonly Label _systemGainLabel = new();
    private readonly Label _micGainLabel = new();
    private readonly Label _micVolumeLabel = new();
    private readonly Label _mouseLabel = new();
    private readonly Label _outputLabel = new();
    private readonly Label _ffmpegLabel = new();
    private readonly Label _generalHeader = new();
    private readonly Label _recordingHeader = new();
    private readonly Label _outputHeader = new();
    private readonly Label _hotkeysHeader = new();
    private readonly Label _hotkeyStartLabel = new();
    private readonly Label _hotkeyStopLabel = new();
    private readonly Label _hotkeyPauseLabel = new();
    private readonly Label _hotkeyShowPanelLabel = new();
    private readonly Label _hotkeyScreenshotSingleLabel = new();
    private readonly Label _hotkeyScreenshotRegionLabel = new();
    private readonly Label _hotkeyScreenshotWindowLabel = new();
    private readonly Label _hotkeyScreenshotScrollLabel = new();
    private readonly Label _modeLabel = new();
    private readonly Label _opacityLabel = new();
    private readonly Label _recordingOpacityLabel = new();
    private readonly Label _hint = new();

    private readonly ComboBox _languageCombo = new();
    private readonly ComboBox _formatCombo = new();
    private readonly ComboBox _qualityCombo = new();
    private readonly ComboBox _audioCombo = new();
    private readonly ComboBox _mouseCombo = new();
    private readonly ComboBox _modeCombo = new();
    private readonly NumericUpDown _fps = new();
    private readonly NumericUpDown _gifFps = new();
    private readonly NumericUpDown _systemGain = new();
    private readonly NumericUpDown _micGain = new();
    private readonly NumericUpDown _micVolume = new();
    private readonly NumericUpDown _opacity = new();
    private readonly NumericUpDown _recordingOpacity = new();
    private readonly TextBox _outputText = new();
    private readonly TextBox _ffmpegText = new();
    private readonly HotkeyBox _hotStart = new();
    private readonly HotkeyBox _hotStop = new();
    private readonly HotkeyBox _hotPause = new();
    private readonly HotkeyBox _hotShowPanel = new();
    private readonly HotkeyBox _hotScreenshotSingle = new();
    private readonly HotkeyBox _hotScreenshotRegion = new();
    private readonly HotkeyBox _hotScreenshotWindow = new();
    private readonly HotkeyBox _hotScreenshotScroll = new();
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
        AutoScroll = true;

        BuildUi();
        Populate();
    }

    private void BuildUi()
    {
        const int margin = 20;
        const int contentW = 740;
        const int labelW = 150;
        const int leftLabelX = 20;
        const int leftFieldX = 178;
        const int rightLabelX = 402;
        const int rightFieldX = 560;
        const int fieldW = 200;
        const int columnW = 358;
        const int rowH = 30;
        const int controlH = 26;

        var generalSep = new Panel();
        var recordingSep = new Panel();
        var outputSep = new Panel();
        var hotkeysSep = new Panel();
        var footerSep = new Panel();

        foreach (var sep in new[] { generalSep, recordingSep, outputSep, hotkeysSep, footerSep })
            sep.BackColor = Color.FromArgb(226, 228, 232);

        int y = 16;

        void Section(Label header, Control separator, int x, int width, ref int rowY)
        {
            header.AutoSize = false;
            header.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            header.ForeColor = Color.FromArgb(64, 64, 64);
            header.TextAlign = ContentAlignment.MiddleLeft;
            header.Bounds = Dpi.R(x, rowY, width, 22);
            rowY += 24;
            separator.Bounds = Dpi.R(x, rowY, width, 1);
            rowY += 10;
        }

        void Row(Label label, Control field, int labelX, int fieldX, int width, ref int rowY)
        {
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Bounds = Dpi.R(labelX, rowY, labelW, controlH);
            field.Bounds = Dpi.R(fieldX, rowY, width, controlH);
            rowY += rowH;
        }

        Section(_generalHeader, generalSep, leftLabelX, contentW, ref y);
        Row(_languageLabel, _languageCombo, leftLabelX, leftFieldX, 300, ref y);
        Row(_modeLabel, _modeCombo, leftLabelX, leftFieldX, 300, ref y);
        Row(_opacityLabel, _opacity, leftLabelX, leftFieldX, 300, ref y);
        Row(_recordingOpacityLabel, _recordingOpacity, leftLabelX, leftFieldX, 300, ref y);
        y += 12;

        int leftY = y;
        int rightY = y;

        Section(_recordingHeader, recordingSep, leftLabelX, columnW, ref leftY);
        Row(_formatLabel, _formatCombo, leftLabelX, leftFieldX, fieldW, ref leftY);
        Row(_qualityLabel, _qualityCombo, leftLabelX, leftFieldX, fieldW, ref leftY);
        Row(_fpsLabel, _fps, leftLabelX, leftFieldX, fieldW, ref leftY);
        Row(_gifFpsLabel, _gifFps, leftLabelX, leftFieldX, fieldW, ref leftY);
        Row(_audioLabel, _audioCombo, leftLabelX, leftFieldX, fieldW, ref leftY);
        Row(_systemGainLabel, _systemGain, leftLabelX, leftFieldX, fieldW, ref leftY);
        Row(_micGainLabel, _micGain, leftLabelX, leftFieldX, fieldW, ref leftY);
        Row(_micVolumeLabel, _micVolume, leftLabelX, leftFieldX, fieldW, ref leftY);
        Row(_mouseLabel, _mouseCombo, leftLabelX, leftFieldX, fieldW, ref leftY);

        Section(_hotkeysHeader, hotkeysSep, rightLabelX, columnW, ref rightY);
        Row(_hotkeyStartLabel, _hotStart, rightLabelX, rightFieldX, fieldW, ref rightY);
        Row(_hotkeyStopLabel, _hotStop, rightLabelX, rightFieldX, fieldW, ref rightY);
        Row(_hotkeyPauseLabel, _hotPause, rightLabelX, rightFieldX, fieldW, ref rightY);
        Row(_hotkeyShowPanelLabel, _hotShowPanel, rightLabelX, rightFieldX, fieldW, ref rightY);
        Row(_hotkeyScreenshotSingleLabel, _hotScreenshotSingle, rightLabelX, rightFieldX, fieldW, ref rightY);
        Row(_hotkeyScreenshotRegionLabel, _hotScreenshotRegion, rightLabelX, rightFieldX, fieldW, ref rightY);
        Row(_hotkeyScreenshotWindowLabel, _hotScreenshotWindow, rightLabelX, rightFieldX, fieldW, ref rightY);
        Row(_hotkeyScreenshotScrollLabel, _hotScreenshotScroll, rightLabelX, rightFieldX, fieldW, ref rightY);

        _hint.AutoSize = false;
        _hint.ForeColor = Color.DimGray;
        _hint.Bounds = Dpi.R(rightLabelX, rightY + 6, columnW, 24);
        rightY += 30;

        y = Math.Max(leftY, rightY) + 12;

        Section(_outputHeader, outputSep, leftLabelX, contentW, ref y);

        _outputLabel.AutoSize = false;
        _outputLabel.TextAlign = ContentAlignment.MiddleLeft;
        _outputLabel.Bounds = Dpi.R(leftLabelX, y, labelW, controlH);
        _outputText.Bounds = Dpi.R(leftFieldX, y, 400, controlH);
        var browseOutput = new Button { Bounds = Dpi.R(586, y, 100, controlH) };
        browseOutput.Click += (_, _) => BrowseOutput();
        _browseOutput = browseOutput;
        y += rowH;

        _ffmpegLabel.AutoSize = false;
        _ffmpegLabel.TextAlign = ContentAlignment.MiddleLeft;
        _ffmpegLabel.Bounds = Dpi.R(leftLabelX, y, labelW, controlH);
        _ffmpegText.Bounds = Dpi.R(leftFieldX, y, 260, controlH);
        var browseFfmpeg = new Button { Bounds = Dpi.R(446, y, 100, controlH) };
        browseFfmpeg.Click += (_, _) => BrowseFfmpeg();
        _browseFfmpeg = browseFfmpeg;
        var detect = new Button { Bounds = Dpi.R(554, y, 124, controlH) };
        detect.Click += (_, _) => DetectFfmpeg();
        _detectFfmpeg = detect;
        y += rowH + 12;

        footerSep.Bounds = Dpi.R(leftLabelX, y, contentW, 1);
        y += 12;

        _save.Bounds = Dpi.R(532, y, 110, 34);
        _save.Click += (_, _) => Save();
        _cancel.Bounds = Dpi.R(650, y, 110, 34);
        _cancel.DialogResult = DialogResult.Cancel;
        y += 34 + 16;

        foreach (var combo in new[] { _languageCombo, _formatCombo, _qualityCombo, _audioCombo, _mouseCombo, _modeCombo })
        {
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.FlatStyle = FlatStyle.System;
        }

        _fps.Minimum = 5;
        _fps.Maximum = 60;
        _gifFps.Minimum = 5;
        _gifFps.Maximum = 30;
        _systemGain.Minimum = 1;
        _systemGain.Maximum = 10;
        _micGain.Minimum = 1;
        _micGain.Maximum = 10;
        _micVolume.Minimum = 0;
        _micVolume.Maximum = 200;
        _opacity.Minimum = 20;
        _opacity.Maximum = 100;
        _recordingOpacity.Minimum = 20;
        _recordingOpacity.Maximum = 100;

        Controls.AddRange(new Control[]
        {
            _generalHeader, generalSep, _recordingHeader, recordingSep,
            _hotkeysHeader, hotkeysSep, _outputHeader, outputSep, footerSep,
            _languageLabel, _modeLabel, _opacityLabel, _recordingOpacityLabel,
            _formatLabel, _qualityLabel, _fpsLabel, _gifFpsLabel, _audioLabel,
            _systemGainLabel, _micGainLabel, _micVolumeLabel, _mouseLabel,
            _hotkeyStartLabel, _hotkeyStopLabel, _hotkeyPauseLabel, _hotkeyShowPanelLabel,
            _hotkeyScreenshotSingleLabel, _hotkeyScreenshotRegionLabel, _hotkeyScreenshotWindowLabel,
            _hotkeyScreenshotScrollLabel, _hint,
            _outputLabel, _ffmpegLabel,
            _languageCombo, _modeCombo, _opacity, _recordingOpacity, _formatCombo, _qualityCombo, _fps, _gifFps,
            _audioCombo, _systemGain, _micGain, _micVolume, _mouseCombo,
            _hotStart, _hotStop, _hotPause, _hotShowPanel,
            _hotScreenshotSingle, _hotScreenshotRegion, _hotScreenshotWindow, _hotScreenshotScroll,
            _outputText, _ffmpegText, browseOutput, browseFfmpeg, detect, _save, _cancel
        });

        int tab = 0;
        foreach (var control in new Control[]
                 {
                     _languageCombo, _modeCombo, _opacity, _recordingOpacity,
                     _formatCombo, _qualityCombo, _fps, _gifFps, _audioCombo,
                     _systemGain, _micGain, _micVolume, _mouseCombo,
                     _hotStart, _hotStop, _hotPause, _hotShowPanel,
                     _hotScreenshotSingle, _hotScreenshotRegion, _hotScreenshotWindow, _hotScreenshotScroll,
                     _outputText, browseOutput, _ffmpegText, browseFfmpeg, detect, _save, _cancel
                 })
            control.TabIndex = tab++;

        int width = Dpi.S(margin * 2 + contentW);
        int height = Dpi.S(y);
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, width, height);
        int gap = Dpi.S(40);
        if (height > area.Height - gap) height = Math.Max(Dpi.S(240), area.Height - gap);
        if (width > area.Width - gap) width = Math.Max(Dpi.S(320), area.Width - gap);
        ClientSize = new Size(width, height);

        AcceptButton = _save;
        CancelButton = _cancel;
    }

    private Button _browseOutput = null!;
    private Button _browseFfmpeg = null!;
    private Button _detectFfmpeg = null!;

    private void Populate()
    {
        Text = Localization.T("settings_title");
        _generalHeader.Text = Localization.T("settings_section_general");
        _recordingHeader.Text = Localization.T("settings_section_recording");
        _outputHeader.Text = Localization.T("settings_section_output");
        _hotkeysHeader.Text = Localization.T("settings_hotkeys");
        _languageLabel.Text = Localization.T("settings_language");
        _formatLabel.Text = Localization.T("settings_format");
        _fpsLabel.Text = Localization.T("settings_fps");
        _gifFpsLabel.Text = Localization.T("settings_gif_fps");
        _qualityLabel.Text = Localization.T("settings_quality");
        _audioLabel.Text = Localization.T("settings_audio");
        _systemGainLabel.Text = Localization.T("settings_system_gain");
        _micGainLabel.Text = Localization.T("settings_mic_gain");
        _micVolumeLabel.Text = Localization.T("settings_mic_volume");
        _mouseLabel.Text = Localization.T("settings_mouse");
        _outputLabel.Text = Localization.T("settings_output");
        _ffmpegLabel.Text = Localization.T("settings_ffmpeg");
        _hotkeyStartLabel.Text = Localization.T("settings_hotkey_start");
        _hotkeyStopLabel.Text = Localization.T("settings_hotkey_stop");
        _hotkeyPauseLabel.Text = Localization.T("settings_hotkey_pause");
        _hotkeyShowPanelLabel.Text = Localization.T("settings_hotkey_show_panel");
        _hotkeyScreenshotSingleLabel.Text = Localization.T("screenshot_single");
        _hotkeyScreenshotRegionLabel.Text = Localization.T("settings_hotkey_screenshot_region");
        _hotkeyScreenshotWindowLabel.Text = Localization.T("settings_hotkey_screenshot_window");
        _hotkeyScreenshotScrollLabel.Text = Localization.T("settings_hotkey_screenshot_scroll");
        _modeLabel.Text = Localization.T("settings_start_mode");
        _opacityLabel.Text = Localization.T("settings_panel_opacity");
        _recordingOpacityLabel.Text = Localization.T("settings_recording_opacity");
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

        _formatCombo.SelectedIndexChanged += (_, _) => UpdateFormatDependentState();

        _fps.Value = Math.Clamp(_result.Fps, 5, 60);
        _gifFps.Value = Math.Clamp(_result.GifFps, 5, 30);
        _systemGain.Value = Math.Clamp(_result.SystemLevelGain, 1, 10);
        _micGain.Value = Math.Clamp(_result.MicLevelGain, 1, 10);
        _micVolume.Value = Math.Clamp(_result.MicVolume, 0, 200);
        _opacity.Value = Math.Clamp(_result.PanelOpacity, 20, 100);
        _recordingOpacity.Value = Math.Clamp(_result.RecordingBarOpacity, 20, 100);
        UpdateFormatDependentState();
        _outputText.Text = _result.OutputFolder;
        _ffmpegText.Text = _result.FfmpegPath;
        _hotStart.Text = _result.HotkeyStart;
        _hotStop.Text = _result.HotkeyStop;
        _hotPause.Text = _result.HotkeyPause;
        _hotShowPanel.Text = _result.HotkeyShowPanel;
        _hotScreenshotSingle.Text = _result.HotkeyScreenshotSingle;
        _hotScreenshotRegion.Text = _result.HotkeyScreenshotRegion;
        _hotScreenshotWindow.Text = _result.HotkeyScreenshotWindow;
        _hotScreenshotScroll.Text = _result.HotkeyScreenshotScroll;
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

    private void UpdateFormatDependentState()
    {
        bool isGif = string.Equals(SelectedValue(_formatCombo), "gif", StringComparison.OrdinalIgnoreCase);
        _fps.Enabled = !isGif;
        _fpsLabel.Enabled = !isGif;
        _gifFps.Enabled = isGif;
        _gifFpsLabel.Enabled = isGif;
    }

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
        _result.SystemLevelGain = (int)_systemGain.Value;
        _result.MicLevelGain = (int)_micGain.Value;
        _result.MicVolume = (int)_micVolume.Value;
        _result.PanelOpacity = (int)_opacity.Value;
        _result.RecordingBarOpacity = (int)_recordingOpacity.Value;
        _result.OutputFolder = _outputText.Text.Trim();
        _result.FfmpegPath = _ffmpegText.Text.Trim();

        if (!ValidateHotkey(_hotStart.Text, "settings_hotkey_start")) return;
        if (!ValidateHotkey(_hotStop.Text, "settings_hotkey_stop")) return;
        if (!ValidateHotkey(_hotPause.Text, "settings_hotkey_pause")) return;
        if (!ValidateHotkey(_hotShowPanel.Text, "settings_hotkey_show_panel")) return;
        if (!ValidateHotkey(_hotScreenshotSingle.Text, "screenshot_single")) return;
        if (!ValidateHotkey(_hotScreenshotRegion.Text, "settings_hotkey_screenshot_region")) return;
        if (!ValidateHotkey(_hotScreenshotWindow.Text, "settings_hotkey_screenshot_window")) return;
        if (!ValidateHotkey(_hotScreenshotScroll.Text, "settings_hotkey_screenshot_scroll")) return;

        _result.HotkeyStart = _hotStart.Text.Trim();
        _result.HotkeyStop = _hotStop.Text.Trim();
        _result.HotkeyPause = _hotPause.Text.Trim();
        _result.HotkeyShowPanel = _hotShowPanel.Text.Trim();
        _result.HotkeyScreenshotSingle = _hotScreenshotSingle.Text.Trim();
        _result.HotkeyScreenshotRegion = _hotScreenshotRegion.Text.Trim();
        _result.HotkeyScreenshotWindow = _hotScreenshotWindow.Text.Trim();
        _result.HotkeyScreenshotScroll = _hotScreenshotScroll.Text.Trim();

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
