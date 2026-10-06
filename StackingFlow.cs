using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Vchange
{
    /// <summary>
    /// 图片堆砌流程：文件夹（星轨/流水/车流长曝光堆砌）→ 输出格式 → 品质与分辨率 → 输出与日志。
    /// 输出为不含元数据的纯净图片。
    /// </summary>
    public partial class MainWindow
    {
        private string _sFolder = "";
        private List<string> _sFiles = new();
        private int _sFirstW, _sFirstH;
        private bool _sBusy;

        #region 第1步：选择文件夹与堆砌模式

        private async void S1Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "选择图片文件夹" };
            if (Directory.Exists(_sFolder)) dlg.InitialDirectory = _sFolder;
            if (dlg.ShowDialog() != true) return;

            _sFolder = dlg.FolderName;
            SFolderTextBox.Text = _sFolder;

            _sFiles = RenameEngine.ListImages(_sFolder);
            _sFirstW = _sFirstH = 0;
            if (_sFiles.Count > 0)
                (_sFirstW, _sFirstH) = await Task.Run(() => TryGetImageSize(_sFiles[0]));

            S1InfoText.Text = _sFiles.Count == 0
                ? "该文件夹中没有找到图片文件"
                : _sFirstW > 0
                    ? $"共 {_sFiles.Count} 张图片 · 首张分辨率 {_sFirstW}×{_sFirstH}（输出将以此为基准）"
                    : $"共 {_sFiles.Count} 张图片";
        }

        private void SModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 模式在开始堆砌时读取，此处无需处理
        }

        private void S1Next_Click(object sender, RoutedEventArgs e)
        {
            if (_sFiles.Count == 0)
            {
                ShowMessage("请选择包含图片文件的文件夹。");
                return;
            }
            ShowStackStep(2);
        }

        #endregion

        #region 第2步：输出格式

        private void SFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SFormatHintText == null || SQualityCard == null) return;
            var fmt = GetSFormat();
            bool isJpg = fmt == "jpg";
            SQualityCard.Visibility = isJpg ? Visibility.Visible : Visibility.Collapsed;
            SFormatHintText.Text = fmt switch
            {
                "jpg" => "JPEG 格式可在下一步设置品质",
                "png" => "无损格式，文件较大，支持透明",
                "bmp" => "无压缩位图，兼容性最好",
                "tiff" => "通用无损格式，适合后期处理",
                "dng" => "Adobe 数字底片（16-bit 线性 DNG）",
                _ => ""
            };
        }

        /// <summary>当前选择的输出格式（小写）。</summary>
        private string GetSFormat() =>
            ((SFormatComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "JPG").ToLowerInvariant();

        private void S2Back_Click(object sender, RoutedEventArgs e) => ShowStackStep(1);

        private void S2Next_Click(object sender, RoutedEventArgs e)
        {
            if (SFormatComboBox.SelectedItem == null)
            {
                ShowMessage("请选择输出格式。");
                return;
            }
            ShowStackStep(3);
        }

        #endregion

        #region 第3步：品质与分辨率

        private void SQualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SQualityValueText != null)
                SQualityValueText.Text = ((int)e.NewValue).ToString();
        }

        private void SResSame_Changed(object sender, RoutedEventArgs e)
        {
            if (SResPanel == null) return;
            bool same = SResSameCheckBox.IsChecked == true;
            SResPanel.Visibility = same ? Visibility.Collapsed : Visibility.Visible;
            SResPanel.IsEnabled = !same;
        }

        private void SResolutionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SCustomResolutionPanel == null) return;
            var selected = (SResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            SCustomResolutionPanel.Visibility = selected == "自定义" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void S3Back_Click(object sender, RoutedEventArgs e) => ShowStackStep(2);

        private void S3Next_Click(object sender, RoutedEventArgs e)
        {
            if (SResSameCheckBox.IsChecked != true)
            {
                var selected = (SResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (selected == null)
                {
                    ShowMessage("请选择分辨率，或勾选“与图片分辨率相同”。");
                    return;
                }
                if (selected == "自定义" &&
                    (!int.TryParse(SCustomResolutionWidthTextBox.Text, out var w) || w <= 0 ||
                     !int.TryParse(SCustomResolutionHeightTextBox.Text, out var h) || h <= 0))
                {
                    ShowMessage("请在自定义分辨率中填写宽和高（正整数）。");
                    return;
                }
            }
            ShowStackStep(4);
            BuildStackSummary();
        }

        #endregion

        #region 第4步：输出与日志

        private void BuildStackSummary()
        {
            string fmt = GetSFormat();
            string mode = (SModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "最大值堆砌";
            string res = SResSameCheckBox.IsChecked == true
                ? (_sFirstW > 0 ? $"与图片相同（{_sFirstW}×{_sFirstH}）" : "与图片相同")
                : ((SResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() == "自定义"
                    ? $"{SCustomResolutionWidthTextBox.Text}×{SCustomResolutionHeightTextBox.Text}"
                    : (SResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "未选择");

            SSummaryTextBlock.Text =
                $"图片文件夹：{_sFolder}\n" +
                $"图片数量：{_sFiles.Count} 张\n" +
                $"堆砌模式：{mode}\n" +
                $"输出格式：{fmt.ToUpper()}" + (fmt == "jpg" ? $"（品质 {(int)SQualitySlider.Value}）" : "") + "\n" +
                $"分辨率：{res}";
        }

        private void S4Back_Click(object sender, RoutedEventArgs e)
        {
            if (_sBusy) return;
            ShowStackStep(3);
        }

        private async void SStart_Click(object sender, RoutedEventArgs e)
        {
            if (_sBusy) return;
            if (_sFiles.Count == 0)
            {
                ShowMessage("请先选择包含图片的文件夹。");
                return;
            }

            string fmt = GetSFormat();

            int width = _sFirstW, height = _sFirstH;
            if (SResSameCheckBox.IsChecked != true)
            {
                var selected = (SResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (selected == "自定义")
                {
                    int.TryParse(SCustomResolutionWidthTextBox.Text, out width);
                    int.TryParse(SCustomResolutionHeightTextBox.Text, out height);
                }
                else if (selected != null)
                {
                    var parts = selected.Split(' ')[0].Split('x');
                    int.TryParse(parts[0], out width);
                    int.TryParse(parts[1], out height);
                }
            }
            if (width <= 0 || height <= 0)
            {
                ShowMessage("无法确定输出分辨率，请手动选择。");
                return;
            }

            string mode = SModeComboBox.SelectedIndex switch
            {
                1 => "avg",
                2 => "min",
                _ => "max"
            };

            var saveDlg = new SaveFileDialogEx
            {
                InitialDirectory = GetWritableSaveDir(Directory.Exists(_sFolder) ? _sFolder : null),
                FileName = $"stacked_{DateTime.Now:yyyyMMdd_HHmmss}.{fmt}",
                Filter = fmt.ToUpper() + $"文件|*.{fmt}"
            };
            string? outputPath = saveDlg.ShowDialog(this)
            if (saveDlg.Diagnostics.Count > 0)
             SLogTextBox.AppendText("对话框诊断: " + string.Join("; ", saveDlg.Diagnostics) + Environment.NewLine);;
            if (outputPath == null) return;
            string outPath = outputPath;

            _sBusy = true;
            SStartButton.IsEnabled = false;

            _activeProgressBar = SProgressBar;
            _activeProgressText = SProgressTextBlock;
            _activeLogBox = SLogTextBox;
            SLogTextBox.Clear();
            SProgressBar.Visibility = Visibility.Visible;
            SProgressTextBlock.Visibility = Visibility.Visible;
            SProgressBar.Value = 0;
            SProgressTextBlock.Text = "0%";

            var options = new StackOptions
            {
                Files = _sFiles,
                Width = width,
                Height = height,
                Mode = mode,
                Format = fmt,
                Quality = (int)SQualitySlider.Value,
                OutputPath = outputPath
            };

            AppendLog($"开始堆砌：{_sFiles.Count} 张图片 → {width}×{height} {fmt.ToUpper()}（{mode}）");

            var progress = new Progress<StackProgress>(p =>
            {
                if (p.Stage == "encode")
                {
                    SProgressTextBlock.Text = "正在编码输出…";
                    return;
                }
                double pct = p.Done * 100.0 / Math.Max(1, p.Total);
                SProgressBar.Value = pct;
                SProgressTextBlock.Text = $"{pct:0}%  ·  {p.Done}/{p.Total}  ·  {p.CurrentFile}";
            });

            var result = await ImageStacker.StackAsync(options, progress);

            if (result.Success)
            {
                SProgressBar.Value = 100;
                SProgressTextBlock.Text = "已完成 100%";
                AppendLog($"堆砌完成：{outputPath}");
                TryOpenWithDefaultPlayer(outputPath);
            }
            else
            {
                SProgressBar.Value = 0;
                SProgressTextBlock.Text = "堆砌失败";
                AppendLog($"堆砌失败：{result.Error}");
                var saveErr = result.Error ?? "";
                ShowMessage(saveErr.Contains("权限") || saveErr.Contains("denied", StringComparison.OrdinalIgnoreCase)
                    ? saveErr + "\n请换一个保存位置（例如“下载”或“文档”文件夹）后重试。"
                    : $"堆砌失败：{saveErr}");
            }

            _sBusy = false;
            SStartButton.IsEnabled = true;
        }

        #endregion
    }
}
