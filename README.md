# iRecord 1.0.0

[English](README_en.md) | 简体中文

一个极简的 Windows 原生录屏工具。程序启动后常驻系统托盘，使用全局快捷键即可开始 / 暂停 / 停止录制，支持 **MP4 / MKV / MOV / WebM / GIF** 输出，并可把鼠标指针与点击效果录制进画面，通过开源 **FFmpeg** 完成编码。

## 功能

- 托盘常驻，单实例运行，体积精简
- 三种录制范围：
  - 全屏（鼠标所在显示器）
  - 框选区域（拖动选择）
  - 指定窗口（鼠标悬停高亮，点击选择）
- 输出格式：`MP4`（H.264）、`MKV`（H.264）、`MOV`（H.264）、`WebM`（VP9 + Opus）或 `GIF`
- 鼠标录制：不显示 / 显示鼠标指针 / 显示指针并高亮左右键与中键点击
- 音频录制：无音频 / 系统声音 / 麦克风 / 系统声音 + 麦克风
- 全局快捷键：开始、停止、暂停/恢复（默认 `F9` / `F10` / `F11`）
- 桌面录制控制条：显示时长，提供暂停、停止按钮；控制条不会出现在录制画面中
- 暂停/恢复会将暂停时段从视频与音频中剔除，音画保持同步
- 设置界面支持多语言切换：简体中文、繁體中文、English、日本語、한국어、Deutsch、Français、Español
- 可配置帧率、GIF 帧率、画质、鼠标录制方式、输出目录、FFmpeg 路径、快捷键与默认启动模式
- 版本 `1.0.0`

## 环境要求

- Windows 10 2004 及以上（推荐 Windows 11）
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)（框架依赖发布方式）
- FFmpeg 可执行文件（`ffmpeg.exe`）

## FFmpeg 配置

程序按以下顺序查找 FFmpeg：

1. 设置中手动指定的 `ffmpeg.exe`
2. 程序所在目录下的 `ffmpeg.exe`
3. `D:\software\ffmpeg-8.1.1\bin\ffmpeg.exe`
4. 系统 `PATH` 中的 `ffmpeg.exe`

首次启动若未找到，会在托盘提示，可在「设置 → FFmpeg 路径」中指定或点击「自动检测」。

## 使用

1. 双击 `iRecord.exe`，程序出现在系统托盘（右下角）。
2. 右键托盘图标，选择：
   - 开始录制（全屏 / 区域 / 窗口）
   - 暂停 / 继续
   - 停止
   - 设置
   - 打开输出文件夹
3. 也可以使用全局快捷键：
   - `F9` 开始录制（使用设置中的默认启动模式）
   - `F10` 停止录制
   - `F11` 暂停 / 恢复
4. 录制时桌面底部中央会出现控制条，可直接点击「暂停」「停止」。
5. 停止后自动编码，完成后托盘会提示文件保存位置。默认输出目录为 `视频\iRecord`。

## 构建

需要 .NET 10 SDK。

```powershell
dotnet restore
dotnet build -c Release
```

## 发布可执行程序

已提供脚本 `tools\build.ps1`，采用 **框架依赖 + 单文件** 方式发布：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build.ps1
```

输出目录：`publish\`。目标机器需安装 .NET 10 Desktop Runtime。

## 项目结构

```
Program.cs                     入口
App/                           托盘、设置、本地化、快捷键、配置持久化
Capture/                       屏幕捕获、区域/窗口选择、音频录制、FFmpeg 编码
UI/                            设置窗口、录制控制条、快捷键输入框
Assets/app.ico                 应用图标
tools/build.ps1                发布脚本
tools/make-icon.ps1            重新生成图标
```

## 实现说明

- 屏幕捕获：Windows GDI（`Graphics.CopyFromScreen`）
- 音频捕获：Windows WASAPI（系统回环 / 麦克风，基于 NAudio）
- 视频编码：FFmpeg `libx264`，采用 VFR 时间戳，确保高分辨率下录制时长准确
- 鼠标录制：每帧根据光标位置把指针绘制到画面，并轮询按键状态绘制点击涟漪（MP4/MKV/MOV 直接封装，WebM 转码为 VP9）
- 暂停/恢复：按段录制视频，停止时无损拼接，暂停时段不计入成片
- 控制条通过 `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` 从录制画面中排除

## 已知限制

- 使用 GDI 捕获，独占全屏 / 硬件覆盖层（部分游戏）可能显示为黑屏
- WebM 需要将视频重新编码为 VP9，生成速度明显慢于其他格式
- 若所选音频源在录制期间完全无声（尤其是系统声音），成片可能不包含音频轨
- 麦克风与系统声音会统一混音为 48 kHz 立体声 AAC

## 版本

1.0.0
