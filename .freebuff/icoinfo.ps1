param([string]$Path)
$bytes = [System.IO.File]::ReadAllBytes($Path)
$count = [BitConverter]::ToUInt16($bytes, 4)
Write-Host ("count=" + $count)
for ($i = 0; $i -lt $count; $i++) {
 $o = 6 + 16 * $i
 $w = $bytes[$o]
 $h = $bytes[$o + 1]
 if ($w -eq 0) { $w = 256 }
 if ($h -eq 0) { $h = 256 }
 $off = [BitConverter]::ToInt32($bytes, $o + 12)
 $len = [BitConverter]::ToInt32($bytes, $o + 8)
 $png = 0
 if ($len -gt 8) { if ($bytes[$off] -eq 137) { $png = 1 } }
 Write-Host ("entry " + $i + " w=" + $w + " h=" + $h + " len=" + $len + " png=" + $png)
}
Write-Host ("filesize=" + $bytes.Length)
