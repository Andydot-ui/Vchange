# -*- coding: utf-8 -*-
"""Fix ffmpeg progress reporting: TS PTS offset + runaway percentage."""
import io, sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
P = r"C:\Users\andyd\Documents\shipin\VideoConverter\MainWindow.xaml.cs"

with open(P, encoding="utf-8") as f:
    src = f.read()

applied = []


def sub(old, new, label):
    global src
    n = src.count(old)
    if n != 1:
        print("[FAIL] %s: expected 1 match, found %d" % (label, n))
        sys.exit(1)
    src = src.replace(old, new, 1)
    applied.append(label)
    print("[ok] " + label)


# A. new fields + regexes
sub(
"""        private bool _progressUntrusted;               // 容器报的时长不可信 → 改用不确定进度
        private long _currentFrame = -1;               // -progress 的 frame= 计数
        private double _lastPct;                       // 单调递增，避免进度回退
        private static readonly Regex DurationRegex = new(@"Duration:\\s*(\\d+):(\\d+):(\\d+(?:\\.\\d+)?)", RegexOptions.Compiled);
        private static readonly Regex OutTimeRegex = new(@"out_time=(\\d+):(\\d+):(\\d+(?:\\.\\d+)?)", RegexOptions.Compiled);""",
"""        private bool _progressUntrusted;               // 容器报的时长不可信 → 改用不确定进度
        private long _currentFrame = -1;               // -progress 的 frame= 计数
        private TimeSpan _inputStart = TimeSpan.Zero;  // 输入容器起始 PTS 偏移（TS 常见）
        private double _lastPct;                       // 单调递增，避免进度回退
        private static readonly Regex DurationRegex = new(@"Duration:\\s*(\\d+):(\\d+):(\\d+(?:\\.\\d+)?)", RegexOptions.Compiled);
        private static readonly Regex StartRegex = new(@"start:\\s*(\\d+(?:\\.\\d+)?)", RegexOptions.Compiled);
        private static readonly Regex OutTimeRegex = new(@"out_time=(\\d+):(\\d+):(\\d+(?:\\.\\d+)?)", RegexOptions.Compiled);
        private static readonly Regex FrameRegex = new(@"frame=(\\d+)", RegexOptions.Compiled);""",
    "fields + StartRegex/FrameRegex")

# B. reset per-file state
sub(
"""            _mediaDuration = null;
            _currentTime = TimeSpan.Zero;
            _currentSpeed = "";
            _runStarted = DateTime.Now;""",
"""            _mediaDuration = null;
            _currentTime = TimeSpan.Zero;
            _currentSpeed = "";
            _runStarted = DateTime.Now;
            _inputStart = TimeSpan.Zero;
            _progressUntrusted = false;
            _currentFrame = -1;
            _lastPct = 0;""",
    "reset per-file progress state")

# C. parse container start offset from the same banner line
sub(
"""                            _mediaDuration = new TimeSpan(
                                int.Parse(m.Groups[1].Value),
                                int.Parse(m.Groups[2].Value),
                                (int)double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
                        }
                        catch { }""",
"""                            _mediaDuration = new TimeSpan(
                                int.Parse(m.Groups[1].Value),
                                int.Parse(m.Groups[2].Value),
                                (int)double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
                            // TS 等容器带较大的起始 PTS（start: 7200 之类），
                            // out_time 若包含它会让百分比瞬间冲顶，先记下这个偏移
                            var sm = StartRegex.Match(line);
                            if (sm.Success)
                            {
                                var sv = double.Parse(sm.Groups[1].Value, CultureInfo.InvariantCulture);
                                if (sv > 0) _inputStart = TimeSpan.FromSeconds(sv);
                            }
                        }
                        catch { }""",
    "parse container start offset")

