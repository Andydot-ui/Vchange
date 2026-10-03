Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$size, [System.Drawing.Color]$fill) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 256.0
    function T($v) { return [float]($v * $s) }

    # one path, Alternate fill: outer shapes filled, inner shapes become holes
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.FillMode = [System.Drawing.Drawing2D.FillMode]::Alternate

    # clapper body (rounded rect)
    $r = T 18
    $x1 = T 16; $y1 = T 106; $x2 = T 240; $y2 = T 224
    $body = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $body.AddArc($x1, $y1, $r * 2, $r * 2, 180, 90)
    $body.AddArc($x2 - $r * 2, $y1, $r * 2, $r * 2, 270, 90)
    $body.AddArc($x2 - $r * 2, $y2 - $r * 2, $r * 2, $r * 2, 0, 90)
    $body.AddArc($x1, $y2 - $r * 2, $r * 2, $r * 2, 90, 90)
    $body.CloseFigure()
    $path.AddPath($body, $false)

    # top clapper bar (slanted)
    $bar = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $bar.AddPolygon(@(
        [System.Drawing.PointF]::new((T 16),  (T 50)),
        [System.Drawing.PointF]::new((T 240), (T 42)),
        [System.Drawing.PointF]::new((T 240), (T 88)),
        [System.Drawing.PointF]::new((T 16),  (T 96))
    ))
    $path.AddPath($bar, $false)

    # diagonal stripe holes on the bar
    foreach ($x0 in @(44, 96, 148, 200)) {
        $st = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $st.AddPolygon(@(
            [System.Drawing.PointF]::new((T $x0),        (T 40)),
            [System.Drawing.PointF]::new((T ($x0 + 14)), (T 40)),
            [System.Drawing.PointF]::new((T ($x0 + 28)), (T 100)),
            [System.Drawing.PointF]::new((T ($x0 + 14)), (T 100))
        ))
        $path.AddPath($st, $false)
        $st.Dispose()
    }

    # play triangle hole in the body
    $tri = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $tri.AddPolygon(@(
        [System.Drawing.PointF]::new((T 104), (T 136)),
        [System.Drawing.PointF]::new((T 172), (T 165)),
        [System.Drawing.PointF]::new((T 104), (T 194))
    ))
    $path.AddPath($tri, $false)

    $brush = [System.Drawing.SolidBrush]::new($fill)
    $g.FillPath($brush, $path)

    $brush.Dispose(); $tri.Dispose(); $bar.Dispose(); $body.Dispose(); $path.Dispose(); $g.Dispose()

    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return ,$bytes
}

function Save-Ico([byte[][]]$Pngs, [int[]]$Sizes, [string]$Path) {
    $ms = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($ms)
    $bw.Write([UInt16]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]$Pngs.Count)
    $offset = 6 + 16 * $Pngs.Count
    for ($i = 0; $i -lt $Pngs.Count; $i++) {
        $s = $Sizes[$i]
        $dim = 0
        if ($s -lt 256) { $dim = $s }
        $bw.Write([Byte]$dim)
        $bw.Write([Byte]$dim)
        $bw.Write([Byte]0)
        $bw.Write([Byte]0)
        $bw.Write([UInt16]1)
        $bw.Write([UInt16]32)
        $bw.Write([UInt32]$Pngs[$i].Length)
        $bw.Write([UInt32]$offset)
        $offset += $Pngs[$i].Length
    }
    foreach ($png in $Pngs) { $bw.Write($png) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($Path, $ms.ToArray())
    $bw.Dispose(); $ms.Dispose()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$outDir = Join-Path $PSScriptRoot "..\Resources"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# white icon (for dark system theme)
$white = [System.Drawing.Color]::White
$pngsW = @()
foreach ($s in $sizes) { $pngsW += ,(New-IconPng $s $white) }
Save-Ico -Pngs $pngsW -Sizes $sizes -Path (Join-Path $outDir "app_white.ico")

# dark icon (for light system theme)
$dark = [System.Drawing.Color]::FromArgb(255, 32, 32, 32)
$pngsD = @()
foreach ($s in $sizes) { $pngsD += ,(New-IconPng $s $dark) }
Save-Ico -Pngs $pngsD -Sizes $sizes -Path (Join-Path $outDir "app_dark.ico")

Get-ChildItem $outDir -Filter "app_*.ico" | ForEach-Object {
    Write-Host "$($_.Name): $([math]::Round($_.Length/1KB,1)) KB"
}
