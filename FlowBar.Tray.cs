// Свой трей: значки области уведомлений программ показывает FlowBar, а не панель Windows.
// Как это работает (так же делают альтернативные панели задач): программа добавляет значок вызовом Shell_NotifyIcon,
// а Windows ищет трей через FindWindow("Shell_TrayWnd") и шлёт ему WM_COPYDATA. Наше скрытое окно этого класса
// стоит выше окна Проводника, поэтому находится первым. Каждое сообщение мы запоминаем и пересылаем Проводнику без изменений:
// стандартный трей продолжает работать, а после выхода FlowBar программы снова говорят только с Проводником.
// При запуске рассылается TaskbarCreated — программы заново добавляют свои значки, и мы получаем уже существующие.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FlowBar
{
    class TrayIcon
    {
        public uint HWnd, Id, CbMsg, Version; public Guid Guid; public bool HasGuid, Hidden; public IntPtr Icon; public string Tip = "";
        public IntPtr Wnd { get { return new IntPtr(unchecked((int)HWnd)); } }
        ImageSource img; IntPtr imgOf;
        public ImageSource Image
        {
            get
            {
                if (Icon == IntPtr.Zero) return null;
                if (img == null || imgOf != Icon)
                {
                    try { var b = Imaging.CreateBitmapSourceFromHIcon(Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); b.Freeze(); img = b; } catch { img = null; }
                    imgOf = Icon;
                }
                return img;
            }
        }
    }

    static class Tray
    {
        delegate IntPtr WndProcD(IntPtr h, uint m, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASSEX { public int cbSize, style; public WndProcD lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; public IntPtr hIconSm; }
        [StructLayout(LayoutKind.Sequential)] struct COPYDATA { public IntPtr dwData; public int cbData; public IntPtr lpData; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WNDCLASSEX c);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetProp(IntPtr h, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetProp(IntPtr h, string name, IntPtr value);
        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool SendNotifyMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string s);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] static extern bool ChangeWindowMessageFilterEx(IntPtr h, uint m, uint action, IntPtr p);
        [DllImport("user32.dll")] static extern IntPtr CopyIcon(IntPtr h);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string m);

        const uint WM_COPYDATA = 0x4A, WM_COMMAND = 0x111;
        const int SetProgressValue = 0x440, SetProgressState = 0x441;
        const string TaskbandProperty = "TaskbandHWND";
        static WndProcD proc; static IntPtr wnd; static DispatcherTimer top;
        static HwndSource progressReceiver;
        internal enum ProgressState { None = 0, Indeterminate = 1, Normal = 2, Error = 4, Paused = 8 }
        internal sealed class TaskProgress { public uint Pid; public ProgressState State; public double Value; }
        static readonly Dictionary<IntPtr, TaskProgress> taskProgress = new Dictionary<IntPtr, TaskProgress>();
        internal static int ProgressVersion { get; private set; }
        public static readonly List<TrayIcon> Icons = new List<TrayIcon>();
        public static int Version; // растёт при каждом изменении — открытая карточка перерисовывается
        public static DateTime Broadcast = DateTime.MinValue; // когда мы сами разослали TaskbarCreated
        public static bool On { get { return wnd != IntPtr.Zero; } }

        public static bool Start()
        {
            if (wnd != IntPtr.Zero) return true;
            try
            {
                proc = Proc;
                var c = new WNDCLASSEX { cbSize = Marshal.SizeOf(typeof(WNDCLASSEX)), lpfnWndProc = proc, hInstance = GetModuleHandle(null), lpszClassName = "Shell_TrayWnd" };
                RegisterClassEx(ref c);
                wnd = CreateWindowEx(0x8 | 0x80 | 0x08000000, "Shell_TrayWnd", "", unchecked((int)0x80000000), 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, c.hInstance, IntPtr.Zero); // TOPMOST | TOOLWINDOW | NOACTIVATE, WS_POPUP, не показывается
                if (wnd == IntPtr.Zero) { Bar.Log(new Exception("Свой трей: окно не создано, код " + Marshal.GetLastWin32Error())); return false; }
                ChangeWindowMessageFilterEx(wnd, WM_COPYDATA, 1, IntPtr.Zero); // значки от программ с меньшими правами тоже доходят
                KeepOnTop();
                if (Bar.C("apps.progress", "0") == "1") EnableTaskbarProgress();
                // Проводник поднимает свою панель при показе — возвращаемся выше, иначе новые сообщения уйдут только ему
                top = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; top.Tick += (s, e) => KeepOnTop(); top.Start();
                Broadcast = DateTime.Now;
                SendNotifyMessage(new IntPtr(0xFFFF), RegisterWindowMessage("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero); // программы заново добавят свои значки
                return true;
            }
            catch (Exception ex) { Bar.Log(ex); wnd = IntPtr.Zero; return false; }
        }

        public static void Stop()
        {
            if (top != null) top.Stop();
            if (wnd != IntPtr.Zero) { DestroyWindow(wnd); wnd = IntPtr.Zero; }
            if (progressReceiver != null) { progressReceiver.Dispose(); progressReceiver = null; }
            taskProgress.Clear(); ProgressVersion++;
            foreach (var t in Icons) if (t.Icon != IntPtr.Zero) DestroyIcon(t.Icon);
            Icons.Clear();
        }

        static void KeepOnTop() { if (wnd != IntPtr.Zero) SetWindowPos(wnd, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10); }

        // панель Проводника — первое окно Shell_TrayWnd, которое не наше
        public static IntPtr Explorer()
        {
            for (var h = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null); h != IntPtr.Zero; h = FindWindowEx(IntPtr.Zero, h, "Shell_TrayWnd", null))
                if (h != wnd) return h;
            return IntPtr.Zero;
        }

        static IntPtr ExplorerTaskband()
        {
            uint shellPid;
            if (GetWindowThreadProcessId(GetShellWindow(), out shellPid) == 0) return IntPtr.Zero;
            for (var tray = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null); tray != IntPtr.Zero;
                 tray = FindWindowEx(IntPtr.Zero, tray, "Shell_TrayWnd", null))
            {
                uint pid;
                if (tray == wnd || GetWindowThreadProcessId(tray, out pid) == 0 || pid != shellPid) continue;
                var band = GetProp(tray, TaskbandProperty);
                if (band != IntPtr.Zero) return band;
            }
            return IntPtr.Zero;
        }

        internal static void EnableTaskbarProgress()
        {
            if (wnd == IntPtr.Zero || progressReceiver != null || ExplorerTaskband() == IntPtr.Zero) return;
            HwndSource receiver = null;
            try
            {
                receiver = new HwndSource(new HwndSourceParameters("FlowBar taskband")
                {
                    WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080, Width = 1, Height = 1
                });
                receiver.AddHook(TaskbandProc);
                ChangeWindowMessageFilterEx(receiver.Handle, SetProgressValue, 1, IntPtr.Zero);
                ChangeWindowMessageFilterEx(receiver.Handle, SetProgressState, 1, IntPtr.Zero);
                // Advertise the receiver only on our own tray. Explorer keeps its original property and still receives the messages.
                if (SetProp(wnd, TaskbandProperty, receiver.Handle)) { progressReceiver = receiver; receiver = null; }
            }
            catch (Exception ex) { Bar.Log(ex); }
            finally { if (receiver != null) receiver.Dispose(); }
        }

        static IntPtr TaskbandProc(IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled)
        {
            if (message < 0x400 || message >= 0xC000) return IntPtr.Zero;
            handled = true;
            var band = ExplorerTaskband();
            if (message == SetProgressValue || message == SetProgressState)
            {
                if (band != IntPtr.Zero && band != h) PostMessage(band, (uint)message, w, l);
                RecordTaskProgress(w, message, l.ToInt64());
                return IntPtr.Zero;
            }
            IntPtr result;
            return band != IntPtr.Zero && band != h && SendMessageTimeout(band, (uint)message, w, l, 0x2, 200, out result) != IntPtr.Zero
                ? result : IntPtr.Zero;
        }

        static void RecordTaskProgress(IntPtr window, int message, long data)
        {
            if (window == IntPtr.Zero || data < 0 || data > ushort.MaxValue) return;
            uint pid;
            if (GetWindowThreadProcessId(window, out pid) == 0) return;
            var state = (ProgressState)data;
            if (message == SetProgressState && state == ProgressState.None)
            {
                if (taskProgress.Remove(window)) ProgressVersion++;
                return;
            }
            if (message == SetProgressState && state != ProgressState.Normal && state != ProgressState.Paused &&
                state != ProgressState.Error && state != ProgressState.Indeterminate) return;
            TaskProgress previous;
            if (!taskProgress.TryGetValue(window, out previous) || previous.Pid != pid)
                previous = new TaskProgress { Pid = pid, State = ProgressState.None, Value = 1 };
            // ITaskbarList3 sends a normalized 16-bit value to the taskband on Windows.
            double value = message == SetProgressValue ? data / (double)ushort.MaxValue : previous.Value;
            if (message == SetProgressValue)
                state = previous.State == ProgressState.Error || previous.State == ProgressState.Paused ? previous.State : ProgressState.Normal;
            if (state == previous.State && value == previous.Value) return;
            taskProgress[window] = new TaskProgress { Pid = pid, State = state, Value = value };
            ProgressVersion++;
        }

        internal static TaskProgress ProgressFor(IntPtr window, uint pid)
        {
            TaskProgress progress;
            return taskProgress.TryGetValue(window, out progress) && progress.Pid == pid ? progress : null;
        }

        internal static void PruneTaskProgress()
        {
            foreach (var window in taskProgress.Keys.ToList())
            {
                uint pid;
                if (GetWindowThreadProcessId(window, out pid) != 0 && taskProgress[window].Pid == pid) continue;
                taskProgress.Remove(window); ProgressVersion++;
            }
        }

        static IntPtr Forward(uint m, IntPtr w, IntPtr l, out bool ok)
        {
            IntPtr res = IntPtr.Zero; var ex = Explorer();
            ok = ex != IntPtr.Zero && SendMessageTimeout(ex, m, w, l, 0x2 /* ABORTIFHUNG */, 3000, out res) != IntPtr.Zero;
            return res;
        }

        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool DuplicateHandle(IntPtr srcProc, IntPtr src, IntPtr dstProc, out IntPtr dst, uint access, bool inherit, uint options);

        // SHAppBarMessage (резерв места панелями, положение панели задач для всплывающих окон программ) шлёт WM_COPYDATA первому Shell_TrayWnd — нашему.
        // Общая память для ответа (APPBARMSGDATA: abd 40 байт, dwMessage, hSharedMemory @44, dwSourceProcessId @48) открыта для нашего процесса,
        // Проводник её не прочтёт — передаём ему свою копию описателя на время вызова. Без этого рабочая область не уменьшалась и окна уходили под панель.
        static IntPtr ForwardAppBar(uint m, IntPtr w, COPYDATA cd)
        {
            var ex = Explorer(); if (ex == IntPtr.Zero) return IntPtr.Zero;
            var buf = new byte[cd.cbData]; Marshal.Copy(cd.lpData, buf, 0, cd.cbData);
            uint shared = BitConverter.ToUInt32(buf, 44);
            IntPtr proc = IntPtr.Zero, dup = IntPtr.Zero, mem = Marshal.AllocHGlobal(cd.cbData), pcd = IntPtr.Zero;
            try
            {
                if (shared != 0)
                {
                    uint pid; GetWindowThreadProcessId(ex, out pid);
                    proc = OpenProcess(0x40 /* PROCESS_DUP_HANDLE */, false, pid);
                    if (proc == IntPtr.Zero || !DuplicateHandle(GetCurrentProcess(), new IntPtr((long)shared), proc, out dup, 0, false, 2 /* SAME_ACCESS */)) return IntPtr.Zero;
                    BitConverter.GetBytes((uint)dup.ToInt64()).CopyTo(buf, 44);
                }
                Marshal.Copy(buf, 0, mem, buf.Length);
                pcd = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(COPYDATA)));
                Marshal.StructureToPtr(new COPYDATA { dwData = IntPtr.Zero, cbData = cd.cbData, lpData = mem }, pcd, false);
                IntPtr res; SendMessageTimeout(ex, m, w, pcd, 0x2 /* ABORTIFHUNG */, 3000, out res);
                return res;
            }
            finally
            {
                if (dup != IntPtr.Zero) { IntPtr x; DuplicateHandle(proc, dup, IntPtr.Zero, out x, 0, false, 1 /* CLOSE_SOURCE */); }
                if (proc != IntPtr.Zero) CloseHandle(proc);
                Marshal.FreeHGlobal(mem); if (pcd != IntPtr.Zero) Marshal.FreeHGlobal(pcd);
            }
        }

        static IntPtr Proc(IntPtr h, uint m, IntPtr w, IntPtr l)
        {
            try
            {
                if (m == WM_COPYDATA)
                {
                    var cd = (COPYDATA)Marshal.PtrToStructure(l, typeof(COPYDATA));
                    if (cd.dwData == IntPtr.Zero && cd.cbData >= 52) return ForwardAppBar(m, w, cd);
                    bool ok; var res = Forward(m, w, l, out ok); // Проводнику — всё без изменений (полосы, значки, их положение)
                    if (cd.dwData.ToInt64() == 1 && cd.cbData >= 8 + 24) { bool mine = Handle(cd.lpData, cd.cbData); return ok ? res : (IntPtr)(mine ? 1 : 0); }
                    return res;
                }
                // команды панели от программ (свернуть все окна и т. п.) — тоже Проводнику
                if (m == WM_COMMAND || (m >= 0x400 && m < 0xC000)) { bool ok; return Forward(m, w, l, out ok); }
            }
            catch (Exception ex) { Bar.Log(ex); }
            return DefWindowProc(h, m, w, l);
        }

        // SHELLTRAYDATA: подпись, NIM_*, затем NOTIFYICONDATA с 32-битными описателями (одинаково у 32- и 64-битных программ)
        internal static bool Handle(IntPtr p, int len)
        {
            if (Marshal.ReadInt32(p, 0) != 0x34753423) return false;
            uint msg = (uint)Marshal.ReadInt32(p, 4); IntPtr d = p + 8; int size = Math.Min(Marshal.ReadInt32(d, 0), len - 8);
            uint hw = (uint)Marshal.ReadInt32(d, 4), id = (uint)Marshal.ReadInt32(d, 8), flags = (uint)Marshal.ReadInt32(d, 12);
            bool hasGuid = (flags & 0x20) != 0 && size >= 952; Guid g = Guid.Empty;
            if (hasGuid) { var gb = new byte[16]; Marshal.Copy(d + 936, gb, 0, 16); g = new Guid(gb); }
            var t = Icons.FirstOrDefault(x => hasGuid ? x.HasGuid && x.Guid == g : !x.HasGuid && x.HWnd == hw && x.Id == id);
            if (msg == 2) { if (t == null) return false; Remove(t); return true; } // NIM_DELETE
            if (msg == 4) { if (t == null) return false; if (size >= 804) t.Version = (uint)Marshal.ReadInt32(d, 800); Version++; return true; } // NIM_SETVERSION
            if (msg == 3) return t != null; // NIM_SETFOCUS
            if (msg == 0) { if (t == null) { t = new TrayIcon { HWnd = hw, Id = id, Guid = g, HasGuid = hasGuid }; Icons.Add(t); } } // NIM_ADD; повторное (после перезапуска Проводника) — обновляет известный значок
            else if (msg != 1) return false;
            else if (t == null) { t = new TrayIcon { HWnd = hw, Id = id, Guid = g, HasGuid = hasGuid }; Icons.Add(t); } // NIM_MODIFY незнакомого значка: его NIM_ADD ушёл мимо нас (Проводник был выше) — подбираем сейчас
            t.HWnd = hw; t.Id = id;
            if ((flags & 1) != 0) t.CbMsg = (uint)Marshal.ReadInt32(d, 16);
            if (size >= 804) { uint v = (uint)Marshal.ReadInt32(d, 800); if (v == 3 || v == 4) t.Version = v; } // многие программы (Telegram, Qt) указывают версию прямо при добавлении, без NIM_SETVERSION
            if ((flags & 2) != 0) { if (t.Icon != IntPtr.Zero) DestroyIcon(t.Icon); var hi = new IntPtr(Marshal.ReadInt32(d, 20)); t.Icon = hi == IntPtr.Zero ? IntPtr.Zero : CopyIcon(hi); }
            if ((flags & 4) != 0) t.Tip = Marshal.PtrToStringUni(d + 24, size <= 88 ? 64 : 128).Split('\0')[0];
            if ((flags & 8) != 0 && size >= 288) { uint st = (uint)Marshal.ReadInt32(d, 280), mask = (uint)Marshal.ReadInt32(d, 284); if ((mask & 1) != 0) t.Hidden = (st & 1) != 0; }
            Version++;
            return true;
        }

        static void Remove(TrayIcon t) { if (t.Icon != IntPtr.Zero) DestroyIcon(t.Icon); Icons.Remove(t); Version++; }

        // видимые значки; значки закрытых без NIM_DELETE программ убираем
        public static List<TrayIcon> Visible()
        {
            foreach (var t in Icons.Where(x => !IsWindow(x.Wnd)).ToList()) Remove(t);
            return Icons.Where(x => !x.Hidden).ToList();
        }

        // список всех известных значков (и скрытых) — для сверки с треем Windows: --open traydump
        public static string[] Dump()
        {
            return Icons.Select(t => { uint pid = Pid(t); string exe = null; try { exe = System.Diagnostics.Process.GetProcessById((int)pid).MainModule.FileName; } catch { }
                return (t.Hidden ? "скрыт" : t.Icon == IntPtr.Zero ? "без значка" : "виден") + "\t" + (IsWindow(t.Wnd) ? "" : "окно закрыто\t") + (exe ?? "pid " + pid) + "\t" + t.Tip.Replace("\n", " / ").Replace("\r", ""); }).ToArray();
        }
        // Программы, чьи значки Windows помнит (реестр NotifyIconSettings, с картинкой значка), запущенные сейчас, но не приславшие значок FlowBar:
        // такие программы добавляют значок только при своём запуске, а FlowBar запустился позже. Клик по ним открывает окно программы.
        public class Missed { public string Exe, Name; public ImageSource Image; }
        public static List<Missed> MissedApps(IEnumerable<string> haveExe)
        {
            var have = new HashSet<string>(haveExe.Where(x => x != null).Select(x => System.IO.Path.GetFileName(x).ToLowerInvariant()));
            var running = new Dictionary<string, string>();
            foreach (var p in System.Diagnostics.Process.GetProcesses()) { try { var path = Bar.ExePathOf((uint)p.Id); if (path != null) running[System.IO.Path.GetFileName(path).ToLowerInvariant()] = path; } catch { } finally { p.Dispose(); } }
            var res = new List<Missed>(); var seen = new HashSet<string>();
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings"))
            {
                if (k == null) return res;
                foreach (var sub in k.GetSubKeyNames())
                    using (var s = k.OpenSubKey(sub))
                    {
                        var exe = s == null ? null : s.GetValue("ExecutablePath") as string; if (exe == null) continue;
                        var fn = System.IO.Path.GetFileName(exe).ToLowerInvariant(); string full;
                        if (have.Contains(fn) || seen.Contains(fn) || !running.TryGetValue(fn, out full) || fn == "explorer.exe" || fn == "flowbar.exe" || fn == "powershell.exe") continue;
                        seen.Add(fn);
                        ImageSource img = null; var png = s.GetValue("IconSnapshot") as byte[];
                        if (png != null) try { var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = new System.IO.MemoryStream(png); bi.EndInit(); bi.Freeze(); img = bi; } catch { img = null; }
                        res.Add(new Missed { Exe = full, Name = (s.GetValue("InitialTooltip") as string) ?? "", Image = img });
                    }
            }
            return res;
        }
        public static uint Pid(TrayIcon t) { uint pid; GetWindowThreadProcessId(t.Wnd, out pid); return pid; }

        // нажатие — так же, как его передаёт программе панель Windows: у версии 4 событие и номер значка в lParam, точка — в wParam
        public static void Click(TrayIcon t, bool right, bool dbl)
        {
            if (t.CbMsg == 0) { if (right) Bar.Info("трей, правая кнопка: у значка нет сообщения для кликов — " + Bar.ExePathOf(Pid(t))); return; }
            uint pid = Pid(t), me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id, fp;
            GetWindowThreadProcessId(N.GetForegroundWindow(), out fp); bool weFront = fp == me;
            bool allowed = AllowSetForegroundWindow(pid); // программа сможет вывести своё окно или меню вперёд
            // меню: окно значка — вперёд заранее, как при клике по трею Windows. Без этого меню части программ не появляется или сразу закрывается
            bool fronted = right && N.SetForegroundWindow(t.Wnd);
            POINT pt; GetCursorPos(out pt);
            uint[] seq = right ? new uint[] { 0x204, 0x205 } : dbl ? new uint[] { 0x203, 0x202 } : new uint[] { 0x201, 0x202 };
            bool v4 = t.Version >= 4;
            IntPtr wp = v4 ? new IntPtr((pt.X & 0xFFFF) | (pt.Y << 16)) : new IntPtr(unchecked((int)t.Id));
            foreach (var e in seq) PostMessage(t.Wnd, t.CbMsg, wp, v4 ? new IntPtr((int)(e | (t.Id << 16))) : new IntPtr((int)e));
            if (v4) PostMessage(t.Wnd, t.CbMsg, wp, new IntPtr((int)((right ? 0x7Bu : 0x400u) | (t.Id << 16)))); // WM_CONTEXTMENU или NIN_SELECT
            // журнал: правая кнопка у части программ не открывает меню — что получила программа и что оказалось впереди
            if (right)
            {
                string exe = Bar.ExePathOf(pid);
                diag = new System.Threading.Timer(_ =>
                {
                    uint p2; GetWindowThreadProcessId(N.GetForegroundWindow(), out p2);
                    Bar.Info("трей, правая кнопка: " + exe + ", версия значка " + t.Version + ", FlowBar впереди: " + (weFront ? "да" : "нет") + ", разрешение: " + (allowed ? "да" : "нет")
                        + ", окно значка вперёд: " + (fronted ? "да" : "нет") + "; через 0,6 с впереди: " + (p2 == pid ? "эта программа" : p2 == me ? "FlowBar" : Bar.ExePathOf(p2)));
                }, null, 600, System.Threading.Timeout.Infinite);
            }
        }
        static System.Threading.Timer diag;
    }
}
