$ErrorActionPreference = 'SilentlyContinue'
$root = 'C:\Users\andyd\Documents\shipin\VideoConverter'
$ff = Join-Path $root 'Resources\ffmpeg.exe'

Write-Host '=== A. MOTW / signature state of every Vchange exe on disk ==='
$dirs = @(
  "$env:USERPROFILE\Downloads",
  "$env:USERPROFILE\Desktop",
  "$root\Release",
  "$root\发布",
  "$root\bin\Release\net8.0-windows",
  "$root\bin\Release\net8.0-windows\win-x64"
)
foreach ($d in $dirs) {
  if (-not (Test-Path $d)) { continue }
  Get-ChildItem $d -Filter 'Vchange*.exe' -File | ForEach-Object {
    $z = Get-Item $_.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue
    $motw = if ($z) { 'MOTW: ' + (((Get-Content $_.FullName -Stream Zone.Identifier) -join ' / ')) } else { 'no-MOTW' }
    '{0,-58} {1,12} {2} {3}' -f $_.FullName.Replace($root, '.'), $_.Length, $_.LastWriteTime.ToString('MM-dd HH:mm'), $motw
  }
}

Write-Host ''
Write-Host '=== B. SmartScreen / Attachment Manager policy ==='
$ah = Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppHost'
'HKCU AppHost.EnableWebContentEvaluation = ' + $ah.EnableWebContentEvaluation
'HKCU AppHost.PreventOverride           = ' + $ah.PreventOverride
'HKLM Explorer SmartScreenEnabled       = ' + (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer').SmartScreenEnabled
'HKLM Policies EnableSmartScreen         = ' + (Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System').EnableSmartScreen

Write-Host ''
Write-Host '=== C. ffmpeg -progress out_time: MP4 vs TS input ==='
$tmp = Join-Path $env:TEMP 'tsprobe'
Remove-Item $tmp -Recurse -Force
New-Item -ItemType Directory -Path $tmp -Force | Out-Null
$src = Join-Path $tmp 'src.mp4'
$ts  = Join-Path $tmp 'src.ts'

& $ff -y -hide_banner -loglevel error -f lavfi -i 'testsrc=size=320x240:rate=25:duration=10' -c:v libx264 -pix_fmt yuv420p $src
& $ff -y -hide_banner -loglevel error -i $src -c copy -bsf:v h264_mp4toannexb $ts

function Probe($input, $tag) {
  $so = Join-Path $tmp "$tag.out.txt"
  $se = Join-Path $tmp "$tag.err.txt"
  $out = Join-Path $tmp "$tag.mp4"
  & $ff -y -hide_banner -nostdin -nostats -progress pipe:1 -i $input -c:v libx264 -preset ultrafast -crf 30 $out 1>$so 2>$se
  Write-Host "---- $tag : stderr Duration/start ----"
  (Get-Content $se | Select-String -Pattern 'Duration:|start:' | Select-Object -First 2) | ForEach-Object { '   ' + $_.Line.Trim() }
  $t = Get-Content $so | Where-Object { $_ -like 'out_time=*' }
  Write-Host "---- $tag : out_time lines = $($t.Count) ----"
  Write-Host '   first 6:'
  $t | Select-Object -First 6 | ForEach-Object { '     ' + $_ }
  Write-Host '   last 2:'
  $t | Select-Object -Last 2 | ForEach-Object { '     ' + $_ }
}

Probe $src 'MP4'
Probe $ts  'TS'
Remove-Item $tmp -Recurse -Force
