# Génère Assets/Arboryn.ico à partir du même tracé que Controls/BrandMark.xaml.
# Multi-résolution PNG-in-ICO : 16, 24, 32, 48, 64, 128, 256.
# Réexécutable à la main si le tracé de la marque évolue.

[CmdletBinding()]
param(
    [string]$Output = (Join-Path $PSScriptRoot 'Arboryn.ico')
)

Add-Type -AssemblyName System.Drawing

function New-BrandBitmap([int]$IconSize) {
    [int]$n = $IconSize
    [single]$nf = [single]$n

    $bmp = New-Object System.Drawing.Bitmap -ArgumentList $n, $n, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    # Fond : rectangle arrondi (rayon = 7/28 du côté) avec dégradé linéaire 150°.
    [single]$radius = [single]([Math]::Max(1.0, $nf * 7.0 / 28.0))
    [single]$d = [single]($radius * 2.0)
    [single]$ns = $nf
    $bgPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bgPath.AddArc(0.0, 0.0, $d, $d, 180.0, 90.0)
    $bgPath.AddArc(($ns - $d), 0.0, $d, $d, 270.0, 90.0)
    $bgPath.AddArc(($ns - $d), ($ns - $d), $d, $d, 0.0, 90.0)
    $bgPath.AddArc(0.0, ($ns - $d), $d, $d, 90.0, 90.0)
    $bgPath.CloseFigure()

    [single]$sx = [single]($nf * 0.15)
    [single]$ex = [single]($nf * 0.85)
    $startPoint = New-Object System.Drawing.PointF -ArgumentList $sx, ([single]0.0)
    $endPoint   = New-Object System.Drawing.PointF -ArgumentList $ex, $ns
    $cLight = [System.Drawing.Color]::FromArgb(255, 0x43, 0xA0, 0x62)
    $cDark  = [System.Drawing.Color]::FromArgb(255, 0x18, 0x62, 0x36)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList $startPoint, $endPoint, $cLight, $cDark
    $g.FillPath($brush, $bgPath)
    $brush.Dispose()
    $bgPath.Dispose()

    # Glyphe : icône arbre custom (viewBox 24×24), centrée dans une zone 20×20 du badge.
    [single]$iconArea = [single]($nf * 20.0 / 28.0)
    [single]$offset = [single](($nf - $iconArea) / 2.0)
    [single]$scale = [single]($iconArea / 24.0)

    function script:P([single]$x, [single]$y) {
        return (New-Object System.Drawing.PointF -ArgumentList ([single]($script:Offset + $x * $script:Scale)), ([single]($script:Offset + $y * $script:Scale)))
    }
    $script:Offset = $offset
    $script:Scale = $scale

    $white = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::White)

    # Tronc : M 12 9 → M 12 21 ; stroke white 2px, caps arrondis.
    $pen = New-Object System.Drawing.Pen -ArgumentList ([System.Drawing.Color]::White), ([single](2.0 * $scale))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($pen, (script:P 12 9), (script:P 12 21))
    $pen.Dispose()

    # Feuille droite : M 12 13 C 12 9 15 7 18 7 C 18 11 16 13 12 13 Z
    $leafR = New-Object System.Drawing.Drawing2D.GraphicsPath
    $leafR.AddBezier((script:P 12 13), (script:P 12 9),  (script:P 15 7),  (script:P 18 7))
    $leafR.AddBezier((script:P 18 7),  (script:P 18 11), (script:P 16 13), (script:P 12 13))
    $leafR.CloseFigure()
    $g.FillPath($white, $leafR)
    $leafR.Dispose()

    # Feuille gauche : M 12 16 C 12 13 9.5 11 6.5 11 C 6.5 14 8.5 16 12 16 Z
    $leafL = New-Object System.Drawing.Drawing2D.GraphicsPath
    $leafL.AddBezier((script:P 12 16),  (script:P 12 13),   (script:P 9.5 11), (script:P 6.5 11))
    $leafL.AddBezier((script:P 6.5 11), (script:P 6.5 14),  (script:P 8.5 16), (script:P 12 16))
    $leafL.CloseFigure()
    $g.FillPath($white, $leafL)
    $leafL.Dispose()

    $white.Dispose()
    $g.Dispose()
    return $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$Bitmap) {
    $ms = New-Object System.IO.MemoryStream
    $Bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return ,$bytes
}

# Génère les PNG en mémoire pour chaque taille demandée.
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = New-Object System.Collections.ArrayList
foreach ($s in $sizes) {
    $bmp = New-BrandBitmap -IconSize $s
    $png = Get-PngBytes -Bitmap $bmp
    $bmp.Dispose()
    [void]$images.Add([pscustomobject]@{ Size = $s; Data = $png })
}

# Sérialise au format ICO (PNG-in-ICO supporté par Vista+).
$headerSize = 6
$entrySize  = 16
$dirSize    = $headerSize + $entrySize * $images.Count

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

# ICONDIR
$bw.Write([uint16]0)             # reserved
$bw.Write([uint16]1)             # type = icon
$bw.Write([uint16]$images.Count) # count

$offset = $dirSize
foreach ($img in $images) {
    $w = if ($img.Size -ge 256) { [byte]0 } else { [byte]$img.Size }
    $h = $w
    $bw.Write($w)                 # width
    $bw.Write($h)                 # height
    $bw.Write([byte]0)            # color count (0 si > 256 couleurs)
    $bw.Write([byte]0)            # reserved
    $bw.Write([uint16]1)          # color planes
    $bw.Write([uint16]32)         # bits per pixel
    $bw.Write([uint32]$img.Data.Length)
    $bw.Write([uint32]$offset)
    $offset += $img.Data.Length
}

foreach ($img in $images) {
    $bw.Write($img.Data)
}

$bw.Flush()
[System.IO.File]::WriteAllBytes($Output, $ms.ToArray())
$bw.Dispose()
$ms.Dispose()

$len = (Get-Item $Output).Length
"Arboryn.ico écrit ({0} tailles, {1} octets) → {2}" -f $images.Count, $len, $Output
