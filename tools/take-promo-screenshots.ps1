$ErrorActionPreference = "Stop"

# Capture NEW UI screenshots for the promo video:
#   home.png, cv-params.png, tl-plan.png, tl-summary.png,
#   st-mode.png, st-format.png, st-summary.png,
#   img-format.png, img-options.png
# Drives the app with UIAutomation + SendKeys (folder/file dialogs).

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class ShotWin32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public static bool ForceForeground(IntPtr hWnd) {
        if (hWnd == IntPtr.Zero) return false;
        if (GetForegroundWindow() == hWnd) return true;
        uint dummy;
        uint target = GetWindowThreadProcessId(hWnd, out dummy);
        uint cur = GetCurrentThreadId();
        AttachThreadInput(cur, target, true);
        ShowWindow(hWnd, 9);      // SW_RESTORE
        BringWindowToTop(hWnd);
        SetForegroundWindow(hWnd);
        AttachThreadInput(cur, target, false);
        return GetForegroundWindow() == hWnd;
    }
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

$exe = "C:\Users\andyd\Documents\shipin\VideoConverter\bin\Release\net8.0-windows\Vchange.exe"
$shots = "C:\Users\andyd\Documents\shipin\VideoConverter\docs\screenshots"
$demoVideo = "C:\Users\andyd\Documents\shipin\VideoConverter\promo\demo.mp4"
$imageFolder = "C:\Users\andyd\Documents\shipin\VideoConverter\docs\screenshots"
$imageFile = "C:\Users\andyd\Documents\shipin\VideoConverter\docs\screenshots\1-file.png"

function Stop-App {
    Get-Process Vchange -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 600
}

