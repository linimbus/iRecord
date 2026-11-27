// iRecord - ScreenRecorderLib recording pipeline.
// Owns capture sources, audio packets, pause/resume, red border rect, and output conversion.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using iRecord.App;
using ScreenRecorderLib;

namespace iRecord.Capture;

/// <summary>Capture target chosen before a recording starts.</summary>
public enum CaptureMode
{
    /// <summary>A complete monitor (selected by the mouse or the multi-monitor picker).</summary>
    FullScreen,

    /// <summary>A screen-space rectangle selected by the user.</summary>
    Region,

    /// <summary>A top-level HWND; the session follows its position while recording.</summary>
    Window
}

/// <summary>
/// One recording run. Captures fullscreen/region/window video and optional audio via ScreenRecorderLib,
/// exposes live levels/elapsed time, then converts the temporary MP4 into the requested format.
/// </summary>
public sealed class RecordingSession : IDisposable
{
    private static readonly object LogLock = new();

    private static void Diag(string message)
    {
        try
        {
            lock (LogLock)
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "iRecord.diag.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} {message}\r\n");
        }
        catch { }
    }

    private readonly AppSettings _settings;
    private readonly Rectangle _captureRect;
    private readonly IntPtr _windowHandle;
    private readonly string _ffmpeg;
    private readonly int _fps;

    private Recorder? _recorder;
    private TaskCompletionSource<string?>? _recordingCompletion;
    private CaptureAudioSource? _micSource;
    private CancellationTokenSource? _windowWatch;
    private readonly Stopwatch _clock = new();
    private readonly object _rectLock = new();

    private string? _tempDir;
    private string? _recordingPath;
    private string? _systemAudioId;
    private string? _micAudioId;
    private string? _error;
    private int _stopRequested;
    private int _disposed;
    private int _started;
    private bool _clockStarted;
    private bool _hasSystemAudio;
    private bool _hasMicAudio;
    private int _recordWidth;
    private int _recordHeight;
    private int _micVolumePercent;
    private float _systemLevel;
    private float _micLevel;

    private Rectangle _currentCaptureRect;
    private volatile bool _running;
    private volatile bool _paused;

    // Public state is intentionally lightweight: the tray/control bar polls these values.
    public CaptureMode Mode { get; }
    public int Width { get; }
    public int Height { get; }
    public bool IsPaused => _paused;
    public bool IsEncoding { get; private set; }
    public TimeSpan Elapsed => _clock.Elapsed;
    public string? LastError => _error;

    /// <summary>True only when a real loopback source was added to the recorder.</summary>
    public bool HasSystemAudio => _hasSystemAudio;

    /// <summary>True only when a real microphone source was added to the recorder.</summary>
    public bool HasMicAudio => _hasMicAudio;
    public float SystemAudioLevel => _systemLevel;
    public float MicAudioLevel => _micLevel;
    public int MicVolumePercent => _micVolumePercent;

    public Rectangle CurrentCaptureRect
    {
        get
        {
            lock (_rectLock)
                return _currentCaptureRect;
        }
    }

    public void SetMicVolume(int percent)
    {
        _micVolumePercent = Math.Clamp(percent, 0, 200);

        if (_micSource == null)
            return;

        try
        {
            _micSource.Volume = _micVolumePercent / 100f;
            _recorder?.GetDynamicOptionsBuilder()
                .SetUpdatedAudioSource(_micSource)
                .Apply();
        }
        catch
        {
        }
    }

    public event Action<string>? Finished;
    public event Action<string>? Failed;
    public event Action? Started;
    public event Action? StateChanged;

    public RecordingSession(AppSettings settings, CaptureMode mode, Rectangle rect, IntPtr windowHandle)
    {
        _settings = settings;
        Mode = mode;
        _windowHandle = windowHandle;

        Width = Math.Max(2, rect.Width & ~1);
        Height = Math.Max(2, rect.Height & ~1);
        _recordWidth = Width;
        _recordHeight = Height;
        _captureRect = new Rectangle(rect.X, rect.Y, Width, Height);
        _fps = Math.Clamp(settings.Fps, 5, 60);
        _ffmpeg = FfmpegTools.Resolve(settings.FfmpegPath) ?? "ffmpeg.exe";
        _micVolumePercent = Math.Clamp(settings.MicVolume, 0, 200);
        _currentCaptureRect = _captureRect;
    }

    /// <summary>
    /// Builds RecorderOptions, starts ScreenRecorderLib into a temporary MP4, and returns
    /// false (with <see cref="LastError"/>) when source/audio/FFmpeg prerequisites fail.
    /// </summary>
    public bool Start()
    {
        try
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "iRecord", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            var options = BuildOptions();
            if (options == null)
            {
                CleanupTemp();
                return false;
            }

            _recordingPath = Path.Combine(_tempDir, "recording.mp4");
            _recordingCompletion = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _recorder = Recorder.CreateRecorder(options);
            _recorder.OnRecordingComplete += OnRecordingComplete;
            _recorder.OnRecordingFailed += OnRecordingFailed;
            _recorder.OnStatusChanged += OnStatusChanged;
            _recorder.OnFrameRecorded += OnFrameRecorded;
            _recorder.OnAudioPacketRecorded += OnAudioPacketRecorded;

            _running = true;
            _recorder.Record(_recordingPath);

            if (Mode == CaptureMode.Window)
                StartWindowWatcher();

            Diag($"Start mode={Mode} {_recordWidth}x{_recordHeight} fps={_fps} " +
                 $"source={(_recorder != null ? "screenrecorderlib" : "none")} " +
                 $"audio={_settings.Audio} system={_hasSystemAudio} mic={_hasMicAudio} " +
                 $"mouse={_settings.MouseMode} format={_settings.Format} ffmpeg={_ffmpeg}");
            StateChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _error = Localization.Format("error_capture", ex.Message);
            _running = false;
            try { _recorder?.Dispose(); } catch { }
            _recorder = null;
            CleanupTemp();
            return false;
        }
    }

    /// <summary>Maps application settings to ScreenRecorderLib options; null means a precondition failed.</summary>
    private RecorderOptions? BuildOptions()
    {
        if (string.Equals(NormalizeFormat(_settings.Format), "mp4", StringComparison.OrdinalIgnoreCase) == false &&
            FfmpegTools.Resolve(_settings.FfmpegPath) == null)
        {
            _error = Localization.T("notify_ffmpeg_missing");
            return null;
        }

        var options = RecorderOptions.Default;

        options.OutputOptions.RecorderMode = RecorderMode.Video;
        options.OutputOptions.Stretch = StretchMode.Fill;
        options.VideoEncoderOptions.Framerate = _fps;
        options.VideoEncoderOptions.IsFixedFramerate = true;
        options.VideoEncoderOptions.Bitrate = CalculateVideoBitrate();
        options.VideoEncoderOptions.IsHardwareEncodingEnabled = true;
        options.VideoEncoderOptions.IsMp4FastStartEnabled = true;
        options.VideoEncoderOptions.IsFragmentedMp4Enabled = false;

        bool pointer = !string.Equals(_settings.MouseMode, "off", StringComparison.OrdinalIgnoreCase);
        bool clicks = string.Equals(_settings.MouseMode, "clicks", StringComparison.OrdinalIgnoreCase);

        options.MouseOptions.IsMousePointerEnabled = pointer;
        options.MouseOptions.IsMouseClicksDetected = clicks;
        options.MouseOptions.MouseClickDetectionMode = MouseDetectionMode.Hook;
        options.MouseOptions.MouseClickDetectionDuration = 420;
        options.MouseOptions.MouseClickDetectionRadius = 26;
        options.MouseOptions.MouseLeftClickDetectionColor = "#FF5252";
        options.MouseOptions.MouseRightClickDetectionColor = "#5296FF";

        var source = CreateRecordingSource(pointer);
        if (source == null)
            return null;

        options.SourceOptions.RecordingSources = new List<RecordingSourceBase> { source };
        options.OutputOptions.OutputFrameSize = new ScreenSize(_recordWidth, _recordHeight);

        ConfigureAudio(options);
        return options;
    }

    /// <summary>
    /// Creates a display/window source. Region mode crops via SourceRect on the monitor
    /// containing the rectangle, which keeps capture efficient.
    /// </summary>
    private RecordingSourceBase? CreateRecordingSource(bool pointer)
    {
        if (Mode == CaptureMode.Window)
        {
            if (_windowHandle == IntPtr.Zero || !NativeMethods.IsWindow(_windowHandle))
            {
                _error = Localization.T("error_window_closed");
                return null;
            }

            return new WindowRecordingSource(_windowHandle)
            {
                IsCursorCaptureEnabled = pointer,
                IsBorderRequired = false,
                Stretch = StretchMode.Fill,
                OutputSize = new ScreenSize(_recordWidth, _recordHeight)
            };
        }

        var screen = Screen.FromRectangle(_captureRect) ?? Screen.PrimaryScreen;
        if (screen == null)
        {
            _error = Localization.Format("error_capture", "no display available");
            return null;
        }

        var displays = Recorder.GetDisplays();
        var display = displays.FirstOrDefault(d =>
                          string.Equals(d.DeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase))
                      ?? displays.FirstOrDefault();

        if (display == null)
        {
            _error = Localization.Format("error_capture", "no display available");
            return null;
        }

        var source = new DisplayRecordingSource(display.DeviceName)
        {
            IsCursorCaptureEnabled = pointer,
            IsBorderRequired = false,
            Stretch = StretchMode.Fill,
            OutputSize = new ScreenSize(_recordWidth, _recordHeight)
        };

        if (Mode == CaptureMode.Region)
        {
            var bounds = screen.Bounds;
            int left = Math.Max(0, _captureRect.Left - bounds.Left);
            int top = Math.Max(0, _captureRect.Top - bounds.Top);
            int right = Math.Min(bounds.Width, _captureRect.Right - bounds.Left);
            int bottom = Math.Min(bounds.Height, _captureRect.Bottom - bounds.Top);
            int width = (right - left) & ~1;
            int height = (bottom - top) & ~1;

            if (width < 2 || height < 2)
            {
                _error = Localization.Format("error_capture", "region is outside the display");
                return null;
            }

            _recordWidth = width;
            _recordHeight = height;
            source.SourceRect = new ScreenRect(left, top, width, height);
            source.OutputSize = new ScreenSize(width, height);
        }

        return source;
    }

    /// <summary>
    /// Adds one optional loopback and one optional microphone source. Missing devices are
    /// tolerated so video-only recording still succeeds; IDs are kept for live level metering.
    /// </summary>
    private void ConfigureAudio(RecorderOptions options)
    {
        _hasSystemAudio = false;
        _hasMicAudio = false;
        _systemAudioId = null;
        _micAudioId = null;
        _micSource = null;

        var sources = new List<AudioSourceBase>();

        if (_settings.Audio is "system" or "both")
        {
            try
            {
                var device = Recorder.GetSystemAudioLoopbackDevices()
                    .FirstOrDefault(d => d.IsDefaultDevice)
                    ?? Recorder.GetSystemAudioLoopbackDevices().FirstOrDefault();

                if (device != null && !string.IsNullOrWhiteSpace(device.DeviceName))
                {
                    var source = new LoopbackAudioSource { DeviceName = device.DeviceName };
                    sources.Add(source);
                    _systemAudioId = source.ID;
                    _hasSystemAudio = true;
                }
            }
            catch (Exception ex)
            {
                Diag("system audio unavailable: " + ex.Message);
            }
        }

        if (_settings.Audio is "mic" or "both")
        {
            try
            {
                var device = Recorder.GetSystemAudioCaptureDevices()
                    .FirstOrDefault(d => d.IsDefaultDevice)
                    ?? Recorder.GetSystemAudioCaptureDevices().FirstOrDefault();

                if (device != null && !string.IsNullOrWhiteSpace(device.DeviceName))
                {
                    var source = new CaptureAudioSource
                    {
                        DeviceName = device.DeviceName,
                        Volume = _micVolumePercent / 100f
                    };
                    sources.Add(source);
                    _micSource = source;
                    _micAudioId = source.ID;
                    _hasMicAudio = true;
                }
            }
            catch (Exception ex)
            {
                Diag("microphone unavailable: " + ex.Message);
            }
        }

        if (sources.Count == 0)
        {
            options.AudioOptions.IsAudioEnabled = false;
            options.AudioOptions.AudioSources = new List<AudioSourceBase>();
            return;
        }

        options.AudioOptions.AudioSources = sources;
        options.AudioOptions.IsAudioEnabled = true;
        options.AudioOptions.IsAudioPacketPreviewEnabled = true;
        options.AudioOptions.Bitrate = AudioBitrate.bitrate_192kbps;
        options.AudioOptions.Channels = AudioChannels.Stereo;
    }

    private int CalculateVideoBitrate()
    {
        int baseBitrate = _settings.Quality switch
        {
            "low" => 2_000_000,
            "medium" => 4_500_000,
            _ => 8_000_000
        };

        double pixelScale = (_recordWidth * (double)_recordHeight) / (1920.0 * 1080.0);
        double fpsScale = _fps / 30.0;
        double scale = Math.Clamp(pixelScale, 0.4, 2.0) * Math.Clamp(fpsScale, 0.5, 2.0);
        return (int)Math.Clamp(baseBitrate * scale, 1_000_000, 40_000_000);
    }

    /// <summary>Our best available "recording actually started" signal; starts the elapsed clock once.</summary>
    private void OnFrameRecorded(object? sender, FrameRecordedEventArgs e)
    {
        if (Volatile.Read(ref _stopRequested) != 0 ||
            Interlocked.Exchange(ref _started, 1) != 0)
            return;

        _clockStarted = true;
        if (!_paused)
            _clock.Start();

        Diag($"first frame={e.FrameNumber} timestamp={e.Timestamp}");
        Started?.Invoke();
    }

    private void MarkStarted()
    {
        if (Volatile.Read(ref _stopRequested) != 0 ||
            Interlocked.Exchange(ref _started, 1) != 0)
            return;

        _clockStarted = true;
        if (!_paused)
            _clock.Start();

        Started?.Invoke();
    }

    private void StartWindowWatcher()
    {
        var cts = new CancellationTokenSource();
        _windowWatch = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    if (!NativeMethods.IsWindow(_windowHandle))
                    {
                        _error ??= Localization.T("error_window_closed");
                        RequestStop();
                        break;
                    }

                    if (!NativeMethods.IsIconic(_windowHandle) &&
                        NativeMethods.GetWindowRect(_windowHandle, out var wr))
                    {
                        lock (_rectLock)
                            _currentCaptureRect = new Rectangle(wr.Left, wr.Top, Width, Height);
                    }

                    await Task.Delay(200, cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Diag("window watcher failed: " + ex);
            }
        });
    }

    private void OnRecordingComplete(object? sender, RecordingCompleteEventArgs e)
    {
        Diag($"recorder complete path={e.FilePath} len={(File.Exists(e.FilePath) ? new FileInfo(e.FilePath).Length : 0)}");
        _recordingCompletion?.TrySetResult(e.FilePath);
    }

    private void OnRecordingFailed(object? sender, RecordingFailedEventArgs e)
    {
        _error ??= Localization.Format("error_capture", e.Error);
        Diag("recorder failed: " + e.Error);
        _recordingCompletion?.TrySetResult(null);
        RequestStop();
    }

    private void OnStatusChanged(object? sender, RecordingStatusEventArgs e)
    {
        Diag("recorder status=" + e.Status);

        if (e.Status == RecorderStatus.Recording)
        {
            MarkStarted();
            if (_clockStarted && !_paused && !_clock.IsRunning)
                _clock.Start();
        }
        else if (e.Status is RecorderStatus.Paused or RecorderStatus.Finishing or RecorderStatus.Idle)
        {
            _clock.Stop();
        }

        StateChanged?.Invoke();
    }

    /// <summary>Computes per-source peaks from 48 kHz/16-bit stereo packet preview data.</summary>
    private void OnAudioPacketRecorded(object? sender, AudioDataRecordedEventArgs e)
    {
        if (_paused || e.AudioData == null)
            return;

        foreach (var packet in e.AudioData.Sources)
        {
            if (packet.Data == null || packet.Data.Length < 2)
                continue;

            float peak = ComputePeak(packet.Data);
            if (packet.Id == _systemAudioId)
                _systemLevel = Math.Max(peak, _systemLevel * 0.75f);
            else if (packet.Id == _micAudioId)
                _micLevel = Math.Max(peak, _micLevel * 0.75f);
        }
    }

    private static float ComputePeak(byte[] data)
    {
        float peak = 0f;
        for (int i = 0; i + 1 < data.Length; i += 2)
        {
            short value = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i, 2));
            float abs = Math.Abs(value / 32768f);
            if (abs > peak)
                peak = abs;
        }

        return Math.Min(1f, peak);
    }

    /// <summary>Pauses/resumes both ScreenRecorderLib and the elapsed clock; output excludes paused time.</summary>
    public void TogglePause()
    {
        if (!_running || Volatile.Read(ref _stopRequested) != 0 || IsEncoding)
            return;

        try
        {
            if (_paused)
            {
                _paused = false;
                _recorder?.Resume();
                if (_clockStarted)
                    _clock.Start();
            }
            else
            {
                _paused = true;
                _clock.Stop();
                _recorder?.Pause();
            }
        }
        catch (Exception ex)
        {
            _error ??= Localization.Format("error_capture", ex.Message);
            RequestStop();
        }

        StateChanged?.Invoke();
    }

    public void Stop()
    {
        RequestStop();
    }

    /// <summary>Idempotently stops capture, hides the frame clock, and starts the async finalize/convert task.</summary>
    private void RequestStop()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) != 0)
            return;

        _running = false;
        _clock.Stop();

        try { _windowWatch?.Cancel(); } catch { }

        IsEncoding = true;
        try
        {
            _recorder?.Stop();
        }
        catch (Exception ex)
        {
            _error ??= Localization.Format("error_encode", ex.Message);
            _recordingCompletion?.TrySetResult(null);
        }

        StateChanged?.Invoke();
        _ = Task.Run(FinishEncodingAsync);
    }

    /// <summary>
    /// Waits for ScreenRecorderLib to finalize the MP4, converts it to the requested format,
    /// then raises Finished/Failed and always cleans the temp directory.
    /// </summary>
    private async Task FinishEncodingAsync()
    {
        string? result = null;
        string? error = null;

        try
        {
            string? recorded = null;
            var completion = _recordingCompletion;
            if (completion != null)
            {
                try
                {
                    recorded = await completion.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    error = Localization.Format("error_encode", "recording finalize timeout");
                }
                catch (Exception ex)
                {
                    error = Localization.Format("error_encode", ex.Message);
                }
            }

            error ??= _error;

            if (error == null &&
                (string.IsNullOrWhiteSpace(recorded) || !File.Exists(recorded) ||
                 new FileInfo(recorded!).Length == 0))
            {
                error = Localization.Format("error_encode", "no frames captured");
            }

            if (error == null)
            {
                string outputFolder = string.IsNullOrWhiteSpace(_settings.OutputFolder)
                    ? SettingsStore.CreateDefault().OutputFolder
                    : _settings.OutputFolder;
                Directory.CreateDirectory(outputFolder);

                string ext = ExtensionFor(_settings.Format);
                string fileName = $"iRecord_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";
                string target = Path.Combine(outputFolder, fileName);

                var (ok, output) = await EncodeAsync(recorded!, target).ConfigureAwait(false);
                Diag($"Encode target={target} ok={ok} output={output}");

                if (ok && File.Exists(target) && new FileInfo(target).Length > 0)
                    result = target;
                else
                    error = Localization.Format("error_encode",
                        string.IsNullOrWhiteSpace(output) ? "conversion failed" : output.Trim());
            }
        }
        catch (Exception ex)
        {
            error = Localization.Format("error_encode", ex.Message);
        }
        finally
        {
            IsEncoding = false;
            try { _windowWatch?.Cancel(); } catch { }
            try { _windowWatch?.Dispose(); } catch { }
            _windowWatch = null;

            var recorder = _recorder;
            _recorder = null;
            try { recorder?.Dispose(); } catch { }

            CleanupTemp();
        }

        if (result != null)
            Finished?.Invoke(result);
        else
            Failed?.Invoke(error ?? Localization.T("error_encode"));

        StateChanged?.Invoke();
    }

    private static string NormalizeFormat(string? format) => format?.Trim().ToLowerInvariant() switch
    {
        "gif" => "gif",
        "webm" => "webm",
        "mkv" => "mkv",
        "mov" => "mov",
        _ => "mp4"
    };

    private static string ExtensionFor(string? format) => NormalizeFormat(format);

    /// <summary>
    /// MP4 is moved as-is; MKV/MOV are stream-copied by FFmpeg; WebM/GIF are transcoded.
    /// </summary>
    private async Task<(bool Ok, string Output)> EncodeAsync(string source, string target)
    {
        string format = NormalizeFormat(_settings.Format);

        return format switch
        {
            "mp4" => MoveToTarget(source, target),
            "gif" => await EncodeGifAsync(source, target).ConfigureAwait(false),
            "webm" => await EncodeWebmAsync(source, target).ConfigureAwait(false),
            _ => await EncodeRemuxAsync(source, target, format).ConfigureAwait(false)
        };
    }

    private async Task<(bool Ok, string Output)> EncodeRemuxAsync(string source, string target, string format)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-i", source,
            "-map", "0:v:0", "-map", "0:a?",
            "-c", "copy"
        };

        if (format == "mov")
            args.AddRange(new[] { "-movflags", "+faststart" });

        args.Add(target);
        return await FfmpegTools.RunAsync(_ffmpeg, args).ConfigureAwait(false);
    }

    private async Task<(bool Ok, string Output)> EncodeGifAsync(string source, string target)
    {
        int gifWidth = Math.Min(_recordWidth, 1600) & ~1;
        if (gifWidth < 2) gifWidth = 2;

        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-i", source,
            "-vf", $"fps={_settings.GifFps},scale={gifWidth}:-2:flags=lanczos,split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5",
            "-an",
            "-loop", "0",
            target
        };
        return await FfmpegTools.RunAsync(_ffmpeg, args).ConfigureAwait(false);
    }

    private async Task<(bool Ok, string Output)> EncodeWebmAsync(string source, string target)
    {
        string crf = _settings.Quality switch
        {
            "low" => "42",
            "medium" => "34",
            _ => "28"
        };

        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-i", source,
            "-map", "0:v:0", "-map", "0:a?",
            "-c:v", "libvpx-vp9",
            "-b:v", "0",
            "-crf", crf,
            "-row-mt", "1",
            "-cpu-used", "4",
            "-pix_fmt", "yuv420p",
            "-c:a", "libopus",
            "-b:a", "128k",
            "-ar", "48000",
            "-ac", "2",
            target
        };

        return await FfmpegTools.RunAsync(_ffmpeg, args).ConfigureAwait(false);
    }

    private (bool Ok, string Output) MoveToTarget(string source, string target)
    {
        try
        {
            File.Move(source, target, overwrite: true);
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            Diag("move failed: " + ex);
            return (false, ex.Message);
        }
    }

    private void CleanupTemp()
    {
        if (_tempDir == null) return;
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
        _tempDir = null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _running = false;
        _clock.Stop();

        try { _windowWatch?.Cancel(); } catch { }
        try { _windowWatch?.Dispose(); } catch { }
        _windowWatch = null;

        try { _recorder?.Stop(); } catch { }
        try { _recorder?.Dispose(); } catch { }
        _recorder = null;
        _recordingCompletion?.TrySetResult(null);
        CleanupTemp();
    }
}
