#requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$root = Split-Path $PSScriptRoot -Parent
$brand = Join-Path $root 'assets/brand'
[xml]$source = Get-Content -LiteralPath (Join-Path $brand 'mark.svg') -Raw
$path = $source.DocumentElement.SelectSingleNode('*[local-name()="path"]')
$geometry = [System.Windows.Media.Geometry]::Parse('F0 ' + $path.GetAttribute('d'))
$geometry.Freeze()
$brass = [System.Windows.Media.BrushConverter]::new().ConvertFromString($path.GetAttribute('fill'))
$canvas = [System.Windows.Media.BrushConverter]::new().ConvertFromString('#101113')

function Export-IconPng([int]$Size, [string]$Destination) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    try {
        $drawing.PushTransform([System.Windows.Media.ScaleTransform]::new($Size / 512.0, $Size / 512.0))
        $drawing.DrawRoundedRectangle($canvas, $null, [System.Windows.Rect]::new(0, 0, 512, 512), 104, 104)
        $drawing.DrawGeometry($brass, $null, $geometry)
        $drawing.Pop()
    } finally { $drawing.Close() }
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($Size, $Size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    [void][System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($Destination))
    $stream = [System.IO.File]::Create($Destination)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
}

$frames = @()
foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256, 512)) {
    $file = Join-Path $brand "icon-$size.png"
    Export-IconPng $size $file
    if ($size -le 256) { $frames += @{ Size = $size; Bytes = [System.IO.File]::ReadAllBytes($file) } }
}
$iconDirectory = Join-Path $root 'windows/BedsideRemote.Agent/Assets'
[void][System.IO.Directory]::CreateDirectory($iconDirectory)
$stream = [System.IO.File]::Create((Join-Path $iconDirectory 'AppIcon.ico'))
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose() }

$extensionIcons = Join-Path $root 'browser-extension/static/icons'
[void][System.IO.Directory]::CreateDirectory($extensionIcons)
foreach ($size in @(16, 32, 48, 128)) { Copy-Item -LiteralPath (Join-Path $brand "icon-$size.png") -Destination $extensionIcons }
Copy-Item -LiteralPath (Join-Path $brand 'mark.svg') -Destination $extensionIcons
$drawable = Join-Path $root 'android/app/src/main/res/drawable'
$legacy = '<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="48dp" android:height="48dp" android:viewportWidth="512" android:viewportHeight="512"><path android:fillColor="#101113" android:pathData="M0 0H512V512H0Z"/><path android:fillColor="#D8B57A" android:fillType="evenOdd" android:pathData="' + $path.GetAttribute('d') + '" /></vector>' + "`n"
[System.IO.File]::WriteAllText((Join-Path $drawable 'ic_remote.xml'), $legacy, [System.Text.UTF8Encoding]::new($false))
foreach ($variant in @('foreground', 'monochrome')) {
    $fill = if ($variant -eq 'monochrome') { '#FFFFFF' } else { '#D8B57A' }
    $vector = '<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="108dp" android:height="108dp" android:viewportWidth="512" android:viewportHeight="512"><path android:fillColor="' + $fill + '" android:fillType="evenOdd" android:pathData="' + $path.GetAttribute('d') + '" /></vector>' + "`n"
    [System.IO.File]::WriteAllText((Join-Path $drawable "ic_brand_$variant.xml"), $vector, [System.Text.UTF8Encoding]::new($false))
}
Write-Output 'Generated shared PNG, ICO and Android vector icons from assets/brand/mark.svg.'
