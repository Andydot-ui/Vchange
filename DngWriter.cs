using System;
using System.Collections.Generic;
using System.IO;

namespace Vchange
{
    /// <summary>
    /// 线性 DNG 写入器（16-bit RGB，LinearRaw，未压缩，不包含 EXIF 元数据）。
    /// ffmpeg 的 TIFF 编码器不支持写 DNG，因此这里手工按 DNG 1.4 规范构造文件：
    /// IFD0（图像标签 + DNG 专用标签）+ 像素数据。
    /// </summary>
    public static class DngWriter
    {
        private const ushort TagNewSubFileType = 254;
        private const ushort TagImageWidth = 256;
        private const ushort TagImageLength = 257;
        private const ushort TagBitsPerSample = 258;
        private const ushort TagCompression = 259;
        private const ushort TagPhotometric = 262;
        private const ushort TagStripOffsets = 273;
        private const ushort TagOrientation = 274;
        private const ushort TagSamplesPerPixel = 277;
        private const ushort TagRowsPerStrip = 278;
        private const ushort TagStripByteCounts = 279;
        private const ushort TagPlanarConfig = 284;
        private const ushort TagSampleFormat = 339;
        private const ushort TagUniqueCameraModel = 50708;
        private const ushort TagWhiteLevel = 50717;
        private const ushort TagColorMatrix1 = 50721;
        private const ushort TagAsShotNeutral = 50728;
        private const ushort TagCalibrationIlluminant1 = 50778;

        /// <summary>sRGB（D65）→ XYZ 色彩矩阵，DNG 1.4 要求 LinearRaw 必须提供 ColorMatrix1。</summary>
        private static readonly double[,] SrgbToXyz =
        {
            { 0.4124564, 0.3575761, 0.1804375 },
            { 0.2126729, 0.7151522, 0.0721750 },
            { 0.0193339, 0.1191920, 0.9503041 }
        };

        /// <summary>
        /// 把 BGRA32 像素缓冲写成线性 DNG。
        /// </summary>
        public static void Write(string path, byte[] bgra, int width, int height)
        {
            long pixelBytes = (long)width * height * 6;

            uint stripOffset = 0;
            var entries = new List<TiffEntry>
            {
                TiffEntry.Long(TagNewSubFileType, 0),
                TiffEntry.Long(TagImageWidth, (uint)width),
                TiffEntry.Long(TagImageLength, (uint)height),
                TiffEntry.Short(TagBitsPerSample, 16, 16, 16),
                TiffEntry.Short(TagCompression, 1),
                TiffEntry.Short(TagPhotometric, 34892), // LinearRaw
                TiffEntry.Short(TagSamplesPerPixel, 3),
                TiffEntry.Long(TagRowsPerStrip, (uint)height),
                TiffEntry.Long(TagStripByteCounts, (uint)pixelBytes),
                TiffEntry.Short(TagPlanarConfig, 1),
                TiffEntry.Short(TagSampleFormat, 1, 1, 1),
                TiffEntry.Short(TagOrientation, 1),
                TiffEntry.Bytes(50706, 1, 4, 0, 0),       // DNGVersion 1.4.0.0
                TiffEntry.Bytes(50707, 1, 1, 0, 0),       // DNGBackwardVersion 1.1.0.0
                TiffEntry.Ascii(TagUniqueCameraModel, "Vchange Stack"),
                TiffEntry.Long(TagWhiteLevel, 65535),
                BuildColorMatrix1(),
                TiffEntry.Short(TagCalibrationIlluminant1, 21), // D65
            };

            // StripOffsets 用延迟条目回填
            var stripOffsets = new TiffDeferredEntry(TagStripOffsets, 4, 4, off => stripOffset = off);
            entries.Add(stripOffsets);

            // AsShotNeutral = 3 个 1/1
            entries.Add(new TiffEntry(TagAsShotNeutral, 5, new byte[24]
            {
                1,0,0,0, 1,0,0,0,
                1,0,0,0, 1,0,0,0,
                1,0,0,0, 1,0,0,0
            }));

            var header = TiffBuilder.Serialize(entries);

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            fs.Write(header, 0, header.Length);

            // 像素数据从 AssignedOffset 开始（可能大于 header.Length，需要补零）
            if (fs.Position < stripOffset)
                fs.Write(new byte[stripOffset - fs.Length], 0, (int)(stripOffset - fs.Length));

            WritePixels(fs, bgra, width, height);
        }

        private static TiffEntry BuildColorMatrix1()
        {
            var data = new byte[9 * 8];
            int p = 0;
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    var (num, den) = TiffEntry.ToRational(SrgbToXyz[r, c]);
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(p), (int)num);
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(p + 4), (int)den);
                    p += 8;
                }
            }
            return new TiffEntry(TagColorMatrix1, 10, data);
        }

        /// <summary>BGRA32 → 16-bit RGB（LE），分块写出避免大内存峰值。</summary>
        private static void WritePixels(FileStream fs, byte[] bgra, int width, int height)
        {
            int rowsPerChunk = Math.Max(1, 1_000_000 / (width * 6));
            var chunk = new byte[width * rowsPerChunk * 6];

            for (int rowStart = 0; rowStart < height; rowStart += rowsPerChunk)
            {
                int rows = Math.Min(rowsPerChunk, height - rowStart);
                int p = 0;
                for (int y = rowStart; y < rowStart + rows; y++)
                {
                    int src = y * width * 4;
                    for (int x = 0; x < width; x++)
                    {
                        int b = bgra[src + x * 4];
                        int g = bgra[src + x * 4 + 1];
                        int r = bgra[src + x * 4 + 2];
                        chunk[p++] = 0;
                        chunk[p++] = (byte)(r >> 8);
                        chunk[p++] = 0;
                        chunk[p++] = (byte)(g >> 8);
                        chunk[p++] = 0;
                        chunk[p++] = (byte)(b >> 8);
                    }
                }
                fs.Write(chunk, 0, rows * width * 6);
            }
        }
    }
}
