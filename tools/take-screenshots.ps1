$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class ShotWin32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(80);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(40);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }
}
"@

[ShotWin32]::SetProcessDPIAware() | Out-Null

function Get-AppWindow {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Vchange")
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}

function Get-Buttons($win) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    $rect = $win.Current.BoundingRectangle
    $list = @()
    foreach ($b in $all) {
        if ($b.Current.IsOffscreen) { continue }
        $r = $b.Current.BoundingRectangle
        if ($r.Top -le $rect.Top + 40) { continue }  # skip close button in title bar
        $list += @{ Element = $b; Left = $r.Left; Top = $r.Top; Width = $r.Width; Height = $r.Height }
    }
    return $list
}

function Click-Rightmost {
    $w = Get-AppWindow
    $bs = Get-Buttons $w
    $next = ($bs | Sort-Object -Property Left -Descending)[0]
    $x = [int]($next.Left + $next.Width / 2)
    $y = [int]($next.Top + $next.Height / 2)
    [ShotWin32]::Click($x, $y)
    Start-Sleep -Milliseconds 1400
}

function Capture([string]$path) {
    $win = Get-AppWindow
    $h = [IntPtr]$win.Current.NativeWindowHandle
    [ShotWin32]::SetForegroundWindow($h) | Out-Null
    $r = New-Object ShotWin32+RECT
    [ShotWin32]::GetWindowRect($h, [ref]$r) | Out-Null
    # move cursor to an empty spot to avoid hover tooltips in the shot
    [ShotWin32]::SetCursorPos(($r.Left + 25), ($r.Bottom - 120)) | Out-Null
    Start-Sleep -Milliseconds 450
    $w = $r.Right - $r.Left
    $ht = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $ht)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $ht)))
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "captured: $path"
}

$exe = "C:\Users\andyd\Documents\shipin\VideoConverter\bin\Debug\net6.0-windows\Vchange.exe"
$shots = "$env:TEMP\opencode\shots"
New-Item -ItemType Directory -Force -Path $shots | Out-Null

Get-Process Vchange -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Start-Process -FilePath $exe -ArgumentList @("--input", "C:\Users\andyd\Downloads\VchangeDemo.mp4", "--output", "C:\Users\andyd\Downloads\VchangeDemo_out.mp4")
Start-Sleep -Seconds 4

Capture "$shots\1-file.png"
Click-Rightmost; Capture "$shots\2-format.png"
Click-Rightmost; Capture "$shots\3-params.png"
Click-Rightmost; Capture "$shots\4-bitrate.png"
Click-Rightmost; Capture "$shots\5-summary.png"

# Start conversion (rightmost button on step 5)
Click-Rightmost
Start-Sleep -Seconds 16
Capture "$shots\6-done.png"

Write-Host "ALL DONE"
