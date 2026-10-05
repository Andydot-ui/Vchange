using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Vchange
{
    /// <summary>TIFF IFD 条目（小端原始负载；Count = Data.Length / 类型大小）。</summary>
    public class TiffEntry
    {
        public ushort Tag;
        public ushort Type;
        public byte[] Data;

        public TiffEntry(ushort tag, ushort type, byte[] data)
        {
            Tag = tag; Type = type; Data = data;
        }

        public static TiffEntry Ascii(ushort tag, string? value)
        {
            if (string.IsNullOrEmpty(value)) value = "";
            return new TiffEntry(tag, 2, System.Text.Encoding.ASCII.GetBytes(value + "\0"));
        }

        public static TiffEntry Short(ushort tag, params ushort[] values)
        {
            var data = new byte[values.Length * 2];
            for (int i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 2), values[i]);
            return new TiffEntry(tag, 3, data);
        }

        public static TiffEntry Long(ushort tag, params uint[] values)
        {
            var data = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4), values[i]);
            return new TiffEntry(tag, 4, data);
        }

        public static TiffEntry Bytes(ushort tag, params byte[] values)
            => new(tag, 1, values);

        /// <summary>double → (分子, 分母)，寻找合适的分母使误差最小。</summary>
        public static (uint num, uint den) ToRational(double value)
        {
            if (value <= 0) return (0, 1);
            if (value > uint.MaxValue) return (uint.MaxValue, 1);

            int[] dens = value >= 1
                ? new[] { 1, 10, 100, 1000, 10000 }
                : new[] { 1, 10, 100, 1000, 10000, 100000, 1000000 };

            (uint, uint) best = (0, 1);
            double bestErr = double.MaxValue;
            foreach (var den in dens)
            {
                double scaled = value * den;
                if (scaled > uint.MaxValue) break;
                var num = (uint)Math.Round(scaled);
                if (num == 0) continue;
                double err = Math.Abs(scaled - num) / value;
                if (err < bestErr)
                {
                    bestErr = err;
                    best = (num, (uint)den);
                }
                if (err < 1e-9) break;
            }
            return best;
        }
    }

    /// <summary>延迟写入的大块数据（如 DNG 像素流）：Serialize 时只分配偏移，不写入内容。</summary>
    public class TiffDeferredEntry : TiffEntry
    {
        public int DeferredLength;
        public uint AssignedOffset;

        /// <summary>offsetCallback 在布局时被调用，拿到数据将写入的文件偏移。</summary>
        public TiffDeferredEntry(ushort tag, ushort type, int length, Action<uint> offsetCallback)
            : base(tag, type, Array.Empty<byte>())
        {
            DeferredLength = length;
            OffsetAssigned = offsetCallback;
        }

        public Action<uint>? OffsetAssigned;
    }

    /// <summary>
    /// TIFF 序列化器：把 IFD0 写成小端 TIFF 文件结构（供 DngWriter 使用）。
    /// </summary>
    public static class TiffBuilder
    {
        public static byte[] Serialize(List<TiffEntry> ifd0)
        {
            ifd0.Sort((a, b) => a.Tag.CompareTo(b.Tag));

            // 布局：头(8) + IFD0 + 数据池
            int size0 = 2 + ifd0.Count * 12 + 4;
            int ifd0Off = 8;
            int poolOff = ifd0Off + size0;

            using var ms = new MemoryStream();
            // 头
            ms.Write("II"u8);
            ms.Write(new byte[] { 0x2A, 0x00 });
            Span<byte> offBuf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(offBuf, (uint)ifd0Off);
            ms.Write(offBuf);

            // 数据池：先收集所有 >4 字节的负载与延迟数据
            var pool = new List<(byte[] data, int offset)>();
            int cursor = poolOff;
            foreach (var e in ifd0)
            {
                if (e is TiffDeferredEntry d)
                {
                    cursor = (cursor + 3) & ~3; // 4 字节对齐
                    d.AssignedOffset = (uint)cursor;
                    d.OffsetAssigned?.Invoke(d.AssignedOffset);
                    cursor += d.DeferredLength;
                }
                else if (e.Data.Length > 4)
                {
                    pool.Add((e.Data, cursor));
                    cursor += e.Data.Length;
                    if (cursor % 2 == 1) cursor++; // 字对齐
                }
            }

            Span<byte> b2 = stackalloc byte[2];
            Span<byte> b4 = stackalloc byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(b2, (ushort)ifd0.Count);
            ms.Write(b2);
            foreach (var e in ifd0)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(b2, e.Tag);
                ms.Write(b2);
                BinaryPrimitives.WriteUInt16LittleEndian(b2, e.Type);
                ms.Write(b2);
                uint count = e is TiffDeferredEntry d2
                    ? (uint)(d2.DeferredLength / TypeSize(e.Type))
                    : (uint)(e.Data.Length / TypeSize(e.Type));
                BinaryPrimitives.WriteUInt32LittleEndian(b4, count);
                ms.Write(b4);

                if (e is TiffDeferredEntry def)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(b4, def.AssignedOffset);
                    ms.Write(b4);
                }
                else if (e.Data.Length <= 4)
                {
                    ms.Write(e.Data, 0, e.Data.Length);
                    for (int i = e.Data.Length; i < 4; i++) ms.WriteByte(0);
                }
                else
                {
                    int off = pool.First(p => ReferenceEquals(p.data, e.Data)).offset;
                    BinaryPrimitives.WriteUInt32LittleEndian(b4, (uint)off);
                    ms.Write(b4);
                }
            }
            BinaryPrimitives.WriteUInt32LittleEndian(b4, 0); // next IFD = 0
            ms.Write(b4);

            // 数据池
            foreach (var (data, offset) in pool)
            {
                int current = (int)ms.Position;
                if (current < offset)
                    ms.Write(new byte[offset - current], 0, offset - current);
                ms.Write(data, 0, data.Length);
            }

            return ms.ToArray();
        }

        public static int TypeSize(ushort type) => type switch
        {
            1 or 2 or 7 => 1,
            3 => 2,
            4 or 9 => 4,
            5 or 10 => 8,
            _ => 0
        };
    }
}
