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
    private MainPanelForm? _panel;
    private bool _panelShouldReturn;
    private bool _capturingScreenshot;
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
        _tray.DoubleClick += (_, _) => ShowMainPanel();

        _hotkeys.Pressed += OnHotkeyPressed;

        RebuildMenu();
        RegisterHotkeys();

        if (FfmpegTools.Resolve(_settings.FfmpegPath) == null)
        {
            Post(() => ShowBalloon(Localization.T("notify_error_title"),
                Localization.T("notify_ffmpeg_missing"), ToolTipIcon.Warning));
        }

        Application.Idle += ShowPanelOnStartup;
    }

    private void ShowPanelOnStartup(object? sender, EventArgs e)
    {
        Application.Idle -= ShowPanelOnStartup;
        ShowMainPanel();
    }

    private void ShowMainPanel()
    {
        if (_panel == null || _panel.IsDisposed)
        {
            _panel = new MainPanelForm();
            _panel.ScreenshotRegionRequested += CaptureRegionScreenshot;
            _panel.ScreenshotWindowRequested += CaptureWindowScreenshot;
            _panel.ScreenshotScrollRequested += CaptureScrollingScreenshot;
            _panel.FullscreenRequested += () => StartRecording(CaptureMode.FullScreen);
            _panel.RegionRequested += () => StartRecording(CaptureMode.Region);
            _panel.WindowRequested += () => StartRecording(CaptureMode.Window);
            _panel.SettingsRequested += OpenSettings;
            _panel.FormClosed += (_, _) => _panel = null;
        }

        _panel.SetRecording(_session != null);
        if (!_panel.Visible) _panel.Show();
        _panel.Activate();
        _panel.BringToFront();
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

        var showPanel = new ToolStripMenuItem(Localization.T("tray_show_panel"));
        showPanel.Click += (_, _) => ShowMainPanel();

        var startFull = new ToolStripMenuItem(Localization.T("tray_start_fullscreen"));
        startFull.Enabled = idle;
        startFull.Click += (_, _) => StartRecording(CaptureMode.FullScreen);

        var startRegion = new ToolStripMenuItem(Localization.T("tray_start_region"));
        startRegion.Enabled = idle;
        startRegion.Click += (_, _) => StartRecording(CaptureMode.Region);

        var startWindow = new ToolStripMenuItem(Localization.T("tray_start_window"));
        startWindow.Enabled = idle;
        startWindow.Click += (_, _) => StartRecording(CaptureMode.Window);

        var screenshotRegion = new ToolStripMenuItem(Localization.T("screenshot_region"));
        screenshotRegion.Enabled = idle;
        screenshotRegion.Click += (_, _) => CaptureRegionScreenshot();

        var screenshotWindow = new ToolStripMenuItem(Localization.T("screenshot_window"));
        screenshotWindow.Enabled = idle;
        screenshotWindow.Click += (_, _) => CaptureWindowScreenshot();

        var screenshotScroll = new ToolStripMenuItem(Localization.T("screenshot_scroll"));
        screenshotScroll.Enabled = idle;
        screenshotScroll.Click += (_, _) => CaptureScrollingScreenshot();

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

        _menu.Items.Add(showPanel);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(startFull);
        _menu.Items.Add(startRegion);
        _menu.Items.Add(startWindow);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(screenshotRegion);
        _menu.Items.Add(screenshotWindow);
        _menu.Items.Add(screenshotScroll);
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

        _panel?.SetRecording(_session != null);
    }

    private void OnLanguageChanged()
    {
        Post(() =>
        {
            RebuildMenu();
            _bar?.RefreshLanguage();
            _panel?.RefreshLanguage();
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
        Register("screenshot-region", _settings.HotkeyScreenshotRegion);
        Register("screenshot-window", _settings.HotkeyScreenshotWindow);
        Register("screenshot-scroll", _settings.HotkeyScreenshotScroll);

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
        else if (action == "screenshot-region")
        {
            CaptureRegionScreenshot();
        }
        else if (action == "screenshot-window")
        {
            CaptureWindowScreenshot();
        }
        else if (action == "screenshot-scroll")
        {
            CaptureScrollingScreenshot();
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

        _panelShouldReturn = _panel?.Visible == true;
        if (_panelShouldReturn)
        {
            _panel!.Hide();
            Application.DoEvents();
            Thread.Sleep(150);
        }

        var session = new RecordingSession(_settings, mode, rect, windowHandle);
        session.Finished += OnSessionFinished;
        session.Failed += OnSessionFailed;
        session.StateChanged += OnSessionStateChanged;

        if (!session.Start())
        {
            session.Dispose();
            ShowBalloon(Localization.T("notify_error_title"),
                session.LastError ?? Localization.T("notify_ffmpeg_missing"), ToolTipIcon.Error);
            ReturnPanelIfNeeded();
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
            ReturnPanelIfNeeded();
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
            ReturnPanelIfNeeded();
        });
    }

    private void ReturnPanelIfNeeded()
    {
        if (!_panelShouldReturn) return;
        _panelShouldReturn = false;
        ShowMainPanel();
    }

    private void OpenSettings()
    {
        if (_session != null) return;
        if (_settingsForm is { Visible: true })
        {
            _settingsForm.Activate();
            return;
        }

        bool panelTopMost = SuspendPanelTopMost();
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
            RestorePanelTopMost(panelTopMost);
        }
    }

    private bool SuspendPanelTopMost()
    {
        if (_panel == null || !_panel.TopMost) return false;
        _panel.TopMost = false;
        return true;
    }

    private void RestorePanelTopMost(bool restore)
    {
        if (restore && _panel != null)
            _panel.TopMost = true;
    }

    private void CaptureRegionScreenshot()
    {
        if (_session != null || _capturingScreenshot) return;
        _capturingScreenshot = true;

        bool returnPanel = false;
        bool returnSettings = false;
        double settingsOpacity = 1d;
        var previousCursor = Cursor.Current;

        try
        {
            returnPanel = _panel?.Visible == true;
            _panel?.Hide();

            returnSettings = _settingsForm is { Visible: true };
            settingsOpacity = returnSettings ? _settingsForm!.Opacity : 1d;
            if (returnSettings)
                _settingsForm!.Opacity = 0d;

            if (returnPanel || returnSettings)
            {
                Application.DoEvents();
                Thread.Sleep(120);
            }

            Cursor.Current = Cursors.WaitCursor;

            using var picker = new MultiRegionSelectorForm();
            if (picker.ShowDialog() != DialogResult.OK) return;

            using var bitmap = picker.CreateMergedImage();
            SaveScreenshot(bitmap, "region");
        }
        catch (Exception ex)
        {
            ShowScreenshotError(ex);
        }
        finally
        {
            Cursor.Current = previousCursor;
            if (returnPanel) ShowMainPanel();
            RestoreSettingsAfterCapture(returnSettings, settingsOpacity);
            _capturingScreenshot = false;
        }
    }

    private void CaptureWindowScreenshot()
    {
        if (_session != null || _capturingScreenshot) return;
        _capturingScreenshot = true;

        bool returnPanel = false;
        bool returnSettings = false;
        double settingsOpacity = 1d;
        var previousCursor = Cursor.Current;

        try
        {
            returnPanel = _panel?.Visible == true;
            _panel?.Hide();

            returnSettings = _settingsForm is { Visible: true };
            settingsOpacity = returnSettings ? _settingsForm!.Opacity : 1d;
            if (returnSettings)
                _settingsForm!.Opacity = 0d;

            if (returnPanel || returnSettings)
            {
                Application.DoEvents();
                Thread.Sleep(120);
            }

            Cursor.Current = Cursors.WaitCursor;

            using var picker = new WindowPickerForm();
            if (picker.ShowDialog() != DialogResult.OK) return;

            ActivateTargetWindow(picker.SelectedHandle);
            using var bitmap = ScreenshotService.CaptureWindow(picker.SelectedHandle, picker.SelectedRect);
            SaveScreenshot(bitmap, "window");
        }
        catch (Exception ex)
        {
            ShowScreenshotError(ex);
        }
        finally
        {
            Cursor.Current = previousCursor;
            if (returnPanel) ShowMainPanel();
            RestoreSettingsAfterCapture(returnSettings, settingsOpacity);
            _capturingScreenshot = false;
        }
    }

    private void CaptureScrollingScreenshot()
    {
        if (_session != null || _capturingScreenshot) return;
        _capturingScreenshot = true;

        bool returnPanel = false;
        bool returnSettings = false;
        double settingsOpacity = 1d;
        var previousCursor = Cursor.Current;

        try
        {
            returnPanel = _panel?.Visible == true;
            _panel?.Hide();

            returnSettings = _settingsForm is { Visible: true };
            settingsOpacity = returnSettings ? _settingsForm!.Opacity : 1d;
            if (returnSettings)
                _settingsForm!.Opacity = 0d;

            if (returnPanel || returnSettings)
            {
                Application.DoEvents();
                Thread.Sleep(120);
            }

            Cursor.Current = Cursors.WaitCursor;

            using var picker = new WindowPickerForm();
            if (picker.ShowDialog() != DialogResult.OK) return;

            ActivateTargetWindow(picker.SelectedHandle);
            using var bitmap = ScreenshotService.CaptureScrollingWindow(picker.SelectedHandle);
            SaveScreenshot(bitmap, "long");
        }
        catch (Exception ex)
        {
            ShowScreenshotError(ex);
        }
        finally
        {
            Cursor.Current = previousCursor;
            if (returnPanel) ShowMainPanel();
            RestoreSettingsAfterCapture(returnSettings, settingsOpacity);
            _capturingScreenshot = false;
        }
    }

    private void RestoreSettingsAfterCapture(bool restore, double opacity)
    {
        if (!restore || _settingsForm == null) return;
        _settingsForm.Opacity = opacity;
        _settingsForm.Activate();
        _settingsForm.BringToFront();
    }

    private static void ActivateTargetWindow(IntPtr hwnd)
    {
        try { NativeMethods.SetForegroundWindow(hwnd); } catch { }
        Thread.Sleep(220);
    }

    private void SaveScreenshot(Bitmap bitmap, string suffix)
    {
        string folder = _settings.OutputFolder;
        if (string.IsNullOrWhiteSpace(folder))
            folder = SettingsStore.CreateDefault().OutputFolder;

        string path = ScreenshotService.SavePng(bitmap, folder, suffix);
        ShowBalloon(Localization.T("screenshot_saved_title"),
            Localization.Format("screenshot_saved_text", path), ToolTipIcon.Info);
    }

    private void ShowScreenshotError(Exception ex)
    {
        ShowBalloon(Localization.T("screenshot_failed_title"),
            Localization.Format("screenshot_failed_text", ex.Message), ToolTipIcon.Error);
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
        bool panelTopMost = SuspendPanelTopMost();
        try
        {
            MessageBox.Show(Localization.T("about_text"), Localization.T("about_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            RestorePanelTopMost(panelTopMost);
        }
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
        try { _panel?.Dispose(); } catch { }
        _panel = null;
        try { _hotkeys.Dispose(); } catch { }
        try { _tray.Visible = false; _tray.Dispose(); } catch { }
        Localization.Changed -= OnLanguageChanged;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.Idle -= ShowPanelOnStartup;
            try { _panel?.Dispose(); } catch { }
            try { _hotkeys.Dispose(); } catch { }
            try { _tray.Dispose(); } catch { }
            try { _icon.Dispose(); } catch { }
            try { _menu.Dispose(); } catch { }
            try { _dispatcher.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }
}
