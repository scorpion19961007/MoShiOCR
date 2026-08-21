param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\Assets\AppIcon.ico")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng {
    param([int]$Size)

    $scale = $Size / 256.0
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $background = New-RoundedRectanglePath (4 * $scale) (4 * $scale) (248 * $scale) (248 * $scale) (48 * $scale)
    $backgroundBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 27, 77, 67))
    $graphics.FillPath($backgroundBrush, $background)

    $page = New-RoundedRectanglePath (76 * $scale) (47 * $scale) (104 * $scale) (162 * $scale) (17 * $scale)
    $pageBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 247, 249, 246))
    $graphics.FillPath($pageBrush, $page)

    $accentPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 234, 122, 74), [Math]::Max(1.25, 13 * $scale))
    $accentPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $accentPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $segments = @(
        @(48, 84, 48, 58), @(48, 58, 72, 58),
        @(208, 84, 208, 58), @(208, 58, 184, 58),
        @(48, 172, 48, 198), @(48, 198, 72, 198),
        @(208, 172, 208, 198), @(208, 198, 184, 198)
    )
    foreach ($segment in $segments) {
        $graphics.DrawLine($accentPen, $segment[0] * $scale, $segment[1] * $scale, $segment[2] * $scale, $segment[3] * $scale)
    }

    $inkPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 27, 77, 67), [Math]::Max(1.0, 10 * $scale))
    $inkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $inkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($inkPen, 99 * $scale, 100 * $scale, 157 * $scale, 100 * $scale)
    $graphics.DrawLine($inkPen, 99 * $scale, 128 * $scale, 145 * $scale, 128 * $scale)
    $graphics.DrawLine($inkPen, 99 * $scale, 156 * $scale, 157 * $scale, 156 * $scale)

    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()

    $stream.Dispose()
    $inkPen.Dispose()
    $accentPen.Dispose()
    $pageBrush.Dispose()
    $page.Dispose()
    $backgroundBrush.Dispose()
    $background.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
    return $bytes
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $images.Add((New-IconPng $size))
}
$targetDirectory = Split-Path -Parent $OutputPath
[System.IO.Directory]::CreateDirectory($targetDirectory) | Out-Null

$file = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)

    $offset = 6 + (16 * $images.Count)
    for ($index = 0; $index -lt $images.Count; $index++) {
        $size = $sizes[$index]
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }

    foreach ($image in $images) {
        $writer.Write([byte[]]$image)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Output "Generated $OutputPath"
