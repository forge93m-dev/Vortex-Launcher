# Reproducible branding asset: existing logo + typography; no generated illustration.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$branding = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Assets/Branding'))
$logo = [Drawing.Image]::FromFile((Join-Path $branding 'vortex-icon-v1.png'))
$bitmap = [Drawing.Bitmap]::new(1024, 576)
$g = [Drawing.Graphics]::FromImage($bitmap)
$owned = [Collections.Generic.List[IDisposable]]::new()
function Keep($item) { $owned.Add($item); return $item }
try {
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $rect = [Drawing.Rectangle]::new(0, 0, 1024, 576)
    $gradient = Keep ([Drawing.Drawing2D.LinearGradientBrush]::new($rect, [Drawing.ColorTranslator]::FromHtml('#100F20'), [Drawing.ColorTranslator]::FromHtml('#25203D'), 24.0))
    $g.FillRectangle($gradient, $rect)
    $line = Keep ([Drawing.Pen]::new([Drawing.Color]::FromArgb(25, 194, 170, 255), 1.0))
    for ($i = -500; $i -lt 1400; $i += 100) { $g.DrawLine($line, $i, 0, ($i + 500), 576) }
    $halo = Keep ([Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(35, 150, 110, 255)))
    $g.FillEllipse($halo, 36, 100, 370, 370)
    $g.DrawImage($logo, [Drawing.Rectangle]::new(55, 124, 330, 330))
    $white = Keep ([Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#F6F4FF')))
    $muted = Keep ([Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#B9B2CE')))
    $accent = Keep ([Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#BBA1FF')))
    $brand = Keep ([Drawing.Font]::new('Segoe UI', 17, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel))
    $heading = Keep ([Drawing.Font]::new('Segoe UI', 65, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel))
    $body = Keep ([Drawing.Font]::new('Segoe UI', 21, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel))
    $caption = Keep ([Drawing.Font]::new('Segoe UI', 14, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel))
    $g.FillRectangle($accent, 440, 135, 44, 4)
    $g.DrawString('VORTEX', $brand, $accent, [Drawing.PointF]::new(440, 159))
    $g.DrawString('PLAY', $heading, $white, [Drawing.PointF]::new(434, 198))
    $g.DrawString('TOGETHER.', $heading, $white, [Drawing.PointF]::new(434, 272))
    $g.DrawString('Your next session starts here.', $body, $muted, [Drawing.PointF]::new(440, 373))
    $g.DrawString('MINECRAFT. FRIENDS. VORTEX.', $caption, $muted, [Drawing.PointF]::new(440, 462))
    $output = Join-Path $branding 'vortex-discord-cover.png'
    $bitmap.Save($output, [Drawing.Imaging.ImageFormat]::Png)
    Write-Output $output
} finally {
    foreach ($item in $owned) { $item.Dispose() }
    $g.Dispose(); $bitmap.Dispose(); $logo.Dispose()
}
