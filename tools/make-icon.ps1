# Genera src/App.ico y src/Rama/Resources/Images/rama-mark.png con System.Drawing (sin descargas).
param([string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src'))
Add-Type -AssemblyName System.Drawing
$amber = [System.Drawing.ColorTranslator]::FromHtml('#e6a23c')
$ground = [System.Drawing.ColorTranslator]::FromHtml('#17181a')

function New-Mark([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
    $k = $size / 22.0
    # fondo: cuadrado redondeado r=5
    $r = 5 * $k * 2
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gp.AddArc(0, 0, $r, $r, 180, 90); $gp.AddArc($size - $r, 0, $r, $r, 270, 90)
    $gp.AddArc($size - $r, $size - $r, $r, $r, 0, 90); $gp.AddArc(0, $size - $r, $r, $r, 90, 90); $gp.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $ground), $gp)
    $s = 0.7 * $k; $o = 3.3 * $k
    function P($x, $y) { New-Object System.Drawing.PointF ($o + $x * $s), ($o + $y * $s) }
    $pen = New-Object System.Drawing.Pen $amber, (2.2 * $s)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $pen2 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(140, $amber)), (2.2 * $s)
    $pen2.StartCap = 'Round'; $pen2.EndCap = 'Round'
    # tronco: M7 20 V9.5 c0-2.5 1.6-4 4-4 h1.5
    $g.DrawLine($pen, (P 7 20), (P 7 9.5))
    $g.DrawBezier($pen, (P 7 9.5), (P 7 7) , (P 8.6 5.5), (P 11 5.5))
    $g.DrawLine($pen, (P 11 5.5), (P 12.5 5.5))
    # rama tenue: M7 13 c0-2 1.4-3.2 3.4-3.2 H15
    $g.DrawBezier($pen2, (P 7 13), (P 7 11), (P 8.4 9.8), (P 10.4 9.8))
    $g.DrawLine($pen2, (P 10.4 9.8), (P 15 9.8))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $amber), ($o + (16 - 2.6) * $s), ($o + (5.5 - 2.6) * $s), 5.2 * $s, 5.2 * $s)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(140, $amber))), ($o + (17.6 - 1.9) * $s), ($o + (9.8 - 1.9) * $s), 3.8 * $s, 3.8 * $s)
    $g.Dispose(); return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($z in $sizes) {
    $b = New-Mark $z; $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); ,@($z, $ms.ToArray())
}
$ico = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($p in $pngs) {
    $z = $p[0]; $d = $p[1]
    $w.Write([byte]($(if ($z -ge 256) { 0 } else { $z }))); $w.Write([byte]($(if ($z -ge 256) { 0 } else { $z })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$d.Length); $w.Write([uint32]$offset); $offset += $d.Length
}
foreach ($p in $pngs) { $w.Write([byte[]]$p[1]) }
[System.IO.File]::WriteAllBytes((Join-Path $Out 'App.ico'), $ico.ToArray())
(New-Mark 256).Save((Join-Path $Out 'Rama/Resources/Images/rama-mark.png'), [System.Drawing.Imaging.ImageFormat]::Png)
