using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using iRecord.Capture;
using iRecord.UI;

namespace iRecord.App;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Control _dispatcher = new();
    private readonly Icon _idleIcon;
    private readonly Icon _recordingIcon;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly HotkeyWindow _hotkeys = new();
    private readonly System.Windows.Forms.Timer _recordingBlinkTimer;
    private bool _recordingBlinkOn = true;

    private AppSettings _settings;
    private RecordingSession? _session;
    private ControlBarForm? _bar;
    private RecordingBorderForm? _border;
    private SettingsForm? _settingsForm;
    private MainPanelForm? _panel;
    private readonly List<ImageEditorForm> _editors = new();
    private bool _panelShouldReturn;
    private bool _editorsTopMostSuspended;
    private bool _capturingScreenshot;
    private bool _startingRecording;

    public TrayApplicationContext()
    {
        _ = _dispatcher.Handle;

        _settings = SettingsStore.Load();
        Localization.SetLanguage(_settings.Language);
        Localization.Changed += OnLanguageChanged;

        _idleIcon = CreateRingIcon(Color.White, null);
        _recordingIcon = CreateRingIcon(Color.White, Color.FromArgb(232, 54, 51));
        _tray = new NotifyIcon
        {
            Icon = _idleIcon,
            Visible = true,
            ContextMenuStrip = _menu
        };
        _tray.DoubleClick += (_, _) => ShowMainPanel();

        _recordingBlinkTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _recordingBlinkTimer.Tick += (_, _) => ToggleRecordingBlink();

        _hotkeys.Pressed += OnHotkeyPressed;

        RebuildMenu();
        RegisterHotkeys();

        if (NeedsFfmpeg(_settings.Format) && FfmpegTools.Resolve(_settings.FfmpegPath) == null)
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
            _panel.SingleRegionRequested += CaptureSingleRegionScreenshot;
            _panel.ScreenshotRegionRequested += CaptureRegionScreenshot;
            _panel.ScreenshotWindowRequested += CaptureWindowScreenshot;
            _panel.ScreenshotScrollRequested += CaptureScrollingScreenshot;
            _panel.FullscreenRequested += () => StartRecording(CaptureMode.FullScreen);
            _panel.RegionRequested += () => StartRecording(CaptureMode.Region);
            _panel.WindowRequested += () => StartRecording(CaptureMode.Window);
            _panel.SettingsRequested += OpenSettings;
            _panel.OpenFolderRequested += OpenOutputFolder;
            _panel.FormClosed += (_, _) => _panel = null;
        }

        _panel.SetRecording(_session != null);
        _panel.SetIdleOpacity(_settings.PanelOpacity / 100.0);
        if (!_panel.Visible) _panel.Show();
        _panel.Activate();
        _panel.BringToFront();
    }

    private static Icon CreateRingIcon(Color ringColor, Color? centerColor)
    {
        using var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            if (centerColor.HasValue)
            {
                using var center = new SolidBrush(centerColor.Value);
                g.FillEllipse(center, 17, 17, 30, 30);
            }

            using var pen = new Pen(ringColor, 8f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };
            g.DrawEllipse(pen, 9, 9, 46, 46);
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

    private static bool NeedsFfmpeg(string? format) =>
        !(string.IsNullOrWhiteSpace(format) ||
          string.Equals(format.Trim(), "mp4", StringComparison.OrdinalIgnoreCase));

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

        var screenshotSingle = new ToolStripMenuItem(Localization.T("screenshot_single"));
        screenshotSingle.Enabled = idle;
        screenshotSingle.Click += (_, _) => CaptureSingleRegionScreenshot();

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

        var showRecordingBar = new ToolStripMenuItem(Localization.T("tray_show_recording_bar"));
        showRecordingBar.Enabled = recording;
        showRecordingBar.Click += (_, _) => ShowRecordingBar();

        var settings = new ToolStripMenuItem(Localization.T("tray_settings"));
        settings.Enabled = idle;
        settings.Click += (_, _) => OpenSettings();

        var openFolder = new ToolStripMenuItem(Localization.T("tray_open_folder"));
        openFolder.Click += (_, _) => OpenOutputFolder();

        var about = new ToolStripMenuItem(Localization.T("tray_about"));
        about.Click += (_, _) => ShowAbout();

        var exit = new ToolStripMenuItem(Localization.T("tray_exit"));
        exit.Click += (_, _) => ExitApplication();

        static void SetShortcut(ToolStripMenuItem item, string? text)
            => item.ShortcutKeyDisplayString = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();

        SetShortcut(showPanel, _settings.HotkeyShowPanel);
        string startMode = _settings.StartMode;
        SetShortcut(startFull, string.Equals(startMode, "fullscreen", StringComparison.OrdinalIgnoreCase) ? _settings.HotkeyStart : null);
        SetShortcut(startRegion, string.Equals(startMode, "region", StringComparison.OrdinalIgnoreCase) ? _settings.HotkeyStart : null);
        SetShortcut(startWindow, string.Equals(startMode, "window", StringComparison.OrdinalIgnoreCase) ? _settings.HotkeyStart : null);
        SetShortcut(screenshotSingle, _settings.HotkeyScreenshotSingle);
        SetShortcut(screenshotRegion, _settings.HotkeyScreenshotRegion);
        SetShortcut(screenshotWindow, _settings.HotkeyScreenshotWindow);
        SetShortcut(screenshotScroll, _settings.HotkeyScreenshotScroll);
        SetShortcut(pause, _settings.HotkeyPause);
        SetShortcut(stop, _settings.HotkeyStop);

        _menu.Items.Add(showPanel);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(startFull);
        _menu.Items.Add(startRegion);
        _menu.Items.Add(startWindow);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(screenshotSingle);
        _menu.Items.Add(screenshotRegion);
        _menu.Items.Add(screenshotWindow);
        _menu.Items.Add(screenshotScroll);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(pause);
        _menu.Items.Add(stop);
        _menu.Items.Add(showRecordingBar);
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
        _panel?.SetIdleOpacity(_settings.PanelOpacity / 100.0);
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
        Register("show-panel", _settings.HotkeyShowPanel);
        Register("screenshot-single", _settings.HotkeyScreenshotSingle);
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
        else if (action == "show-panel")
        {
            ShowMainPanel();
        }
        else if (action == "screenshot-single")
        {
            CaptureSingleRegionScreenshot();
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
        if (_session != null || _startingRecording) return;

        _startingRecording = true;
        try
        {
            StartRecordingCore(mode);
        }
        finally
        {
            _startingRecording = false;
        }
    }

    private void StartRecordingCore(CaptureMode mode)
    {
        if (NeedsFfmpeg(_settings.Format) && FfmpegTools.Resolve(_settings.FfmpegPath) == null)
        {
            ShowBalloon(Localization.T("notify_error_title"),
                Localization.T("notify_ffmpeg_missing"), ToolTipIcon.Error);
            ReturnPanelIfNeeded();
            return;
        }

        bool selectScreen = mode == CaptureMode.FullScreen && Screen.AllScreens.Length > 1;
        _panelShouldReturn = _panel?.Visible == true;
        bool panelHiddenForPicker = false;

        if (selectScreen && _panelShouldReturn)
        {
            _panel!.Hide();
            Application.DoEvents();
            Thread.Sleep(150);
            panelHiddenForPicker = true;
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
                if (selectScreen)
                {
                    using var picker = new FullscreenScreenSelectorForm();
                    if (picker.ShowDialog() != DialogResult.OK)
                    {
                        ReturnPanelIfNeeded();
                        return;
                    }

                    rect = picker.SelectedScreenBounds;
                }
                else
                {
                    var screen = Screen.FromPoint(Cursor.Position) ?? Screen.PrimaryScreen;
                    rect = screen?.Bounds ?? SystemInformation.VirtualScreen;
                }

                mode = CaptureMode.FullScreen;
                break;
            }
        }

        if (rect.Width < 8 || rect.Height < 8)
        {
            if (panelHiddenForPicker)
                ReturnPanelIfNeeded();
            return;
        }

        if (!panelHiddenForPicker && _panelShouldReturn)
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
        _recordingBlinkOn = true;
        _tray.Icon = _recordingIcon;
        _recordingBlinkTimer.Start();

        _bar = new ControlBarForm(
            () => _session?.Elapsed ?? TimeSpan.Zero,
            session.HasSystemAudio,
            session.HasMicAudio,
            () => _session == null
                ? (0f, 0f)
                : (_session.SystemAudioLevel, _session.MicAudioLevel),
            _settings.SystemLevelGain,
            _settings.MicLevelGain,
            _settings.RecordingBarOpacity);
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

    private void ShowRecordingBar()
    {
        if (_session == null || _session.IsEncoding || _bar == null)
            return;

        if (!_bar.Visible)
            _bar.Show();
        _bar.BringToFront();
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

        var session = _session;
        session.Stop();
        _recordingBlinkTimer.Stop();
        _tray.Icon = _idleIcon;

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

            if (_session.IsPaused)
            {
                _recordingBlinkTimer.Stop();
                _recordingBlinkOn = true;
                _tray.Icon = _recordingIcon;
            }
            else if (!_session.IsEncoding)
            {
                _recordingBlinkOn = true;
                _tray.Icon = _recordingIcon;
                _recordingBlinkTimer.Start();
            }

            RebuildMenu();
        });
    }

    private void ToggleRecordingBlink()
    {
        if (_session == null || _session.IsPaused || _session.IsEncoding)
            return;

        _recordingBlinkOn = !_recordingBlinkOn;
        _tray.Icon = _recordingBlinkOn ? _recordingIcon : _idleIcon;
    }

    private void OnSessionFinished(string path)
    {
        Post(() =>
        {
            _session = null;
            _recordingBlinkTimer.Stop();
            _tray.Icon = _idleIcon;
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
            _recordingBlinkTimer.Stop();
            _tray.Icon = _idleIcon;
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

    private void CaptureSingleRegionScreenshot()
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

            using var picker = new RegionSelectorForm();
            if (picker.ShowDialog() != DialogResult.OK) return;

            using var bitmap = ScreenshotService.CaptureRect(picker.SelectedRegion);
            SaveScreenshot(bitmap, "region-single");
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
        CopyScreenshotToClipboard(bitmap);
        ShowImageEditor((Bitmap)bitmap.Clone(), path);
        ShowBalloon(Localization.T("screenshot_saved_title"),
            Localization.Format("screenshot_saved_text", path), ToolTipIcon.Info);
    }

    private void ShowImageEditor(Bitmap image, string path)
    {
        var editor = new ImageEditorForm(image, path);
        _editors.Add(editor);

        if (_editors.Count == 1)
            _editorsTopMostSuspended = SuspendPanelTopMost();

        editor.FormClosed += (_, _) =>
        {
            _editors.Remove(editor);
            editor.Dispose();

            if (_editors.Count == 0 && _editorsTopMostSuspended)
            {
                _editorsTopMostSuspended = false;
                RestorePanelTopMost(true);
            }
        };

        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (editor.IsDisposed) return;
            editor.Show();
            editor.Activate();
        }));
    }

    private static void CopyScreenshotToClipboard(Bitmap bitmap)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Bitmap? copy = null;
            try
            {
                copy = new Bitmap(bitmap);
                Clipboard.SetImage(copy);
                copy = null;
                return;
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
            using var about = new AboutForm();
            about.ShowDialog();
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
        foreach (var editor in _editors.ToArray())
        {
            try { editor.Close(); } catch { }
            try { editor.Dispose(); } catch { }
        }
        _editors.Clear();
        try { _panel?.Dispose(); } catch { }
        _panel = null;
        try { _hotkeys.Dispose(); } catch { }
        try { _recordingBlinkTimer.Stop(); _recordingBlinkTimer.Dispose(); } catch { }
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
            try { _recordingBlinkTimer.Stop(); _recordingBlinkTimer.Dispose(); } catch { }
            try { _tray.Dispose(); } catch { }
            try { _idleIcon.Dispose(); } catch { }
            try { _recordingIcon.Dispose(); } catch { }
            try { _menu.Dispose(); } catch { }
            try { _dispatcher.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }
}
