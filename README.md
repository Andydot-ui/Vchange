# Vchange 视频转换器 (WPF)

一个使用 **WPF** 构建的 Windows 视频转换工具，底层调用 **ffmpeg** 完成格式、编码、分辨率、帧率、码率转换。

## 功能

- **向导式操作流程**：选择文件 → 输出格式 → 编码/分辨率/帧率 → 码率 → 转换
- 视频编码、分辨率、帧率可分别选择「与原视频相同」
- 码率根据分辨率**自动推荐**，也可手动修改
- 实时进度条（百分比 + 转换速度），转换日志仅显示 ffmpeg 原始输出
- 深色/浅色主题**跟随系统自动切换**（含应用图标）
- Windows 11 亚克力（Acrylic）毛玻璃效果
- 自绘无边框窗口、步骤切换动画、圆角控件、自定义提示对话框
- 记住窗口位置

## 运行环境

- Windows 10 / 11（64 位）
- 直接使用发布版 `Vchange.exe` 时：**无需安装 .NET，无需单独安装 ffmpeg**（已内嵌完整版 ffmpeg，首次转换时自动释放到 `%LocalAppData%\Vchange\ffmpeg.exe`）
- 从源码构建需要：Visual Studio 2022 / .NET 6 SDK

## 使用方法

1. 运行 `Vchange.exe`
2. 选择要转换的视频文件
3. 依次设置输出格式、编码、分辨率、帧率、码率（不需要改的项可勾选"与原视频相同"）
4. 点击「开始转换」，等待进度条完成

## 打包（生成单文件 exe）

1. 将完整版 `ffmpeg.exe` 放到 `Resources\` 目录（仅 Release 发布时内嵌）
2. 执行：

```
dotnet publish Vchange.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

3. 产物在 `bin\Release\net6.0-windows\win-x64\publish\Vchange.exe`

## 项目结构

```
Vchange/
├─ Vchange.csproj                 # 项目文件（.NET 6.0-windows + WPF）
├─ App.xaml / App.xaml.cs         # 应用入口与全局样式（主题资源）
├─ MainWindow.xaml / .xaml.cs     # 主界面与业务逻辑
├─ MessageDialog.xaml / .xaml.cs  # 自定义提示对话框
├─ FfmpegProvider.cs              # ffmpeg 定位（外部/PATH/内嵌释放）
├─ ThemeManager.cs                # 深浅色主题管理与切换
├─ Vchange.slnx                   # 解决方案文件
├─ Resources/
│   ├─ app_white.ico              # 深色主题图标
│   └─ app_dark.ico               # 浅色主题图标
└─ tools/
    └─ make-icon.ps1              # 图标生成脚本
```

## 常见问题

- **转换失败**：查看「转换日志」中 ffmpeg 的原始输出定位原因
- **日志中文乱码**：程序已按 UTF-8 解码 ffmpeg 输出，如仍异常请反馈
- **图标在浅色环境看不清**：图标会跟随系统深浅色自动切换（白/黑场记板）
