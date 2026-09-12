<#
.SYNOPSIS
    LDv2rayN Automated UI Smoke Test
.DESCRIPTION
    Tests tray icon, floating button, AI chat window, menus via UI Automation.
.NOTES
    powershell -ExecutionPolicy Bypass -File SmokeTest.ps1
#>
param(
    [string]$ExePath = "D:\LUODA\LDv2rayN\v2rayN\bin\Release\net10.0-windows10.0.19041.0\win-x64\LDv2rayN.exe",
    [int]$WaitSec = 5
)

$ErrorActionPreference = "Continue"
$script:PassCount = 0
$script:FailCount = 0
$script:Results = [System.Collections.ArrayList]::new()

# --- helpers ---
function Log([string]$msg) { Write-Host $msg -ForegroundColor Cyan }
function DoPass([string]$item, [string]$detail) {
    $script:PassCount++
    [void]$script:Results.Add([PSCustomObject]@{ Item=$item; Status="PASS"; Detail=$detail })
    Write-Host "  PASS $item $detail" -ForegroundColor Green
}
function DoFail([string]$item, [string]$detail) {
    $script:FailCount++
    [void]$script:Results.Add([PSCustomObject]@{ Item=$item; Status="FAIL"; Detail=$detail })
    Write-Host "  FAIL $item $detail" -ForegroundColor Red
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$uia = [System.Windows.Automation.AutomationElement]

function Find-All {
    param($Root, [string]$Name="", [string]$CtrlType="", [int]$TimeoutMs=3000)
    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)
    while ($true) {
        $cond = [System.Windows.Automation.Condition]::TrueCondition
        if ($Name -and $CtrlType) {
            $nCond = New-Object System.Windows.Automation.PropertyCondition($uia::NameProperty, $Name)
            $cCond = New-Object System.Windows.Automation.PropertyCondition($uia::ControlTypeProperty, $CtrlType)
            $and = New-Object System.Windows.Automation.AndCondition($nCond, $cCond)
            $cond = $and
        } elseif ($Name) {
            $cond = New-Object System.Windows.Automation.PropertyCondition($uia::NameProperty, $Name)
        } elseif ($CtrlType) {
            $cond = New-Object System.Windows.Automation.PropertyCondition($uia::ControlTypeProperty, $CtrlType)
        }
        $found = $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($found.Count -gt 0) { return $found }
        if ([DateTime]::UtcNow -ge $deadline) { return $null }
        Start-Sleep -Milliseconds 200
    }
}

function Find-One {
    param($Root, [string]$Name="", [string]$CtrlType="", [int]$TimeoutMs=3000)
    $all = Find-All -Root $Root -Name $Name -CtrlType $CtrlType -TimeoutMs $TimeoutMs
    if ($all -and $all.Count -gt 0) { return $all[0] }
    return $null
}

