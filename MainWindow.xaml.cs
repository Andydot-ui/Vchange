using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using Microsoft.Win32;

using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Collections.Generic;

namespace Vchange
{
    public partial class MainWindow : Window
    {
        private Process? _ffmpegProcess;
        private int _currentStep = 1;

        // 进度解析
        private TimeSpan? _mediaDuration;
        private TimeSpan _currentTime = TimeSpan.Zero;
        private string _currentSpeed = "";
        private static readonly Regex DurationRegex = new(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);
        private static readonly Regex OutTimeRegex = new(@"out_time=(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);

        public MainWindow()
        {
            // 先应用系统主题，再加载界面（避免闪色）
            ThemeManager.Apply(ThemeManager.SystemIsLight());

            InitializeComponent();
            ShowStep(1);
            UpdateControlStates();
            RestoreWindowPosition();

            // 应用与主题相关的图标和视觉
            ApplyThemeIcon();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            // 启动后在调试输出记录材质状态（不写入界面日志）
            Loaded += (s, e) => System.Diagnostics.Debug.WriteLine($"界面材质: {_acrylicStatus}（Windows {Environment.OSVersion.Version}）");
        }

        /// <summary>
        /// 深色环境下用白色图标，浅色环境下用深色图标（窗口/任务栏/标题栏）。
        /// </summary>
        private void ApplyThemeIcon()
        {
            try
            {
                bool light = ThemeManager.IsLight;
                var uri = new Uri(light
                    ? "pack://application:,,,/Resources/app_dark.ico"
                    : "pack://application:,,,/Resources/app_white.ico");

                var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
                Icon = frame;
                if (TitleBarIcon != null)
                    TitleBarIcon.Source = frame;
            }
            catch
            {
                // 忽略：保持 XAML 中设置的默认图标
            }
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ThemeManager.Apply(ThemeManager.SystemIsLight());
                UpdateThemeVisuals();
            }));
        }

        /// <summary>主题切换后刷新与主题相关的图形元素。</summary>
        private void UpdateThemeVisuals()
        {
            UpdateDwmDarkMode();
            ApplyThemeIcon();
            UpdateStepIndicator(_currentStep);
        }

        /// <summary>让系统标题栏/材质跟随深浅色。</summary>
        private void UpdateDwmDarkMode()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                int dark = ThemeManager.IsLight ? 0 : 1;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            }
            catch { }
        }

        #region 亚克力（Acrylic）模糊效果

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            TryEnableAcrylic();
        }

        private string _acrylicStatus = "未尝试";

        /// <summary>
        /// 启用系统亚克力模糊：Win11 用 DWM 系统背景，失败时回退到 AccentPolicy（Win10 1803+）。
        /// 不支持的系统上静默忽略，窗口显示为半透明深色。
        /// </summary>
        private void TryEnableAcrylic()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    _acrylicStatus = "失败(无窗口句柄)";
                    return;
                }

                // 深色模式 + 圆角
                int dark = ThemeManager.IsLight ? 0 : 1;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
                int round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));

                // Win11 22H2+: DWMWA_SYSTEMBACKDROP_TYPE = 3 (Acrylic)
                int backdrop = 3;
                int hr = DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
                if (hr == 0)
                {
                    _acrylicStatus = "系统亚克力(DWM)";
                    return;
                }

                // 回退：Acrylic 模糊（ACCENT_ENABLE_ACRYLICBLURBEHIND）
                var accent = new AccentPolicy
                {
                    AccentState = 4,
                    AccentFlags = 2,
                    GradientColor = unchecked((int)0x8C202020) // 0xAABBGGRR
                };

                int size = Marshal.SizeOf<AccentPolicy>();
                IntPtr ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(accent, ptr, false);
                    var data = new WindowCompositionAttributeData
                    {
                        Attribute = 19, // WCA_ACCENT_POLICY
                        Data = ptr,
                        SizeOfData = size
                    };
                    int result = SetWindowCompositionAttribute(hwnd, ref data);
                    _acrylicStatus = result != 0 ? "亚克力(AccentPolicy)" : "失败(AccentPolicy)";
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            catch (Exception ex)
            {
                _acrylicStatus = "异常: " + ex.Message;
            }
        }

        #endregion

        // 推荐码率映射（分辨率 → 码率 kbps）
        private static readonly Dictionary<string, int> ResolutionBitrateMap = new()
        {
            { "1920x1080", 6000 },
            { "1280x720", 2500 },
            { "854x480", 1500 },
            { "640x360", 800 },
            { "2560x1440", 12000 },
            { "3840x2160", 20000 },
            { "7680x4320", 50000 }
        };
        
        private void SetRecommendedBitrate(string? selected)
        {
            if (string.IsNullOrEmpty(selected)) return;
            if (BitrateTextBox == null) return; // XAML 初始化期间控件可能尚未创建
            if (selected.StartsWith("自定义"))
            {
                // 自定义分辨率在输入后会自动计算，默认不改动当前码率
                return;
            }

            var sizePart = selected.Split(' ')[0]; // e.g., "1920x1080"
            if (ResolutionBitrateMap.TryGetValue(sizePart, out var bitrate))
            {
                BitrateTextBox.Text = bitrate.ToString();
            }
            else
            {
                // 计算一个估算值：宽×高 × 0.0025
                var parts = sizePart.Split('x');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var w) &&
                    int.TryParse(parts[1], out var h))
                {
                    var estimated = (int)Math.Round(w * h * 0.0025);
                    BitrateTextBox.Text = estimated.ToString();
                }
            }
        }

        private void CustomResolution_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (CustomResolutionPanel.Visibility != Visibility.Visible) return;

            if (int.TryParse(CustomResolutionWidthTextBox.Text, out var w) &&
                int.TryParse(CustomResolutionHeightTextBox.Text, out var h))
            {
                var estimated = (int)Math.Round(w * h * 0.0025);
                BitrateTextBox.Text = estimated.ToString();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

            if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
            {
                try { _ffmpegProcess.Kill(); } catch { }
            }
            SaveWindowPosition();
            base.OnClosing(e);
        }

        private static string WindowPosFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vchange", "window.txt");

        /// <summary>
        /// 恢复上次关闭时的窗口位置；没有记录时居中到主屏幕。
        /// </summary>
        private void RestoreWindowPosition()
        {
            try
            {
                if (File.Exists(WindowPosFile))
                {
                    var parts = File.ReadAllText(WindowPosFile).Split(',');
                    if (parts.Length == 2 &&
                        double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var left) &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var top))
                    {
                        double vLeft = SystemParameters.VirtualScreenLeft;
                        double vTop = SystemParameters.VirtualScreenTop;
                        double vRight = vLeft + SystemParameters.VirtualScreenWidth;
                        double vBottom = vTop + SystemParameters.VirtualScreenHeight;

                        // 记录的位置必须在屏幕可见范围内（避免拔掉显示器后窗口跑到屏幕外）
                        if (left >= vLeft - 20 && left <= vRight - 100 &&
                            top >= vTop - 10 && top <= vBottom - 50)
                        {
                            WindowStartupLocation = WindowStartupLocation.Manual;
                            Left = left;
                            Top = top;
                            return;
                        }
                    }
                }
            }
            catch { }

            // 默认：主屏幕正中间
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
        }

        private void SaveWindowPosition()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(WindowPosFile)!);
                File.WriteAllText(WindowPosFile, FormattableString.Invariant($"{Left},{Top}"));
            }
            catch { }
        }

        private void BrowseInput_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择视频文件",
                Filter = "视频文件|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.flv;*.webm|所有文件|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                InputFileTextBox.Text = dlg.FileName;
            }
        }

        private void Step1Next_Click(object sender, RoutedEventArgs e)
        {
            // Ensure a file is selected before proceeding
            if (string.IsNullOrWhiteSpace(InputFileTextBox.Text) || !File.Exists(InputFileTextBox.Text))
            {
                ShowMessage("请先选择有效的输入文件。");
                return;
            }
            ShowStep(2);
        }
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void Step2Next_Click(object sender, RoutedEventArgs e)
        {
            ShowStep(3);
        }

        private void Step3Next_Click(object sender, RoutedEventArgs e)
        {
            // Auto-fill recommended bitrate based on selected resolution (if not using source)
            var selected = (ResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            SetRecommendedBitrate(selected);
            ShowStep(4);
        }

        private void Step4Next_Click(object sender, RoutedEventArgs e)
        {
            BuildSummary();
            ShowStep(5);
        }

        private void Step2Back_Click(object sender, RoutedEventArgs e)
        {
            ShowStep(1);
        }

        private void Step3Back_Click(object sender, RoutedEventArgs e)
        {
            ShowStep(2);
        }

        private void Step4Back_Click(object sender, RoutedEventArgs e)
        {
            ShowStep(3);
        }

        private void Step5Back_Click(object sender, RoutedEventArgs e)
        {
            ShowStep(4);
        }

        private void ShowStep(int step)
        {
            int previous = _currentStep;
            _currentStep = step;

            var panels = new[] { Step1Panel, Step2Panel, Step3Panel, Step4Panel, Step5Panel };
            bool forward = step >= previous;

            for (int i = 0; i < panels.Length; i++)
            {
                var panel = panels[i];
                int panelStep = i + 1;

                if (panelStep == step)
                {
                    // 渐入：0.3s；只做位移滑动，避免淡入时深色半透明层产生色阶
                    panel.Visibility = Visibility.Visible;
                    panel.BeginAnimation(OpacityProperty, null);
                    panel.Opacity = 1;
                    panel.CacheMode = null;
                    Panel.SetZIndex(panel, 1); // 新页面置于上层

                    var inOffset = forward ? 48 : -48;
                    var translate = new TranslateTransform(inOffset, 0);
                    panel.RenderTransform = translate;

                    translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(inOffset, 0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
                }
                else if (panelStep == previous && previous != step && panel.Visibility == Visibility.Visible)
                {
                    // 旧页面：0.3s 淡出并反向滑走，与新页面同时结束，避免重叠残留
                    var outPanel = panel;
                    outPanel.CacheMode = null;
                    Panel.SetZIndex(outPanel, 0);

                    var translate = new TranslateTransform(0, 0);
                    outPanel.RenderTransform = translate;

                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    fadeOut.Completed += (s, ev) =>
                    {
                        outPanel.BeginAnimation(OpacityProperty, null);
                        outPanel.Opacity = 0;
                        outPanel.RenderTransform = null;
                        outPanel.Visibility = Visibility.Collapsed;
                    };
                    outPanel.BeginAnimation(OpacityProperty, fadeOut);

                    var outOffset = forward ? -48 : 48;
                    translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, outOffset, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
                }
                else
                {
                    // 其余面板直接隐藏并清理动画
                    panel.BeginAnimation(OpacityProperty, null);
                    panel.Opacity = 1;
                    panel.RenderTransform = null;
                    panel.CacheMode = null;
                    Panel.SetZIndex(panel, 0);
                    panel.Visibility = Visibility.Collapsed;
                }
            }

            UpdateStepIndicator(step);
        }

        private void UpdateStepIndicator(int activeStep)
        {
            var dots = new[] { StepDot1, StepDot2, StepDot3, StepDot4, StepDot5 };
            var nums = new[] { StepNum1, StepNum2, StepNum3, StepNum4, StepNum5 };
            var labels = new[] { StepLabel1, StepLabel2, StepLabel3, StepLabel4, StepLabel5 };
            var accent = (Brush)FindResource("AccentBrush");
            var dotInactive = (Brush)FindResource("Theme.StepDotInactive");
            var labelInactive = (Brush)FindResource("Theme.TextSecondary");
            var numInactive = (Brush)FindResource("Theme.TextPrimary");

            for (int i = 0; i < dots.Length; i++)
            {
                bool isActive = (i + 1) == activeStep;
                dots[i].Background = isActive ? accent : dotInactive;
                labels[i].Foreground = isActive ? accent : labelInactive;
                labels[i].FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
                nums[i].Foreground = isActive ? Brushes.White : numInactive;
            }
        }

        private void SameOption_Changed(object sender, RoutedEventArgs e)
        {
            UpdateControlStates();
        }

        /// <summary>
        /// 勾选“与原视频相同/使用原视频码率”时，将对应输入控件禁用变灰。
        /// </summary>
        private void UpdateControlStates()
        {
            // XAML 初始化期间控件可能尚未创建
            if (CodecComboBox == null) return;

            CodecComboBox.IsEnabled = CodecSameCheckBox.IsChecked != true;

            bool resSame = ResolutionSameCheckBox.IsChecked == true;
            ResolutionComboBox.IsEnabled = !resSame;
            if (CustomResolutionPanel != null)
                CustomResolutionPanel.IsEnabled = !resSame;

            FpsTextBox.IsEnabled = FpsSameCheckBox.IsChecked != true;
            BitrateTextBox.IsEnabled = BitrateSameCheckBox.IsChecked != true;
        }

        private void BuildSummary()
        {
            string fmt = ((ComboBoxItem)FormatComboBox.SelectedItem)?.Content?.ToString() ?? "未选择";

            string codec = CodecSameCheckBox.IsChecked == true
                ? "与原视频相同"
                : ((ComboBoxItem)CodecComboBox.SelectedItem)?.Content?.ToString() ?? "未选择";

            string res;
            if (ResolutionSameCheckBox.IsChecked == true)
            {
                res = "与原视频相同";
            }
            else
            {
                var sel = ((ComboBoxItem)ResolutionComboBox.SelectedItem)?.Content?.ToString();
                if (sel == "自定义")
                    res = $"{CustomResolutionWidthTextBox.Text}×{CustomResolutionHeightTextBox.Text}";
                else
                    res = sel ?? "未选择";
            }

            string fps = FpsSameCheckBox.IsChecked == true ? "与原视频相同" : FpsTextBox.Text + " fps";
            string bitrate = BitrateSameCheckBox.IsChecked == true ? "与原视频相同" : BitrateTextBox.Text + " kbps";

            SummaryTextBlock.Text =
                $"输入文件：{InputFileTextBox.Text}\n" +
                $"输出格式：{fmt}\n" +
                $"视频编码：{codec}\n" +
                $"分辨率：{res}\n" +
                $"帧率：{fps}\n" +
                $"码率：{bitrate}";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ResolutionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = (ResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

            if (CustomResolutionPanel != null)
            {
                if (selected == "自定义")
                    CustomResolutionPanel.Visibility = Visibility.Visible;
                else
                    CustomResolutionPanel.Visibility = Visibility.Collapsed;
            }

            // 自动推荐码率
            SetRecommendedBitrate(selected);
        }

        private async void WizardConvert_Click(object sender, RoutedEventArgs e)
        {
            // Same implementation as the enhanced conversion logic
            var inputPath = InputFileTextBox.Text;
            if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
            {
                ShowMessage("请选择有效的输入文件。");
                return;
            }

            if (FormatComboBox.SelectedItem == null)
            {
                ShowMessage("请选择输出格式。");
                return;
            }
            var format = ((ComboBoxItem)FormatComboBox.SelectedItem).Content.ToString();

            // Output file location
            var saveDlg = new SaveFileDialog
            {
                FileName = Path.GetFileNameWithoutExtension(inputPath) + "." + format,
                Filter = $"{format.ToUpper()}文件|*.{format}"
            };
            if (saveDlg.ShowDialog() != true)
                return;
            var outputPath = saveDlg.FileName;

            // Determine whether to use source values
            bool useSourceCodec = CodecSameCheckBox != null && CodecSameCheckBox.IsChecked == true;
            bool useSourceResolution = ResolutionSameCheckBox != null && ResolutionSameCheckBox.IsChecked == true;
            bool useSourceFps = FpsSameCheckBox != null && FpsSameCheckBox.IsChecked == true;
            bool useSourceBitrate = BitrateSameCheckBox != null && BitrateSameCheckBox.IsChecked == true;

            // Codec
            string codec;
            if (useSourceCodec)
                codec = "copy";
            else
            {
                var codecRaw = ((ComboBoxItem)CodecComboBox.SelectedItem)?.Content?.ToString() ?? "copy";
                codec = codecRaw.Replace(" (常用)", "").Trim();
            }

            // Bitrate
            int bitrate = 0;
            if (!useSourceBitrate)
            {
                if (!int.TryParse(BitrateTextBox.Text, out bitrate))
                {
                    ShowMessage("请填写合法的码率。");
                    return;
                }
            }

            // FPS
            int fps = 0;
            if (!useSourceFps)
            {
                if (!int.TryParse(FpsTextBox.Text, out fps))
                {
                    ShowMessage("请填写合法的帧率。");
                    return;
                }
            }

            // Resolution
            string width = "", height = "";
            if (!useSourceResolution)
            {
                if (ResolutionComboBox.SelectedItem == null)
                {
                    ShowMessage("请选择分辨率。");
                    return;
                }
                var res = ((ComboBoxItem)ResolutionComboBox.SelectedItem).Content.ToString();
                if (res.StartsWith("自定义"))
                {
                    var widthStr = CustomResolutionWidthTextBox.Text;
                    var heightStr = CustomResolutionHeightTextBox.Text;
                    if (!int.TryParse(widthStr, out var w) || !int.TryParse(heightStr, out var h))
                    {
                        ShowMessage("请在自定义分辨率中分别填写宽和高（整数）");
                        return;
                    }
                    width = w.ToString();
                    height = h.ToString();
                }
                else
                {
                    var sizePart = res.Split(' ')[0];
                    var parts = sizePart.Split('x');
                    width = parts[0];
                    height = parts[1];
                }
            }

            // Build ffmpeg arguments conditionally
            var argsList = new List<string>();
            argsList.Add($"-i \"{inputPath}\"");
            argsList.Add($"-c:v {codec}");
            if (!useSourceBitrate)
                argsList.Add($"-b:v {bitrate}k");
            if (!useSourceResolution)
                argsList.Add($"-vf \"scale={width}:{height}\"");
            if (!useSourceFps)
                argsList.Add($"-r {fps}");
            argsList.Add($"\"{outputPath}\"");

            // -y 覆盖输出；-hide_banner 精简日志；-nostats/-progress 输出可解析进度
            var args = "-y -hide_banner -nostdin -nostats -progress pipe:1 " + string.Join(" ", argsList);

            // 初始化进度显示，并清空上一次的转换日志
            ConvertProgressBar.Visibility = Visibility.Visible;
            ProgressTextBlock.Visibility = Visibility.Visible;
            ConvertProgressBar.IsIndeterminate = false;
            ConvertProgressBar.Value = 0;
            ProgressTextBlock.Text = "0%";
            LogTextBox.Clear();

            bool ok = await RunFfmpegAsync(args);

            if (ok)
            {
                ConvertProgressBar.Value = 100;
                ProgressTextBlock.Text = "已完成 100%";
            }
            else
            {
                ConvertProgressBar.Value = 0;
                ProgressTextBlock.Text = "转换失败";
            }
        }

        private async Task<bool> RunFfmpegAsync(string arguments)
        {
            _mediaDuration = null;
            _currentTime = TimeSpan.Zero;
            _currentSpeed = "";

            var psi = new ProcessStartInfo
            {
                FileName = FfmpegProvider.GetPath(),
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            _ffmpegProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

            // stdout：-progress 的 key=value 进度信息（不写日志，只更新进度条）
            _ffmpegProcess.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                var line = e.Data;
                Dispatcher.BeginInvoke(new Action(() => HandleProgressLine(line)));
            };

            // stderr：读取总时长 + 正常日志
            _ffmpegProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                var line = e.Data;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var m = DurationRegex.Match(line);
                    if (m.Success && _mediaDuration == null)
                    {
                        try
                        {
                            _mediaDuration = new TimeSpan(
                                int.Parse(m.Groups[1].Value),
                                int.Parse(m.Groups[2].Value),
                                (int)double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
                        }
                        catch { }
                    }
                    AppendFfmpegLog(line);
                }));
            };

            try
            {
                _ffmpegProcess.Start();

                // 提高优先级，避免被界面线程/系统调度拖慢
                try { _ffmpegProcess.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }

                _ffmpegProcess.BeginOutputReadLine();
                _ffmpegProcess.BeginErrorReadLine();
                await _ffmpegProcess.WaitForExitAsync();

                int exitCode = _ffmpegProcess.ExitCode;
                _ffmpegProcess = null;

                if (exitCode == 0)
                    return true;

                ShowMessage($"转换失败（退出码 {exitCode}），详情请查看转换日志。");
                return false;
            }
            catch (Exception ex)
            {
                ShowMessage($"执行 ffmpeg 时出错: {ex.Message}");
                return false;
            }
        }

        private void HandleProgressLine(string line)
        {
            if (line.StartsWith("out_time="))
            {
                var m = OutTimeRegex.Match(line);
                if (m.Success)
                {
                    try
                    {
                        _currentTime = new TimeSpan(
                            int.Parse(m.Groups[1].Value),
                            int.Parse(m.Groups[2].Value),
                            (int)double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
                    }
                    catch { }
                }
            }
            else if (line.StartsWith("speed="))
            {
                _currentSpeed = line.Substring("speed=".Length).Trim();
            }
            else if (line.StartsWith("progress="))
            {
                UpdateProgressUi();
            }
        }

        private void UpdateProgressUi()
        {
            if (_mediaDuration.HasValue && _mediaDuration.Value.TotalSeconds > 0.1)
            {
                double pct = Math.Min(100, _currentTime.TotalSeconds / _mediaDuration.Value.TotalSeconds * 100);
                ConvertProgressBar.IsIndeterminate = false;
                ConvertProgressBar.Value = pct;
                ProgressTextBlock.Text = string.IsNullOrEmpty(_currentSpeed)
                    ? $"{pct:0}%"
                    : $"{pct:0}% · {_currentSpeed}";
            }
            else if (!string.IsNullOrEmpty(_currentSpeed))
            {
                ProgressTextBlock.Text = _currentSpeed;
            }
        }

        private void AppendFfmpegLog(string message)
        {
            // 仅记录 ffmpeg 原始输出；过长时裁剪，避免界面越用越卡
            if (LogTextBox.Text.Length > 200_000)
                LogTextBox.Text = LogTextBox.Text.Substring(LogTextBox.Text.Length - 100_000);

            LogTextBox.AppendText(message + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        }

        private void ShowMessage(string message, string title = "提示")
        {
            var dlg = new MessageDialog(message, title) { Owner = this };
            dlg.ShowDialog();
        }

        private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 该事件会在 InitializeComponent 期间触发，此时 FormatHintText 可能尚未创建
            if (FormatHintText == null) return;
            if (FormatComboBox.SelectedItem is ComboBoxItem item)
                FormatHintText.Text = $"输出扩展名: .{item.Content}";
        }
    }
}
