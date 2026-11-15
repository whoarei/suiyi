# Render a schematic, not a recording. Run: powershell.exe -NoProfile -STA -File scripts/generate-readme-demo.ps1
param([string]$FfmpegPath, [ValidateSet('zh','en')][string]$Language = 'zh', [ValidateSet('Light','Dark')][string]$Mode = 'Light')
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run with -STA.' }
Add-Type -AssemblyName PresentationCore, WindowsBase
if (-not $FfmpegPath) { $FfmpegPath = (Get-Command ffmpeg -ErrorAction Stop).Source }
$projectRoot = Split-Path -Parent $PSScriptRoot
$frames = Join-Path $projectRoot ('artifacts\readme-animation-' + [Guid]::NewGuid().ToString('N'))
$dark = $Mode -eq 'Dark'
$suffix = $(if ($dark) { '-dark' } else { '' }) + $(if ($Language -eq 'en') { '.en' } else { '' })
$output = Join-Path $projectRoot ('docs\assets\quick-replace' + $suffix + '.gif')
$theme = if ($dark) {
    @{ Canvas='#161B21'; Shadow='#12171D'; Surface='#222831'; Field='#2B323C'; Text='#EDF0F5'; Muted='#A5AFBC'; Border='#414A57'; Soft='#4A383B'; AccentBorder='#9C584F'; Selection='#664848'; AccentText='#FFA695'; Accent='#FF8067'; OnAccent='#171717' }
} else {
    @{ Canvas='#F3F5F7'; Shadow='#E8EBED'; Surface='#FFFFFF'; Field='#F7F8FA'; Text='#28312E'; Muted='#68746F'; Border='#E1E6E2'; Soft='#FFF0ED'; AccentBorder='#FFC6BB'; Selection='#F9D6D1'; AccentText='#AE5B4B'; Accent='#FF8067'; OnAccent='#171717' }
}
$labels = if ($Language -eq 'en') {
    @{ Name='Suiyi'; Tagline='Select a sentence. Switch the language.'; Mode='Quick mode · Demo'; Draft='Message draft'; Busy='Translating…'; Done='Replaced'; Select='Select text'; Replace='Replace in place'; Footer='Text outside the selection stays unchanged.' }
} else {
    @{ Name='随译'; Tagline='选中一句，换一种语言。'; Mode='快速模式 · 示意'; Draft='消息草稿'; Busy='翻译中…'; Done='已替换'; Select='选中文字'; Replace='原位替换'; Footer='选区之外，保持原样。' }
}
New-Item -ItemType Directory -Path $frames -Force | Out-Null
$fps = 15
$width = 960
$height = 440
$source = '我们明天下午三点见。'
$translation = 'See you tomorrow at 3 PM.'
$culture = [Globalization.CultureInfo]::GetCultureInfo('zh-CN')
$icon = [Windows.Media.Imaging.BitmapImage]::new([Uri](Join-Path $projectRoot 'src\TranslationFloat.App\Assets\Suiyi.png'))

