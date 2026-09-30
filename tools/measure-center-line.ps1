# Measures the black center band in scene screenshots with sub-pixel precision.
# For every row, the image under the band is estimated by a straight line between
# the pixels just outside it; darkness = 1 - actual / estimate. The darkness profile,
# averaged over all rows, gives the band center (centroid) and its width (integral).
# Usage: .\tools\measure-center-line.ps1 screenshots\flat-*.png
param([Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)][string[]]$Paths)

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class CenterLine
{
    // Returns { centroid offset from the exact image center, integrated width } in pixels.
    public static double[] Measure(string path, int reach)
    {
        using (var bmp = new Bitmap(path))
        {
            int w = bmp.Width, h = bmp.Height;
            int x0 = w / 2 - reach, x1 = w / 2 + reach - 1; // reference columns outside the band
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var row = new byte[w * 4];
            var dark = new double[x1 - x0 + 1];
            int rows = 0;
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, w * 4);
                Func<int, double> lum = x => 0.0722 * row[x * 4] + 0.7152 * row[x * 4 + 1] + 0.2126 * row[x * 4 + 2];
                double a = lum(x0), b = lum(x1);
                if (a < 40 || b < 40) continue; // too dark to measure reliably
                for (int x = x0; x <= x1; x++)
                {
                    double est = a + (b - a) * (x - x0) / (double)(x1 - x0);
                    dark[x - x0] += Math.Max(0.0, Math.Min(1.0, 1.0 - lum(x) / est));
                }
                rows++;
            }
            bmp.UnlockBits(data);
            double sum = 0, moment = 0;
            for (int i = 0; i < dark.Length; i++)
            {
                double d = dark[i] / Math.Max(rows, 1);
                double centerOfPixel = x0 + i + 0.5;
                sum += d;
                moment += d * centerOfPixel;
            }
            return new[] { moment / sum - w / 2.0, sum, rows };
        }
    }
}
"@

foreach ($pattern in $Paths) {
    foreach ($file in Get-ChildItem $pattern) {
        $img = [System.Drawing.Image]::FromFile($file.FullName); $w = $img.Width; $h = $img.Height; $img.Dispose()
        $target = 6.0 * $h / 1080
        $reach = [int]([Math]::Ceiling($target * 1.2)) + 3
        $m = [CenterLine]::Measure($file.FullName, $reach)
        "{0,-28} {1}x{2}  center offset {3:+0.00;-0.00} px  width {4:N2} px (setting 6 px at 1080p = {5:N2})  rows used {6}" -f `
            $file.Name, $w, $h, $m[0], $m[1], $target, $m[2]
    }
}
