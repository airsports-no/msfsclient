<#
.SYNOPSIS
    Makes AppIcon.ico, the client's program icon, from the Air Sports logo (Square44x44Logo.altform-unplated_targetsize-256.png).

.DESCRIPTION
    The logo's text is unreadable at icon sizes, so the icon is the emblem only: the globe with the plane and its trail at
    48-256 px, and the globe alone (masked to its circle) at 16-32 px, where the trail would only be noise. Sizes below 256
    are classic 32-bit bitmaps and 256 is PNG, the layout every Windows version and tool reads. Run it again after changing
    the logo; the crop boxes below are for the current 256 px logo.
#>
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$src = Join-Path $PSScriptRoot "Square44x44Logo.altform-unplated_targetsize-256.png"
$target = Join-Path $PSScriptRoot "AppIcon.ico"

function Square($image, [int]$x0, [int]$y0, [int]$w, [int]$h, [int]$pad, [bool]$circle) {
    $side = [Math]::Max($w, $h) + $pad
    $b = New-Object System.Drawing.Bitmap $side, $side
    $cx = $x0 + $w / 2.0; $cy = $y0 + $h / 2.0; $r = [Math]::Max($w, $h) / 2.0 + 0.5
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            if ($circle) { $dx = $x0 + $x + 0.5 - $cx; $dy = $y0 + $y + 0.5 - $cy; if ($dx * $dx + $dy * $dy -gt $r * $r) { continue } }
            $b.SetPixel([int](($side - $w) / 2) + $x, [int](($side - $h) / 2) + $y, $image.GetPixel($x0 + $x, $y0 + $y))
        }
    }
    return $b
}

function Scaled($b, [int]$n) {
    $s = New-Object System.Drawing.Bitmap $n, $n, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($s)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($b, 0, 0, $n, $n)
    $g.Dispose()
    return $s
}

# One image of the icon: PNG at 256, otherwise a 32-bit DIB (BGRA, bottom-up) with an all-clear AND mask.
function Entry($bmp) {
    $n = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    if ($n -ge 256) { $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); return , $ms.ToArray() }
    $bw = New-Object System.IO.BinaryWriter $ms
    $maskStride = [int]([Math]::Ceiling($n / 32.0) * 4)
    $bw.Write([UInt32]40); $bw.Write([Int32]$n); $bw.Write([Int32]($n * 2)); $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]0); $bw.Write([UInt32]($n * $n * 4 + $maskStride * $n)); $bw.Write([Int32]0); $bw.Write([Int32]0)
    $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    for ($y = $n - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $n; $x++) { $c = $bmp.GetPixel($x, $y); $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A) }
    }
    $bw.Write((New-Object byte[] ($maskStride * $n)))
    $bw.Flush()
    return , $ms.ToArray()
}

$logo = [System.Drawing.Bitmap]::FromFile($src)
$emblem = Square $logo 32 19 172 150 6 $false # globe, plane and trail
$globe = Square $logo 62 27 142 142 2 $true   # globe only
$logo.Dispose()

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$entries = foreach ($n in $sizes) { $b = Scaled ($(if ($n -le 32) { $globe } else { $emblem })) $n; , (Entry $b); $b.Dispose() }
$emblem.Dispose(); $globe.Dispose()

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $d = [byte]($(if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }))
    $bw.Write($d); $bw.Write($d); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$entries[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $entries[$i].Length
}
foreach ($e in $entries) { $bw.Write($e) }
$bw.Flush()
[IO.File]::WriteAllBytes($target, $ms.ToArray())
Write-Host "Wrote $target ($($sizes -join ', ') px)"
