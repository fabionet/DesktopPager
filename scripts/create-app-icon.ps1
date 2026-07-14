# Genera l'icona dell'app (v0.3): schermo bianco con puntini-icone e freccia
# circolare di rotazione, su gradiente viola. Multi-risoluzione (PNG-ICO).
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$assetsDir = Join-Path $PSScriptRoot "..\src\DesktopPager.Tray\Assets"
New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
$iconPath = Join-Path (Resolve-Path $assetsDir) "DesktopPager.ico"

function Draw-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # sfondo arrotondato con gradiente viola
    $r = [single]($s * 0.20)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $r*2, $r*2, 180, 90)
    $path.AddArc($s-$r*2, 0, $r*2, $r*2, 270, 90)
    $path.AddArc($s-$r*2, $s-$r*2, $r*2, $r*2, 0, 90)
    $path.AddArc(0, $s-$r*2, $r*2, $r*2, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
    $c1 = [System.Drawing.Color]::FromArgb(255, 139, 92, 246)
    $c2 = [System.Drawing.Color]::FromArgb(255, 62, 20, 123)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 55)
    $g.FillPath($brush, $path)

    # schermo bianco al centro con puntini "icone" viola
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $sx = [single]($s*0.30); $sy = [single]($s*0.34)
    $sw = [single]($s*0.40); $sh = [single]($s*0.32)
    $g.FillRectangle($white, $sx, $sy, $sw, $sh)
    if ($s -ge 32) {
        $violet = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 109, 66, 210))
        $dot = [single]($s*0.07)
        foreach ($dx in @(0.35, 0.47, 0.59)) {
            foreach ($dy in @(0.39, 0.51)) {
                $g.FillRectangle($violet, [single]($s*$dx), [single]($s*$dy), $dot, $dot)
            }
        }
    }

    # freccia circolare di rotazione color ciano attorno allo schermo
    $cyan = [System.Drawing.Color]::FromArgb(255, 34, 211, 238)
    $pen = New-Object System.Drawing.Pen($cyan, [single]($s*0.085))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cr = [single]($s*0.33)
    $cx = [single]($s*0.5); $cy2 = [single]($s*0.5)
    # arco in senso orario, gap in alto a sinistra
    $g.DrawArc($pen, $cx-$cr, $cy2-$cr, $cr*2, $cr*2, -80, 250)
    # punta della freccia sul lato sinistro, direzione di marcia verso l'alto
    $cyanBrush = New-Object System.Drawing.SolidBrush($cyan)
    $ax = $cx - $cr
    $ah = [single]($s*0.14); $aw = [single]($s*0.11)
    $pts = @(
        (New-Object System.Drawing.PointF($ax, [single]($cy2 - $ah))),
        (New-Object System.Drawing.PointF([single]($ax - $aw), [single]($cy2 + $ah*0.25))),
        (New-Object System.Drawing.PointF([single]($ax + $aw), [single]($cy2 + $ah*0.25)))
    )
    $g.FillPolygon($cyanBrush, $pts)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = @(256, 48, 32, 16)
$pngs = @{}
foreach ($s in $sizes) { $pngs[$s] = Draw-IconPng $s }

$ms = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($ms)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $data = $pngs[$s]
    $w.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $w.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$data.Length)
    $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($s in $sizes) { $w.Write($pngs[$s]) }
$w.Flush()
[System.IO.File]::WriteAllBytes($iconPath, $ms.ToArray())
"Creato: $iconPath ($($ms.Length) byte)"
