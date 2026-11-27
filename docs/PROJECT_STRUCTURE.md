# iRecord 项目结构与增量开发指南

> 当前版本：`1.5.4`（本文档不单独改版本号）
>
> 阅读目标：让后续 AI/开发者在不重新摸索整个仓库的情况下，快速定位代码、理解数据流，并安全地做增量修改。

---

## 1. 项目定位与技术栈

iRecord 是一个 Windows 常驻托盘录屏/截图工具，同时提供无头 CLI。

核心能力：

- 全屏录制：单屏直接开始；多屏显示红色选择框，鼠标移动切换显示器，点击后开始。
- 区域录制：拖动框选一个屏幕矩形。
- 窗口录制：鼠标悬停选择窗口，录制窗口内容并跟随窗口移动。
- 音频：系统声音 / 麦克风 / 两者混音；暂停/恢复时音画同步。
- 输出：MP4（原生）、MKV / MOV（FFmpeg 流复制）、WebM / GIF（FFmpeg 转码）。
- 截图：单区域、多区域合并、窗口、滚动长截图；保存 PNG、复制剪贴板、打开标注编辑器。
- 图片编辑：形状、实心、文字、编号、马赛克、自由绘制、缩放平移、尺寸调整、OCR、系统画图。
- 托盘、全局快捷键、多语言、无头 CLI、自动打包。

技术栈：

| 类别 | 方案 |
| --- | --- |
| 运行时 | .NET 10 `net10.0-windows10.0.19041.0`，WinForms，x64 |
| 界面 | WinForms + 自绘控件 |
| 录制 | ScreenRecorderLib 7.0.1（Windows Graphics Capture / Desktop Duplication + Media Foundation 编码） |
| 转码 | FFmpeg（仅非 MP4 输出；MP4 原生阶段不需要 FFmpeg） |
| OCR | Windows.Media.Ocr（WinRT） |
| 配置 | `%APPDATA%\iRecord\settings.json` |
| 图标 | `Assets/app.ico` |

重要部署约束：

- 仅 x64；`ScreenRecorderLib.dll` 不嵌入单文件，必须与 exe 同目录。
- 目标机器需要 .NET 10 Desktop Runtime、VC++ 2015–2022 Redistributable (x64)。
- MP4 输出不需要 FFmpeg；MKV/MOV/WebM/GIF 需要。

---

## 2. 快速开始

```powershell
dotnet restore
dotnet build -c Release
```

运行托盘程序：

```powershell
dotnet run --project .\iRecord.csproj
# 或
.\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\iRecord.exe
```

运行无头 CLI（构建后）：

```powershell
.\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\iRecord-cli.exe help
```

