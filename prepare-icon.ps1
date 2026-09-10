$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'assets') -Force | Out-Null
$original = Join-Path $PSScriptRoot 'assets\penguin.png'
if (-not (Test-Path -LiteralPath $original)) { throw 'Place the original icon at assets/penguin.png first.' }
$source = [Drawing.Image]::FromFile($original)
$chunks = @()
$sizes = @(16,24,32,48,64,128,256)
try {
    foreach ($size in $sizes) {
        $bitmap = New-Object Drawing.Bitmap($size,$size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($source,0,0,$size,$size)
        $stream = New-Object IO.MemoryStream
        $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
        $chunks += ,$stream.ToArray()
        $graphics.Dispose(); $bitmap.Dispose(); $stream.Dispose()
    }
    $file = [IO.File]::Create((Join-Path $PSScriptRoot 'assets\penguin.ico'))
    $writer = New-Object IO.BinaryWriter($file)
    $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i=0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -eq 256) {0} else {$sizes[$i]}
        $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$chunks[$i].Length); $writer.Write([UInt32]$offset)
        $offset += $chunks[$i].Length
    }
    foreach ($chunk in $chunks) { $writer.Write([byte[]]$chunk) }
    $writer.Dispose()
} finally { $source.Dispose() }
