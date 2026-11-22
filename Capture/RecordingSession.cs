using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using iRecord.App;

namespace iRecord.Capture;

public enum CaptureMode
{
    FullScreen,
    Region,
    Window
}

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

    private Thread? _thread;
    private Process? _videoProcess;
    private Stream? _videoStdin;
    private AudioRecorder? _audio;
    private readonly Stopwatch _clock = new();
    private readonly ManualResetEventSlim _pauseReached = new(false);
    private string? _ffmpegStderr;
    private string? _tempDir;
    private string? _tempVideo;
    private string? _currentVideoPath;
    private readonly List<string> _videoSegments = new();
    private int _segmentIndex;
    private int _segmentFrames;
    private string? _systemAudioPath;
    private string? _micAudioPath;
    private long _systemAudioBytes;
    private long _micAudioBytes;
    private int _frameCount;
    private int _finalized;

    private volatile bool _running;
    private volatile bool _paused;
    private string? _error;

    public CaptureMode Mode { get; }
    public int Width { get; }
    public int Height { get; }
    public bool IsPaused => _paused;
    public bool IsEncoding { get; private set; }
    public TimeSpan Elapsed => _clock.Elapsed;
    public string? LastError => _error;

    public event Action<string>? Finished;
    public event Action<string>? Failed;
    public event Action? StateChanged;

    public RecordingSession(AppSettings settings, CaptureMode mode, Rectangle rect, IntPtr windowHandle)
    {
        _settings = settings;
        Mode = mode;
        _windowHandle = windowHandle;

        Width = Math.Max(2, rect.Width & ~1);
        Height = Math.Max(2, rect.Height & ~1);
        _captureRect = new Rectangle(rect.X, rect.Y, Width, Height);
        _fps = Math.Clamp(settings.Fps, 5, 60);
        _ffmpeg = FfmpegTools.Resolve(settings.FfmpegPath) ?? "ffmpeg.exe";

        _clock.Restart();
        _clock.Stop();
    }

    public bool Start()
    {
        try
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "iRecord", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            if (!StartVideoSegment())
                return false;

            if (_settings.Audio is not "none")
            {
                _audio = new AudioRecorder();
                _audio.Start(_tempDir, _settings.Audio);
            }

            _running = true;
            _clock.Restart();
            Diag($"Start mode={Mode} {Width}x{Height} fps={_fps} ffmpeg={_ffmpeg} audio={_settings.Audio} mouse={_settings.MouseMode} format={_settings.Format}");
            _thread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "iRecord.Capture",
                Priority = ThreadPriority.AboveNormal
            };
            _thread.Start();
            StateChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _error = Localization.Format("error_capture", ex.Message);
            CleanupTemp();
            return false;
        }
    }

    private bool StartVideoSegment()
    {
        if (_tempDir == null)
            return false;

        string crf = _settings.Quality switch
        {
            "low" => "28",
            "medium" => "23",
            _ => "18"
        };

        string segmentPath = Path.Combine(_tempDir, $"video_{_segmentIndex:000}.mp4");
        _segmentIndex++;

        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "rawvideo",
            "-pixel_format", "bgra",
            "-video_size", $"{Width}x{Height}",
            "-framerate", _fps.ToString(),
            "-use_wallclock_as_timestamps", "1",
            "-i", "pipe:0",
            "-an",
            "-c:v", "libx264",
            "-preset", "ultrafast",
            "-crf", crf,
            "-pix_fmt", "yuv420p",
            "-fps_mode", "vfr",
            "-movflags", "+faststart",
            segmentPath
        };

        var psi = FfmpegTools.CreateStartInfo(_ffmpeg, args, redirectStdin: true, redirectStderr: true);
        var process = new Process { StartInfo = psi };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _ffmpegStderr = e.Data;
        };

        if (!process.Start())
        {
            _error = Localization.T("notify_ffmpeg_missing");
            return false;
        }

        process.BeginErrorReadLine();
        _videoProcess = process;
        _videoStdin = process.StandardInput.BaseStream;
        _currentVideoPath = segmentPath;
        _segmentFrames = 0;
        Diag($"ffmpeg started pid={process.Id} segment={segmentPath}");
        return true;
    }

    private void CaptureLoop()
    {
        var frameInterval = 1000.0 / _fps;
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var overlay = new MouseOverlay(_settings.MouseMode);
        var buffer = new byte[Width * Height * 4];
        var sw = new Stopwatch();

        try
        {
            while (_running)
            {
                if (_paused)
                {
                    _pauseReached.Set();
                    Thread.Sleep(20);
                    continue;
                }

                _pauseReached.Reset();

                Rectangle sourceRect;
                bool minimized = false;

                if (Mode == CaptureMode.Window)
                {
                    if (!NativeMethods.IsWindow(_windowHandle))
                    {
                        _error = Localization.T("error_window_closed");
                        break;
                    }

                    minimized = NativeMethods.IsIconic(_windowHandle);
                    if (minimized)
                    {
                        sourceRect = _captureRect;
                    }
                    else
                    {
                        NativeMethods.GetWindowRect(_windowHandle, out var wr);
                        sourceRect = new Rectangle(wr.Left, wr.Top, Width, Height);
                    }
                }
                else
                {
                    sourceRect = _captureRect;
                }

                sw.Restart();

                try
                {
                    if (minimized)
                    {
                        graphics.Clear(Color.Black);
                    }
                    else
                    {
                        graphics.CopyFromScreen(sourceRect.X, sourceRect.Y, 0, 0,
                            new Size(Width, Height), CopyPixelOperation.SourceCopy);
                        if (overlay.Enabled)
                            overlay.Draw(graphics, sourceRect);
                    }
                }
                catch (Exception ex)
                {
                    _error = Localization.Format("error_capture", ex.Message);
                    break;
                }

                try
                {
                    var data = bitmap.LockBits(new Rectangle(0, 0, Width, Height),
                        ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        if (data.Stride == Width * 4)
                        {
                            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                        }
                        else
                        {
                            for (int y = 0; y < Height; y++)
                            {
                                IntPtr row = IntPtr.Add(data.Scan0, y * data.Stride);
                                Marshal.Copy(row, buffer, y * Width * 4, Width * 4);
                            }
                        }
                    }
                    finally
                    {
                        bitmap.UnlockBits(data);
                    }
                }
                catch (Exception ex)
                {
                    _error = Localization.Format("error_capture", ex.Message);
                    break;
                }

                try
                {
                    _videoStdin!.Write(buffer, 0, buffer.Length);
                    _videoStdin.Flush();
                    _frameCount++;
                    _segmentFrames++;
                    if (_frameCount == 1 || _frameCount % 30 == 0)
                        Diag($"frame {_frameCount} rect={sourceRect}");
                }
                catch (Exception ex)
                {
                    _error = Localization.Format("error_encode",
                        string.IsNullOrWhiteSpace(_ffmpegStderr) ? "ffmpeg pipe closed" : _ffmpegStderr!);
                    Diag("pipe write failed: " + ex + " stderr=" + _ffmpegStderr);
                    break;
                }

                double spent = sw.Elapsed.TotalMilliseconds;
                double delay = frameInterval - spent;
                if (delay > 1)
                    Thread.Sleep((int)delay);
            }
        }
        catch (Exception ex)
        {
            if (_error == null)
                _error = Localization.Format("error_capture", ex.Message);
        }
        finally
        {
            if (_error != null)
            {
                _running = false;
                _clock.Stop();
                ThreadPool.QueueUserWorkItem(_ => FinalizeAbort());
            }
        }
    }

    private void FinalizeAbort()
    {
        if (Interlocked.Exchange(ref _finalized, 1) != 0) return;
        string message = _error ?? Localization.T("error_capture");
        FinishVideoSegment();
        try { _audio?.Dispose(); } catch { }
        _audio = null;
        CleanupTemp();
        Failed?.Invoke(message);
        StateChanged?.Invoke();
    }

    public void TogglePause()
    {
        if (!_running) return;

        if (_paused)
        {
            _paused = false;
            _pauseReached.Reset();
            if (!StartVideoSegment())
            {
                _running = false;
                _clock.Stop();
                ThreadPool.QueueUserWorkItem(_ => FinalizeAbort());
                StateChanged?.Invoke();
                return;
            }
            _clock.Start();
            _audio?.Resume();
        }
        else
        {
            _paused = true;
            _clock.Stop();
            _audio?.Pause();
            _pauseReached.Wait(2000);
            FinishVideoSegment();
        }

        StateChanged?.Invoke();
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _finalized, 1) != 0) return;

        _running = false;
        _clock.Stop();
        try { _thread?.Join(5000); } catch { }
        Diag($"Stop requested frames={_frameCount} elapsed={_clock.Elapsed.TotalSeconds:F2}");

        FinishVideoSegment();

        if (_audio != null)
        {
            _systemAudioPath = _audio.SystemPath;
            _micAudioPath = _audio.MicPath;
            _systemAudioBytes = _audio.SystemBytes;
            _micAudioBytes = _audio.MicBytes;
            Diag($"audio bytes system={_systemAudioBytes} mic={_micAudioBytes} errors={string.Join('|', _audio.Errors)}");
        }

        try { _audio?.Dispose(); } catch { }
        _audio = null;

        IsEncoding = true;
        StateChanged?.Invoke();
        Task.Run(FinishEncoding);
    }

    private void CloseVideoEncoder()
    {
        try { _videoStdin?.Flush(); } catch { }
        try { _videoStdin?.Close(); } catch { }
        try { _videoStdin?.Dispose(); } catch { }
        _videoStdin = null;

        var process = _videoProcess;
        _videoProcess = null;
        if (process != null)
        {
            try
            {
                if (!process.WaitForExit(15000))
                {
                    try { process.Kill(true); } catch { }
                }
            }
            catch { }
            finally
            {
                try { Diag($"ffmpeg exit={process.ExitCode} stderr={_ffmpegStderr}"); } catch { }
                try { process.Dispose(); } catch { }
            }
        }
    }

    private void FinishVideoSegment()
    {
        string? path = _currentVideoPath;
        int frames = _segmentFrames;
        CloseVideoEncoder();
        _currentVideoPath = null;
        _segmentFrames = 0;

        if (path == null) return;

        try
        {
            if (frames > 0 && File.Exists(path) && new FileInfo(path).Length > 0)
            {
                _videoSegments.Add(path);
                Diag($"segment finalized {path} frames={frames} len={new FileInfo(path).Length}");
            }
            else
            {
                try { File.Delete(path); } catch { }
                Diag($"segment discarded {path} frames={frames}");
            }
        }
        catch { }
    }

    private async Task FinishEncoding()
    {
        string? result = null;
        string? error = null;

        try
        {
            _tempVideo = await ConcatVideosAsync().ConfigureAwait(false);

            if (_frameCount == 0 || _tempVideo == null || !File.Exists(_tempVideo) ||
                new FileInfo(_tempVideo).Length == 0)
            {
                error = Localization.Format("error_encode",
                    string.IsNullOrWhiteSpace(_ffmpegStderr) ? "no frames captured" : _ffmpegStderr!);
            }
            else
            {
                Diag($"temp video len={new FileInfo(_tempVideo).Length} frames={_frameCount}");
                string outputFolder = string.IsNullOrWhiteSpace(_settings.OutputFolder)
                    ? SettingsStore.CreateDefault().OutputFolder
                    : _settings.OutputFolder;
                Directory.CreateDirectory(outputFolder);

                string ext = ExtensionFor(_settings.Format);
                string fileName = $"iRecord_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";
                string target = Path.Combine(outputFolder, fileName);

                var (ok, output) = await EncodeAsync(target).ConfigureAwait(false);
                Diag($"Encode target={target} ok={ok} output={output}");
                if (ok && File.Exists(target))
                    result = target;
                else
                    error = Localization.Format("error_encode",
                        string.IsNullOrWhiteSpace(output) ? "ffmpeg failed" : output.Trim());
            }
        }
        catch (Exception ex)
        {
            error = Localization.Format("error_encode", ex.Message);
        }
        finally
        {
            IsEncoding = false;
            CleanupTemp();
        }

        if (result != null)
            Finished?.Invoke(result);
        else
            Failed?.Invoke(error ?? Localization.T("error_encode"));

        StateChanged?.Invoke();
    }

    private async Task<string?> ConcatVideosAsync()
    {
        if (_videoSegments.Count == 0) return null;
        if (_videoSegments.Count == 1) return _videoSegments[0];
        if (_tempDir == null) return null;

        try
        {
            string listPath = Path.Combine(_tempDir, "segments.txt");
            var lines = _videoSegments.Select(p =>
                "file '" + p.Replace('\\', '/').Replace("'", "'\\''") + "'");
            File.WriteAllLines(listPath, lines);

            string outPath = Path.Combine(_tempDir, "video_concat.mp4");
            var args = new List<string>
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-f", "concat", "-safe", "0", "-i", listPath,
                "-c", "copy", "-movflags", "+faststart", outPath
            };

            var (ok, output) = await FfmpegTools.RunAsync(_ffmpeg, args).ConfigureAwait(false);
            Diag($"concat segments={_videoSegments.Count} ok={ok} output={output}");
            if (ok && File.Exists(outPath) && new FileInfo(outPath).Length > 0)
                return outPath;

            return await ConcatByReencodeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Diag("concat failed: " + ex);
            return null;
        }
    }

    private async Task<string?> ConcatByReencodeAsync()
    {
        if (_tempDir == null) return null;

        string outPath = Path.Combine(_tempDir, "video_concat.mp4");
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
        foreach (var segment in _videoSegments)
        {
            args.Add("-i");
            args.Add(segment);
        }

        string filter = string.Concat(Enumerable.Range(0, _videoSegments.Count).Select(i => $"[{i}:v]")) +
                        $"concat=n={_videoSegments.Count}:v=1:a=0[v]";
        args.AddRange(new[]
        {
            "-filter_complex", filter,
            "-map", "[v]",
            "-c:v", "libx264",
            "-preset", "ultrafast",
            "-crf", "18",
            "-pix_fmt", "yuv420p",
            "-fps_mode", "vfr",
            "-movflags", "+faststart",
            outPath
        });

        var (ok, output) = await FfmpegTools.RunAsync(_ffmpeg, args).ConfigureAwait(false);
        Diag($"concat reencode ok={ok} output={output}");
        return ok && File.Exists(outPath) && new FileInfo(outPath).Length > 0 ? outPath : null;
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

    private async Task<(bool Ok, string Output)> EncodeAsync(string target)
    {
        string format = NormalizeFormat(_settings.Format);

        return format switch
        {
            "gif" => await EncodeGifAsync(target).ConfigureAwait(false),
            "webm" => await EncodeWebmAsync(target).ConfigureAwait(false),
            _ => await EncodeMuxedAsync(target, format).ConfigureAwait(false)
        };
    }

    private async Task<(bool Ok, string Output)> EncodeGifAsync(string target)
    {
        int gifWidth = Math.Min(Width, 1600) & ~1;
        if (gifWidth < 2) gifWidth = 2;

        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-i", _tempVideo!,
            "-vf", $"fps={_settings.GifFps},scale={gifWidth}:-2:flags=lanczos,split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5",
            "-loop", "0",
            target
        };
        return await FfmpegTools.RunAsync(_ffmpeg, args).ConfigureAwait(false);
    }

    private async Task<(bool Ok, string Output)> EncodeMuxedAsync(string target, string format)
    {
        var systemAudio = _systemAudioPath;
        var micAudio = _micAudioPath;

        bool hasSystem = _systemAudioBytes > 0 && systemAudio != null && File.Exists(systemAudio);
        bool hasMic = _micAudioBytes > 0 && micAudio != null && File.Exists(micAudio);

        if (!hasSystem && !hasMic)
        {
            if (format == "mp4")
                return MoveTempToTarget(target);
        }

        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-i", _tempVideo!
        };

        if (hasSystem) args.AddRange(new[] { "-i", systemAudio! });
        if (hasMic) args.AddRange(new[] { "-i", micAudio! });

        if (hasSystem && hasMic)
        {
            args.AddRange(new[]
            {
                "-filter_complex",
                "[1:a]aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo[a1];" +
                "[2:a]aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo[a2];" +
                "[a1][a2]amix=inputs=2:duration=longest:dropout_transition=0[a]",
                "-map", "0:v:0", "-map", "[a]",
                "-c:v", "copy",
                "-c:a", "aac",
                "-b:a", "192k",
                "-shortest"
            });
        }
        else if (hasSystem || hasMic)
        {
            args.AddRange(new[]
            {
                "-map", "0:v:0", "-map", "1:a:0",
                "-c:v", "copy",
                "-c:a", "aac",
                "-b:a", "192k",
                "-ar", "48000",
                "-ac", "2",
                "-shortest"
            });
        }
        else
        {
            args.AddRange(new[] { "-map", "0:v:0", "-c:v", "copy" });
        }

        if (format is "mp4" or "mov")
            args.AddRange(new[] { "-movflags", "+faststart" });

        args.Add(target);
        return await FfmpegTools.RunAsync(_ffmpeg, args).ConfigureAwait(false);
    }

    private async Task<(bool Ok, string Output)> EncodeWebmAsync(string target)
    {
        var systemAudio = _systemAudioPath;
        var micAudio = _micAudioPath;

        bool hasSystem = _systemAudioBytes > 0 && systemAudio != null && File.Exists(systemAudio);
        bool hasMic = _micAudioBytes > 0 && micAudio != null && File.Exists(micAudio);

        string crf = _settings.Quality switch
        {
            "low" => "42",
            "medium" => "34",
            _ => "28"
        };

        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-i", _tempVideo!
        };

        if (hasSystem) args.AddRange(new[] { "-i", systemAudio! });
        if (hasMic) args.AddRange(new[] { "-i", micAudio! });

        if (hasSystem && hasMic)
        {
            args.AddRange(new[]
            {
                "-filter_complex",
                "[1:a]aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo[a1];" +
                "[2:a]aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo[a2];" +
                "[a1][a2]amix=inputs=2:duration=longest:dropout_transition=0[a]",
                "-map", "0:v:0", "-map", "[a]"
            });
        }
        else if (hasSystem || hasMic)
        {
            args.AddRange(new[] { "-map", "0:v:0", "-map", "1:a:0" });
        }
        else
        {
            args.AddRange(new[] { "-map", "0:v:0" });
        }

        args.AddRange(new[]
        {
            "-c:v", "libvpx-vp9",
            "-b:v", "0",
            "-crf", crf,
            "-row-mt", "1",
            "-cpu-used", "4",
            "-pix_fmt", "yuv420p"
        });

        if (hasSystem || hasMic)
        {
            args.AddRange(new[]
            {
                "-c:a", "libopus",
                "-b:a", "128k",
                "-ar", "48000",
                "-ac", "2",
                "-shortest"
            });
        }

        args.Add(target);
        return await FfmpegTools.RunAsync(_ffmpeg, args).ConfigureAwait(false);
    }

    private (bool Ok, string Output) MoveTempToTarget(string target)
    {
        try
        {
            bool existed = File.Exists(target);
            long before = existed ? new FileInfo(target).Length : -1;
            Diag($"moving temp({new FileInfo(_tempVideo!).Length}) -> {target} existed={existed} before={before}");
            File.Move(_tempVideo!, target, overwrite: true);
            Diag($"moved target len={new FileInfo(target).Length}");
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
        _running = false;
        try { _thread?.Join(1000); } catch { }
        CloseVideoEncoder();
        try { _audio?.Dispose(); } catch { }
        CleanupTemp();
    }
}
