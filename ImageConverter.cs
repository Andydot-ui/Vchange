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
        /// <summary>输出色彩空间：same / srgb / adobergb / displayp3（像素重映射）。</summary>
        public string ColorSpace = "same";
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
                    // 广色域/HDR 色彩空间需 16-bit PNG 输出
                    bool force16 = o.ColorSpace is "rec2020pq" or "rec2020hlg" or "prophoto" or "acescg";
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

                        // 色彩空间处理（HDR / 广色域重映射 → 16-bit；常规 → 8-bit）
                        var targetFormat = ccitt ? PixelFormats.BlackWhite : PixelFormats.Bgra32;
                        BitmapSource conv = new FormatConvertedBitmap(src, targetFormat, null, 0);

                        if (o.ColorSpace != "same" && !ccitt)
                        {
                            var buf8 = new byte[tw * th * 4];
                            conv.CopyPixels(buf8, tw * 4, 0);
                            var (out8, out16) = RemapColorSpace(buf8, tw, th, o.ColorSpace);

                            if (out16 != null)
                            {
                                var px16 = BitmapSource.Create(tw, th, 96, 96, PixelFormats.Rgba64, null, out16, tw * 8);
                                px16.Freeze();
                                conv = px16;
                                result.Warning = o.ColorSpace switch
                                {
                                    "rec2020pq" => "已输出 Rec.2100 PQ 编码的 16-bit PNG（HDR）",
                                    "rec2020hlg" => "已输出 Rec.2100 HLG 编码的 16-bit PNG（HDR）",
                                    "prophoto" => "已输出 ProPhoto RGB 的 16-bit PNG",
                                    "acescg" => "已输出 ACEScg（线性）的 16-bit PNG",
                                    _ => result.Warning
                                };
                            }
                            else if (out8 != null)
                            {
                                var px8 = BitmapSource.Create(tw, th, 96, 96, PixelFormats.Bgra32, null, out8, tw * 4);
                                px8.Freeze();
                                conv = px8;
                            }
                        }

                        conv.Freeze(); // 跨线程安全
                        converted[i] = conv;
                    }

                    progress?.Report(new ImageConvertProgress { Frame = frames.Length, FrameCount = frames.Length, Stage = "编码" });

                    // 广色域/HDR 以 16-bit PNG 输出
                    string effFormat = force16 ? "png" : o.Format;
                    switch (effFormat)
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

        #region 色彩空间（HDR 与广色域）

        // ---------- 通用色彩空间定义：从色度坐标构造矩阵，白点用 Bradford 适应 ----------

        private class RgbSpace
        {
            public double[,] RgbFromXyz = new double[3, 3]; // 线性 XYZ → 线性 RGB
            public double[,] XyzFromRgb = new double[3, 3]; // 线性 RGB → 线性 XYZ
            public bool AdobeGamma;                         // gamma 1.8（ProPhoto）
            public bool Linear;                             // 线性（ACEScg / scRGB）
        }

        private static readonly Dictionary<string, RgbSpace> RgbSpaces = BuildRgbSpaces();

        private static Dictionary<string, RgbSpace> BuildRgbSpaces()
        {
            var d = new Dictionary<string, RgbSpace>();

            void Add(string id, (double x, double y) r, (double x, double y) g, (double x, double y) b,
                     (double x, double y) wp, bool adobeGamma, bool linear)
            {
                var rgbFromXyz = MatrixFromChromasity(r, g, b, wp);
                d[id] = new RgbSpace
                {
                    RgbFromXyz = rgbFromXyz,
                    XyzFromRgb = Invert3(rgbFromXyz),
                    AdobeGamma = adobeGamma,
                    Linear = linear
                };
            }

            Add("srgb",      (0.6400, 0.3300), (0.3000, 0.6000), (0.1500, 0.0600), (0.3127, 0.3290), false, false);
            Add("adobergb",  (0.6400, 0.3300), (0.2100, 0.7100), (0.1500, 0.0600), (0.3127, 0.3290), true,  false);
            Add("displayp3", (0.6800, 0.3200), (0.2650, 0.6900), (0.1500, 0.0600), (0.3127, 0.3290), false, false);
            Add("prophoto",  (0.7347, 0.2653), (0.1596, 0.8404), (0.0366, 0.0001), (0.3457, 0.3585), true,  false);
            Add("acescg",    (0.7130, 0.2930), (0.1650, 0.8300), (0.1280, 0.0440), (0.3217, 0.3377), false, true);
            Add("rec2020",   (0.7080, 0.2920), (0.1700, 0.7970), (0.1310, 0.0460), (0.3127, 0.3290), false, false);
            return d;
        }

        /// <summary>由 RGB 原色与白点的 xy 色度坐标构造 线性XYZ → 线性RGB 矩阵（含白点归一）。</summary>
        private static double[,] MatrixFromChromasity(
            (double x, double y) r, (double x, double y) g, (double x, double y) b,
            (double x, double y) wp)
        {
            double[] pr = XyzFromChroma(r);
            double[] pg = XyzFromChroma(g);
            double[] pb = XyzFromChroma(b);
            double[] w = XyzFromChroma(wp);

            var pInv = Invert3(new double[3, 3]
            {
                { pr[0], pg[0], pb[0] },
                { pr[1], pg[1], pb[1] },
                { pr[2], pg[2], pb[2] }
            });

            // S = P⁻¹ · W（各行与白点分量的点积）
            var s = new double[3];
            for (int row = 0; row < 3; row++)
                s[row] = w[0] * pInv[row, 0] + w[1] * pInv[row, 1] + w[2] * pInv[row, 2];

            var m = new double[3, 3];
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                    m[row, col] = pInv[row, col] * s[col];
            return m;
        }

        private static double[] XyzFromChroma((double x, double y) c)
        {
            double y = 1.0;
            return new[] { c.x * y / c.y, y, (1 - c.x - c.y) * y / c.y };
        }

        /// <summary>Bradford 色适应矩阵（源白点 → 目标白点，XYZ 空间）。</summary>
        private static double[,] BradfordAdapt((double x, double y) srcWp, (double x, double y) dstWp)
        {
            double[,] mb =
            {
                { 0.8951, 0.2664, -0.1614 },
                { -0.7502, 1.7135, 0.0367 },
                { 0.0389, -0.0685, 1.0296 }
            };
            double[,] mbInv =
            {
                { 0.9869929, -0.1470543, 0.1599627 },
                { 0.4323053, 0.5183603, 0.0492912 },
                { -0.0085287, 0.0400428, 0.9684867 }
            };

            var s = MulVec(mb, XyzFromChroma(srcWp));
            var dvec = MulVec(mb, XyzFromChroma(dstWp));

            var d = new double[3, 3];
            for (int i = 0; i < 3; i++) d[i, i] = dvec[i] / s[i];

            return MatMul(MatMul(mbInv, d), mb);
        }

        private static double[] MulVec(double[,] m, double[] v)
        {
            var r = new double[3];
            for (int i = 0; i < 3; i++)
                r[i] = m[i, 0] * v[0] + m[i, 1] * v[1] + m[i, 2] * v[2];
            return r;
        }

        private static double[,] MatMul(double[,] a, double[,] b)
        {
            var r = new double[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    for (int k = 0; k < 3; k++)
                        r[i, j] += a[i, k] * b[k, j];
            return r;
        }

        private static double[,] Invert3(double[,] m)
        {
            double a = m[0, 0], b = m[0, 1], c = m[0, 2];
            double d = m[1, 0], e = m[1, 1], f = m[1, 2];
            double g = m[2, 0], h = m[2, 1], i = m[2, 2];

            double A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g;
            double det = a * A + b * B + c * C;
            double inv = 1.0 / det;

            return new double[3, 3]
            {
                { A * inv, -(b * i - c * h) * inv, (b * f - c * e) * inv },
                { B * inv, (a * i - c * g) * inv, -(a * f - c * d) * inv },
                { C * inv, -(a * h - b * g) * inv, (a * e - b * d) * inv }
            };
        }

        // ---------- sRGB 编解码 ----------

        private static readonly double[] SrgbDecodeLut = BuildSrgbDecodeLut();

        private static double[] BuildSrgbDecodeLut()
        {
            var lut = new double[256];
            for (int i = 0; i < 256; i++)
            {
                double v = i / 255.0;
                lut[i] = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }
            return lut;
        }

        private static double SrgbDecode(int b) => SrgbDecodeLut[b];

        private static double SrgbEncode(double v) =>
            v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;

        // ---------- HDR 传输函数（Rec.2100 PQ / HLG） ----------

        /// <summary>SMPTE ST 2084（PQ）OETF：输入线性亮度（1.0 = SDR 白 ≈ 203 nits）。</summary>
        private static double PqEncode(double linear)
        {
            const double m1 = 2610.0 / 16384.0;
            const double m2 = 2523.0 / 4096.0 * 128;
            const double c1 = 3424.0 / 4096.0;
            const double c2 = 2413.0 / 4096.0 * 32;
            const double c3 = 2392.0 / 4096.0 * 32;

            double nits = Math.Max(linear, 0) * 203.0;
            double e = Math.Min(nits / 10000.0, 1.0);
            double ep = Math.Pow(e, m1);
            return Math.Pow((c1 + c2 * ep) / (1 + c3 * ep), m2);
        }

        /// <summary>BT.2100 HLG OETF：输入线性（1.0 = HLG 参考白 ≈ SDR 白）。</summary>
        private static double HlgOetf(double e)
        {
            double a = 0.17883277;
            double b = 1.0 - 4 * a;
            double c = 0.5 - a * Math.Log(4 * a);
            e = Math.Clamp(e, 0.0, 1.0);
            return e <= 1.0 / 12.0 ? Math.Sqrt(3 * e) : a * Math.Log(12 * e - b) + c;
        }

        /// <summary>
        /// 色彩空间重映射：sRGB 8-bit BGRA → 目标空间。
        /// 返回 (8位缓冲, 16位缓冲)：广色域/HDR 输出 16 位，其余 8 位。
        /// </summary>
        private static (byte[]? out8, ushort[]? out16) RemapColorSpace(byte[] bgra8, int w, int h, string target)
        {
            var src = RgbSpaces["srgb"];
            var dst = RgbSpaces[target];

            // linear_target = M_target_from_xyz × Adapt × M_xyz_from_srgb × rgb_srgb
            double[,] adapt = target == "prophoto"
                ? BradfordAdapt((0.3127, 0.3290), (0.3457, 0.3585)) // D65 → D50
                : new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            double[,] linFromSrgb = MatMul(dst.XyzFromRgb, MatMul(adapt, src.XyzFromRgb));

            bool isHdr = target == "rec2020pq" || target == "rec2020hlg";
            bool is16 = isHdr || target == "prophoto" || target == "acescg";

            byte[]? out8 = is16 ? null : new byte[bgra8.Length];
            ushort[]? out16 = is16 ? new ushort[bgra8.Length * 2] : null;

            for (int i = 0; i + 3 < bgra8.Length; i += 4)
            {
                double[] lin =
                {
                    SrgbDecodeLut[bgra8[i + 2]],
                    SrgbDecodeLut[bgra8[i + 1]],
                    SrgbDecodeLut[bgra8[i]]
                };
                var t = MulVec(linFromSrgb, lin);

                double rE, gE, bE;
                if (target == "rec2020pq")
                {
                    rE = PqEncode(Math.Clamp(t[0], 0.0, 1.0));
                    gE = PqEncode(Math.Clamp(t[1], 0.0, 1.0));
                    bE = PqEncode(Math.Clamp(t[2], 0.0, 1.0));
                }
                else if (target == "rec2020hlg")
                {
                    rE = HlgOetf(Math.Clamp(t[0], 0.0, 1.0));
                    gE = HlgOetf(Math.Clamp(t[1], 0.0, 1.0));
                    bE = HlgOetf(Math.Clamp(t[2], 0.0, 1.0));
                }
                else if (dst.Linear)
                {
                    rE = Math.Clamp(t[0], 0.0, 1.0); gE = Math.Clamp(t[1], 0.0, 1.0); bE = Math.Clamp(t[2], 0.0, 1.0);
                }
                else if (dst.AdobeGamma)
                {
                    rE = Math.Pow(Math.Clamp(t[0], 0.0, 1.0), 1 / 1.8);
                    gE = Math.Pow(Math.Clamp(t[1], 0.0, 1.0), 1 / 1.8);
                    bE = Math.Pow(Math.Clamp(t[2], 0.0, 1.0), 1 / 1.8);
                }
                else
                {
                    rE = SrgbEncode(Math.Clamp(t[0], 0.0, 1.0));
                    gE = SrgbEncode(Math.Clamp(t[1], 0.0, 1.0));
                    bE = SrgbEncode(Math.Clamp(t[2], 0.0, 1.0));
                }

                if (out16 != null)
                {
                    int o = i * 2;
                    out16[o] = (ushort)Math.Clamp(Math.Round(rE * 65535.0), 0, 65535);     // R
                    out16[o + 1] = (ushort)Math.Clamp(Math.Round(gE * 65535.0), 0, 65535); // G
                    out16[o + 2] = (ushort)Math.Clamp(Math.Round(bE * 65535.0), 0, 65535); // B
                    out16[o + 3] = 65535;                                                  // A
                }
                else
                {
                    out8![i + 2] = (byte)Math.Round(rE * 255.0);
                    out8[i + 1] = (byte)Math.Round(gE * 255.0);
                    out8[i] = (byte)Math.Round(bE * 255.0);
                    out8[i + 3] = 255;
                }
            }

            return (out8, out16);
        }

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
