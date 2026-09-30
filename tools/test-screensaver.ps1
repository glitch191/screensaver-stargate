# Tests the /s protocol: full screen start, single instance, and exit on a key,
# a mouse move beyond the threshold and a click. It also captures one full screen
# frame with the diagnostics overlay. The screen is covered for a few seconds per step.
# Usage: .\tools\test-screensaver.ps1 [-Exe publish\ScreensaverStargate.exe]
param(
    [string]$Exe = "publish\ScreensaverStargate.exe",
    [string]$Out = "screenshots\raw\fullscreen-diag.png"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Input
{
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
}
"@

$results = @()
function Start-Saver([string]$extra = "") {
    $p = Start-Process -FilePath $Exe -ArgumentList "/s $extra" -PassThru
    Start-Sleep -Seconds 3
    $p.Refresh()
    if ($p.HasExited) { throw "The screensaver exited on its own (code $($p.ExitCode))." }
    return $p
}
function Check([string]$name, [bool]$ok) {
    $script:results += "{0}  {1}" -f ($(if ($ok) { "PASS" } else { "FAIL" })), $name
}

# 1. Start, diagnostics capture, single instance, exit on a key.
$p = Start-Saver "--diag"
Start-Sleep -Seconds 3
$bmp = New-Object System.Drawing.Bitmap 3440, 1440
$g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
# The full screen window has no taskbar entry, so find it under the cursor.
$cur = New-Object Input+POINT; [Input]::GetCursorPos([ref]$cur) | Out-Null
[Input]::PrintWindow([Input]::WindowFromPoint($cur), $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc); $g.Dispose(); $bmp.Save((Join-Path (Get-Location) $Out)); $bmp.Dispose()
$second = Start-Process -FilePath $Exe -ArgumentList "/s" -PassThru
Check "second /s instance exits immediately" ($second.WaitForExit(3000) -and -not $p.HasExited)
[Input]::keybd_event(0x10, 0, 0, [UIntPtr]::Zero); [Input]::keybd_event(0x10, 0, 2, [UIntPtr]::Zero) # Shift
Check "exits on a key press" ($p.WaitForExit(3000))

# 2. A tiny mouse move (below the threshold) keeps it running; a larger one closes it.
$p = Start-Saver
$pt = New-Object Input+POINT; [Input]::GetCursorPos([ref]$pt) | Out-Null
[Input]::SetCursorPos($pt.X + 3, $pt.Y + 2) | Out-Null
Start-Sleep -Milliseconds 800
$p.Refresh()
Check "ignores a 3 pixel mouse move" (-not $p.HasExited)
[Input]::SetCursorPos($pt.X + 60, $pt.Y + 40) | Out-Null
Check "exits on a mouse move beyond the threshold" ($p.WaitForExit(3000))
[Input]::SetCursorPos($pt.X, $pt.Y) | Out-Null

# 3. Exit on a click.
$p = Start-Saver
[Input]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 50; [Input]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
Check "exits on a mouse click" ($p.WaitForExit(3000))

Get-Process -Id $p.Id -ErrorAction SilentlyContinue | Stop-Process
$results | ForEach-Object { Write-Host $_ }
Write-Host "Full screen capture: $Out"
if ($results -match "^FAIL") { exit 1 }
