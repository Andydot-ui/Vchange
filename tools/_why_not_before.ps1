$ErrorActionPreference = 'Continue'
Set-Location 'C:\Users\andyd\Documents\shipin\VideoConverter'

Write-Host '############ 1. 保存路径相关代码 diff: v1.1.0(4af6649) -> 现在 ############'
$d = git diff 4af6649 HEAD -- MainWindow.xaml.cs ImageConvertFlow.cs 2>$null
$hits = @($d | Select-String -Pattern '^[+-].*(SaveFileDialog|InitialDirectory|OpenFolderDialog|FolderName|PlanOutputs|_converted|GetDirectoryName|DefaultExt)')
if ($hits.Count -eq 0) { Write-Host '  (空 —— 保存路径代码没有变化)' }
$hits | ForEach-Object { '  ' + $_.Line }

Write-Host ''
Write-Host '############ 2. Defender 受控文件夹访问 (Controlled Folder Access) ############'
try {
    $mp = Get-MpPreference
    '  EnableControlledFolderAccess = ' + $mp.EnableControlledFolderAccess
    '  ProtectedFolders             = ' + (($mp.ControlledFolderAccessProtectedFolders) -join ' | ')
    '  AllowedApps                  = ' + (($mp.ControlledFolderAccessAllowedApplications) -join ' | ')
} catch { '  Get-MpPreference 失败: ' + $_.Exception.Message }
try {
    $st = Get-MpComputerStatus
    '  RealTimeProtection = ' + $st.RealTimeProtectionEnabled + '   TamperProtected = ' + $st.IsTamperProtected
} catch { '  Get-MpComputerStatus 失败' }

Write-Host ''
Write-Host '############ 3. 各个 Vchange.exe 的 MOTW(网络下载标记) ############'
foreach ($p in @("$env:USERPROFILE\Downloads\Vchange.exe",
                 "$env:USERPROFILE\Desktop\Vchange.exe",
                 (Join-Path (Get-Location) 'Release\Vchange.exe'))) {
    if (Test-Path $p) {
        $z = Get-Item $p -Stream Zone.Identifier -ErrorAction SilentlyContinue
        $v = if ($z) { ((Get-Content $p -Stream Zone.Identifier -ErrorAction SilentlyContinue) -join ' / ') } else { '无 MOTW（干净，不会弹 SmartScreen）' }
        '  {0,-16} {1,12}  {2}' -f (Split-Path $p -Leaf), (Get-Item $p).Length, $v
    } else { '  不存在: ' + $p }
}

Write-Host ''
Write-Host '############ 4. memory1791281634388.mp4 到底是谁生成的 ############'
foreach ($dir in @("$env:USERPROFILE\Downloads", "$env:USERPROFILE\Desktop", "$env:USERPROFILE\Videos", "$env:USERPROFILE\Documents")) {
    Get-ChildItem $dir -Filter 'memory*' -File -ErrorAction SilentlyContinue | ForEach-Object {
        '  ' + $_.FullName + '   ' + $_.Length + '   ' + $_.LastWriteTime
    }
}
Write-Host '  --- 崩溃转储目录 ---'
Get-ChildItem "$env:LOCALAPPDATA\CrashDumps" -ErrorAction SilentlyContinue |
    Select-Object -First 8 | ForEach-Object { '  ' + $_.Name + '   ' + $_.Length + '   ' + $_.LastWriteTime }

Write-Host ''
Write-Host '############ 5. Downloads 目录权限/属性 ############'
$dl = "$env:USERPROFILE\Downloads"
$i = Get-Item $dl
'  ReadOnly 属性 = ' + ((($i.Attributes -band [IO.FileAttributes]::ReadOnly) -ne 0))
'  ACL:'
(Get-Acl $dl).Access | ForEach-Object {
    '    {0,-42} {1,-16} {2}' -f $_.IdentityReference, $_.FileSystemRights, $_.AccessControlType
}