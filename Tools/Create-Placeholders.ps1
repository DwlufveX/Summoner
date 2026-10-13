# Original geometric placeholder icons. No third-party images are read or copied.
param([string]$Output = (Join-Path $PSScriptRoot '..\Mod\Textures\UI\Summoner'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $Output | Out-Null
foreach ($name in @('Contract','Call','Link','Hunt','Evolve','Crystal')) {
    $bitmap = [System.Drawing.Bitmap]::new(128,128)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::FromArgb(255,18,36,48))
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255,116,224,230),6)
    $brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255,116,224,230))
    $g.DrawEllipse($pen,8,8,112,112)
    switch ($name) {
        'Contract' { $g.DrawEllipse($pen,28,34,44,54); $g.DrawEllipse($pen,56,34,44,54) }
        'Call' { $g.DrawArc($pen,24,24,80,80,40,280); $g.DrawLine($pen,96,80,78,82); $g.DrawLine($pen,96,80,95,99) }
        'Link' { $g.DrawEllipse($pen,22,46,36,36); $g.DrawEllipse($pen,70,46,36,36); $g.DrawLine($pen,52,64,76,64) }
        'Hunt' { $g.DrawLine($pen,30,92,90,32); $g.DrawLine($pen,90,32,65,32); $g.DrawLine($pen,90,32,90,57) }
        'Evolve' { $g.DrawLine($pen,32,80,64,40); $g.DrawLine($pen,64,40,96,80); $g.DrawLine($pen,32,99,64,59); $g.DrawLine($pen,64,59,96,99) }
        'Crystal' { $points = [System.Drawing.Point[]]@([System.Drawing.Point]::new(64,23),[System.Drawing.Point]::new(91,64),[System.Drawing.Point]::new(64,105),[System.Drawing.Point]::new(37,64)); $g.DrawPolygon($pen,$points); $g.DrawLine($pen,64,23,64,105) }
    }
    $bitmap.Save((Join-Path $Output ($name + '.png')),[System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $pen.Dispose(); $brush.Dispose(); $bitmap.Dispose()
}
