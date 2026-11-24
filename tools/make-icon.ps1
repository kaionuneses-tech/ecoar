# Draws the Ecoar icon (speaker + sound waves on a purple-to-cyan tile) and writes a
# multi-size .ico (PNG frames 16..256) plus a 256 px preview PNG.
param([string]$OutDir = (Join-Path $PSScriptRoot '..\app'))
Add-Type -AssemblyName System.Drawing

function New-Frame([int]$s) {
  $bmp = New-Object Drawing.Bitmap $s, $s
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.Clear([Drawing.Color]::Transparent)
  # Rounded tile with diagonal gradient.
  $r = [Math]::Max(2, $s * 0.22); $d = 2 * $r
  $tile = New-Object Drawing.Drawing2D.GraphicsPath
  $tile.AddArc(0, 0, $d, $d, 180, 90); $tile.AddArc($s - $d - 1, 0, $d, $d, 270, 90)
  $tile.AddArc($s - $d - 1, $s - $d - 1, $d, $d, 0, 90); $tile.AddArc(0, $s - $d - 1, $d, $d, 90, 90); $tile.CloseFigure()
  $grad = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Point 0, 0), (New-Object Drawing.Point $s, $s), ([Drawing.Color]::FromArgb(255, 124, 58, 237)), ([Drawing.Color]::FromArgb(255, 6, 182, 212))
  $g.FillPath($grad, $tile)
  # Speaker body + cone.
  $white = [Drawing.Brushes]::White
  $u = $s / 32.0
  $g.FillRectangle($white, [float](6 * $u), [float](12 * $u), [float](5 * $u), [float](8 * $u))
  $cone = [Drawing.PointF[]]@((New-Object Drawing.PointF (10 * $u), (12 * $u)), (New-Object Drawing.PointF (16 * $u), (7 * $u)), (New-Object Drawing.PointF (16 * $u), (25 * $u)), (New-Object Drawing.PointF (10 * $u), (20 * $u)))
  $g.FillPolygon($white, $cone)
  # Sound waves (fewer at tiny sizes so they stay readable).
  $pen = New-Object Drawing.Pen ([Drawing.Color]::White), ([float][Math]::Max(1.5, 2.4 * $u))
  $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
  $waves = if ($s -le 16) { @(6) } else { @(5, 10) }
  foreach ($w in $waves) { $g.DrawArc($pen, [float]((16 - $w) * $u + 2 * $u), [float]((16 - $w) * $u), [float](2 * $w * $u), [float](2 * $w * $u), -50, 100) }
  $g.Dispose(); $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) { $f = New-Frame $s; $ms = New-Object IO.MemoryStream; $f.Save($ms, [Drawing.Imaging.ImageFormat]::Png); if ($s -eq 256) { $f.Save((Join-Path $OutDir 'ecoar-icon-preview.png')) }; $f.Dispose(); ,$ms.ToArray() }

$ico = Join-Path $OutDir 'ecoar.ico'
$fs = [IO.File]::Create($ico); $w = New-Object IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $dim = [byte]($(if ($s -ge 256) { 0 } else { $s }))
  $w.Write($dim); $w.Write($dim); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
"wrote $ico ($((Get-Item $ico).Length) bytes, sizes: $($sizes -join ','))"
