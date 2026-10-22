$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/main/resources/assets/vortex_client/textures/ui/icons'))
foreach ($iconName in @('resource_counter','storage_memory')) {
    $canvas = [Drawing.Bitmap]::new(384,384)
    $graphics = [Drawing.Graphics]::FromImage($canvas)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(4,4)
    $pen = [Drawing.Pen]::new([Drawing.Color]::White,5)
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    try {
        if ($iconName -eq 'resource_counter') {
            $graphics.DrawLine($pen,17,76,79,76)
            $graphics.DrawLine($pen,25,66,25,49)
            $graphics.DrawLine($pen,46,66,46,35)
            $graphics.DrawLine($pen,67,66,67,20)
            $graphics.DrawLine($pen,17,23,22,28)
            $graphics.DrawLine($pen,22,28,33,15)
        } else {
            $graphics.DrawRectangle($pen,14,23,48,48)
            $graphics.DrawLine($pen,14,38,58,38)
            $graphics.DrawLine($pen,33,40,42,40)
            $graphics.DrawEllipse($pen,48,46,25,25)
            $graphics.DrawLine($pen,70,69,82,82)
        }
        $small = [Drawing.Bitmap]::new(96,96)
        $resizer = [Drawing.Graphics]::FromImage($small)
        try {
            $resizer.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $resizer.DrawImage($canvas,0,0,96,96)
            $small.Save((Join-Path $iconRoot ($iconName+'.png')),[Drawing.Imaging.ImageFormat]::Png)
        } finally { $resizer.Dispose(); $small.Dispose() }
    } finally { $pen.Dispose(); $graphics.Dispose(); $canvas.Dispose() }
}
