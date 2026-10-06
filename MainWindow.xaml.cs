using System;
using System.Collections.Generic;
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

namespace Vchange
{
    public enum AppMode
    {
        Home,
        Convert,
        Timelapse,
        Stack,
        ImageConvert
    }

    public partial class MainWindow : Window
    {
        private Process? _ffmpegProcess;
        private AppMode _mode = AppMode.Home;
        private int _convertStep = 1;
        private int _timelapseStep = 1;
        private int _stackStep = 1;
        private int _imageConvertStep = 1;
        private string? _autoOutputPath;

        // 批量转换：视频流程已选择的输入文件（>=1 个）
        private List<string> _inputFiles = new();
        // >1 表示批量模式：进度文字显示 “i/N · ” 前缀
        private int _batchTotal;
        private int _batchIndex;
        private bool _convertBusy;

        // 面板与步骤指示器
        private Grid[] _convertPanels = Array.Empty<Grid>();
        private Grid[] _timelapsePanels = Array.Empty<Grid>();
        private Grid[] _stackPanels = Array.Empty<Grid>();
        private Grid[] _imagePanels = Array.Empty<Grid>();
        private List<Border> _indicatorDots = new();
        private List<TextBlock> _indicatorNums = new();
        private List<TextBlock> _indicatorLabels = new();

