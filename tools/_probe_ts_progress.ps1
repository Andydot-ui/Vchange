$ErrorActionPreference = 'Continue'
$root = 'C:\Users\andyd\Documents\shipin\VideoConverter'
$ff = Join-Path $root 'Resources\ffmpeg.exe'
$tmp = Join-Path $env:TEMP 'tsprobe4'
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

$src = Join-Path $tmp 'src.mp4'
$tsPlain = Join-Path $tmp 'plain.ts'

Write-Host '--- build 180s source mp4 (640x480) ---'
& $ff -y -hide_banner -loglevel error -f lavfi -i 'testsrc=size=640x480:rate=25:duration=180' -c:v libx264 -preset ultrafast -pix_fmt yuv420p $src
Write-Host "src.mp4 size: $((Get-Item $src -ErrorAction SilentlyContinue).Length)"

Write-Host '--- remux to TS ---'
& $ff -y -hide_banner -loglevel error -i $src -c copy -bsf:v h264_mp4toannexb $tsPlain
Write-Host "plain.ts size: $((Get-Item $tsPlain -ErrorAction SilentlyContinue).Length)"

function Probe-File([string]$inFile, [string]$tag) {
    $so = Join-Path $tmp "$tag.progress.txt"
    $se = Join-Path $tmp "$tag.stderr.txt"
    $out = Join-Path $tmp "$tag.out.mp4"
    Write-Host ''
    Write-Host "========== $tag =========="
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath $ff -PassThru -NoNewWindow `
        -ArgumentList @('-y','-hide_banner','-nostdin','-nostats','-progress','pipe:1',
                        '-i', $inFile, '-c:v','libx264','-preset','medium','-crf','28',
                        '-pix_fmt','yuv420p', $out) `
        -RedirectStandardOutput $so -RedirectStandardError $se
    # sample the progress file while running
    $seen = 0
    $samples = @()
    while (-not $proc.HasExited) {
        Start-Sleep -Milliseconds 700
        $lines = @(Get-Content $so -ErrorAction SilentlyContinue)
        $t = @($lines | Where-Object { $_ -like 'out_time=*' })
        if ($t.Count -gt $seen) {
            for ($i = $seen; $i -lt $t.Count; $i++) {
                $samples += ('    t={0,6:N1}s  {1}' -f $sw.Elapsed.TotalSeconds, $t[$i])
            }
            $seen = $t.Count
        }
    }
    $proc.WaitForExit()
    $sw.Stop()
    Write-Host ("  exit=" + $proc.ExitCode + "  wall=" + [math]::Round($sw.Elapsed.TotalSeconds,1) + "s  out.mp4=" + (Test-Path $out))
    $errLines = @(Get-Content $se -ErrorAction SilentlyContinue)
    $errLines | Select-String -Pattern 'Duration:|start:' | Select-Object -First 2 | ForEach-Object { '  err| ' + $_.Line.Trim() }
    $lines = @(Get-Content $so -ErrorAction SilentlyContinue)
    $t = @($lines | Where-Object { $_ -like 'out_time=*' })
    Write-Host "  progress blocks=$($lines.Count)  out_time lines=$($t.Count)  sampled=$($samples.Count)"
    $samples | Select-Object -First 8 | ForEach-Object { $_ }
    Write-Host '    ...'
    $samples | Select-Object -Last 3 | ForEach-Object { $_ }
}

Probe-File $src 'A_mp4'
Probe-File $tsPlain 'B_ts'

Write-Host ''
Write-Host "temp dir: $tmp"