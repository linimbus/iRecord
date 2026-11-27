// iRecord - FFmpeg discovery and process helpers.
// MP4 is recorded natively; FFmpeg is used for MKV/MOV remuxing and WebM/GIF conversion.
using System.Diagnostics;

namespace iRecord.Capture;

/// <summary>
/// Locates ffmpeg.exe and runs it without a console window, capturing stdout/stderr.
/// </summary>
public static class FfmpegTools
{
    /// <summary>Searches the app folder, the configured dev path, and PATH for ffmpeg.exe.</summary>
    public static string? Detect()
    {
        var candidates = new List<string>();

        string local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        candidates.Add(local);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "ffmpeg", "bin", "ffmpeg.exe"));
        candidates.Add(@"D:\software\ffmpeg-8.1.1\bin\ffmpeg.exe");

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try { candidates.Add(Path.Combine(dir.Trim(), "ffmpeg.exe")); }
                catch { }
            }
        }

        foreach (var candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch
            {
            }
        }

        return null;
    }

    /// <summary>Uses the configured executable when valid, otherwise falls back to <see cref="Detect"/>.</summary>
    public static string? Resolve(string? configured)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                return Path.GetFullPath(configured);
        }
        catch
        {
        }

        if (!string.IsNullOrWhiteSpace(configured) && string.Equals(configured, "ffmpeg", StringComparison.OrdinalIgnoreCase))
            return "ffmpeg";

        return Detect();
    }

    /// <summary>Creates a console-less process start info with argument-list escaping.</summary>
    public static ProcessStartInfo CreateStartInfo(string ffmpeg, IEnumerable<string> args,
        bool redirectStdin = false, bool redirectStdout = false, bool redirectStderr = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = redirectStdin,
            RedirectStandardOutput = redirectStdout,
            RedirectStandardError = redirectStderr,
            WorkingDirectory = AppContext.BaseDirectory
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        return psi;
    }

    /// <summary>Runs FFmpeg to completion and returns the combined stdout/stderr for diagnostics.</summary>
    public static async Task<(bool Ok, string Output)> RunAsync(string ffmpeg, IEnumerable<string> args)
    {
        try
        {
            var psi = CreateStartInfo(ffmpeg, args, redirectStdout: true, redirectStderr: true);
            using var process = new Process { StartInfo = psi };
            if (!process.Start())
                return (false, "failed to start ffmpeg");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            string output = (await stdoutTask.ConfigureAwait(false)) + (await stderrTask.ConfigureAwait(false));
            return (process.ExitCode == 0, output);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
