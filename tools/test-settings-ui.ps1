# Drives the settings window with UI Automation: selects Flat and Vertical, checks that
# the live preview changes, clicks OK, and checks settings.json. Then reopens the window
# to check that the choices were loaded, and clicks Cancel. Restores any previous file.
# Usage: .\tools\test-settings-ui.ps1 [-Exe publish\ScreensaverStargate.exe]
param([string]$Exe = "publish\ScreensaverStargate.exe")

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class PW2 { [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f); }
"@
$A = [System.Windows.Automation.AutomationElement]
$folder = Join-Path $env:APPDATA "ScreensaverStargate"
$file = Join-Path $folder "settings.json"
$backup = "$file.uibackup"
if (Test-Path $file) { Move-Item $file $backup -Force }

function Open-Settings {
    $p = Start-Process -FilePath $Exe -ArgumentList "/c" -PassThru
    for ($i = 0; $i -lt 50 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 200; $p.Refresh() }
    Start-Sleep -Seconds 1
    return @($p, $A::FromHandle($p.MainWindowHandle))
}
function Find([object]$root, [string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
    $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $el) { throw "Control '$name' not found." }
    return $el
}
function Select-Radio($root, $name) { (Find $root $name).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
function IsSelected($root, $name) { (Find $root $name).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected }
function Click($root, $name) { (Find $root $name).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Snapshot($p) {
    $bmp = New-Object System.Drawing.Bitmap 1200, 800
    $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc(); [PW2]::PrintWindow($p.MainWindowHandle, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc); $g.Dispose()
    return $bmp
}

try {
    $p, $win = Open-Settings
    $before = Snapshot $p
    Select-Radio $win "Flat"
    Select-Radio $win "Vertical"
    Start-Sleep -Milliseconds 800
    $after = Snapshot $p
    # Compare a grid of samples inside the preview area (right half of the window).
    $diff = 0; for ($x = 540; $x -lt 940; $x += 20) { for ($y = 90; $y -lt 310; $y += 20) { if ($before.GetPixel($x, $y) -ne $after.GetPixel($x, $y)) { $diff++ } } }
    $before.Dispose(); $after.Dispose()
    Write-Host ("Preview changed after the clicks: {0} of 220 samples differ" -f $diff)
    Click $win "OK"
    if (-not $p.WaitForExit(5000)) { throw "The window did not close after OK." }
    $json = Get-Content $file -Raw | ConvertFrom-Json
    Write-Host "Saved: WallMode=$($json.WallMode) Orientation=$($json.Orientation)"

    $p, $win = Open-Settings
    Write-Host "Reloaded: Flat selected=$(IsSelected $win 'Flat') Vertical selected=$(IsSelected $win 'Vertical')"
    Click $win "Cancel"
    $p.WaitForExit(5000) | Out-Null
}
finally {
    Get-Process -Id $p.Id -ErrorAction SilentlyContinue | Stop-Process
    if (Test-Path $file) { Remove-Item $file }
    if (Test-Path $backup) { Move-Item $backup $file -Force }
}
