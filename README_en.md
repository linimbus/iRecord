# iRecord 1.3.2

English | [简体中文](README.md)

A minimal native screen recorder for Windows. It runs in the system tray, lets you start / pause / stop recording with global hotkeys, supports **MP4 / MKV / MOV / WebM / GIF** output, can capture the mouse pointer and click effects into the video, and encodes with open-source [**FFmpeg**](https://ffmpeg.org/).

## Features

- Lives in the system tray, single instance, small footprint
- Headless CLI: `iRecord-cli.exe` records without any UI, designed for scripts and LLM tool calls
- Three capture scopes:
  - Full screen (the monitor under the mouse)
  - Region (drag to select)
  - Window (hover to highlight, click to select)
- Screenshots: region screenshots (select multiple regions and merge them into one image), window screenshots, and scrolling window screenshots that auto-scroll and stitch seamlessly into one PNG; all are saved to disk and copied to the clipboard automatically
- The control panel groups the pin, screenshot and recording actions, separated by vertical dividers
- The control panel has an always-on-top (pin) button so it stays above other windows
- Output formats: `MP4` (H.264), `MKV` (H.264), `MOV` (H.264), `WebM` (VP9 + Opus) or `GIF`
- Mouse capture: hidden / show pointer / show pointer with highlighted left, right and middle clicks
- Audio capture: no audio / system audio / microphone / system + microphone
- Global hotkeys: start, stop, pause/resume (default `F9` / `F10` / `F11`); show-panel and region / window / scrolling screenshot hotkeys are configurable in Settings
- On-screen recording bar showing elapsed time with pause and stop buttons; the bar is excluded from the recording
- Recording border: a red frame marks the actual capture area in tray mode, follows window movement, and is excluded from the recording
- Volume feedback: the control bar shows live system and microphone levels; while recording the mic you can click or drag the volume cells to adjust gain (0–200%)
- Pause/resume removes the paused period from both video and audio, keeping them in sync
- Settings UI available in Simplified Chinese, Traditional Chinese, English, Japanese, Korean, German, French and Spanish
- Configurable frame rate, GIF frame rate, quality, system / microphone level gain, mouse capture mode, output folder, FFmpeg path, hotkeys and default start mode; microphone volume can be adjusted while recording
- Version `1.3.2`

## Requirements

- Windows 10 2004 or later (Windows 11 recommended)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (framework-dependent deployment)
- FFmpeg executable (`ffmpeg.exe`, official site: <https://ffmpeg.org/>)

## FFmpeg Setup

FFmpeg is located in the following order:

1. `ffmpeg.exe` specified manually in Settings
2. `ffmpeg.exe` next to the application
3. `D:\software\ffmpeg-8.1.1\bin\ffmpeg.exe`
4. `ffmpeg.exe` on the system `PATH`

If it is not found on first launch, a tray notification appears. Set the path under "Settings → FFmpeg path" or click "Auto-detect".

## Usage

1. Run `iRecord.exe`; a foreground control panel appears: an always-on-top pin button on the far left, then screenshots (region / window / scrolling long screenshot), recording (full screen / region / window), plus settings and open-folder. Region screenshots let you select multiple regions and press Enter to merge them vertically into one image. The app also stays in the system tray (bottom right); closing the panel hides it to the tray, and double-clicking the tray icon reopens it. Both screenshots and recordings are saved to the configured output folder.
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
4. While recording, a control bar appears at the bottom center of the desktop; click "Pause" or "Stop" directly. A red border marks the capture area. When audio capture is enabled, the bar shows system / microphone levels, and in microphone mode you can click or drag the volume cells to adjust the volume.
5. Encoding starts automatically after stopping; a tray notification then shows where the file was saved. The default output folder is `Videos\iRecord`.

## Build

Requires the .NET 10 SDK.

```powershell
dotnet restore
dotnet build -c Release
```

## Publish the executable

The script `tools\build.ps1` performs a **framework-dependent single-file** publish:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build.ps1
```

Output folder: `publish\`, containing `iRecord.exe` (tray app) and `iRecord-cli.exe` (headless CLI). The target machine needs the .NET 10 Desktop Runtime.

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

- Screen capture: Windows GDI (`Graphics.CopyFromScreen`)
- Audio capture: Windows WASAPI (system loopback / microphone, via NAudio)
- Video encoding: FFmpeg `libx264` with VFR timestamps for accurate duration at high resolutions
- Mouse capture: the pointer is drawn into each frame at its screen position, and button state is polled to draw click ripples (MP4/MKV/MOV are muxed directly; WebM is transcoded to VP9)
- Recording border: a topmost, click-through border window that tracks the real capture rect and is excluded via `WDA_EXCLUDEFROMCAPTURE`
- Volume feedback and gain: peak levels are computed from WASAPI callback data; microphone gain is applied in software before writing the WAV
- Pause/resume: video is recorded in segments and joined losslessly on stop; paused time is not included
- The control bar is excluded from the recording via `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`

## Known limitations

- Headless CLI mode does not show the red border, control bar or volume cells
- Uses GDI capture, so exclusive full-screen / hardware overlay content (some games) may appear black
- WebM requires re-encoding the video to VP9, which is noticeably slower than other formats
- If the selected audio source is completely silent during recording (especially system audio), the result may have no audio track
- Microphone and system audio are mixed into a single 48 kHz stereo AAC track

## Version

1.3.2
