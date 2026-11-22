---
name: iRecord Headless Recording
description: Record the Windows desktop, a screen region, or a specific window using iRecord's headless CLI (iRecord-cli.exe). Use when asked to capture the screen, make a demo/tutorial video, record an app window, or produce MP4/MKV/MOV/WebM/GIF output on Windows.
---

# iRecord 无头录屏

使用 `iRecord-cli.exe` 在 Windows 上以无界面方式录制屏幕、区域或指定窗口。命令是同步的：进程会一直等待到停止录制并完成编码，然后返回 JSON 结果和退出码。

## 适用场景

- 用户要求录屏、录制某个窗口/应用、录制屏幕某个区域。
- 需要生成演示视频、教程、Bug 复现录像。
- 需要把桌面画面保存为 MP4 / MKV / MOV / WebM / GIF。
- 自动化流程中需要无人值守地定时录制一小段画面。

## 前置条件

- 当前是已登录且未锁屏的 Windows 交互式桌面。锁屏、其他用户会话、UAC 安全桌面无法录制。
- 已安装 .NET 10 Desktop Runtime。
- 能找到 FFmpeg；缺失时命令会返回错误，提示在 iRecord 设置中配置或把 `ffmpeg.exe` 放在程序目录。

## 定位可执行文件

按以下顺序查找，找到即可使用：

1. 系统 `PATH` 中的 `iRecord-cli.exe`。
2. 仓库/发布目录下的 `publish\iRecord-cli.exe`（相对 iRecord 项目根目录）。
3. 若不存在，先在项目根目录发布：

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\tools\build.ps1
   ```

`iRecord.exe` 也支持完全相同的参数，但它属于 GUI 子系统，部分 Shell 不会等待其结束。自动化场景优先使用 `iRecord-cli.exe`。

不确定用法时先执行：

```powershell
iRecord-cli.exe help
```

## 标准工作流

1. **确认录制目标**
   - 全屏：`list-screens` 获取显示器索引。
   - 指定窗口：`list-windows` 获取窗口 id / 标题 / 进程名。
   - 指定区域：让用户给出 `x,y,w,h`，或结合 `list-screens` 的坐标计算。
2. **执行录制**，使用 `--duration` 指定时长，并加 `--json`。
3. **解析结果**：读取 stdout 的 JSON（或 `--result-file` 指定的文件），检查 `success`、`path`、`sizeBytes`。
4. 如果命令失败（退出码 `1`），把 stderr 中的错误信息反馈给用户，不要臆造文件路径。

常用最小示例：

```powershell
# 录制主显示器 10 秒
iRecord-cli.exe record --duration 10 --format mp4 --audio none --mouse clicks --output "C:\temp" --json

# 录制记事本窗口 8 秒
iRecord-cli.exe record --mode window --window notepad --duration 8 --format mp4 --json

# 录制区域 1280x720，输出 GIF
iRecord-cli.exe record --mode region --region 0,0,1280,720 --duration 5 --format gif --output "C:\temp\demo.gif" --json
```

## 命令参考

### list-screens

输出显示器 JSON 数组：

```json
[
  { "index": 0, "primary": true, "x": 0, "y": 0, "width": 1920, "height": 1080, "device": "\\\\.\\DISPLAY1" }
]
```

将 `index` 传给 `record --screen <index>`。

### list-windows

输出可录制窗口 JSON 数组：

```json
[
  { "id": 12345, "idHex": "0x3039", "pid": 6789, "process": "notepad",
    "title": "Untitled - Notepad", "x": 100, "y": 100, "width": 800, "height": 600 }
]
```

将 `id`（十进制或 `0x` 十六进制）传给 `record --window`，也可以直接传标题或进程名子串（取面积最大的匹配窗口）。

### record

```text
iRecord-cli.exe record [options]
```

| 选项 | 说明 |
| --- | --- |
| `--mode fullscreen\|region\|window` | 捕获范围，默认 `fullscreen` |
| `--screen <index>` | 全屏模式下选择显示器，默认主显示器 |
| `--region <x,y,w,h>` | 区域坐标（物理像素），`mode=region` 时必填 |
| `--window <id\|标题子串>` | 窗口 id 或标题/进程子串，`mode=window` 时必填 |
| `--duration <秒>` | 录制时长；`0` 表示直到 Ctrl+C，默认 10 |
| `--delay <秒>` | 开始录制前等待，默认 0 |
| `--fps <5-60>` | 帧率，默认使用已保存设置（通常 30） |
| `--format mp4\|mkv\|mov\|webm\|gif` | 输出格式，默认使用已保存设置 |
| `--quality low\|medium\|high` | 画质 |
| `--audio none\|system\|mic\|both` | 音频来源 |
| `--mouse off\|cursor\|clicks` | 鼠标指针/点击高亮 |
| `--output <path>` | 输出文件夹；若带媒体扩展名则视为精确文件路径 |
| `--json` | stdout 只输出一个 JSON 对象，进度/错误走 stderr |
| `--result-file <path>` | 额外把 JSON 结果写入文件 |
| `--quiet` | 不输出进度 |

成功时 stdout 的 JSON：

```json
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
```

失败时：

```json
{ "success": false, "command": "record", "error": "..." }
```

### version / help

```powershell
iRecord-cli.exe version
iRecord-cli.exe help
```

## 退出码

| 退出码 | 含义 |
| --- | --- |
| `0` | 成功，文件已生成 |
| `1` | 录制或编码失败 |
| `2` | 命令行参数错误 |

## 参数选择建议

- 面向用户的需求优先录制具体窗口，画面更稳定：先 `list-windows` 找 `id`，再 `--mode window --window <id>`。
- 不确定窗口大小时用 `list-windows` 返回的 `width`/`height` 校验。
- 无声演示用 `--audio none`；需要讲解用 `--audio mic`；需要系统声音用 `--audio system`。
- 需要突出操作过程时用 `--mouse clicks`；只需要指针用 `--mouse cursor`；不需要鼠标用 `--mouse off`。
- GIF 没有音频且体积较大，适合短片段；长视频优先 MP4。
- WebM（VP9 + Opus）编码明显更慢，给工具的超时至少留出录制时长的 5 倍以上。

## 超时与结果可靠性

- `record` 是同步命令，会阻塞到编码结束。调用方超时应设置为 **录制时长 + 至少 120 秒**；WebM 建议更久。
- 如果运行环境无法捕获 stdout，使用 `--result-file <path>`，命令结束后读取该文件。
- 自动化中不要使用 `--duration 0`，它会一直录制直到人工按 Ctrl+C。
- 命令行参数只影响本次运行，不会修改用户保存的设置。

## 重要限制

- 不支持锁屏、其他会话或 UAC 安全桌面。
- 使用 GDI 捕获，独占全屏或硬件覆盖层内容（部分游戏）可能黑屏。
- 同一音频设备不要同时被两个录制进程占用。
- 路径含空格时必须加引号，优先使用绝对路径。

## 故障排查

| 现象 | 处理 |
| --- | --- |
| `FFmpeg was not found` | 在 iRecord 设置中配置 `ffmpeg.exe`，或将其放到程序目录后重试 |
| `Window not found` | 重新运行 `list-windows`，使用窗口 id 或更换标题关键字 |
| 退出码 `2` | 检查参数拼写、取值范围与必填项（`--region` / `--window`） |
| 生成视频黑屏 | 目标可能是独占全屏/受保护内容，改为录制窗口或区域 |
| 输出没有音频轨 | 所选音频源在录制期间完全无声，或设备被占用 |
