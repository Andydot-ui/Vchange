$ErrorActionPreference = 'Continue'
Set-Location 'C:\Users\andyd\Documents\shipin\VideoConverter'
$ff = Join-Path (Get-Location) 'Resources\ffmpeg.exe'

Write-Host '############ A. save-dialog code diff: v1.1.0 -> now ############'
$d = git diff 4af6649 HEAD -- MainWindow.xaml.cs
@($d | Select-String -Pattern '^[+-].*(SaveFileDialog|InitialDirectory|FileName =|OpenFolderDialog|FolderName|PlanOutputs)') |
    ForEach-Object { '  ' + $_.Line.Trim() }

Write-Host ''
Write-Host '############ B. candidate input files (recent media) ############'
$dirs = @("$env:USERPROFILE\Downloads", "$env:USERPROFILE\Desktop", "$env:USERPROFILE\Videos", "$env:USERPROFILE\Pictures")
$exts = @('.mp4','.ts','.m2ts','.mts','.mov','.mkv','.avi','.flv','.webm','.gif')
$files = @()
foreach ($d in $dirs) {
    if (-not (Test-Path $d)) { continue }
    $files += Get-ChildItem $d -File -Recurse -Depth 1 -ErrorAction SilentlyContinue |
              Where-Object { $exts -contains $_.Extension.ToLower() }
}
$top = @($files | Sort-Object LastWriteTime -Descending | Select-Object -First 12)
$top | ForEach-Object { '  {0}  {1,12}  {2}' -f $_.LastWriteTime.ToString('MM-dd HH:mm'), $_.Length, $_.FullName }

Write-Host ''
Write-Host '############ C. decode-only probe: declared Duration vs real out_time ############'
foreach ($f in ($top | Select-Object -First 5)) {
    $so = Join-Path $env:TEMP 'ru_out.txt'
    $se = Join-Path $env:TEMP 'ru_err.txt'
    Remove-Item $so, $se -Force -ErrorAction SilentlyContinue
    $proc = Start-Process -FilePath $ff -PassThru -NoNewWindow `
        -ArgumentList @('-nostdin','-nostats','-progress','pipe:1','-i',$f.FullName,'-f','null','-') `
        -RedirectStandardOutput $so -RedirectStandardError $se
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not $proc.HasExited -and $sw.Elapsed.TotalSeconds -lt 90) { Start-Sleep -Milliseconds 400 }
    if (-not $proc.HasExited) { $proc.Kill(); Write-Host "  [timeout] $($f.Name)"; continue }
    $proc.WaitForExit()

    $err = @(Get-Content $se -ErrorAction SilentlyContinue)
    $durLine = ($err | Select-String -Pattern 'Duration:' | Select-Object -First 1).Line
    $start = 0.0
    $dur = $null
    if ($durLine) {
        $m = [regex]::Match($durLine, 'Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)')
        if ($m.Success) { $dur = [int]$m.Groups[1].Value * 3600 + [int]$m.Groups[2].Value * 60 + [double]$m.Groups[3].Value }
        $ms = [regex]::Match($durLine, 'start:\s*(\d+(?:\.\d+)?)')
        if ($ms.Success) { $start = [double]$ms.Groups[1].Value }
    }
    $ot = @(Get-Content $so -ErrorAction SilentlyContinue | Where-Object { $_ -like 'out_time=*' })
    $firstOt = if ($ot.Count) { $ot[0].Trim() } else { '(none)' }
    $maxOt = 0.0
    foreach ($l in $ot) {
        $mm = [regex]::Match($l, 'out_time=(\d+):(\d+):(\d+(?:\.\d+)?)')
        if ($mm.Success) {
            $s = [int]$mm.Groups[1].Value * 3600 + [int]$mm.Groups[2].Value * 60 + [double]$mm.Groups[3].Value
            if ($s -gt $maxOt) { $maxOt = $s }
        }
    }
    Write-Host ("  --- {0}  ({1:N1} MB)" -f $f.Name, ($f.Length / 1MB))
    Write-Host ("      ffmpeg: Duration={0}  start={1:N2}s  out_time blocks={2}" -f $(if ($null -ne $dur) { '{0:N2}s' -f $dur } else { 'N/A' }), $start, $ot.Count)
    Write-Host ("      first out_time: {0}   max out_time: {1:N2}s" -f $firstOt, $maxOt)
    if ($null -ne $dur -and $dur -gt 0) {
        Write-Host ("      ==> maxOut/Duration = {0:N2}x   {1}" -f ($maxOt / $dur), $(if ($maxOt / $dur -gt 1.3) { 'RUNAWAY (would pin the bar)' } else { 'ok' }))
    }
    if ($null -eq $dur -or $dur -le 0.1) { Write-Host '      ==> Duration unusable -> indeterminate path' }
}
Write-Host ''
Write-Host 'done'