param(
    [string]$Source = 'C:\Users\Lumino Game 04\.codex\generated_images\01a022bd-086e-7a71-ada7-7474fc7135eb\exec-c66f66a4-043e-45d6-b986-f03e9e99819e.png',
    [string]$ColorSource = 'C:\Users\Lumino Game 04\.codex\generated_images\01a022bd-086e-7a71-ada7-7474fc7135eb\exec-16cd9e31-4c86-42d7-8fcd-2558d0a3b4dc.png',
    [string]$OutputRoot = 'D:\GitLab\REDIV\output\imagegen\login-ui'
)

Add-Type -AssemblyName System.Drawing

$sourceBitmap = [System.Drawing.Bitmap]::FromFile($Source)
$colorBitmap = [System.Drawing.Bitmap]::FromFile($ColorSource)
$transparentBitmap = New-Object System.Drawing.Bitmap($sourceBitmap.Width, $sourceBitmap.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

for ($y = 0; $y -lt $sourceBitmap.Height; $y++) {
    for ($x = 0; $x -lt $sourceBitmap.Width; $x++) {
        $keyColor = $sourceBitmap.GetPixel($x, $y)
        $renderColor = $colorBitmap.GetPixel($x, $y)
        $dominance = [int]$keyColor.G - [Math]::Max([int]$keyColor.R, [int]$keyColor.B)
        $alpha = 255

        # Preserve the intentional green online-status dot inside the server selector.
        $insideStatusDot = ($x -ge 700 -and $x -le 755 -and $y -ge 455 -and $y -le 520)

        if (-not $insideStatusDot -and $keyColor.G -gt 100 -and $dominance -gt 10) {
            if ($dominance -ge 145) {
                $alpha = 0
            }
            else {
                $alpha = [int](255.0 * (145.0 - $dominance) / 135.0)
            }
        }

        if ($alpha -le 8) {
            $transparentBitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0, 0))
            continue
        }

        if ($alpha -lt 255) {
            $a = $alpha / 255.0
            $checker = if ((([int]($x / 23) + [int]($y / 23)) % 2) -eq 0) { 254.0 } else { 244.0 }
            $red = [Math]::Max(0, [Math]::Min(255, [int](($renderColor.R - (1.0 - $a) * $checker) / $a)))
            $green = [Math]::Max(0, [Math]::Min(255, [int](($renderColor.G - (1.0 - $a) * $checker) / $a)))
            $blue = [Math]::Max(0, [Math]::Min(255, [int](($renderColor.B - (1.0 - $a) * $checker) / $a)))
            $transparentBitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($alpha, $red, $green, $blue))
        }
        else {
            $transparentBitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $renderColor.R, $renderColor.G, $renderColor.B))
        }
    }
}

$sourceBitmap.Dispose()
$colorBitmap.Dispose()

[System.IO.Directory]::CreateDirectory($OutputRoot) | Out-Null
$elementsRoot = Join-Path $OutputRoot 'classic-elements'
[System.IO.Directory]::CreateDirectory($elementsRoot) | Out-Null

$sheetPath = Join-Path $OutputRoot 'rediv-classic-login-ui-elements-sheet.png'
$sheet2kPath = Join-Path $OutputRoot 'rediv-classic-login-ui-elements-sheet-2k.png'
$transparentBitmap.Save($sheetPath, [System.Drawing.Imaging.ImageFormat]::Png)

$sheet2k = New-Object System.Drawing.Bitmap(2048, 1152, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$sheetGraphics = [System.Drawing.Graphics]::FromImage($sheet2k)
$sheetGraphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
$sheetGraphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$sheetGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$sheetGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$sheetGraphics.DrawImage($transparentBitmap, 0, 0, 2048, 1152)
$sheetGraphics.Dispose()
$sheet2k.Save($sheet2kPath, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet2k.Dispose()

$regions = @(
    @{ Name = 'logo';              X = 120;  Y = 30;  W = 780; H = 340 },
    @{ Name = 'language-selector'; X = 1050; Y = 90;  W = 500; H = 200 },
    @{ Name = 'server-selector';   X = 85;   Y = 360; W = 850; H = 245 },
    @{ Name = 'tap-to-start';      X = 950;  Y = 300; W = 710; H = 325 },
    @{ Name = 'account-login';     X = 90;   Y = 640; W = 440; H = 230 },
    @{ Name = 'announcement';      X = 590;  Y = 590; W = 270; H = 280 },
    @{ Name = 'settings';          X = 845;  Y = 590; W = 270; H = 280 },
    @{ Name = 'version-text';      X = 1180; Y = 710; W = 330; H = 150 }
)

foreach ($region in $regions) {
    $cropRect = New-Object System.Drawing.Rectangle($region.X, $region.Y, $region.W, $region.H)
    $crop = $transparentBitmap.Clone($cropRect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    $minX = $crop.Width
    $minY = $crop.Height
    $maxX = -1
    $maxY = -1

    for ($cy = 0; $cy -lt $crop.Height; $cy++) {
        for ($cx = 0; $cx -lt $crop.Width; $cx++) {
            if ($crop.GetPixel($cx, $cy).A -gt 8) {
                if ($cx -lt $minX) { $minX = $cx }
                if ($cy -lt $minY) { $minY = $cy }
                if ($cx -gt $maxX) { $maxX = $cx }
                if ($cy -gt $maxY) { $maxY = $cy }
            }
        }
    }

    if ($maxX -ge $minX -and $maxY -ge $minY) {
        $padding = 24
        $trimX = [Math]::Max(0, $minX - $padding)
        $trimY = [Math]::Max(0, $minY - $padding)
        $trimRight = [Math]::Min($crop.Width - 1, $maxX + $padding)
        $trimBottom = [Math]::Min($crop.Height - 1, $maxY + $padding)
        $trimWidth = [int]($trimRight - $trimX + 1)
        $trimHeight = [int]($trimBottom - $trimY + 1)
        $trimRect = [System.Drawing.Rectangle]::new([int]$trimX, [int]$trimY, $trimWidth, $trimHeight)
        $trimmed = $crop.Clone($trimRect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $elementPath = Join-Path $elementsRoot ($region.Name + '.png')
        $trimmed.Save($elementPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $trimmed.Dispose()
    }

    $crop.Dispose()
}

$transparentBitmap.Dispose()

Write-Output $sheet2kPath
Get-ChildItem -LiteralPath $elementsRoot -Filter '*.png' | Sort-Object Name | Select-Object Name, Length