function Brush([string]$Color) { [Windows.Media.BrushConverter]::new().ConvertFromInvariantString($Color) }
function Text([string]$Value, [double]$Size, [string]$Color, [bool]$Bold = $false) {
    $weight = if ($Bold) { [Windows.FontWeights]::SemiBold } else { [Windows.FontWeights]::Normal }
    $face = [Windows.Media.Typeface]::new([Windows.Media.FontFamily]::new('Segoe UI, Microsoft YaHei UI'), [Windows.FontStyles]::Normal, $weight, [Windows.FontStretches]::Normal)
    [Windows.Media.FormattedText]::new($Value, $culture, [Windows.FlowDirection]::LeftToRight, $face, $Size, (Brush $Color))
}
function Label([string]$Value, [double]$X, [double]$Y, [double]$Size = 16, [string]$Color = $theme.Text, [bool]$Bold = $false) {
    $drawing.DrawText((Text $Value $Size $Color $Bold), [Windows.Point]::new($X, $Y))
}
function Round([double]$X, [double]$Y, [double]$W, [double]$H, [double]$Radius, [string]$Fill, [string]$Stroke = '') {
    $pen = if ($Stroke) { [Windows.Media.Pen]::new((Brush $Stroke), 1) } else { $null }
    $drawing.DrawRoundedRectangle((Brush $Fill), $pen, [Windows.Rect]::new($X, $Y, $W, $H), $Radius, $Radius)
}
function Ease([double]$Value) { $v = [Math]::Max(0, [Math]::Min(1, $Value)); $v * $v * (3 - 2 * $v) }
function Step([int]$Number, [string]$Title, [double]$X, [double]$W, [bool]$Active, [bool]$Done) {
    $fill = if ($Active) { $theme.Soft } else { $theme.Surface }
    $border = if ($Active) { $theme.AccentBorder } else { $theme.Border }
    $ink = if ($Active -or $Done) { $theme.AccentText } else { $theme.Muted }
    Round $X 344 $W 42 10 $fill $border
    $dot = if ($Active -or $Done) { $theme.Accent } else { $theme.Muted }
    $numberInk = if ($Active -or $Done) { $theme.OnAccent } else { $theme.Surface }
    $drawing.DrawEllipse((Brush $dot), $null, [Windows.Point]::new(($X + 23), 365), 10, 10)
    Label ([string]$Number) ($X + 19) 355 13 $numberInk $true
    Label $Title ($X + 44) 352 17 $ink $true
}
function Save-Png($Visual, [string]$Path) {
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($width, $height, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($Visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create($Path)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
}

$sourceWidth = (Text $source 28 $theme.Text $true).Width
$englishWidth = (Text $translation 28 $theme.Text $true).Width
for ($frame = 0; $frame -lt 108; $frame++) {
    $t = $frame / $fps
    $select = Ease (($t - 0.55) / 1.0)
    $swap = Ease (($t - 3.15) / 0.38)
    $reset = Ease (($t - 6.45) / 0.65)
    $phase = if ($t -lt 2.15 -or $t -ge 6.45) { 1 } elseif ($t -lt 3.53) { 2 } else { 3 }
    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $drawing.DrawRectangle((Brush $theme.Canvas), $null, [Windows.Rect]::new(0, 0, $width, $height))
    Round 20 25 920 397 20 $theme.Shadow
    Round 20 20 920 398 20 $theme.Surface $theme.Border
    $drawing.DrawImage($icon, [Windows.Rect]::new(50, 42, 38, 38))
    Label $labels.Name 99 43 25 $theme.Text $true
    Label $labels.Tagline 52 94 18 $theme.Muted
    Round 738 47 170 30 15 $theme.Soft
    $drawing.DrawEllipse((Brush $theme.Accent), $null, [Windows.Point]::new(755, 62), 3, 3)
    Label $labels.Mode 767 51 14 $theme.AccentText
    $border = if ($phase -eq 3) { $theme.AccentBorder } else { $theme.Border }
    Round 52 132 856 188 12 $theme.Field $border
    Label $labels.Draft 74 145 12 $theme.Muted
    Label 'Hi Alex,' 76 175 18 $theme.Muted
    Label '— Lin' 76 273 18 $theme.Muted
    if ($select -gt 0 -and $t -lt 3.53) {
        $drawing.PushOpacity((1 - $swap))
        Round 70 213 (($sourceWidth + 12) * $select) 44 5 $theme.Selection
        $drawing.Pop()
    }
    $drawing.PushOpacity((1 - $swap) + $swap * $reset)
    Label $source 76 216 28 $theme.Text $true
    $drawing.Pop()
    if ($swap -gt 0 -and $reset -lt 1) {
        $drawing.PushOpacity(($swap * (1 - $reset)))
        $drawing.PushClip([Windows.Media.RectangleGeometry]::new([Windows.Rect]::new(70, 210, (($englishWidth + 28) * $swap), 50)))
        Label $translation 76 216 28 $theme.AccentText $true
        $drawing.Pop()
        if (([int](($t - 3.53) * 2.5) % 2) -eq 0 -and $t -ge 3.53) {
            $drawing.DrawLine([Windows.Media.Pen]::new((Brush $theme.Accent), 2), [Windows.Point]::new((82 + $englishWidth), 219), [Windows.Point]::new((82 + $englishWidth), 250))
        }
        $drawing.Pop()
    }
    if ($t -ge 1.0 -and $t -lt 2.05) {
        $drawing.PushTransform([Windows.Media.TranslateTransform]::new((72 + $sourceWidth * $select), 247))
        $drawing.DrawGeometry((Brush $theme.Surface), [Windows.Media.Pen]::new((Brush $theme.Text), 1.3), [Windows.Media.Geometry]::Parse('M0,0 L0,19 L5,14 L10,24 L14,22 L9,12 L17,12 Z'))
        $drawing.Pop()
    }
    if ($phase -eq 2) {
        Label $labels.Busy 796 145 12 $theme.AccentText
        $pulse = ($t - 2.15) / (3.53 - 2.15)
        $drawing.DrawLine([Windows.Media.Pen]::new((Brush $theme.AccentBorder), 2), [Windows.Point]::new(74, 312), [Windows.Point]::new((74 + 812 * $pulse), 312))
    }
    if ($phase -eq 3) { Label $labels.Done 811 145 12 $theme.AccentText }
    Step 1 $labels.Select 52 230 ($phase -eq 1) ($phase -gt 1)
    Step 2 'Ctrl + Alt + T' 328 270 ($phase -eq 2) ($phase -gt 2)
    Step 3 $labels.Replace 644 264 ($phase -eq 3) $false
    Label '→' 296 349 22 $theme.Muted
    Label '→' 612 349 22 $theme.Muted
    Label $labels.Footer 52 396 11 $theme.Muted
    $drawing.Close()
    Save-Png $visual (Join-Path $frames ('frame-{0:D3}.png' -f $frame))
}
& $FfmpegPath -hide_banner -loglevel error -y -framerate $fps -i (Join-Path $frames 'frame-%03d.png') -filter_complex 'split[a][b];[a]palettegen=max_colors=128:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle' -loop 0 -gifflags '+transdiff' $output
if ($LASTEXITCODE -ne 0) { throw 'GIF encoding failed.' }
if ((Get-Item -LiteralPath $output).Length -gt 1MB) { throw 'README GIF should stay under 1 MB.' }
Write-Output "Animation: $output"
Write-Output "Preview frames: $frames"
