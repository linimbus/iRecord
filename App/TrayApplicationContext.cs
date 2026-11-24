using System.Diagnostics;
using System.Drawing;
using iRecord.Capture;
using iRecord.UI;

namespace iRecord.App;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Control _dispatcher = new();
    private readonly Icon _icon;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly HotkeyWindow _hotkeys = new();

    private AppSettings _settings;
    private RecordingSession? _session;
    private ControlBarForm? _bar;
    private RecordingBorderForm? _border;
    private SettingsForm? _settingsForm;
    private bool _micVolumeDirty;

    public TrayApplicationContext()
    {
        _ = _dispatcher.Handle;

        _settings = SettingsStore.Load();
        Localization.SetLanguage(_settings.Language);
        Localization.Changed += OnLanguageChanged;

        _icon = LoadIcon();
        _tray = new NotifyIcon
        {
            Icon = _icon,
            Visible = true,
            ContextMenuStrip = _menu
        };
        _tray.DoubleClick += (_, _) =>
        {
            if (_session == null) StartRecording(ParseMode(_settings.StartMode));
            else TogglePause();
        };

        _hotkeys.Pressed += OnHotkeyPressed;

        RebuildMenu();
        RegisterHotkeys();

        if (FfmpegTools.Resolve(_settings.FfmpegPath) == null)
        {
            Post(() => ShowBalloon(Localization.T("notify_error_title"),
                Localization.T("notify_ffmpeg_missing"), ToolTipIcon.Warning));
        }
    }

    private static Icon LoadIcon()
    {
        try
        {
            var extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (extracted != null) return (Icon)extracted.Clone();
        }
        catch { }
        return (Icon)SystemIcons.Application.Clone();
    }

    private void Post(Action action)
    {
        try
        {
            if (_dispatcher.InvokeRequired)
                _dispatcher.BeginInvoke(action);
            else
                action();
        }
        catch { }
    }

    private static CaptureMode ParseMode(string value) => value switch
    {
        "region" => CaptureMode.Region,
        "window" => CaptureMode.Window,
        _ => CaptureMode.FullScreen
    };

    private void RebuildMenu()
    {
        _menu.Items.Clear();

        bool idle = _session == null;
        bool recording = _session != null && !_session.IsEncoding;

        var startFull = new ToolStripMenuItem(Localization.T("tray_start_fullscreen"));
        startFull.Enabled = idle;
        startFull.Click += (_, _) => StartRecording(CaptureMode.FullScreen);

        var startRegion = new ToolStripMenuItem(Localization.T("tray_start_region"));
        startRegion.Enabled = idle;
        startRegion.Click += (_, _) => StartRecording(CaptureMode.Region);

        var startWindow = new ToolStripMenuItem(Localization.T("tray_start_window"));
        startWindow.Enabled = idle;
        startWindow.Click += (_, _) => StartRecording(CaptureMode.Window);

        var pause = new ToolStripMenuItem(Localization.T(
            _session?.IsPaused == true ? "tray_resume" : "tray_pause"));
        pause.Enabled = recording;
        pause.Click += (_, _) => TogglePause();

        var stop = new ToolStripMenuItem(Localization.T("tray_stop"));
        stop.Enabled = recording;
        stop.Click += (_, _) => StopRecording();

        var settings = new ToolStripMenuItem(Localization.T("tray_settings"));
        settings.Enabled = idle;
        settings.Click += (_, _) => OpenSettings();

        var openFolder = new ToolStripMenuItem(Localization.T("tray_open_folder"));
        openFolder.Click += (_, _) => OpenOutputFolder();

        var about = new ToolStripMenuItem(Localization.T("tray_about"));
        about.Click += (_, _) => ShowAbout();

        var exit = new ToolStripMenuItem(Localization.T("tray_exit"));
        exit.Click += (_, _) => ExitApplication();

        _menu.Items.Add(startFull);
        _menu.Items.Add(startRegion);
        _menu.Items.Add(startWindow);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(pause);
        _menu.Items.Add(stop);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(settings);
        _menu.Items.Add(openFolder);
        _menu.Items.Add(about);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exit);

        string status = _session == null
            ? Localization.T("tray_status_idle")
            : _session.IsEncoding
                ? Localization.T("tray_status_recording")
                : _session.IsPaused
                    ? Localization.T("tray_status_paused")
                    : Localization.T("tray_status_recording");

        try { _tray.Text = status.Length > 63 ? status[..63] : status; }
        catch { }
    }

    private void OnLanguageChanged()
    {
        Post(() =>
        {
            RebuildMenu();
            _bar?.RefreshLanguage();
        });
    }

    private void RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var failed = new List<string>();

        void Register(string action, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!HotkeyParser.TryParse(text, out var hotkey)) return;
            if (!_hotkeys.Register(action, hotkey, out _))
                failed.Add(text);
        }

        Register("start", _settings.HotkeyStart);
        Register("stop", _settings.HotkeyStop);
        Register("pause", _settings.HotkeyPause);

        if (failed.Count > 0)
        {
            Post(() => ShowBalloon(Localization.T("notify_error_title"),
                string.Join("\n", failed.Select(f => Localization.Format("notify_hotkey_failed", f))),
                ToolTipIcon.Warning));
        }
    }

    private void OnHotkeyPressed(string action)
    {
        if (action == "start")
        {
            if (_session == null)
                StartRecording(ParseMode(_settings.StartMode));
        }
        else if (action == "stop")
        {
            StopRecording();
        }
        else if (action == "pause")
        {
            TogglePause();
        }
    }

    private void StartRecording(CaptureMode mode)
    {
        if (_session != null) return;

        if (FfmpegTools.Resolve(_settings.FfmpegPath) == null)
        {
            ShowBalloon(Localization.T("notify_error_title"),
                Localization.T("notify_ffmpeg_missing"), ToolTipIcon.Warning);
            OpenSettings();
            return;
        }

        Rectangle rect;
        IntPtr windowHandle = IntPtr.Zero;

        switch (mode)
        {
            case CaptureMode.Region:
            {
                using var picker = new RegionSelectorForm();
                if (picker.ShowDialog() != DialogResult.OK) return;
                rect = picker.SelectedRegion;
                break;
            }
            case CaptureMode.Window:
            {
                using var picker = new WindowPickerForm();
                if (picker.ShowDialog() != DialogResult.OK) return;
                rect = picker.SelectedRect;
                windowHandle = picker.SelectedHandle;
                break;
            }
            default:
            {
                var screen = Screen.FromPoint(Cursor.Position) ?? Screen.PrimaryScreen;
                rect = screen?.Bounds ?? SystemInformation.VirtualScreen;
                mode = CaptureMode.FullScreen;
                break;
            }
        }

        if (rect.Width < 8 || rect.Height < 8)
            return;

        var session = new RecordingSession(_settings, mode, rect, windowHandle);
        session.Finished += OnSessionFinished;
        session.Failed += OnSessionFailed;
        session.StateChanged += OnSessionStateChanged;

        if (!session.Start())
        {
            session.Dispose();
            ShowBalloon(Localization.T("notify_error_title"),
                session.LastError ?? Localization.T("notify_ffmpeg_missing"), ToolTipIcon.Error);
            return;
        }

        _session = session;

        _bar = new ControlBarForm(
            () => _session?.Elapsed ?? TimeSpan.Zero,
            session.HasSystemAudio,
            session.HasMicAudio,
            () => _session == null
                ? (0f, 0f)
                : (_session.SystemAudioLevel, _session.MicAudioLevel),
            _settings.SystemLevelGain,
            _settings.MicLevelGain,
            session.MicVolumePercent,
            OnMicVolumeChanged);
        _bar.PauseClicked += TogglePause;
        _bar.StopClicked += StopRecording;
        _bar.Show();
        _bar.SetPaused(false);

        _border = new RecordingBorderForm(() => _session?.CurrentCaptureRect ?? Rectangle.Empty);
        _border.Show();

        RebuildMenu();
    }

    private void TogglePause()
    {
        if (_session == null || _session.IsEncoding) return;
        _session.TogglePause();
        _bar?.SetPaused(_session.IsPaused);
        RebuildMenu();
    }

    private void OnMicVolumeChanged(int percent)
    {
        _session?.SetMicVolume(percent);
        _settings.MicVolume = percent;
        _micVolumeDirty = true;
    }

    private void SaveMicVolumeIfNeeded()
    {
        if (!_micVolumeDirty) return;
        _micVolumeDirty = false;
        try { SettingsStore.Save(_settings); } catch { }
    }

    private void CloseBorder()
    {
        _border?.Close();
        _border?.Dispose();
        _border = null;
    }

    private void StopRecording()
    {
        if (_session == null || _session.IsEncoding) return;

        SaveMicVolumeIfNeeded();

        var session = _session;
        session.Stop();

        _bar?.Close();
        _bar?.Dispose();
        _bar = null;
        CloseBorder();

        RebuildMenu();
        ShowBalloon(Localization.T("notify_processing_title"), Localization.T("notify_processing_body"),
            ToolTipIcon.Info);
    }

    private void OnSessionStateChanged()
    {
        Post(() =>
        {
            if (_session == null) return;
            _bar?.SetPaused(_session.IsPaused);
            RebuildMenu();
        });
    }

    private void OnSessionFinished(string path)
    {
        Post(() =>
        {
            _session = null;
            SaveMicVolumeIfNeeded();
            _bar?.Close();
            _bar?.Dispose();
            _bar = null;
            CloseBorder();
            RebuildMenu();
            ShowBalloon(Localization.T("notify_saved_title"),
                Localization.Format("notify_saved_text", path), ToolTipIcon.Info);
        });
    }

    private void OnSessionFailed(string message)
    {
        Post(() =>
        {
            _session = null;
            SaveMicVolumeIfNeeded();
            _bar?.Close();
            _bar?.Dispose();
            _bar = null;
            CloseBorder();
            RebuildMenu();
            ShowBalloon(Localization.T("notify_error_title"),
                Localization.Format("notify_error_text", message), ToolTipIcon.Error);
        });
    }

    private void OpenSettings()
    {
        if (_session != null) return;
        if (_settingsForm is { Visible: true })
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settings);
        try
        {
            if (_settingsForm.ShowDialog() == DialogResult.OK)
            {
                _settings = _settingsForm.Result;
                RegisterHotkeys();
                RebuildMenu();
            }
        }
        finally
        {
            _settingsForm.Dispose();
            _settingsForm = null;
        }
    }

    private void OpenOutputFolder()
    {
        try
        {
            string folder = _settings.OutputFolder;
            if (string.IsNullOrWhiteSpace(folder))
                folder = SettingsStore.CreateDefault().OutputFolder;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch
        {
            ShowBalloon(Localization.T("notify_error_title"),
                Localization.T("msg_open_output_failed"), ToolTipIcon.Warning);
        }
    }

    private void ShowAbout()
    {
        MessageBox.Show(Localization.T("about_text"), Localization.T("about_title"),
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        try
        {
            _tray.BalloonTipTitle = title;
            _tray.BalloonTipText = text;
            _tray.BalloonTipIcon = icon;
            _tray.ShowBalloonTip(4000);
        }
        catch { }
    }

    private void ExitApplication()
    {
        try { _bar?.Close(); } catch { }
        try { _bar?.Dispose(); } catch { }
        try { _border?.Close(); } catch { }
        try { _border?.Dispose(); } catch { }
        try { _session?.Dispose(); } catch { }
        try { _settingsForm?.Close(); } catch { }
        try { _hotkeys.Dispose(); } catch { }
        try { _tray.Visible = false; _tray.Dispose(); } catch { }
        Localization.Changed -= OnLanguageChanged;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _hotkeys.Dispose(); } catch { }
            try { _tray.Dispose(); } catch { }
            try { _icon.Dispose(); } catch { }
            try { _menu.Dispose(); } catch { }
            try { _dispatcher.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }
}
