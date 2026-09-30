# Tests the /p protocol: creates a parent window with a small child area like the
# monitor in the Windows screen saver dialog, starts "/p <hwnd>", captures it,
# then destroys the parent and checks that the preview process exits by itself.
# Usage: .\tools\test-preview.ps1 [-Exe publish\ScreensaverStargate.exe] [-Out screenshots\raw\preview-p.png]
param(
    [string]$Exe = "publish\ScreensaverStargate.exe",
    [string]$Out = "screenshots\raw\preview-p.png"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class PW
{
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
}
"@

$form = New-Object System.Windows.Forms.Form
$form.Text = "Preview host"
$form.FormBorderStyle = "FixedDialog"
$form.ClientSize = New-Object System.Drawing.Size 240, 180
$form.StartPosition = "CenterScreen"
$panel = New-Object System.Windows.Forms.Panel
$panel.Location = New-Object System.Drawing.Point 44, 34
$panel.Size = New-Object System.Drawing.Size 152, 112
$panel.BackColor = [System.Drawing.Color]::Gray
$form.Controls.Add($panel)
$form.Show()
[System.Windows.Forms.Application]::DoEvents()

$hwnd = $panel.Handle.ToInt64()
$p = Start-Process -FilePath $Exe -ArgumentList "/p $hwnd" -PassThru
for ($i = 0; $i -lt 30; $i++) { Start-Sleep -Milliseconds 100; [System.Windows.Forms.Application]::DoEvents() }

$child = [PW]::GetWindow($panel.Handle, 5) # GW_CHILD
Write-Host ("Child window created inside the parent: {0}" -f ($child -ne [IntPtr]::Zero))

$bmp = New-Object System.Drawing.Bitmap $form.Width, $form.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc(); [PW]::PrintWindow($form.Handle, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc); $g.Dispose()
New-Item -ItemType Directory -Force (Split-Path -Parent (Join-Path (Get-Location) $Out)) | Out-Null
$bmp.Save((Join-Path (Get-Location) $Out)); $bmp.Dispose()
Write-Host "Saved $Out"

$form.Close(); $form.Dispose()
$exited = $p.WaitForExit(5000)
Write-Host ("Preview process exited after the parent was destroyed: {0}" -f $exited)
if (-not $exited) { Stop-Process -Id $p.Id; exit 1 }
