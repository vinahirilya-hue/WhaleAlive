param([string]$ProjectRoot = (Join-Path $PSScriptRoot '..'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$brandRoot = Join-Path $ProjectRoot 'prototype\Branding'
$assetRoot = Join-Path $ProjectRoot 'prototype\Assets\Brand'
[IO.Directory]::CreateDirectory($assetRoot) | Out-Null
$logo = [Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText((Join-Path $brandRoot 'WhaleLogo.xaml')))
function Render-Logo([int]$size) {
    $visual = New-Object Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $width = $size * .88
    $height = $width * $logo.Height / $logo.Width
    $context.DrawImage($logo, [Windows.Rect]::new(($size-$width)/2,($size-$height)/2,$width,$height))
    $context.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    $encoder.Save($stream)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return ,$bytes
}
$sizes = @(16,20,24,32,40,48,64,128,256)
$images = @($sizes | ForEach-Object { ,(Render-Logo $_) })
$output = [IO.File]::Create((Join-Path $brandRoot 'WhaleAlive.ico'))
$writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16*$sizes.Count
    for($index=0;$index -lt $sizes.Count;$index++) {
        $dimension = if($sizes[$index] -eq 256){0}else{$sizes[$index]}
        $writer.Write([byte]$dimension);$writer.Write([byte]$dimension)
        $writer.Write([byte]0);$writer.Write([byte]0)
        $writer.Write([uint16]1);$writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length);$writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach($bytes in $images){$writer.Write([byte[]]$bytes)}
} finally {$writer.Dispose();$output.Dispose()}
foreach($size in @(44,50,150,256,512)) {
    [IO.File]::WriteAllBytes((Join-Path $assetRoot "WhaleAlive-$size.png"),(Render-Logo $size))
}
Write-Output 'Exported the shared whale vector to a multi-resolution ICO and application PNG assets.'
