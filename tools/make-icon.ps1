Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = New-Object System.Collections.Generic.List[byte[]]

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, [int]($s * 0.05))
    $w = $s - (2 * $pad)
    $rad = [Math]::Max(2, [int]($s * 0.22))
    $x = $pad
    $y = $pad

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $rad, $rad, 180, 90)
    $path.AddArc($x + $w - $rad, $y, $rad, $rad, 270, 90)
    $path.AddArc($x + $w - $rad, $y + $w - $rad, $rad, $rad, 0, 90)
    $path.AddArc($x, $y + $w - $rad, $rad, $rad, 90, 90)
    $path.CloseFigure()

    $bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, 26, 28, 32))
    $g.FillPath($bg, $path)

    $d = [int]($s * 0.50)
    $dx = [int](($s - $d) / 2)
    $red = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 232, 54, 51))
    $g.FillEllipse($red, $dx, $dx, $d, $d)

    $ringPad = [int]($s * 0.135)
    $ringD = $s - (2 * $ringPad)
    $ringPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 255, 255, 255), [Math]::Max(1, [int]($s * 0.055)))
    $g.DrawEllipse($ringPen, $ringPad, $ringPad, $ringD, $ringD)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs.Add($ms.ToArray())

    $ringPen.Dispose(); $red.Dispose(); $bg.Dispose(); $path.Dispose(); $g.Dispose(); $bmp.Dispose(); $ms.Dispose()
}

$outPath = Join-Path $PSScriptRoot '..\Assets\app.ico'
$fs = [System.IO.File]::Create($outPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $data = $pngs[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $bw.Write($data) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()
Write-Host "Icon written to $outPath"
