param(
    [string]$Source = (Join-Path $PSScriptRoot '..\ArtSource\SpiritWolf\SpiritWolf_SourceSheet.png'),
    [string]$Output = (Join-Path $PSScriptRoot '..\Mod\Textures\Things\Pawn\Animal\SpiritWolf')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $Output | Out-Null

function Export-View {
    param([string]$Name, [System.Drawing.Rectangle]$Crop, [bool]$Mirror = $false)
    $sourceImage = [System.Drawing.Bitmap]::FromFile($Source)
    try {
        $piece = $sourceImage.Clone($Crop, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            if ($Mirror) { $piece.RotateFlip([System.Drawing.RotateFlipType]::RotateNoneFlipX) }
            $canvas = [System.Drawing.Bitmap]::new(512, 512, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $graphics = [System.Drawing.Graphics]::FromImage($canvas)
                try {
                    $graphics.Clear([System.Drawing.Color]::Transparent)
                    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                    $targetHeight = 454
                    $targetWidth = [int]($piece.Width * $targetHeight / $piece.Height)
                    if ($targetWidth -gt 500) { $targetWidth = 500; $targetHeight = [int]($piece.Height * 500 / $piece.Width) }
                    $x = [int]((512 - $targetWidth) / 2)
                    $y = 512 - $targetHeight - 18
                    $graphics.DrawImage($piece, [System.Drawing.Rectangle]::new($x, $y, $targetWidth, $targetHeight))
                    # The source is a multi-view sheet. Clear the outer gutter so
                    # antialiased pixels from neighbouring views cannot leak into a sprite.
                    $graphics.FillRectangle([System.Drawing.Brushes]::Transparent, 0, 0, 22, 512)
                    $graphics.FillRectangle([System.Drawing.Brushes]::Transparent, 490, 0, 22, 512)
                } finally { $graphics.Dispose() }
                $canvas.Save((Join-Path $Output ("SpiritWolf_{0}.png" -f $Name)), [System.Drawing.Imaging.ImageFormat]::Png)
            } finally { $canvas.Dispose() }
        } finally { $piece.Dispose() }
    } finally { $sourceImage.Dispose() }
}

# Crop coordinates are tied to the checked-in generated source sheet. Keep that
# sheet in ArtSource so final directional textures remain reproducible.
Export-View -Name 'south' -Crop ([System.Drawing.Rectangle]::new(0, 300, 270, 660))
Export-View -Name 'east'  -Crop ([System.Drawing.Rectangle]::new(280, 300, 710, 660))
Export-View -Name 'west'  -Crop ([System.Drawing.Rectangle]::new(280, 300, 710, 660)) -Mirror $true
Export-View -Name 'north' -Crop ([System.Drawing.Rectangle]::new(1005, 300, 249, 660))

Write-Output "Built four directional spirit-wolf textures in $Output"