        // 进度解析
        private TimeSpan? _mediaDuration;
        private TimeSpan? _expectedDuration;   // 延时合成：帧数/帧率 可预知总时长
        private TimeSpan _currentTime = TimeSpan.Zero;
        private string _currentSpeed = "";
        private static readonly Regex DurationRegex = new(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);
        private static readonly Regex OutTimeRegex = new(@"out_time=(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);

        // 当前 ffmpeg 任务绑定的界面控件（进度条/文字/日志）
        private ProgressBar _activeProgressBar = null!;
        private TextBlock _activeProgressText = null!;
        private TextBox _activeLogBox = null!;

        public MainWindow()
        {
            // 先应用系统主题，再加载界面（避免闪色）
            ThemeManager.Apply(ThemeManager.SystemIsLight());

            InitializeComponent();

            _convertPanels = new[] { Step1Panel, Step2Panel, Step3Panel, Step4Panel, Step5Panel };
            _timelapsePanels = new[] { T1Panel, T2Panel, T3Panel, T4Panel, T5Panel, T6Panel };
            _stackPanels = new[] { S1Panel, S2Panel, S3Panel, S4Panel };
            _imagePanels = new[] { C1Panel, C2Panel, C3Panel, C4Panel };

            UpdateControlStates();
            RestoreWindowPosition();

            // 隐藏的命令行参数（截图/自动化测试用）：--input <文件> --output <文件>
            var cliArgs = Environment.GetCommandLineArgs();
            for (int i = 1; i < cliArgs.Length; i++)
            {
                if (cliArgs[i] == "--input" && i + 1 < cliArgs.Length)
                {
                    InputFileTextBox.Text = cliArgs[++i];
                    _inputFiles = new List<string> { InputFileTextBox.Text };
                    ShowConvertStep(1);
                }
                else if (cliArgs[i] == "--output" && i + 1 < cliArgs.Length)
                    _autoOutputPath = cliArgs[++i];
            }

            // 应用与主题相关的图标和视觉
            ApplyThemeIcon();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            // 启动后在调试输出记录材质状态（不写入界面日志）
            Loaded += (s, e) => System.Diagnostics.Debug.WriteLine($"界面材质: {_acrylicStatus}（Windows {Environment.OSVersion.Version}）");
        }

        #region 流程导航

        private void ShowHome()
        {
            _mode = AppMode.Home;
            StepIndicatorBorder.Visibility = Visibility.Collapsed;
            EnsureAllPanels();
            // 返回主页属于“后退”导航；当前可见的流程页作为退出页
            Grid? leaving = _allPanels.FirstOrDefault(p =>
                p != null && !ReferenceEquals(p, HomePanel) && p.Visibility == Visibility.Visible);
            AnimatePanelSwitch(new[] { HomePanel }, 0, prevIndex: -1, backward: true, extraOutgoing: leaving);
        }

        private void ShowConvertStep(int step)
        {
            int prev = _convertStep > 0 ? _convertStep - 1 : -1;
            bool backward = step < _convertStep;
            _mode = AppMode.Convert;
            _convertStep = step;
            StepIndicatorBorder.Visibility = Visibility.Visible;
            BuildStepIndicator(new[] { "选择文件", "输出格式", "视频参数", "码率", "转换" });
            UpdateStepIndicator(step);
            AnimatePanelSwitch(_convertPanels, step - 1, prev, backward);
        }

        private void ShowTimelapseStep(int step)
        {
            int prev = _timelapseStep > 0 ? _timelapseStep - 1 : -1;
            bool backward = step < _timelapseStep;
            _mode = AppMode.Timelapse;
            _timelapseStep = step;
            StepIndicatorBorder.Visibility = Visibility.Visible;
            BuildStepIndicator(new[] { "选择文件夹", "重命名方案", "输出格式", "视频参数", "码率", "合成" });
            UpdateStepIndicator(step);
            AnimatePanelSwitch(_timelapsePanels, step - 1, prev, backward);
        }

        private void ShowStackStep(int step)
        {
            int prev = _stackStep > 0 ? _stackStep - 1 : -1;
            bool backward = step < _stackStep;
            _mode = AppMode.Stack;
            _stackStep = step;
            StepIndicatorBorder.Visibility = Visibility.Visible;
            BuildStepIndicator(new[] { "选择文件夹", "输出格式", "品质与分辨率", "输出与日志" });
            UpdateStepIndicator(step);
            AnimatePanelSwitch(_stackPanels, step - 1, prev, backward);
        }

        private void ShowImageConvertStep(int step)
        {
            int prev = _imageConvertStep > 0 ? _imageConvertStep - 1 : -1;
            bool backward = step < _imageConvertStep;
            _mode = AppMode.ImageConvert;
            _imageConvertStep = step;
            StepIndicatorBorder.Visibility = Visibility.Visible;
            BuildStepIndicator(new[] { "选择文件", "输出格式", "品质与选项", "输出与日志" });
            UpdateStepIndicator(step);
            AnimatePanelSwitch(_imagePanels, step - 1, prev, backward);
        }

        private void BackToHome_Click(object sender, RoutedEventArgs e) => ShowHome();

        private void HomeConvert_Click(object sender, RoutedEventArgs e)
        {
            _convertStep = 0; // 从主页进入固定为“前进”方向
            ShowConvertStep(1);
            UpdateControlStates();
        }

        private void HomeTimelapse_Click(object sender, RoutedEventArgs e)
        {
            _timelapseStep = 0;
            ShowTimelapseStep(1);
            T1InfoText.Text = "";
            ResetTimelapseProgressUi();
        }

        private void HomeStack_Click(object sender, RoutedEventArgs e)
        {
            _stackStep = 0;
            ShowStackStep(1);
            S1InfoText.Text = "";
        }

        private void HomeImageCard_Click(object sender, RoutedEventArgs e)
        {
            _imageConvertStep = 0;
            ShowImageConvertStep(1);
            C1InfoText.Text = "";
        }

        /// <summary>确保全部面板清单已初始化。</summary>
        private void EnsureAllPanels()
        {
            if (_allPanels.Length == 0)
                _allPanels = new[] { HomePanel, Step1Panel, Step2Panel, Step3Panel, Step4Panel, Step5Panel,
                    T1Panel, T2Panel, T3Panel, T4Panel, T5Panel, T6Panel,
                    S1Panel, S2Panel, S3Panel, S4Panel,
                    C1Panel, C2Panel, C3Panel, C4Panel };
        }

        /// <summary>全部页面面板：跨流程切换时用于清理残留页面。</summary>
        private Grid[] _allPanels = Array.Empty<Grid>();

        /// <summary>
        /// 通用面板切换动画（与项目最初的 ShowStep 相同）：
        /// 前进时新页从右侧滑入、旧页向左滑出并淡出；后退方向相反。
        /// 300ms，两页同时开始同时结束。
        /// </summary>
        private void AnimatePanelSwitch(Grid[] panels, int index, int prevIndex, bool backward,
            Grid? extraOutgoing = null)
        {
            // 不在目标数组内的所有面板直接隐藏（防止跨流程切换时旧页面残留）
            EnsureAllPanels();
            var targetSet = new HashSet<Grid>(panels);
            foreach (var p in _allPanels)
            {
                if (p == null || targetSet.Contains(p)) continue;
                p.BeginAnimation(OpacityProperty, null);
                p.Opacity = 1;
                p.RenderTransform = null;
                p.CacheMode = null;
                Panel.SetZIndex(p, 0);
                p.Visibility = Visibility.Collapsed;
            }

            // 前进：新页从右(+48)滑入、旧页向左(-48)滑出；后退相反
            double inOffset = backward ? -48 : 48;
            double outOffset = backward ? 48 : -48;

            for (int i = 0; i < panels.Length; i++)
            {
                var panel = panels[i];

                if (i == index)
                {
                    // 新页：只做位移滑入，不透明度保持 1
                    panel.Visibility = Visibility.Visible;
                    panel.BeginAnimation(OpacityProperty, null);
                    panel.Opacity = 1;
                    panel.CacheMode = null;
                    Panel.SetZIndex(panel, 1); // 新页面置于上层

                    var translate = new TranslateTransform(inOffset, 0);
                    panel.RenderTransform = translate;

                    translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(inOffset, 0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
                }
                else if (i == prevIndex && prevIndex != index)
                {
                    // 旧页：淡出并沿反方向滑走，与新页面同时结束
                    var outPanel = panel;
                    outPanel.CacheMode = null;
                    Panel.SetZIndex(outPanel, 0);
                    outPanel.Visibility = Visibility.Visible;

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
                        outPanel.Opacity = 1;
                    };
                    outPanel.BeginAnimation(OpacityProperty, fadeOut);

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

            // 跨流程退出页（如返回主页时原流程页）：滑出淡出
            if (extraOutgoing != null && !targetSet.Contains(extraOutgoing) &&
                extraOutgoing.Visibility == Visibility.Visible)
            {
                var outPanel = extraOutgoing;
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
                    outPanel.Opacity = 1;
                };
                outPanel.BeginAnimation(OpacityProperty, fadeOut);

                translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, outOffset, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            }
        }

        /// <summary>按当前功能重建步骤指示器圆点。</summary>
        private void BuildStepIndicator(string[] labels)
        {
            var indicator = StepIndicatorPanel;
            indicator.Children.Clear();
            _indicatorDots.Clear();
            _indicatorNums.Clear();
            _indicatorLabels.Clear();

            var accent = (Brush)FindResource("AccentBrush");
            var dotInactive = (Brush)FindResource("Theme.StepDotInactive");
            var labelInactive = (Brush)FindResource("Theme.TextSecondary");
            var numInactive = (Brush)FindResource("Theme.TextPrimary");
            var chevronBrush = (Brush)FindResource("Theme.Chevron");

            for (int i = 0; i < labels.Length; i++)
            {
                if (i > 0)
                {
                    indicator.Children.Add(new TextBlock
                    {
                        Text = "›",
                        Foreground = chevronBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0),
                        FontSize = 16
                    });
                }

                var dot = new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = new CornerRadius(12),
                    Background = dotInactive
                };
                var num = new TextBlock
                {
                    Text = (i + 1).ToString(),
                    Foreground = numInactive,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                dot.Child = num;

                var label = new TextBlock
                {
                    Text = labels[i],
                    Foreground = labelInactive,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };

                var wrap = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
                wrap.Children.Add(dot);
                wrap.Children.Add(label);
                indicator.Children.Add(wrap);

                _indicatorDots.Add(dot);
                _indicatorNums.Add(num);
                _indicatorLabels.Add(label);
            }
        }

        private void UpdateStepIndicator(int activeStep)
        {
            if (_indicatorDots.Count == 0) return;
            var accent = (Brush)FindResource("AccentBrush");
            var dotInactive = (Brush)FindResource("Theme.StepDotInactive");
            var labelInactive = (Brush)FindResource("Theme.TextSecondary");
            var numInactive = (Brush)FindResource("Theme.TextPrimary");

            for (int i = 0; i < _indicatorDots.Count; i++)
            {
                bool isActive = (i + 1) == activeStep;
                _indicatorDots[i].Background = isActive ? accent : dotInactive;
                _indicatorLabels[i].Foreground = isActive ? accent : labelInactive;
                _indicatorLabels[i].FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
                _indicatorNums[i].Foreground = isActive ? Brushes.White : numInactive;
            }
        }

        #endregion

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
            if (_mode == AppMode.Convert) UpdateStepIndicator(_convertStep);
            else if (_mode == AppMode.Timelapse) UpdateStepIndicator(_timelapseStep);
            else if (_mode == AppMode.Stack) UpdateStepIndicator(_stackStep);
            else if (_mode == AppMode.ImageConvert) UpdateStepIndicator(_imageConvertStep);
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
        internal static readonly Dictionary<string, int> ResolutionBitrateMap = new()
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

        /// <summary>
        /// 返回一个“确定可写”的推荐保存目录：优先用户指定目录（校验可写性），
        /// 失败时回退到 下载 → 文档 → 桌面。避免对话框默认落在受保护的库根目录。
        /// </summary>
        internal static string GetWritableSaveDir(string? preferred)
        {
            foreach (var dir in new[]
            {
                preferred,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            })
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
                try
                {
                    var probe = Path.Combine(dir, $"$vc_write_test_{Guid.NewGuid():N}.tmp");
                    File.WriteAllText(probe, "t");
                    File.Delete(probe);
                    return dir;
                }
                catch { /* 不可写，尝试下一个 */ }
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        /// <summary>把保存/写入相关异常翻译为友好的中文提示。</summary>
        internal static string FriendlySaveError(Exception ex, string path)
        {
            return ex is UnauthorizedAccessException
                ? $"没有权限写入：{path}\n请换一个保存位置（例如“下载”或“文档”文件夹）后重试。"
                : $"保存文件失败：{ex.Message}\n文件路径：{path}\n请尝试更换保存位置。";
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
                Title = "选择视频文件（可多选，批量转换）",
                Filter = "视频/动图文件|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.flv;*.webm;*.gif;*.png|所有文件|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
            {
                _inputFiles = dlg.FileNames.ToList();
                InputFileTextBox.Text = DescribeFiles(_inputFiles);
            }
        }

        /// <summary>把已选文件列表转成显示文本：单个显示完整路径，多个显示数量与文件名。</summary>
        private static string DescribeFiles(List<string> files)
        {
            if (files.Count == 1) return files[0];
            var shown = string.Join("、", files.Take(3).Select(Path.GetFileName));
            if (files.Count > 3) shown += " 等";
            return $"已选择 {files.Count} 个文件：{shown}";
        }

        /// <summary>
        /// 为批量输出规划互不冲突的路径：同名或已存在则追加序号，多输入同名文件也不会互相覆盖。
        /// </summary>
        private static List<string> PlanOutputs(string dir, List<string> inputs, string format, string suffix = "")
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var f in inputs)
            {
                string stem = Path.GetFileNameWithoutExtension(f) + suffix;
                string cand = Path.Combine(dir, stem + "." + format);
                int i = 1;
                while (File.Exists(cand) || used.Contains(cand))
                    cand = Path.Combine(dir, $"{stem} ({i++}).{format}");
                used.Add(cand);
                result.Add(cand);
            }
            return result;
        }

