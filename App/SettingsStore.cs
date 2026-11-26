using System.Text.Json;
using iRecord.Capture;

namespace iRecord.App;

public static class SettingsStore
{
    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iRecord");

    public static string FilePath => Path.Combine(Directory, "settings.json");

    public static AppSettings CreateDefault()
    {
        string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrWhiteSpace(videos))
            videos = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return new AppSettings
        {
            Language = Localization.DetectLanguage(),
            OutputFolder = Path.Combine(videos, "iRecord"),
            FfmpegPath = FfmpegTools.Detect() ?? string.Empty
        };
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    Normalize(settings);
                    return settings;
                }
            }
        }
        catch
        {
        }

        var def = CreateDefault();
        Normalize(def);
        return def;
    }

    public static void Save(AppSettings settings)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }

    private static void Normalize(AppSettings s)
    {
        if (!Localization.IsSupported(s.Language))
            s.Language = Localization.DetectLanguage();

        if (s.Format is not ("mp4" or "mkv" or "mov" or "webm" or "gif")) s.Format = "mp4";
        if (s.Quality is not ("low" or "medium" or "high")) s.Quality = "high";
        if (s.Audio is not ("none" or "system" or "mic" or "both")) s.Audio = "system";
        if (string.Equals(s.MouseMode, "none", StringComparison.OrdinalIgnoreCase)) s.MouseMode = "off";
        if (s.MouseMode is not ("off" or "cursor" or "clicks")) s.MouseMode = "clicks";
        if (s.StartMode is not ("fullscreen" or "region" or "window")) s.StartMode = "fullscreen";

        s.Fps = Math.Clamp(s.Fps, 5, 60);
        s.GifFps = Math.Clamp(s.GifFps, 5, 30);
        s.MicVolume = Math.Clamp(s.MicVolume, 0, 200);
        s.SystemLevelGain = Math.Clamp(s.SystemLevelGain, 1, 10);
        s.MicLevelGain = Math.Clamp(s.MicLevelGain, 1, 10);
        s.PanelOpacity = Math.Clamp(s.PanelOpacity, 20, 100);
        s.RecordingBarOpacity = Math.Clamp(s.RecordingBarOpacity, 20, 100);

        if (string.IsNullOrWhiteSpace(s.OutputFolder))
            s.OutputFolder = CreateDefault().OutputFolder;

        if (string.IsNullOrWhiteSpace(s.FfmpegPath) || !File.Exists(s.FfmpegPath))
            s.FfmpegPath = FfmpegTools.Detect() ?? s.FfmpegPath;

        if (string.IsNullOrWhiteSpace(s.HotkeyStart)) s.HotkeyStart = "F9";
        if (string.IsNullOrWhiteSpace(s.HotkeyStop)) s.HotkeyStop = "F10";
        if (string.IsNullOrWhiteSpace(s.HotkeyPause)) s.HotkeyPause = "F11";
        s.HotkeyShowPanel ??= string.Empty;
        s.HotkeyScreenshotSingle ??= string.Empty;
        s.HotkeyScreenshotRegion ??= string.Empty;
        s.HotkeyScreenshotWindow ??= string.Empty;
        s.HotkeyScreenshotScroll ??= string.Empty;
    }
}
