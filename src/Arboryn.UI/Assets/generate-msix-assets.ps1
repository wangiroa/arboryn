# Génère les assets visuels MSIX (Assets/*.png) à partir du même tracé de marque que
# Controls/BrandMark.xaml et generate-app-icon.ps1. Réexécutable à la main si la marque évolue.
# Nécessite System.Drawing (Windows PowerShell 5.1 ou pwsh 7 sous Windows).

[CmdletBinding()]
param(
    [string]$OutputDir = $PSScriptRoot
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

    [single]$iconArea = [single]($nf * 20.0 / 28.0)
    [single]$offset = [single](($nf - $iconArea) / 2.0)
    [single]$scale = [single]($iconArea / 24.0)

    $script:Offset = $offset
    $script:Scale = $scale
    function script:P([single]$x, [single]$y) {
        return (New-Object System.Drawing.PointF -ArgumentList ([single]($script:Offset + $x * $script:Scale)), ([single]($script:Offset + $y * $script:Scale)))
    }

    $white = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::White)

    $pen = New-Object System.Drawing.Pen -ArgumentList ([System.Drawing.Color]::White), ([single](2.0 * $scale))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($pen, (script:P 12 9), (script:P 12 21))
    $pen.Dispose()

    $leafR = New-Object System.Drawing.Drawing2D.GraphicsPath
    $leafR.AddBezier((script:P 12 13), (script:P 12 9),  (script:P 15 7),  (script:P 18 7))
    $leafR.AddBezier((script:P 18 7),  (script:P 18 11), (script:P 16 13), (script:P 12 13))
    $leafR.CloseFigure()
    $g.FillPath($white, $leafR)
    $leafR.Dispose()

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

# Enregistre le badge carré, dimension $Size, dans un fichier PNG.
function Save-Square([int]$Size, [string]$FileName) {
    $bmp = New-BrandBitmap -IconSize $Size
    $path = Join-Path $OutputDir $FileName
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "  {0} ({1}x{1})" -f $FileName, $Size
}

# Compose le badge (dimension = plus petit côté) centré sur un canevas transparent WxH.
function Save-Canvas([int]$Width, [int]$Height, [string]$FileName) {
    $badgeSize = [Math]::Min($Width, $Height)
    $badge = New-BrandBitmap -IconSize $badgeSize
    $canvas = New-Object System.Drawing.Bitmap -ArgumentList $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.Clear([System.Drawing.Color]::Transparent)
    $x = [int](($Width - $badgeSize) / 2)
    $y = [int](($Height - $badgeSize) / 2)
    $g.DrawImage($badge, $x, $y)
    $g.Dispose()
    $path = Join-Path $OutputDir $FileName
    $canvas.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose()
    $badge.Dispose()
    "  {0} ({1}x{2})" -f $FileName, $Width, $Height
}

"Génération des assets MSIX → $OutputDir"
Save-Square 44  'Square44x44Logo.png'
Save-Square 71  'Square71x71Logo.png'
Save-Square 150 'Square150x150Logo.png'
Save-Square 310 'Square310x310Logo.png'
Save-Square 50  'StoreLogo.png'
Save-Canvas 310 150 'Wide310x150Logo.png'
Save-Canvas 620 300 'SplashScreen.png'
"Terminé."