        private void Step1Next_Click(object sender, RoutedEventArgs e)
        {
            // Ensure files are selected before proceeding
            if (_inputFiles.Count == 0)
            {
                ShowMessage("请先选择有效的输入文件。");
                return;
            }
            if (_inputFiles.Any(f => !File.Exists(f)))
            {
                ShowMessage("所选文件中有文件不存在，请重新选择。");
                return;
            }
            ShowConvertStep(2);
        }
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void Step2Next_Click(object sender, RoutedEventArgs e)
        {
            ShowConvertStep(3);
        }

        private void Step3Next_Click(object sender, RoutedEventArgs e)
        {
            // Auto-fill recommended bitrate based on selected resolution (if not using source)
            var selected = (ResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            SetRecommendedBitrate(selected);
            ShowConvertStep(4);
        }

        private void Step4Next_Click(object sender, RoutedEventArgs e)
        {
            BuildSummary();
            ShowConvertStep(5);
        }

        private void Step2Back_Click(object sender, RoutedEventArgs e)
        {
            ShowConvertStep(1);
        }

        private void Step3Back_Click(object sender, RoutedEventArgs e)
        {
            ShowConvertStep(2);
        }

        private void Step4Back_Click(object sender, RoutedEventArgs e)
        {
            ShowConvertStep(3);
        }

        private void Step5Back_Click(object sender, RoutedEventArgs e)
        {
            ShowConvertStep(4);
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

            string inputDesc;
            if (_inputFiles.Count == 1)
            {
                inputDesc = _inputFiles[0];
            }
            else
            {
                var names = string.Join("、", _inputFiles.Take(3).Select(Path.GetFileName));
                inputDesc = $"共 {_inputFiles.Count} 个文件（{names}{(_inputFiles.Count > 3 ? " 等" : "")}）";
            }

            SummaryTextBlock.Text =
                $"输入文件：{inputDesc}\n" +
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
            if (_convertBusy) return;   // 转换进行中，忽略重复点击
            _convertBusy = true;
            try
            {
                await RunWizardConvertAsync();
            }
            finally
            {
                _convertBusy = false;
                _batchTotal = 0;
                _batchIndex = 0;
            }
        }

        /// <summary>向导“开始转换”：单文件弹出保存对话框，多文件选输出文件夹逐个批量转换。</summary>
        private async Task RunWizardConvertAsync()
        {
            var inputs = _inputFiles;
            if (inputs.Count == 0 || inputs.Any(f => !File.Exists(f)))
            {
                ShowMessage("请选择有效的输入文件。");
                return;
            }

            if (FormatComboBox.SelectedItem == null)
            {
                ShowMessage("请选择输出格式。");
                return;
            }
            var formatRaw = ((ComboBoxItem)FormatComboBox.SelectedItem).Content?.ToString() ?? "mp4";
            var format = formatRaw.StartsWith("apng") ? "apng" : formatRaw;

            // 输出路径：单文件弹出保存对话框（自动化参数优先）；多文件选择输出文件夹
            var outputs = new List<string>();
            if (!string.IsNullOrEmpty(_autoOutputPath) && inputs.Count == 1)
            {
                outputs.Add(_autoOutputPath);
            }
            else if (inputs.Count == 1)
            {
                var srcDir = Path.GetDirectoryName(inputs[0]);
                var saveDlg = new SaveFileDialogEx
                {
                    InitialDirectory = GetWritableSaveDir(srcDir),
                    FileName = Path.GetFileNameWithoutExtension(inputs[0]) + "." + format,
                    Filter = $"{format.ToUpper()}文件|*.{format}"
                };
                string? picked = saveDlg.ShowDialog(this)
            if (saveDlg.Diagnostics.Count > 0)
             LogTextBox.AppendText("对话框诊断: " + string.Join("; ", saveDlg.Diagnostics) + Environment.NewLine);;
                if (picked == null)
                    return;
                outputs.Add(picked);
            }
            else
            {
                var folderDlg = new OpenFolderDialog { Title = "选择输出文件夹" };
                if (folderDlg.ShowDialog() != true)
                    return;
                outputs.AddRange(PlanOutputs(folderDlg.FolderName, inputs, format));
            }

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
                var res = ((ComboBoxItem)ResolutionComboBox.SelectedItem).Content?.ToString() ?? "";
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

            // 组装单个文件的 ffmpeg 参数（闭包复用上面的全部校验结果）
            string BuildArgs(string inputPath, string outputPath)
            {
                var argsList = new List<string>();
                argsList.Add($"-i \"{inputPath}\"");
                argsList.Add($"-c:v {codec}");
                if (!useSourceBitrate)
                    argsList.Add($"-b:v {bitrate}k");
                if (!useSourceResolution)
                    argsList.Add($"-vf \"scale={width}:{height}\"");
                if (!useSourceFps)
                    argsList.Add($"-r {fps}");
                // gif/apng 用 RGB 像素；其余统一 yuv420p 保证兼容性
                if (format == "gif" || format == "apng")
                    argsList.Add("-pix_fmt rgb24");
                else
                    argsList.Add("-pix_fmt yuv420p");
                argsList.Add($"\"{outputPath}\"");

                // -y 覆盖输出；-hide_banner 精简日志；-nostats/-progress 输出可解析进度
                return "-y -hide_banner -nostdin -nostats -progress pipe:1 " + string.Join(" ", argsList);
            }

            // 初始化进度显示，并清空上一次的转换日志
            _activeProgressBar = ConvertProgressBar;
            _activeProgressText = ProgressTextBlock;
            _activeLogBox = LogTextBox;
            ConvertProgressBar.Visibility = Visibility.Visible;
            ProgressTextBlock.Visibility = Visibility.Visible;
            ConvertProgressBar.IsIndeterminate = false;
            ConvertProgressBar.Value = 0;
            ProgressTextBlock.Text = "0%";
            LogTextBox.Clear();

            // 批量顺序转换：逐个执行，进度显示 “i/N · 百分比”
            _batchTotal = inputs.Count;
            int okCount = 0, failCount = 0;
            for (int i = 0; i < inputs.Count; i++)
            {
                _batchIndex = i + 1;
                _expectedDuration = null;
                ConvertProgressBar.Value = 0;
                ProgressTextBlock.Text = inputs.Count > 1 ? $"{i + 1}/{inputs.Count} · 0%" : "0%";
                AppendLog($"[{i + 1}/{inputs.Count}] 开始：{Path.GetFileName(inputs[i])} → {Path.GetFileName(outputs[i])}");

                bool ok = await RunFfmpegAsync(BuildArgs(inputs[i], outputs[i]), showErrors: inputs.Count == 1);
                if (ok)
                {
                    okCount++;
                    AppendLog($"[{i + 1}/{inputs.Count}] 完成：{Path.GetFileName(outputs[i])}");
                }
                else
                {
                    failCount++;
                    AppendLog($"[{i + 1}/{inputs.Count}] 转换失败：{Path.GetFileName(inputs[i])}");
                }
            }

            if (failCount == 0)
            {
                ConvertProgressBar.Value = 100;
                ProgressTextBlock.Text = inputs.Count == 1 ? "已完成 100%" : $"批量完成 {okCount}/{inputs.Count}";
            }
            else if (inputs.Count == 1)
            {
                ConvertProgressBar.Value = 0;
                ProgressTextBlock.Text = "转换失败";
            }
            else
            {
                ConvertProgressBar.Value = okCount * 100.0 / inputs.Count;
                ProgressTextBlock.Text = $"完成 {okCount}/{inputs.Count}（{failCount} 个失败）";
            }
            if (inputs.Count > 1)
                AppendLog($"输出文件夹：{Path.GetDirectoryName(outputs[0])}");
        }

        private async Task<bool> RunFfmpegAsync(string arguments, bool showErrors = true)
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

                if (showErrors)
                    ShowMessage($"执行失败（退出码 {exitCode}），详情请查看日志。");
                return false;
            }
            catch (Exception ex)
            {
                if (showErrors)
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
            // 批量模式显示 “i/N · ” 前缀
            string prefix = _batchTotal > 1 ? $"{_batchIndex}/{_batchTotal} · " : "";
            // 优先使用 ffmpeg 报告的时长；延时合成为图片序列（无输入时长），使用预估值
            TimeSpan? total = _mediaDuration ?? _expectedDuration;
            if (total.HasValue && total.Value.TotalSeconds > 0.1)
            {
                double pct = Math.Min(100, _currentTime.TotalSeconds / total.Value.TotalSeconds * 100);
                _activeProgressBar.IsIndeterminate = false;
                _activeProgressBar.Value = pct;
                _activeProgressText.Text = string.IsNullOrEmpty(_currentSpeed)
                    ? $"{prefix}{pct:0}%"
                    : $"{prefix}{pct:0}% · {_currentSpeed}";
            }
            else if (!string.IsNullOrEmpty(_currentSpeed))
            {
                _activeProgressText.Text = prefix + _currentSpeed;
            }
        }