function Invoke-ClickEl($el) {
    try {
        $pat = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pat.Invoke()
        return $true
    } catch {}
    try {
        $r = $el.Current.BoundingRectangle
        if ($r -eq [System.Windows.Rect]::Empty) { return $false }
        $x = [int]($r.X + $r.Width / 2)
        $y = [int]($r.Y + $r.Height / 2)
        [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($x, $y)
        Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class Clicker { [DllImport("user32.dll")] public static extern void mouse_event(int f,int x,int y,0,0); }' -ErrorAction SilentlyContinue
        [Clicker]::mouse_event(0x0002,0,0,0,0) # left down
        [Clicker]::mouse_event(0x0004,0,0,0,0) # left up
        Start-Sleep -Milliseconds 200
        return $true
    } catch { return $false }
}

function Cleanup {
    Get-Process -Name "LDv2rayN" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

# ===========================================================
Cleanup
Log ""
Log "=========================================="
Log " LDv2rayN UI Smoke Test"
Log "=========================================="
Log ""

# --- 0.1 Launch ---
Log "0.1 Starting EXE..."
if (-not (Test-Path $ExePath)) { DoFail "0.1" "EXE not found: $ExePath"; return }
Start-Process $ExePath
Start-Sleep -Seconds $WaitSec
$proc = Get-Process -Name "LDv2rayN" -ErrorAction SilentlyContinue
if ($proc) { DoPass "0.1" "PID $($proc.Id) mem $([math]::Round($proc.WorkingSet64/1MB,0))MB" }
else { DoFail "0.1" "Process not running"; return }

# --- 0.2 Core ---
Log "0.2 Checking core status..."
try {
    $resp = Invoke-RestMethod -Uri "http://127.0.0.1:26066/ai/status" -TimeoutSec 10
    $core = $resp.data.runningCore
    if ($core -and $core -ne "0") { DoPass "0.2" "Core: $core" }
    else { DoFail "0.2" "Core not running: $core" }
} catch { DoFail "0.2" "API unreachable" }

$desktop = $uia::RootElement

# --- 1.1 Tray ---
Log "1.1 Checking system tray..."
$tray = Find-One -Root $desktop -Name "System Tray" -TimeoutMs 2000
if ($tray) { DoPass "1.1" "System Tray found" }
else { DoFail "1.1" "System Tray not found" }

# --- 2.1 Main window ---
Log "2.1 Finding main window..."
$mainWin = Find-One -Root $desktop -Name "LDv2rayN" -TimeoutMs 5000
if ($mainWin) { DoPass "2.1" "Main window found" }
else { DoFail "2.1" "Main window not found"; return }

# --- 2.2 Floating button ---
Log "2.2 Finding AI floating button..."
$aiBtn = Find-One -Root $mainWin -Name "AI" -TimeoutMs 3000
if (-not $aiBtn) {
    # try tooltip name
    $aiBtn = Find-One -Root $mainWin -Name "AI " -TimeoutMs 1000
}
if ($aiBtn) { DoPass "2.2" "AI button found" }
else { DoFail "2.2" "AI floating button not found" }

# --- 2.2 Click → AI chat ---
Log "2.3 Clicking AI button..."
if ($aiBtn) {
    Invoke-ClickEl $aiBtn | Out-Null
    Start-Sleep -Seconds 2
    $aiWin = Find-One -Root $desktop -Name "AI " -TimeoutMs 3000
    if ($aiWin) { DoPass "3.1" "AI chat window opened" }
    else { DoFail "3.1" "AI chat window did not open" }
} else { $aiWin = $null; DoFail "3.1" "Cannot test - button not found" }

# --- 3.2 Header buttons ---
Log "3.2 Checking header buttons..."
if ($aiWin) {
    $btns = Find-All -Root $aiWin -CtrlType "Button" -TimeoutMs 2000
    $btnNames = @()
    if ($btns) { foreach ($b in $btns) { $btnNames += $b.Current.Name } }
    if ($btnNames.Count -ge 3) { DoPass "3.2" "Found $($btnNames.Count) buttons: $($btnNames -join ', ')" }
    else { DoFail "3.2" "Only $($btnNames.Count) buttons found" }
} else { DoFail "3.2" "Cannot test" }

# --- 3.5 Settings button ---
Log "3.5 Testing settings button..."
if ($aiWin) {
    $settingsBtn = Find-One -Root $aiWin -Name "AI " -TimeoutMs 1000
    if (-not $settingsBtn) {
        $settingsBtn = Find-One -Root $aiWin -CtrlType "Button" -TimeoutMs 1000
    }
    if ($settingsBtn) {
        Invoke-ClickEl $settingsBtn | Out-Null
        Start-Sleep -Seconds 2
        $dlg = Find-One -Root $desktop -CtrlType "Window" -TimeoutMs 3000
        $dlgName = ""
        if ($dlg) { $dlgName = $dlg.Current.Name }
        if ($dlgName -and $dlgName -ne "LDv2rayN" -and $dlgName -ne "AI ") {
            DoPass "3.5" "Settings dialog opened: $dlgName"
            $closeDlg = Find-One -Root $dlg -Name "Close" -TimeoutMs 1000
            if ($closeDlg) { Invoke-ClickEl $closeDlg | Out-Null; Start-Sleep -Milliseconds 500 }
        } else { DoFail "3.5" "Settings dialog did not open" }
    } else { DoFail "3.5" "Settings button not found" }
} else { DoFail "3.5" "Cannot test" }

# --- 3.8 Input + Send ---
Log "3.8 Testing text input..."
if ($aiWin) {
    $inputBox = Find-One -Root $aiWin -CtrlType "Edit" -TimeoutMs 2000
    if ($inputBox) {
        try {
            $vp = $inputBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            $vp.SetValue("hello test")
            Start-Sleep -Milliseconds 300
            DoPass "3.8" "Text entered into input box"
        } catch { DoFail "3.8" "Could not set value: $_" }
    } else { DoFail "3.8" "Input box not found" }
} else { DoFail "3.8" "Cannot test" }

# --- 3.6 Minimize ---
Log "3.6 Testing minimize..."
if ($aiWin) {
    $minBtn = Find-One -Root $aiWin -CtrlType "Button" -TimeoutMs 1000
    if ($minBtn) {
        Invoke-ClickEl $minBtn | Out-Null
        Start-Sleep -Milliseconds 500
        DoPass "3.6" "Minimize clicked"
    } else { DoFail "3.6" "Minimize button not found" }
} else { DoFail "3.6" "Cannot test" }

# --- 3.7 Re-open (cache) ---
Log "3.7 Testing cached re-open..."
if ($aiBtn) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Invoke-ClickEl $aiBtn | Out-Null
    Start-Sleep -Seconds 1
    $aiWin2 = Find-One -Root $desktop -Name "AI " -TimeoutMs 3000
    $sw.Stop()
    if ($aiWin2) { DoPass "3.7" "Re-opened in $($sw.ElapsedMilliseconds)ms" }
    else { DoFail "3.7" "Window did not re-open" }
} else { DoFail "3.7" "Cannot test" }

# --- Close AI window ---
$aiWinClose = Find-One -Root $desktop -Name "AI " -TimeoutMs 1000
if ($aiWinClose) {
    $closeBtn = Find-One -Root $aiWinClose -Name "Close" -TimeoutMs 1000
    if ($closeBtn) { Invoke-ClickEl $closeBtn | Out-Null; Start-Sleep -Milliseconds 500 }
}

# --- 6.1 Menu: AI Setting ---
Log "6.1 Testing menu AI Setting..."
$mainWin2 = Find-One -Root $desktop -Name "LDv2rayN" -TimeoutMs 2000
if ($mainWin2) {
    $menuBar = Find-One -Root $mainWin2 -CtrlType "MenuBar" -TimeoutMs 2000
    if ($menuBar) {
        $menus = Find-All -Root $menuBar -CtrlType "MenuItem" -TimeoutMs 2000
        $foundSetting = $false
        if ($menus) {
            foreach ($m in $menus) {
                $mName = $m.Current.Name
                if ($mName -like "*AI*" -or $mName -like "*智能*") {
                    Invoke-ClickEl $m | Out-Null
                    Start-Sleep -Seconds 2
                    $dlg = Find-One -Root $desktop -CtrlType "Window" -TimeoutMs 3000
                    if ($dlg -and $dlg.Current.Name -ne "LDv2rayN") {
                        DoPass "6.1" "AI setting opened via menu"
                        $c = Find-One -Root $dlg -Name "Close" -TimeoutMs 1000
                        if ($c) { Invoke-ClickEl $c | Out-Null; Start-Sleep -Milliseconds 500 }
                        $foundSetting = $true
                        break
                    }
                }
            }
        }
        if (-not $foundSetting) { DoFail "6.1" "AI setting menu item not found or did not open" }
    } else { DoFail "6.1" "MenuBar not found" }
} else { DoFail "6.1" "Main window not found" }

# --- 6.2 Menu: AI Log ---
Log "6.2 Testing menu AI Log..."
$mainWin3 = Find-One -Root $desktop -Name "LDv2rayN" -TimeoutMs 2000
if ($mainWin3) {
    $menuBar = Find-One -Root $mainWin3 -CtrlType "MenuBar" -TimeoutMs 2000
    if ($menuBar) {
        $menus = Find-All -Root $menuBar -CtrlType "MenuItem" -TimeoutMs 2000
        $foundLog = $false
        if ($menus) {
            foreach ($m in $menus) {
                $mName = $m.Current.Name
                if ($mName -like "*AI*" -or $mName -like "*日志*" -or $mName -like "*Log*") {
                    Invoke-ClickEl $m | Out-Null
                    Start-Sleep -Seconds 2
                    $dlg = Find-One -Root $desktop -CtrlType "Window" -TimeoutMs 3000
                    if ($dlg -and $dlg.Current.Name -ne "LDv2rayN") {
                        DoPass "6.2" "AI log opened via menu"
                        $c = Find-One -Root $dlg -Name "Close" -TimeoutMs 1000
                        if ($c) { Invoke-ClickEl $c | Out-Null; Start-Sleep -Milliseconds 500 }
                        $foundLog = $true
                        break
                    }
                }
            }
        }
        if (-not $foundLog) { DoFail "6.2" "AI log menu item not found or did not open" }
    } else { DoFail "6.2" "MenuBar not found" }
} else { DoFail "6.2" "Main window not found" }

# --- Cleanup ---
Log ""
Log "Cleaning up..."
Cleanup

# --- Summary ---
$total = $script:PassCount + $script:FailCount
Log ""
Log "=========================================="
Log " RESULTS: $script:PassCount / $total PASSED"
Log "=========================================="
if ($script:FailCount -gt 0) {
    Log ""
    Log "FAILED ITEMS:"
    foreach ($r in $script:Results) {
        if ($r.Status -eq "FAIL") {
            Write-Host "  $($r.Item): $($r.Detail)" -ForegroundColor Red
        }
    }
}
Log ""
$script:Results | Format-Table -AutoSize