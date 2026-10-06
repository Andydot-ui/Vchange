using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Vchange
{
    public class ImageConvertProgress
    {
        public int Frame;
        public int FrameCount;
        public string Stage = "";
    }

    public class ImageConvertOptions
    {
        public string SourcePath = "";
        public string OutputPath = "";
        /// <summary>jpg / png / bmp / tiff / wmp / dng / webp。</summary>
        public string Format = "jpg";
        /// <summary>JPEG/WebP 品质 1-100。</summary>
        public int Quality = 90;
        /// <summary>TIFF 压缩：lzw / none / zip / rle / ccitt4。</summary>
        public string TiffCompression = "lzw";
        /// <summary>PNG 交错（Adam7）。</summary>
        public bool PngInterlace = false;
        /// <summary>目标尺寸；0 表示与原图相同。</summary>
        public int Width;
        public int Height;
    }

    public class ImageConvertResult
    {
        public bool Success;
        public string? Error;
        public int FrameCount;
        public string? Warning;
    }

    /// <summary>
    /// 图片格式转换引擎：解码（WPF 优先，webp 等回退 ffmpeg）→（可选缩放/色彩空间重映射）→ 编码。
    /// 多页 TIFF 保留全部帧；其余格式取第一帧（动图转换请使用视频转换功能）。
    /// 输出为不含元数据的纯净图片。
    /// </summary>
    public static class ImageConverter
    {
        public static async Task<ImageConvertResult> ConvertAsync(ImageConvertOptions o,
            IProgress<ImageConvertProgress> progress, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                var result = new ImageConvertResult();
                try
                {
                    int w = 0, h = 0;
                    BitmapSource[] frames;

                    // 解码：优先 WPF；webp 等不支持的格式回退 ffmpeg（取首帧）
                    if (TryDecodeWithWpf(o.SourcePath, out var wpfFrames))
                    {
                        frames = wpfFrames;
                        w = frames[0].PixelWidth;
                        h = frames[0].PixelHeight;
                    }
                    else
                    {
                        frames = DecodeWithFfmpeg(o.SourcePath)
                            ?? throw new InvalidDataException("无法解码该图片（WPF 与 ffmpeg 均失败）");
                        w = frames[0].PixelWidth;
                        h = frames[0].PixelHeight;
                    }
                    result.FrameCount = frames.Length;

                    // 目标尺寸：0 = 与第一帧相同
                    int tw = o.Width > 0 ? o.Width : w;
                    int th = o.Height > 0 ? o.Height : h;

                    // 逐帧：缩放 → 统一 BGRA32 →（可选）色彩空间重映射
                    bool ccitt = o.Format == "tiff" &&
                                 (o.TiffCompression == "ccitt4" || o.TiffCompression == "ccitt3");
                    var converted = new BitmapSource[frames.Length];
                    for (int i = 0; i < frames.Length; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Report(new ImageConvertProgress { Frame = i, FrameCount = frames.Length, Stage = "解码" });

                        BitmapSource src = frames[i];
                        if (src.PixelWidth != tw || src.PixelHeight != th)
                        {
                            src = new TransformedBitmap(src,
                                new ScaleTransform(tw / (double)src.PixelWidth, th / (double)src.PixelHeight));
                        }

                        var targetFormat = ccitt ? PixelFormats.BlackWhite : PixelFormats.Bgra32;
                        var conv = new FormatConvertedBitmap(src, targetFormat, null, 0);
                        conv.Freeze(); // 跨线程安全
                        converted[i] = conv;
                    }

                    progress?.Report(new ImageConvertProgress { Frame = frames.Length, FrameCount = frames.Length, Stage = "编码" });

                    switch (o.Format)
                    {
                        case "jpg":
                        {
                            var enc = new JpegBitmapEncoder { QualityLevel = Math.Clamp(o.Quality, 1, 100) };
                            enc.Frames.Add(BitmapFrame.Create(converted[0]));
                            Save(enc, o.OutputPath);
                            break;
                        }
                        case "png":
                        {
                            var enc = new PngBitmapEncoder { Interlace = o.PngInterlace ? PngInterlaceOption.On : PngInterlaceOption.Off };
                            enc.Frames.Add(BitmapFrame.Create(converted[0]));
                            Save(enc, o.OutputPath);
                            break;
                        }
                        case "bmp":
                        {
                            var enc = new BmpBitmapEncoder();
                            enc.Frames.Add(BitmapFrame.Create(converted[0]));
                            Save(enc, o.OutputPath);
                            break;
                        }
                        case "tiff":
                        {
                            var enc = new TiffBitmapEncoder { Compression = MapTiffCompression(o.TiffCompression) };
                            foreach (var f in converted) enc.Frames.Add(BitmapFrame.Create(f));
                            Save(enc, o.OutputPath);
                            break;
                        }
                        case "wmp":
                        {
                            var enc = new WmpBitmapEncoder();
                            enc.Frames.Add(BitmapFrame.Create(converted[0]));
                            Save(enc, o.OutputPath);
                            break;
                        }
                        case "webp":
                        {
                            EncodeWebp(converted[0], o, tw, th);
                            break;
                        }
                        case "dng":
                        {
                            if (frames.Length > 1)
                                result.Warning = "DNG 仅支持单帧，已转换第一帧。";
                            var buf = new byte[tw * th * 4];
                            converted[0].CopyPixels(buf, tw * 4, 0);
                            DngWriter.Write(o.OutputPath, buf, tw, th);
                            break;
                        }
                        default:
                            throw new NotSupportedException($"不支持的输出格式: {o.Format}");
                    }

                    result.Success = true;
                    return result;
                }
                catch (OperationCanceledException)
                {
                    return new ImageConvertResult { Error = "已取消" };
                }
                catch (UnauthorizedAccessException)
                {
                    return new ImageConvertResult { Error = $"没有权限写入输出位置：{o.OutputPath}\n请换一个保存位置（例如“下载”或“文档”文件夹）后重试。" };
                }
                catch (Exception ex)
                {
                    return new ImageConvertResult { Error = ex.Message };
                }
            }, ct);
        }

        private static bool TryDecodeWithWpf(string path, out BitmapSource[] frames)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frames = decoder.Frames.Select(f => (BitmapSource)f).ToArray();
                return frames.Length > 0;
            }
            catch
            {
                frames = Array.Empty<BitmapSource>();
                return false;
            }
        }

        /// <summary>ffmpeg 解码到临时 PNG（保留原尺寸、取首帧），再交给 WPF 读取。</summary>
        private static BitmapSource[]? DecodeWithFfmpeg(string path)
        {
            string tempPng = Path.Combine(Path.GetTempPath(), $"vchange_dec_{Guid.NewGuid():N}.png");
            try
            {
                using var process = new System.Diagnostics.Process();
                process.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = FfmpegProvider.GetPath(),
                    Arguments = $"-y -hide_banner -nostdin -nostats -i \"{path}\" -frames:v 1 \"{tempPng}\"",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                process.Start();
                _ = process.StandardError.ReadToEndAsync();
                process.WaitForExit();

                if (process.ExitCode != 0 || !File.Exists(tempPng))
                    return null;

                return TryDecodeWithWpf(tempPng, out var frames) ? frames : null;
            }
            catch
            {
                return null;
            }
            finally
            {
                try { File.Delete(tempPng); } catch { }
            }
        }

        /// <summary>WebP 输出：ffmpeg libwebp（有损，品质可调）。源为非 WPF 可解码格式时先经 ffmpeg 解码。</summary>
        private static void EncodeWebp(BitmapSource frame, ImageConvertOptions o, int w, int h)
        {
            // 帧数据 → PNG 临时文件 → ffmpeg 编码 webp（保持尺寸与品质）
            string tempPng = Path.Combine(Path.GetTempPath(), $"vchange_webp_{Guid.NewGuid():N}.png");
            try
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(frame));
                using (var fs = new FileStream(tempPng, FileMode.Create, FileAccess.Write))
                    enc.Save(fs);

                using var process = new System.Diagnostics.Process();
                process.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = FfmpegProvider.GetPath(),
                    Arguments = $"-y -hide_banner -nostdin -nostats -i \"{tempPng}\" -c:v libwebp -q:v {Math.Clamp(o.Quality, 1, 100)} \"{o.OutputPath}\"",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                process.Start();
                string err = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0 || !File.Exists(o.OutputPath))
                    throw new InvalidOperationException("WebP 编码失败（libwebp）：" + err.Split('\n').LastOrDefault()?.Trim());
            }
            finally
            {
                try { File.Delete(tempPng); } catch { }
            }
        }

        #region 色彩空间（已移除：按需可从 git 历史恢复）
        #endregion

        private static void Save(BitmapEncoder enc, string path)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            enc.Save(fs);
        }

        private static System.Windows.Media.Imaging.TiffCompressOption MapTiffCompression(string s) => s switch
        {
            "none" => TiffCompressOption.None,
            "zip" => TiffCompressOption.Zip,
            "rle" => TiffCompressOption.Rle,
            "ccitt4" => TiffCompressOption.Ccitt4,
            "ccitt3" => TiffCompressOption.Ccitt3,
            _ => TiffCompressOption.Lzw
        };
    }
}
