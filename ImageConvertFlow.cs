using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Vchange
{
    /// <summary>
    /// 图片格式转换流程：选择图片 → 输出格式 → 品质与选项（品质/压缩/交错/分辨率）→ 输出与日志。
    /// </summary>
    public partial class MainWindow
    {
        private string _cSource = "";
        private int _cFirstW, _cFirstH;
        private bool _cBusy;

        #region 第1步：选择图片文件

        private async void C1Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择图片文件",
                Filter = "图片文件|*.jpg;*.jpeg;*.jpe;*.png;*.bmp;*.tif;*.tiff;*.gif;*.wdp;*.jxr;*.webp;*.dng|所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            _cSource = dlg.FileName;
            CSourceTextBox.Text = _cSource;

            _cFirstW = _cFirstH = 0;
            (_cFirstW, _cFirstH) = await Task.Run(() => TryGetImageSize(_cSource));

            C1InfoText.Text = _cFirstW > 0
                ? $"原图分辨率：{_cFirstW}×{_cFirstH}"
                : "已选择文件（无法预读尺寸）";
        }

        private void C1Next_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_cSource) || !File.Exists(_cSource))
            {
                ShowMessage("请先选择有效的图片文件。");
                return;
            }
            ShowImageConvertStep(2);
        }

        #endregion

        #region 第2步：输出格式

        private void CFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CFormatHintText == null || CQualityCard == null || CTiffCard == null || CPngCard == null) return;
            var fmt = GetCFormat();
            CQualityCard.Visibility = fmt == "jpg" ? Visibility.Visible : Visibility.Collapsed;
            CTiffCard.Visibility = fmt == "tiff" ? Visibility.Visible : Visibility.Collapsed;
            CPngCard.Visibility = fmt == "png" ? Visibility.Visible : Visibility.Collapsed;
            CFormatHintText.Text = fmt switch
            {
                "jpg" => "有损压缩，可在下一步设置品质",
                "png" => "无损压缩，支持透明，可设交错",
                "bmp" => "无压缩位图，兼容性最好",
                "tiff" => "通用无损格式，可设压缩算法，支持多页",
                "webp" => "Google 高效压缩格式，可在下一步设置品质",
                "wmp" => "HD Photo / JPEG XR 高效压缩",
                "dng" => "Adobe 数字底片（16-bit 线性 DNG，仅第一帧）",
                _ => ""
            };
        }

        private string GetCFormat() =>
            ((CFormatComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "JPG").ToLowerInvariant();

        private void C2Back_Click(object sender, RoutedEventArgs e) => ShowImageConvertStep(1);

        private void C2Next_Click(object sender, RoutedEventArgs e)
        {
            if (CFormatComboBox.SelectedItem == null)
            {
                ShowMessage("请选择输出格式。");
                return;
            }
            ShowImageConvertStep(3);
        }

        #endregion

        #region 第3步：品质与选项

        private void CQualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (CQualityValueText != null)
                CQualityValueText.Text = ((int)e.NewValue).ToString();
        }

        private void CResSame_Changed(object sender, RoutedEventArgs e)
        {
            if (CResPanel == null) return;
            bool same = CResSameCheckBox.IsChecked == true;
            CResPanel.Visibility = same ? Visibility.Collapsed : Visibility.Visible;
            CResPanel.IsEnabled = !same;
        }

        private void CResolutionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CCustomResolutionPanel == null) return;
            var selected = (CResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            CCustomResolutionPanel.Visibility = selected == "自定义" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void C3Back_Click(object sender, RoutedEventArgs e) => ShowImageConvertStep(2);

        private void C3Next_Click(object sender, RoutedEventArgs e)
        {
            if (CResSameCheckBox.IsChecked != true)
            {
                var selected = (CResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (selected == null)
                {
                    ShowMessage("请选择分辨率，或勾选“与原图相同”。");
                    return;
                }
                if (selected == "自定义" &&
                    (!int.TryParse(CCustomResolutionWidthTextBox.Text, out var w) || w <= 0 ||
                     !int.TryParse(CCustomResolutionHeightTextBox.Text, out var h) || h <= 0))
                {
                    ShowMessage("请在自定义分辨率中填写宽和高（正整数）。");
                    return;
                }
            }
            ShowImageConvertStep(4);
            BuildConvertSummary();
        }

        private void BuildConvertSummary()
        {
            string fmt = GetCFormat();
            string res = CResSameCheckBox.IsChecked == true
                ? (_cFirstW > 0 ? $"与原图相同（{_cFirstW}×{_cFirstH}）" : "与原图相同")
                : ((CResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() == "自定义"
                    ? $"{CCustomResolutionWidthTextBox.Text}×{CCustomResolutionHeightTextBox.Text}"
                    : (CResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "未选择");

            string csName = CColorSpaceCombo.SelectedIndex switch
            {
                0 => "与原图一致",
                2 => "sRGB",
                3 => "Adobe RGB（1998）",
                4 => "Display P3（苹果广色域）",
                5 => "ProPhoto RGB（16-bit PNG）",
                6 => "ACEScg（线性，16-bit PNG）",
                8 => "HDR10（Rec.2100 PQ，16-bit PNG）",
                9 => "HLG（Rec.2100 HLG，16-bit PNG）",
                _ => "与原图一致"
            };
            string extra = fmt switch
            {
                "jpg" => $"品质 {(int)CQualitySlider.Value}",
                "webp" => $"品质 {(int)CQualitySlider.Value}",
                "tiff" => "压缩 " + ((CTiffCompressionCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "LZW"),
                "png" => "交错 " + (CPngInterlaceCombo != null && CPngInterlaceCombo.SelectedIndex == 1 ? "开启" : "关闭"),
                _ => "无附加选项"
            };

            CSummaryTextBlock.Text =
                $"源文件：{_cSource}\n" +
                $"原图分辨率：{_cFirstW}×{_cFirstH}\n" +
                $"输出格式：{fmt.ToUpper()}（{extra}）\n" +
                $"色彩空间：{csName}\n" +
                $"输出分辨率：{res}";
        }

        #endregion

        #region 第4步：输出与日志

        private void C4Back_Click(object sender, RoutedEventArgs e)
        {
            if (_cBusy) return;
            ShowImageConvertStep(3);
        }

        private async void CStart_Click(object sender, RoutedEventArgs e)
        {
            if (_cBusy) return;
            if (string.IsNullOrWhiteSpace(_cSource) || !File.Exists(_cSource))
            {
                ShowMessage("请先选择有效的图片文件。");
                return;
            }

            string fmt = GetCFormat();

            int width = 0, height = 0;
            if (CResSameCheckBox.IsChecked != true)
            {
                var selected = (CResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (selected == "自定义")
                {
                    int.TryParse(CCustomResolutionWidthTextBox.Text, out width);
                    int.TryParse(CCustomResolutionHeightTextBox.Text, out height);
                }
                else if (selected != null)
                {
                    var parts = selected.Split(' ')[0].Split('x');
                    int.TryParse(parts[0], out width);
                    int.TryParse(parts[1], out height);
                }
            }

            bool force16Png = CColorSpaceCombo.SelectedIndex is 5 or 6 or 8 or 9;
            if (force16Png && fmt != "png")
                fmt = "png"; // 广色域/HDR 以 16-bit PNG 输出

            var saveDlg = new SaveFileDialog
            {
                FileName = Path.GetFileNameWithoutExtension(_cSource) + "_converted." + fmt,
                Filter = fmt.ToUpper() + $"文件|*.{fmt}"
            };
            if (saveDlg.ShowDialog() != true) return;
            string outputPath = saveDlg.FileName;

            _cBusy = true;
            CStartButton.IsEnabled = false;

            _activeProgressBar = CProgressBar;
            _activeProgressText = CProgressTextBlock;
            _activeLogBox = CLogTextBox;
            CLogTextBox.Clear();
            CProgressBar.IsIndeterminate = false;
            CProgressBar.Visibility = Visibility.Visible;
            CProgressTextBlock.Visibility = Visibility.Visible;
            CProgressBar.Value = 0;
            CProgressTextBlock.Text = "0%";

            var options = new ImageConvertOptions
            {
                SourcePath = _cSource,
                OutputPath = outputPath,
                Format = fmt,
                Quality = (int)CQualitySlider.Value,
                TiffCompression = CTiffCompressionCombo.SelectedIndex switch
                {
                    1 => "none",
                    2 => "zip",
                    3 => "rle",
                    4 => "ccitt4",
                    _ => "lzw"
                },
                PngInterlace = CPngInterlaceCombo != null && CPngInterlaceCombo.SelectedIndex == 1,
                ColorSpace = CColorSpaceCombo.SelectedIndex switch
                {
                    2 => "srgb",
                    3 => "adobergb",
                    4 => "displayp3",
                    5 => "prophoto",
                    6 => "acescg",
                    8 => "rec2020pq",
                    9 => "rec2020hlg",
                    _ => "same"
                },
                Width = width,
                Height = height
            };

            AppendLog($"开始转换：{Path.GetFileName(_cSource)} → {fmt.ToUpper()}");

            var progress = new Progress<ImageConvertProgress>(p =>
            {
                if (p.FrameCount > 1)
                {
                    double pct = p.Frame * 100.0 / Math.Max(1, p.FrameCount);
                    CProgressBar.Value = pct;
                    CProgressTextBlock.Text = $"{pct:0}%  ·  {p.Stage} {p.Frame}/{p.FrameCount} 帧";
                }
                else
                {
                    CProgressTextBlock.Text = p.Stage + "…";
                }
            });

            var result = await ImageConverter.ConvertAsync(options, progress);

            if (result.Success)
            {
                CProgressBar.Value = 100;
                CProgressTextBlock.Text = "已完成 100%";
                AppendLog($"转换完成：{outputPath}" +
                    (result.FrameCount > 1 ? $"（共 {result.FrameCount} 帧）" : ""));
                if (result.Warning != null)
                    AppendLog("注意：" + result.Warning);
                TryOpenWithDefaultPlayer(outputPath);
            }
            else
            {
                CProgressBar.Value = 0;
                CProgressTextBlock.Text = "转换失败";
                AppendLog($"转换失败：{result.Error}");
                ShowMessage($"转换失败：{result.Error}");
            }

            _cBusy = false;
            CStartButton.IsEnabled = true;
        }

        #endregion
    }
}
