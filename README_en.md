# iRecord 1.5.4

English | [简体中文](README.md)

A minimal native screen recorder for Windows. It runs in the system tray, lets you start / pause / stop recording with global hotkeys, supports **MP4 / MKV / MOV / WebM / GIF** output, can capture the mouse pointer and click effects into the video, and uses [**ScreenRecorderLib**](https://github.com/sskodje/ScreenRecorderLib) for high-performance native capture with hardware encoding at a fixed frame rate; MKV / MOV / WebM / GIF are converted with open-source [**FFmpeg**](https://ffmpeg.org/).

## Features

- Lives in the system tray, single instance, small footprint
- Headless CLI: `iRecord-cli.exe` records without any UI, designed for scripts and LLM tool calls
- Three capture scopes:
  - Full screen (with multiple monitors, a red selection frame follows the mouse across screens; click to start; single monitor starts immediately)
  - Region (drag to select)
  - Window (hover to highlight, click to select)
- Screenshots: region screenshots (single selection), multi-region screenshots (select several regions and merge them into one image), window screenshots, and scrolling window screenshots that auto-scroll and stitch seamlessly into one PNG; all are saved to disk and copied to the clipboard automatically
- The control panel groups the pin, screenshot and recording actions, separated by vertical dividers
- The control panel has an always-on-top (pin) button so it stays above other windows
- Output formats: `MP4` (native ScreenRecorderLib H.264, fixed frame rate), `MKV` (H.264), `MOV` (H.264), `WebM` (VP9 + Opus) or `GIF`\r\n- Capture engine: ScreenRecorderLib native capture and hardware encoding for full screen, region and window recording, avoiding frame-rate drift from manual sampling
- Mouse capture: hidden / show pointer / show pointer with highlighted left, right and middle clicks
- Audio capture: no audio / system audio / microphone / system + microphone
- Global hotkeys: start, stop, pause/resume (default `F9` / `F10` / `F11`); show-panel and single / multi-region / window / scrolling screenshot hotkeys are configurable in Settings
- On-screen recording bar showing elapsed time with pause, stop and hide buttons; a hidden bar can be restored from the tray menu; the red recording indicator is on for 1 second and off for 1 second, and stops blinking while paused; it fades to semi-transparent when the mouse leaves and restores on hover, and is excluded from the recording
- Tray icon: a white ring is always shown; while recording, the red center blinks with 1 second on and 1 second off, and disappears when stopped
- Recording border: a red frame marks the actual capture area in both tray and headless CLI recording, supports full screen / region / window, follows window movement, and is excluded from the recording
- Volume feedback: the control bar shows live system and microphone levels as five vertical segments; microphone volume is configured in Settings (0–200%)
- Pause/resume removes the paused period from both video and audio, keeping them in sync
- Settings UI available in Simplified Chinese, Traditional Chinese, English, Japanese, Korean, German, French and Spanish
- Configurable frame rate, GIF frame rate, quality, system / microphone level gain, microphone volume, recording bar opacity, mouse capture mode, output folder, FFmpeg path, hotkeys and default start mode
- Version `1.5.4`

## Requirements

- Windows 10 2004 or later (Windows 11 recommended), x64 only
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (framework-dependent deployment)
- [Visual C++ 2015–2022 Redistributable (x64)](https://aka.ms/vs/17/release/vc_redist.x64.exe) (required by ScreenRecorderLib)
- MP4 output does not require FFmpeg; MKV / MOV / WebM / GIF require an FFmpeg executable (official site: <https://ffmpeg.org/>)

## FFmpeg Setup

FFmpeg is located in the following order:

1. `ffmpeg.exe` specified manually in Settings
2. `ffmpeg.exe` next to the application
3. `D:\software\ffmpeg-8.1.1\bin\ffmpeg.exe`
4. `ffmpeg.exe` on the system `PATH`

MP4 output does not require FFmpeg. If FFmpeg is not found and the current format needs conversion, a tray notification appears. Set the path under "Settings → FFmpeg path" or click "Auto-detect".

## Usage

1. Run `iRecord.exe`; a foreground control panel appears: an always-on-top pin button on the far left, then screenshots (region / multi-region / window / scrolling long screenshot), recording (full screen / region / window), plus settings and open-folder. Region screenshots capture a single selection; multi-region screenshots let you select several regions and press Enter to merge them vertically into one image. The app also stays in the system tray (bottom right); closing the panel hides it to the tray, and double-clicking the tray icon reopens it. Both screenshots and recordings are saved to the configured output folder.
2. Right-click the tray icon and choose:
   - Start recording (full screen / region / window)
   - Screenshots (region / window / scrolling long screenshot)
   - Pause / Resume
   - Stop
   - Settings
   - Open output folder
3. You can also use global hotkeys:
   - `F9` start recording (using the default start mode)
   - `F10` stop recording
   - `F11` pause / resume
4. While recording, a control bar appears at the bottom center of the desktop; click "Pause" or "Stop" directly. A red border marks the capture area. When audio capture is enabled, the bar shows system / microphone levels; microphone volume is configured in Settings.
5. Encoding starts automatically after stopping; a tray notification then shows where the file was saved. The default output folder is `Videos\iRecord`.

## Build

Requires the .NET 10 SDK.

```powershell
dotnet restore
dotnet build -c Release
```

## Publish the executable

The script `tools\build.ps1` performs a **framework-dependent single-file publish with an external ScreenRecorderLib.dll**:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build.ps1
```

Output folder: `publish\`, containing `iRecord.exe` (tray app), `iRecord-cli.exe` (headless CLI) and `ScreenRecorderLib.dll`. Keep `ScreenRecorderLib.dll` next to the executables. The target machine needs the .NET 10 Desktop Runtime and the Visual C++ 2015–2022 Redistributable (x64).

To build and package a zip in one step (README files and `skills\` included):

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\package.ps1
```

This produces `publish\iRecord-v<version>-win-x64.zip`. On Windows you can also double-click `tools\package.cmd`. Override `-Runtime`, `-Configuration` or `-Output` as needed, or pass `-SkipBuild` to repackage only.

## Headless CLI (automation / LLMs)

`iRecord-cli.exe` is a console application intended for scripts and LLM tool calls: it blocks until encoding completes and returns an exit code. `iRecord.exe` accepts the same arguments, but it is a GUI-subsystem program, so some shells do not wait for it.

Run `iRecord-cli.exe help` for the full capability reference, including JSON schemas and guidance for LLM agents. Common commands:

```powershell
# List monitors / capturable windows (JSON)
iRecord-cli.exe list-screens
iRecord-cli.exe list-windows

# Record a screen region for 8 seconds, return a JSON result
iRecord-cli.exe record --mode region --region 0,0,1280,720 --duration 8 `
    --format mp4 --audio none --mouse clicks --output "C:\out" --json

# Record a window for 10 seconds as WebM
iRecord-cli.exe record --mode window --window notepad --duration 10 --format webm --json
```

- With `--json`, stdout contains exactly one JSON object; progress and errors go to stderr.
- `--result-file <path>` also writes the same result to a file, for environments that cannot capture stdout.
- Exit codes: `0` success, `1` recording/encoding failed, `2` invalid arguments.
- Headless recording shows the red recording border (full screen / region / window); the control bar and volume cells are not shown.
- Headless mode still requires an unlocked, interactive desktop in the current session.

## Project structure

```
Program.cs                     Entry point (tray mode / CLI mode)
App/                           Tray, settings, localization, hotkeys, settings persistence
Capture/                       Screen capture, region/window pickers, audio recording, mouse overlay, FFmpeg encoding
Cli/                           Headless CLI: argument parsing, recording flow, help text
UI/                            Settings window, recording bar, hotkey input box
Assets/app.ico                 Application icon
tools/build.ps1                Publish script
tools/package.ps1              One-step publish + zip (package.cmd for double-click)
tools/make-icon.ps1            Regenerate the icon
tools/iRecord.Cli/             Console launcher project (produces iRecord-cli.exe)
```

## Implementation notes

- Screen capture: ScreenRecorderLib (Windows Graphics Capture / Desktop Duplication) for full screen, region and window recording with fixed-frame-rate encoding
- Audio capture: ScreenRecorderLib built-in WASAPI system loopback and microphone sources, mixed into the MP4
- Video encoding: ScreenRecorderLib real-time H.264 via Media Foundation (hardware encoding preferred); MP4 is written directly, MKV / MOV are remuxed, WebM / GIF are converted by FFmpeg
- Mouse capture: ScreenRecorderLib captures the cursor natively and detects clicks to draw highlight effects
- Recording border: a topmost, click-through border window that tracks the real capture rect and is excluded via `WDA_EXCLUDEFROMCAPTURE`
- Volume feedback and gain: peak levels are computed from ScreenRecorderLib audio packet data; microphone volume is applied through the audio source volume
- Pause/resume: native ScreenRecorderLib pause/resume; paused time is not included and audio/video stay in sync
- The control bar is excluded from the recording via `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`

## Known limitations

- Headless CLI mode shows the red recording border, but not the control bar or volume cells
- Uses ScreenRecorderLib capture, so exclusive full-screen / hardware overlay content (some games) may appear black
- WebM requires re-encoding the video to VP9, which is noticeably slower than other formats
- If the selected audio source is completely silent during recording (especially system audio), the result may have no audio track
- Microphone and system audio are mixed into a single 48 kHz stereo AAC track

## Version

1.5.4
