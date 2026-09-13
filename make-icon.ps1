$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
function Rounded($x,$y,$w,$h,$r) {
 $p = New-Object Drawing.Drawing2D.GraphicsPath
 $p.AddArc($x,$y,$r,$r,180,90);$p.AddArc(($x+$w-$r),$y,$r,$r,270,90)
 $p.AddArc(($x+$w-$r),($y+$h-$r),$r,$r,0,90);$p.AddArc($x,($y+$h-$r),$r,$r,90,90);$p.CloseFigure();return $p
}
$bmp=New-Object Drawing.Bitmap 256,256
$g=[Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([Drawing.Color]::Transparent)
$bg=Rounded 8 8 240 240 54
$g.FillPath((New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#171615'))),$bg)
$g.DrawPath((New-Object Drawing.Pen ([Drawing.ColorTranslator]::FromHtml('#463A29')),2),$bg)
$book=Rounded 67 47 124 162 14
$gold=New-Object Drawing.Drawing2D.LinearGradientBrush ([Drawing.Point]::new(67,47)),([Drawing.Point]::new(190,209)),([Drawing.ColorTranslator]::FromHtml('#F4CC7E')),([Drawing.ColorTranslator]::FromHtml('#B88032'))
$g.FillPath($gold,$book)
$ink=[Drawing.ColorTranslator]::FromHtml('#553B20')
$g.DrawLine((New-Object Drawing.Pen $ink,3),86,54,86,201)
$line=New-Object Drawing.Pen $ink,4
$line.StartCap=[Drawing.Drawing2D.LineCap]::Round;$line.EndCap=[Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($line,105,109,169,109);$g.DrawLine($line,105,128,169,128);$g.DrawLine($line,105,147,149,147)
$flag=[Drawing.Point[]]@([Drawing.Point]::new(151,47),[Drawing.Point]::new(171,47),[Drawing.Point]::new(171,87),[Drawing.Point]::new(161,79),[Drawing.Point]::new(151,87))
$g.FillPolygon((New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#49311C'))),$flag)
$bmp.Save((Join-Path $PSScriptRoot 'icon-preview.png'),[Drawing.Imaging.ImageFormat]::Png)
$sizes=@(16,24,32,48,64,128,256)
$chunks=New-Object 'System.Collections.Generic.List[byte[]]'
foreach($size in $sizes){
 $small=New-Object Drawing.Bitmap $size,$size
 $sg=[Drawing.Graphics]::FromImage($small);$sg.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic;$sg.DrawImage($bmp,0,0,$size,$size)
 $ms=New-Object IO.MemoryStream;$small.Save($ms,[Drawing.Imaging.ImageFormat]::Png);$chunks.Add($ms.ToArray());$ms.Dispose();$sg.Dispose();$small.Dispose()
}
$file=[IO.File]::Create((Join-Path $PSScriptRoot 'App.ico'));$writer=New-Object IO.BinaryWriter $file
$writer.Write([UInt16]0);$writer.Write([UInt16]1);$writer.Write([UInt16]$sizes.Count)
$offset=6+16*$sizes.Count
for($i=0;$i -lt $sizes.Count;$i++){
 $dimension=$sizes[$i];if($dimension -eq 256){$dimension=0}
 $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([UInt16]1);$writer.Write([UInt16]32);$writer.Write([UInt32]$chunks[$i].Length);$writer.Write([UInt32]$offset);$offset+=$chunks[$i].Length
}
foreach($chunk in $chunks){$writer.Write($chunk)}
$writer.Dispose();$file.Dispose();$g.Dispose();$bmp.Dispose()
