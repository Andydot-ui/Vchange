using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Vchange
{
    public class StackProgress
    {
        public int Done;
        public int Total;
        public string CurrentFile = "";
        /// <summary>decode（逐张堆砌）或 encode（编码输出）。</summary>
        public string Stage = "decode";
    }

    public class StackOptions
    {
        public IReadOnlyList<string> Files = Array.Empty<string>();
        public int Width;
        public int Height;
        /// <summary>max（最大值，星轨/车流）、avg（平均值，流水/降噪）、min（最小值）。</summary>
        public string Mode = "max";
        /// <summary>jpg / png / bmp / tiff / dng。</summary>
        public string Format = "jpg";
        /// <summary>JPEG 品质 1-100。</summary>
        public int Quality = 90;
        public string OutputPath = "";
    }

    public class StackResult
    {
        public bool Success;
        public string? Error;
        public List<string> Warnings = new();
    }

    /// <summary>
    /// 长曝光堆砌引擎：把文件夹中的所有图片按指定混合方式（最大值/平均/最小值）
    /// 合成为一张不含元数据的图片。解码优先使用 WPF 内置解码器（jpg/png/bmp/tiff/gif），
    /// 不支持的格式回退到 ffmpeg 解码。
    /// </summary>
    public static class ImageStacker
    {
        public static async Task<StackResult> StackAsync(StackOptions o, IProgress<StackProgress> progress,
            CancellationToken ct = default)
        {
            return await Task.Run(async () =>
            {
                var result = new StackResult();
                try
                {
                    int w = o.Width, h = o.Height;
                    int pixels = w * h;
                    var files = o.Files;

                    byte[] acc = new byte[pixels * 4];
                    uint[]? sum = null;
                    if (o.Mode == "avg")
                    {
                        sum = new uint[pixels * 4];
                    }
                    else if (o.Mode == "min")
                    {
                        Array.Fill(acc, (byte)255);
                    }
                    // max：初始为 0

                    var buffer = new byte[pixels * 4];

                    for (int i = 0; i < files.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Report(new StackProgress
                        {
                            Done = i, Total = files.Count,
                            CurrentFile = Path.GetFileName(files[i]), Stage = "decode"
                        });

                        await DecodeToBgraAsync(files[i], w, h, buffer);
                        Blend(o.Mode, acc, sum, buffer);
                    }

                    progress?.Report(new StackProgress
                    {
                        Done = files.Count, Total = files.Count, CurrentFile = "", Stage = "encode"
                    });

                    // 平均值归一化
                    if (sum != null)
                    {
                        int n = files.Count;
                        for (int p = 0; p < acc.Length; p++)
                            acc[p] = (byte)Math.Min(255, sum[p] / (uint)n);
                    }

                    EncodeOutput(o.OutputPath, o.Format, acc, w, h, o.Quality);

                    result.Success = true;
                    return result;
                }
                catch (OperationCanceledException)
                {
                    result.Error = "已取消";
                    return result;
                }
                catch (UnauthorizedAccessException)
                {
                    result.Error = $"没有权限写入输出位置：{o.OutputPath}\n请换一个保存位置（例如“下载”或“文档”文件夹）后重试。";
                    return result;
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                    return result;
                }
            }, ct);
        }

        /// <summary>把一张图片解码为 W×H 的 BGRA32 缓冲。</summary>
        private static async Task DecodeToBgraAsync(string file, int w, int h, byte[] buffer)
        {
            // 优先 WPF 内置解码器
            try
            {
                DecodeWithWpf(file, w, h, buffer);
                return;
            }
            catch
            {
                // 回退 ffmpeg
            }
            await DecodeWithFfmpegAsync(file, w, h, buffer);
        }

        private static void DecodeWithWpf(string file, int w, int h, byte[] buffer)
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault() ?? throw new InvalidDataException("无可解码帧");

            BitmapSource src = frame;
            if (frame.PixelWidth != w || frame.PixelHeight != h)
            {
                src = new TransformedBitmap(frame,
                    new ScaleTransform(w / (double)frame.PixelWidth, h / (double)frame.PixelHeight));
            }

            var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            conv.CopyPixels(buffer, w * 4, 0);
        }

        private static async Task DecodeWithFfmpegAsync(string file, int w, int h, byte[] buffer)
        {
            int expected = w * h * 4;
            var psi = new ProcessStartInfo
            {
                FileName = FfmpegProvider.GetPath(),
                Arguments = $"-y -hide_banner -nostdin -nostats -i \"{file}\" -vf scale={w}:{h} -f rawvideo -pix_fmt bgra pipe:1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            _ = process.StandardError.ReadToEndAsync(); // 防止 stderr 缓冲塞满导致阻塞

            var ms = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(ms);
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new InvalidDataException($"ffmpeg 解码失败: {Path.GetFileName(file)}");
            if (ms.Length < expected)
                throw new InvalidDataException($"ffmpeg 输出不完整: {Path.GetFileName(file)}");

            Array.Copy(ms.GetBuffer(), buffer, expected);
        }

        private static void Blend(string mode, byte[] acc, uint[]? sum, byte[] buf)
        {
            if (mode == "avg")
            {
                for (int i = 0; i < acc.Length; i++) sum![i] += buf[i];
            }
            else if (mode == "min")
            {
                for (int i = 0; i < acc.Length; i++)
                    if (buf[i] < acc[i]) acc[i] = buf[i];
            }
            else // max
            {
                for (int i = 0; i < acc.Length; i++)
                    if (buf[i] > acc[i]) acc[i] = buf[i];
            }
        }

        private static void EncodeOutput(string path, string format, byte[] acc, int w, int h, int quality)
        {
            switch (format)
            {
                case "jpg":
                {
                    var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, acc, w * 4);
                    var enc = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
                    enc.Frames.Add(BitmapFrame.Create(src));
                    using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
                    enc.Save(fs);
                    break;
                }
                case "png":
                {
                    var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, acc, w * 4);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(src));
                    using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
                    enc.Save(fs);
                    break;
                }
                case "bmp":
                {
                    var bgra = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, acc, w * 4);
                    var src = new FormatConvertedBitmap(bgra, PixelFormats.Bgr24, null, 0);
                    var enc = new BmpBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(src));
                    using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
                    enc.Save(fs);
                    break;
                }
                case "tiff":
                {
                    var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, acc, w * 4);
                    var enc = new TiffBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(src));
                    using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
                    enc.Save(fs);
                    break;
                }
                case "dng":
                {
                    DngWriter.Write(path, acc, w, h);
                    break;
                }
                default:
                    throw new NotSupportedException($"不支持的输出格式: {format}");
            }
        }
    }
}
