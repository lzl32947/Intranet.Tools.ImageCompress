$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDir = Join-Path $PSScriptRoot 'Assets'
[IO.Directory]::CreateDirectory($assetDir) | Out-Null
function New-LogoBytes([int]$size) {
    $bmp = [Drawing.Bitmap]::new($size, $size)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform($size / 128.0, $size / 128.0)
    $shape = [Drawing.Drawing2D.GraphicsPath]::new()
    $shape.AddArc(3,3,40,40,180,90)
    $shape.AddArc(85,3,40,40,270,90)
    $shape.AddArc(85,85,40,40,0,90)
    $shape.AddArc(3,85,40,40,90,90)
    $shape.CloseFigure()
    $bg = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#135650'))
    $ink = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#E8FFEE'),8)
    $ink.StartCap = $ink.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $ink.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $g.FillPath($bg,$shape)
    $g.DrawLine($ink,29,29,50,50)
    $g.DrawLine($ink,50,50,50,35)
    $g.DrawLine($ink,50,50,35,50)
    $g.DrawLine($ink,99,99,78,78)
    $g.DrawLine($ink,78,78,93,78)
    $g.DrawLine($ink,78,78,78,93)
    $g.DrawLine($ink,32,91,92,31)
    $stream = [IO.MemoryStream]::new()
    $bmp.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    $bg.Dispose()
    $ink.Dispose()
    $shape.Dispose()
    $g.Dispose()
    $bmp.Dispose()
    return ,$bytes
}
foreach ($entry in @{ 'Square44.png'=44; 'Square150.png'=150; 'StoreLogo.png'=50 }.GetEnumerator()) {
    [IO.File]::WriteAllBytes((Join-Path $assetDir $entry.Key),(New-LogoBytes $entry.Value))
}
$sizes = @(16,24,32,48,64,128,256)
$data = @{}
foreach ($size in $sizes) { $data[$size] = New-LogoBytes $size }
$icon = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $assetDir 'ImagePress.ico')))
try {
    $icon.Write([uint16]0)
    $icon.Write([uint16]1)
    $icon.Write([uint16]$sizes.Length)
    $offset = 6 + 16 * $sizes.Length
    foreach ($size in $sizes) {
        $icon.Write([byte]($size % 256))
        $icon.Write([byte]($size % 256))
        $icon.Write([uint16]0)
        $icon.Write([uint16]1)
        $icon.Write([uint16]32)
        $icon.Write([uint32]$data[$size].Length)
        $icon.Write([uint32]$offset)
        $offset += $data[$size].Length
    }
    foreach ($size in $sizes) { $icon.Write([byte[]]$data[$size]) }
} finally { $icon.Dispose() }
