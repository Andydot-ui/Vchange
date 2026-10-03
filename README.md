<!--markdownlint-disable MD033 MD041-->

<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/logo-dark-theme.png">
  <img src="docs/images/logo-light-theme.png" width="72" height="72" alt="Vchange Logo">
</picture>

# Vchange

**简洁美观的 Windows 视频转换器 · WPF + ffmpeg**

支持格式 / 编码 / 分辨率 / 帧率 / 码率的可视化转换，单文件免安装，开箱即用。

[![最新版本](https://img.shields.io/github/v/release/Andydot-ui/Vchange?style=flat-square&color=%233fb950&label=%E6%9C%80%E6%96%B0%E7%89%88%E6%9C%AC)](https://github.com/Andydot-ui/Vchange/releases/latest)
[![下载量](https://img.shields.io/github/downloads/Andydot-ui/Vchange/total?style=flat-square&color=%230a84ff&label=%E4%B8%8B%E8%BD%BD%E9%87%8F)](https://github.com/Andydot-ui/Vchange/releases)
[![Stars](https://img.shields.io/github/stars/Andydot-ui/Vchange?style=flat-square&label=Stars)](https://github.com/Andydot-ui/Vchange)
[![仓库大小](https://img.shields.io/github/repo-size/Andydot-ui/Vchange?style=flat-square&color=3cb371)](https://github.com/Andydot-ui/Vchange)
<br/>
![.NET](https://img.shields.io/badge/.NET-6-512bd4?style=flat-square)
[![许可证](https://img.shields.io/github/license/Andydot-ui/Vchange?style=flat-square)](LICENSE)
[![Issues](https://img.shields.io/github/issues/Andydot-ui/Vchange?style=flat-square&color=%233fb950&label=Issues)](https://github.com/Andydot-ui/Vchange/issues)

[**⬇ 立即下载**](https://github.com/Andydot-ui/Vchange/releases/latest) | [**🐛 反馈问题**](https://github.com/Andydot-ui/Vchange/issues)

</div>

## ✨ 功能

### 向导式转换流程

- [x] 分步操作：**选择文件 → 输出格式 → 视频参数 → 码率 → 转换**
- [x] 清晰的步骤指示器，可随时返回上一步修改
- [x] 流畅的页面切换动画

### 灵活的转换参数

- [x] 支持 20 种输出格式（mp4 / mkv / avi / mov / webm …）
- [x] 视频编码、分辨率、帧率可**分别选择与原视频相同**
- [x] 码率根据分辨率**自动推荐**，也可手动调整
- [x] 分辨率支持常见预设与自定义（宽 × 高）

### 现代化的界面

- [x] 深色 / 浅色主题**跟随系统自动切换**
- [x] Windows 11 亚克力（Acrylic）毛玻璃窗口
- [x] 自绘无边框窗口、圆角卡片、拨动开关
- [x] 实时进度条（百分比 + 转换速度）
- [x] 转换日志实时显示 ffmpeg 原始输出
- [x] 记住窗口位置，下次在原位打开

### 开箱即用

- [x] **单文件 exe**，免安装 .NET 运行时
- [x] **内嵌完整版 ffmpeg**，无需单独下载
- [x] 应用图标随系统深浅色自动切换（白 / 黑场记板）

## 📷 界面截图

### 主流程

##### 1. 选择视频文件

![选择文件](docs/screenshots/1-file.png)

##### 2. 选择输出格式

![输出格式](docs/screenshots/2-format.png)

##### 3. 设置视频参数

![视频参数](docs/screenshots/3-params.png)

##### 4. 设置码率

![码率](docs/screenshots/4-bitrate.png)

##### 5. 转换设置摘要

![转换摘要](docs/screenshots/5-summary.png)

##### 6. 转换完成（进度与日志）

![转换完成](docs/screenshots/6-done.png)

## 🚀 开始使用

**系统要求**：Windows 10 / 11（64 位）

1. 打开 [**Releases 页面**](https://github.com/Andydot-ui/Vchange/releases/latest)
2. 下载并运行 `Vchange.exe`
3. 按向导选择视频、设置参数，点击「开始转换」即可

> [!NOTE]
> 首次转换时会自动释放内嵌的 ffmpeg（约需几秒），之后复用不再重复释放。
> 无需安装 .NET 运行时，也无需单独配置 ffmpeg。

### 下载文件说明

| 文件 | 用途 |
| --- | --- |
| `Vchange.exe` | **主程序**（推荐）——单文件绿色版，内置 ffmpeg 与 .NET 运行时，下载后双击直接用 |
| `ffmpeg-9.0.2-full.exe` | 完整版 ffmpeg 命令行工具，普通用户无需下载；适合需要单独使用 ffmpeg 或替换内嵌版本的进阶用户 |

## 🔧 从源码构建

1. 安装 Visual Studio 2022 / .NET 6 SDK
2. 将完整版 `ffmpeg.exe` 放入 `Resources\` 目录（仅 Release 打包时内嵌）
3. 执行：

```bash
dotnet publish Vchange.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true
```

4. 产物位于 `bin\Release\net6.0-windows\win-x64\publish\Vchange.exe`

## 📁 项目结构

```
Vchange/
├─ Vchange.csproj                 # 项目文件（.NET 6.0-windows + WPF）
├─ App.xaml / App.xaml.cs         # 应用入口与全局样式（主题资源）
├─ MainWindow.xaml / .xaml.cs     # 主界面与业务逻辑
├─ MessageDialog.xaml / .xaml.cs  # 自定义提示对话框
├─ FfmpegProvider.cs              # ffmpeg 定位（外部 / PATH / 内嵌释放）
├─ ThemeManager.cs                # 深浅色主题管理与切换
├─ Vchange.slnx                   # 解决方案文件
├─ Resources/
│   ├─ app_white.ico              # 深色主题图标
│   └─ app_dark.ico               # 浅色主题图标
├─ tools/
│   ├─ make-icon.ps1              # 应用图标生成脚本
│   └─ take-screenshots.ps1       # 界面截图自动化脚本
└─ docs/                          # 文档与截图资源
```

## ❓ 常见问题

<details>
<summary><b>转换失败怎么办？</b></summary>

查看「转换日志」中 ffmpeg 的原始输出，通常会明确指出失败原因（如源文件损坏、编码器不支持等）。
</details>

<details>
<summary><b>为什么第一次转换会慢一点？</b></summary>

首次转换需要把内嵌的 ffmpeg 释放到 `%LocalAppData%\Vchange\ffmpeg.exe`（约 217MB），只需一次，之后立即可用。
</details>

<details>
<summary><b>支持硬件加速吗？</b></summary>

支持。视频编码下拉框中可选择 `h264_nvenc`（N 卡）、`h264_qsv`（Intel 核显）、`h264_amf`（A 卡）等硬件编码器。
</details>

## 🙋 反馈与支持

遇到问题或有新想法？欢迎反馈（提交时请按模板填写，信息越完整解决越快）：

- [🐛 提交 Bug 反馈](https://github.com/Andydot-ui/Vchange/issues/new?template=BugReport.yml)
- [💡 提交功能建议](https://github.com/Andydot-ui/Vchange/issues/new?template=FeatureRequest.yml)
- [📋 查看已有 Issues](https://github.com/Andydot-ui/Vchange/issues)
- 📮 邮箱联系：**andydot@qq.com**

## ⭐ Star History

<a href="https://www.star-history.com/?repos=andydot-ui%2Fvchange&type=date&releases=&legend=bottom-right">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=andydot-ui/vchange&type=date&theme=dark&legend=bottom-right" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=andydot-ui/vchange&type=date&legend=bottom-right" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=andydot-ui/vchange&type=date&legend=bottom-right" />
 </picture>
</a>

## 📄 许可证

本项目基于 [MIT License](LICENSE) 开源。

<div align="center">

如果这个项目对你有帮助，欢迎点一个 ⭐ Star！

</div>
