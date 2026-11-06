# мониторы и окна FlowBar в физических пикселях (процесс с учётом DPI)
Add-Type -Namespace WH -Name M -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(System.IntPtr v);
public delegate bool MonProc(System.IntPtr h, System.IntPtr dc, ref RECT r, System.IntPtr d);
[DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(System.IntPtr dc, System.IntPtr clip, MonProc p, System.IntPtr d);
[DllImport("shcore.dll")] public static extern int GetDpiForMonitor(System.IntPtr m, int t, out uint x, out uint y);
public delegate bool WinProc(System.IntPtr h, System.IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumWindows(WinProc p, System.IntPtr l);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint p);
[DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(System.IntPtr h, System.Text.StringBuilder s, int n);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
'@
[void][WH.M]::SetProcessDpiAwarenessContext([IntPtr]-4)
$out = New-Object System.Collections.ArrayList
[void][WH.M]::EnumDisplayMonitors([IntPtr]::Zero, [IntPtr]::Zero, { param($h, $dc, [ref]$r, $d) $x = 0; $y = 0; [void][WH.M]::GetDpiForMonitor($h, 0, [ref]$x, [ref]$y); [void]$out.Add(("монитор {0},{1}..{2},{3} dpi {4}" -f $r.Value.L, $r.Value.T, $r.Value.R, $r.Value.B, $x)); $true }, [IntPtr]::Zero)
$fp = @(Get-Process FlowBar -ErrorAction SilentlyContinue | % { [uint32]$_.Id })
[void][WH.M]::EnumWindows({ param($h, $l) $p = [uint32]0; [void][WH.M]::GetWindowThreadProcessId($h, [ref]$p); if ($fp -contains $p -and [WH.M]::IsWindowVisible($h)) { $r = New-Object WH.M+RECT; [void][WH.M]::GetWindowRect($h, [ref]$r); $t = New-Object Text.StringBuilder 64; [void][WH.M]::GetWindowText($h, $t, 64); [void]$out.Add(("окно '{0}' {1},{2}..{3},{4} ({5}x{6})" -f $t, $r.L, $r.T, $r.R, $r.B, ($r.R - $r.L), ($r.B - $r.T))) }; $true }, [IntPtr]::Zero)
$out