# D. track frame counter
sub(
"""            else if (line.StartsWith("speed="))
            {
                _currentSpeed = line.Substring("speed=".Length).Trim();
            }""",
"""            else if (line.StartsWith("speed="))
            {
                _currentSpeed = line.Substring("speed=".Length).Trim();
            }
            else if (line.StartsWith("frame="))
            {
                var fm = FrameRegex.Match(line);
                if (fm.Success && long.TryParse(fm.Groups[1].Value, out var fr))
                    _currentFrame = fr;
            }""",
    "track frame counter")

# E. progress computation
sub(
"""            // 优先使用 ffmpeg 报告的时长；延时合成为图片序列（无输入时长），使用预估值
            TimeSpan? total = _mediaDuration ?? _expectedDuration;
            if (total.HasValue && total.Value.TotalSeconds > 0.1)
            {
                // TS 等容器时间戳不连续会让百分比瞬间跑满：
                // 运行中封顶 99%，真正的 100% 只在 ffmpeg 正常退出后显示
                double pct = Math.Min(99, Math.Max(0,
                    _currentTime.TotalSeconds / total.Value.TotalSeconds * 100));
                _activeProgressBar.IsIndeterminate = false;
                _activeProgressBar.Value = pct;
                _activeProgressText.Text = string.IsNullOrEmpty(_currentSpeed)
                    ? $"{prefix}{pct:0}%"
                    : $"{prefix}{pct:0}% · {_currentSpeed}";
            }
            else
            {
                // 头部时长不可用（如部分 .ts）：不确定进度动画 + 已用时长/速度
                _activeProgressBar.IsIndeterminate = true;
                var elapsed = DateTime.Now - _runStarted;
                string spent = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
                _activeProgressText.Text = string.IsNullOrEmpty(_currentSpeed)
                    ? $"{prefix}转码中 {spent}"
                    : $"{prefix}转码中 {spent} · {_currentSpeed}";
            }""",
"""            // 优先使用 ffmpeg 报告的时长；延时合成为图片序列（无输入时长），使用预估值
            TimeSpan? total = _mediaDuration ?? _expectedDuration;

            // TS / MPEG-TS 的 out_time 常带起始 PTS 偏移（例如 start: 7200），
            // 直接当已处理时长会让百分比瞬间冲顶 —— 先减掉这个偏移
            var done = _currentTime - _inputStart;
            if (done < TimeSpan.Zero) done = TimeSpan.Zero;

            if (total.HasValue && total.Value.TotalSeconds > 0.1 && !_progressUntrusted)
            {
                double raw = done.TotalSeconds / total.Value.TotalSeconds * 100;
                if (raw > 130)
                {
                    // 明显越界：容器声明的时长不可信，本文件改用不确定进度，不再假装精确
                    _progressUntrusted = true;
                }
                else
                {
                    // 只前进不后退；运行中封顶 99%，真正的 100% 只在 ffmpeg 正常退出后显示
                    double pct = Math.Max(_lastPct, Math.Min(99, raw));
                    _lastPct = pct;
                    _activeProgressBar.IsIndeterminate = false;
                    _activeProgressBar.Value = pct;
                    _activeProgressText.Text = string.IsNullOrEmpty(_currentSpeed)
                        ? $"{prefix}{pct:0}%"
                        : $"{prefix}{pct:0}% · {_currentSpeed}";
                    return;
                }
            }

            // 无法给出可靠百分比：不确定进度动画 + 已用时长 / 已处理帧 / 速度
            _activeProgressBar.IsIndeterminate = true;
            var elapsed = DateTime.Now - _runStarted;
            string spent = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
            string frames = _currentFrame > 0 ? $" · {_currentFrame} 帧" : "";
            _activeProgressText.Text = string.IsNullOrEmpty(_currentSpeed)
                ? $"{prefix}转码中 {spent}{frames}"
                : $"{prefix}转码中 {spent}{frames} · {_currentSpeed}";""",
    "progress: subtract PTS offset + runaway guard")

with open(P, "w", encoding="utf-8", newline="") as f:
    f.write(src)

print("\npatched %d/%d blocks -> %s" % (len(applied), 5, P))