function Start-App([string[]]$CliArgs) {
    Stop-App
    # Launch via WMI CreateProcess: bypasses the ShellExecute security
    # warning dialog that otherwise blocks Start-Process.
    $cmd = "`"$exe`""
    if ($CliArgs) {
        $cmd += " " + (($CliArgs | ForEach-Object { "`"$_`"" }) -join ' ')
    }
    Invoke-CimMethod -ClassName Win32_Process -MethodName Create `
        -Arguments @{ CommandLine = $cmd } | Out-Null
    # wait for main window
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 300
        if (Get-AppWindow) { break }
    }
    Start-Sleep -Milliseconds 1600
}

function Get-AppWindow {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, "Vchange")
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}

function Get-Buttons($win) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    $rect = $win.Current.BoundingRectangle
    $list = @()
    foreach ($b in $all) {
        if ($b.Current.IsOffscreen) { continue }
        $r = $b.Current.BoundingRectangle
        if ($r.Top -le $rect.Top + 40) { continue }
        $list += [pscustomobject]@{
            Element = $b; Left = $r.Left; Top = $r.Top
            Width = $r.Width; Height = $r.Height; Name = $b.Current.Name
        }
    }
    return $list
}

function Click-Element($el, [switch]$Mouse) {
    $x = [int]($el.Left + $el.Width / 2)
    $y = [int]($el.Top + $el.Height / 2)
    if (-not $Mouse) {
        # prefer UIA Invoke (works for navigation buttons / cards)
        $uia = $el
        if ($el.PSObject.Properties['Element'] -and $el.Element) { $uia = $el.Element }
        try {
            $pat = $uia.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            $pat.Invoke()
            Write-Host "    (uia invoke)"
            Start-Sleep -Milliseconds 1500
            return
        } catch {
            Write-Host ("    (invoke unavailable: {0})" -f $_.Exception.Message)
        }
    }
    $win = Get-AppWindow
    $h = [IntPtr]$win.Current.NativeWindowHandle
    $ok = [ShotWin32]::ForceForeground($h)
    Write-Host ("    (foreground={0})" -f $ok)
    Start-Sleep -Milliseconds 300
    [ShotWin32]::Click($x, $y)
    Write-Host ("    (mouse click {0},{1})" -f $x, $y)
    Start-Sleep -Milliseconds 1500
}

# bottom action bar: next = rightmost button in the bottom ~170px strip
function Click-Next {
    Dismiss-AppMessage
    $w = Get-AppWindow
    $rect = $w.Current.BoundingRectangle
    $bs = Get-Buttons $w | Where-Object { $_.Top -gt ($rect.Bottom - 170) }
    $next = ($bs | Sort-Object -Property Left -Descending)[0]
    if (-not $next) { throw "next button not found" }
    Write-Host ("  next -> {0}" -f $next.Name)
    Click-Element $next
}

function Click-HomeCard([int]$idx) {
    $w = Get-AppWindow
    $rect = $w.Current.BoundingRectangle
    $bs = @(Get-Buttons $w | Sort-Object -Property Top)
    Write-Host ("  window rect L={0} T={1} R={2} B={3}" -f $rect.Left, $rect.Top, $rect.Right, $rect.Bottom)
    Write-Host ("  home buttons: {0}" -f $bs.Count)
    $i = 0
    foreach ($b in $bs) {
        Write-Host ("    [{0}] L={1} T={2} W={3} H={4}" -f $i, $b.Left, $b.Top, $b.Width, $b.Height)
        $i++
    }
    if ($bs.Count -lt ($idx + 1)) { throw "home card $idx not found" }
    Click-Element $bs[$idx]
    Start-Sleep -Milliseconds 800
    $bs2 = @(Get-Buttons (Get-AppWindow))
    Write-Host ("  after card click, visible buttons: {0}" -f $bs2.Count)
}

function Click-Browse {
    $w = Get-AppWindow
    $rect = $w.Current.BoundingRectangle
    # browse button sits inside the centered card (above the bottom action
    # bar); buttons expose empty UIA names, so pick by position instead
    $all = @(Get-Buttons $w)
    foreach ($b in $all) {
        Write-Host ("    btn L={0} T={1} W={2} H={3}" -f $b.Left, $b.Top, $b.Width, $b.Height)
    }
    $bs = $all | Where-Object { $_.Top -lt ($rect.Bottom - 170) }
    $b2 = ($bs | Sort-Object -Property Left -Descending)[0]
    if (-not $b2) { throw "browse button not found" }
    Write-Host ("  browse at {0},{1}" -f $b2.Left, $b2.Top)
    Click-Element $b2 -Mouse   # UIA Invoke is a no-op on this button
}

function Find-Dlg {
    foreach ($title in @("选择图片文件夹", "选择图片文件")) {
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $title)
        # owned dialogs are nested under the owner window in the UIA view
        $win = Get-AppWindow
        if ($win) {
            $dlg = $win.FindFirst([System.Windows.Automation.TreeScope]::Subtree, $cond)
            if ($dlg) { return $dlg }
        }
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $dlg = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        if ($dlg) { return $dlg }
    }
    return $null
}

function Wait-Dialog([int]$seconds = 10) {
    for ($i = 0; $i -lt ($seconds * 4); $i++) {
        $dlg = Find-Dlg
        if ($dlg) { return $dlg }
        Start-Sleep -Milliseconds 250
    }
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Window)
    $win = Get-AppWindow
    if ($win) {
        Write-Host "    windows under Vchange:"
        foreach ($w in $win.FindAll([System.Windows.Automation.TreeScope]::Subtree, $cond)) {
            Write-Host ("      '{0}' cls={1}" -f $w.Current.Name, $w.Current.ClassName)
        }
    }
    throw "dialog did not appear"
}

function Dialog-IsOpen {
    return (Find-Dlg)
}

# Close the app's own modal message box (提示 / 知道了) if one is up.
function Dismiss-AppMessage {
    try {
        $main = Get-AppWindow
        if (-not $main) { return }
        $mpid = $main.Current.ProcessId
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $wcond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Window)
        $bcond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)
        foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $wcond)) {
            if ($w.Current.ProcessId -ne $mpid) { continue }
            if ($w.Current.Name -eq "Vchange") { continue }
            foreach ($b in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bcond)) {
                $n = $b.Current.Name
                if ($n -like "*知道了*" -or $n -like "*确定*" -or $n -like "*关闭*") {
                    try {
                        ($b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
                        Write-Host ("    (dismissed app message: {0})" -f $n)
                        Start-Sleep -Milliseconds 500
                    } catch { }
                    return
                }
            }
        }
    } catch { }
}

# Text of the visible single-line edit (path textbox) in the main window.
function Get-AppEditValue {
    $w = Get-AppWindow
    if (-not $w) { return $null }
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)
    foreach ($e in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($e.Current.IsOffscreen) { continue }
        try {
            $vp = $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            return [string]$vp.Current.Value
        } catch { }
    }
    return $null
}

# Visible edit elements inside a dialog.
function Find-DlgEdits($dlg) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)
    $out = @()
    foreach ($e in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if (-not $e.Current.IsOffscreen) { $out += $e }
    }
    return $out
}

# Folder picker: focus address bar, set path via UIA with read-back
# verification, Enter to navigate, invoke confirm — then VERIFY the app's
# path textbox; retry the whole cycle on failure.
function Pick-Folder([string]$path) {
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Write-Host ("  pick-folder attempt {0}" -f $attempt)
        $dlg = Wait-Dialog
        Start-Sleep -Milliseconds 1500          # let the shell dialog settle
        $h = [IntPtr]$dlg.Current.NativeWindowHandle

        [ShotWin32]::ForceForeground($h) | Out-Null
        Start-Sleep -Milliseconds 400
        [System.Windows.Forms.SendKeys]::SendWait("%d")
        Start-Sleep -Milliseconds 600

        $set = $false
        for ($i = 0; $i -lt 5 -and -not $set; $i++) {
            $dlg = Dialog-IsOpen
            if (-not $dlg) { break }
            foreach ($e in (Find-DlgEdits $dlg)) {
                try {
                    $vp = $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
                    $vp.SetValue($path)
                    Start-Sleep -Milliseconds 350
                    if ([string]$vp.Current.Value -eq $path) { $set = $true; break }
                } catch { }
            }
            if (-not $set) { Start-Sleep -Milliseconds 500 }
        }
        Write-Host ("    address set via uia: {0}" -f $set)
        if (-not $set) {
            [ShotWin32]::ForceForeground($h) | Out-Null
            Start-Sleep -Milliseconds 300
            [System.Windows.Forms.SendKeys]::SendWait("%d")
            Start-Sleep -Milliseconds 400
            [System.Windows.Forms.SendKeys]::SendWait("^a")
            [System.Windows.Forms.SendKeys]::SendWait($path)
            Start-Sleep -Milliseconds 400
        }

        [ShotWin32]::ForceForeground($h) | Out-Null
        Start-Sleep -Milliseconds 250
        [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
        Start-Sleep -Milliseconds 1300

        $btn = $null
        for ($i = 0; $i -lt 6; $i++) {
            $dlg = Dialog-IsOpen
            if (-not $dlg) { break }
            $bcond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)
            foreach ($b in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bcond)) {
                if ($b.Current.Name -like "*选择文件夹*") { $btn = $b; break }
            }
            if ($btn) { break }
            Start-Sleep -Milliseconds 400
        }
        if ($btn) {
            try {
                ($btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
                Write-Host "    confirm invoked"
            } catch {
                [ShotWin32]::ForceForeground($h) | Out-Null
                [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
            }
            Start-Sleep -Milliseconds 1400
        } elseif (Dialog-IsOpen) {
            [ShotWin32]::ForceForeground($h) | Out-Null
            [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
            Start-Sleep -Milliseconds 1400
        }

        $val = Get-AppEditValue
        Write-Host ("    app path field: '{0}'" -f $val)
        if ($val -eq $path) { Write-Host "  folder picked (verified)"; return }

        Dismiss-AppMessage
        $dlg = Dialog-IsOpen
        if ($dlg) {
            $h2 = [IntPtr]$dlg.Current.NativeWindowHandle
            [ShotWin32]::ForceForeground($h2) | Out-Null
            Start-Sleep -Milliseconds 250
            [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
            Start-Sleep -Milliseconds 700
        }
        Click-Browse
        Start-Sleep -Milliseconds 600
    }
    throw "folder pick verification failed"
}

# File picker: set the filename box via UIA with read-back verification,
# invoke Open, then VERIFY the app's source path textbox; retry on failure.
function Pick-File([string]$path) {
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Write-Host ("  pick-file attempt {0}" -f $attempt)
        $dlg = Wait-Dialog
        Start-Sleep -Milliseconds 1500
        $h = [IntPtr]$dlg.Current.NativeWindowHandle

        $set = $false
        for ($i = 0; $i -lt 6 -and -not $set; $i++) {
            $dlg = Dialog-IsOpen
            if (-not $dlg) { break }
            $cands = @()
            foreach ($ct in @([System.Windows.Automation.ControlType]::Edit,
                              [System.Windows.Automation.ControlType]::ComboBox)) {
                $cond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct)
                foreach ($e in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
                    if ($e.Current.Name -like "*文件名*") { $cands += $e }
                }
            }
            if ($cands.Count -eq 0) { $cands = @(Find-DlgEdits $dlg) }
            foreach ($e in $cands) {
                try {
                    $vp = $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
                    $vp.SetValue($path)
                    Start-Sleep -Milliseconds 350
                    if ([string]$vp.Current.Value -eq $path) { $set = $true; break }
                } catch { }
            }
            if (-not $set) { Start-Sleep -Milliseconds 500 }
        }
        Write-Host ("    filename set via uia: {0}" -f $set)

        if ($set) {
            for ($i = 0; $i -lt 5; $i++) {
                $dlg = Dialog-IsOpen
                if (-not $dlg) { break }
                $bcond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Button)
                $openBtn = $null
                foreach ($b in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bcond)) {
                    if ($b.Current.Name -like "打开*") { $openBtn = $b; break }
                }
                if ($openBtn) {
                    try {
                        ($openBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
                        Write-Host "    open invoked"
                    } catch { }
                    Start-Sleep -Milliseconds 1500
                    if (-not (Dialog-IsOpen)) { break }
                }
                Start-Sleep -Milliseconds 500
            }
        }

        if (Dialog-IsOpen) {
            # fallback: keyboard into filename box
            $dlg = Dialog-IsOpen
            $h = [IntPtr]$dlg.Current.NativeWindowHandle
            [ShotWin32]::ForceForeground($h) | Out-Null
            Start-Sleep -Milliseconds 350
            [System.Windows.Forms.SendKeys]::SendWait("%n")
            Start-Sleep -Milliseconds 400
            [System.Windows.Forms.SendKeys]::SendWait("^a")
            [System.Windows.Forms.SendKeys]::SendWait($path)
            Start-Sleep -Milliseconds 400
            [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
            Start-Sleep -Milliseconds 1800
        }

        $val = Get-AppEditValue
        Write-Host ("    app path field: '{0}'" -f $val)
        if ($val -eq $path) { Write-Host "  file picked (verified)"; return }

        Dismiss-AppMessage
        $dlg = Dialog-IsOpen
        if ($dlg) {
            $h2 = [IntPtr]$dlg.Current.NativeWindowHandle
            [ShotWin32]::ForceForeground($h2) | Out-Null
            Start-Sleep -Milliseconds 250
            [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
            Start-Sleep -Milliseconds 700
        }
        Click-Browse
        Start-Sleep -Milliseconds 600
    }
    throw "file pick verification failed"
}

function Capture([string]$name) {
    Dismiss-AppMessage
    $win = Get-AppWindow
    if (-not $win) { throw "app window missing for capture $name" }
    $h = [IntPtr]$win.Current.NativeWindowHandle
    [ShotWin32]::SetForegroundWindow($h) | Out-Null
    $r = New-Object ShotWin32+RECT
    [ShotWin32]::GetWindowRect($h, [ref]$r) | Out-Null
    [ShotWin32]::SetCursorPos(($r.Left + 25), ($r.Bottom - 120)) | Out-Null
    Start-Sleep -Milliseconds 500
    $w = $r.Right - $r.Left
    $ht = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $ht)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $ht)))
    $bmp.Save("$shots\$name", [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "captured: $name"
}

function Try-Scroll-PageDown {
    try {
        $w = Get-AppWindow
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ScrollViewer)
        $all = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
        $sv = $null
        foreach ($s in $all) {
            if (-not $s.Current.IsOffscreen) { $sv = $s; break }
        }
        if (-not $sv) { Write-Host "  (no visible scrollviewer)"; return }
        $pattern = $sv.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
        $pattern.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount,
                        [System.Windows.Automation.ScrollAmount]::PageDown)
        Start-Sleep -Milliseconds 700
        Write-Host "  (scrolled one page)"
    } catch { Write-Host "  (scroll skipped)" }
}

$failures = @()

# ---------------- 1. home screen ----------------
try {
    Write-Host "== home"
    Start-App @()
    Capture "home.png"
} catch { $failures += "home: $_"; Write-Host "FAIL home: $_" }

# ---------------- 2. video params page (new 2x2 + color space) ----------------
try {
    Write-Host "== video params"
    Start-App @("--input", $demoVideo, "--output", "$env:TEMP\demo_out.mp4")
    Click-Next            # step1 -> step2
    Click-Next            # step2 -> step3 (params)
    Capture "cv-params.png"
} catch { $failures += "cv-params: $_"; Write-Host "FAIL cv-params: $_" }

# ---------------- 3. timelapse flow ----------------
try {
    Write-Host "== timelapse"
    Start-App @()
    Click-HomeCard 1      # 延时合成
    Click-Browse
    Pick-Folder $imageFolder
    Click-Next            # T1 -> T2 (rename plan)
    Capture "tl-plan.png"
    Click-Next            # T2 -> T3
    Click-Next            # T3 -> T4
    Click-Next            # T4 -> T5
    Click-Next            # T5 -> T6 (summary / compose)
    Capture "tl-summary.png"
} catch { $failures += "timelapse: $_"; Write-Host "FAIL timelapse: $_" }

# ---------------- 4. stacking flow ----------------
try {
    Write-Host "== stacking"
    Start-App @()
    Click-HomeCard 2      # 图片堆砌
    Click-Browse
    Pick-Folder $imageFolder
    Capture "st-mode.png" # folder + blend mode
    Click-Next            # S1 -> S2 (format)
    Capture "st-format.png"
    Click-Next            # S2 -> S3 (quality)
    Click-Next            # S3 -> S4 (summary/log)
    Capture "st-summary.png"
} catch { $failures += "stacking: $_"; Write-Host "FAIL stacking: $_" }

# ---------------- 5. image conversion flow ----------------
try {
    Write-Host "== image convert"
    Start-App @()
    Click-HomeCard 3      # 图片转换
    Click-Browse
    Pick-File $imageFile
    Click-Next            # C1 -> C2 (format)
    Capture "img-format.png"
    Click-Next            # C2 -> C3 (quality & options)
    Try-Scroll-PageDown
    Capture "img-options.png"
} catch { $failures += "image: $_"; Write-Host "FAIL image: $_" }

Stop-App

if ($failures) {
    Write-Host "---- FAILURES ----"
    $failures | ForEach-Object { Write-Host $_ }
    exit 1
}
Write-Host "ALL DONE"
