# Measures the black center band in scene screenshots: where it starts and ends,
# its center compared with the exact image center, and its width at 50% darkness.
# Usage: .\tools\measure-center-line.ps1 screenshots\flat-*.png
param([Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)][string[]]$Paths)

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class CenterLine
{
    // Returns the mean luminance (0-255) of each column.
    public static double[] Columns(string path)
    {
        using (var bmp = new Bitmap(path))
        {
            int w = bmp.Width, h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var row = new byte[w * 4];
            var sum = new double[w];
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, w * 4);
                for (int x = 0; x < w; x++)
                    sum[x] += 0.0722 * row[x * 4] + 0.7152 * row[x * 4 + 1] + 0.2126 * row[x * 4 + 2];
            }
            bmp.UnlockBits(data);
            for (int x = 0; x < w; x++) sum[x] /= h;
            return sum;
        }
    }
}
"@

foreach ($pattern in $Paths) {
    foreach ($file in Get-ChildItem $pattern) {
        $cols = [CenterLine]::Columns($file.FullName)
        $w = $cols.Length
        $mid = [int]($w / 2)
        # Reference brightness: median of the columns 40 to 200 pixels away from the center.
        $ref = @($cols[($mid - 200)..($mid - 40)] + $cols[($mid + 40)..($mid + 200)]) | Sort-Object
        $level = $ref[[int]($ref.Count / 2)]
        $half = $level * 0.5
        $l = $mid; while ($l -gt 0 -and $cols[$l - 1] -lt $half) { $l-- }
        $r = $mid; while ($r -lt $w - 1 -and $cols[$r] -lt $half) { $r++ }
        # Band covers columns l..r-1; its center in pixel-edge coordinates is (l + r) / 2.
        $center = ($l + $r) / 2.0
        $bmp = [System.Drawing.Image]::FromFile($file.FullName); $h = $bmp.Height; $bmp.Dispose()
        "{0,-32} {1}x{2}  band {3}..{4}  width {5} px (target {6:N1})  center {7} vs {8}  offset {9}" -f `
            $file.Name, $w, $h, $l, ($r - 1), ($r - $l), (6.0 * $h / 1080), $center, ($w / 2.0), ($center - $w / 2.0)
    }
}
