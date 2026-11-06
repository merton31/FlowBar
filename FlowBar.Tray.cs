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
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string m);

        const uint WM_COPYDATA = 0x4A, WM_COMMAND = 0x111;
        static WndProcD proc; static IntPtr wnd; static DispatcherTimer top;
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
                // Проводник поднимает свою панель при показе — возвращаемся выше, иначе новые сообщения уйдут только ему
                top = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; top.Tick += (s, e) => KeepOnTop(); top.Start();
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

        static IntPtr Forward(uint m, IntPtr w, IntPtr l, out bool ok)
        {
            IntPtr res = IntPtr.Zero; var ex = Explorer();
            ok = ex != IntPtr.Zero && SendMessageTimeout(ex, m, w, l, 0x2 /* ABORTIFHUNG */, 3000, out res) != IntPtr.Zero;
            return res;
        }

        static IntPtr Proc(IntPtr h, uint m, IntPtr w, IntPtr l)
        {
            try
            {
                if (m == WM_COPYDATA)
                {
                    var cd = (COPYDATA)Marshal.PtrToStructure(l, typeof(COPYDATA));
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
            else if (msg != 1 || t == null) return false; // NIM_MODIFY неизвестного значка
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
            return Icons.Where(x => !x.Hidden && x.Icon != IntPtr.Zero).ToList();
        }

        public static uint Pid(TrayIcon t) { uint pid; GetWindowThreadProcessId(t.Wnd, out pid); return pid; }

        // нажатие — так же, как его передаёт программе панель Windows: у версии 4 событие и номер значка в lParam, точка — в wParam
        public static void Click(TrayIcon t, bool right, bool dbl)
        {
            if (t.CbMsg == 0) return;
            AllowSetForegroundWindow(Pid(t)); // программа сможет вывести своё окно или меню вперёд
            POINT pt; GetCursorPos(out pt);
            uint[] seq = right ? new uint[] { 0x204, 0x205 } : dbl ? new uint[] { 0x203, 0x202 } : new uint[] { 0x201, 0x202 };
            bool v4 = t.Version >= 4;
            IntPtr wp = v4 ? new IntPtr((pt.X & 0xFFFF) | (pt.Y << 16)) : new IntPtr(unchecked((int)t.Id));
            foreach (var e in seq) PostMessage(t.Wnd, t.CbMsg, wp, v4 ? new IntPtr((int)(e | (t.Id << 16))) : new IntPtr((int)e));
            if (v4) PostMessage(t.Wnd, t.CbMsg, wp, new IntPtr((int)((right ? 0x7Bu : 0x400u) | (t.Id << 16)))); // WM_CONTEXTMENU или NIN_SELECT
        }
    }
}
