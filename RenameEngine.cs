using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Vchange
{
    /// <summary>重命名执行结果。</summary>
    public class RenameResult
    {
        public string SourceDir = "";
        /// <summary>合成时使用的序列目录（复制模式 = rename 子目录，直接重命名 = 原目录）。</summary>
        public string WorkingDir = "";
        /// <summary>复制模式创建的 rename 目录；直接重命名模式为 null。</summary>
        public string? RenameDir;
        public List<string> NewNames = new();
        public int Pad = 2;
        public int Count;
        /// <summary>第一个文件的扩展名（含点，保留原扩展名大小写）。</summary>
        public string FirstExtension = ".jpg";
    }

    /// <summary>
    /// 延时合成前的图像序列重命名：
    /// 按自然排序（数字按数值比较）将图片重命名为 001.jpg、002.jpg…（补零位数由文件数决定，至少 2 位）。
    /// 复制模式：复制到源目录下的 rename 子目录；直接模式：就地重命名原文件。
    /// </summary>
    public static class RenameEngine
    {
        public static readonly string[] ImageExtensions =
        {
            ".jpg", ".jpeg", ".jpe", ".png", ".bmp", ".tif", ".tiff", ".webp", ".gif", ".jxl",
            ".dng", ".wdp", ".jxr"
        };

        /// <summary>列出目录下所有图片并按自然顺序排序。</summary>
        public static List<string> ListImages(string dir) =>
            Directory.EnumerateFiles(dir)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, NaturalComparer.Instance)
                .ToList();

        /// <summary>补零位数：由文件数决定，至少 2 位。</summary>
        public static int ComputePad(int count) => Math.Max(2, count.ToString().Length);

        /// <summary>第 i 个（0 基）文件的目标文件名。</summary>
        public static string BuildName(int index, int pad, string originalName) =>
            (index + 1).ToString(new string('0', pad)) + Path.GetExtension(originalName);

        /// <summary>源目录下复制模式所需空间（所有图片总大小 + 5% 余量）。</summary>
        public static long EstimateCopyBytes(List<string> files) =>
            (long)(files.Sum(f =>
            {
                try { return new FileInfo(f).Length; } catch { return 0L; }
            }) * 1.05);

        /// <summary>获取目录所在分区的可用空间；失败返回 null。</summary>
        public static long? GetFreeBytes(string dir)
        {
            try
            {
                var full = Path.GetFullPath(dir);
                var drive = DriveInfo.GetDrives()
                    .FirstOrDefault(d => full.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase) && d.IsReady);
                return drive?.AvailableFreeSpace;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>复制模式的目标目录：优先 “rename”，被占用时使用 rename2、rename3…</summary>
        public static string PickRenameDir(string sourceDir)
        {
            string candidate = Path.Combine(sourceDir, "rename");
            int n = 2;
            while (Directory.Exists(candidate) && Directory.EnumerateFileSystemEntries(candidate).Any())
            {
                candidate = Path.Combine(sourceDir, $"rename{n}");
                n++;
            }
            Directory.CreateDirectory(candidate);
            return candidate;
        }

        /// <summary>
        /// 复制模式：复制到 rename 目录并按序命名。progress(done,total,currentFile)。
        /// </summary>
        public static RenameResult CopyToRenameDir(string sourceDir, List<string> files, Action<int, int, string>? progress)
        {
            string targetDir = PickRenameDir(sourceDir);
            int pad = ComputePad(files.Count);
            var result = new RenameResult
            {
                SourceDir = sourceDir,
                WorkingDir = targetDir,
                RenameDir = targetDir,
                Pad = pad,
                Count = files.Count
            };

            int total = files.Count;
            for (int i = 0; i < total; i++)
            {
                string src = files[i];
                string dst = Path.Combine(targetDir, BuildName(i, pad, Path.GetFileName(src)));
                File.Copy(src, dst, overwrite: true);
                result.NewNames.Add(Path.GetFileName(dst));
                progress?.Invoke(i + 1, total, Path.GetFileName(src));
            }

            if (total > 0)
                result.FirstExtension = Path.GetExtension(files[0]);
            return result;
        }

        /// <summary>
        /// 直接模式：就地重命名。为避免目标名与尚未重命名的文件冲突，采用两趟：
        /// 第一趟全部改成临时名，第二趟改成最终名。
        /// </summary>
        public static RenameResult RenameInPlace(string sourceDir, List<string> files, Action<int, int, string>? progress)
        {
            int pad = ComputePad(files.Count);
            var result = new RenameResult
            {
                SourceDir = sourceDir,
                WorkingDir = sourceDir,
                RenameDir = null,
                Pad = pad,
                Count = files.Count
            };

            int total = files.Count;
            int done = 0;
            var tempNames = new string[total];

            // 第一趟：改成临时名
            for (int i = 0; i < total; i++)
            {
                string src = files[i];
                string temp = Path.Combine(sourceDir, $"$vc_tmp_{i:D6}{Path.GetExtension(src)}");
                if (File.Exists(temp)) temp = Path.Combine(sourceDir, $"$vc_tmp_{i:D6}_{Guid.NewGuid():N}{Path.GetExtension(src)}");
                File.Move(src, temp);
                tempNames[i] = temp;
                done++;
                progress?.Invoke(done, total * 2, Path.GetFileName(src));
            }

            // 第二趟：改成最终名
            for (int i = 0; i < total; i++)
            {
                string final = Path.Combine(sourceDir, BuildName(i, pad, Path.GetFileName(files[i])));
                File.Move(tempNames[i], final);
                result.NewNames.Add(Path.GetFileName(final));
                done++;
                progress?.Invoke(done, total * 2, Path.GetFileName(final));
            }

            if (total > 0)
                result.FirstExtension = Path.GetExtension(files[0]);
            return result;
        }
    }

    /// <summary>自然排序比较器：数字段按数值比较（IMG_2 排在 IMG_10 前），其余按 Ordinal。</summary>
    public class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? a, string? b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            int ia = 0, ib = 0;
            while (ia < a.Length && ib < b.Length)
            {
                char ca = a[ia], cb = b[ib];
                if (char.IsDigit(ca) && char.IsDigit(cb))
                {
                    // 取出完整数字段
                    int sa = ia, sb = ib;
                    while (ia < a.Length && char.IsDigit(a[ia])) ia++;
                    while (ib < b.Length && char.IsDigit(b[ib])) ib++;

                    string da = a.Substring(sa, ia - sa).TrimStart('0');
                    string db = b.Substring(sb, ib - sb).TrimStart('0');
                    if (da.Length != db.Length) return da.Length.CompareTo(db.Length);
                    int c = string.CompareOrdinal(da, db);
                    if (c != 0) return c;
                }
                else
                {
                    // 数字段排在字母段之前（与资源管理器一致：IMG_1 < cover.jpg）
                    if (char.IsDigit(ca)) return -1;
                    if (char.IsDigit(cb)) return 1;

                    int c = char.ToUpperInvariant(ca).CompareTo(char.ToUpperInvariant(cb));
                    if (c != 0) return c;
                    // 大小写不同但字母相同：退回 Ordinal 保证确定性
                    c = ca.CompareTo(cb);
                    if (c != 0) return c;
                    ia++;
                    ib++;
                }
            }
            return (a.Length - ia).CompareTo(b.Length - ib);
        }
    }
}
