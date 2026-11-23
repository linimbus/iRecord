using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using iRecord.App;
using iRecord.Capture;

namespace iRecord.Cli;

public static class CliRunner
{
    private const int ExitOk = 0;
    private const int ExitFailure = 1;
    private const int ExitUsage = 2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed class CliUsageException : Exception
    {
        public CliUsageException(string message) : base(message) { }
    }

    public static int Run(string[] args)
    {
        TryAttachConsole();

        try
        {
            string command = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "help";
            string[] rest = args.Skip(1).ToArray();

            return command switch
            {
                "help" or "-h" or "--help" => ShowHelp(),
                "version" or "-v" or "--version" => ShowVersion(),
                "record" => Record(rest),
                "list-screens" or "screens" => ListScreens(rest),
                "list-windows" or "windows" => ListWindows(rest),
                _ => UsageError($"Unknown command: {command}")
            };
        }
        catch (CliUsageException ex)
        {
            return UsageError(ex.Message);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return ExitFailure;
        }
    }

    private static void TryAttachConsole()
    {
        try
        {
            if (NativeMethods.GetConsoleWindow() != IntPtr.Zero)
                return;

            if (Console.IsOutputRedirected || Console.IsErrorRedirected)
                return;

            NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);
        }
        catch
        {
        }
    }

    private static void Log(string message)
    {
        try { Console.Error.WriteLine(message); } catch { }
    }

    private static void Out(string message)
    {
        try { Console.WriteLine(message); } catch { }
    }

    private static int UsageError(string message)
    {
        Log("error: " + message);
        Log("Run \"iRecord-cli.exe help\" for usage.");
        return ExitUsage;
    }

    private static string Version =>
        typeof(CliRunner).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static int ShowVersion()
    {
        Out($"iRecord {Version}");
        return ExitOk;
    }

    private static int ShowHelp()
    {
        Out(HelpText);
        return ExitOk;
    }

    private sealed class Options
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

        public static Options Parse(string[] args, IEnumerable<string> flags, IEnumerable<string> values)
        {
            var flagSet = new HashSet<string>(flags, StringComparer.OrdinalIgnoreCase);
            var valueSet = new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
            var result = new Options();

            for (int i = 0; i < args.Length; i++)
            {
                string raw = args[i];
                if (!raw.StartsWith("-", StringComparison.Ordinal) || raw == "-" || raw == "--")
                    throw new CliUsageException($"Unexpected argument: {raw}");

                string name = raw.TrimStart('-');
                string? inline = null;
                int eq = name.IndexOf('=');
                if (eq >= 0)
                {
                    inline = name[(eq + 1)..];
                    name = name[..eq];
                }

                if (flagSet.Contains(name))
                {
                    if (inline != null)
                        throw new CliUsageException($"Option --{name} does not take a value.");
                    result._flags.Add(name);
                    continue;
                }

                if (!valueSet.Contains(name))
                    throw new CliUsageException($"Unknown option: {raw}");

                string value;
                if (inline != null)
                {
                    value = inline;
                }
                else
                {
                    if (i + 1 >= args.Length)
                        throw new CliUsageException($"Missing value for {raw}");
                    value = args[++i];
                }

                if (string.IsNullOrWhiteSpace(value))
                    throw new CliUsageException($"Empty value for --{name}");

                result._values[name] = value;
            }

            return result;
        }

        public bool Flag(string name) => _flags.Contains(name);
        public string? Get(string name) => _values.TryGetValue(name, out var value) ? value : null;
    }

    private static int Record(string[] args)
    {
        var options = Options.Parse(args,
            new[] { "json", "quiet" },
            new[]
            {
                "mode", "screen", "region", "window", "duration", "delay", "fps",
                "format", "quality", "audio", "mouse", "output", "result-file"
            });

        bool json = options.Flag("json");
        bool quiet = options.Flag("quiet");
        string? resultFile = options.Get("result-file");

        var settings = SettingsStore.Load();

        settings.Format = ParseChoice(options.Get("format"), settings.Format,
            new[] { "mp4", "mkv", "mov", "webm", "gif" }, "format");
        settings.Quality = ParseChoice(options.Get("quality"), settings.Quality,
            new[] { "low", "medium", "high" }, "quality");
        settings.Audio = ParseChoice(options.Get("audio"), settings.Audio,
            new[] { "none", "system", "mic", "both" }, "audio");
        settings.MouseMode = ParseChoice(options.Get("mouse"), settings.MouseMode,
            new[] { "off", "cursor", "clicks" }, "mouse");

        if (options.Get("fps") is { } fpsText)
        {
            if (!int.TryParse(fpsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fps) ||
                fps < 5 || fps > 60)
                throw new CliUsageException($"Invalid --fps: {fpsText} (expected 5-60).");
            settings.Fps = fps;
        }

        double duration = ParseNumber(options, "duration", 10, 0, 86400);
        double delay = ParseNumber(options, "delay", 0, 0, 3600);

        string? explicitFile = null;
        if (options.Get("output") is { } output)
        {
            string full = Path.GetFullPath(output);
            if (Directory.Exists(full) || !LooksLikeMediaFile(full))
            {
                settings.OutputFolder = full;
            }
            else
            {
                explicitFile = full;
                settings.OutputFolder = Path.GetDirectoryName(full) ?? settings.OutputFolder;
            }
        }

        var (mode, rect, windowHandle, modeName) = ResolveTarget(options);

        if (rect.Width < 8 || rect.Height < 8)
            return FailRecord(json, resultFile, "The selected capture area is too small.");

        if (FfmpegTools.Resolve(settings.FfmpegPath) == null)
            return FailRecord(json, resultFile,
                "FFmpeg was not found. Place ffmpeg.exe next to iRecord.exe or configure it in Settings.");

        var stopSignal = new ManualResetEventSlim(false);
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            stopSignal.Set();
        };
        Console.CancelKeyPress += onCancel;

        var done = new ManualResetEventSlim(false);
        string? path = null;
        string? error = null;

        try
        {
            if (delay > 0)
            {
                if (!quiet) Log($"[record] waiting {delay:0.#}s before recording...");
                if (stopSignal.Wait(TimeSpan.FromSeconds(delay)))
                    return FailRecord(json, resultFile, "Cancelled before recording started.");
            }

            using var session = new RecordingSession(settings, mode, rect, windowHandle);
            session.Finished += p => { path = p; done.Set(); };
            session.Failed += e => { error = e; done.Set(); };

            if (!session.Start())
                return FailRecord(json, resultFile, session.LastError ?? "Could not start recording.");

            if (!quiet)
            {
                Log($"[record] started mode={modeName} {session.Width}x{session.Height} " +
                    $"format={settings.Format} fps={settings.Fps} audio={settings.Audio} " +
                    $"mouse={settings.MouseMode} duration={(duration > 0 ? duration.ToString("0.#", CultureInfo.InvariantCulture) + "s" : "until Ctrl+C")}");
            }

            using var timer = duration > 0
                ? new System.Threading.Timer(_ => stopSignal.Set(), null, TimeSpan.FromSeconds(duration), Timeout.InfiniteTimeSpan)
                : null;

            using var progressStop = new ManualResetEventSlim(false);
            Thread? progress = null;
            if (!quiet)
            {
                progress = new Thread(() =>
                {
                    while (!progressStop.Wait(1000))
                    {
                        if (done.IsSet) break;
                        Log($"[record] elapsed {session.Elapsed:hh\\:mm\\:ss}");
                    }
                })
                {
                    IsBackground = true,
                    Name = "iRecord.CliProgress"
                };
                progress.Start();
            }

            int signal = WaitHandle.WaitAny(new[] { stopSignal.WaitHandle, done.WaitHandle });

            if (signal == 0 && !done.IsSet)
            {
                if (!quiet) Log("[record] stopping and encoding...");
                session.Stop();
                done.Wait(TimeSpan.FromMinutes(30));
            }

            progressStop.Set();
            if (progress != null) progress.Join(1500);

            if (path == null)
                return FailRecord(json, resultFile, error ?? "Recording failed.");

            if (explicitFile != null)
            {
                try
                {
                    string? dir = Path.GetDirectoryName(explicitFile);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    if (!string.Equals(Path.GetFullPath(path), explicitFile, StringComparison.OrdinalIgnoreCase))
                        File.Move(path, explicitFile, overwrite: true);
                    path = explicitFile;
                }
                catch (Exception ex)
                {
                    return FailRecord(json, resultFile, $"Could not move output file: {ex.Message}");
                }
            }

            long size = 0;
            try { size = new FileInfo(path).Length; } catch { }

            var payload = new
            {
                success = true,
                command = "record",
                path,
                format = settings.Format,
                mode = modeName,
                width = session.Width,
                height = session.Height,
                fps = settings.Fps,
                durationSeconds = Math.Round(session.Elapsed.TotalSeconds, 2),
                sizeBytes = size
            };

            string text = JsonSerializer.Serialize(payload, JsonOptions);
            if (json) Out(text);
            if (!string.IsNullOrWhiteSpace(resultFile)) TryWriteFile(resultFile!, text);
            if (!json) Out("SAVED: " + path);

            return ExitOk;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    private static (CaptureMode Mode, Rectangle Rect, IntPtr Window, string Name) ResolveTarget(Options options)
    {
        string modeName = (options.Get("mode") ?? "fullscreen").Trim().ToLowerInvariant();

        switch (modeName)
        {
            case "region":
            {
                string? regionText = options.Get("region");
                if (string.IsNullOrWhiteSpace(regionText))
                    throw new CliUsageException("--region x,y,w,h is required for mode=region.");

                var parts = regionText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 4 ||
                    !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                    !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ||
                    !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) ||
                    !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
                    throw new CliUsageException($"Invalid --region: {regionText} (expected x,y,w,h).");

                return (CaptureMode.Region, new Rectangle(x, y, w, h), IntPtr.Zero, "region");
            }

            case "window":
            {
                string? spec = options.Get("window");
                if (string.IsNullOrWhiteSpace(spec))
                    throw new CliUsageException("--window <id|title> is required for mode=window.");

                var hit = FindWindow(spec);
                if (hit == null)
                    throw new CliUsageException($"Window not found: {spec}");

                return (CaptureMode.Window, hit.Value.ScreenRect, hit.Value.Handle, "window");
            }

            case "fullscreen":
            case "screen":
            {
                var screens = Screen.AllScreens;
                int index = -1;
                if (options.Get("screen") is { } screenText)
                {
                    if (!int.TryParse(screenText, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) ||
                        index < 0 || index >= screens.Length)
                        throw new CliUsageException($"Invalid --screen: {screenText} (use list-screens to see indexes).");
                }

                var screen = index >= 0 ? screens[index] : Screen.PrimaryScreen ?? screens[0];
                return (CaptureMode.FullScreen, screen.Bounds, IntPtr.Zero, "fullscreen");
            }

            default:
                throw new CliUsageException($"Invalid --mode: {modeName} (expected fullscreen, region or window).");
        }
    }

    private static WindowHit? FindWindow(string spec)
    {
        IntPtr handle = IntPtr.Zero;
        string text = spec.Trim();

        bool numeric;
        long parsed = 0;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            numeric = long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
        else
            numeric = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

        if (numeric)
            handle = new IntPtr(parsed);

        if (handle != IntPtr.Zero && NativeMethods.IsWindow(handle))
        {
            if (NativeMethods.GetWindowRect(handle, out var r))
                return new WindowHit(handle, NativeMethods.GetWindowTitle(handle),
                    Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
        }

        if (numeric)
            return null;

        WindowHit? best = null;
        long bestArea = 0;
        uint myPid = (uint)Environment.ProcessId;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            if (NativeMethods.IsIconic(hwnd)) return true;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == myPid) return true;

            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;
            if (NativeMethods.IsCloaked(hwnd)) return true;

            string title = NativeMethods.GetWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title)) return true;

            string process = string.Empty;
            try { process = Process.GetProcessById((int)pid).ProcessName; } catch { }

            if (title.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0 &&
                process.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0)
                return true;

            if (!NativeMethods.GetWindowRect(hwnd, out var r)) return true;
            var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (rect.Width < 16 || rect.Height < 16) return true;

            long area = (long)rect.Width * rect.Height;
            if (area > bestArea)
            {
                bestArea = area;
                best = new WindowHit(hwnd, title, rect);
            }

            return true;
        }, IntPtr.Zero);

        return best;
    }

    private static int ListScreens(string[] args)
    {
        Options.Parse(args, new[] { "json", "pretty" }, Array.Empty<string>());

        var screens = Screen.AllScreens.Select((s, index) => new
        {
            index,
            primary = s.Primary,
            x = s.Bounds.X,
            y = s.Bounds.Y,
            width = s.Bounds.Width,
            height = s.Bounds.Height,
            device = s.DeviceName
        });

        Out(JsonSerializer.Serialize(screens, JsonOptions));
        return ExitOk;
    }

    private static int ListWindows(string[] args)
    {
        Options.Parse(args, new[] { "json", "pretty" }, Array.Empty<string>());

        var windows = new List<object>();
        uint myPid = (uint)Environment.ProcessId;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            if (NativeMethods.IsIconic(hwnd)) return true;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == myPid) return true;

            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;
            if (NativeMethods.IsCloaked(hwnd)) return true;

            string title = NativeMethods.GetWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title)) return true;

            if (!NativeMethods.GetWindowRect(hwnd, out var r)) return true;
            int width = r.Width;
            int height = r.Height;
            if (width < 16 || height < 16) return true;

            string process = string.Empty;
            try { process = Process.GetProcessById((int)pid).ProcessName; } catch { }

            windows.Add(new
            {
                id = hwnd.ToInt64(),
                idHex = "0x" + hwnd.ToInt64().ToString("X"),
                pid,
                process,
                title,
                x = r.Left,
                y = r.Top,
                width,
                height
            });

            return true;
        }, IntPtr.Zero);

        Out(JsonSerializer.Serialize(windows, JsonOptions));
        return ExitOk;
    }

    private static string ParseChoice(string? value, string fallback, string[] allowed, string option)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        string normalized = value.Trim().ToLowerInvariant();
        if (!allowed.Contains(normalized, StringComparer.Ordinal))
            throw new CliUsageException($"Invalid --{option}: {value} (expected {string.Join(", ", allowed)}).");
        return normalized;
    }

    private static double ParseNumber(Options options, string name, double fallback, double min, double max)
    {
        string? raw = options.Get(name);
        if (raw == null) return fallback;

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
            value < min || value > max)
            throw new CliUsageException($"Invalid --{name}: {raw} (expected {min:0.#}-{max:0.#}).");

        return value;
    }

    private static bool LooksLikeMediaFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp4" or ".mkv" or ".mov" or ".webm" or ".gif";
    }

    private static void TryWriteFile(string path, string content)
    {
        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, content);
        }
        catch (Exception ex)
        {
            Log("warning: could not write result file: " + ex.Message);
        }
    }

    private static int FailRecord(bool json, string? resultFile, string message)
    {
        var payload = new { success = false, command = "record", error = message };
        string text = JsonSerializer.Serialize(payload, JsonOptions);

        if (json) Out(text);
        if (!string.IsNullOrWhiteSpace(resultFile)) TryWriteFile(resultFile!, text);
        Log("error: " + message);
        return ExitFailure;
    }

    private static readonly string HelpText = $$"""
iRecord {{Version}} - headless command line interface

USAGE
  iRecord-cli.exe <command> [options]
  iRecord.exe <command> [options]   (same commands; the tray executable also accepts them)

COMMANDS
  record          Record the screen and save a video. Blocks until encoding finishes.
  list-screens    Print connected monitors as a JSON array.
  list-windows    Print capturable top-level windows as a JSON array.
  help            Show this help.
  version         Print the version.

RECORD OPTIONS
  --mode <fullscreen|region|window>  Capture scope (default: fullscreen).
  --screen <index>                   Monitor index for fullscreen mode (see list-screens; default: primary).
  --region <x,y,w,h>                 Region in physical pixels (required for mode=region).
  --window <id|title>                Window id (decimal or 0x hex) or a title/process substring
                                     (required for mode=window).
  --duration <seconds>               Stop after N seconds; 0 records until Ctrl+C (default: 10).
  --delay <seconds>                  Wait N seconds before recording starts (default: 0).
  --fps <5-60>                       Frame rate (default: saved setting, usually 30).
  --format <mp4|mkv|mov|webm|gif>    Output container (default: saved setting).
  --quality <low|medium|high>        Encoding quality (default: saved setting).
  --audio <none|system|mic|both>     Audio source (default: saved setting).
  --mouse <off|cursor|clicks>        Mouse pointer/click overlay (default: saved setting).
  --output <path>                    Output folder, or an exact file path with a media extension.
  --json                             Print one JSON object to stdout; progress and errors go to stderr.
  --result-file <path>               Also write the JSON result to this file.
  --quiet                            Suppress progress messages.

EXIT CODES
  0  success
  1  recording or encoding failed
  2  invalid command line usage

OUTPUT
  Human mode prints progress and a final line: SAVED: <path>
  With --json, stdout contains exactly one JSON object, for example:
    {
      "success": true,
      "command": "record",
      "path": "C:\\Users\\me\\Videos\\iRecord\\iRecord_20260101_120000.mp4",
      "format": "mp4",
      "mode": "region",
      "width": 1280,
      "height": 720,
      "fps": 30,
      "durationSeconds": 8.02,
      "sizeBytes": 1234567
    }
  On failure:
    { "success": false, "command": "record", "error": "..." }
  list-screens and list-windows always print a JSON array to stdout.

FOR LLM AGENTS
  Use iRecord-cli.exe: it is a console application, so shells and process runners wait for it
  and receive its exit code. iRecord.exe never opens a window in CLI mode, but Windows shells
  may not wait for GUI-subsystem programs.
  iRecord-cli.exe is a non-interactive screen recorder.
  Recommended flow:
    1. iRecord-cli.exe list-screens
       -> [{"index":0,"primary":true,"x":0,"y":0,"width":1920,"height":1080,"device":"\\\\.\\DISPLAY1"}]
    2. iRecord-cli.exe list-windows
       -> [{"id":12345,"idHex":"0x3039","pid":6789,"process":"notepad",
            "title":"Untitled - Notepad","x":100,"y":100,"width":800,"height":600}]
    3. iRecord-cli.exe record --mode region --region 0,0,1280,720 --duration 8 --format mp4 --audio none --mouse clicks --output "C:\out" --json

  Rules for agents:
  - record is synchronous: it returns only after the file is fully encoded.
  - Set the tool timeout to at least duration + 120 seconds. WebM takes longer (allow 5x).
  - Use --result-file if stdout cannot be captured; read that file for the JSON result.
  - Quote paths containing spaces and prefer absolute paths.
  - list-windows only returns visible, capturable windows; use its id with --window.
  - Use --duration 0 only for interactive use, never for automation.
  - CLI options override settings for this run only; saved settings are not modified.
  - Requires an unlocked, interactive Windows desktop in the current session. It cannot
    capture the lock screen or other user sessions.
  - A running tray instance does not block the CLI, but do not record the same audio
    device twice at the same time.
  - Default output folder when --output is omitted: the configured output folder
    (usually Videos\iRecord).

EXAMPLES
  iRecord-cli.exe record --duration 5 --format gif --output "C:\temp\demo.gif"
  iRecord-cli.exe record --mode window --window notepad --duration 10 --json
  iRecord-cli.exe record --mode fullscreen --screen 1 --duration 15 --audio both --mouse clicks
  iRecord-cli.exe record --mode region --region 100,100,800,600 --duration 20 --result-file "C:\temp\result.json"
  iRecord-cli.exe list-windows
""";
}
