# Packaging only: resize the generated master without changing its artwork/alpha.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$master = Join-Path $PSScriptRoot 'vortex-icon-v1.png'
$ico = Join-Path $PSScriptRoot 'vortex-icon-v1.ico'
$icns = Join-Path $PSScriptRoot 'vortex-icon-v1.icns'
if ((Test-Path -LiteralPath $ico) -or (Test-Path -LiteralPath $icns)) { throw 'Versioned icon outputs already exist; never overwrite an approved asset.' }
$source = [Drawing.Bitmap]::new($master)
function Png-AtSize([int]$size) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $stream = [IO.MemoryStream]::new()
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($source, [Drawing.Rectangle]::new(0,0,$size,$size))
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    } finally { $graphics.Dispose(); $bitmap.Dispose(); $stream.Dispose() }
}
try {
    $sizes = @(16,24,32,48,64,128,256)
    $images = @($sizes | ForEach-Object { ,(Png-AtSize $_) })
    $file = [IO.File]::Open($ico, [IO.FileMode]::CreateNew)
    $writer = [IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i=0; $i -lt $sizes.Count; $i++) {
            $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
            $offset += $images[$i].Length
        }
        foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
    } finally { $writer.Dispose(); $file.Dispose() }
    $large = Png-AtSize 1024; $medium = Png-AtSize 512
    $file = [IO.File]::Open($icns, [IO.FileMode]::CreateNew)
    $writer = [IO.BinaryWriter]::new($file)
    function Write-BigEndian([uint32]$value) { $bytes = [BitConverter]::GetBytes($value); [Array]::Reverse($bytes); $writer.Write($bytes) }
    try {
        $writer.Write([Text.Encoding]::ASCII.GetBytes('icns')); Write-BigEndian (24 + $large.Length + $medium.Length)
        $writer.Write([Text.Encoding]::ASCII.GetBytes('ic10')); Write-BigEndian (8 + $large.Length); $writer.Write([byte[]]$large)
        $writer.Write([Text.Encoding]::ASCII.GetBytes('ic09')); Write-BigEndian (8 + $medium.Length); $writer.Write([byte[]]$medium)
    } finally { $writer.Dispose(); $file.Dispose() }
    Write-Output "Icon formats created. Master: $($source.Width)x$($source.Height), corner alpha: $($source.GetPixel(0,0).A)"
} finally { $source.Dispose() }
