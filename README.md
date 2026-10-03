# 视频转换器 (WPF)

此项目是一个使用 **WPF** 构建的简易 GUI 程序，利用 **ffmpeg** 完成视频的 **格式、码率、分辨率、帧率** 转换。

## 功能
- 选择源视频文件。
- 设定目标格式（mp4、avi、mkv、mov）。
- 输入目标码率（kbps）。
- 选择或自定义分辨率（如 `1280x720`）。
- 输入目标帧率（fps）。
- 通过 `ffmpeg` 执行转换，实时在日志框中显示 ffmpeg 输出。

## 环境要求
- Windows 10/11（已安装 Visual Studio 2022 以上）。
- 已安装 **ffmpeg** 并确保 `ffmpeg.exe` 在系统 `PATH` 中，或者将 `ffmpeg.exe` 复制到生成的可执行文件所在目录。

## 编译运行步骤
1. 打开 `VideoConverter` 文件夹（`C:\Users\andyd\Documents\shipin\VideoConverter`）
   - 在 Visual Studio 中选择 **“打开文件夹”**，或使用 **`File -> Open -> Project/Solution`** 打开 `VideoConverter.csproj`。
2. 若系统未自动恢复 NuGet 包，右键项目 → **“恢复 NuGet 包”**（本项目仅使用默认的 WPF 包）。
3. 生成（`Ctrl+Shift+B`）并运行（`F5`）。
4. 使用界面选择输入视频、设置参数后点击 **开始转换**。

## 常见问题
- **ffmpeg 未找到**：请确认 `ffmpeg.exe` 已加入系统 `PATH`，或将其复制到 `bin\Debug\net6.0-windows`（或 `Release`）目录下。
- **转换失败**：检查日志窗口中的错误信息，确保输入文件路径、参数格式正确。

## 项目结构
```
VideoConverter/
├─ VideoConverter.csproj          # 项目文件，使用 .NET 6.0‑windows + WPF
├─ App.xaml / App.xaml.cs        # 应用入口
├─ MainWindow.xaml / MainWindow.xaml.cs  # 主界面与业务逻辑
└─ README.md                     # 本说明文件
```

祝您使用愉快！