发布并打包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\package.ps1
```

产物：`publish\iRecord-v1.5.4-win-x64.zip`（内含 exe、`ScreenRecorderLib.dll`、README、docs、skills）。

---

## 3. 目录树

```text
iRecord/
├─ Program.cs                     # 主入口：CLI 参数或单实例托盘应用
├─ NativeMethods.cs               # Win32 P/Invoke 常量/结构/导入
├─ iRecord.csproj                 # x64；ScreenRecorderLib 包；单文件外置 SRL
├─ app.manifest                   # Windows 兼容性/DPI manifest
├─ build.bat                      # package.ps1 的双击入口
├─ README.md / README_en.md       # 用户文档（功能、使用、发布）
│
├─ App/                           # 应用层：配置、托盘、热键、本地化、OCR
│  ├─ AppSettings.cs
│  ├─ SettingsStore.cs
│  ├─ Localization.cs
│  ├─ Hotkeys.cs
│  ├─ TrayApplicationContext.cs
│  └─ WindowsOcr.cs
│
├─ Capture/                       # 捕获层：录制、截图、选择器、FFmpeg
│  ├─ RecordingSession.cs
│  ├─ ScreenshotService.cs
│  ├─ FfmpegTools.cs
│  ├─ RegionSelectorForm.cs
│  ├─ MultiRegionSelectorForm.cs
│  ├─ WindowPickerForm.cs
│  └─ FullscreenScreenSelectorForm.cs
│
├─ Cli/                           # 无头命令实现
│  └─ CliRunner.cs
│
├─ UI/                            # WinForms 界面层
│  ├─ MainPanelForm.cs
│  ├─ ControlBarForm.cs
│  ├─ SettingsForm.cs
│  ├─ ImageEditorForm.cs
│  ├─ ImageResizeForm.cs
│  ├─ OcrResultForm.cs
│  ├─ AboutForm.cs
│  ├─ HotkeyBox.cs
│  ├─ LedTimeDisplay.cs
│  ├─ RecordingBorderForm.cs
│  └─ Dpi.cs
│
├─ Assets/
│  └─ app.ico
│
├─ docs/
│  ├─ PROJECT_STRUCTURE.md        # 本文档
│  └─ images/                     # README 用截图（本文档不使用图片）
│
├─ skills/
│  └─ irecord-headless/SKILL.md   # 无头 CLI 的 AI 使用说明
│
├─ tools/
│  ├─ build.ps1                   # 发布脚本
│  ├─ package.ps1                 # 发布 + zip
│  ├─ package.cmd                 # 双击入口
│  ├─ make-icon.ps1               # 重新生成图标
│  └─ iRecord.Cli/
│     ├─ iRecord.Cli.csproj
│     └─ Program.cs               # 控制台启动器
│
├─ bin/ obj/ publish/             # 构建/发布产物（Git 忽略）
└─ .gitignore
```

---

## 4. 启动与进程模型

### 4.1 入口选择

`Program.cs`：

1. 调用 `ApplicationConfiguration.Initialize()`。
2. 如果存在命令行参数 → `iRecord.Cli.CliRunner.Run(args)`，进入无头模式。
3. 否则创建命名互斥量 `iRecord.SingleInstance.v1`；重复启动直接退出。
4. 启动 `TrayApplicationContext`，进入托盘常驻模式。

`tools/iRecord.Cli/Program.cs` 是独立控制台入口：先启用 PerMonitorV2 DPI 与 WinForms 视觉样式，再调用同一个 `CliRunner`。这样无头录制也能显示红色边框。

### 4.2 线程模型

- UI 主线程：托盘、菜单、面板、选择器、控制条、编辑器。
- 录制：ScreenRecorderLib 原生线程 + 事件回调；`RecordingSession` 在回调中更新状态/电平。
- 无头边框：独立 STA 线程 + WinForms 消息循环（`CliRunner.RecordingBorderHost`）。
- 编码：`RecordingSession.FinishEncodingAsync` 在后台 Task 中运行 FFmpeg/移动文件，完成后触发事件。
- 托盘事件通过 `TrayApplicationContext.Post` 回到 UI 线程。

---

## 5. 核心业务流程

### 5.1 托盘与主面板

`TrayApplicationContext` 是应用总协调器：

- 维护托盘图标、菜单、热键、录制会话、控制条、边框、设置窗口、编辑器列表。
- 托盘图标：白色圆环常驻；录制时中心红色 1 秒亮/暗闪烁；暂停时停止闪烁。
- 主面板 `MainPanelForm` 只负责显示和发出意图事件，实际动作由 Tray 执行。
- 设置修改后重新注册热键、重建菜单，并更新新窗口的配置。

### 5.2 GUI 录制流程

入口：面板按钮 / 托盘菜单 / 全局热键 → `StartRecording(mode)`。

1. `StartRecording` 增加启动防重入锁。
2. 检查当前格式是否需要 FFmpeg。
3. 解析目标：
   - 全屏：单屏直接使用鼠标所在显示器；多屏弹出 `FullscreenScreenSelectorForm`，红框跟随鼠标，左键确认。
   - 区域：`RegionSelectorForm`。
   - 窗口：`WindowPickerForm` 返回 HWND + 屏幕矩形。
4. 如果面板可见，隐藏面板，避免出现在录制/选择截图中。
5. 创建 `RecordingSession`，`Start()` 后创建 `ControlBarForm` 和 `RecordingBorderForm`。
6. 暂停/继续：Tray → `session.TogglePause()` → ScreenRecorderLib 原生 Pause/Resume。
7. 停止：Tray 关闭控制条/边框 → `session.Stop()` → 后台 finalize/转码 → `Finished`/`Failed` 事件。
8. Tray 更新托盘图标、菜单、气泡通知，并按需恢复主面板。

### 5.3 无头录制流程

`iRecord-cli.exe record ...` → `CliRunner.Record()`：

1. 解析参数并覆盖本次运行的设置副本（不写回 settings.json）。
2. 校验目标、FFmpeg（仅需要时）。
3. 启动 `RecordingBorderHost`：独立 STA 线程显示红色边框。
4. 启动 `RecordingSession`。
5. 等待 `Started` 事件（实际首帧）后再启动 `--duration` 计时器；避免录制启动慢导致时长偏短。
6. 停止后立即关闭边框，等待编码完成。
7. 输出人类可读结果或单个 JSON 对象；stdout/stderr 严格分离。

支持的 CLI 命令：

| 命令 | 作用 |
| --- | --- |
| `help` | 完整帮助（含 JSON/LLM 使用建议） |
| `version` | 打印程序集版本 |
| `list-screens` | JSON 列出显示器 |
| `list-windows` | JSON 列出可录制窗口 |
| `record` | 录制并阻塞到编码完成 |

常用 `record` 选项：`--mode fullscreen|region|window`、`--screen`、`--region x,y,w,h`、`--window id|title`、`--duration`、`--delay`、`--fps`、`--format`、`--quality`、`--audio`、`--mouse`、`--output`、`--json`、`--result-file`、`--quiet`。

### 5.4 截图与长截图

`ScreenshotService` 提供底层能力，`TrayApplicationContext` 负责流程：

- 单区域：选择器 → `CaptureRect` → 保存 PNG + 剪贴板 → 编辑器。
- 多区域：`MultiRegionSelectorForm` 收集多个矩形 → `MergeRegions` 纵向合并。
- 窗口：`PrintWindow(PW_RENDERFULLCONTENT)`；空白时回退为屏幕复制。
- 滚动长截图：
  - 激活窗口、将鼠标放到客户区中心。
  - 反复滚轮并截取客户区。
  - 用行签名找重叠、检测固定底部、裁剪新内容，直到到底/达到上限。
  - 完成后恢复滚动位置和鼠标位置。

### 5.5 图片编辑器

`ImageEditorForm` 是最大单体文件，核心模型：

- `_baseImage`：原始底图。
- `_elements`：所有可编辑元素（Shape/Text/Number/Mosaic）。
- `_undo`：无上限快照（Base 引用 + 元素深拷贝 + 下一个编号）。
- `_allBases`：保留历次底图，供快照共享，避免反复克隆大图。
- 工具：矩形/正方形/圆/椭圆/星形/心形/箭头/虚线箭头/自由绘制/马赛克/文字/编号/移动/平移。
- 文字：内联 TextBox + `InlineTextOverlay` 实时预览；字体/字号/样式。
- 编号：3 倍超采样绘制圆形编号，消除锯齿；双击编号工具重置；按钮显示下一个编号。
- 移动：移动工具可选择/拖动所有元素，Del 删除。
- 尺寸调整：`ImageResizeForm` 收集尺寸，`ApplyResize` 重绘底图并缩放元素。
- OCR：`WindowsOcr` → `OcrResultForm`。
- 保存/复制/打开画图/撤销（无上限）。

### 5.6 设置、热键与本地化

- `AppSettings`：所有持久化字段（见第 7 节）。
- `SettingsStore`：加载/保存/规范化；无效值回退默认。
- `HotkeyParser`：文本 ↔ Win32 修饰符/虚拟键。
- `HotkeyWindow`：隐藏 NativeWindow 注册全局热键，WM_HOTKEY → action 字符串。
- `HotkeyBox`：设置界面捕获按键。
- `Localization`：8 种语言字典；新增键必须同步所有语言，英文兜底。

---

## 6. 源文件详解

### 6.1 根目录

#### `Program.cs`

- 职责：主进程入口。
- 关键逻辑：参数 → CLI；无参数 → 命名互斥量 + `TrayApplicationContext`。
- 修改注意：托盘单实例语义在这里；不要在 UI 层重复实现。

#### `NativeMethods.cs`

- 职责：集中 Win32 P/Invoke。
- 包含：热键、窗口枚举/矩形、控制台、`PrintWindow`、鼠标滚轮、DPI/显示器亲和性、DWM cloak。
- 修改注意：只放无法用托管 API 替代的声明；新增结构体注意 `StructLayout`。

### 6.2 `App/`

#### `App/AppSettings.cs`

- 职责：JSON 配置模型。
- 字段：格式、FPS/GIF FPS、画质、音频、鼠标、麦克风音量/电平增益、面板/控制条透明度、输出目录、FFmpeg、启动模式、各类快捷键。
- 修改注意：新增设置需同步 `SettingsStore.Normalize` 与 `SettingsForm`。

#### `App/SettingsStore.cs`

- 职责：`%APPDATA%\iRecord\settings.json` 加载/保存/修复。
- 修改注意：默认值、范围 clamp、路径/FFmpeg 回退都在 `Normalize`。

#### `App/Localization.cs`

- 职责：全部 UI 文本、8 语言字典、语言检测、格式化。
- 修改注意：新 key 要加进每一门语言；UI 禁止硬编码用户可见文本。

#### `App/Hotkeys.cs`

- 职责：`Hotkey` 记录、`HotkeyParser`、`HotkeyWindow`。
- 修改注意：Win32 虚拟键表与显示名需同步；注册失败由 Tray 汇总提示。

#### `App/TrayApplicationContext.cs`

- 职责：总协调器/托盘生命周期。
- 关键流程：菜单重建、热键注册、录制启停暂停、截图流程、编辑器生命周期、通知、退出清理。
- 修改注意：所有工作线程事件必须经过 `Post` 回到 UI 线程；新增录制入口要经过 `StartRecording` 防重入。

#### `App/WindowsOcr.cs`

- 职责：GDI+ Bitmap → WinRT SoftwareBitmap → Windows OCR。
- 修改注意：依赖已安装 OCR 语言包；无语言包时抛出明确异常，由编辑器提示。

### 6.3 `Capture/`

#### `Capture/RecordingSession.cs`

- 职责：一次录制的完整生命周期。
- 主要成员：
  - `CaptureMode`：FullScreen / Region / Window。
  - `Start/Stop/TogglePause/Dispose`：生命周期。
  - `BuildOptions/CreateRecordingSource/ConfigureAudio`：源、音频、鼠标、编码参数。
  - `OnFrameRecorded/Started`：实际首帧计时。
  - `OnAudioPacketRecorded`：SYS/MIC 电平。
  - `FinishEncodingAsync/EncodeAsync`：MP4 移动、MKV/MOV remux、WebM/GIF 转码。
- 修改注意：
  - 区域在单一显示器内通过 `SourceRect` 裁剪；跨显示器目前按主要显示器裁剪。
  - 窗口模式由后台 watcher 更新 `CurrentCaptureRect`。
  - 暂停使用原生能力，不要再按段拼接。
  - 临时目录必须最终清理。

#### `Capture/ScreenshotService.cs`

- 职责：矩形/窗口截图、多区域合并、滚动长截图拼接。
- 关键算法：`PrintWindow` + 空白回退；行签名/滚动偏移/底部固定检测；`ArrayPool` 减少大图分配。
- 修改注意：滚动长截图会移动鼠标/滚轮，必须恢复原状态；不要在主线程执行长循环。

#### `Capture/FfmpegTools.cs`

- 职责：FFmpeg 查找与进程执行。
- 修改注意：MP4 不依赖 FFmpeg；其他格式必须确保路径可用。

#### `Capture/RegionSelectorForm.cs`

- 职责：单矩形框选截图/录制区域。
- 关键逻辑：虚拟屏快照、暗化、尺寸提示、屏幕坐标转换、ESC/右键取消。

#### `Capture/MultiRegionSelectorForm.cs`

- 职责：多区域截图选择器。
- 关键逻辑：多个编号矩形、Backspace/Delete 撤销、Enter/Space 确认、`MergeRegions`。

#### `Capture/WindowPickerForm.cs`

- 职责：窗口拾取。
- 关键逻辑：`WindowHit`、`FindAt`（过滤自身进程/tool/最小化/cloaked/无标题窗口）、悬停高亮、点击返回 HWND。
- 修改注意：窗口标题可能被截断；不要拾取 iRecord 自身窗口。

#### `Capture/FullscreenScreenSelectorForm.cs`

- 职责：多显示器全屏录制选择器。
- 关键逻辑：虚拟屏快照、暗化非目标屏、红框跟随鼠标、点击确认、ESC/右键取消。
- 修改注意：仅在 `Screen.AllScreens.Length > 1` 时由 Tray 使用。

### 6.4 `Cli/`

#### `Cli/CliRunner.cs`

- 职责：无头命令解析、录制、窗口/屏幕枚举、JSON 输出、帮助文本、无头红框宿主。
- 关键类型：
  - `Options`：轻量 flag/value 解析。
  - `RecordingBorderHost`：独立 STA 消息循环显示红框。
  - `Record`：目标解析 → 边框 → 会话 → 实际开始计时 → 停止/编码。
  - `ResolveTarget`：fullscreen/region/window → `RecordingSession` 参数。
  - `FindWindow`：按 HWND 或标题/进程子串匹配。
- 修改注意：`--json` 时 stdout 只能有 JSON；进度/错误必须写 stderr；退出码 0/1/2 语义固定。

### 6.5 `UI/`

#### `UI/MainPanelForm.cs`

- 职责：暗色无边框主面板。
- 关键点：自绘按钮/图标、分组分隔线、拖动、图钉置顶、悬停透明度、语言刷新、录制态禁用。
- 事件：截图/全屏/区域/窗口/设置/打开目录。
- 修改注意：面板只发意图事件，不直接启动录制。

#### `UI/ControlBarForm.cs`

- 职责：录制控制条。
- 关键点：LED 时间、五格 SYS/MIC 电平、暂停/停止/隐藏、拖动、半透明、闪烁指示、排除出录制。
- 修改注意：通过 provider 读取 session 状态，不持有 session。

#### `UI/SettingsForm.cs`

- 职责：设置对话框。
- 关键点：克隆设置、两列布局、语言/格式/画质/FPS/音频/鼠标/快捷键/透明度/输出/FFmpeg、格式相关启用状态、热键校验。
- 修改注意：取消不能修改原设置；新增项要更新 `Populate` 和 `Save`。

#### `UI/ImageEditorForm.cs`

- 职责：截图标注编辑器（最大文件）。
- 关键模型：`EditorElement`、`EditorSnapshot`、`EditorCanvas`、`InlineTextOverlay`、`EditorToolbarPanel`、`IconToolButton`。
- 关键流程：工具选择、画布坐标映射、缩放/平移、内联文字、移动/选择/删除、编号复用、马赛克、撤销、保存/复制、尺寸调整、OCR、画图。
- 修改注意：
  - 新元素类型必须实现渲染、命中测试、偏移/缩放、快照克隆。
  - 快照不要克隆 Base 位图；只复制元素和引用。
  - 所有固定尺寸需经 `Dpi.S`/`Dpi.R`。

#### `UI/ImageResizeForm.cs`

- 职责：尺寸调整对话框。
- 关键点：宽/高/百分比联动、保持比例、像素总量上限（20,000 边长 / 80,000,000 像素）。

#### `UI/OcrResultForm.cs`

- 职责：OCR 结果只读窗口，复制/关闭，空结果提示。

#### `UI/AboutForm.cs`

- 职责：关于窗口，展示本地化版本/功能信息和官网按钮。

#### `UI/HotkeyBox.cs`

- 职责：捕获下一组快捷键；Backspace/Delete 清空；Tab 保留焦点导航。

#### `UI/LedTimeDisplay.cs`

- 职责：自绘七段数码时间显示；无字体依赖，自动缩放。

#### `UI/RecordingBorderForm.cs`

- 职责：置顶、点击穿透、透明 key 的红色录制边框。
- 关键点：50ms 轮询 provider 更新 Bounds；`WDA_EXCLUDEFROMCAPTURE` 排除出录制。
- 使用方：托盘录制 + 无头 CLI。

#### `UI/Dpi.cs`

- 职责：缓存系统 DPI 缩放；`S()` / `R()` 供固定像素布局与绘制使用。

### 6.6 `tools/`

#### `tools/iRecord.Cli/Program.cs`

- 职责：独立控制台入口；初始化 DPI/视觉样式；异常映射退出码。

#### `tools/iRecord.Cli/iRecord.Cli.csproj`

- 职责：CLI 项目；引用主项目；同样 x64、外置 ScreenRecorderLib。

#### `tools/build.ps1`

- 职责：框架依赖 + 单文件发布；主程序与 CLI；删除 runtimeconfig/PDB/XML；复制 README/docs。

#### `tools/package.ps1`

- 职责：调用 build，复制 exe + `ScreenRecorderLib.dll` + README + skills + docs 到 stage，生成版本化 zip，清理旧 zip。

#### `tools/make-icon.ps1`

- 职责：重新生成 `Assets/app.ico`（仅图标维护时使用）。

---

## 7. 关键数据与状态

### AppSettings 字段

| 字段 | 作用 |
| --- | --- |
| `Language` | UI 语言代码 |
| `Format` | mp4/mkv/mov/webm/gif |
| `Fps` | 视频帧率 5–60 |
| `GifFps` | GIF 帧率 5–30 |
| `Quality` | low/medium/high |
| `Audio` | none/system/mic/both |
| `MouseMode` | off/cursor/clicks |
| `MicVolume` | 麦克风音量 0–200% |
| `SystemLevelGain` / `MicLevelGain` | 控制条电平视觉增益 1–10 |
| `PanelOpacity` / `RecordingBarOpacity` | 面板/控制条透明度 20–100 |
| `OutputFolder` | 输出目录 |
| `FfmpegPath` | FFmpeg 路径 |
| `StartMode` | 默认启动模式 |
| `Hotkey*` | 全局快捷键 |

### RecordingSession 状态

- `Mode/Width/Height/CurrentCaptureRect`：目标信息。
- `IsPaused/IsEncoding/Elapsed`：控制条/托盘状态。
- `HasSystemAudio/HasMicAudio`：实际可用音频源。
- `SystemAudioLevel/MicAudioLevel`：控制条电平。
- `Started`：实际首帧事件（CLI 计时用）。
- `Finished/Failed/StateChanged`：生命周期通知。
- `LastError`：启动/录制失败原因。

### 重要不变量

1. `Screen.AllScreens.Length > 1` 才显示全屏选择器；单屏直接录制。
2. 红框必须在录制前创建并在 `WDA_EXCLUDEFROMCAPTURE` 后启动，避免进入视频。
3. 区域/窗口/全屏都使用同一个 `RecordingSession`，不要在 UI 层重复实现采集。
4. 暂停使用 ScreenRecorderLib 原生 Pause/Resume，计时同步停止/继续。
5. 临时 MP4 在 `%TEMP%\iRecord\<guid>\recording.mp4`，最终必须清理。
6. `ScreenRecorderLib.dll` 外置，发布/打包脚本必须复制它。
7. 所有固定像素布局/绘制必须乘 DPI。
8. 用户可见字符串必须来自 `Localization`。

---

## 8. 构建、发布与验证

### 构建

```powershell
dotnet build -c Release
```

### 发布

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build.ps1
```

