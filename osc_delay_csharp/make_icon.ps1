Add-Type -AssemblyName System.Drawing
$blue = [System.Drawing.Color]::FromArgb(128,191,255)
$dark = [System.Drawing.Color]::FromArgb(37,37,37)
$white = [System.Drawing.Color]::FromArgb(240,240,240)

function Draw-Icon([int]$s, [bool]$play) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $dark), 0, 0, $s, $s)
    # rounded blue border
    $inset = [Math]::Max(1, $s / 16.0)
    $d = $inset * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($inset, $inset, $d, $d, 180, 90)
    $path.AddArc($s - $inset - $d, $inset, $d, $d, 270, 90)
    $path.AddArc($s - $inset - $d, $s - $inset - $d, $d, $d, 0, 90)
    $path.AddArc($inset, $s - $inset - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $border = New-Object System.Drawing.Pen($blue, [float]$inset)
    $g.DrawPath($border, $path)
    # clock
    $cx = $s / 2.0
    $cy = $s / 2.0
    $r = 0.28 * $s
    $pen = New-Object System.Drawing.Pen($blue, [float][Math]::Max(1, 0.08 * $s))
    $g.DrawEllipse($pen, ($cx - $r), ($cy - $r), (2 * $r), (2 * $r))
    $g.DrawLine($pen, $cx, $cy, $cx, ($cy - 0.22 * $s))
    $g.DrawLine($pen, $cx, $cy, ($cx + 0.16 * $s), ($cy + 0.14 * $s))
    $dot = [Math]::Max(1, 0.1 * $s)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $blue), ($cx - $dot / 2), ($cy - $dot / 2), $dot, $dot)
    # play triangle (omitted at 16 px)
    if ($play -and $s -ge 32) {
        $p1 = New-Object System.Drawing.PointF((0.70 * $s), (0.10 * $s))
        $p2 = New-Object System.Drawing.PointF((0.70 * $s), (0.30 * $s))
        $p3 = New-Object System.Drawing.PointF((0.92 * $s), (0.20 * $s))
        $g.FillPolygon((New-Object System.Drawing.SolidBrush $white), @($p1, $p2, $p3))
    }
    $g.Dispose()
    return $bmp
}

function PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b = $ms.ToArray()
    $ms.Dispose()
    $bmp.Dispose()
    return $b
}

# Sizes: play triangle at 256/48/32, clock only at 16.
$sizes = @(256, 48, 32, 16)
$pngs = @()
foreach ($s in $sizes) { $pngs += ,(PngBytes (Draw-Icon $s ($s -ge 32))) }

$count = $sizes.Count
$offset = 6 + 16 * $count
$bytes = New-Object System.Collections.Generic.List[byte]
function Add16([int]$v) { $bytes.Add([byte]($v -band 0xFF)); $bytes.Add([byte]((($v -shr 8) -band 0xFF))) }
function Add32([int]$v) {
    $bytes.Add([byte]($v -band 0xFF)); $bytes.Add([byte]((($v -shr 8) -band 0xFF)))
    $bytes.Add([byte]((($v -shr 16) -band 0xFF))); $bytes.Add([byte]((($v -shr 24) -band 0xFF)))
}
Add16 0          # reserved
Add16 1          # type = icon
Add16 $count
for ($i = 0; $i -lt $count; $i++) {
    $s = $sizes[$i]
    $len = $pngs[$i].Length
    $wbyte = if ($s -ge 256) { 0 } else { $s }
    $hbyte = if ($s -ge 256) { 0 } else { $s }
    $bytes.Add([byte]$wbyte); $bytes.Add([byte]$hbyte)
    $bytes.Add(0); $bytes.Add(0)
    Add16 1       # planes
    Add16 32      # bpp
    Add32 $len
    Add32 $offset
    $offset += $len
}
for ($i = 0; $i -lt $count; $i++) { foreach ($b in $pngs[$i]) { $bytes.Add($b) } }
[System.IO.File]::WriteAllBytes('X:\Bild\OSC_Delay_Windows\osc_delay_csharp\app.ico', $bytes.ToArray())

# Verify: parse header, print sizes.
$raw = [System.IO.File]::ReadAllBytes('X:\Bild\OSC_Delay_Windows\osc_delay_csharp\app.ico')
$cnt = $raw[4] -band 0xFF
$sizesFound = @()
for ($i = 0; $i -lt $cnt; $i++) {
    $pos = 6 + 16 * $i
    $w = $raw[$pos]; $h = $raw[$pos + 1]
    if ($w -eq 0) { $w = 256 }
    if ($h -eq 0) { $h = 256 }
    $sizesFound += "$w x $h"
}
Write-Output ("count: $cnt, sizes: " + ($sizesFound -join ', '))
$icon = New-Object System.Drawing.Icon('X:\Bild\OSC_Delay_Windows\osc_delay_csharp\app.ico')
Write-Output ("Icon loads: " + ($icon -ne $null))
Write-Output ("File size KB: " + [Math]::Round($raw.Length / 1024, 1))
