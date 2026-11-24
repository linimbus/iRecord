namespace iRecord.App;

public sealed class AppSettings
{
    public string Language { get; set; } = string.Empty;
    public string Format { get; set; } = "mp4";
    public int Fps { get; set; } = 30;
    public int GifFps { get; set; } = 15;
    public string Quality { get; set; } = "high";
    public string Audio { get; set; } = "system";
    public string MouseMode { get; set; } = "clicks";
    public int MicVolume { get; set; } = 100;
    public int SystemLevelGain { get; set; } = 3;
    public int MicLevelGain { get; set; } = 4;
    public string OutputFolder { get; set; } = string.Empty;
    public string FfmpegPath { get; set; } = string.Empty;
    public string StartMode { get; set; } = "fullscreen";
    public string HotkeyStart { get; set; } = "F9";
    public string HotkeyStop { get; set; } = "F10";
    public string HotkeyPause { get; set; } = "F11";
    public string HotkeyScreenshotRegion { get; set; } = string.Empty;
    public string HotkeyScreenshotWindow { get; set; } = string.Empty;
    public string HotkeyScreenshotScroll { get; set; } = string.Empty;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
