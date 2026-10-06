$ErrorActionPreference = 'Continue'
$root = 'C:\Users\andyd\Documents\shipin\VideoConverter'
$ff = Join-Path $root 'Resources\ffmpeg.exe'
$tmp = Join-Path $env:TEMP 'tsprobe5'
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

# 两个 60s 片段，第二个故意重置 PTS（模拟真实世界的 TS 拼接/断流）
$a = Join-Path $tmp 'a.mp4'; $b = Join-Path $tmp 'b.mp4'
& $ff -y -hide_banner -loglevel error -f lavfi -i 'testsrc2=size=640x480:rate=25:duration=60' -c:v libx264 -preset ultrafast -pix_fmt yuv420p $a
& $ff -y -hide_banner -loglevel error -i $a -c copy $b

$ta = Join-Path $tmp 'a.ts'; $tb = Join-Path $tmp 'b.ts'
& $ff -y -hide_banner -loglevel error -i $a -c copy -bsf:v h264_mp4toannexb $ta
& $ff -y -hide_banner -loglevel error -i $b -c copy -bsf:v h264_mp4toannexb $tb

# 原始字节拼接 → 时间戳不连续的真实坏样本
$cat = Join-Path $tmp 'discontinuity.ts'
$bytes = [IO.File]::ReadAllBytes($ta) + [IO.File]::ReadAllBytes($tb)
[IO.File]::WriteAllBytes($cat, $bytes)
Write-Host "discontinuity.ts size = $($bytes.Length)  (a.ts=$( (Get-Item $ta).Length ) b.ts=$( (Get-Item $tb).Length ))"

$so = Join-Path $tmp 'prog.txt'; $se = Join-Path $tmp 'err.txt'; $out = Join-Path $tmp 'out.mp4'
Write-Host ''
Write-Host '========== ffmpeg 对这个 TS 的时长判断 vs 实际 out_time =========='
& $ff -y -hide_banner -nostdin -nostats -progress pipe:1 -i $cat -c:v libx264 -preset ultrafast -crf 30 -pix_fmt yuv420p $out 1>$so 2>$se
Write-Host "exit=$LASTEXITCODE"
Get-Content $se | Select-String -Pattern 'Duration:|start:' | ForEach-Object { '  ' + $_.Line.Trim() }
$t = @(Get-Content $so | Where-Object { $_ -like 'out_time=*' })
Write-Host "  out_time 行数 = $($t.Count)"
Write-Host '  前 5:'; $t | Select-Object -First 5 | ForEach-Object { '    ' + $_ }
Write-Host '  后 2:'; $t | Select-Object -Last 2 | ForEach-Object { '    ' + $_ }

$dur = $null
Get-Content $se | Select-String -Pattern 'Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)' | ForEach-Object {
  if ($null -eq $dur) { $dur = $_.Matches[0].Groups }
}
if ($dur) {
  $dsec = [int]$dur[1].Value * 3600 + [int]$dur[2].Value * 60 + [double]$dur[3].Value
  Write-Host ''
  Write-Host ("  ==> ffmpeg 声明的总时长 = {0:N2}s" -f $dsec)
  Write-Host ("  ==> 实际输出到       = {0}" -f $t[-1])
  Write-Host '  ==> 若 App 用 Duration 当分母，第一条进度就会 >= 100%'
}
Write-Host "`ntemp: $tmp"