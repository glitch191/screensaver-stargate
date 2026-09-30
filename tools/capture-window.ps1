# Starts the program with the given arguments, waits, and saves a screen capture of its main window.
# Usage: .\tools\capture-window.ps1 -Arguments "--windowed --size 1280x720 --diag" -Out shot.png [-Seconds 6] [-Keep]
param(
    [string]$Exe = "publish\ScreensaverStargate.exe",
    [string]$Arguments = "",
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$Seconds = 6,
    [switch]$Keep
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
}
"@

$p = Start-Process -FilePath $Exe -ArgumentList $Arguments -PassThru
$deadline = (Get-Date).AddSeconds(15)
while ($p.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200; $p.Refresh() }
if ($p.MainWindowHandle -eq 0) { throw "No window appeared." }
Start-Sleep -Seconds $Seconds
# PrintWindow with PW_RENDERFULLCONTENT (2) captures the window's own content,
# even when other windows cover it, so nothing else on the desktop is recorded.
$r = New-Object Win+RECT
[Win]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [Win]::PrintWindow($p.MainWindowHandle, $hdc, 2)
$g.ReleaseHdc($hdc); $g.Dispose()
if (-not $ok) { throw "PrintWindow failed." }
New-Item -ItemType Directory -Force (Split-Path -Parent (Join-Path (Get-Location) $Out)) | Out-Null
$bmp.Save((Join-Path (Get-Location) $Out)); $bmp.Dispose()
Write-Host "Saved $Out ($w x $h)"
if (-not $Keep) { Stop-Process -Id $p.Id -ErrorAction SilentlyContinue }