- 输出 `publish\iRecord.exe`、`publish\iRecord-cli.exe`、`publish\ScreenRecorderLib.dll`。
- 发布前如果 `publish\iRecord.exe` 正在运行，会因文件占用失败；先关闭进程。

### 打包

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\package.ps1
```

或双击 `build.bat` / `tools\package.cmd`。

### 推荐验证

1. 构建 0 warning / 0 error。
2. 无头区域录制：`iRecord-cli.exe record --mode region --region ... --duration 2 --format mp4 --audio none --output ... --json`。
3. 检查输出帧率/尺寸/时长；确认红框在录制期间存在、成片内没有红框。
4. 托盘启动冒烟：启动 exe → 保持 3 秒 → 正常退出。
5. 改动录制后端时额外验证 MP4/MKV/MOV/WebM/GIF 与暂停恢复。

---

## 9. AI 增量开发指南

### 常见改动入口

| 需求 | 首选文件 |
| --- | --- |
| 新增设置项 | `AppSettings` → `SettingsStore.Normalize` → `SettingsForm` → 使用方 |
| 新增语言/文案 | `Localization.cs`（所有语言） |
| 新增录制选项 | `RecordingSession.BuildOptions` + `SettingsForm` |
| 新增截图模式 | `TrayApplicationContext` 截图流程 + `ScreenshotService` |
| 新增编辑工具 | `ImageEditorForm` 的 `EditorTool/ElementKind`、绘制、命中、快照、工具栏 |
| 新增 CLI 参数 | `CliRunner.Options` 白名单、帮助文本、`Record`/`ResolveTarget` |
| 新增全局快捷键 | `HotkeyParser` 键表 + `SettingsForm` + `TrayApplicationContext.RegisterHotkeys` |
| 发布/部署变更 | `iRecord.csproj`、`tools/build.ps1`、`tools/package.ps1` |

### 修改原则

- 先读本文档和目标文件顶部注释，再做最小改动。
- 不要轻易重构 `ImageEditorForm` 或 `RecordingSession` 的大结构；优先新增小型方法。
- 不要破坏 headless stdout JSON 契约。
- 不要在主线程做长截图/编码/大图克隆。
- 所有工作线程事件回到 UI 线程再更新控件。
- 修改录制/截图相关代码后必须实际录制一次验证，而不是只看编译。
- 版本号更新属于发布动作，按项目流程单独处理。

---

## 10. 已知限制与注意事项

- 区域录制按包含面积最大的显示器裁剪；跨显示器区域目前不会拼接。
- ScreenRecorderLib 对独占全屏/部分硬件覆盖层游戏可能显示黑屏。
- WebM 需要 VP9 转码，耗时明显高于其他格式。
- 无头 CLI 显示红框，但不显示控制条和音量格。
- 无头/托盘录制都需要已登录且未锁屏的交互式桌面。
- 系统声音/麦克风完全无声时，成片可能没有可听声音；两者最终混为 48 kHz 立体声 AAC。
- `ScreenRecorderLib.dll` 与 VC++ 运行库是硬依赖；缺失时录制启动失败。
- 各语言文件为 UTF-8；批量修改后应检查中文/日文/韩文未损坏。
