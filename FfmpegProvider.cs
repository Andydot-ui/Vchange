using System;
using System.IO;
using System.Reflection;

namespace Vchange
{
    /// <summary>
    /// 提供 ffmpeg 可执行文件路径：
    /// 1) 程序目录下存在 ffmpeg.exe（绿色版）时直接使用；
    /// 2) 否则把内嵌的完整版 ffmpeg 释放到 %LocalAppData%\Vchange 后使用；
    /// 3) 以上都不可用时回退到系统 PATH 中的 ffmpeg。
    /// </summary>
    internal static class FfmpegProvider
    {
        private const string ResourceName = "Vchange.Resources.ffmpeg.exe";
        private static string? _cachedPath;

        public static string GetPath()
        {
            if (_cachedPath != null) return _cachedPath;

            // 1) 外部 ffmpeg.exe
            var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(local))
                return _cachedPath = local;

            // 2) 释放内嵌 ffmpeg
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
                if (stream != null)
                {
                    var dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Vchange");
                    Directory.CreateDirectory(dir);
                    var target = Path.Combine(dir, "ffmpeg.exe");

                    // 文件不存在或大小不一致（版本变化）时重新释放
                    if (!File.Exists(target) || new FileInfo(target).Length != stream.Length)
                    {
                        using var fs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                        stream.CopyTo(fs);
                    }

                    return _cachedPath = target;
                }
            }
            catch
            {
                // 忽略，回退到 PATH
            }

            // 3) PATH 中的 ffmpeg
            return _cachedPath = "ffmpeg";
        }
    }
}
