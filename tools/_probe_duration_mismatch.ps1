$ErrorActionPreference = 'Continue'
$root = 'C:\Users\andyd\Documents\shipin\VideoConverter'
$ff = Join-Path $root 'Resources\ffmpeg.exe'
$tmp = Join-Path $env:TEMP 'durmismatch'
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

function Run-Ff([string[]]$av) {
    $p = Start-Process -FilePath $ff -ArgumentList $av -PassThru -NoNewWindow `
        -RedirectStandardOutput (Join-Path $tmp 'so.txt') `
        -RedirectStandardError (Join-Path $tmp 'se.txt')
    $p.WaitForExit()
    return $p.ExitCode
}

$a  = Join-Path $tmp 'a.mp4'
$ta = Join-Path $tmp 'a.ts'
$tb = Join-Path $tmp 'b.ts'

Write-Host '--- build 60s mp4 + two TS copies ---'
Run-Ff @('-y','-hide_banner','-loglevel','error','-f','lavfi','-i','testsrc=size=640x480:rate=25:duration=60','-c:v','libx264','-preset','ultrafast','-pix_fmt','yuv420p',$a) | Out-Null
Write-Host ("  a.mp4 = " + (Get-Item $a -ErrorAction SilentlyContinue).Length)
Run-Ff @('-y','-hide_banner','-loglevel','error','-i',$a,'-c','copy','-bsf:v','h264_mp4toannexb',$ta) | Out-Null
Run-Ff @('-y','-hide_banner','-loglevel','error','-i',$a,'-c','copy','-bsf:v','h264_mp4toannexb',$tb) | Out-Null
Write-Host ("  a.ts  = " + (Get-Item $ta -ErrorAction SilentlyContinue).Length)
Write-Host ("  b.ts  = " + (Get-Item $tb -ErrorAction SilentlyContinue).Length)

$cat = Join-Path $tmp 'joined.ts'
$bytes = [IO.File]::ReadAllBytes($ta) + [IO.File]::ReadAllBytes($tb)
[IO.File]::WriteAllBytes($cat, $bytes)
Write-Host ("  joined.ts = " + $bytes.Length + " bytes (real content ~120s)")

$so = Join-Path $tmp 'prog.txt'
$se = Join-Path $tmp 'err.txt'
$out = Join-Path $tmp 'out.mp4'

Write-Host ''
Write-Host '=== ffmpeg Duration claim vs real out_time (joined.ts) ==='
$sw = [Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $ff -PassThru -NoNewWindow `
    -ArgumentList @('-y','-hide_banner','-nostdin','-nostats','-progress','pipe:1',
                    '-i',$cat,'-c:v','libx264','-preset','veryfast','-crf','30',
                    '-pix_fmt','yuv420p',$out) `
    -RedirectStandardOutput $so -RedirectStandardError $se
while (-not $proc.HasExited) { Start-Sleep -Milliseconds 500 }
$proc.WaitForExit()
$sw.Stop()
Write-Host ("  exit=" + $proc.ExitCode + "  wall=" + [math]::Round($sw.Elapsed.TotalSeconds,1) + "s")

$errAll = @(Get-Content $se -ErrorAction SilentlyContinue)
$errAll | Select-String -Pattern 'Duration:|start:' | Select-Object -First 2 | ForEach-Object { '  err| ' + $_.Line.Trim() }

# parse Duration like the app does
$durSec = $null
foreach ($l in $errAll) {
    $m = [regex]::Match($l, 'Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)')
    if ($m.Success -and $null -eq $durSec) {
        $durSec = [int]$m.Groups[1].Value * 3600 + [int]$m.Groups[2].Value * 60 + [double]$m.Groups[3].Value
    }
}
$t = @(Get-Content $so | Where-Object { $_ -like 'out_time=*' })
Write-Host ("  out_time blocks = " + $t.Count)
Write-Host ('  first 3: ' + ($t | Select-Object -First 3 | ForEach-Object { $_.Trim() }) -join ' | ')
Write-Host ('  last  2: ' + ($t | Select-Object -Last 2 | ForEach-Object { $_.Trim() }) -join ' | ')

if ($durSec) {
    Write-Host ''
    Write-Host ("  ffmpeg 声明的总时长 Duration = {0:N1}s" -f $durSec)
    $maxOut = 0.0
    foreach ($line in $t) {
        $m2 = [regex]::Match($line, 'out_time=(\d+):(\d+):(\d+(?:\.\d+)?)')
        if ($m2.Success) {
            $s = [int]$m2.Groups[1].Value * 3600 + [int]$m2.Groups[2].Value * 60 + [double]$m2.Groups[3].Value
            if ($s -gt $maxOut) { $maxOut = $s }
        }
    }
    Write-Host ("  实际输出到 out_time max     = {0:N1}s" -f $maxOut)
    foreach ($s in @(5.0, 10.0, 20.0, 40.0)) {
        if ($s -le $maxOut) {
            Write-Host ("    当 out_time={0,5:N1}s 时, 老公式 min(99, {0:N1}/{1:N1}*100) = {2:N1}%" -f $s, $durSec, [math]::Min(99.0, $s / $durSec * 100))
        }
    }
    Write-Host ''
    Write-Host ("  ==> 老代码: 百分比 = min(99, out_time/Duration*100) —— 会瞬间顶到 99% 并卡住")
    Write-Host ("  ==> 新代码: raw>130% 判定 Duration 不可信 -> 切换为跑马灯+已用时长/帧数/速度")
}
Write-Host ''
Write-Host "temp: $tmp"
