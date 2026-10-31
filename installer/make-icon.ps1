# Writes RgbStudio\app.ico: the tray icon's design (gradient disc, white dot) at 16-256 px.
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\RgbStudio\app.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $k = $s / 32.0
    $rect = New-Object System.Drawing.Rectangle 0, 0, $s, $s
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 45, 85)), ([System.Drawing.Color]::FromArgb(124, 77, 255)), 45
    $g.FillEllipse($fill, [single](1 * $k), [single](1 * $k), [single](30 * $k), [single](30 * $k))
    $g.FillEllipse([System.Drawing.Brushes]::White, [single](11 * $k), [single](11 * $k), [single](10 * $k), [single](10 * $k))
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}
$fs = [IO.File]::Create($out); $w = New-Object IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
