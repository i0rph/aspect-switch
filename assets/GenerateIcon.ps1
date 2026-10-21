# Rasterizes the matching code-native app.svg geometry; no external tools required.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
function RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = [Drawing.Drawing2D.GraphicsPath]::new()
    $d = 2 * $r
    $p.AddArc($x,$y,$d,$d,180,90); $p.AddArc(($x+$w-$d),$y,$d,$d,270,90)
    $p.AddArc(($x+$w-$d),($y+$h-$d),$d,$d,0,90); $p.AddArc($x,($y+$h-$d),$d,$d,90,90)
    $p.CloseFigure(); return $p
}
$bitmap = [Drawing.Bitmap]::new(1024,1024)
$g = [Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.ScaleTransform(4,4)
$tile = RoundedPath 8 8 240 240 54
$gradient = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(8,8),[Drawing.Point]::new(248,248),[Drawing.ColorTranslator]::FromHtml('#147CE5'),[Drawing.ColorTranslator]::FromHtml('#0750A9'))
$g.FillPath($gradient,$tile)
$screen = RoundedPath 42 62 172 114 14
$fill = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#122B51'))
$white = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#F2F9FF'),12)
$white.StartCap = $white.EndCap = [Drawing.Drawing2D.LineCap]::Round
$g.FillPath($fill,$screen); $g.DrawPath($white,$screen)
$g.DrawLine($white,128,182,128,205); $g.DrawLine($white,94,207,162,207)
$cyan = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#75DEFF'),12)
$cyan.StartCap = $cyan.EndCap = [Drawing.Drawing2D.LineCap]::Round
$cyan.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
foreach ($points in @(@(112,96,88,119,112,142),@(144,96,168,119,144,142))) {
    $g.DrawLines($cyan, [Drawing.PointF[]]@([Drawing.PointF]::new($points[0],$points[1]),[Drawing.PointF]::new($points[2],$points[3]),[Drawing.PointF]::new($points[4],$points[5])))
}
$entries = @()
foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
    $small = [Drawing.Bitmap]::new($size,$size)
    $scaled = [Drawing.Graphics]::FromImage($small)
    $scaled.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $scaled.DrawImage($bitmap,0,0,$size,$size)
    $stream = [IO.MemoryStream]::new(); $small.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $entries += @{ Size=$size; Data=$stream.ToArray() }
    if ($size -eq 256) { $small.Save((Join-Path $PSScriptRoot 'app.png'),[Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $scaled.Dispose(); $small.Dispose()
}
$out = [IO.File]::Create((Join-Path $PSScriptRoot 'app.ico')); $writer = [IO.BinaryWriter]::new($out)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$entries.Count)
$offset = 6 + 16*$entries.Count
foreach ($entry in $entries) {
    $dimension = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([uint16]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$entry.Data.Length); $writer.Write([uint32]$offset)
    $offset += $entry.Data.Length
}
foreach ($entry in $entries) { $writer.Write([byte[]]$entry.Data) }
$writer.Dispose(); $out.Dispose(); $cyan.Dispose(); $white.Dispose(); $fill.Dispose(); $screen.Dispose(); $gradient.Dispose(); $tile.Dispose(); $g.Dispose(); $bitmap.Dispose()
