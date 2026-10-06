$exe = Join-Path (Get-Location) "Release\Vchange.exe"
Write-Host "=== 1. contents of the 15:07 successful extraction dir:"
Get-ChildItem "$env:TEMP\.net\Vchange\371kUJoUN_kA" -ErrorAction SilentlyContinue |
    ForEach-Object { "  $($_.Name) $($_.Length)" }

Write-Host "=== 2. retry x3:"
for ($i = 1; $i -le 3; $i++) {
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo.FileName = $exe
    $p.StartInfo.UseShellExecute = $false
    $p.StartInfo.RedirectStandardError = $true
    $p.StartInfo.CreateNoWindow = $true
    [void]$p.Start()
    $e = $p.StandardError.ReadToEndAsync()
    if ($p.WaitForExit(25000)) {
        $msg = $e.Result.Trim()
        $m = [regex]::Match($msg, 'Vchange\\([0-9a-zA-Z_\-]+)')
        Write-Host ("  try{0}: FAIL name='{1}'" -f $i, $m.Groups[1].Value)
    } else {
        Write-Host ("  try{0}: SUCCESS window='{1}'" -f $i, $p.MainWindowTitle)
        $p.Kill()
    }
}
Write-Host "=== dir listing now:"
Get-ChildItem "$env:TEMP\.net\Vchange" -Force | ForEach-Object { "  $($_.Name) $($_.LastWriteTime)" }
