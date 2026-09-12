param(
  [string]$png = 'D:\LUODA\LDv2rayN\LDv2rayN2.png',
  [string]$out = 'D:\LUODA\LDv2rayN\v2rayN\Resources\LDv2rayN.ico'
)

Add-Type -AssemblyName System.Drawing

$bmp = [System.Drawing.Bitmap]::FromFile($png)
$sizes = @(16, 32, 48, 64, 128, 256)
$imgList = New-Object System.Collections.ArrayList
$bytesList = New-Object System.Collections.ArrayList

foreach ($s in $sizes) {
  $img = New-Object System.Drawing.Bitmap($s, $s)
  $g = [System.Drawing.Graphics]::FromImage($img)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.Clear([System.Drawing.Color]::Transparent)
  $g.DrawImage($bmp, 0, 0, $s, $s)
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $img.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  [void]$imgList.Add($img)
  [void]$bytesList.Add($ms.ToArray())
  $ms.Dispose()
}

$w = New-Object System.IO.BinaryWriter([System.IO.File]::OpenWrite($out))

# ICONDIR
$w.Write([UInt16]0)
$w.Write([UInt16]1)
$w.Write([UInt16]$sizes.Count)

$headerSize = 6 + $sizes.Count * 16
$offset = $headerSize

# ICONDIRENTRY
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]
  $len = $bytesList[$i].Length
  if ($s -ge 256) { $wDim = 0; $wHeight = 0 } else { $wDim = $s; $wHeight = $s }
  $w.Write([Byte]$wDim)
  $w.Write([Byte]$wHeight)
  $w.Write([Byte]0)
  $w.Write([Byte]0)
  $w.Write([UInt16]1)
  $w.Write([UInt16]32)
  $w.Write([UInt32]$len)
  $w.Write([UInt32]$offset)
  $offset += $len
}

for ($i = 0; $i -lt $bytesList.Count; $i++) {
  $w.Write($bytesList[$i])
}

$w.Flush()
$w.Dispose()
$bmp.Dispose()
foreach ($img in $imgList) { $img.Dispose() }

Write-Output ("WROTE " + $out + " size=" + (Get-Item $out).Length + " bytes, " + $sizes.Count + " sizes")