        private void AppendFfmpegLog(string message)
        {
            if (_activeLogBox == null) return;

            // 仅记录 ffmpeg 原始输出；过长时裁剪，避免界面越用越卡
            if (_activeLogBox.Text.Length > 200_000)
                _activeLogBox.Text = _activeLogBox.Text.Substring(_activeLogBox.Text.Length - 100_000);

            _activeLogBox.AppendText(message + Environment.NewLine);
            _activeLogBox.ScrollToEnd();
        }

        /// <summary>追加一行业务日志（区别于 ffmpeg 原始输出）。</summary>
        private void AppendLog(string message)
        {
            if (_activeLogBox == null) return;
            _activeLogBox.AppendText(message + Environment.NewLine);
            _activeLogBox.ScrollToEnd();
        }

        private void ShowMessage(string message, string title = "提示")
        {
            var dlg = new MessageDialog(message, title) { Owner = this };
            dlg.ShowDialog();
        }

        /// <summary>带“是/否”按钮的确认对话框。</summary>
        private bool ShowConfirm(string message, string title = "确认", string? yes = null, string? no = null)
        {
            var dlg = new MessageDialog(message, title, confirm: true, yesText: yes, noText: no) { Owner = this };
            dlg.ShowDialog();
            return dlg.Confirmed;
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
