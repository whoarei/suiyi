# Run with Windows PowerShell in STA mode:
# powershell.exe -NoProfile -STA -File scripts/generate-icons.ps1
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run this script with -STA.' }
Add-Type -AssemblyName PresentationCore, WindowsBase, System.Drawing
$projectRoot = Split-Path -Parent $PSScriptRoot
$assetPath = Join-Path $projectRoot 'src\TranslationFloat.App\Assets'
$previewPath = Join-Path $projectRoot 'docs\assets'
New-Item -ItemType Directory -Path $previewPath -Force | Out-Null
[xml]$appSvg = Get-Content -LiteralPath (Join-Path $assetPath 'Suiyi.svg') -Raw
[xml]$traySvg = Get-Content -LiteralPath (Join-Path $assetPath 'Suiyi.Tray.svg') -Raw

function New-Brush([string]$color) {
    return [Windows.Media.BrushConverter]::new().ConvertFromInvariantString($color)
}

# The source icons deliberately use only filled paths, rounded rectangles and one gradient.
function Render-Icon([xml]$svg, [int]$size) {
    $viewBox = $svg.DocumentElement.GetAttribute('viewBox').Split(' ')
    $scale = $size / [double]$viewBox[2]
    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform([Windows.Media.ScaleTransform]::new($scale, $scale))
    foreach ($node in $svg.DocumentElement.ChildNodes) {
        if ($node.LocalName -in @('title', 'defs')) { continue }
        $fill = $node.GetAttribute('fill')
        if ($fill -eq 'url(#accent)') {
            $brush = [Windows.Media.LinearGradientBrush]::new()
            $brush.StartPoint = [Windows.Point]::new(0, 0)
            $brush.EndPoint = [Windows.Point]::new(1, 1)
            foreach ($stop in $svg.GetElementsByTagName('stop')) {
                $color = [Windows.Media.ColorConverter]::ConvertFromString($stop.GetAttribute('stop-color'))
                $brush.GradientStops.Add([Windows.Media.GradientStop]::new($color, [double]$stop.GetAttribute('offset')))
            }
        } else { $brush = New-Brush $fill }
        switch ($node.LocalName) {
            'rect' {
                $rect = [Windows.Rect]::new([double]$node.x, [double]$node.y, [double]$node.width, [double]$node.height)
                $drawing.DrawRoundedRectangle($brush, $null, $rect, [double]$node.rx, [double]$node.rx)
            }
            'path' { $drawing.DrawGeometry($brush, $null, [Windows.Media.Geometry]::Parse($node.d)) }
            default { throw "Unsupported SVG element: $($node.LocalName)" }
        }
    }
    $drawing.Pop()
    $drawing.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $bitmap.Freeze()
    return $bitmap
}

function Get-PngBytes($bitmap) {
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try { $encoder.Save($stream); return ,$stream.ToArray() }
    finally { $stream.Dispose() }
}

function Write-Ico([string]$path, [int[]]$sizes, [bool]$trayOnly) {
    $frames = @()
    foreach ($size in $sizes) {
        $source = if ($trayOnly -or $size -le 24) { $traySvg } else { $appSvg }
        $frames += ,(Get-PngBytes (Render-Icon $source $size))
    }
    $stream = [IO.File]::Create($path)
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$i].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    } finally { $writer.Dispose() }
}

[IO.File]::WriteAllBytes((Join-Path $assetPath 'Suiyi.png'), (Get-PngBytes (Render-Icon $appSvg 512)))
Write-Ico (Join-Path $assetPath 'Suiyi.ico') @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256) $false
Write-Ico (Join-Path $assetPath 'Suiyi.Tray.ico') @(16, 20, 24, 32, 40, 48) $true

# Verify all frames with WIC; also check native small-icon selection used by the tray.
foreach ($name in @('Suiyi.ico', 'Suiyi.Tray.ico')) {
    $sizes = if ($name -eq 'Suiyi.ico') { @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256) } else { @(16, 20, 24, 32, 40, 48) }
    $stream = [IO.File]::OpenRead((Join-Path $assetPath $name))
    try {
        $decoder = [Windows.Media.Imaging.IconBitmapDecoder]::new($stream, [Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat, [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        foreach ($size in $sizes) {
            if (-not ($decoder.Frames | Where-Object { $_.PixelWidth -eq $size -and $_.PixelHeight -eq $size })) { throw "Missing frame: $name at $size" }
        }
    } finally { $stream.Dispose() }
    foreach ($size in $sizes) {
        # .NET Framework System.Drawing skips the 256px PNG entry; WIC verifies it above.
        if ($size -gt 48) { continue }
        $icon = [Drawing.Icon]::new((Join-Path $assetPath $name), $size, $size)
        try { if ($icon.Width -ne $size -or $icon.Height -ne $size) { throw "Incorrect frame: $name at $size" } }
        finally { $icon.Dispose() }
    }
}

# Contact sheet includes native tray sizes on light and dark surfaces.
$preview = [Windows.Media.DrawingVisual]::new()
$drawing = $preview.RenderOpen()
$drawing.DrawRectangle((New-Brush '#F7F8FA'), $null, [Windows.Rect]::new(0, 0, 720, 300))
$drawing.DrawRectangle((New-Brush '#222831'), $null, [Windows.Rect]::new(360, 0, 360, 300))
function Draw-Label([string]$text, [double]$x, [double]$y, [int]$size, [string]$color) {
    $formatted = [Windows.Media.FormattedText]::new($text, [Globalization.CultureInfo]::InvariantCulture,
        [Windows.FlowDirection]::LeftToRight, [Windows.Media.Typeface]::new('Segoe UI'), $size, (New-Brush $color))
    $drawing.DrawText($formatted, [Windows.Point]::new($x, $y))
}
Draw-Label 'SUIYI' 30 22 18 '#28312E'
Draw-Label 'SUIYI' 390 22 18 '#EDF0F5'
foreach ($x in @(28, 388)) {
    $drawing.DrawImage((Render-Icon $appSvg 144), [Windows.Rect]::new($x, 64, 144, 144))
    $drawing.DrawImage((Render-Icon $traySvg 72), [Windows.Rect]::new(($x + 190), 100, 72, 72))
    $color = if ($x -eq 28) { '#68746F' } else { '#A5AFBC' }
    Draw-Label 'APP' ($x + 55) 212 10 $color
    Draw-Label 'TRAY' ($x + 214) 180 10 $color
    $position = $x + 12
    foreach ($size in @(16, 20, 24, 32)) {
        $drawing.DrawImage((Render-Icon $traySvg $size), [Windows.Rect]::new($position, (264 - $size / 2), $size, $size))
        $position += 66
    }
}
$drawing.Close()
$bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(720, 300, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($preview)
[IO.File]::WriteAllBytes((Join-Path $previewPath 'suiyi-icons.png'), (Get-PngBytes $bitmap))
Write-Output 'Generated SVG-derived PNG, multi-resolution ICO files and preview; native icon decoding passed.'
