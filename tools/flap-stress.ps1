# нагрузка: открыть и закрыть окна Блокнота, пока снимается панель; считаются перестроения ряда в «тихие» кадры между событиями
param([int]$y = 1340, [int]$h = 100)
Add-Type -AssemblyName System.Drawing
if (-not ("FS.G" -as [type])) { Add-Type -Namespace FS -Name G -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); [DllImport("user32.dll")] public static extern System.IntPtr GetDC(System.IntPtr h); [DllImport("gdi32.dll")] public static extern bool BitBlt(System.IntPtr d,int x,int y,int w,int h,System.IntPtr s,int sx,int sy,int rop); [DllImport("user32.dll")] public static extern int ReleaseDC(System.IntPtr h, System.IntPtr dc);' }
[void][FS.G]::SetProcessDPIAware()
$w = 2560; $prev = $null; $log = @(); $events = @{ 4 = "open"; 8 = "open"; 12 = "open"; 16 = "open"; 26 = "close"; 30 = "close"; 34 = "close"; 38 = "close" }; $np = New-Object System.Collections.ArrayList
$quietChanges = 0; $eventChanges = 0; $lastEvent = -10
for ($i = 0; $i -lt 50; $i++) {
  if ($events.ContainsKey($i)) {
    $lastEvent = $i
    if ($events[$i] -eq "open") { [void]$np.Add((Start-Process notepad -PassThru)) } elseif ($np.Count -gt 0) { $p = $np[$np.Count - 1]; $np.RemoveAt($np.Count - 1); try { $p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(1500)) { $p.Kill() } } catch {} }
  }
  $bmp = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc(); $s = [FS.G]::GetDC([IntPtr]::Zero)
  [void][FS.G]::BitBlt($hdc, 0, 0, $w, $h, $s, 0, $y, 0x00CC0020 -bor 0x40000000); [void][FS.G]::ReleaseDC([IntPtr]::Zero, $s); $g.ReleaseHdc($hdc); $g.Dispose()
  $cols = New-Object double[] ($w / 8)
  for ($x = 0; $x -lt $w; $x += 8) { $sum = 0; for ($yy = 20; $yy -lt $h - 10; $yy += 6) { $c = $bmp.GetPixel($x, $yy); $sum += $c.R + $c.G + $c.B }; $cols[$x / 8] = $sum }
  $bmp.Dispose()
  if ($prev) {
    $d = 0; for ($k = 0; $k -lt $cols.Length; $k++) { $d += [Math]::Abs($cols[$k] - $prev[$k]) }; $d = [int]($d / 1000)
    $changed = $d -gt 400; $quiet = ($i - $lastEvent) -gt 3
    if ($changed -and $quiet) { $quietChanges++ } elseif ($changed) { $eventChanges++ }
    $log += $(if ($changed) { if ($quiet) { "!" + $d } else { "e" + $d } } else { "." })
  }
  $prev = $cols; Start-Sleep -Milliseconds 250
}
foreach ($p in $np) { try { $p.Kill() } catch {} }
"перестроений рядом с событиями: $eventChanges · в тихие кадры (раскачка): $quietChanges"
"кадры: " + ($log -join " ")
