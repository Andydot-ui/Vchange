using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace Vchange
{
    /// <summary>
    /// 延时合成流程：图片文件夹 → 重命名方式（复制到 rename / 就地重命名）→
    /// 输出格式 → 视频参数（编码/分辨率/帧率）→ 码率 → 合成（重命名与合成各一条进度条）。
    /// </summary>
    public partial class MainWindow
    {
        private string _tFolder = "";
        private List<string> _tFiles = new();
        private int _tFirstW, _tFirstH;
        private bool _tRenameForced;      // 磁盘空间不足时强制使用“直接重命名原文件”
        private bool _tBusy;

        #region 第1步：选择文件夹

        private async void T1Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "选择图片文件夹" };
            if (Directory.Exists(_tFolder)) dlg.InitialDirectory = _tFolder;
            if (dlg.ShowDialog() != true) return;

            _tFolder = dlg.FolderName;
            TFolderTextBox.Text = _tFolder;

            // 用户换文件夹后，解除复制选项的禁用状态（恢复默认：复制到 rename 文件夹）
            _tRenameForced = false;
            TRename1Radio.IsEnabled = true;
            TRenameCard1.Opacity = 1;
            T2NoteText.Text = "";

            _tFiles = RenameEngine.ListImages(_tFolder);
            _tFirstW = _tFirstH = 0;
            if (_tFiles.Count > 0)
            {
                (_tFirstW, _tFirstH) = await Task.Run(() => TryGetImageSize(_tFiles[0]));
            }
            UpdateT1Info();
            UpdateTResInfo();
        }

        private void UpdateT1Info()
        {
            if (_tFiles.Count == 0)
                T1InfoText.Text = string.IsNullOrEmpty(_tFolder) ? "" : "该文件夹中没有找到图片文件";
            else
                T1InfoText.Text = _tFirstW > 0
                    ? $"共 {_tFiles.Count} 张图片 · 首张分辨率 {_tFirstW}×{_tFirstH}"
                    : $"共 {_tFiles.Count} 张图片";
        }

        private void T1Next_Click(object sender, RoutedEventArgs e)
        {
            if (_tFiles.Count == 0)
            {
                ShowMessage("请选择包含图片文件的文件夹。");
                return;
            }
            ShowTimelapseStep(2);
            RefreshTDiskInfo();
            // 进入页面立即对当前选中卡片播强调动画（与滑入同步，无停顿）
            AnimateCardSelection(TRenameCard1, TRenameMode == 1);
            AnimateCardSelection(TRenameCard2, TRenameMode == 2);
        }

        #endregion

        #region 第2步：重命名方式（两张卡片 + 醒目圆点）

        private void TRenameCard1_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!TRename1Radio.IsEnabled) return;
            TRename1Radio.IsChecked = true;
        }

        private void TRenameCard2_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            TRename2Radio.IsChecked = true;
        }

        // 选中切换：卡片边框渐变到强调蓝 + 光晕淡入 + 选中瞬间轻微弹跳
        private void TRenameMode_Changed(object sender, RoutedEventArgs e)
        {
            if (TRenameCard1 == null || TRenameCard2 == null) return;
            bool mode1 = TRename1Radio.IsChecked == true;
            AnimateCardSelection(TRenameCard1, mode1);
            AnimateCardSelection(TRenameCard2, !mode1);
        }

        /// <summary>卡片选中/取消的强调动画（边框颜色、光晕、弹跳）。</summary>
        private void AnimateCardSelection(System.Windows.Controls.Border card, bool selected)
        {
            var accent = (Color)ColorConverter.ConvertFromString("#FF0A84FF");
            var normalColor = ((SolidColorBrush)FindResource("Theme.SurfaceBorder")).Color;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            // 边框颜色：选中 → 强调蓝；取消 → 恢复主题边框色（动画完成后重新跟随主题资源）
            var brush = card.BorderBrush as SolidColorBrush;
            if (brush == null || brush.IsFrozen)
            {
                brush = new SolidColorBrush(((SolidColorBrush)card.BorderBrush).Color);
                card.BorderBrush = brush;
            }
            var colorAnim = new ColorAnimation(selected ? accent : normalColor, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = ease
            };
            if (!selected)
            {
                colorAnim.Completed += (s, ev) =>
                    card.SetResourceReference(Border.BorderBrushProperty, "Theme.SurfaceBorder");
            }
            brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);

            // 选中瞬间：轻微弹跳（缩放 0.985 → 1 带回弹）
            if (selected)
            {
                card.RenderTransformOrigin = new Point(0.5, 0.5);
                var scale = new ScaleTransform(0.985, 0.985);
                card.RenderTransform = scale;
                var bounce = new DoubleAnimation(1, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 1.6 }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, bounce);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(1, TimeSpan.FromMilliseconds(260))
                    {
                        EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 1.6 }
                    });
                bounce.Completed += (s, ev) =>
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    card.RenderTransform = null;
                };
            }
            else if (card.RenderTransform is ScaleTransform)
            {
                card.RenderTransform = null;
            }
        }

        private int TRenameMode => TRename1Radio.IsChecked == true ? 1 : 2;

        private void RefreshTDiskInfo()
        {
            long needed = RenameEngine.EstimateCopyBytes(_tFiles);
            long? free = RenameEngine.GetFreeBytes(_tFolder);
            string needStr = FormatBytes(needed);
            if (free.HasValue)
                TDiskInfoText.Text = $"当前分区可用空间：{FormatBytes(free.Value)} ｜ 复制全部图片约需要：{needStr}";
            else
                TDiskInfoText.Text = $"复制全部图片约需要：{needStr}（无法读取可用空间）";
        }

        private void T2Back_Click(object sender, RoutedEventArgs e) => ShowTimelapseStep(1);

        private void T2Next_Click(object sender, RoutedEventArgs e)
        {
            if (TRenameMode == 1 && !_tRenameForced)
            {
                long needed = RenameEngine.EstimateCopyBytes(_tFiles);
                long? free = RenameEngine.GetFreeBytes(_tFolder);

                if (free.HasValue && needed > free.Value)
                {
                    // 磁盘空间不足：询问是否切换；切换后停留在本页
                    bool switchTo2 = ShowConfirm(
                        $"复制方案需要约 {FormatBytes(needed)} 的磁盘空间，当前分区仅有 {FormatBytes(free.Value)} 可用。\n\n" +
                        $"是否改用“直接重命名原文件”？",
                        "磁盘空间不足",
                        "是", "否");
                    if (switchTo2)
                    {
                        _tRenameForced = true;
                        TRename1Radio.IsEnabled = false;   // 复制选项变灰
                        TRenameCard1.Opacity = 0.45;
                        TRename2Radio.IsChecked = true;    // 选中“直接重命名原文件”
                        T2NoteText.Text = "磁盘空间不足，已自动切换为直接重命名原文件。";
                    }
                    return; // 不进入下一页
                }
            }
            ShowTimelapseStep(3);
        }

        #endregion

        #region 第3步：输出格式

        private void TFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TFormatHintText == null) return;
            if (TFormatComboBox.SelectedItem is ComboBoxItem item)
                TFormatHintText.Text = $"输出扩展名: .{item.Content}";
        }

        private void T3Back_Click(object sender, RoutedEventArgs e) => ShowTimelapseStep(2);

        private void T3Next_Click(object sender, RoutedEventArgs e)
        {
            if (TFormatComboBox.SelectedItem == null)
            {
                ShowMessage("请选择输出格式。");
                return;
            }
            ShowTimelapseStep(4);
        }

        #endregion

        #region 第4步：视频参数

        private void TResSame_Changed(object sender, RoutedEventArgs e)
        {
            if (TResPanel == null) return;
            bool same = TResSameCheckBox.IsChecked == true;
            TResPanel.Visibility = same ? Visibility.Collapsed : Visibility.Visible;
            TResPanel.IsEnabled = !same;
        }

        private void TResolutionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TCustomResolutionPanel == null) return;
            var selected = (TResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            TCustomResolutionPanel.Visibility = selected == "自定义" ? Visibility.Visible : Visibility.Collapsed;
            SetTRecommendedBitrate(selected);
        }

        private void TCustomResolution_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TCustomResolutionPanel.Visibility != Visibility.Visible) return;
            if (int.TryParse(TCustomResolutionWidthTextBox.Text, out var w) &&
                int.TryParse(TCustomResolutionHeightTextBox.Text, out var h))
            {
                TBitrateTextBox.Text = EstimateBitrate(w, h).ToString();
            }
        }

        private void SetTRecommendedBitrate(string? selected)
        {
            if (selected == null || selected.StartsWith("自定义"))
            {
                if (selected != null && _tFirstW > 0)
                    TBitrateTextBox.Text = EstimateBitrate(_tFirstW, _tFirstH).ToString();
                return;
            }

            var sizePart = selected.Split(' ')[0];
            if (ResolutionBitrateMap.TryGetValue(sizePart, out var bitrate))
            {
                TBitrateTextBox.Text = bitrate.ToString();
            }
            else if (sizePart.Split('x') is { Length: 2 } parts &&
                     int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
            {
                TBitrateTextBox.Text = EstimateBitrate(w, h).ToString();
            }
            else if (_tFirstW > 0)
            {
                TBitrateTextBox.Text = EstimateBitrate(_tFirstW, _tFirstH).ToString();
            }
        }

        private static int EstimateBitrate(int w, int h) => (int)Math.Round(w * h * 0.0025);

        private void UpdateTResInfo()
        {
            TResInfoText.Text = _tFirstW > 0 ? $"图片分辨率：{_tFirstW}×{_tFirstH}" : "";
        }

        private void T4Back_Click(object sender, RoutedEventArgs e) => ShowTimelapseStep(3);

        private void T4Next_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TFpsTextBox.Text, out var fps) || fps < 1 || fps > 240)
            {
                ShowMessage("请填写合法的帧率（1-240）。");
                return;
            }

            if (TResSameCheckBox.IsChecked != true)
            {
                var selected = (TResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (selected == null)
                {
                    ShowMessage("请选择分辨率，或勾选“与图片分辨率相同”。");
                    return;
                }
                if (selected == "自定义" &&
                    (!int.TryParse(TCustomResolutionWidthTextBox.Text, out var w) || w <= 0 ||
                     !int.TryParse(TCustomResolutionHeightTextBox.Text, out var h) || h <= 0))
                {
                    ShowMessage("请在自定义分辨率中填写宽和高（正整数）。");
                    return;
                }
            }

            ShowTimelapseStep(5);
        }

        #endregion

        #region 第5步：码率

        private void T5Back_Click(object sender, RoutedEventArgs e) => ShowTimelapseStep(4);

        private void T5Next_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TBitrateTextBox.Text, out var br) || br < 100 || br > 1_000_000)
            {
                ShowMessage("请填写合法的码率（100-1000000 kbps）。");
                return;
            }
            BuildTimelapseSummary();
            ShowTimelapseStep(6);
        }

        private void BuildTimelapseSummary()
        {
            string fmt = ((ComboBoxItem)TFormatComboBox.SelectedItem)?.Content?.ToString() ?? "未选择";
            string codec = ((ComboBoxItem)TCodecComboBox.SelectedItem)?.Content?.ToString() ?? "未选择";

            string res = TResSameCheckBox.IsChecked == true
                ? (_tFirstW > 0 ? $"与图片相同（{_tFirstW}×{_tFirstH}）" : "与图片相同")
                : ((TResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() == "自定义"
                    ? $"{TCustomResolutionWidthTextBox.Text}×{TCustomResolutionHeightTextBox.Text}"
                    : (TResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "未选择");

            string mode = TRenameMode == 1 ? "复制到 rename 文件夹" : "直接重命名原文件";
            string renameDir = TRenameMode == 1 ? Path.Combine(_tFolder, "rename") : _tFolder;

            string duration = TimeSpan.FromSeconds(_tFiles.Count / Math.Max(1,
                int.TryParse(TFpsTextBox.Text, out var summaryFps) ? summaryFps : 30)).ToString("hh\\:mm\\:ss");
            TSummaryTextBlock.Text =
                $"图片文件夹：{_tFolder}\n" +
                $"图片数量：{_tFiles.Count} 张 ｜ 重命名方式：{mode}\n" +
                $"重命名目录：{renameDir}\n" +
                $"输出格式：{fmt} ｜ 视频编码：{codec} ｜ 码率：{TBitrateTextBox.Text} kbps\n" +
                $"分辨率：{res} ｜ 帧率：{TFpsTextBox.Text} fps ｜ 预计时长：{duration}";
        }

        #endregion

        #region 第6步：合成

        private void ResetTimelapseProgressUi()
        {
            TRenameProgressBar.Visibility = Visibility.Collapsed;
            TRenameProgressText.Visibility = Visibility.Collapsed;
            TRenameProgressBar.Value = 0;
            TSynthProgressBar.Visibility = Visibility.Collapsed;
            TSynthProgressText.Visibility = Visibility.Collapsed;
            TSynthProgressBar.Value = 0;
        }

        private void T6Back_Click(object sender, RoutedEventArgs e)
        {
            if (_tBusy) return;
            ShowTimelapseStep(5);
        }

        private async void TStart_Click(object sender, RoutedEventArgs e)
        {
            if (_tBusy) return;

            if (!int.TryParse(TFpsTextBox.Text, out var fps) || fps < 1 || fps > 240)
            {
                ShowMessage("请填写合法的帧率（1-240）。");
                return;
            }
            if (!int.TryParse(TBitrateTextBox.Text, out var bitrate) || bitrate < 100)
            {
                ShowMessage("请填写合法的码率。");
                return;
            }
            if (TFormatComboBox.SelectedItem == null)
            {
                ShowMessage("请选择输出格式。");
                return;
            }
            var format = ((ComboBoxItem)TFormatComboBox.SelectedItem).Content?.ToString() ?? "mp4";

            var saveDlg = new SaveFileDialogEx
            {
                InitialDirectory = GetWritableSaveDir(Directory.Exists(_tFolder) ? _tFolder : null),
                FileName = $"timelapse_{DateTime.Now:yyyyMMdd_HHmmss}.{format}",
                Filter = $"{format.ToUpper()}文件|*.{format}"
            };
            string? outputPath = saveDlg.ShowDialog(this);
            if (saveDlg.Diagnostics.Count > 0)
                TLogTextBox.AppendText("对话框诊断: " + string.Join("; ", saveDlg.Diagnostics) + Environment.NewLine);
            if (outputPath == null) return;

            _tBusy = true;
            TStartButton.IsEnabled = false;

            _activeProgressBar = TSynthProgressBar;
            _activeProgressText = TSynthProgressText;
            _activeLogBox = TLogTextBox;
            TLogTextBox.Clear();
            ResetTimelapseProgressUi();

            try
            {
                // ---------- 阶段1：重命名（独立进度条） ----------
                TRenameProgressBar.Visibility = Visibility.Visible;
                TRenameProgressText.Visibility = Visibility.Visible;
                AppendLog($"开始重命名 {_tFiles.Count} 张图片（{(TRenameMode == 1 ? "复制到 rename 文件夹" : "就地重命名")}）…");

                var renameProgress = new Progress<(int done, int total, string file)>(p =>
                {
                    double pct = p.done * 100.0 / Math.Max(1, p.total);
                    TRenameProgressBar.Value = pct;
                    TRenameProgressText.Text = $"{pct:0}%  ·  {p.done}/{p.total}  ·  {p.file}";
                });
                IProgress<(int done, int total, string file)> report = renameProgress;

                RenameResult? renameResult = null;
                string renameError = "";
                int mode = TRenameMode;
                var files = _tFiles.ToList();
                string folder = _tFolder;

                await Task.Run(() =>
                {
                    try
                    {
                        renameResult = mode == 1
                            ? RenameEngine.CopyToRenameDir(folder, files, (d, t, f) => report.Report((d, t, f)))
                            : RenameEngine.RenameInPlace(folder, files, (d, t, f) => report.Report((d, t, f)));
                    }
                    catch (UnauthorizedAccessException)
                    {
                        renameError = $"没有权限写入文件夹：{folder}\n请确认文件夹未被占用，或以管理员身份运行。";
                    }
                    catch (Exception ex)
                    {
                        renameError = ex.Message;
                    }
                });

                if (renameResult == null)
                {
                    TRenameProgressBar.Value = 0;
                    TRenameProgressText.Text = "重命名失败";
                    AppendLog($"重命名失败：{renameError}");
                    ShowMessage($"重命名失败：{renameError}");
                    return;
                }

                TRenameProgressBar.Value = 100;
                TRenameProgressText.Text = $"已完成（{renameResult.Count} 张）";
                AppendLog($"重命名完成：{renameResult.WorkingDir}");

                // ---------- 阶段2：合成视频（独立进度条） ----------
                TSynthProgressBar.Visibility = Visibility.Visible;
                TSynthProgressText.Visibility = Visibility.Visible;

                string codec = (((ComboBoxItem)TCodecComboBox.SelectedItem)?.Content?.ToString() ?? "libx264")
                    .Replace(" (常用)", "").Trim();

                var argsList = new List<string>
                {
                    $"-framerate {fps}",
                    $"-start_number 1",
                    $"-i \"{Path.Combine(renameResult.WorkingDir, "%0" + renameResult.Pad + "d" + renameResult.FirstExtension)}\""
                };

                // 分辨率
                bool resSame = TResSameCheckBox.IsChecked == true;
                if (resSame)
                {
                    if (_tFirstW > 0 && (_tFirstW % 2 != 0 || _tFirstH % 2 != 0))
                        argsList.Add("-vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\""); // yuv420p 需要偶数尺寸
                }
                else
                {
                    var selected = (TResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                    if (selected == "自定义" &&
                        int.TryParse(TCustomResolutionWidthTextBox.Text, out var cw) &&
                        int.TryParse(TCustomResolutionHeightTextBox.Text, out var ch))
                    {
                        argsList.Add($"-vf \"scale={cw}:{ch}\"");
                    }
                    else if (selected != null)
                    {
                        var parts = selected.Split(' ')[0].Split('x');
                        argsList.Add($"-vf \"scale={parts[0]}:{parts[1]}\"");
                    }
                }

                argsList.Add($"-c:v {codec}");
                argsList.Add($"-b:v {bitrate}k");
                if (!format.Equals("gif", StringComparison.OrdinalIgnoreCase))
                    argsList.Add("-pix_fmt yuv420p");
                argsList.Add($"\"{outputPath}\"");

                var args = "-y -hide_banner -nostdin -nostats -progress pipe:1 " + string.Join(" ", argsList);
                AppendLog($"开始合成视频（{fps} fps × {_tFiles.Count} 帧）…");

                // 图片序列没有输入时长：用 帧数/帧率 预估总时长驱动进度条
                _expectedDuration = TimeSpan.FromSeconds(_tFiles.Count / (double)fps);
                bool ok = await RunFfmpegAsync(args);

                if (ok)
                {
                    TSynthProgressBar.Value = 100;
                    TSynthProgressText.Text = "已完成 100%";
                    AppendLog($"视频已生成：{outputPath}");

                    // 用默认播放器播放
                    TryOpenWithDefaultPlayer(outputPath);

                    // 复制模式创建了 rename 文件夹：确认无误后删除
                    if (renameResult.RenameDir != null)
                    {
                        bool confirmed = ShowConfirm(
                            "视频已生成并尝试用默认播放器打开。\n\n确认效果无误后，是否删除 rename 文件夹及其中的所有文件？",
                            "删除确认",
                            "删除", "保留");
                        if (confirmed)
                        {
                            try
                            {
                                Directory.Delete(renameResult.RenameDir, recursive: true);
                                AppendLog("已删除 rename 文件夹及其中所有文件。");
                            }
                            catch (Exception ex)
                            {
                                AppendLog($"删除 rename 文件夹失败：{ex.Message}");
                                ShowMessage($"删除 rename 文件夹失败：{ex.Message}");
                            }
                        }
                        else
                        {
                            AppendLog($"已保留 rename 文件夹：{renameResult.RenameDir}");
                        }
                    }
                    else
                    {
                        AppendLog("已使用默认播放器打开视频，请查看效果。");
                    }
                }
                else
                {
                    TSynthProgressBar.Value = 0;
                    TSynthProgressText.Text = "合成失败";
                    AppendLog("合成失败，详情请查看上方日志。");
                }
            }
            finally
            {
                _tBusy = false;
                TStartButton.IsEnabled = true;
            }
        }

        #endregion

        #region 公共工具

        /// <summary>用系统默认程序打开文件（播放视频）。</summary>
        private void TryOpenWithDefaultPlayer(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppendLog($"无法用默认播放器打开视频：{ex.Message}");
            }
        }

        internal static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return $"{v:0.#} {units[u]}";
        }

        /// <summary>用 WPF 解码器尝试读取图片尺寸（失败返回 0,0）。</summary>
        internal static (int w, int h) TryGetImageSize(string file)
        {
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                    fs, System.Windows.Media.Imaging.BitmapCreateOptions.None,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                var frame = decoder.Frames.FirstOrDefault();
                return frame == null ? (0, 0) : (frame.PixelWidth, frame.PixelHeight);
            }
            catch
            {
                return (0, 0);
            }
        }

        #endregion
    }
}
