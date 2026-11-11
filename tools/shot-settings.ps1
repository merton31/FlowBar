# Снимок окна настроек FlowBar: tools\shot-settings.ps1 -Section look -Out файл.png [-HoverX n -HoverY n — навести мышь в точку окна]
param([string]$Section = 'look', [Parameter(Mandatory)][string]$Out, [int]$HoverX = -1, [int]$HoverY = -1)
Add-Type -AssemblyName System.Drawing
if (-not ('U.SS' -as [type])) {
Add-Type -Name SS -Namespace U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern System.IntPtr FindWindow(string c, string n);
[DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int c);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern bool GetCursorPos(out PT p);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, int d, System.UIntPtr e);
public struct RECT { public int L, T, R, B; } public struct PT { public int X, Y; }
'@ }
[void][U.SS]::SetProcessDPIAware()
& "$env:LOCALAPPDATA\Programs\FlowBar\FlowBar.exe" --open $Section; Start-Sleep -Seconds 2
$h = [U.SS]::FindWindow([NullString]::Value, 'FlowBar — Настройки'); if ($h -eq [IntPtr]::Zero) { throw 'окно настроек не найдено' }
[void][U.SS]::ShowWindow($h, 9); [void][U.SS]::SetForegroundWindow($h); Start-Sleep -Milliseconds 700
$r = New-Object U.SS+RECT; [void][U.SS]::GetWindowRect($h, [ref]$r)
$old = New-Object U.SS+PT; [void][U.SS]::GetCursorPos([ref]$old)
if ($HoverX -ge 0) { [void][U.SS]::SetCursorPos($r.L + $HoverX, $r.T + $HoverY); [U.SS]::mouse_event(1, 1, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 700 }
$bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T); $g = [Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size); $bmp.Save($Out)
if ($HoverX -ge 0) { [void][U.SS]::SetCursorPos($old.X, $old.Y) }
"$($r.R - $r.L)x$($r.B - $r.T) -> $Out"
