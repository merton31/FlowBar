# проверка «скачков» панели: N снимков полосы панели и сколько раз ряд менялся между соседними кадрами
param([int]$n = 15, [int]$ms = 250, [int]$y = 1340, [int]$h = 100)
Add-Type -AssemblyName System.Drawing
if (-not ("FT.G" -as [type])) { Add-Type -Namespace FT -Name G -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); [DllImport("user32.dll")] public static extern System.IntPtr GetDC(System.IntPtr h); [DllImport("gdi32.dll")] public static extern bool BitBlt(System.IntPtr d,int x,int y,int w,int h,System.IntPtr s,int sx,int sy,int rop); [DllImport("user32.dll")] public static extern int ReleaseDC(System.IntPtr h, System.IntPtr dc);' }
[void][FT.G]::SetProcessDPIAware()
$w = 2560; $prev = $null; $changes = 0; $diffs = @()
for ($i = 0; $i -lt $n; $i++) {
  $bmp = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc(); $s = [FT.G]::GetDC([IntPtr]::Zero)
  [void][FT.G]::BitBlt($hdc, 0, 0, $w, $h, $s, 0, $y, 0x00CC0020 -bor 0x40000000); [void][FT.G]::ReleaseDC([IntPtr]::Zero, $s); $g.ReleaseHdc($hdc); $g.Dispose()
  # «отпечаток» ряда: яркость по столбцам (сдвиг значков меняет его сильно, смена цифр — слабо)
  $cols = New-Object double[] ($w / 8)
  for ($x = 0; $x -lt $w; $x += 8) { $sum = 0; for ($yy = 20; $yy -lt $h - 10; $yy += 6) { $c = $bmp.GetPixel($x, $yy); $sum += $c.R + $c.G + $c.B }; $cols[$x / 8] = $sum }
  $bmp.Dispose()
  if ($prev) { $d = 0; for ($k = 0; $k -lt $cols.Length; $k++) { $d += [Math]::Abs($cols[$k] - $prev[$k]) }; $diffs += [int]($d / 1000); if ($d / 1000 -gt 400) { $changes++ } }
  $prev = $cols; Start-Sleep -Milliseconds $ms
}
"кадров: $n, перестроений ряда: $changes · разница по кадрам: " + ($diffs -join " ")
