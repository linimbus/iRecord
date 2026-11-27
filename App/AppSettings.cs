// iRecord - persisted application settings model.
// Serialized as JSON by SettingsStore; add new user options here and normalize them there.
namespace iRecord.App;

/// <summary>
/// All user-configurable options. Values are plain strings/numbers so old settings files stay readable.
/// </summary>
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
    public int PanelOpacity { get; set; } = 90;
    public int RecordingBarOpacity { get; set; } = 80;
    public string OutputFolder { get; set; } = string.Empty;
    public string FfmpegPath { get; set; } = string.Empty;
    public string StartMode { get; set; } = "fullscreen";
    public string HotkeyStart { get; set; } = "F9";
    public string HotkeyStop { get; set; } = "F10";
    public string HotkeyPause { get; set; } = "F11";
    public string HotkeyShowPanel { get; set; } = string.Empty;
    public string HotkeyScreenshotSingle { get; set; } = string.Empty;
    public string HotkeyScreenshotRegion { get; set; } = string.Empty;
    public string HotkeyScreenshotWindow { get; set; } = string.Empty;
    public string HotkeyScreenshotScroll { get; set; } = string.Empty;

    /// <summary>Shallow copy used by the settings dialog for cancelable edits.</summary>
    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
