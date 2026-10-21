# Renders src/MCG.Launcher/Assets/AppIcon.ico and AppIcon.png from the same geometry as AppIcon.svg.
# Windows PowerShell 5.1 (System.Drawing). Sizes up to 24 px drop the translucent back M so the mark stays legible.
param([string]$AssetsDir = (Join-Path $PSScriptRoot "..\src\MCG.Launcher\Assets"))
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"

$mPoints = @(22,78, 22,26, 36,26, 50,50, 64,26, 78,26, 78,78, 66,78, 66,46, 54,66, 46,66, 34,46, 34,78)
$skew = [Math]::Tan(-10 * [Math]::PI / 180)

function New-MPath([double]$dx, [double]$dy, [double]$tx, [double]$ty, [double]$scale) {
    $points = for ($i = 0; $i -lt $mPoints.Count; $i += 2) { New-Object System.Drawing.PointF $mPoints[$i], $mPoints[$i + 1] }
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddPolygon([System.Drawing.PointF[]]$points)
    # Same order as the SVG transform list: translate(dx,dy) skewX(-10) translate(tx,ty) scale(s).
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate($dx, $dy); $m.Shear($skew, 0); $m.Translate($tx, $ty); $m.Scale($scale, $scale)
    $path.Transform($m)
    $path
}

function New-Tile {
    $r = 22; $x = 4; $y = 4; $w = 92; $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $d, $d, 180, 90); $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $w - $d, $d, $d, 0, 90); $path.AddArc($x, $y + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure(); $path
}

function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"; $g.PixelOffsetMode = "HighQuality"; $g.CompositingQuality = "HighQuality"
    $g.ScaleTransform($size / 100, $size / 100)
    $tile = New-Tile
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x14, 0x21, 0x3D))), $tile)
    $g.SetClip($tile)
    $diag = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 4, 96), (New-Object System.Drawing.PointF 96, 4), (New-Object System.Drawing.PointF 96, 96))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x1C, 0x2C, 0x4F))), $diag)
    $g.ResetClip()
    $ink = [System.Drawing.Color]::FromArgb(255, 0xE5, 0xE5, 0xE5)
    if ($size -le 24) {
        $g.FillPath((New-Object System.Drawing.SolidBrush $ink), (New-MPath 0 0 12 4 1))
    } else {
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(102, $ink))), (New-MPath -3 -4 12 6 0.88))
        $g.FillPath((New-Object System.Drawing.SolidBrush $ink), (New-MPath 4 4 12 6 0.88))
    }
    $g.Dispose()
    $bmp
}

New-Item -ItemType Directory -Force $AssetsDir | Out-Null
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = Render $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256) { $bmp.Save((Join-Path $AssetsDir "AppIcon.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    , $ms.ToArray()
}

# ICO with PNG-compressed entries (supported since Windows Vista).
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $AssetsDir "AppIcon.ico"), $out.ToArray())
"AppIcon.ico ($($sizes -join ', ') px) e AppIcon.png gerados em $AssetsDir"
