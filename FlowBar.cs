// FlowBar — плавающая панель задач для Windows 10/11. Сборка: build.ps1 (C# 5, .NET Framework 4 + WinRT, без зависимостей).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = Windows.Foundation;
using MC = Windows.Media.Control;
using RD = Windows.Devices.Radios;
using SS = Windows.Storage.Streams;

namespace FlowBar
{
    static class N
    {
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string c, string t);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
        [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint tid);
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUT li);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT p, uint flags);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("user32.dll")] public static extern int SetWindowCompositionAttribute(IntPtr h, ref WCAD d);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")] public static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] public static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref THUMBPROPS p);
        [DllImport("dwmapi.dll")] public static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out POINT size);
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder s, ref int size);
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMSTAT m);

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        // AppBar: Windows резервирует полосу экрана под панель, развёрнутые окна под неё не заходят
        [StructLayout(LayoutKind.Sequential)] public struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage, uEdge; public RECT rc; public IntPtr lParam; }
        [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA d);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct LASTINPUT { public uint Size, Time; }
        [StructLayout(LayoutKind.Sequential)] public struct THUMBPROPS { public int Flags; public RECT Dest, Src; public byte Opacity; public int Visible, ClientOnly; }
        [StructLayout(LayoutKind.Sequential)] public struct MEMSTAT { public uint Len, Load; public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirt, AvailVirt, AvailExt; }
        [StructLayout(LayoutKind.Sequential)] public struct ACCENT { public int State, Flags, Color, Anim; }
        [StructLayout(LayoutKind.Sequential)] public struct WCAD { public int Attr; public IntPtr Data; public int Size; }
        // значки приложений Store и других элементов оболочки (shell:AppsFolder\...)
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr hbmp); }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] public struct DIBSECTION { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; public int biSize, biWidth, biHeight; public ushort biPlanes, biBitCount; public int biCompression, biSizeImage, biXPels, biYPels, biClrUsed, biClrImportant, m0, m1, m2; public IntPtr hSection; public int offset; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)] public static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory item);
    [DllImport("gdi32.dll")] public static extern int GetObject(IntPtr h, int size, ref DIBSECTION ds);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RBINFO { public int Size; public long Bytes, Items; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHQueryRecycleBin(string root, ref RBINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);
        [DllImport("shell32.dll")] public static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
    }

    // .NET Framework не видит штатные await-расширения WinRT — свой минимальный адаптер
    // Громкость устройства вывода по умолчанию — Core Audio (IAudioEndpointVolume); устройство перечитывается раз в 10 с
    static partial class Audio
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class Enumerator { }
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator { [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IMMDeviceCollection devs); [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice dev); }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice { [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o); [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore ps); [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id); }
        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection { [PreserveSig] int GetCount(out int n); [PreserveSig] int Item(int i, out IMMDevice dev); }
        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore { [PreserveSig] int GetCount(out int n); [PreserveSig] int GetAt(int i, out PKey k); [PreserveSig] int GetValue(ref PKey k, out PropVar v); }
        [StructLayout(LayoutKind.Sequential)] struct PKey { public Guid Fmt; public int Pid; }
        [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVar { [FieldOffset(0)] public ushort Vt; [FieldOffset(8)] public IntPtr P; }
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVar v);
        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr p); [PreserveSig] int UnregisterControlChangeNotify(IntPtr p); [PreserveSig] int GetChannelCount(out int n);
            [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx); [PreserveSig] int SetMasterVolumeLevelScalar(float v, ref Guid ctx);
            [PreserveSig] int GetMasterVolumeLevel(out float db); [PreserveSig] int GetMasterVolumeLevelScalar(out float v);
            [PreserveSig] int SetChannelVolumeLevel(int ch, float db, ref Guid ctx); [PreserveSig] int SetChannelVolumeLevelScalar(int ch, float v, ref Guid ctx);
            [PreserveSig] int GetChannelVolumeLevel(int ch, out float db); [PreserveSig] int GetChannelVolumeLevelScalar(int ch, out float v);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool m, ref Guid ctx); [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool m);
        }
        static IAudioEndpointVolume ep; static string name; static DateTime at;
        static IAudioEndpointVolume Ep()
        {
            if (ep != null && (DateTime.Now - at).TotalSeconds < 10) return ep;
            at = DateTime.Now; ep = null; name = null;
            try
            {
                var en = (IMMDeviceEnumerator)new Enumerator(); IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return null; // вывод, мультимедиа
                var iid = typeof(IAudioEndpointVolume).GUID; object o;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) == 0) ep = o as IAudioEndpointVolume;
                IPropertyStore ps;
                if (dev.OpenPropertyStore(0, out ps) == 0 && ps != null)
                {
                    var k = new PKey { Fmt = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Pid = 14 }; PropVar v; // имя устройства
                    if (ps.GetValue(ref k, out v) == 0) { if (v.Vt == 31) name = Marshal.PtrToStringUni(v.P); PropVariantClear(ref v); }
                }
            }
            catch { ep = null; }
            return ep;
        }
        public static bool Read(out double vol, out bool mute, out string dev)
        {
            vol = double.NaN; mute = false; dev = null;
            try
            {
                var e = Ep(); if (e == null) return false;
                float v; bool m; if (e.GetMasterVolumeLevelScalar(out v) != 0) { ep = null; return false; }
                e.GetMute(out m); vol = v; mute = m; dev = name; return true;
            }
            catch { ep = null; return false; }
        }
        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2 { [PreserveSig] int GetAudioSessionControl(IntPtr g, int f, out IntPtr c); [PreserveSig] int GetSimpleAudioVolume(IntPtr g, int f, out IntPtr v); [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e); }
        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator { [PreserveSig] int GetCount(out int n); [PreserveSig] int GetSession(int i, out IAudioSessionControl2 s); }
        [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            [PreserveSig] int GetState(out int st); [PreserveSig] int GetDisplayName(out IntPtr s); [PreserveSig] int SetDisplayName(string s, IntPtr g); [PreserveSig] int GetIconPath(out IntPtr s); [PreserveSig] int SetIconPath(string s, IntPtr g);
            [PreserveSig] int GetGroupingParam(out Guid g); [PreserveSig] int SetGroupingParam(ref Guid g, IntPtr c); [PreserveSig] int RegisterAudioSessionNotification(IntPtr n); [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
            [PreserveSig] int GetSessionIdentifier(out IntPtr s); [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr s); [PreserveSig] int GetProcessId(out uint pid);
        }
        // звук процесса сейчас идёт на этом компьютере: true — сеанс активен, false — сеансы есть, но молчат, null — сеансов нет или не удалось узнать
        public static bool? Playing(HashSet<uint> pids)
        {
            try
            {
                var en = (IMMDeviceEnumerator)new Enumerator(); IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return null;
                var iid = typeof(IAudioSessionManager2).GUID; object o;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return null;
                var mgr = (IAudioSessionManager2)o; IAudioSessionEnumerator e; if (mgr.GetSessionEnumerator(out e) != 0) return null;
                int n; e.GetCount(out n); bool any = false;
                for (int i = 0; i < n; i++)
                {
                    IAudioSessionControl2 s; if (e.GetSession(i, out s) != 0 || s == null) continue;
                    uint pid; if (s.GetProcessId(out pid) != 0 || !pids.Contains(pid)) continue;
                    int st; s.GetState(out st); any = true; if (st == 1) return true;
                }
                return any ? (bool?)false : null;
            }
            catch { return null; }
        }
        // ---------- устройства вывода ----------
        // Сменить устройство по умолчанию официально нельзя: недокументированный IPolicyConfig (тот же, что у EarTrumpet и SoundSwitch); может перестать работать в новой Windows
        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigClient { }
        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat(); [PreserveSig] int GetDeviceFormat(); [PreserveSig] int ResetDeviceFormat(); [PreserveSig] int SetDeviceFormat(); [PreserveSig] int GetProcessingPeriod();
            [PreserveSig] int SetProcessingPeriod(); [PreserveSig] int GetShareMode(); [PreserveSig] int SetShareMode(); [PreserveSig] int GetPropertyValue(); [PreserveSig] int SetPropertyValue();
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        }
        static string NameOf(IMMDevice dev)
        {
            IPropertyStore ps; string n = null;
            if (dev.OpenPropertyStore(0, out ps) == 0 && ps != null)
            {
                var k = new PKey { Fmt = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Pid = 14 }; PropVar v;
                if (ps.GetValue(ref k, out v) == 0) { if (v.Vt == 31) n = Marshal.PtrToStringUni(v.P); PropVariantClear(ref v); }
            }
            return n;
        }
        // включённые устройства вывода: id, имя, по умолчанию ли
        public static List<Tuple<string, string, bool>> Devices()
        {
            var res = new List<Tuple<string, string, bool>>();
            try
            {
                var en = (IMMDeviceEnumerator)new Enumerator(); IMMDevice def; string defId = null;
                if (en.GetDefaultAudioEndpoint(0, 1, out def) == 0 && def != null) def.GetId(out defId);
                IMMDeviceCollection col; if (en.EnumAudioEndpoints(0, 1 /* DEVICE_STATE_ACTIVE */, out col) != 0 || col == null) return res;
                int n; col.GetCount(out n);
                for (int i = 0; i < n; i++) { IMMDevice dv; string id; if (col.Item(i, out dv) != 0 || dv == null || dv.GetId(out id) != 0) continue; res.Add(Tuple.Create(id, NameOf(dv) ?? "Устройство " + (i + 1), id == defId)); }
            }
            catch (Exception ex) { Bar.Log(ex); }
            return res;
        }
        public static string SetDefault(string id)
        {
            try
            {
                var pc = (IPolicyConfig)new PolicyConfigClient();
                for (int role = 0; role < 3; role++) { int hr = pc.SetDefaultEndpoint(id, role); if (hr != 0) return "Windows не переключила устройство (0x" + hr.ToString("X8") + ")"; } // консоль, мультимедиа, связь
                ep = null; return null;
            }
            catch (Exception ex) { Bar.Log(ex); return "Эта версия Windows не даёт переключить устройство: " + ex.Message; }
        }
        // сигнал при смене громкости, как в Windows: стандартный звук схемы на новом уровне; не чаще раза в 150 мс
        static DateTime beepAt;
        public static void Beep() { if ((DateTime.Now - beepAt).TotalMilliseconds < 150) return; beepAt = DateTime.Now; try { SystemSounds.Beep.Play(); } catch { } }
        public static void SetVolume(float v) { try { var e = Ep(); if (e == null) return; var g = Guid.Empty; e.SetMasterVolumeLevelScalar(Math.Max(0, Math.Min(1, v)), ref g); if (v > 0) e.SetMute(false, ref g); } catch { ep = null; } }
        public static void SetMute(bool on) { try { var e = Ep(); if (e == null) return; var g = Guid.Empty; e.SetMute(on, ref g); } catch { ep = null; } }
    }

    // Значки рабочего стола и «не засыпать» — обычные средства Windows
    static class Desk
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint f);
        static IntPtr DefView()
        {
            var v = FindWindowEx(FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null); var w = IntPtr.Zero;
            while (v == IntPtr.Zero && (w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero) v = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
            return v;
        }
        public static bool IconsShown { get { var v = DefView(); var lv = v == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(v, IntPtr.Zero, "SysListView32", null); return lv != IntPtr.Zero && IsWindowVisible(lv); } }
        public static void ToggleIcons() { var v = DefView(); if (v != IntPtr.Zero) SendMessage(v, 0x111, new IntPtr(0x7402), IntPtr.Zero); } // команда «Показать значки рабочего стола»
        public static bool TryToggleDesktop()
        {
            object shell = null;
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type == null) return false;
                shell = Activator.CreateInstance(type);
                shell.GetType().InvokeMember("ToggleDesktop", BindingFlags.InvokeMethod, null, shell, null);
                return true;
            }
            catch (Exception ex) { Bar.Log(ex); return false; }
            finally { if (shell != null) Marshal.ReleaseComObject(shell); }
        }
        public static bool Awake;
        public static void SetAwake(bool on) { Awake = on; SetThreadExecutionState(on ? 0x80000003u : 0x80000000u); } // система и экран не засыпают, пока включено
    }

    // Яркость встроенного экрана (ноутбук) через WMI; внешние мониторы Windows так не регулирует — тогда -1
    static class Bright
    {
        public static int Get()
        {
            try { using (var q = new System.Management.ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness")) foreach (System.Management.ManagementObject o in q.Get()) return Convert.ToInt32(o["CurrentBrightness"]); }
            catch { }
            return -1;
        }
        public static void Set(int v)
        {
            try { using (var q = new System.Management.ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods")) foreach (System.Management.ManagementObject o in q.Get()) o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)Math.Max(0, Math.Min(100, v)) }); }
            catch { }
        }
    }

    static class AsyncExt
    {
        public static TaskAwaiter GetAwaiter(this WF.IAsyncAction op)
        {
            var t = new TaskCompletionSource<bool>();
            op.Completed = (o, s) => { if (s == WF.AsyncStatus.Completed) t.TrySetResult(true); else t.TrySetException(o.ErrorCode ?? new Exception(s.ToString())); };
            return ((Task)t.Task).GetAwaiter();
        }
        public static TaskAwaiter<T> GetAwaiter<T>(this WF.IAsyncOperation<T> op)
        {
            var t = new TaskCompletionSource<T>();
            op.Completed = (o, s) =>
            {
                if (s == WF.AsyncStatus.Completed) t.TrySetResult(o.GetResults());
                else t.TrySetException(o.ErrorCode ?? new Exception(s.ToString()));
            };
            return t.Task.GetAwaiter();
        }
    }

    class Win { public IntPtr H; public uint Pid; public string Path; public IntPtr Mon; }
    class Todo { public bool Done; public string Text; public DateTime? Due; }
    // Токены стиля: фон панели, текст, акцент (индикаторы, шкалы, «сегодня») и режим размытия (0 — нет, 3 — blur, 4 — акрил)
    class Sty
    {
        public Color Bg, Fg, Ac; public int Blur;
        public Sty(string bg, string fg, string ac, int blur)
        {
            Bg = (Color)ColorConverter.ConvertFromString(bg); Fg = (Color)ColorConverter.ConvertFromString(fg); Ac = (Color)ColorConverter.ConvertFromString(ac); Blur = blur;
        }
        public static double Lum(Color c) { return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B; }
        public bool LightText { get { return Lum(Fg) > 128; } }
    }

    // Экранное время за день: секунды по приложениям и по часам. Файл: h|час|сек, a|сек|имя
    class ScreenTime
    {
        public sealed class Interval { public int Start, End; public string App; }
        public string Day; public Dictionary<string, int> Apps = new Dictionary<string, int>(); public int[] Hours = new int[24];
        public readonly List<Interval> Intervals = new List<Interval>();
        public int Total { get { return Hours.Sum(); } }
        public void Add(string app, int hour) { int v; Apps.TryGetValue(app, out v); Apps[app] = v + 1; Hours[hour]++; }
        public void Track(string app, DateTime when)
        {
            Add(app, when.Hour);
            int second = (int)when.TimeOfDay.TotalSeconds;
            var last = Intervals.LastOrDefault();
            if (last != null && last.App == app && second >= last.Start && second <= last.End + 1) // +1: опрос раз в секунду иногда пропускает секунду — это не перерыв
                last.End = second + 1;
            else Intervals.Add(new Interval { Start = second, End = second + 1, App = app });
        }
        public static ScreenTime Parse(string day, IEnumerable<string> lines)
        {
            var st = new ScreenTime { Day = day };
            foreach (var l in lines)
            {
                if (l.StartsWith("i|"))
                {
                    var part = l.Split(new[] { '|' }, 4);
                    int start, end;
                    if (part.Length == 4 && int.TryParse(part[1], out start) && int.TryParse(part[2], out end) && start >= 0 && end <= 86400 && end > start)
                        st.Intervals.Add(new Interval { Start = start, End = end, App = part[3] });
                    continue;
                }
                var p = l.Split(new[] { '|' }, 3); int a, b;
                if (p.Length < 3 || !int.TryParse(p[1], out a)) continue;
                if (p[0] == "h" && int.TryParse(p[2], out b) && a >= 0 && a < 24) st.Hours[a] = b;
                else if (p[0] == "a") st.Apps[p[2]] = a;
            }
            return st;
        }
        public IEnumerable<string> Lines()
        {
            for (int h = 0; h < 24; h++) if (Hours[h] > 0) yield return "h|" + h + "|" + Hours[h];
            foreach (var kv in Apps) yield return "a|" + kv.Value + "|" + kv.Key;
            foreach (var slot in Intervals) yield return "i|" + slot.Start + "|" + slot.End + "|" + slot.App;
        }
        public static string Fmt(int s) { int h = s / 3600, m = s / 60 % 60; return h > 0 ? h + "ч " + m + "м" : m + "м"; }
        static string FileOf(string day) { return System.IO.Path.Combine(Bar.Dir, "screentime", day + ".txt"); }
        public static ScreenTime Load(string day) { var f = FileOf(day); return Parse(day, SafeFile.ReadLines(f)); }
        public void Save()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FileOf(Day))); SafeFile.WriteLines(FileOf(Day), Lines());
            if (pathsDirty) { pathsDirty = false; SafeFile.WriteLines(PathsFile, Paths.Select(kv => kv.Key + "|" + kv.Value)); }
        }
        // название приложения → путь к его файлу (для значков в отчёте); подсчёт по-прежнему по названию
        static string PathsFile { get { return System.IO.Path.Combine(Bar.Dir, "screentime", "apps.txt"); } }
        static Dictionary<string, string> paths; static bool pathsDirty;
        public static Dictionary<string, string> Paths
        {
            get
            {
                if (paths == null) { paths = new Dictionary<string, string>(); foreach (var l in SafeFile.ReadLines(PathsFile)) { int i = l.IndexOf('|'); if (i > 0) paths[l.Substring(0, i)] = l.Substring(i + 1); } }
                return paths;
            }
        }
        public static void Seen(string name, string path) { if (name != null && path != null && !Paths.ContainsKey(name)) { Paths[name] = path; pathsDirty = true; } }
    }

    class City { public string Name, Label, Lat, Lon; }

    // Погода от Open-Meteo (бесплатно, без ключа). Время в ответе — местное время выбранного города.
    class Weather
    {
        public DateTime Now, Sunrise, Sunset, Updated;
        public double Temp, Wind; public int Code; public bool Day;
        public DateTime[] HTime; public double[] HTemp; public int[] HCode, HProb; public int HStart;
        public DateTime[] DDate; public double[] DMax, DMin, DSum; public int[] DCode, DProb;

        public const string Fields = "&current=temperature_2m,weather_code,wind_speed_10m,is_day" +
            "&hourly=temperature_2m,weather_code,precipitation_probability" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,precipitation_sum,sunrise,sunset" +
            "&timezone=auto&forecast_days=5&wind_speed_unit=ms";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static double Num(object o) { return o == null ? 0 : Convert.ToDouble(o, Inv); }
        static object[] Arr(object o) { return o as object[] ?? new object[0]; }
        static DateTime Time(object o) { var s = (string)o; return DateTime.ParseExact(s, s.Length > 10 ? "yyyy-MM-dd'T'HH:mm" : "yyyy-MM-dd", Inv); }
        static Dictionary<string, object> D(object o) { return (Dictionary<string, object>)o; }

        public static Weather Parse(string json)
        {
            var root = D(new JavaScriptSerializer().DeserializeObject(json));
            var cur = D(root["current"]); var hr = D(root["hourly"]); var dy = D(root["daily"]);
            var w = new Weather
            {
                Now = Time(cur["time"]), Temp = Num(cur["temperature_2m"]), Code = (int)Num(cur["weather_code"]),
                Wind = Num(cur["wind_speed_10m"]), Day = Num(cur["is_day"]) == 1,
                HTime = Arr(hr["time"]).Select(Time).ToArray(), HTemp = Arr(hr["temperature_2m"]).Select(Num).ToArray(),
                HCode = Arr(hr["weather_code"]).Select(x => (int)Num(x)).ToArray(), HProb = Arr(hr["precipitation_probability"]).Select(x => (int)Num(x)).ToArray(),
                DDate = Arr(dy["time"]).Select(Time).ToArray(), DCode = Arr(dy["weather_code"]).Select(x => (int)Num(x)).ToArray(),
                DMax = Arr(dy["temperature_2m_max"]).Select(Num).ToArray(), DMin = Arr(dy["temperature_2m_min"]).Select(Num).ToArray(),
                DProb = Arr(dy["precipitation_probability_max"]).Select(x => (int)Num(x)).ToArray(), DSum = Arr(dy["precipitation_sum"]).Select(Num).ToArray(),
                Sunrise = Time(Arr(dy["sunrise"])[0]), Sunset = Time(Arr(dy["sunset"])[0]), Updated = DateTime.Now /* при чтении кэша вызывающий ставит время файла */
            };
            w.HStart = Math.Max(0, Array.FindIndex(w.HTime, t => t > w.Now)); // ближайшие часы начинаются со следующего
            return w;
        }

        public static List<City> ParseCities(string json)
        {
            var root = D(new JavaScriptSerializer().DeserializeObject(json));
            object res; if (!root.TryGetValue("results", out res)) return new List<City>();
            return Arr(res).Select(o =>
            {
                var c = D(o); object a, k;
                c.TryGetValue("admin1", out a); c.TryGetValue("country", out k);
                var name = (string)c["name"];
                return new City
                {
                    Name = name, Label = string.Join(", ", new[] { name, a as string, k as string }.Where(x => !string.IsNullOrEmpty(x))),
                    Lat = Num(c["latitude"]).ToString(Inv), Lon = Num(c["longitude"]).ToString(Inv)
                };
            }).ToList();
        }

        public static string Deg(double t) { return (int)Math.Round(t, MidpointRounding.AwayFromZero) + "°"; }

        // Коды погоды WMO → вид значка (см. Bar.WxIcon) и описание
        public static Tuple<string, string> Wmo(int c, bool day)
        {
            if (c == 0) return Tuple.Create(day ? "sun" : "moon", "Ясно");
            if (c == 1) return Tuple.Create(day ? "partly" : "partly-night", "Преимущественно ясно");
            if (c == 2) return Tuple.Create(day ? "partly" : "partly-night", "Переменная облачность");
            if (c == 3) return Tuple.Create("cloud", "Пасмурно");
            if (c == 45 || c == 48) return Tuple.Create("fog", "Туман");
            if (c >= 51 && c <= 57) return Tuple.Create("drizzle", c >= 56 ? "Ледяная морось" : "Морось");
            if (c >= 61 && c <= 67) return Tuple.Create("rain", c >= 66 ? "Ледяной дождь" : c == 61 ? "Небольшой дождь" : c == 63 ? "Дождь" : "Сильный дождь");
            if (c >= 71 && c <= 77) return Tuple.Create("snow", c == 71 ? "Небольшой снег" : c == 75 ? "Сильный снег" : "Снег");
            if (c >= 80 && c <= 82) return Tuple.Create("rain", "Ливень");
            if (c == 85 || c == 86) return Tuple.Create("snow", "Снегопад");
            if (c >= 95) return Tuple.Create("storm", c == 95 ? "Гроза" : "Гроза с градом");
            return Tuple.Create("cloud", "Облачно");
        }
    }

    // Запись без потери данных: сначала во временный файл (со сбросом на диск), затем атомарная замена.
    // Предыдущая версия остаётся в .bak — из неё читаем, если основной файл пропал или не открывается.
    static class SafeFile
    {
        public static void Write(string path, string text)
        {
            var tmp = path + ".tmp"; var bytes = new UTF8Encoding(false).GetBytes(text);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None)) { fs.Write(bytes, 0, bytes.Length); fs.Flush(true); }
            if (!File.Exists(path)) { File.Move(tmp, path); return; }
            try { File.Replace(tmp, path, path + ".bak", true); }
            catch (IOException) { File.Copy(path, path + ".bak", true); File.Copy(tmp, path, true); File.Delete(tmp); } // диск без поддержки Replace
        }
        public static void WriteLines(string path, IEnumerable<string> lines) { Write(path, string.Concat(lines.Select(l => l + "\r\n"))); }
        public static string Read(string path)
        {
            foreach (var p in new[] { path, path + ".bak" })
                try { if (File.Exists(p)) return File.ReadAllText(p); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return null;
        }
        public static string[] ReadLines(string path) { var t = Read(path); return t == null ? new string[0] : t.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim() != "").ToArray(); }
    }

    // Плитка/кнопка, которая ведёт себя как настоящая кнопка: фокус с клавиатуры (Tab, стрелки), Enter/Пробел — нажатие,
    // Shift+F10 или клавиша меню — контекстное меню; для экранного диктора это «кнопка» с действием «Вызвать».
    class Press : Border
    {
        public static Brush Ring = Brushes.DodgerBlue;
        public Action Invoke; public Action<Press> Menu; public bool ShellKey; // кнопка сама открывает и закрывает окно Windows (Пуск, поиск, уведомления)
        public Color Base; // подложка в покое — к ней возвращается подсветка наведения
        Brush savedBrush; Thickness savedThick;
        public Press() { Focusable = true; FocusVisualStyle = null; }
        public Press Shell() { ShellKey = true; return this; }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if ((e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Space) && Invoke != null) { Invoke(); e.Handled = true; }
            else if ((e.Key == System.Windows.Input.Key.Apps || (e.SystemKey == System.Windows.Input.Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift)) && Menu != null) { Menu(this); e.Handled = true; }
            base.OnKeyDown(e);
        }
        // рамка фокуса — только при переходе с клавиатуры (мышью такие элементы фокус не получают)
        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { savedBrush = BorderBrush; savedThick = BorderThickness; BorderBrush = Ring; BorderThickness = new Thickness(2); base.OnGotKeyboardFocus(e); }
        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { BorderBrush = savedBrush; BorderThickness = savedThick; base.OnLostKeyboardFocus(e); }
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() { return new PressPeer(this); }
    }
    class PressPeer : System.Windows.Automation.Peers.FrameworkElementAutomationPeer, System.Windows.Automation.Provider.IInvokeProvider
    {
        public PressPeer(Press p) : base(p) { }
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() { return System.Windows.Automation.Peers.AutomationControlType.Button; }
        protected override string GetClassNameCore() { return "FlowBarButton"; }
        protected override bool IsKeyboardFocusableCore() { return true; }
        public override object GetPattern(System.Windows.Automation.Peers.PatternInterface p) { return p == System.Windows.Automation.Peers.PatternInterface.Invoke ? this : base.GetPattern(p); }
        public void Invoke() { var o = (Press)Owner; if (o.Invoke != null) o.Invoke(); }
    }

    class CalEvent { public string Title, Location, Cal; public DateTime Start, End; public bool AllDay; }

    // Разбор iCalendar (RFC 5545): события, RRULE (DAILY/WEEKLY/MONTHLY/YEARLY, INTERVAL, COUNT, UNTIL, BYDAY, BYMONTHDAY),
    // EXDATE, перенесённые повторы (RECURRENCE-ID), часовые пояса Windows и IANA.
    static class Ics
    {
        class Prop
        {
            public string Name, Value; public Dictionary<string, string> Params = new Dictionary<string, string>();
            public static Prop Parse(string l)
            {
                bool q = false; int i = 0;
                for (; i < l.Length; i++) { if (l[i] == '"') q = !q; else if (l[i] == ':' && !q) break; }
                if (i >= l.Length) return null;
                var head = l.Substring(0, i).Split(';');
                var p = new Prop { Name = head[0].ToUpperInvariant(), Value = l.Substring(i + 1) };
                foreach (var kv in head.Skip(1)) { int e = kv.IndexOf('='); if (e > 0) p.Params[kv.Substring(0, e).ToUpperInvariant()] = kv.Substring(e + 1).Trim('"'); }
                return p;
            }
        }
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Dictionary<string, TimeZoneInfo> tzCache = new Dictionary<string, TimeZoneInfo>();
        [DllImport("icu.dll", CharSet = CharSet.Unicode)] static extern int ucal_getWindowsTimeZoneID(string id, int len, StringBuilder winid, int cap, ref int status);

        public static TimeZoneInfo Zone(string tzid, Dictionary<string, TimeSpan> fixedTz)
        {
            TimeZoneInfo z;
            lock (tzCache) if (tzCache.TryGetValue(tzid, out z)) return z;
            try { z = TimeZoneInfo.FindSystemTimeZoneById(tzid); } catch { }             // Outlook: «Russian Standard Time»
            if (z == null) try                                                             // Google/iCloud: «Europe/Moscow» → через ICU Windows
                {
                    var sb = new StringBuilder(128); int st = 0; int n = ucal_getWindowsTimeZoneID(tzid, tzid.Length, sb, 128, ref st);
                    if (n > 0 && st <= 0) z = TimeZoneInfo.FindSystemTimeZoneById(sb.ToString(0, n));
                }
                catch { }
            if (z != null) { lock (tzCache) tzCache[tzid] = z; return z; }
            TimeSpan off; // shortcut: неизвестный пояс берём со стандартным смещением из VTIMEZONE, без перехода на летнее время
            return fixedTz.TryGetValue(tzid, out off) ? TimeZoneInfo.CreateCustomTimeZone(tzid, off, tzid, tzid) : null;
        }
        static TimeSpan Offset(string v)
        {
            int sign = v.StartsWith("-") ? -1 : 1; v = v.TrimStart('+', '-');
            return v.Length >= 4 ? TimeSpan.FromMinutes(sign * (int.Parse(v.Substring(0, 2)) * 60 + int.Parse(v.Substring(2, 2)))) : TimeSpan.Zero;
        }
        static string Unescape(string s)
        {
            return Regex.Replace(s, @"\\(.)", m => m.Groups[1].Value == "n" || m.Groups[1].Value == "N" ? "\n" : m.Groups[1].Value);
        }
        public static TimeSpan Duration(string v)
        {
            var m = Regex.Match(v, @"^([+-])?P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$");
            if (!m.Success) return TimeSpan.Zero;
            Func<int, int> g = i => m.Groups[i].Success ? int.Parse(m.Groups[i].Value) : 0;
            var t = new TimeSpan(g(2) * 7 + g(3), g(4), g(5), g(6));
            return m.Groups[1].Value == "-" ? -t : t;
        }
        // Время «по часам» в поясе события; перевод в местное — отдельно, чтобы повторы считались по часам события
        static DateTime Wall(Prop p, string v, Dictionary<string, TimeSpan> fixedTz, out bool allDay, out TimeZoneInfo zone)
        {
            v = v.Trim(); zone = null; string pv;
            allDay = v.Length == 8 || (p.Params.TryGetValue("VALUE", out pv) && pv == "DATE");
            if (allDay) return DateTime.ParseExact(v.Substring(0, 8), "yyyyMMdd", Inv);
            var t = DateTime.ParseExact(v.TrimEnd('Z').Substring(0, 15), "yyyyMMdd'T'HHmmss", Inv);
            string tz;
            if (v.EndsWith("Z")) zone = TimeZoneInfo.Utc;
            else if (p.Params.TryGetValue("TZID", out tz)) zone = Zone(tz, fixedTz);
            return t;
        }
        static DateTime ToLocal(DateTime wall, TimeZoneInfo zone)
        {
            if (zone == null) return wall; // «плавающее» время — как есть
            try { return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(wall, DateTimeKind.Unspecified), zone, TimeZoneInfo.Local); }
            catch { return wall; } // несуществующее время при переводе часов
        }
        static int DayIdx(string d) { return Array.IndexOf(new[] { "MO", "TU", "WE", "TH", "FR", "SA", "SU" }, d); }
        static int Mon(DateTime d) { return ((int)d.DayOfWeek + 6) % 7; }
        static IEnumerable<DateTime> NthWeekdays(DateTime month, string spec)
        {
            int wd = DayIdx(spec.Substring(spec.Length - 2)); var num = spec.Substring(0, spec.Length - 2);
            var all = Enumerable.Range(0, DateTime.DaysInMonth(month.Year, month.Month)).Select(i => month.AddDays(i)).Where(d => Mon(d) == wd).ToList();
            if (num == "" || num == "+") return all;
            int n = int.Parse(num); int idx = n > 0 ? n - 1 : all.Count + n;
            return idx >= 0 && idx < all.Count ? new[] { all[idx] } : new DateTime[0];
        }

        public static IEnumerable<DateTime> Occurrences(DateTime start, Dictionary<string, string> r, DateTime? until, DateTime from, DateTime limit)
        {
            string freq; r.TryGetValue("FREQ", out freq);
            string s; int interval = r.TryGetValue("INTERVAL", out s) ? Math.Max(1, int.Parse(s)) : 1;
            int count = r.TryGetValue("COUNT", out s) ? int.Parse(s) : int.MaxValue;
            var byday = r.TryGetValue("BYDAY", out s) ? s.Split(',').Where(x => x.Length >= 2).ToList() : new List<string>();
            var bymd = r.TryGetValue("BYMONTHDAY", out s) ? s.Split(',').Select(int.Parse).ToList() : new List<int>();
            if (freq != "DAILY" && freq != "WEEKLY" && freq != "MONTHLY" && freq != "YEARLY") { yield return start; yield break; }
            // без COUNT можно сразу перескочить к окну просмотра
            int k0 = 0;
            if (count == int.MaxValue && from > start)
            {
                double span = freq == "DAILY" ? (from - start).TotalDays : freq == "WEEKLY" ? (from - start).TotalDays / 7 :
                    freq == "MONTHLY" ? (from.Year - start.Year) * 12 + from.Month - start.Month : from.Year - start.Year;
                k0 = Math.Max(0, (int)(span / interval) - 1);
            }
            int n = 0; var tod = start.TimeOfDay;
            for (int k = k0; k < k0 + 5000; k++)
            {
                var c = new List<DateTime>();
                if (freq == "DAILY")
                {
                    // BYDAY/BYMONTHDAY у DAILY — фильтры (например, «каждый будний день»)
                    var d = start.AddDays((double)k * interval);
                    if ((byday.Count == 0 || byday.Any(b => DayIdx(b.Substring(b.Length - 2)) == Mon(d))) && (bymd.Count == 0 || bymd.Contains(d.Day))) c.Add(d);
                }
                else if (freq == "WEEKLY")
                {
                    var ws = start.Date.AddDays(-Mon(start)).AddDays(7.0 * k * interval);
                    foreach (var d in byday.Count > 0 ? byday.Select(x => DayIdx(x.Substring(x.Length - 2))) : new[] { Mon(start) }) if (d >= 0) c.Add(ws.AddDays(d) + tod);
                }
                else if (freq == "MONTHLY")
                {
                    var m = new DateTime(start.Year, start.Month, 1).AddMonths(k * interval); int dim = DateTime.DaysInMonth(m.Year, m.Month);
                    if (bymd.Count > 0) { foreach (var d in bymd) { int dd = d > 0 ? d : dim + d + 1; if (dd >= 1 && dd <= dim) c.Add(m.AddDays(dd - 1) + tod); } }
                    else if (byday.Count > 0) { foreach (var b in byday) c.AddRange(NthWeekdays(m, b).Select(x => x + tod)); }
                    else if (start.Day <= dim) c.Add(m.AddDays(start.Day - 1) + tod);
                }
                else
                {
                    int y = start.Year + k * interval;
                    if (start.Month != 2 || start.Day != 29 || DateTime.IsLeapYear(y)) c.Add(new DateTime(y, start.Month, start.Day) + tod);
                }
                foreach (var x in c.OrderBy(x => x))
                {
                    if (x < start) continue;
                    if ((until != null && x > until.Value) || x > limit || ++n > count) yield break;
                    yield return x;
                }
            }
        }

        public static List<CalEvent> Parse(string text, DateTime from, DateTime to, out string name)
        {
            name = null;
            var lines = Regex.Replace(text, @"\r?\n[ \t]", "").Split('\n').Select(l => l.TrimEnd('\r'));
            var fixedTz = new Dictionary<string, TimeSpan>(); string curTz = null; bool inStd = false;
            var events = new List<List<Prop>>(); List<Prop> cur = null; int sub = 0;
            foreach (var raw in lines)
            {
                var p = Prop.Parse(raw); if (p == null) continue;
                if (p.Name == "BEGIN")
                {
                    if (cur != null) sub++; // VALARM и т.п. внутри события пропускаем
                    else if (p.Value == "VEVENT") cur = new List<Prop>();
                    else if (p.Value == "STANDARD") inStd = true;
                    continue;
                }
                if (p.Name == "END")
                {
                    if (cur != null) { if (sub > 0) sub--; else if (p.Value == "VEVENT") { events.Add(cur); cur = null; } }
                    else if (p.Value == "STANDARD") inStd = false;
                    continue;
                }
                if (cur != null) { if (sub == 0) cur.Add(p); continue; }
                if (p.Name == "X-WR-CALNAME") name = Unescape(p.Value);
                else if (p.Name == "TZID") curTz = p.Value;
                else if (p.Name == "TZOFFSETTO" && inStd && curTz != null) fixedTz[curTz] = Offset(p.Value);
            }

            Func<List<Prop>, string, Prop> one = (ps, n) => ps.FirstOrDefault(x => x.Name == n);
            Func<List<Prop>, string, string> val = (ps, n) => { var x = one(ps, n); return x == null ? null : x.Value; };
            Func<Prop, DateTime> local = p => { bool a; TimeZoneInfo z; var w = Wall(p, p.Value, fixedTz, out a, out z); return ToLocal(w, z); };

            var overridden = new HashSet<string>();
            foreach (var ev in events) { var rid = one(ev, "RECURRENCE-ID"); if (rid != null) try { overridden.Add(val(ev, "UID") + "|" + local(rid).ToString("s")); } catch { } }

            var result = new List<CalEvent>();
            foreach (var ev in events)
            {
                try
                {
                    if (val(ev, "STATUS") == "CANCELLED") continue;
                    var ds = one(ev, "DTSTART"); if (ds == null) continue;
                    bool allDay; TimeZoneInfo zone; var sWall = Wall(ds, ds.Value, fixedTz, out allDay, out zone);
                    var de = one(ev, "DTEND"); var du = val(ev, "DURATION"); TimeSpan len;
                    if (de != null) { bool a2; TimeZoneInfo z2; var eWall = Wall(de, de.Value, fixedTz, out a2, out z2); len = allDay ? eWall - sWall : ToLocal(eWall, z2) - ToLocal(sWall, zone); }
                    else len = du != null ? Duration(du) : allDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;

                    string uid = val(ev, "UID"), rr = val(ev, "RRULE"); bool isOverride = one(ev, "RECURRENCE-ID") != null;
                    IEnumerable<DateTime> occ = new[] { sWall };
                    if (rr != null && !isOverride)
                    {
                        var rule = rr.Split(';').Select(x => x.Split('=')).Where(x => x.Length == 2).ToDictionary(x => x[0].ToUpperInvariant(), x => x[1]);
                        DateTime? until = null; string u;
                        if (rule.TryGetValue("UNTIL", out u))
                        {
                            if (u.Length == 8) until = DateTime.ParseExact(u, "yyyyMMdd", Inv).AddDays(1).AddTicks(-1);
                            else
                            {
                                var ut = DateTime.ParseExact(u.TrimEnd('Z').Substring(0, 15), "yyyyMMdd'T'HHmmss", Inv);
                                until = !u.EndsWith("Z") ? ut : zone != null && zone != TimeZoneInfo.Utc ? TimeZoneInfo.ConvertTimeFromUtc(ut, zone) : zone == TimeZoneInfo.Utc ? ut : DateTime.SpecifyKind(ut, DateTimeKind.Utc).ToLocalTime();
                            }
                        }
                        occ = Occurrences(sWall, rule, until, from.AddDays(-2), to.AddDays(2));
                    }
                    // RDATE — дополнительные даты повторов (RFC 5545 3.8.5.2); у PERIOD берём начало
                    if (!isOverride)
                    {
                        var extra = new List<DateTime>();
                        foreach (var rd in ev.Where(x => x.Name == "RDATE"))
                            foreach (var v in rd.Value.Split(',')) { bool a; TimeZoneInfo z; extra.Add(Wall(rd, v.Split('/')[0], fixedTz, out a, out z)); }
                        if (extra.Count > 0) occ = occ.Concat(extra).Distinct().OrderBy(x => x).ToList();
                    }
                    var ex = new HashSet<DateTime>();
                    foreach (var e in ev.Where(x => x.Name == "EXDATE"))
                        foreach (var v in e.Value.Split(',')) { bool a; TimeZoneInfo z; var w = Wall(e, v, fixedTz, out a, out z); ex.Add(allDay ? w : ToLocal(w, z)); }

                    string title = val(ev, "SUMMARY"), loc = val(ev, "LOCATION");
                    foreach (var w in occ)
                    {
                        var st = allDay ? w : ToLocal(w, zone);
                        if (ex.Contains(st) || (!isOverride && rr != null && overridden.Contains(uid + "|" + st.ToString("s")))) continue;
                        var en = st + len;
                        if (en < from || st > to) continue;
                        result.Add(new CalEvent { Title = title == null ? "(без названия)" : Unescape(title), Location = loc == null ? null : Unescape(loc), Start = st, End = en, AllDay = allDay });
                    }
                }
                catch { } // повреждённое событие пропускаем, остальные показываем
            }
            return result;
        }
    }

    // Использование Claude Code по его журналам (~/.claude/projects/**/*.jsonl). Процентов лимита там нет — только токены.
    class ClaudeUsage
    {
        public class Msg { public DateTime T; public string Model; public long In, Out, CacheW, CacheR; public long Weight { get { return In + Out + CacheW; } } }
        public List<Msg> Msgs = new List<Msg>();
        readonly Dictionary<string, long> offsets = new Dictionary<string, long>();
        readonly HashSet<string> seen = new HashSet<string>();
        public static readonly string Root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

        static string Match(string s, string rx) { var m = Regex.Match(s, rx); return m.Success ? m.Groups[1].Value : ""; }
        static long Num(string s, string key) { long v; long.TryParse(Match(s, "\"" + key + "\":(\\d+)"), out v); return v; }

        // В сырой строке JSON кавычки внутри текста экранированы, поэтому неэкранированные ключи — настоящие поля
        public static Msg ParseLine(string line, out string id)
        {
            id = "";
            if (line.IndexOf("\"type\":\"assistant\"", StringComparison.Ordinal) < 0) return null;
            int u = line.IndexOf("\"usage\":{", StringComparison.Ordinal);
            DateTime t;
            if (u < 0 || !DateTime.TryParse(Match(line, "\"timestamp\":\"([^\"]+)\""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t)) return null;
            var model = Match(line, "\"model\":\"([^\"]+)\"");
            if (model.StartsWith("<")) return null; // <synthetic> — служебные записи
            var rest = line.Substring(u);
            id = Match(line, "\"id\":\"(msg_[^\"]+)\"") + Match(line, "\"requestId\":\"([^\"]+)\"");
            return new Msg { T = t.ToLocalTime(), Model = model, In = Num(rest, "input_tokens"), Out = Num(rest, "output_tokens"), CacheW = Num(rest, "cache_creation_input_tokens"), CacheR = Num(rest, "cache_read_input_tokens") };
        }

        // Читает только дописанное с прошлого раза. Вызывается в фоне, результат добавляется в UI-потоке через Add.
        public List<Msg> Scan()
        {
            var found = new List<Msg>();
            if (!Directory.Exists(Root)) return found;
            var since = DateTime.Now.AddDays(-8);
            foreach (var f in new DirectoryInfo(Root).EnumerateFiles("*.jsonl", SearchOption.AllDirectories))
            {
                if (f.LastWriteTime < since) continue;
                long off; offsets.TryGetValue(f.FullName, out off);
                if (f.Length < off) off = 0;
                if (f.Length == off) continue;
                using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    fs.Seek(off, SeekOrigin.Begin);
                    var buf = new byte[1 << 20]; var line = new MemoryStream(); long committed = off; int n;
                    while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                    {
                        long chunkPos = fs.Position - n; int start = 0;
                        for (int i = 0; i < n; i++)
                        {
                            if (buf[i] != 10) continue;
                            line.Write(buf, start, i - start); start = i + 1; committed = chunkPos + i + 1;
                            string id; var m = ParseLine(Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length), out id);
                            line.SetLength(0);
                            if (m != null && m.T >= since && (id == "" || seen.Add(id))) found.Add(m);
                        }
                        line.Write(buf, start, n - start);
                    }
                    offsets[f.FullName] = committed; // недописанная последняя строка будет прочитана в следующий раз
                }
            }
            return found;
        }
        public void Add(List<Msg> found)
        {
            Msgs.AddRange(found);
            var since = DateTime.Now.AddDays(-8);
            Msgs.RemoveAll(m => m.T < since);
            Msgs.Sort((a, b) => a.T.CompareTo(b.T));
        }

        // 5-часовое окно начинается с первого сообщения (округлённого до часа) после окончания предыдущего
        public static DateTime? BlockStart(IEnumerable<Msg> msgs, DateTime now)
        {
            DateTime? start = null; var end = DateTime.MinValue;
            foreach (var m in msgs.OrderBy(x => x.T))
                if (m.T >= end) { start = new DateTime(m.T.Year, m.T.Month, m.T.Day, m.T.Hour, 0, 0); end = start.Value.AddHours(5); }
            return start != null && now < end ? start : null;
        }
        public static string ModelName(string model)
        {
            var p = model.Replace("claude-", "").Split('-');
            var ver = string.Join(".", p.Skip(1).TakeWhile(x => x.Length <= 2));
            return p[0].Length == 0 ? model : char.ToUpper(p[0][0]) + p[0].Substring(1) + (ver == "" ? "" : " " + ver);
        }
    }

    // Проценты лимитов Claude. Claude Code сообщает их только строке состояния (rate_limits: пятичасовое и недельное окна —
    // для подписки Pro/Max, после первого ответа в сессии; там же context_window — заполнение контекста сессии).
    // Мост — скрипт строки состояния: сохраняет присланное в файл и вызывает прежнюю строку состояния, её вывод не меняется.
    // Подключается и отключается только кнопкой в FlowBar.
    class ClaudeBridge
    {
        public double P5 = double.NaN, PW = double.NaN, Ctx = double.NaN; public DateTime R5, RW, Seen;
        public static readonly string SettingsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");
        public static string Script { get { return System.IO.Path.Combine(Bar.Dir, "claude-statusline.ps1"); } }
        public static string DataPath { get { return System.IO.Path.Combine(Bar.Dir, "claude-limits.json"); } }
        static double Num(object o) { if (o == null) return double.NaN; try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return double.NaN; } }
        static Dictionary<string, object> Obj(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o as Dictionary<string, object> : null; }
        public static ClaudeBridge Parse(string json, DateTime seen)
        {
            var o = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>; if (o == null) return null;
            var c = new ClaudeBridge { Seen = seen }; var rl = Obj(o, "rate_limits");
            Action<string, Action<double, DateTime>> win = (k, set) =>
            {
                var w = Obj(rl, k); if (w == null) return; object p, r; w.TryGetValue("used_percentage", out p); w.TryGetValue("resets_at", out r);
                double rs = Num(r); set(Num(p), double.IsNaN(rs) ? DateTime.MaxValue : DateTimeOffset.FromUnixTimeSeconds((long)rs).LocalDateTime);
            };
            win("five_hour", (p, r) => { c.P5 = p; c.R5 = r; }); win("seven_day", (p, r) => { c.PW = p; c.RW = r; });
            var cw = Obj(o, "context_window"); object u; if (cw != null && cw.TryGetValue("used_percentage", out u)) c.Ctx = Num(u);
            return c;
        }
        public static ClaudeBridge Read() { try { var f = new FileInfo(DataPath); return f.Exists ? Parse(File.ReadAllText(f.FullName), f.LastWriteTime) : null; } catch { return null; } }
        // процент окна, пока оно не сброшено; после сброса — неизвестно до следующего ответа Claude Code
        public static double Eff(double p, DateTime reset, DateTime now) { return now >= reset ? double.NaN : p; }
        public static string Left(double p, DateTime reset, DateTime now) { return reset == DateTime.MaxValue || reset == default(DateTime) ? "—" : now >= reset ? "ждём" : double.IsNaN(p) ? "—" : Bar.Until(reset - now); }
        // подключён ли мост: команда строки состояния в настройках Claude Code указывает на наш скрипт
        public static bool On
        {
            get
            {
                try
                {
                    if (!File.Exists(SettingsPath)) return false;
                    var sl = Obj(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(SettingsPath)) as Dictionary<string, object>, "statusLine"); object c;
                    return sl != null && sl.TryGetValue("command", out c) && c is string && ((string)c).IndexOf("claude-statusline.ps1", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                catch { return false; }
            }
        }
        static string Q(string s) { return s.Replace("'", "''"); }
        // байты из Claude Code проходят без перекодирования: в файл (если есть лимиты) и в прежнюю строку состояния
        public static string ScriptText(string prev)
        {
            var sb = new StringBuilder();
            sb.Append("# FlowBar: сохраняет проценты лимитов Claude Code для панели и вызывает прежнюю строку состояния.\r\n");
            sb.Append("# Отключить: FlowBar → Настройки → Подключения → Claude Code → «Отключить проценты».\r\n");
            sb.Append("$ms = New-Object IO.MemoryStream; [Console]::OpenStandardInput().CopyTo($ms); $b = $ms.ToArray()\r\n");
            sb.Append("if ([Text.Encoding]::UTF8.GetString($b).Contains('\"rate_limits\"')) { try { $t = '" + Q(DataPath) + ".tmp'; [IO.File]::WriteAllBytes($t, $b); Move-Item -LiteralPath $t -Destination '" + Q(DataPath) + "' -Force } catch {} }\r\n");
            sb.Append("$prev = '" + Q(prev ?? "") + "'\r\n");
            sb.Append("if ($prev) {\r\n");
            sb.Append("  $si = New-Object Diagnostics.ProcessStartInfo 'cmd.exe', ('/d /s /c \"' + $prev + '\"'); $si.UseShellExecute = $false; $si.RedirectStandardInput = $true; $si.RedirectStandardOutput = $true; $si.CreateNoWindow = $true\r\n");
            sb.Append("  $p = [Diagnostics.Process]::Start($si); $p.StandardInput.BaseStream.Write($b, 0, $b.Length); $p.StandardInput.Close()\r\n");
            sb.Append("  $o = [Console]::OpenStandardOutput(); $p.StandardOutput.BaseStream.CopyTo($o); $o.Flush(); $p.WaitForExit()\r\n");
            sb.Append("}\r\n");
            return sb.ToString();
        }
    }

    // Лимиты Codex из его журналов (~/.codex/sessions): Codex CLI сам пишет туда проценты окон
    class CodexLimits
    {
        public double P5, PW; public DateTime R5, RW, Seen; public string Plan; public bool Has5, HasW;
        public static readonly string Root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");
        // NaN — окна нет в записи или оно уже сброшено, а новой записи ещё нет: показываем «—», а не 0%
        public double Eff5(DateTime now) { return !Has5 || now >= R5 ? double.NaN : P5; }
        public double EffW(DateTime now) { return !HasW || now >= RW ? double.NaN : PW; }
        public bool Expired5(DateTime now) { return Has5 && now >= R5; }
        public bool ExpiredW(DateTime now) { return HasW && now >= RW; }
        public static string Pct(double v) { return double.IsNaN(v) ? "—" : Math.Round(v) + "%"; }

        static DateTime Unix(object o) { return DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(o, CultureInfo.InvariantCulture)).LocalDateTime; }
        public static CodexLimits ParseLine(string line)
        {
            var o = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(line);
            var pl = o["payload"] as Dictionary<string, object>; object rlo;
            if (pl == null || !pl.TryGetValue("rate_limits", out rlo)) return null;
            var rl = rlo as Dictionary<string, object>; if (rl == null) return null;
            var p = rl.ContainsKey("primary") ? rl["primary"] as Dictionary<string, object> : null;
            var s = rl.ContainsKey("secondary") ? rl["secondary"] as Dictionary<string, object> : null;
            if (p == null && s == null) return null;
            object plan; rl.TryGetValue("plan_type", out plan);
            DateTime seen; DateTime.TryParse(o.ContainsKey("timestamp") ? o["timestamp"] as string : null, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out seen);
            var c = new CodexLimits { Plan = plan as string, Seen = seen.ToLocalTime() };
            if (p != null) { c.P5 = Convert.ToDouble(p["used_percent"], CultureInfo.InvariantCulture); c.R5 = Unix(p["resets_at"]); c.Has5 = true; }
            if (s != null) { c.PW = Convert.ToDouble(s["used_percent"], CultureInfo.InvariantCulture); c.RW = Unix(s["resets_at"]); c.HasW = true; }
            return c;
        }
        // Берём самую свежую запись по её timestamp среди последних файлов: время изменения файла не гарантирует свежесть записи
        public static CodexLimits ReadLatest(string root = null)
        {
            root = root ?? Root;
            if (!Directory.Exists(root)) return null;
            CodexLimits best = null;
            foreach (var f in new DirectoryInfo(root).EnumerateFiles("*.jsonl", SearchOption.AllDirectories).OrderByDescending(x => x.LastWriteTime).Take(10))
            {
                var c = LatestIn(f.FullName);
                if (c != null && (best == null || c.Seen > best.Seen)) best = c;
            }
            return best;
        }
        static CodexLimits LatestIn(string path) // журнал дописывается по порядку — последняя целая запись в файле самая свежая
        {
            string tail;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long len = Math.Min(fs.Length, 512 * 1024); fs.Seek(-len, SeekOrigin.End);
                var b = new byte[len]; int read = 0; while (read < len) { int n = fs.Read(b, read, (int)len - read); if (n <= 0) break; read += n; }
                tail = Encoding.UTF8.GetString(b, 0, read);
            }
            foreach (var l in tail.Split('\n').Reverse())
                if (l.Contains("\"rate_limits\""))
                    try { var c = ParseLine(l); if (c != null) return c; } catch { } // первая строка хвоста может быть обрезана
            return null;
        }
    }

    partial class Bar : Window
    {
        // --demo: пример для визуальной сверки — отдельная папка (настройки и данные пользователя не трогаются) и фиксированные данные
        public static readonly bool DemoMode = Environment.GetCommandLineArgs().Contains("--demo");
        // --selftest тоже в отдельную папку: его нарочные ошибки (файл «только для чтения») не должны попадать в настоящий журнал и отчёт
        public static readonly string Dir = (DemoMode ? Environment.GetEnvironmentVariable("FLOWBAR_DEMO_DIR") : null)
            ?? (Environment.GetCommandLineArgs().Contains("--selftest") ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FlowBar-selftest")
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), DemoMode ? "FlowBar-demo" : "FlowBar"));
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        // Базовые стили + 10 тем из Theme Factory. Пары текст/фон подобраны с контрастом ≥ 4.5:1, акцент ≥ 3:1 к фону.
        static readonly Dictionary<string, Sty> Styles = new Dictionary<string, Sty> {
            { "Бумага", new Sty("#FFF3EBDC", "#FF28221C", "#9B482B", 0) },
            { "Табло", new Sty("#FF121A17", "#FFE8F4E9", "#86D7A6", 0) },
            { "Black", new Sty("#EE121212", "#FFFFFFFF", "#FF7049", 0) },
            { "White", new Sty("#F2F7F7F7", "#FF161616", "#005FB8", 0) },
            { "Midnight", new Sty("#EE0D1426", "#FFE6ECFF", "#B39BFF", 0) },
            { "Clear", new Sty("#30000000", "#FFFFFFFF", "#FFFFFF", 0) },
            { "Liquid Glass", new Sty("#3C000000", "#FFFFFFFF", "#9FE1FF", 3) },
            { "iOS Glass", new Sty("#D0F4F4F7", "#FF111111", "#0A63D6", 4) },
            { "Frosted Dark", new Sty("#A01C1C22", "#FFFFFFFF", "#7CC4FF", 4) },
            { "Ocean Depths", new Sty("#F21A2332", "#FFF1FAEE", "#A8DADC", 0) },
            { "Midnight Galaxy", new Sty("#F22B1E3E", "#FFE6E6FA", "#A490C2", 0) },
            { "Arctic Frost", new Sty("#F2D4E4F7", "#FF1B2A41", "#4A6FA5", 0) },
            { "Tech Innovation", new Sty("#F21E1E1E", "#FFFFFFFF", "#00E5FF", 0) },
            { "Forest Canopy", new Sty("#F22D4A2B", "#FFFAF9F6", "#A4AC86", 0) },
            { "Sunset Boulevard", new Sty("#F2264653", "#FFFFFFFF", "#F4A261", 0) },
            { "Golden Hour", new Sty("#F24A403A", "#FFFFF8EE", "#F4A900", 0) },
            { "Desert Rose", new Sty("#F25D2E46", "#FFF6EAE0", "#D4A5A5", 0) },
            { "Botanical Garden", new Sty("#F2F5F3ED", "#FF1F2A22", "#3F6E4E", 0) },
            { "Modern Minimalist", new Sty("#F236454F", "#FFFFFFFF", "#D3D3D3", 0) },
        };
        static readonly string[] BaseStyles = { "Black", "White", "Midnight", "Clear", "Liquid Glass", "iOS Glass" };

        // общее для всех мониторов
        static Dictionary<string, string> cfg = new Dictionary<string, string>();
        static List<string> pinned;
        static List<Todo> todos;
        static ScreenTime screen;
        static List<Bar> bars = new List<Bar>();
        static Dictionary<uint, string> pathCache = new Dictionary<uint, string>();
        static Dictionary<string, ImageSource> iconCache = new Dictionary<string, ImageSource>();
        static Dictionary<string, string> lnkCache = new Dictionary<string, string>();
        static Dictionary<string, string> nameCache = new Dictionary<string, string>();
        static DateTime peekUntil; static bool peekWas;
        static DateTime lastDragEnd;
        static bool dragging;
        public static List<string> appOrder = new List<string>(); // порядок значков на панели (теги плиток), меняется перетаскиванием
        static int ticks;

        // свои для каждого монитора
        readonly System.Windows.Forms.Screen scr; readonly bool primary; readonly IntPtr mon;
        Border root; StartLogo startLogo;
        IntPtr hwnd; uint selfPid = (uint)Process.GetCurrentProcess().Id;
        string lastKey;
        Window panel; string panelTitle;
        Window preview; FrameworkElement previewFor; List<IntPtr> thumbs = new List<IntPtr>(); DispatcherTimer hoverT; int previewMiss;
        
        static ClaudeUsage claude = new ClaudeUsage(); static CodexLimits codex; static bool aiBusy;
        // календари: строки calendars.txt — «ссылка<TAB>название»
        static List<string> calFeeds; static List<CalEvent> calEvents = new List<CalEvent>(); static bool calBusy; static DateTime calUpdated; static string calError;
        static HashSet<string> calNotified = new HashSet<string>();

        [ThreadStatic] static Dictionary<string, string> previewCfg;
        internal static string C(string k, string d) { string v; return (previewCfg ?? cfg).TryGetValue(k, out v) ? v : d; }
        public static bool WatchOk = true; // сторож подтвердил готовность; без него панель Windows не скрываем
        static bool HideTb { get { return C("hideTaskbar", "1") == "1" && WatchOk; } }
        string MonitorSetting(string key, string fallback) { return C(key + "@" + scr.DeviceName, C(key, fallback)); }
        string PositionKey { get { return MonitorSetting("pos", "bottom"); } }
        bool IsTop { get { return PositionKey == "top"; } }
        static bool Locked { get { return C("lock", "0") == "1"; } }
        static Sty Cur { get { Sty s; return Styles.TryGetValue(C("style", "Black"), out s) ? s : Styles["Black"]; } }
        static Brush Accent { get { return Frozen(AccentColor, 255); } }
        static SolidColorBrush Frozen(Color c, byte a) { var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)); b.Freeze(); return b; }
        static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        static bool Motion { get { return SystemParameters.ClientAreaAnimation && C("motion", "soft") != "off"; } }
        // Цвет карточек: сплошные стили — свой фон; прозрачные — нейтральный тёмный/светлый под цвет текста
        static Color CardBg
        {
            get
            {
                var s = Cur;
                if (s.Blur == 0 && s.Bg.A >= 0xE0) return Color.FromRgb(s.Bg.R, s.Bg.G, s.Bg.B);
                return s.LightText ? Color.FromRgb(0x20, 0x20, 0x24) : Color.FromRgb(0xF5, 0xF5, 0xF7);
            }
        }
        static Color CardFg { get { var s = Cur; return s.Blur == 0 && s.Bg.A >= 0xE0 ? s.Fg : s.LightText ? Colors.White : Color.FromRgb(0x11, 0x11, 0x11); } }
        static Color OnAccent { get { return Sty.Lum(AccentColor) > 150 ? Color.FromRgb(0x10, 0x10, 0x10) : Colors.White; } }
        // Фигура цвета текста окна с заданной прозрачностью — разделители и дорожки шкал во всех стилях
        static T FgShape<T>(T s, double opacity) where T : Shape
        {
            FgBind(s, Shape.FillProperty);
            s.Opacity = opacity; return s;
        }
        static string Today { get { return DateTime.Now.ToString("yyyy-MM-dd"); } }
        static void All(Action<Bar> a) { foreach (var b in bars.ToList()) a(b); }

        public static void Init()
        {
            Directory.CreateDirectory(Dir);
            foreach (var l in Lines("settings.txt")) { int i = l.IndexOf('='); if (i > 0) cfg[l.Substring(0, i)] = l.Substring(i + 1); }
            LoadThemeFiles(); StartTrim(); if (!DemoMode) MigrateAutoStart();
            rememberedAppearance = SnapshotAppearance();
            LogHideSource();
            // первая установка (настроек ещё нет): только ярлыки программ, все виджеты выключены — добавляются в каталоге. --demo остаётся с виджетами для сверки вида
            // файл настроек есть, но не открылся (блокировка): это не первая установка — ничего не подставляем и не перезаписываем
            bool settingsUnreadable = cfg.Count == 0 && (File.Exists(F("settings.txt")) || File.Exists(F("settings.txt.bak")));
            if (cfg.Count == 0 && !settingsUnreadable && !DemoMode) cfg["widgets"] = NoWidgets;
            pinned = Lines("pinned.txt");
            // первый запуск (список пуст или только «Проводник» по умолчанию из прошлых версий): берём закрепления панели Windows в её порядке
            var explorer = Environment.ExpandEnvironmentVariables(@"%WINDIR%\explorer.exe");
            if ((pinned.Count == 0 || (pinned.Count == 1 && string.Equals(pinned[0], explorer, StringComparison.OrdinalIgnoreCase))) && !cfg.ContainsKey("pinsImported") && !settingsUnreadable)
            {
                pinned = WindowsPins(); cfg["pinsImported"] = "1"; SaveCfg();
                if (pinned.Count == 0) pinned.Add(explorer);
                SafeFile.WriteLines(F("pinned.txt"), pinned);
            }
            todos = Lines("todos.txt").Select(ParseTodo).ToList();
            calFeeds = Lines("calendars.txt");
            LoadGroups(); ApplyAppTheme();
            screen = ScreenTime.Load(Today);
        }
        public static string ExitWhy; // кто попросил закрыть панель — для журнала
        public static void Shutdown()
        {
            CloseGuarded(); CloseDesktopMedia(true);
            foreach (var b in bars.ToList()) b.Unreserve();
            if (!DemoMode) { Tray.Stop(); Taskbar.Restore(true, true); } // всегда показываем панель Windows, даже если до FlowBar она была скрыта
            try { if (screen != null) screen.Save(); } catch (Exception ex) { Log(ex); }
        }

        public Bar(System.Windows.Forms.Screen s)
        {
            scr = s; primary = s.Primary; bars.Add(this);
            mon = N.MonitorFromPoint(new N.POINT { X = s.Bounds.Left + s.Bounds.Width / 2, Y = s.Bounds.Top + s.Bounds.Height / 2 }, 2);
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"); Title = "FlowBar";
            System.Windows.Documents.Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular); // часы и проценты не «прыгают» по ширине
            Build();
            SizeChanged += (o, e) => Place();
            SourceInitialized += OnInit;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            t.Tick += (o, e) => Tick(); t.Start();
            Closed += (o, e) => { t.Stop(); ClosePreview(); if (hideT != null) hideT.Stop(); Unreserve(); bars.Remove(this); };
            hoverT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            hoverT.Tick += (o, e) => PreviewHoverCheck();
            // async-код WinRT запускаем только после старта диспетчера, иначе продолжения уйдут не в UI-поток
            Loaded += (o, e) => { Tick(); if (primary) InitRadios(); };
        }

        // ---------- хранение ----------
        static string F(string name) { return System.IO.Path.Combine(Dir, name); }
        static List<string> Lines(string name) { return SafeFile.ReadLines(F(name)).ToList(); }
        static void SaveCfg() { CheckHide("сохранение"); Persist("настройки", () => SafeFile.WriteLines(F("settings.txt"), cfg.Select(kv => kv.Key + "=" + kv.Value))); RecordAppearance(); }
        // запись на диск без падения: сбой — в журнал и одно уведомление (пока записи не пойдут снова), а не тихая потеря настройки
        internal static bool saveWarned;
        internal static void Persist(string what, Action write)
        {
            try { write(); saveWarned = false; }
            catch (Exception ex) { Log(ex); if (!saveWarned && Primary != null) { saveWarned = true; Primary.Notify("Не удалось сохранить: " + what, ex.Message, true, ""); } }
        }

        // Диагностика: при входе в Windows панель дважды считала скрытие панели Windows выключенным, хотя в файле включено.
        // Пишем в журнал, что прочитано из settings.txt и .bak, и каждое изменение значения в памяти — с местом в коде.
        static string hideSeen;
        static void LogHideSource()
        {
            hideSeen = C("hideTaskbar", "1");
            if (hideSeen == "1") return;
            Func<string, string> raw = f => { try { var l = File.Exists(f) ? File.ReadAllLines(f).FirstOrDefault(x => x.StartsWith("hideTaskbar=")) : "нет файла"; return (l ?? "нет строки") + " (" + (File.Exists(f) ? File.GetLastWriteTime(f).ToString("dd.MM HH:mm:ss") : "—") + ")"; } catch (Exception ex) { return ex.GetType().Name; } };
            Info("при запуске скрытие панели Windows выключено: в памяти hideTaskbar=" + hideSeen + "; settings.txt: " + raw(F("settings.txt")) + "; .bak: " + raw(F("settings.txt.bak")));
        }
        internal static void CheckHide(string where)
        {
            var v = C("hideTaskbar", "1"); if (hideSeen == null || v == hideSeen) return;
            Info("hideTaskbar изменилось: " + hideSeen + " → " + v + " (" + where + ")\r\n" + Environment.StackTrace); hideSeen = v;
        }
        static void SavePinned()
        {
            pinned = pinned.OrderBy(p => { int i = appOrder.IndexOf("p|" + p); return i < 0 ? int.MaxValue : i; }).ToList(); // на диск — в порядке панели
            Persist("закрепления", () => SafeFile.WriteLines(F("pinned.txt"), pinned)); All(b => { b.lastKey = null; b.RefreshApps(); });
        }
        public static void Retag(string from, string to)
        {
            appOrder.Remove(to); int i = appOrder.IndexOf(from); if (i >= 0) appOrder[i] = to;
            foreach (var b in bars) if (b.seq != null) { b.seq.Remove(to); int j = b.seq.IndexOf(from); if (j >= 0) { b.seq[j] = to; b.SaveSeq(); } } // закрепили/открепили — значок на своём месте
        }
        void SaveTodos() { Persist("задачи", () => SafeFile.WriteLines(F("todos.txt"), todos.Select(TodoLine))); UpdateTodoTile(); }
        public static Todo ParseTodo(string l)
        {
            int i = l.IndexOf('|');
            if (i < 0) return new Todo { Text = l };
            var flag = l.Substring(0, i);
            DateTime due;
            return new Todo
            {
                Done = flag == "1" || flag.StartsWith("1@"), Text = l.Substring(i + 1),
                Due = flag.Length == 10 && flag[1] == '@' && DateTime.TryParseExact(flag.Substring(2), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out due) ? (DateTime?)due : null
            };
        }
        public static string TodoLine(Todo t) { return (t.Done ? "1" : "0") + (t.Due.HasValue ? "@" + t.Due.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture) : "") + "|" + t.Text; }
        static Todo NextDueTodo() { return todos.Where(t => !t.Done).OrderBy(t => t.Due ?? DateTime.MaxValue).FirstOrDefault(); }
        // журнал error.log: ошибки и важные события (запуск, смена мониторов); больше 1 МБ — прежний уходит в error.old.log
        public static void Log(Exception ex) { WriteLog(ex.ToString()); }
        public static void Info(string s) { WriteLog("· " + s); }
        static readonly object logLock = new object();
        static void WriteLog(string s)
        {
            try
            {
                lock (logLock)
                {
                    var f = F("error.log"); var fi = new FileInfo(f);
                    if (fi.Exists && fi.Length > 1048576) { File.Copy(f, F("error.old.log"), true); File.Delete(f); }
                    File.AppendAllText(f, DateTime.Now + " " + s + "\r\n");
                }
            }
            catch { }
        }

        // версия, Windows, права, мониторы и панели — в журнал при запуске и в отчёт
        internal static string SysInfo()
        {
            var sb = new StringBuilder();
            string win = "";
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")) { int bn; int.TryParse(k.GetValue("CurrentBuild") as string, out bn); win = ((k.GetValue("ProductName") as string) ?? "").Replace("Windows 10", bn >= 22000 ? "Windows 11" : "Windows 10") + " " + k.GetValue("DisplayVersion") + ", сборка " + bn + "." + k.GetValue("UBR"); } } catch { }
            sb.Append("FlowBar " + Setup.VersionText + (Setup.IsInstalledCopy ? "" : " (запущен не из папки установки)") + "; " + win + "; права администратора: " + (Setup.Elevated ? "да" : "нет")
                + "; панель Windows скрывается: " + (HideTb ? "да" : C("hideTaskbar", "1") == "1" ? "нет — сторож не запустился" : "нет — выключено") + "; свой трей: " + (Bar.C("ownTray", "1") == "1" ? Tray.Icons.Count + " значков" : "выключен")
                + "; стекло: " + (!WantsGlass(Cur) ? "не выбрано" : Backdrop.Why == null ? "работает" : "нет — " + Backdrop.Why)
                + "; память: " + (Process.GetCurrentProcess().WorkingSet64 >> 20) + " МБ занято, куча " + (GC.GetTotalMemory(false) >> 20) + " МБ\r\n");
            foreach (var b in bars.ToList())
                try { sb.Append("монитор " + b.scr.DeviceName + (b.scr.Primary ? " (основной)" : "") + ": " + b.scr.Bounds.Width + "×" + b.scr.Bounds.Height + " в " + b.scr.Bounds.X + "," + b.scr.Bounds.Y + ", масштаб " + Math.Round(b.MonScale * 100) + "%; панель " + Math.Round(b.Left) + "," + Math.Round(b.Top) + " " + Math.Round(b.ActualWidth) + "×" + Math.Round(b.ActualHeight) + (b.IsVisible ? "" : " скрыта") + "\r\n"); }
                catch { }
            return sb.ToString();
        }

        // Отчёт для разработчика: файл на рабочем столе — сведения о системе, настройки (место и ссылки скрыты), трей и конец журнала; путь к профилю заменён на %USERPROFILE%
        static void SaveReport()
        {
            try
            {
                var sb = new StringBuilder("Отчёт FlowBar от " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n\r\n" + SysInfo());
                sb.Append("\r\n— настройки —\r\n");
                foreach (var kv in cfg) sb.Append(kv.Key + "=" + (Regex.IsMatch(kv.Key, "wx|url|token|key|secret|feed|ical|mail|pass", RegexOptions.IgnoreCase) ? "(скрыто)" : kv.Value) + "\r\n");
                sb.Append("\r\n— значки трея (без подсказок) —\r\n");
                try { foreach (var l in Tray.Dump()) { var p = l.Split('\t'); sb.Append(string.Join("\t", p.Take(Math.Max(1, p.Length - 1))) + "\r\n"); } } catch { }
                sb.Append("\r\n— журнал, последние 400 строк —\r\n");
                var lines = new List<string>();
                lock (logLock) foreach (var f in new[] { F("error.old.log"), F("error.log") }) try { if (File.Exists(f)) lines.AddRange(File.ReadAllLines(f)); } catch { }
                foreach (var l in lines.Skip(Math.Max(0, lines.Count - 400))) sb.Append(l + "\r\n");
                var text = sb.ToString().Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%");
                if (Environment.UserName.Length >= 3) text = text.Replace(Environment.UserName, "%USERNAME%");
                var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "FlowBar-отчёт-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm") + ".txt");
                File.WriteAllText(path, text, Encoding.UTF8);
                Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception ex) { Log(ex); MessageBox.Show("Не удалось сохранить отчёт:\n" + ex.Message, "FlowBar"); }
        }

        // ---------- системное ----------
        public static void SetTaskbar(bool show) { Taskbar.Set(show); }
        // Windows не пускает нажатия от обычной программы в окно с правами администратора (UIPI): пока впереди Диспетчер задач, «Пуск», поиск, громкость
        // не срабатывали. Тогда сначала выводим вперёд своё окно, потом шлём клавиши.
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        static bool HigherIntegrity(uint pid)
        {
            // обычному процессу токен процесса с правами администратора не открыть (отказ в доступе) — значит, окно «выше» нас
            var ph = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid); if (ph == IntPtr.Zero) return true;
            try { IntPtr th; if (!OpenProcessToken(ph, 0x8 /* TOKEN_QUERY */, out th)) return true; CloseHandle(th); return false; }
            finally { CloseHandle(ph); }
        }
        static void FrontForKeys()
        {
            try
            {
                var fg = N.GetForegroundWindow(); if (fg == IntPtr.Zero) return;
                uint pid; N.GetWindowThreadProcessId(fg, out pid);
                if (pid == (uint)Process.GetCurrentProcess().Id || !HigherIntegrity(pid)) return;
                var p = Primary; if (p == null || p.hwnd == IntPtr.Zero) return;
                N.SetForegroundWindow(p.hwnd);
                for (int i = 0; i < 10 && N.GetForegroundWindow() != p.hwnd; i++) System.Threading.Thread.Sleep(10);
            }
            catch (Exception ex) { Log(ex); }
        }
        static void Keys(params byte[] vks)
        {
            FrontForKeys();
            foreach (var k in vks) N.keybd_event(k, 0, 0, UIntPtr.Zero);
            for (int i = vks.Length - 1; i >= 0; i--) N.keybd_event(vks[i], 0, 2, UIntPtr.Zero);
        }
        static string Cls(IntPtr h) { var sb = new StringBuilder(64); N.GetClassName(h, sb, 64); return sb.ToString(); }
        // ---------- отметки на ярлыках: счётчик непрочитанного и «требует внимания» ----------
        // Значок-наложение программы отдают только панели Windows. Число берём из заголовка окна: «(3) WhatsApp», «(12) Discord», у Telegram — «… (5)» в конце.
        [DllImport("user32.dll")] static extern bool RegisterShellHookWindow(IntPtr h);
        static readonly uint ShellHookMsg = N.RegisterWindowMessage("SHELLHOOK");
        static readonly HashSet<IntPtr> flashing = new HashSet<IntPtr>();
        public static int BadgeOf(string title, string exe)
        {
            if (string.IsNullOrEmpty(title)) return 0;
            var mt = Regex.Match(title, @"^\((\d[\d\s\u00A0]{0,8})\)");
            if (!mt.Success && exe != null && System.IO.Path.GetFileNameWithoutExtension(exe).ToLowerInvariant().Contains("telegram")) mt = Regex.Match(title, @"\((\d[\d\s\u00A0]{0,8})\)$");
            int n; return mt.Success && int.TryParse(Regex.Replace(mt.Groups[1].Value, @"[^\d]", ""), out n) ? n : 0;
        }
        static string TitleOf(IntPtr h) { var sb = new StringBuilder(256); N.GetWindowText(h, sb, 256); return sb.ToString(); }
        internal static string ExePathOf(uint pid) { return ExePath(pid); }
        static string ExePath(uint pid)
        {
            string p; if (pathCache.TryGetValue(pid, out p)) return p;
            var h = N.OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (h != IntPtr.Zero) { var sb = new StringBuilder(1024); int n = sb.Capacity; if (N.QueryFullProcessImageName(h, 0, sb, ref n)) p = sb.ToString(); N.CloseHandle(h); }
            return pathCache[pid] = p;
        }
        static string AppName(string path)
        {
            if (path == null) return "Система";
            if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return PinName(path); // приложение Windows: имя из папки приложений, а не «Пакет!Приложение»
            string n; if (nameCache.TryGetValue(path, out n)) return n;
            try { n = FileVersionInfo.GetVersionInfo(path).FileDescription; } catch { }
            if (string.IsNullOrWhiteSpace(n)) n = System.IO.Path.GetFileNameWithoutExtension(path);
            return nameCache[path] = n.Trim();
        }
        static string Target(string p)
        {
            if (!p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return p;
            string t; if (lnkCache.TryGetValue(p, out t)) return t;
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell"); object sh = Activator.CreateInstance(type);
                object lnk = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { p });
                t = (string)lnk.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, lnk, null);
                t = SquirrelTarget(t, (string)lnk.GetType().InvokeMember("Arguments", BindingFlags.GetProperty, null, lnk, null));
            }
            catch { }
            return lnkCache[p] = string.IsNullOrEmpty(t) ? p : t;
        }
        // Discord, Slack и др. (установщик Squirrel): ярлык ведёт на «Update.exe --processStart Discord.exe» — настоящая программа лежит рядом
        public static string SquirrelTarget(string target, string args)
        {
            if (target == null || !System.IO.Path.GetFileName(target).Equals("Update.exe", StringComparison.OrdinalIgnoreCase)) return target;
            var m = Regex.Match(args ?? "", @"--processStart\s+""?([^""\s]+)");
            return m.Success ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target), m.Groups[1].Value) : target;
        }
        public static string Key(string path)
        {
            var p = (path ?? "").ToLowerInvariant();
            // приложение Store: «shell:appsfolder\имя_издатель!app» и «…\windowsapps\имя_версия_арх__издатель\x.exe» — одно семейство пакета
            if (p.StartsWith(@"shell:appsfolder\") && p.Contains("!")) return "pkg:" + p.Substring(17, p.IndexOf('!') - 17);
            var m = Regex.Match(p, @"\\windowsapps\\([^\\_]+)_[^\\]*_([a-z0-9]{13})\\");
            if (m.Success) return "pkg:" + m.Groups[1].Value + "_" + m.Groups[2].Value;
            return Regex.Replace(p, @"\\app-[\d.]+(?=\\)", ""); // Squirrel: «…\discord\app-1.0.9\discord.exe» = «…\discord\discord.exe»
        }
        static Dictionary<string, string> shellNames = new Dictionary<string, string>();
        static string PinName(string path)
        {
            if (path == null) return "?";
            if (!path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return System.IO.Path.GetFileNameWithoutExtension(path);
            string n = null; if (shellNames.TryGetValue(path, out n)) return n;
            var id = path.Substring(path.IndexOf('\\') + 1);
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application"); object sh = Activator.CreateInstance(type);
                object folder = type.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, sh, new object[] { "shell:AppsFolder" });
                object item = folder.GetType().InvokeMember("ParseName", BindingFlags.InvokeMethod, null, folder, new object[] { id });
                if (item != null) n = (string)item.GetType().InvokeMember("Name", BindingFlags.GetProperty, null, item, null);
            }
            catch { }
            return shellNames[path] = string.IsNullOrEmpty(n) ? id : n;
        }
        static ImageSource ShellIcon(string path, int px = 48)
        {
            try
            {
                N.IShellItemImageFactory f; N.SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(N.IShellItemImageFactory).GUID, out f);
                IntPtr hb; if (f.GetImage(new N.SIZE { cx = px, cy = px }, 0x4 /* только значок */, out hb) != 0) return null;
                try
                {
                    var ds = new N.DIBSECTION(); N.GetObject(hb, Marshal.SizeOf(ds), ref ds);
                    if (ds.bmBitsPixel != 32 || ds.bmBits == IntPtr.Zero) return null;
                    int h = ds.bmHeight, stride = ds.bmWidthBytes; var pix = new byte[stride * h];
                    for (int y = 0; y < h; y++) Marshal.Copy(ds.bmBits + (ds.biHeight > 0 ? h - 1 - y : y) * stride, pix, y * stride, stride); // DIB бывает «снизу вверх»
                    // альфа у GetImage не предумножена (проверено: цвет бывает больше альфы); с Pbgra32 края давали светлую кайму
                    var img = BitmapSource.Create(ds.bmWidth, h, 96, 96, PixelFormats.Bgra32, null, pix, stride); img.Freeze(); return img;
                }
                finally { N.DeleteObject(hb); }
            }
            catch { return null; }
        }
        // закрепления панели Windows: ярлыки в папке TaskBar и приложения Store; порядок — по месту в Taskband\Favorites
        static List<string> WindowsPins()
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar";
            byte[] fav = null;
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband")) if (k != null) fav = k.GetValue("Favorites") as byte[]; } catch { }
            var own = new List<string>();
            try { own.AddRange(Directory.GetFiles(dir, "*.lnk")); } catch { }
            // ярлык закрепления бывает удалён (так у Steam) — тогда ищем одноимённый в «Пуске»
            var lnks = own.Concat(StartMenuApps().Where(p => p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))).ToList();
            var res = fav == null ? own : OrderPins(fav, lnks);
            // у ярлыка «Проводник» нет пути к программе — подставляем explorer.exe, чтобы окна проводника собирались под ним
            return res.Select(p => p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && Target(p) == p && Regex.IsMatch(System.IO.Path.GetFileName(p), "explorer|проводник", RegexOptions.IgnoreCase)
                ? Environment.ExpandEnvironmentVariables(@"%WINDIR%\explorer.exe") : p).Distinct().ToList();
        }
        public static List<string> OrderPins(byte[] fav, List<string> lnks)
        {
            var found = new List<KeyValuePair<int, string>>();
            var seen = new HashSet<string>();
            for (int a = 0; a < 2; a++) // строки UTF-16 в блобе лежат и с чётным, и с нечётным смещением
            {
                var s = Encoding.Unicode.GetString(fav, a, fav.Length - a);
                foreach (Match m in Regex.Matches(s, @"[A-Za-z0-9][\w.\-]*_[a-z0-9]{13}![\w.\-]+"))
                    found.Add(new KeyValuePair<int, string>(m.Index * 2 + a, @"shell:AppsFolder\" + m.Value));
                // длинные имена первыми, чтобы «Chrome.lnk» не нашёлся внутри «Google Chrome.lnk»
                var spans = new List<int[]>();
                foreach (var l in lnks.OrderByDescending(x => System.IO.Path.GetFileName(x).Length))
                {
                    var nm = System.IO.Path.GetFileName(l);
                    if (seen.Contains(nm.ToLowerInvariant())) continue;
                    for (int i = s.IndexOf(nm, StringComparison.OrdinalIgnoreCase); i >= 0; i = s.IndexOf(nm, i + 1, StringComparison.OrdinalIgnoreCase))
                    {
                        int st = i, en = i + nm.Length;
                        if (spans.Any(x => st >= x[0] && en <= x[1])) continue; // внутри уже найденного имени
                        spans.Add(new[] { st, en }); seen.Add(nm.ToLowerInvariant());
                        found.Add(new KeyValuePair<int, string>(i * 2 + a, l)); break;
                    }
                }
            }
            var keys = new HashSet<string>();
            return found.OrderBy(kv => kv.Key).Select(kv => kv.Value).Where(p => keys.Add(p.ToLowerInvariant())).ToList();
        }
        static ImageSource IconFor(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            // размер плитки S/M/L × наибольший масштаб мониторов с панелями, с запасом до стандартного размера значка
            int px = NeedIconPx; var key = path + "|" + px;
            ImageSource img; if (iconCache.TryGetValue(key, out img)) return img;
            // значок от оболочки Windows — тот же, что на её панели (у ярлыков — их собственный); запасной путь — значок файла 32 px
            img = ShellIcon(path, px);
            if (img != null || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return iconCache[key] = img;
            try
            {
                using (var ic = System.Drawing.Icon.ExtractAssociatedIcon(path))
                { img = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); img.Freeze(); }
            }
            catch { img = null; }
            return iconCache[key] = img;
        }
        public static int IconPxFor(double dip, double scale) { int need = (int)Math.Ceiling(dip * scale); foreach (var p in new[] { 32, 40, 48, 64, 96, 128, 256 }) if (p >= need) return p; return 256; }
        static int NeedIconPx { get { return bars.Count == 0 ? 32 : bars.Max(b => IconPxFor(b.IconPx, b.MonScale)); } }
        // Окно показывает вспомогательный процесс, а программа и ярлык — другой exe: Steam рисует окна в steamwebhelper.exe.
        // Берём основной exe программы, иначе у закреплённого ярлыка нет отметки, а рядом появляется второй значок.
        static readonly Dictionary<string, string> hostCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static string HostExe(string path)
        {
            if (path == null || !path.EndsWith("steamwebhelper.exe", StringComparison.OrdinalIgnoreCase)) return path;
            string res;
            if (hostCache.TryGetValue(path, out res)) return res;
            res = path;
            try { for (var d = System.IO.Path.GetDirectoryName(path); !string.IsNullOrEmpty(d); d = System.IO.Path.GetDirectoryName(d)) { var s = System.IO.Path.Combine(d, "steam.exe"); if (File.Exists(s)) { res = s; break; } } } catch { }
            hostCache[path] = res; return res;
        }
        // окна FlowBar, которые видны на панели как открытая программа: настройки, каталог, карточки виджетов
        static readonly HashSet<IntPtr> shownOnBar = new HashSet<IntPtr>();
        static readonly string SelfExe = System.Reflection.Assembly.GetExecutingAssembly().Location;
        internal static void ShowOnBar(Window w)
        {
            IntPtr h = IntPtr.Zero;
            w.SourceInitialized += (s, e) => { h = new WindowInteropHelper(w).Handle; shownOnBar.Add(h); };
            w.Closed += (s, e) => shownOnBar.Remove(h);
        }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore { [PreserveSig] int GetCount(out uint c); [PreserveSig] int GetAt(uint i, out PropKey k); [PreserveSig] int GetValue(ref PropKey k, out PropVar v); [PreserveSig] int SetValue(ref PropKey k, ref PropVar v); [PreserveSig] int Commit(); }
        [StructLayout(LayoutKind.Sequential, Pack = 4)] struct PropKey { public Guid Fmt; public uint Pid; }
        [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVar { [FieldOffset(0)] public ushort Vt; [FieldOffset(8)] public IntPtr Ptr; }
        [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr h, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore ps);
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVar v);
        static readonly Dictionary<IntPtr, string> aumidCache = new Dictionary<IntPtr, string>();
        // AppUserModelID окна (System.AppUserModel.ID); у окон UWP — идентификатор самого приложения
        static string WindowAumid(IntPtr h)
        {
            string cached; if (aumidCache.TryGetValue(h, out cached)) return cached;
            string id = null;
            try
            {
                var iid = typeof(IPropertyStore).GUID; IPropertyStore ps;
                if (SHGetPropertyStoreForWindow(h, ref iid, out ps) == 0 && ps != null)
                    try
                    {
                        var key = new PropKey { Fmt = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Pid = 5 }; PropVar v;
                        if (ps.GetValue(ref key, out v) == 0)
                            try { if (v.Vt == 31 && v.Ptr != IntPtr.Zero) id = Marshal.PtrToStringUni(v.Ptr); } finally { PropVariantClear(ref v); }
                    }
                    finally { Marshal.ReleaseComObject(ps); }
            }
            catch { }
            if (aumidCache.Count > 300) aumidCache.Clear();
            if (id != null && id.IndexOf('!') < 0) id = null; // «Пакет_издатель!Приложение»: у классических программ ID иной, их значок берётся из exe
            return aumidCache[h] = id;
        }
        List<Win> GetWindows()
        {
            var list = new List<Win>();
            N.EnumWindows((h, l) =>
            {
                if (!N.IsWindowVisible(h)) return true;
                uint pid; N.GetWindowThreadProcessId(h, out pid);
                if (pid == selfPid) { if (shownOnBar.Contains(h)) list.Add(new Win { H = h, Pid = pid, Path = SelfExe, Mon = MonOf(h) }); return true; } // свои окна — только настройки, каталог и карточки
                if (N.GetWindow(h, 4) != IntPtr.Zero) return true; // есть владелец
                long ex = N.GetWindowLongPtr(h, -20).ToInt64();
                if ((ex & 0x80) != 0) return true; // WS_EX_TOOLWINDOW
                if ((ex & 0x08000000) != 0 && (ex & 0x40000) == 0) return true; // NOACTIVATE без APPWINDOW
                int cloaked; if (N.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                if (N.GetWindowText(h, new StringBuilder(2), 2) == 0) return true;
                var c = Cls(h); if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return true;
                // окно UWP-приложения (Параметры, Калькулятор) рисует ApplicationFrameHost: значок и группа — по AppUserModelID окна, а не общий значок хоста
                string uwp = c == "ApplicationFrameWindow" ? WindowAumid(h) : null;
                list.Add(new Win { H = h, Pid = pid, Path = uwp != null ? @"shell:AppsFolder\" + uwp : HostExe(ExePath(pid)), Mon = MonOf(h) });
                return true;
            }, IntPtr.Zero);
            allWins = list;
            return WindowsForBar(list);
        }
        // какие окна на какой панели — настройка «Мониторы»: all — все окна везде; main — основная всё, остальные свои; own — каждая свои
        List<Win> WindowsForBar(List<Win> list)
        {
            var mode = WinMode;
            bool all = !CompactMonitor && (bars.Count < 2 || mode == "all" || (mode == "main" && primary));
            return all ? list : list.Where(w => w.Mon == mon).ToList();
        }
        // окна на всех мониторах (панель со «своими окнами» показывает только свои): клик по ярлыку выводит уже открытое окно с другого экрана, а не запускает программу заново
        List<Win> allWins = new List<Win>();
        List<Win> Elsewhere(string path) { if (path == null) return new List<Win>(); var k = Key(Target(path)); return allWins.Where(w => w.Mon != mon && Key(w.Path) == k).OrderBy(w => w.H.ToInt64()).ToList(); }
        static string WinMode { get { var m = C("winMode", "own"); return m == "all" || m == "main" ? m : "own"; } }
        // монитор окна; у свёрнутого — монитор его обычного положения (свёрнутое стоит за пределами экранов)
        [StructLayout(LayoutKind.Sequential)] struct WinPlace { public int length, flags, showCmd; public N.POINT minPos, maxPos; public N.RECT normal; }
        [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr h, ref WinPlace p);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref N.RECT r, uint flags);
        static IntPtr MonOf(IntPtr h)
        {
            if (N.IsIconic(h))
            {
                var p = new WinPlace { length = Marshal.SizeOf(typeof(WinPlace)) };
                if (GetWindowPlacement(h, ref p)) return MonitorFromRect(ref p.normal, 2);
            }
            return N.MonitorFromWindow(h, 2);
        }
        static void Launch(string path, string args = null)
        {
            try { Start(path, args); }
            catch (Exception ex) { MessageBox.Show("Не удалось запустить:\n" + path + "\n\n" + ex.Message, "FlowBar"); }
        }
        // Панель с правами администратора запускает программы через Проводник (рабочий стол — IShellDispatch2.ShellExecute), то есть без своих прав;
        // иначе всё открытое из панели работало бы от администратора.
        internal static void Start(string file, string args = null, string dir = null, bool viaShell = false)
        {
            if (Setup.Elevated || viaShell)
                try
                {
                    var wins = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")));
                    var a = new object[] { 0, null, 8, 0, 1 }; var pm = new ParameterModifier(5); pm[0] = true; pm[1] = true; pm[3] = true; // SWC_DESKTOP, SWFO_NEEDDISPATCH
                    var desk = wins.GetType().InvokeMember("FindWindowSW", BindingFlags.InvokeMethod, null, wins, a, new[] { pm }, null, null);
                    var doc = desk.GetType().InvokeMember("Document", BindingFlags.GetProperty, null, desk, null);
                    var app = doc.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, doc, null);
                    app.GetType().InvokeMember("ShellExecute", BindingFlags.InvokeMethod, null, app, new object[] { file, args ?? "", dir ?? "", "open", 1 });
                    return;
                }
                catch (Exception ex) { Log(new Exception("Запуск через Проводник не удался: " + file, ex)); }
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, Arguments = args ?? "", WorkingDirectory = dir ?? "" });
        }
        public static string Rate(double b)
        {
            var inv = CultureInfo.InvariantCulture;
            return b >= 1048576 ? (b / 1048576).ToString("0.0", inv) + "M" : (b / 1024).ToString("0", inv) + "K";
        }
        double Scale { get { var s = PresentationSource.FromVisual(this); return s == null ? 1 : s.CompositionTarget.TransformToDevice.M11; } }
        // ---------- построение UI ----------
        // Шкала шрифта: подписи панели ≥ 10, текст карточек ≥ 11; цифры — табличные (одинаковой ширины)
        static TextBlock T(double size, bool bold)
        {
            return new TextBlock { FontSize = Math.Max(10, size), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        static TextBlock Txt(string text, double size, bool bold) { return new TextBlock { Text = text, FontSize = Math.Max(11, size), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis }; }
        static TextBlock G(string glyph, double size = 0)
        {
            return new TextBlock { Text = glyph, FontFamily = Icons, FontSize = size > 0 ? size : Math.Round(15 * (Primary == null ? 1.12 : Primary.Fs)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        static string lastClosedTitle; static DateTime lastClosedAt;
        // Окна оболочки Windows: «Пуск», поиск, быстрые настройки, центр уведомлений
        static readonly string[] ShellHosts = { "startmenuexperiencehost.exe", "shellexperiencehost.exe", "shellhost.exe", "searchhost.exe" };
        static bool shellAtDown; static string lastShellKeys;
        static bool ShellFlyoutOpen()
        {
            uint pid; N.GetWindowThreadProcessId(N.GetForegroundWindow(), out pid);
            var p = ExePath(pid);
            return p != null && ShellHosts.Contains(System.IO.Path.GetFileName(p).ToLowerInvariant());
        }
        // Пуск и поиск открыты (клавиша Win, Win+S): окно оболочки на переднем плане. Пересчёт только при смене окна — проверка идёт 20 раз в секунду
        static IntPtr shellFgWnd; static bool shellFgOpen;
        static bool ShellFlyoutCached() { var f = N.GetForegroundWindow(); if (f != shellFgWnd) { shellFgWnd = f; shellFgOpen = ShellFlyoutOpen(); } return shellFgOpen; }
        // окно оболочки открыто на экране этой панели (центр окна внутри экрана)
        bool ShellFlyoutHere()
        {
            if (!ShellFlyoutCached()) return false;
            N.RECT r; if (!N.GetWindowRect(shellFgWnd, out r)) return true;
            int cx = (r.L + r.R) / 2, cy = (r.T + r.B) / 2; var b = scr.Bounds;
            return cx >= b.Left && cx < b.Right && cy >= b.Top && cy < b.Bottom;
        }
        // Повторное нажатие на ту же кнопку закрывает открытое ею окно Windows, а не открывает снова
        static void ShellToggle(params byte[] keys)
        {
            var id = string.Join(",", keys);
            if (shellAtDown)
            {
                if (ShellFlyoutOpen()) Keys(0x1B); // Esc — только если окно оболочки всё ещё на переднем плане
                if (lastShellKeys == id) { lastShellKeys = null; return; }
            }
            lastShellKeys = id;
            Keys(keys);
        }
        // Кнопка-крестик (удалить/закрыть) с подписью для экранного диктора
        static Press Cross(string name, Action a)
        {
            var t = new TextBlock { Text = "", FontFamily = Icons, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Press { Child = t, Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Cursor = Cursors.Hand, ToolTip = name, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75, Background = Brushes.Transparent, Invoke = a };
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            b.MouseEnter += (s, e) => b.Opacity = 1; b.MouseLeave += (s, e) => b.Opacity = 0.75;
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; if (a != null) a(); };
            return b;
        }
        // любой элемент как кнопка: Tab, Enter/Пробел, действие для экранного диктора
        static Press Act(FrameworkElement content, string name, Action a)
        {
            var p = new Press { Child = content, Background = Brushes.Transparent, Cursor = Cursors.Hand, Invoke = a, CornerRadius = new CornerRadius(6) };
            if (name != null) System.Windows.Automation.AutomationProperties.SetName(p, name);
            p.MouseLeftButtonUp += (s, e) => { e.Handled = true; a(); };
            return p;
        }
        // Заголовок раздела карточки — единый отступ сверху 16
        static TextBlock H(string s) { var t = Txt(s, 12, true); t.Margin = new Thickness(0, 16, 0, 6); t.Opacity = 0.8; return t; }
        StackPanel Stat(string label, out TextBlock val, out TextBlock lab)
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            lab = T(10, false); lab.Text = label; lab.Opacity = 0.72;
            val = T(12.5, true); val.Text = "…";
            sp.Children.Add(lab); sp.Children.Add(val); return sp;
        }
        StackPanel Stat(string label, out TextBlock val) { TextBlock l; return Stat(label, out val, out l); }

        // Плавная смена фона: 120 мс на наведение, мгновенно при отключённых анимациях Windows
        static void Fade(Border b, Color to, int ms)
        {
            var br = b.Background as SolidColorBrush;
            if (br == null || br.IsFrozen) { br = new SolidColorBrush(br == null ? Colors.Transparent : br.Color); b.Background = br; }
            if (!Motion) { br.BeginAnimation(SolidColorBrush.ColorProperty, null); br.Color = to; return; }
            br.BeginAnimation(SolidColorBrush.ColorProperty, new System.Windows.Media.Animation.ColorAnimation(to, TimeSpan.FromMilliseconds(ms)));
        }

        // значок пункта меню — по началу подписи (Segoe Fluent Icons); нет подходящего — пункт без значка
        static readonly string[][] MenuGlyphs = {
            new[] { "Открыть новое окно", "\uE8A7" }, new[] { "Открепить", "\uE77A" }, new[] { "Закрепить", "\uE718" }, new[] { "Добавить в группу", "\uE8F4" }, new[] { "Убрать из группы", "\uE8F6" },
            new[] { "Новая группа", "\uE8F4" }, new[] { "Закрыть", "\uE8BB" }, new[] { "Завершить", "\uE7E8" }, new[] { "Очистить", "\uE74D" }, new[] { "Открыть корзину", "\uE74D" }, new[] { "Открыть диск", "\uEDA2" },
            new[] { "Открыть Spotify", "\uE8D6" }, new[] { "Открыть", "\uE8E5" }, new[] { "Показать в папке", "\uE838" }, new[] { "Параметры", "\uE713" }, new[] { "Настройки", "\uE713" }, new[] { "Каталог", "\uE71D" },
            new[] { "Убрать с панели", "\uE738" }, new[] { "Размер", "\uE740" }, new[] { "Оставлять видимым", "\uE7B3" }, new[] { "Микшер", "\uE9E9" }, new[] { "Обновить", "\uE72C" }, new[] { "Диспетчер задач", "\uE9D9" },
            new[] { "Устройство вывода", "\uE7F5" }, new[] { "Без звука", "\uE74F" }, new[] { "Список сетей", "\uE701" }, new[] { "Состояние сети", "\uE774" }, new[] { "Сети Wi-Fi", "\uE701" }, new[] { "Устройства Bluetooth", "\uE702" },
            new[] { "Быстрые настройки", "\uE9E9" }, new[] { "Перенести на этот экран", "\uE7F4" }, new[] { "Свернуть", "\uE73F" }, new[] { "Развернуть", "\uE740" },
            new[] { "Заблокировать", "\uE72E" }, new[] { "Разблокировать", "\uE785" }, new[] { "Расположение панели", "\uE8A9" }, new[] { "Виджет на панели", "\uE71D" },
            new[] { "Оформление", "\uE771" }, new[] { "Выход", "\uE8BB" }, new[] { "Включить звук", "\uE767" }, new[] { "Сбросить", "\uE72C" },
            new[] { "Окна (", "\uE737" }, new[] { "Не скрывать", "\uE7B3" } };
        static string MenuGlyph(string header) { foreach (var g in MenuGlyphs) if (header.StartsWith(g[0])) return g[1]; return null; }
        // шапка меню программы: её значок и название
        static FrameworkElement MenuHead(ImageSource icon, string name, string detail)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 6, 12, 6), IsHitTestVisible = false };
            if (icon != null) { var img = new Image { Source = icon, Width = 20, Height = 20, Margin = new Thickness(0, 0, 9, 0) }; RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality); sp.Children.Add(img); }
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 13, MaxWidth = 250, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrEmpty(detail)) labels.Children.Add(new TextBlock { Text = detail, FontSize = 11, Opacity = 0.72, Margin = new Thickness(0, 2, 0, 0) });
            sp.Children.Add(labels);
            return sp;
        }
        static MenuItem MenuBranch(string header)
        {
            var it = new MenuItem { Header = header };
            var gl = MenuGlyph(header);
            if (gl != null) it.Icon = new TextBlock { Text = gl, FontFamily = Icons, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            return it;
        }
        static void MenuSection(ItemsControl menu, params object[] entries)
        {
            var items = entries.Where(x => x != null).ToArray();
            if (items.Length == 0) return;
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            foreach (var item in items) menu.Items.Add(item);
        }
        static MenuItem Item(string header, Action a, bool? check = null)
        {
            var it = MenuBranch(header);
            if (check.HasValue) { it.IsCheckable = true; it.IsChecked = check.Value; }
            if (header.StartsWith("Закрыть окна") || header.StartsWith("Закрыть все") || header.StartsWith("Завершить") || header.StartsWith("Очистить") || header.StartsWith("Вернуть панель Windows")) it.Tag = "danger";
            // сначала снять перехват мыши: действие может надолго занять панель (запуск программы с правами администратора, например
            // Диспетчера задач), а с включённым перехватом каждое движение мыши во всей системе ждало бы панель
            it.Click += (s, e) => { CloseGuarded(); a(); };
            return it;
        }

        // Векторные значки погоды в стиле линейных иконок Fluent (сетка 24×24, линия 1.6) — вместо эмодзи
        static Geometry CloudGeo(double dy)
        {
            Geometry g = new EllipseGeometry(new Point(8, 14.5 + dy), 3.5, 3.5);
            g = Geometry.Combine(g, new EllipseGeometry(new Point(12.5, 11.5 + dy), 5, 5), GeometryCombineMode.Union, null);
            g = Geometry.Combine(g, new EllipseGeometry(new Point(16.5, 14.5 + dy), 3.5, 3.5), GeometryCombineMode.Union, null);
            return Geometry.Combine(g, new RectangleGeometry(new Rect(8, 14.5 + dy, 8.5, 3.5)), GeometryCombineMode.Union, null);
        }
        static Geometry SunGeo(Point c, double r, double ray)
        {
            var g = new GeometryGroup(); g.Children.Add(new EllipseGeometry(c, r, r));
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4, c1 = Math.Cos(a), s1 = Math.Sin(a);
                g.Children.Add(new LineGeometry(new Point(c.X + c1 * (r + 2), c.Y + s1 * (r + 2)), new Point(c.X + c1 * (r + 2 + ray), c.Y + s1 * (r + 2 + ray))));
            }
            return g;
        }
        static FrameworkElement WxIcon(string kind, double size)
        {
            var g = new GeometryGroup(); Geometry filled = null;
            Func<string, Geometry> P = Geometry.Parse;
            switch (kind)
            {
                case "sun": g.Children.Add(SunGeo(new Point(12, 12), 4, 2.5)); break;
                case "moon": g.Children.Add(P("M15,4.5 A7.5,7.5 0 1 0 19.5,15 A6,6 0 0 1 15,4.5 Z")); break;
                case "partly":
                case "partly-night":
                    // солнце/луна за облаком: обводку переводим в площадь и вырезаем по облаку с зазором
                    var back = kind == "partly" ? SunGeo(new Point(8.5, 8.5), 3, 1.6) : P("M10,3.5 A5,5 0 1 0 14.5,9 A4,4 0 0 1 10,3.5 Z");
                    var cloud = CloudGeo(2.5);
                    var area = back.GetWidenedPathGeometry(new Pen(Brushes.Black, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
                    var mask = Geometry.Combine(cloud, cloud.GetWidenedPathGeometry(new Pen(Brushes.Black, 4.6)), GeometryCombineMode.Union, null);
                    filled = Geometry.Combine(area, mask, GeometryCombineMode.Exclude, null);
                    g.Children.Add(cloud); break;
                case "fog": g.Children.Add(P("M4,9 H20 M6,13 H18 M4,17 H20")); break;
                case "drizzle": g.Children.Add(CloudGeo(-3)); g.Children.Add(P("M9,18 L8.5,20 M15,18 L14.5,20")); break;
                case "rain": g.Children.Add(CloudGeo(-3)); g.Children.Add(P("M8,18 L7,21.5 M12,18 L11,21.5 M16,18 L15,21.5")); break;
                case "snow": g.Children.Add(CloudGeo(-3)); foreach (var x in new[] { 8.0, 12.0, 16.0 }) g.Children.Add(new EllipseGeometry(new Point(x, 20), 0.7, 0.7)); break;
                case "storm": g.Children.Add(CloudGeo(-3)); g.Children.Add(P("M12.5,16 L10.5,19.5 H13.5 L11.5,23")); break;
                default: g.Children.Add(CloudGeo(0)); break;
            }
            var path = new System.Windows.Shapes.Path { Data = g, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            FgBind(path, Shape.StrokeProperty);
            var canvas = new Canvas { Width = 24, Height = 24 }; canvas.Children.Add(path);
            if (filled != null) canvas.Children.Add(FgShape(new System.Windows.Shapes.Path { Data = filled }, 1));
            return new Viewbox { Width = size, Height = size, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
        }

        // Единый вид полей ввода и флажков в карточках (стандартные белые поля выпадали из тёмных тем)
        static readonly ControlTemplate RoundedTextBox = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='TextBox'>" +
            "<Border Name='Bd' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>" +
            "<ScrollViewer Name='PART_ContentHost' Padding='{TemplateBinding Padding}' Focusable='False' VerticalScrollBarVisibility='{TemplateBinding ScrollViewer.VerticalScrollBarVisibility}' HorizontalScrollBarVisibility='{TemplateBinding ScrollViewer.HorizontalScrollBarVisibility}'/></Border>" +
            "<ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='{Binding SelectionBrush, RelativeSource={RelativeSource TemplatedParent}}'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        static void StyleInputs(DependencyObject root, Color fg)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                var tb = child as TextBox;
                if (tb != null)
                {
                    tb.Background = Frozen(fg, 0x14); tb.Foreground = Frozen(fg, 255); tb.CaretBrush = Frozen(fg, 255);
                    tb.BorderBrush = Frozen(fg, 0x40); tb.BorderThickness = new Thickness(1); tb.Padding = new Thickness(8, 6, 8, 6);
                    tb.SelectionBrush = Accent; tb.Template = RoundedTextBox; // углы 8 — как у карточек и переключателей, рамка фокуса берёт цвет акцента из SelectionBrush
                }
                var ctl = child as Control; if (ctl != null && !(child is TextBox)) ctl.Foreground = Frozen(fg, 255);
                StyleInputs(child, fg);
            }
        }

        readonly Dictionary<string, FrameworkElement> widgetEls = new Dictionary<string, FrameworkElement>();
        // новый порядок видимых виджетов, скрытые остаются на своих местах в списке
        public static List<KeyValuePair<string, bool>> MergeOrder(List<KeyValuePair<string, bool>> layout, List<string> visibleOrder)
        {
            var q = new Queue<string>(visibleOrder); var set = new HashSet<string>(visibleOrder);
            return layout.Select(l => set.Contains(l.Key) && q.Count > 0 ? new KeyValuePair<string, bool>(q.Dequeue(), true) : l).ToList();
        }
        // «ram,-cpu,…»: порядок; минус — скрыт. Новые виджеты из будущих версий добавляются в конец включёнными.
        public static List<KeyValuePair<string, bool>> ParseLayout(string v, IEnumerable<string> defaults)
        {
            var res = new List<KeyValuePair<string, bool>>(); var seen = new HashSet<string>();
            foreach (var raw in (v ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = raw.Trim(); var id = t.TrimStart('-');
                if (id != "" && seen.Add(id)) res.Add(new KeyValuePair<string, bool>(id, !t.StartsWith("-")));
            }
            foreach (var d in defaults) { var id = d.TrimStart('-'); if (seen.Add(id)) res.Add(new KeyValuePair<string, bool>(id, !d.StartsWith("-"))); } // «-id» — новый виджет, по умолчанию скрыт
            return res;
        }



        // Автозапуск: задание планировщика «при входе» (стартует раньше программ из автозагрузки и успевает занять трей), раздел Run — запасной путь, если задание создать не вышло
        static bool RunKeySet { get { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("FlowBar") != null; } }
        static void RunKeyClear() { using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.DeleteValue("FlowBar", false); }
        static string AutoStartExe { get { return Setup.IsInstalledCopy ? Setup.InstalledExe : Assembly.GetExecutingAssembly().Location; } }
        static bool AutoStart
        {
            get { return RunKeySet || Setup.LogonTaskExists(); }
            set
            {
                if (value)
                {
                    if (Setup.MakeLogonTask(AutoStartExe)) RunKeyClear();
                    else using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.SetValue("FlowBar", "\"" + AutoStartExe + "\"");
                }
                else { Setup.RemoveLogonTask(); RunKeyClear(); }
            }
        }
        // старый автозапуск (раздел Run) переводим на задание планировщика один раз; не вышло — остаётся как был
        static void MigrateAutoStart()
        {
            if (C("logonTaskTried", "") == "1" || !RunKeySet) return;
            cfg["logonTaskTried"] = "1"; SaveCfg();
            System.Threading.Tasks.Task.Run(() => { try { if (Setup.LogonTaskExists() || Setup.MakeLogonTask(AutoStartExe)) { RunKeyClear(); Info("автозапуск переведён на задание планировщика «" + Setup.LogonTask + "»"); } } catch (Exception ex) { Log(ex); } });
        }

        // ---------- стиль и позиция ----------
        void OnInit(object s, EventArgs e)
        {
            hwnd = new WindowInteropHelper(this).Handle;
            long ex = N.GetWindowLongPtr(hwnd, -20).ToInt64();
            N.SetWindowLongPtr(hwnd, -20, new IntPtr(ex | 0x08000000 | 0x80)); // не забирать фокус, не показываться в Alt+Tab
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            if (primary && !DemoMode) RegisterShellHookWindow(hwnd); // мигание окон «требует внимания»
            if (primary && !DemoMode) N.RegisterHotKey(hwnd, 1, 0x1 | 0x4000, 0x54); // Alt+T
            if (primary && !DemoMode) N.RegisterHotKey(hwnd, 2, 0x1 | 0x2 | 0x4000, 0x42); // Ctrl+Alt+B — перейти на панель с клавиатуры
            // стрелки и Tab ходят по плиткам по кругу, Esc возвращает в прежнее окно
            PreviewKeyDown += (o, ke) => { if (ke.Key == System.Windows.Input.Key.Escape && backTo != IntPtr.Zero) { N.SetForegroundWindow(backTo); backTo = IntPtr.Zero; ke.Handled = true; } };
            ApplyStyle();
            if (HideTb) SetTaskbar(false);
            InitAutoHide();
            if (primary && !WatchOk && !DemoMode) Dispatcher.BeginInvoke(new Action(() => Notify("Сторож не запустился", "Панель Windows оставлена видимой, чтобы не остаться без неё при сбое FlowBar. Перезапустите FlowBar.", true, "\uE7BA")), DispatcherPriority.ApplicationIdle);
        }
        IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x312 && w.ToInt32() == 1) { ShowTodos(); handled = true; }
            if (msg == 0x312 && w.ToInt32() == 2) { FocusBar(); handled = true; }
            if (msg == ShellHookMsg && primary)
            {
                int code = w.ToInt32();
                if (code == 0x8006 /* HSHELL_FLASH */) { if (l != N.GetForegroundWindow() && flashing.Add(l)) All(b => b.lastKey = null); if (IsConsent(l)) BringConsent(l); }
                else if ((code == 4 || code == 0x8004 /* окно стало активным */ || code == 2 /* закрыто */) && flashing.Remove(l)) All(b => b.lastKey = null);
                // активное окно сменилось — подложка переезжает сразу, а не при опросе раз в секунду
                if (code == 4 || code == 0x8004) Dispatcher.BeginInvoke(new Action(() => All(b => b.RefreshApps())));
            }
            if (msg == AppBarMsg && w.ToInt32() == 1) { reserveKey = null; Place(); } // ABN_POSCHANGED: соседние панели сдвинулись
            if (msg == TaskbarCreatedMsg && (DateTime.Now - Tray.Broadcast).TotalSeconds > 5) { appBar = false; reserveKey = null; Place(); if (HideTb) SetTaskbar(false); } // перезапуск Проводника: регистрация полосы потеряна
            if (msg == 0x02E0) Dispatcher.BeginInvoke(new Action(() => { reserveKey = null; Place(); })); // WM_DPICHANGED: монитор с другим масштабом
            if (msg == ShotMsg && primary) { SaveShots(w.ToInt32()); handled = true; }
            if (msg == OpenMsg && primary) { OpenByCode(w.ToInt32(), l.ToInt32()); handled = true; }
            return IntPtr.Zero;
        }

        // Окно подтверждения UAC (consent.exe), если Windows не вывела его вперёд, а лишь мигает кнопкой на скрытой панели Windows — человек его не видит. Выводим вперёд сами.
        static bool IsConsent(IntPtr h)
        {
            try { uint pid; N.GetWindowThreadProcessId(h, out pid); return string.Equals(Process.GetProcessById((int)pid).ProcessName, "consent", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
        static void BringConsent(IntPtr h)
        {
            Info("UAC: окно подтверждения мигает, а не показано — вывожу вперёд");
            N.ShowWindow(h, 9 /* SW_RESTORE */); N.SetForegroundWindow(h);
            if (N.GetForegroundWindow() != h) { N.keybd_event(0x12, 0, 0, UIntPtr.Zero); N.keybd_event(0x12, 0, 2, UIntPtr.Zero); N.SetForegroundWindow(h); } // «тычок» Alt даёт право вывести окно вперёд
        }
        IntPtr backTo;
        void FocusBar()
        {
            if (autoHidden) { autoHidden = false; ApplyVis(); }
            var fgw = N.GetForegroundWindow(); if (fgw != hwnd) backTo = fgw;
            Activate(); N.SetForegroundWindow(hwnd);
            root.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }

        // ---------- резервирование места (AppBar) ----------
        // Нужно, когда панель Windows видна: иначе FlowBar висит над ней и закрывает низ развёрнутых окон.
        // При скрытой панели Windows её полоса и так зарезервирована, FlowBar стоит в ней.
        static readonly uint AppBarMsg = N.RegisterWindowMessage("FlowBarAppBar"), TaskbarCreatedMsg = N.RegisterWindowMessage("TaskbarCreated");
        bool appBar; N.RECT reserved; string reserveKey;
        // место под панель: её высота и отступ от края экрана; со стороны окон отступа нет — развёрнутое окно заканчивается у самой панели
        void Reserve()
        {
            if (hwnd == IntPtr.Zero) return;
            bool want = !DemoMode && !AutoHide; // постоянная панель — окна обрезаются по ней, скрываемая — окна на весь экран (отдельного выключателя нет)
            int h = (int)Math.Ceiling((BarH + Gap) * MonScale);
            if (VerticalBar) h = (int)Math.Ceiling((SideWidth + Gap) * MonScale);
            var key = want + "|" + PositionKey + "|" + h + "|" + scr.Bounds;
            if (key == reserveKey) return;
            reserveKey = key;
            var d = new N.APPBARDATA { cbSize = Marshal.SizeOf(typeof(N.APPBARDATA)), hWnd = hwnd, uCallbackMessage = AppBarMsg };
            if (!want) { Unreserve(); return; }
            if (!appBar) { N.SHAppBarMessage(0 /* NEW */, ref d); appBar = true; }
            var b = scr.Bounds;
            d.uEdge = IsTop ? 1u : 3u;
            d.rc = new N.RECT { L = b.Left, R = b.Right, T = IsTop ? b.Top : b.Bottom - h, B = IsTop ? b.Top + h : b.Bottom };
            if (VerticalBar)
            {
                d.uEdge = LeftBar ? 0u : 2u;
                d.rc = new N.RECT { L = LeftBar ? b.Left : b.Right - h, R = LeftBar ? b.Left + h : b.Right, T = b.Top, B = b.Bottom };
            }
            N.SHAppBarMessage(2 /* QUERYPOS: Windows сдвинет полосу с чужих панелей */, ref d);
            if (VerticalBar) { if (LeftBar) d.rc.R = d.rc.L + h; else d.rc.L = d.rc.R - h; }
            else if (IsTop) d.rc.B = d.rc.T + h; else d.rc.T = d.rc.B - h;
            N.SHAppBarMessage(3 /* SETPOS */, ref d);
            reserved = d.rc;
        }
        // Проводник на новых сборках Windows 11 принимает положение полосы (AppBar), но при скрытой панели Windows не всегда уменьшает рабочую
        // область — развёрнутые окна уходили под FlowBar. Пока панель Windows скрыта, задаём рабочую область монитора сами и держим её (раз в секунду).
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint p, ref N.RECT r, uint flags);
        bool workSet;
        // Проводнику и окнам — «рабочая область изменилась», не дожидаясь ответа каждого окна.
        // SPIF_SENDCHANGE рассылает синхронно: зависшее окно задерживало выход на секунды, а с открытым меню (перехват мыши) замирала мышь
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SendNotifyMessage(IntPtr h, uint m, IntPtr w, string l);
        static void WorkAreaChanged() { SendNotifyMessage(new IntPtr(0xFFFF), 0x1A /* WM_SETTINGCHANGE */, new IntPtr(0x2F), null); }
        void EnsureWorkArea()
        {
            if (hwnd == IntPtr.Zero || DemoMode) return; // пример для сверки (--demo) рабочую область не трогает: убитый процесс не вернул бы её, и окна оставались бы без полосы внизу
            var mi = new MonInfo { cbSize = Marshal.SizeOf(typeof(MonInfo)) }; if (!GetMonitorInfo(mon, ref mi)) return;
            var m = mi.rcMonitor; var w = m; var cur = mi.rcWork;
            if (!(!AutoHide && HideTb))
            {
                // скрываемая панель: окна на весь экран. Область, оставшаяся от прежнего запуска (убитый процесс, сбой), возвращается целиком, а не только если её поставили мы
                bool stale = AutoHide && HideTb && !workSet && (cur.L != m.L || cur.T != m.T || cur.R != m.R || cur.B != m.B);
                if (stale) Info("рабочая область " + scr.DeviceName + " урезана до " + cur.L + "," + cur.T + "–" + cur.R + "," + cur.B + " при скрываемой панели — возвращаем экран целиком");
                if (workSet || stale) { workSet = false; SystemParametersInfo(0x2F /* SPI_SETWORKAREA */, 0, ref m, 0); WorkAreaChanged(); }
                return;
            }
            int h = (int)Math.Ceiling((BarH + Gap) * MonScale);
            if (VerticalBar)
            {
                h = (int)Math.Ceiling((SideWidth + Gap) * MonScale);
                if (LeftBar) w.L = m.L + h; else w.R = m.R - h;
            }
            else if (IsTop) w.T = m.T + h; else w.B = m.B - h;
            if (cur.L == w.L && cur.T == w.T && cur.R == w.R && cur.B == w.B) { workSet = true; return; }
            if (!workSet) Info("рабочая область " + scr.DeviceName + " была " + cur.L + "," + cur.T + "–" + cur.R + "," + cur.B + " — ставим " + w.L + "," + w.T + "–" + w.R + "," + w.B);
            // без SPIF_SENDCHANGE: на рассылку WM_SETTINGCHANGE Проводник пересчитывает область по-своему и сразу затирает её — получалась перетяжка каждую секунду
            workSet = true; SystemParametersInfo(0x2F, 0, ref w, 0);
        }
        public void Unreserve()
        {
            if (workSet) { workSet = false; var mi = new MonInfo { cbSize = Marshal.SizeOf(typeof(MonInfo)) }; if (GetMonitorInfo(mon, ref mi)) { SystemParametersInfo(0x2F, 0, ref mi.rcMonitor, 0); WorkAreaChanged(); } } // вернуть экран целиком; панель Windows затем займёт своё место сама
            if (!appBar || hwnd == IntPtr.Zero) return;
            var d = new N.APPBARDATA { cbSize = Marshal.SizeOf(typeof(N.APPBARDATA)), hWnd = hwnd };
            N.SHAppBarMessage(1 /* REMOVE */, ref d); appBar = false; reserveKey = null;
        }

        // ---------- мониторы: подключение, отключение, смена основного ----------
        static string screensKey;
        static DispatcherTimer screensT;
        static string ScreensKey() { return string.Join(";", System.Windows.Forms.Screen.AllScreens.Select(x => x.DeviceName + x.Bounds + x.Primary)); }
        public static void WatchScreens()
        {
            screensKey = ScreensKey();
            screensT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) }; // Windows шлёт несколько событий подряд — ждём, пока утихнет
            screensT.Tick += (o, e) =>
            {
                screensT.Stop();
                var k = ScreensKey(); if (k == screensKey) return;
                screensKey = k;
                foreach (var b in bars.ToList()) b.Close();
                foreach (var scr in System.Windows.Forms.Screen.AllScreens) new Bar(scr).Show();
                Info("мониторы изменились:\r\n" + SysInfo());
            };
            var disp = Dispatcher.CurrentDispatcher;
            SystemEvents.DisplaySettingsChanged += (o, e) => disp.BeginInvoke(new Action(() => { screensT.Stop(); screensT.Start(); }));
        }


        void Fullscreen()
        {
            var f = N.GetForegroundWindow(); bool full = false; N.RECT r; var b = scr.Bounds;
            if (f != IntPtr.Zero && f != hwnd && !N.IsZoomed(f) && N.GetWindowRect(f, out r))
            {
                var c = Cls(f);
                full = c != "Progman" && c != "WorkerW" && r.L <= b.Left && r.T <= b.Top && r.R >= b.Right && r.B >= b.Bottom;
            }
            fsHidden = full; ApplyVis();
        }

        double activeX = double.NaN; // где была подложка активного значка
        List<string> runOrder = new List<string>();
        void RefreshApps()
        {
            if (dragging) return; // перестройка панели оборвала бы перетаскивание
            RefreshAppsFrom(GetWindows());
        }
        void RefreshAppsFrom(List<Win> wins)
        {
            var present = new HashSet<string>(wins.Select(w => Key(w.Path)));
            runOrder.RemoveAll(p => !present.Contains(p));
            foreach (var w in wins) { var k = Key(w.Path); if (!runOrder.Contains(k)) runOrder.Add(k); }
            var groups = wins.GroupBy(w => Key(w.Path)).ToDictionary(g => g.Key, g => g.OrderBy(w => w.H.ToInt64()).ToList());
            var fg = N.GetForegroundWindow();

            var key = GroupsKey() + "#" + string.Join("|", pinned) + "#" + string.Join("|", runOrder.Select(k => k + ":" + string.Join(",", groups[k].Select(w => w.H)))) + "#" + fg + "#" + string.Join(",", allWins.Where(w => w.Mon != mon).Select(w => w.H)) + "#" + string.Join(",", wins.Select(w => BadgeOf(TitleOf(w.H), w.Path))) + "#" + flashing.Count;
            if (C("apps.progress", "0") == "1") key += "#" + Tray.ProgressVersion;
            if (key == lastKey) return;
            lastKey = key;

            // Tag плитки: "p|путь" — закреплённая, "r|ключ" — просто запущенная; порядок задаёт appOrder (перетаскивание)
            var tiles = new List<Border>();
            var used = new HashSet<string>(); var wsOf = new Dictionary<string, List<Win>>();
            bool showPins = !CompactMonitor && (primary || bars.Count < 2 || C("pinsAll", "1") == "1");
            foreach (var p in pinned)
            {
                if (p.StartsWith("group:"))
                {
                    List<string> gm; if (appGroups.TryGetValue(p.Substring(6), out gm)) foreach (var m in gm) used.Add(Key(Target(m)));
                    var gws = (gm ?? new List<string>()).SelectMany(x => { List<Win> gw; return groups.TryGetValue(Key(Target(x)), out gw) ? gw : new List<Win>(); }).ToList();
                    if (!showPins && gws.Count == 0) continue;
                    var gt = GroupTile(p.Substring(6), groups, fg); gt.Tag = "p|" + p; tiles.Add(gt); wsOf["p|" + p] = gws;
                    continue;
                }
                var k = Key(Target(p)); List<Win> ws;
                if (used.Contains(k) || !groups.TryGetValue(k, out ws)) ws = new List<Win>();
                used.Add(k);
                if (!showPins && ws.Count == 0) continue;
                var t = AppTile(p, ws, true, fg); t.Tag = "p|" + p; tiles.Add(t); wsOf["p|" + p] = ws;
            }
            foreach (var k in runOrder) if (!used.Contains(k)) { var t = AppTile(groups[k][0].Path, groups[k], false, fg); t.Tag = "r|" + k; tiles.Add(t); wsOf["r|" + k] = groups[k]; }
            foreach (var t in tiles) if (!appOrder.Contains((string)t.Tag)) appOrder.Add((string)t.Tag); // новые — в конец, как в Windows
            // в общий ряд панели: место каждого значка — в порядке ряда этого монитора
            appEls = tiles.ToDictionary(t => (string)t.Tag, t => (FrameworkElement)t); appWins = wsOf;
            Compose();
        }

        // счётчик уведомлений из заголовков окон; true — какое-то окно мигает и требует внимания. Общее для значка программы и группы
        bool AddBadge(Grid grid, List<Win> ws)
        {
            int badge = ws.Sum(w => BadgeOf(TitleOf(w.H), w.Path)); bool flash = ws.Any(w => flashing.Contains(w.H));
            if (badge > 0 && C("badges", "1") == "1")
            {
                var bt = new TextBlock { Text = badge > 99 ? "99+" : badge.ToString(), FontSize = Math.Round(8.5 * Fs), FontWeight = FontWeights.SemiBold, Foreground = Frozen(OnAccent, 255), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3.5, 0, 3.5, 0.5) };
                double bh = Math.Round(13 * Fs);
                grid.Children.Add(new Border { Child = bt, Background = Accent, CornerRadius = new CornerRadius(bh / 2), MinWidth = bh, Height = bh, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, -4, 0), IsHitTestVisible = false });
            }
            return flash;
        }
        // полоска прогресса окон (загрузка, копирование) под значком и подсказка с процентом
        void AddProgress(Grid grid, Border tile, List<Win> ws, IntPtr fg, string name)
        {
            var progress = ProgressFor(ws, fg);
            if (progress == null) return;
            if (progress.Paused || progress.Indeterminate)
                grid.Children.Add(new Line { X1 = 0, X2 = Math.Max(3, IconPx * (progress.Indeterminate ? 0.45 : progress.Value)), Y1 = 1, Y2 = 1,
                    Stroke = progress.Paused ? Frozen(COrange, 255) : Accent,
                    StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 1, 1 }, VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false });
            else
            {
                var meter = new FillBar(IconPx, 3) { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
                meter.Set(progress.Value, progress.Error ? Frozen(Color.FromRgb(245, 99, 104), 255) : Accent, true);
                grid.Children.Add(meter);
            }
            tile.ToolTip = name + " · " + (progress.Indeterminate ? "Выполняется" : Math.Round(progress.Value * 100) + "%")
                + (progress.Paused ? " · На паузе" : progress.Error ? " · Ошибка" : "");
        }

        Border AppTile(string path, List<Win> ws, bool isPinned, IntPtr fg)
        {
            var grid = new Grid();
            var icon = IconFor(path) ?? IconFor(Target(path ?? "")); // у ярлыка — его значок (как на панели Windows)
            if (icon != null)
            {
                var img = new Image { Source = icon, Width = IconPx, Height = IconPx };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                grid.Children.Add(img);
            }
            else grid.Children.Add(G(path != null && Directory.Exists(path) ? "" : ""));
            if (ws.Count > 1 && C("apps.stack", "0") == "1")
            {
                for (int i = 2; i >= 1; i--)
                {
                    var page = new Border { Width = IconPx, Height = IconPx, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
                        BorderBrush = Frozen(Cur.Fg, (byte)(0x60 + (2 - i) * 0x20)), Background = Frozen(Cur.Bg, 230),
                        RenderTransform = new TranslateTransform(i * 2, -i * 2), IsHitTestVisible = false };
                    grid.Children.Insert(0, page);
                }
            }
            bool active = ws.Any(w => w.H == fg);
            int local = ws.Count(w => w.Mon == mon), remote = Elsewhere(path).Count;
            if (local + remote > 0) grid.Children.Add(Indicator(ws.Any(w => w.Mon == mon && w.H == fg), local, remote));
            bool flash = AddBadge(grid, ws);
            string name = PinName(path);
            var tile = Tile(grid, ws.Count == 0 ? name : null, () => Activate(path, ws), m => AppMenu(m, path, ws, isPinned));
            System.Windows.Automation.AutomationProperties.SetItemStatus(tile, WindowIndicatorStatus(local, remote));
            AddProgress(grid, tile, ws, fg, name);
            if (active)
            {
                // активное приложение — мягкая подложка; при смене окна она «переезжает» от прежнего значка к новому
                var hl = new Border { Background = new SolidColorBrush(Fa(0x1E)), CornerRadius = tile.CornerRadius, Margin = new Thickness(-40, -40, -40, -40), Width = tile.Width, Height = tile.Height, IsHitTestVisible = false, RenderTransform = new TranslateTransform() };
                grid.Children.Insert(0, hl);
                tile.Loaded += (s, e) =>
                {
                    double x; try { x = tile.PointToScreen(new Point(tile.ActualWidth / 2, 0)).X / Scale; } catch { return; }
                    double from = activeX; activeX = x;
                    if (!Motion || double.IsNaN(from) || Math.Abs(from - x) < 2 || Math.Abs(from - x) > 1600) return;
                    ((TranslateTransform)hl.RenderTransform).BeginAnimation(TranslateTransform.XProperty, new System.Windows.Media.Animation.DoubleAnimation(from - x, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
                };
            }
            else if (flash) { tile.Base = Color.FromArgb(0x70, 0xF0, 0x90, 0x20); tile.Background = new SolidColorBrush(tile.Base); } // окно мигает — требует внимания
            tile.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Middle && path != null) Launch(path); };
            if (ws.Count == 0 && remote > 0)
                tile.ToolTip = name + "\n" + WindowIndicatorStatus(local, remote) + "\nКлик — переключиться. Shift+клик или средняя кнопка — новое окно.";
            tile.MouseEnter += (s, e) => HoverStart(tile, ws.Count == 0 && remote > 1 ? Elsewhere(path) : ws);
            tile.AllowDrop = true;
            // файл тянут над значком открытой программы — её окно сразу выходит вперёд, и файл можно бросить прямо в него (как в панели Windows)
            DispatcherTimer spring = null;
            tile.DragEnter += (s, e) =>
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop) || ws.Count == 0) return;
                if (spring != null) spring.Stop();
                spring = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) }; // почти сразу; пауза только чтобы окна не мелькали, когда файл проносят вдоль панели
                spring.Tick += (o2, e2) => { spring.Stop(); if (ws.Count == 1) ActivateWin(ws[0].H); else ShowPreview(tile, ws, true); };
                spring.Start();
            };
            tile.DragOver += (s, e) => { if (preview != null && previewFor == tile) previewMiss = 0; };
            tile.DragLeave += (s, e) => { if (spring != null) spring.Stop(); };

            tile.Drop += (s, e) =>
            {
                if (spring != null) spring.Stop();
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                // ярлык или программа, брошенные на значок, закрепляются на его месте
                if (files != null && !Locked && files.All(IsProgram))
                {
                    e.Handled = true;
                    int at = Math.Max(0, appOrder.IndexOf((string)tile.Tag));
                    int si = seq == null ? -1 : seq.IndexOf((string)tile.Tag);
                    foreach (var f in files.Where(f => !pinned.Contains(f)))
                    {
                        pinned.Add(f); appOrder.Remove("p|" + f); appOrder.Insert(at++, "p|" + f);
                        if (si >= 0) { seq.Remove("p|" + f); seq.Insert(si++, "p|" + f); } // на место значка, на который бросили
                    }
                    if (si >= 0) SaveSeq();
                    SavePinned(); return;
                }
                // остальные файлы, брошенные на значок программы, открываются в ней
                var exe = path == null ? null : Target(path);
                if (files != null && exe != null && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    e.Handled = true;
                    Launch(exe, string.Join(" ", files.Select(f => "\"" + f + "\"")));
                }
            };
            return tile;
        }


        void Activate(string path, List<Win> ws)
        {
            if ((DateTime.Now - lastDragEnd).TotalMilliseconds < 400) return; // отпускание после перетаскивания — не клик
            bool wasOpen = preview != null && previewFor == anchor;
            if (wasOpen && previewFolding) { ClosePreview(); wasOpen = false; } // клик во время сворачивания не теряется: закрываем сразу и обрабатываем как обычный клик (у ярлыка с окнами веер раскроется снова)
            if (wasOpen && previewPinned && (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) == 0)
            {
                FoldPreview();
                return;
            }
            if (wasOpen && !previewPinned && foldPreview != null && (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) == 0)
            {
                previewPinned = true;
                previewMiss = 0;
                if (spreadPreview != null) spreadPreview();
                return;
            }
            ClosePreview();
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0, ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (shift && path != null) { Launch(path); return; } // как в Windows: Shift+клик — новое окно
            var fg = N.GetForegroundWindow();
            if (ws.Count == 0)
            {
                var other = Elsewhere(path);
                if (other.Count > 1 && !ctrl && anchor is Border) ShowPreview((Border)anchor, other, true);
                else if (other.Count > 0) { int j = other.FindIndex(w => w.H == fg); ActivateWin(other[(j + 1) % other.Count].H); }
                else if (path != null) Launch(path);
                return;
            }
            int i = ws.FindIndex(w => w.H == fg);
            if (ws.Count == 1) { if (i == 0) N.ShowWindow(ws[0].H, 6); else ActivateWin(ws[0].H); return; } // повторный клик — свернуть
            if (ctrl) { ActivateWin(ws[(i + 1) % ws.Count].H); return; } // Ctrl+клик — по кругу
            // несколько окон — как в Windows: клик показывает миниатюры, выбор окна — по ним
            Border tile = anchor as Border;
            if (tile != null) ShowPreview(tile, ws, true); else ActivateWin(ws[(i + 1) % ws.Count].H);
        }
        static void ActivateWin(IntPtr h)
        {
            if (N.IsIconic(h)) N.ShowWindow(h, 9);
            if (N.SetForegroundWindow(h) && N.GetForegroundWindow() == h) return;
            Keys(0x12); N.SetForegroundWindow(h); // Windows отказала (передний план у другой программы): нажатие Alt снимает запрет
        }

        void AppMenu(ContextMenu m, string path, List<Win> ws, bool isPinned)
        {
            ClosePreview();
            var shown = ws.Concat(Elsewhere(path)).Where(w => N.IsWindow(w.H))
                .GroupBy(w => w.H).Select(g => g.First()).OrderBy(w => w.Mon != mon).ToList();
            var local = shown.Where(w => w.Mon == mon).ToList();
            var closable = local.Count > 0 ? local : shown;
            var hp = path ?? (shown.Count > 0 ? shown[0].Path : null);
            if (hp != null)
            {
                string detail = shown.Count == 0 ? null : "Окна: " + shown.Count + " · " + (local.Count == shown.Count ? "на этом экране" :
                    local.Count == 0 ? "на других экранах" : local.Count + " здесь, " + (shown.Count - local.Count) + " на других");
                m.Items.Add(MenuHead(IconFor(hp) ?? IconFor(Target(hp)), PinName(hp), detail));
            }
            var windows = MenuBranch("Окна (" + shown.Count + ")");
            foreach (var w in shown)
            {
                var h = w.H;
                var title = TitleOf(h);
                if (string.IsNullOrWhiteSpace(title)) title = PinName(w.Path);
                var label = title + (w.Mon != mon ? " · другой экран" : "");
                var wi = Item("Окна (", () => { if (N.IsWindow(h)) ActivateWin(h); });
                var titleLine = new TextBlock { Text = title, MaxWidth = 255, TextTrimming = TextTrimming.CharacterEllipsis };
                var close = Cross("Закрыть окно «" + title + "»", () =>
                {
                    CloseGuarded();
                    if (N.IsWindow(h)) N.PostMessage(h, 0x10, IntPtr.Zero, IntPtr.Zero);
                });
                close.Width = close.Height = 20; close.Margin = new Thickness(8, 0, 0, 0);
                close.PreviewMouseLeftButtonDown += (s, e) => e.Handled = true;
                close.PreviewMouseLeftButtonUp += (s, e) => { e.Handled = true; close.Invoke(); };
                var row = new DockPanel(); DockPanel.SetDock(close, Dock.Right); row.Children.Add(close);
                row.Children.Add(w.Mon == mon ? (UIElement)titleLine : VS(titleLine,
                    new TextBlock { Text = "Другой экран", FontSize = 10.5, Opacity = 0.72, Margin = new Thickness(0, 2, 0, 0) }));
                wi.Header = row;
                wi.ToolTip = label;
                System.Windows.Automation.AutomationProperties.SetName(wi, label);
                windows.Items.Add(wi);
            }
            MenuSection(m, windows.Items.Count == 1 ? TakeOnlyMenuItem(windows) : windows.Items.Count > 1 ? windows : null,
                path == null ? null : Item(shown.Count == 0 ? "Открыть" : "Открыть новое окно", () => Launch(path)));
            // при закреплении/откреплении значок остаётся на своём месте
            if (path != null)
                MenuSection(m, isPinned
                    ? Item("Открепить", () => { Retag("p|" + path, "r|" + Key(Target(path))); pinned.Remove(path); SavePinned(); })
                    : Item("Закрепить", () => { Retag("r|" + Key(path), "p|" + path); pinned.Add(path); SavePinned(); }),
                    isPinned ? GroupMenuItem(path) : null);
            if (closable.Count > 0)
            {
                string closeLabel = closable.Count == 1 ? "Закрыть окно" : "Закрыть окна (" + closable.Count + ")";
                if (local.Count != shown.Count) closeLabel += local.Count == 0 ? " на других экранах" : " здесь";
                MenuSection(m, Item(closeLabel, () =>
                    { foreach (var w in closable) if (N.IsWindow(w.H)) N.PostMessage(w.H, 0x10, IntPtr.Zero, IntPtr.Zero); }));
            }
        }

        static MenuItem TakeOnlyMenuItem(MenuItem parent)
        {
            var item = (MenuItem)parent.Items[0];
            parent.Items.Clear();
            return item;
        }

        // ---------- миниатюры окон ----------
        void HoverStart(Border tile, List<Win> ws)
        {
            if (ws.Count == 0) return;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            t.Tick += (s, e) => { t.Stop(); if (!dragging && tile.IsMouseOver && previewFor != tile) ShowPreview(tile, ws); };
            t.Start();
        }
        bool previewPinned;
        Action foldPreview, spreadPreview;
        bool previewFolding; // веер сворачивается (≈210 мс): окно ещё открыто, но уже закрывается

        void FoldPreview()
        {
            if (foldPreview != null && Motion) foldPreview();
            else ClosePreview();
        }

        FrameworkElement PreviewFolderBase(string name, TextBlock count, bool vertical, Action close)
        {
            var body = new Grid { Height = 49, Width = vertical ? 104 : 186 };
            var back = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M0,12 Q0,7 7,7 H52 Q58,7 63,3 Q66,0 73,0 H153 Q160,0 160,7 V42 Q160,49 153,49 H7 Q0,49 0,42 Z"),
                Fill = Frozen(AccentColor, 0x27), Stroke = Frozen(AccentColor, 0x55), Stretch = Stretch.Fill, IsHitTestVisible = false
            };
            var front = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M0,7 Q0,0 7,0 H46 Q52,0 57,5 L61,8 Q64,10 70,10 H153 Q160,10 160,17 V42 Q160,49 153,49 H7 Q0,49 0,42 Z"),
                Fill = new LinearGradientBrush(Frozen(CardBg, 0xE8).Color, Frozen(CardBg, 0xC7).Color, 90),
                Stroke = Frozen(CardFg, 0x46), Stretch = Stretch.Fill, IsHitTestVisible = false
            };
            body.Children.Add(back);
            body.Children.Add(front);
            var caption = Txt(name, 12, true);
            caption.TextTrimming = TextTrimming.CharacterEllipsis;
            caption.MaxWidth = vertical ? 78 : 220;
            var labels = VS(caption, count);
            var button = Cross("Свернуть предпросмотр", close);
            button.Width = button.Height = 23;
            button.Background = Frozen(CardFg, 0x17);
            var line = new DockPanel { Margin = new Thickness(12, 10, 8, 5) };
            DockPanel.SetDock(button, Dock.Right);
            line.Children.Add(button);
            line.Children.Add(labels);
            body.Children.Add(line);
            return body;
        }

        void ShowPreview(Border tile, List<Win> ws, bool pinned = false)
        {
            ClosePreview(); previewPinned = pinned; TipHide(true);
            var windows = ws.Where(item => N.IsWindow(item.H)).Take(12).ToList(); // было 6: окна сверх шести молча пропадали из миниатюр; высота ячеек сама уменьшается под число рядов
            if (windows.Count == 0) return;
            bool folder = windows.Count > 1;
            const double padding = 8, margin = 4;
            double available = scr.Bounds.Width / MonScale - 2 * (padding + 5);
            if (folder && VerticalBar) available -= 112;
            double span = Math.Min(folder ? 226 : 244, Math.Max(105, available));
            int columns = Math.Min(windows.Count, Math.Max(1, (int)(available / span)));
            int rows = (windows.Count + columns - 1) / columns;
            double panelSpace = VerticalBar ? 0 : BarH;
            double slotHeight = Math.Min(136, Math.Max(folder ? 42 : 54, Math.Floor((scr.Bounds.Height / MonScale - panelSpace - (folder ? 95 : 32)) / rows - 46)));
            var w = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
                SizeToContent = SizeToContent.WidthAndHeight, AllowsTransparency = true, Background = Brushes.Transparent, Foreground = Frozen(CardFg, 255)
            };
            StylePopupText(w);
            var row = new WrapPanel { Width = span * columns };
            var slots = new List<Tuple<Border, IntPtr>>();
            var transforms = new Dictionary<Border, TransformGroup>();
            // закрытое из миниатюр окно (вкладка) пропадает из окна предпросмотра, остальные остаются; пусто — окно закрывается
            var cells = new Dictionary<IntPtr, Border>(); Action<IntPtr> dismiss = null;
            foreach (var win in windows)
            {
                var h = win.H;
                var title = TitleOf(h);
                if (string.IsNullOrWhiteSpace(title)) title = PinName(win.Path);
                var close = Cross("Закрыть окно", () => { N.PostMessage(h, 0x10, IntPtr.Zero, IntPtr.Zero); dismiss(h); });
                close.MouseEnter += (s, e) => close.Background = Frozen(CardFg, 0x20);
                close.MouseLeave += (s, e) => close.Background = Brushes.Transparent;
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
                DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
                var image = IconFor(win.Path);
                if (image != null)
                {
                    var icon = new Image { Source = image, Width = 16, Height = 16, Margin = new Thickness(0, 0, 7, 0) };
                    DockPanel.SetDock(icon, Dock.Left); head.Children.Add(icon);
                }
                var label = Txt(title, 12, true); label.ToolTip = title; head.Children.Add(label);
                var slot = new Border { Height = slotHeight, Margin = new Thickness(3), Background = Frozen(CardFg, 0x09) };
                var frame = new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), BorderBrush = Frozen(CardFg, 0x20), Child = slot };
                var cell = Act(VS(head, frame), title, () => { ClosePreview(); ActivateWin(h); });
                cell.Width = span - margin * 2; cell.Margin = new Thickness(margin); cell.Padding = new Thickness(7);
                cell.CornerRadius = new CornerRadius(12); cell.BorderThickness = new Thickness(1);
                var restFill = folder ? PopupFill(false) : Frozen(CardFg, 0x0A);
                var restEdge = Frozen(CardFg, folder ? (byte)0x38 : (byte)0x16);
                cell.Background = restFill; cell.BorderBrush = restEdge;
                if (folder)
                {
                    cell.RenderTransformOrigin = new Point(0.5, 0.5);
                    var transform = new TransformGroup();
                    transform.Children.Add(new ScaleTransform(1, 1));
                    transform.Children.Add(new TranslateTransform());
                    cell.RenderTransform = transform;
                    transforms.Add(cell, transform);
                }
                // файл над миниатюрой — это окно выходит вперёд
                cell.AllowDrop = true; DispatcherTimer cs = null;
                cell.DragEnter += (s, e) => { previewMiss = 0; if (cs != null) cs.Stop(); cs = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) }; cs.Tick += (o2, e2) => { cs.Stop(); ClosePreview(); ActivateWin(h); }; cs.Start(); };
                cell.DragOver += (s, e) => { previewMiss = 0; e.Effects = DragDropEffects.None; e.Handled = true; }; cell.DragLeave += (s, e) => { if (cs != null) cs.Stop(); };
                cell.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Middle) { e.Handled = true; N.PostMessage(h, 0x10, IntPtr.Zero, IntPtr.Zero); dismiss(h); } }; // средний клик — закрыть окно
                cells[h] = cell;
                cell.MouseEnter += (s, e) => { if (!folder) cell.Background = Frozen(AccentColor, 0x20); cell.BorderBrush = Frozen(AccentColor, 0x7A); };
                cell.MouseLeave += (s, e) => { cell.Background = restFill; cell.BorderBrush = restEdge; };
                row.Children.Add(cell); slots.Add(Tuple.Create(slot, h));
            }
            var count = folder ? Txt("Окон: " + cells.Count, 10, false) : null;
            if (count != null) count.Opacity = 0.72;
            FrameworkElement content = row;
            FrameworkElement folderBase = null;
            var folderOffset = new TranslateTransform();
            if (folder)
            {
                var layout = new DockPanel();
                folderBase = PreviewFolderBase(PinName(windows[0].Path), count, VerticalBar, FoldPreview);
                folderBase.Margin = VerticalBar ? new Thickness(4, 4, 4, 4) : new Thickness(14, 3, 14, 3);
                folderBase.HorizontalAlignment = HorizontalAlignment.Center;
                folderBase.VerticalAlignment = VerticalAlignment.Center;
                folderBase.RenderTransform = folderOffset;
                DockPanel.SetDock(folderBase, VerticalBar ? (LeftBar ? Dock.Left : Dock.Right) : IsTop ? Dock.Top : Dock.Bottom);
                layout.Children.Add(folderBase);
                layout.Children.Add(row);
                content = layout;
            }
            var face = new Border { Child = content, Padding = new Thickness(padding), Background = folder ? Brushes.Transparent : CardFill, BorderBrush = folder ? Brushes.Transparent : CardEdge,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16) };
            w.Content = face;
            w.SourceInitialized += (s, e) =>
            {
                var ph = new WindowInteropHelper(w).Handle;
                N.SetWindowLongPtr(ph, -20, new IntPtr(N.GetWindowLongPtr(ph, -20).ToInt64() | 0x08000000 | 0x80));
            };
            var previews = new List<Tuple<Border, IntPtr, N.POINT, Border>>();
            Action updateThumbnails = () =>
            {
                if (!w.IsVisible) return;
                double sc = VisualTreeHelper.GetDpi(w).DpiScaleX;
                foreach (var entry in previews)
                {
                    var o = entry.Item1.TranslatePoint(new Point(), w);
                    var end = entry.Item1.TranslatePoint(new Point(entry.Item1.ActualWidth, entry.Item1.ActualHeight), w);
                    double bw = (end.X - o.X) * sc, bh = (end.Y - o.Y) * sc;
                    double k = Math.Min(1, Math.Min(bw / entry.Item3.X, bh / entry.Item3.Y));
                    double dw = entry.Item3.X * k, dh = entry.Item3.Y * k;
                    int left = (int)Math.Round(o.X * sc + (bw - dw) / 2), top = (int)Math.Round(o.Y * sc + (bh - dh) / 2);
                    double opacity = entry.Item4.Opacity;
                    var props = new N.THUMBPROPS { Flags = 1 | 4 | 8, Dest = new N.RECT { L = left, T = top, R = left + (int)Math.Round(dw), B = top + (int)Math.Round(dh) },
                        Opacity = (byte)Math.Round(opacity * 255), Visible = opacity > 0.01 && dw >= 1 && dh >= 1 ? 1 : 0 };
                    N.DwmUpdateThumbnailProperties(entry.Item2, ref props);
                }
            };
            double progress = 0, motionFrom = 0, motionTo = pinned ? 1 : 0.76, motionDuration = 385;
            Action<double> arrangeFan = null;
            EventHandler animate = null; // задан только если веер открыт с движением
            Action<bool> position = refresh =>
            {
                if (!w.IsVisible || !tile.IsLoaded) return;
                double sc = VisualTreeHelper.GetDpi(w).DpiScaleX, gap = Math.Round(7 * sc);
                double width = w.ActualWidth * sc, height = w.ActualHeight * sc;
                var center = tile.PointToScreen(new Point(tile.ActualWidth / 2, tile.ActualHeight / 2));
                var start = root.PointToScreen(new Point()); var end = root.PointToScreen(new Point(root.ActualWidth, root.ActualHeight));
                var bounds = scr.Bounds;
                double x = VerticalBar ? (LeftBar ? end.X + gap : start.X - width - gap) : center.X - width / 2;
                double y = VerticalBar ? center.Y - height / 2 : IsTop ? end.Y + gap : start.Y - height - gap;
                int left = (int)Math.Round(Math.Max(bounds.Left + 4, Math.Min(bounds.Right - 4 - width, x)));
                int top = (int)Math.Round(Math.Max(bounds.Top + 4, Math.Min(bounds.Bottom - 4 - height, y)));
                var handle = new WindowInteropHelper(w).Handle;
                N.RECT rect;
                if (N.GetWindowRect(handle, out rect) && (rect.L != left || rect.T != top))
                    N.SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0, 0x1 | 0x4 | 0x10);
                if (folder)
                {
                    // Окно держит миниатюры на экране. Папка внутри него выравнивается по ярлыку отдельно.
                    var origin = content.TranslatePoint(new Point(), w);
                    var at = VisualTreeHelper.GetOffset(folderBase);
                    double room = VerticalBar ? content.ActualHeight : content.ActualWidth;
                    double size = VerticalBar ? folderBase.ActualHeight : folderBase.ActualWidth;
                    double desired = VerticalBar ? (center.Y - top) / sc - origin.Y : (center.X - left) / sc - origin.X;
                    double offset = Math.Max(0, Math.Min(room - size, desired - size / 2)) - (VerticalBar ? at.Y : at.X);
                    double previous = VerticalBar ? folderOffset.Y : folderOffset.X;
                    if (Math.Abs(offset - previous) >= 0.1)
                    {
                        if (VerticalBar) folderOffset.Y = offset;
                        else folderOffset.X = offset;
                        refresh = true;
                    }
                }
                if (refresh)
                {
                    if (folder && animate != null) arrangeFan(progress); // без анимации (движение было выключено при открытии) карточки лежат как есть: arrangeFan(0) спрятал бы их
                    else updateThumbnails();
                }
            };
            // DWM рисует содержимое окон отдельно. Двигаем его вместе с WPF-карточками.
            var motionWatch = new Stopwatch();
            bool foldingFolder = false;
            arrangeFan = value =>
            {
                if (folderBase == null || !folderBase.IsLoaded) return;
                var mouth = folderBase.TranslatePoint(new Point(folderBase.ActualWidth / 2, folderBase.ActualHeight / 2), w);
                for (int i = 0; i < row.Children.Count; i++)
                {
                    var cell = (Border)row.Children[i];
                    var transform = transforms[cell];
                    var scale = (ScaleTransform)transform.Children[0];
                    var shift = (TranslateTransform)transform.Children[1];
                    double fraction = (i + 0.5) / row.Children.Count - 0.5;
                    double delay = Math.Min(0.22, (row.Children.Count - 1 - i) * 0.045);
                    double t = Math.Max(0, Math.Min(1, (value - delay) / (1 - delay)));
                    double travel = 1 - Math.Pow(1 - t, 2.35);
                    // Координаты берём из раскладки. Сдвиг и масштаб прошлого кадра в них не попадают.
                    var at = row.TranslatePoint(VisualTreeHelper.GetOffset(cell) + new Point(cell.ActualWidth / 2, cell.ActualHeight / 2), w);
                    double openingScale = 0.56 + 0.44 * travel;
                    scale.ScaleX = scale.ScaleY = openingScale;
                    double arc = Math.Sin(Math.PI * t) * (VerticalBar ? 11 : 16) * fraction;
                    double lift = Math.Cos(Math.PI * fraction) * 5 * travel * (IsTop || LeftBar ? 1 : -1);
                    shift.X = (mouth.X - at.X + (!VerticalBar ? fraction * 22 : 0)) * (1 - travel) + (!VerticalBar ? arc : 0);
                    shift.Y = (mouth.Y - at.Y + (VerticalBar ? fraction * 18 : 0)) * (1 - travel) + (VerticalBar ? arc : 0);
                    if (VerticalBar) shift.X += lift;
                    else shift.Y += lift;
                    cell.Opacity = Math.Min(1, t * 3.8);
                }
                // Передняя стенка слегка наклоняется, пока стопка выходит из папки.
                var front = (FrameworkElement)((Grid)folderBase).Children[1];
                var tilt = (ScaleTransform)front.RenderTransform;
                tilt.ScaleY = 1 - 0.20 * Math.Sin(Math.PI * value);
                updateThumbnails();
            };
            Action stopMotion = () =>
            {
                if (animate != null) System.Windows.Media.CompositionTarget.Rendering -= animate;
                motionWatch.Stop();
            };
            if (folder)
            {
                var front = (FrameworkElement)((Grid)folderBase).Children[1];
                front.RenderTransformOrigin = new Point(0.5, 1);
                front.RenderTransform = new ScaleTransform();
                foldPreview = ClosePreview;
                if (Motion)
                {
                    foreach (var cell in cells.Values) cell.Opacity = 0;
                    animate = (s, e) =>
                    {
                        double t = Math.Min(1, motionWatch.Elapsed.TotalMilliseconds / motionDuration);
                        progress = motionFrom + (motionTo - motionFrom) * t;
                        if (!Motion || t >= 1)
                        {
                            stopMotion();
                            if (foldingFolder) ClosePreview();
                            else arrangeFan(motionTo);
                            return;
                        }
                        arrangeFan(progress);
                    };
                    Action<double, double> move = (target, duration) =>
                    {
                        stopMotion();
                        motionFrom = progress;
                        motionTo = target;
                        motionDuration = duration;
                        motionWatch.Restart();
                        System.Windows.Media.CompositionTarget.Rendering += animate;
                    };
                    spreadPreview = () => { if (!foldingFolder) move(1, 190); };
                    foldPreview = () =>
                    {
                        if (foldingFolder) return;
                        foldingFolder = true; previewFolding = true;
                        hoverT.Stop();
                        face.IsHitTestVisible = false;
                        if (progress <= 0.02) { ClosePreview(); return; }
                        move(0, 210);
                    };
                }
            }
            EventHandler anchorLayout = (s, e) => position(false);
            var anchorWindow = Window.GetWindow(tile);
            w.Closed += (s, e) =>
            {
                stopMotion();
                if (!folder) return;
                tile.LayoutUpdated -= anchorLayout;
                if (anchorWindow != null) anchorWindow.LocationChanged -= anchorLayout;
            };
            w.Loaded += (s, e) =>
            {
                var ph = new WindowInteropHelper(w).Handle;
                foreach (var sl in slots)
                {
                    IntPtr th; if (N.DwmRegisterThumbnail(ph, sl.Item2, out th) != 0) continue;
                    thumbs.Add(th);
                    N.POINT sz; N.DwmQueryThumbnailSourceSize(th, out sz);
                    if (sz.X > 0 && sz.Y > 0) previews.Add(Tuple.Create(sl.Item1, th, sz, cells[sl.Item2]));
                }
                position(true);
                if (folder)
                {
                    tile.LayoutUpdated += anchorLayout;
                    if (anchorWindow != null) anchorWindow.LocationChanged += anchorLayout;
                }
                if (animate != null)
                {
                    motionWatch.Start();
                    System.Windows.Media.CompositionTarget.Rendering += animate;
                }
            };
            Action relayout = () => position(true);
            w.DpiChanged += (s, e) => w.Dispatcher.BeginInvoke(relayout, DispatcherPriority.Loaded);
            dismiss = h =>
            {
                Border cell; if (!cells.TryGetValue(h, out cell)) return;
                cells.Remove(h); row.Children.Remove(cell);
                transforms.Remove(cell);
                if (row.Children.Count == 0) { ClosePreview(); return; }
                if (count != null) count.Text = "Окон: " + cells.Count;
                // После закрытия окна ряд сжимается, а папка остаётся у ярлыка.
                var slot = slots.FirstOrDefault(x => x.Item2 == h); if (slot != null) slots.Remove(slot);
                foreach (var entry in previews.Where(x => x.Item1 == (slot == null ? null : slot.Item1)).ToList()) { N.DwmUnregisterThumbnail(entry.Item2); thumbs.Remove(entry.Item2); previews.Remove(entry); }
                int columnsNow = Math.Min(row.Children.Count, columns); row.Width = span * columnsNow;
                w.Dispatcher.BeginInvoke(relayout, DispatcherPriority.Loaded);
            };
            if (!folder) Glass(w, face, face.CornerRadius.TopLeft, 0);
            preview = w; previewFor = tile; previewMiss = 0;
            w.Show(); hoverT.Start();
        }
        void PreviewHoverCheck()
        {
            if (preview == null) { hoverT.Stop(); return; }
            previewMiss = previewFor.IsMouseOver || preview.IsMouseOver ? 0 : previewMiss + 1;
            if (previewMiss >= (previewPinned ? 12 : 3)) FoldPreview(); // ¾ секунды на переход курсора со значка на миниатюру; открытые кликом ждут дольше
        }
        void ClosePreview()
        {
            foldPreview = null; previewFolding = false;
            spreadPreview = null;
            foreach (var t in thumbs) N.DwmUnregisterThumbnail(t);
            thumbs.Clear();
            if (preview != null) { preview.Close(); preview = null; }
            previewFor = null; hoverT.Stop();
        }

        // ---------- виджеты ----------

        static void NextLang() { N.PostMessage(N.GetForegroundWindow(), 0x50, IntPtr.Zero, new IntPtr(1)); } // WM_INPUTLANGCHANGEREQUEST, HKL_NEXT

        static void TogglePeek()
        {
            bool peek = DateTime.Now < peekUntil;
            peekUntil = peek ? DateTime.MinValue : DateTime.Now.AddSeconds(15);
            SetTaskbar(!peek || !HideTb);
            peekWas = !peek;
            All(b => b.Place());
        }

        // «Скрытые значки» Windows: показать спрятанную панель Windows, затем Win+B (фокус на трей) и Enter (раскрыть)
        static void ShowTray()
        {
            if (HideTb && DateTime.Now >= peekUntil) TogglePeek();
            int step = 0; var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            t.Tick += (s, e) => { if (step++ == 0) Keys(0x5B, 0x42); else { t.Stop(); Keys(0x0D); } };
            t.Start();
        }
        static bool TrayOpen() { var h = N.FindWindow("TopLevelWindowForOverflowXamlIsland", null); return h != IntPtr.Zero && N.IsWindowVisible(h); }

        static async void ToggleRadio(RD.Radio r)
        {
            try { await r.SetStateAsync(r.State == RD.RadioState.On ? RD.RadioState.Off : RD.RadioState.On); }
            catch (Exception ex) { Log(ex); }
        }

        static async Task<ImageSource> LoadImage(SS.IRandomAccessStreamReference r)
        {
            using (var st = await r.OpenReadAsync())
            {
                var dr = new SS.DataReader(st);
                uint n = await dr.LoadAsync((uint)st.Size);
                var buf = new byte[n]; dr.ReadBytes(buf);
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 384; bi.StreamSource = new MemoryStream(buf); bi.EndInit();
                bi.Freeze(); return bi;
            }
        }

        // Сеть: таймаут 15 с (вместо 100 с по умолчанию) и один повтор через 2 с при сбое связи.
        // Ответ сервера с ошибкой (404, 401…) не повторяем — повтор его не исправит.
        public static string GetSync(string url)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    var rq = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                    rq.UserAgent = "FlowBar"; rq.Timeout = 15000; rq.ReadWriteTimeout = 15000;
                    rq.AutomaticDecompression = System.Net.DecompressionMethods.GZip; // без Deflate: .NET 4 не понимает zlib-обёртку, которую шлют серверы
                    using (var rs = rq.GetResponse()) using (var sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
                }
                catch (System.Net.WebException ex)
                {
                    if (attempt >= 1 || ex.Response != null) throw;
                    System.Threading.Thread.Sleep(2000);
                }
            }
        }
        static Task<string> Get(string url) { return Task.Run(() => GetSync(url)); }
        // последний удачный ответ на диске: без сети после перезапуска показываем его, а не пустоту
        static string CachePath(string kind, string key)
        {
            var dir = F("cache"); Directory.CreateDirectory(dir);
            using (var h = System.Security.Cryptography.SHA1.Create())
                return System.IO.Path.Combine(dir, kind + "-" + BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").Substring(0, 16).ToLowerInvariant() + ".txt");
        }
        static string Ago(DateTime t) { var d = DateTime.Now - t; return d.TotalHours < 1 ? (int)d.TotalMinutes + " мин назад" : d.TotalDays < 1 ? t.ToString("HH:mm") : t.ToString("d MMM HH:mm"); }


        void ShowWeather()
        {
            var body = new StackPanel();
            Func<string, TextBlock> head = s => { var t = Txt(s, 12, true); t.Margin = new Thickness(0, 12, 0, 4); return t; };
            Func<string, double, FrameworkElement> emo = (s, size) => { var ic = WxIcon(s, size); ic.HorizontalAlignment = HorizontalAlignment.Center; return ic; };
            if (wx != null)
            {
                var d = Weather.Wmo(wx.Code, wx.Day);
                var top = new StackPanel { Orientation = Orientation.Horizontal };
                var big = emo(d.Item1, 40); big.Margin = new Thickness(0, 0, 14, 0); big.VerticalAlignment = VerticalAlignment.Center;
                var tc = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                tc.Children.Add(Txt(Weather.Deg(wx.Temp), 30, true)); tc.Children.Add(Txt(d.Item2, 12, false));
                top.Children.Add(big); top.Children.Add(tc); body.Children.Add(top);
                var info = Txt("Макс " + Weather.Deg(wx.DMax[0]) + " · мин " + Weather.Deg(wx.DMin[0]) +
                    "\nВетер " + wx.Wind.ToString("0.#") + " м/с · осадки " + wx.DProb[0] + "% · " + wx.DSum[0].ToString("0.#") + " мм" +
                    "\nВосход " + wx.Sunrise.ToString("HH:mm") + " · закат " + wx.Sunset.ToString("HH:mm"), 12, false);
                info.Margin = new Thickness(0, 8, 0, 0); info.Opacity = 0.85; body.Children.Add(info);

                body.Children.Add(head(RainSummary(wx)));
                body.Children.Add(head("Ближайшие 12 часов"));
                var hours = new StackPanel { Orientation = Orientation.Horizontal };
                for (int i = 0; i < 12 && wx.HStart + i < wx.HTime.Length; i++)
                {
                    int k = wx.HStart + i; var t = wx.HTime[k];
                    var col = new StackPanel { Width = 47 };
                    var hl = Txt(t.ToString("HH:mm"), 10, false); hl.HorizontalAlignment = HorizontalAlignment.Center; hl.Opacity = 0.7;
                    var tl = Txt(Weather.Deg(wx.HTemp[k]), 12, true); tl.HorizontalAlignment = HorizontalAlignment.Center;
                    var pl = Txt(wx.HProb[k] > 0 ? wx.HProb[k] + "%" : " ", 9, false); pl.HorizontalAlignment = HorizontalAlignment.Center; pl.Opacity = 0.7;
                    bool day = t.TimeOfDay > wx.Sunrise.TimeOfDay && t.TimeOfDay < wx.Sunset.TimeOfDay;
                    col.Children.Add(hl); col.Children.Add(emo(Weather.Wmo(wx.HCode[k], day).Item1, 18)); col.Children.Add(tl); col.Children.Add(pl);
                    hours.Children.Add(col);
                }
                var hourStrip = new ScrollViewer { Content = hours, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
                hourStrip.PreviewMouseWheel += (s, e) =>
                {
                    if (hourStrip.ScrollableWidth <= 0) return;
                    hourStrip.ScrollToHorizontalOffset(hourStrip.HorizontalOffset - e.Delta * 0.6);
                    e.Handled = true;
                };
                body.Children.Add(hourStrip);

                body.Children.Add(head("Прогноз на 4 дня"));
                for (int i = 1; i <= 4 && i < wx.DDate.Length; i++)
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                    var dd = Weather.Wmo(wx.DCode[i], true);
                    var cells = new UIElement[] {
                        Txt(wx.DDate[i].ToString("ddd, d MMM"), 12, false), emo(dd.Item1, 15),
                        Txt(wx.DProb[i] > 0 ? wx.DProb[i] + "%" : "", 10, false),
                        Txt(Weather.Deg(wx.DMax[i]) + " / " + Weather.Deg(wx.DMin[i]), 12, true) };
                    for (int c = 0; c < cells.Length; c++) { Grid.SetColumn(cells[c], c); row.Children.Add(cells[c]); }
                    row.ToolTip = dd.Item2;
                    body.Children.Add(row);
                }
                var foot = Txt(wxError != null ? "Нет связи · данные от " + Ago(wx.Updated) + " · последняя попытка " + wxTried.ToString("HH:mm") : "Open-Meteo · данные от " + wx.Updated.ToString("HH:mm"), 9, false);
                if (wxError != null) foot.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xA0, 0x30));
                foot.Opacity = 0.55; foot.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(foot);
            }
            else body.Children.Add(Txt(wxError != null ? "Нет связи с Open-Meteo" : "Введите город и нажмите Enter", 12, false));

            body.Children.Add(head("Город"));
            var input = new TextBox { Text = C("wxCity", ""), Padding = new Thickness(6, 4, 6, 4), ToolTip = "Название города — Enter для поиска" };
            var results = new StackPanel();
            input.KeyDown += async (s, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Enter || input.Text.Trim() == "") return;
                results.Children.Clear(); results.Children.Add(Txt("Поиск…", 11, false));
                try
                {
                    var found = Weather.ParseCities(await Get("https://geocoding-api.open-meteo.com/v1/search?count=5&language=ru&format=json&name=" + Uri.EscapeDataString(input.Text.Trim())));
                    results.Children.Clear();
                    if (found.Count == 0) results.Children.Add(Txt("Ничего не найдено", 11, false));
                    foreach (var city in found)
                    {
                        var c = city;
                        var line = Txt(c.Label, 12, false); line.Padding = new Thickness(2, 4, 2, 4); line.Cursor = Cursors.Hand;
                        line.MouseEnter += (o, a) => line.TextDecorations = TextDecorations.Underline;
                        line.MouseLeave += (o, a) => line.TextDecorations = null;
                        results.Children.Add(Act(line, c.Label, () =>
                        {
                            cfg["wxCity"] = c.Name; cfg["wxLat"] = c.Lat; cfg["wxLon"] = c.Lon; SaveCfg();
                            wx = null; wxError = null;
                            if (panel != null) panel.Close();
                            UpdateWeather();
                        }));
                    }
                }
                catch (Exception ex) { results.Children.Clear(); results.Children.Add(Txt("Ошибка поиска: " + ex.Message, 11, false)); }
            };
            body.Children.Add(input); body.Children.Add(results);
            if (wxError != null && C("wxLat", "") != "") body.Children.Add(CardActions(Btn("Повторить загрузку", () => { if (panel != null) panel.Close(); UpdateWeather(); }, true)));
            var cityName = C("wxCity", "");
            ShowPanel(cityName == "" ? "Погода" : "Погода — " + cityName, body, 340, wx == null ? input : null);
        }

        // ---------- лимиты ИИ ----------
        public static string Tok(long n)
        {
            var inv = CultureInfo.InvariantCulture;
            return n < 1000 ? n.ToString(inv) : n < 1000000 ? (n / 1000.0).ToString("0", inv) + "K" : (n / 1000000.0).ToString("0.0", inv) + "M";
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] internal static extern ulong GetTickCount64(); // 32-битный TickCount обнуляется через 24,9 дня
        public static string Until(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalDays >= 1 ? (int)t.TotalDays + "д " + t.Hours + "ч" : t.TotalHours >= 1 ? (int)t.TotalHours + "ч " + t.Minutes + "м" : t.Minutes + "м";
        }
        static Brush LevelBrush(double pct)
        {
            return pct >= 90 ? new SolidColorBrush(Color.FromRgb(0xF0, 0x50, 0x50)) : pct >= 75 ? new SolidColorBrush(Color.FromRgb(0xF0, 0xA0, 0x30)) : null;
        }
        static UIElement Meter(double pct, double width)
        {
            var g = new Grid { Width = width, Height = 6, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) };
            g.Children.Add(FgShape(new Rectangle { RadiusX = 3, RadiusY = 3 }, 0.18));
            g.Children.Add(new Rectangle { Fill = LevelBrush(pct) ?? Accent, RadiusX = 3, RadiusY = 3, HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(3, width * Math.Min(100, pct) / 100) });
            return g;
        }



        void ShowClaude()
        {
            var now = DateTime.Now; var msgs = claude.Msgs;
            var body = new StackPanel();
            Func<string, TextBlock> head = s => { var t = Txt(s, 12, true); t.Margin = new Thickness(0, 12, 0, 4); return t; };
            Func<string, TextBlock> line = s => { var t = Txt(s, 12, false); t.TextWrapping = TextWrapping.Wrap; t.Opacity = 0.85; return t; };

            // проценты лимитов — от Claude Code через строку состояния; ниже — токены по локальным журналам
            var ls = live;
            var h0 = head("Лимиты подписки"); h0.Margin = new Thickness(0, 0, 0, 4); body.Children.Add(h0);
            if (ls.ClBridge)
            {
                Action<string, double, string> lim = (name, pct, reset) =>
                {
                    body.Children.Add(line(name + ": " + (double.IsNaN(pct) ? "—" : Math.Round(pct) + "% занято · " + Math.Round(100 - pct) + "% осталось") + (reset != "—" && reset != "" ? " · сброс " + (reset == "ждём" ? "уже прошёл, ждём ответа Claude Code" : "через " + reset) : "")));
                    if (!double.IsNaN(pct)) body.Children.Add(Meter(pct, 310));
                };
                lim("5 часов (сессия)", ls.ClP5, ls.ClR5); lim("Неделя", ls.ClPW, ls.ClRW);
                if (!double.IsNaN(ls.ClCtx)) lim("Контекст текущей сессии", ls.ClCtx, "");
                var src = line("От Claude Code · обновлено " + ls.ClPctSeen + ", после каждого ответа" + (ls.ClPctStale ? ". Ответов давно не было — проценты могли измениться" : ""));
                src.FontSize = 10.5; src.Opacity = 0.6; body.Children.Add(src);
            }
            else
            {
                body.Children.Add(line(ClaudeBridge.On ? "Мост подключён. Проценты появятся после следующего ответа Claude Code (для подписки Pro или Max)." :
                    "Проценты 5-часового и недельного лимитов Claude Code сообщает только своей строке состояния. FlowBar может подключиться к ней — ваша нынешняя строка состояния продолжит работать как прежде."));
                if (!ClaudeBridge.On) { var bb = Btn("Подключить проценты", () => { if (panel != null) panel.Close(); ConnectClaudePct(); }, true); bb.HorizontalAlignment = HorizontalAlignment.Left; bb.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(bb); }
            }
            body.Children.Add(head("Показывать на панели"));
            foreach (var r in ClRows) { var k = r[0]; body.Children.Add(CheckRow(r[1], null, ClSel.Contains(k), on => SetClRow(k, on))); }

            var bs = ClaudeUsage.BlockStart(msgs, now);
            var h1 = head("Текущее 5-часовое окно — токены");
            if (bs != null)
            {
                var end = bs.Value.AddHours(5); var block = msgs.Where(m => m.T >= bs.Value).ToList();
                body.Children.Add(line("Началось ≈ в " + bs.Value.ToString("HH:mm") + " · сброс ≈ в " + end.ToString("HH:mm") + " (через " + Until(end - now) + ")"));
                body.Children.Add(line("Это оценка по журналам Claude Code. Настоящее окно считает аккаунт: в нём ещё чат на claude.ai и другие устройства, поэтому время может отличаться на десятки минут и больше. Точное время даёт «Подключить проценты»."));
                body.Children.Add(Meter((now - bs.Value).TotalMinutes / 3, 310));
                body.Children.Add(line("Токены: " + Tok(block.Sum(m => m.Weight)) + "  (ввод " + Tok(block.Sum(m => m.In)) + " · вывод " + Tok(block.Sum(m => m.Out)) +
                    " · запись кэша " + Tok(block.Sum(m => m.CacheW)) + ")\nЧтение кэша: " + Tok(block.Sum(m => m.CacheR)) + " · ответов: " + block.Count));
            }
            else body.Children.Add(line("Активного окна нет — оно начнётся с первого запроса к Claude Code."));

            var week = msgs.Where(m => m.T >= now.AddDays(-7)).ToList();
            body.Children.Add(head("За 7 дней: " + Tok(week.Sum(m => m.Weight)) + " токенов · " + week.Count + " ответов"));
            var days = new Grid { Height = 60 };
            var perDay = Enumerable.Range(0, 7).Select(d => week.Where(m => m.T.Date == now.Date.AddDays(-d)).Sum(m => m.Weight)).ToArray();
            long maxD = Math.Max(1, perDay.Max());
            for (int d = 6; d >= 0; d--)
            {
                days.ColumnDefinitions.Add(new ColumnDefinition());
                var col = new DockPanel { Margin = new Thickness(4, 0, 4, 0) };
                var lbl = Txt(now.AddDays(-d).ToString("ddd"), 9, d == 0); lbl.HorizontalAlignment = HorizontalAlignment.Center; lbl.Opacity = 0.7;
                DockPanel.SetDock(lbl, Dock.Bottom); col.Children.Add(lbl);
                col.Children.Add(new Rectangle { Fill = Accent, RadiusX = 2, RadiusY = 2, VerticalAlignment = VerticalAlignment.Bottom, Height = Math.Max(2, 44.0 * perDay[d] / maxD), Opacity = d == 0 ? 1 : 0.6, ToolTip = Tok(perDay[d]) });
                Grid.SetColumn(col, 6 - d); days.Children.Add(col);
            }
            body.Children.Add(days);

            body.Children.Add(head("Модели за 7 дней"));
            long total = Math.Max(1, week.Sum(m => m.Weight));
            foreach (var g in week.GroupBy(m => ClaudeUsage.ModelName(m.Model)).Select(g => new { Name = g.Key, W = g.Sum(m => m.Weight) }).OrderByDescending(g => g.W).Take(4))
            {
                var dp = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
                var t = Txt(Math.Round(100.0 * g.W / total) + "% · " + Tok(g.W), 11, false); DockPanel.SetDock(t, Dock.Right);
                dp.Children.Add(t); dp.Children.Add(Txt(g.Name, 11, false));
                body.Children.Add(dp);
            }
            if (week.Count == 0) body.Children.Add(line("Нет запросов за неделю."));

            var foot = line("Токены считаются по журналам Claude Code на этом компьютере; чаты на claude.ai и в приложении в них не входят. Проценты лимитов учитывают всё — они приходят от Claude Code с каждым ответом.");
            foot.FontSize = 10; foot.Opacity = 0.6; foot.Margin = new Thickness(0, 12, 0, 4); body.Children.Add(foot);
            var link = Txt("Открыть claude.ai → Использование ↗", 12, true); link.Cursor = Cursors.Hand; link.Foreground = Accent;
            body.Children.Add(Act(link, link.Text, () => Launch("https://claude.ai/settings/usage")));
            ShowPanel("Claude", body, 340, null);
        }

        void ShowCodex()
        {
            var now = DateTime.Now; var body = new StackPanel();
            if (codex == null)
            {
                body.Children.Add(new TextBlock { Text = "Данных пока нет. Лимиты появятся после первого ответа Codex CLI — он сам записывает их в свои журналы.", TextWrapping = TextWrapping.Wrap });
                ShowPanel("Codex", body, 320, null); return;
            }
            if (!string.IsNullOrEmpty(codex.Plan)) body.Children.Add(Txt("Тариф: " + char.ToUpper(codex.Plan[0]) + codex.Plan.Substring(1), 12, false));
            Action<string, double, DateTime, string> win = (name, pct, reset, fmt) =>
            {
                var t = Txt(name, 12, true); t.Margin = new Thickness(0, 12, 0, 0); body.Children.Add(t);
                body.Children.Add(Meter(double.IsNaN(pct) ? 0 : pct, 290));
                bool expired = reset.Year > 2000 && now >= reset;
                var sub = Txt(expired ? "Окно сброшено " + reset.ToString(fmt) + " — ждём новой записи Codex, текущий процент неизвестен" : double.IsNaN(pct) ? "Нет данных в журнале Codex" : Math.Round(pct) + "% занято · " + Math.Round(100 - pct) + "% осталось\nСброс " + reset.ToString(fmt) + " (через " + Until(reset - now) + ")", 11, false);
                sub.TextWrapping = TextWrapping.Wrap;
                sub.Opacity = 0.8; body.Children.Add(sub);
            };
            win("5-часовое окно", codex.Eff5(now), codex.R5, "HH:mm");
            win("Неделя", codex.EffW(now), codex.RW, "ddd, d MMM HH:mm");
            var foot = CardStatus(((now - codex.Seen).TotalHours > 6 ? "Данные устарели. Последняя запись Codex " : "Последняя запись Codex ") + codex.Seen.ToString("d MMM HH:mm") + ". Обновляется во время работы в Codex.");
            foot.Margin = new Thickness(0, 12, 0, 0); body.Children.Add(foot);
            ShowPanel("Codex", body, 320, null);
        }



        // ---------- всплывающие карточки ----------
        static FrameworkElement anchor; static DateTime anchorAt; // плитка, по которой только что кликнули: карточка откроется у неё
        static uint stickyPid; // процесс программы, по значку трея которой только что нажали: карточка трея не закрывается, пока работают с ней
        Action<TextBlock> headEditor; // выставляется перед ShowPanel и сразу забирается им
        void ShowPanel(string title, UIElement body, double width, IInputElement focus, Action tick = null)
        {
            var edit = headEditor; headEditor = null; // забираем сразу: ранний выход ниже не должен оставить его для другой карточки
            ClosePreview(); TipHide(true);
            double keepCx = cx0Back; cx0Back = double.NaN;
            var from = (DateTime.Now - anchorAt).TotalSeconds < 2 && anchor != null && anchor.IsVisible ? anchor : null; anchor = null;
            // Нажатие на панель сначала уводит фокус с карточки и закрывает её, а клик приходит уже после.
            // Если эта же карточка закрылась только что — это повторное нажатие: оставляем закрытой.
            if (panel == null && title == lastClosedTitle && (DateTime.Now - lastClosedAt).TotalMilliseconds < 400) { lastClosedTitle = null; return; }
            bool same = panel != null && panelTitle == title;
            var back = panel != null && !same ? panelBuild : null; panelBuild = null; // карточка открыта из другой («Ещё») — стрелка назад к ней
            if (back != null) { from = null; keepCx = panel.Left + panel.ActualWidth / 2; } // на месте прежней карточки, как переход внутри одного меню
            if (panel != null) panel.Close();
            if (same) return; // повторный клик по виджету закрывает карточку
            width = Math.Max(320, width); // обычная ширина карточки 320–420
            Color fg = CardFg;
            const double Shadow = 18; // поле под тень вокруг карточки
            var w = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height, Width = width + Shadow * 2, FontFamily = FontFamily,
                Foreground = Frozen(fg, 255)
            };
            StylePopupText(w);
            System.Windows.Documents.Typography.SetNumeralAlignment(w, FontNumeralAlignment.Tabular); UseCardScroll(w);
            // заголовок всегда виден, длинное содержимое прокручивается целиком (поле ввода и кнопки внизу тоже достижимы)
            var sp = new DockPanel();
            var head = new TextBlock { Text = title.Length > 0 ? char.ToUpper(title[0]) + title.Substring(1) : title, FontSize = 17, FontWeight = FontWeights.SemiBold, FontFamily = Display, Margin = new Thickness(0, 0, 0, 12) };
            FrameworkElement top = head;
            if (edit != null) edit(head); // карточка с редактируемым заголовком (название группы)
            if (back != null)
            {
                head.Margin = new Thickness(0); head.VerticalAlignment = VerticalAlignment.Center;
                var arrow = Act(new TextBlock { Text = "", FontFamily = Icons, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, "Назад", () => { cx0Back = w.Left + w.ActualWidth / 2; back(); });
                arrow.Width = 28; arrow.Height = 28; arrow.CornerRadius = new CornerRadius(8); arrow.Margin = new Thickness(-6, 0, 4, 0); arrow.ToolTip = "Назад";
                arrow.MouseEnter += (o, e) => Fade(arrow, Color.FromArgb(0x1C, fg.R, fg.G, fg.B), 100); arrow.MouseLeave += (o, e) => Fade(arrow, Colors.Transparent, 140);
                var hp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -4, 0, 8) }; hp.Children.Add(arrow); hp.Children.Add(head); top = hp;
                w.PreviewKeyDown += (o, e) => { if (e.Key == System.Windows.Input.Key.Back && !(Keyboard.FocusedElement is TextBox)) { e.Handled = true; back(); } };
            }
            if (title == "Значки трея") { head.Text = ""; if (back == null) top.Visibility = Visibility.Collapsed; } // сетка значков говорит сама за себя; стрелка «Назад» остаётся
            DockPanel.SetDock(top, Dock.Top); sp.Children.Add(top);
            sp.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Padding = new Thickness(0, 0, 6, 0), Margin = new Thickness(0, 0, -6, 0) });
            StyleInputs(sp, fg);
            var face = new Border { Background = CardFill, CornerRadius = new CornerRadius(16), Padding = new Thickness(16), BorderThickness = new Thickness(1), BorderBrush = CardEdge, Child = sp };
            // тень — отдельный слой позади: эффект на самом контейнере с текстом заставлял WPF рисовать буквы через промежуточный растр, и они мылились
            var shade = new Border { CornerRadius = face.CornerRadius, Background = Frozen(CardBg, 255), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = IsTop ? 90 : 270, Opacity = 0.32 } };
            var card = new Grid { Margin = new Thickness(Shadow), RenderTransform = new TranslateTransform() }; card.Children.Add(shade); card.Children.Add(face);
            w.Content = card; Glass(w, face, face.CornerRadius.TopLeft, Shadow, shade); FadeClose(w, card, IsTop ? -8 : 8);
            { double sc0 = Scale, room0 = VerticalBar ? scr.Bounds.Height / sc0 - 20 : IsTop ? scr.Bounds.Bottom / sc0 - (CapBottom + 8) : CapTop - 8 - scr.Bounds.Top / sc0; w.MaxHeight = Math.Max(220, room0) + Shadow * 2; }
            bool closed = false;
            double cx0 = double.NaN;
            Action place = () =>
            {
                if (VerticalBar) { PlaceSideCard(w, from, 8 - Shadow); return; }
                // по центру над нажатой плиткой (без неё — у правого края панели), но в пределах экрана
                double sc = Scale, sl = scr.Bounds.Left / sc, sr = scr.Bounds.Right / sc, st = scr.Bounds.Top / sc, sb = scr.Bounds.Bottom / sc;
                if (double.IsNaN(cx0)) cx0 = !double.IsNaN(keepCx) ? keepCx : from != null && from.IsVisible ? from.PointToScreen(new Point(from.ActualWidth / 2, 0)).X / sc : Left + ActualWidth - w.ActualWidth / 2 + Shadow;
                double nl = Math.Max(sl + 4 - Shadow, Math.Min(sr - 4 + Shadow - w.ActualWidth, cx0 - w.ActualWidth / 2));
                double room = IsTop ? sb - (CapBottom + 8) : CapTop - 8 - st; // высота от панели до края экрана
                double hh = Math.Min(w.ActualHeight, room + Shadow * 2); // MaxHeight уже ограничил карточку — лишнее прокручивается
                PlaceWin(w, nl, IsTop ? CapBottom + 8 - Shadow : CapTop - hh - 8 + Shadow);
            };
            w.SizeChanged += (o, e) => { if (w.IsLoaded) place(); }; // содержимое догрузилось (список сетей, устройства) — карточка по-прежнему у панели
            w.Loaded += (o, e) =>
            {
                place();
                if (focus != null) Keyboard.Focus(focus);
                if (!Motion) return;
                // появление от панели: сдвиг 10 px + прозрачность, 160 мс с замедлением
                var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
                // по окончании анимация снимается: карточка остаётся в исходном, выровненном по пикселям положении
                card.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(IsTop ? -10 : 10, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease, FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
                card.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
            };
            w.Deactivated += (o, e) => w.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (closed || dragging) return;
                if (stickyPid == 0) { w.Close(); return; }
                // карточка трея: после нажатия на значок программа выводит своё меню или окно — карточка остаётся, пока работают с этой программой
                uint pid0 = stickyPid; var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; var since = DateTime.Now;
                watch.Tick += (o2, e2) =>
                {
                    if (closed) { watch.Stop(); return; }
                    if (w.IsActive || stickyPid != pid0) { watch.Stop(); return; } // вернулись в карточку или нажали другой значок — дальше следит новый
                    if ((DateTime.Now - since).TotalMilliseconds < 1500) return; // программа выводит меню не сразу: дать ей время
                    uint fp; var fgw = N.GetForegroundWindow(); N.GetWindowThreadProcessId(fgw, out fp);
                    // открыто любое меню (#32768) — карточка остаётся, даже если его окно у другого процесса
                    if (fp != pid0 && Cls(fgw) != "#32768" && N.FindWindow("#32768", null) == IntPtr.Zero)
                    {
                        watch.Stop(); stickyPid = 0; w.Close();
                        Info("карточка трея закрыта: впереди " + ExePathOf(fp) + ", окно «" + Cls(fgw) + "»");
                    }
                };
                watch.Start();
            })); // из «…» модуль тянут на панель — карточка остаётся
            // Esc закрывает карточку и возвращает фокус на плитку, если на панель пришли с клавиатуры
            w.PreviewKeyDown += (o, e) => { if (e.Key != System.Windows.Input.Key.Escape) return; e.Handled = true; w.Close(); if (backTo != IntPtr.Zero && from != null) { N.SetForegroundWindow(hwnd); Keyboard.Focus(from); } };
            w.Closed += (o, e) => { closed = true; stickyPid = 0; lastClosedTitle = title; lastClosedAt = DateTime.Now; if (panel == w) { panel = null; panelTick = null; panelBuild = null; } };
            panel = w; panelTitle = title; panelTick = tick;
            w.Show(); w.Activate();
        }

        void ShowTodos()
        {
            var remList = new StackPanel { Margin = new Thickness(0, 4, -4, 0) };
            var list = new StackPanel { Margin = new Thickness(-8, 0, -4, 0) }; var count = Txt("", 11, false); count.Opacity = 0.6; count.Margin = new Thickness(0, -6, 0, 6);
            var input = new TextBox { Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 8, 0, 0), ToolTip = "Новая задача — Enter" };
            Action render = null;
            render = () =>
            {
                list.Children.Clear();
                if (todos.Count == 0) list.Children.Add(new TextBlock { Text = "Задач нет — введите ниже и нажмите Enter", Opacity = 0.6, Margin = new Thickness(8, 0, 0, 0) });
                // сначала несделанные; строка — круглая отметка, текст и крестик, который виден при наведении
                string lastGroup = null;
                foreach (var td in todos.OrderBy(x => x.Done).ThenBy(x => x.Due ?? DateTime.MaxValue).ToList())
                {
                    var t = td;
                    string group = t.Done ? "Завершённые" : t.Due < DateTime.Today ? "Просроченные" : t.Due == DateTime.Today ? "Сегодня" : "Позже";
                    if (lastGroup != group) { list.Children.Add(CardHead(group)); lastGroup = group; }
                    var mark = new TextBlock { Text = "\uE73E", FontFamily = Icons, FontSize = 10, Foreground = Frozen(OnAccent, 255), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = t.Done ? 1 : 0 };
                    var ring = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.5), Child = mark, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0),
                        BorderBrush = t.Done ? Accent : Frozen(CardFg, 0x70), Background = t.Done ? Accent : Brushes.Transparent };
                    var tx = new TextBlock { Text = t.Text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Opacity = t.Done ? 0.45 : 1, VerticalAlignment = VerticalAlignment.Center };
                    if (t.Done) tx.TextDecorations = TextDecorations.Strikethrough;
                    var del = Cross("Удалить задачу «" + t.Text + "»", () => { todos.Remove(t); SaveTodos(); render(); }); del.Opacity = 0;
                    var dp = new DockPanel { Margin = new Thickness(8, 6, 4, 6) }; DockPanel.SetDock(del, Dock.Right); DockPanel.SetDock(ring, Dock.Left);
                    dp.Children.Add(del); dp.Children.Add(ring);
                    dp.Children.Add(t.Due.HasValue ? (UIElement)VS(tx, CardStatus(t.Due.Value.ToString("d MMM"))) : tx);
                    Press row = null;
                    row = Act(dp, (t.Done ? "Сделано: " : "Задача: ") + t.Text, () =>
                    {
                        t.Done = !t.Done; SaveTodos();
                        // отметка заполняется, текст гаснет — затем список перестраивается (сделанные уходят вниз)
                        ring.Background = t.Done ? Accent : Brushes.Transparent; ring.BorderBrush = t.Done ? Accent : Frozen(CardFg, 0x70);
                        mark.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(t.Done ? 1 : 0, TimeSpan.FromMilliseconds(120)));
                        tx.TextDecorations = t.Done ? TextDecorations.Strikethrough : null;
                        tx.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(t.Done ? 0.45 : 1, TimeSpan.FromMilliseconds(180)));
                        var later = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Motion ? 420 : 0) }; later.Tick += (o2, e2) => { later.Stop(); render(); }; later.Start();
                    });
                    row.CornerRadius = new CornerRadius(8);
                    row.ContextMenu = new ContextMenu();
                    foreach (var choice in new[] { Tuple.Create("Сегодня", (DateTime?)DateTime.Today), Tuple.Create("Завтра", (DateTime?)DateTime.Today.AddDays(1)), Tuple.Create("Без срока", (DateTime?)null) })
                        row.ContextMenu.Items.Add(Item(choice.Item1, () => { t.Due = choice.Item2; SaveTodos(); render(); }));
                    // напомнить об этой задаче уведомлением
                    row.ContextMenu.Items.Add(new Separator());
                    foreach (var rc in RemindChoices())
                    { var at = rc.Item2; row.ContextMenu.Items.Add(Item("Напомнить " + rc.Item1, () => { AddReminder(at, t.Text); render(); })); }
                    row.MouseEnter += (s, e) => { Fade(row, Color.FromArgb(0x14, CardFg.R, CardFg.G, CardFg.B), 100); del.Opacity = 0.75; };
                    row.MouseLeave += (s, e) => { Fade(row, Colors.Transparent, 140); del.Opacity = 0; };
                    list.Children.Add(row);
                }
                int left = todos.Count(x => !x.Done);
                count.Text = todos.Count == 0 ? "" : left == 0 ? "Всё сделано" : "Осталось " + left + " из " + todos.Count;
                // ожидающие напоминания (свои и поставленные агентами): время, текст, крестик отмены
                remList.Children.Clear();
                if (reminders == null) LoadReminders();
                if (reminders.Count > 0) remList.Children.Add(H("Напоминания"));
                foreach (var r0 in reminders.OrderBy(x => x.At).ToList())
                {
                    var r = r0;
                    var when = Txt(r.At.Date == DateTime.Today ? r.At.ToString("HH:mm") : r.At.ToString("d MMM HH:mm"), 11.5, true); when.Width = 86; when.Opacity = 0.85;
                    var what = new TextBlock { Text = r.Text, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
                    var x = Cross("Отменить напоминание «" + r.Text + "»", () => { reminders.Remove(r); SaveReminders(); render(); });
                    var dp = new DockPanel { Margin = new Thickness(0, 1, 4, 1) }; DockPanel.SetDock(x, Dock.Right); DockPanel.SetDock(when, Dock.Left); dp.Children.Add(x); dp.Children.Add(when); dp.Children.Add(what);
                    remList.Children.Add(dp);
                }
            };
            var dueDate = new DatePicker { Margin = new Thickness(0, 5, 0, 0) };
            // remList объявлен выше render
            System.Windows.Automation.AutomationProperties.SetName(dueDate, "Срок новой задачи");
            input.KeyDown += (s, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Enter || input.Text.Trim() == "") return;
                todos.Add(new Todo { Text = input.Text.Trim(), Due = dueDate.SelectedDate }); input.Text = ""; dueDate.SelectedDate = null; SaveTodos(); render();
            };
            render();
            var hint = Txt("Правый клик по задаче — срок и напоминание", 10.5, false); hint.Opacity = 0.55; hint.Margin = new Thickness(0, 6, 0, 0);
            var body = new StackPanel(); body.Children.Add(count); body.Children.Add(list); body.Children.Add(input); body.Children.Add(dueDate); body.Children.Add(hint); body.Children.Add(remList);
            ShowPanel("Задачи", body, 320, input);
        }

        // ---------- инструменты: таймер, секундомер, валюта, файлы, корзина ----------
        static DateTime timerEnd; static TimeSpan timerLeft; static bool timerRunning;
        static int TimerLast { get { int n; return int.TryParse(C("timer.last", "300"), out n) && n > 0 ? n : 300; } }
        // старт, пауза и продолжение прямо с панели — та же логика, что в «Инструментах»
        static void TimerToggle()
        {
            if (timerRunning) { timerLeft = timerEnd - DateTime.Now; timerRunning = false; return; }
            var dd = timerLeft > TimeSpan.Zero ? timerLeft : TimeSpan.FromSeconds(TimerLast);
            timerEnd = DateTime.Now + dd; timerLeft = TimeSpan.Zero; timerRunning = true;
        }
        static void TimerReset() { timerRunning = false; timerLeft = TimeSpan.Zero; }
        static void StopwatchReset()
        {
            if (sw.Elapsed > TimeSpan.Zero) File.AppendAllLines(F("stopwatch.txt"), new[] { DateTime.Now.ToString("d MMM HH:mm") + "|" + sw.Elapsed.Ticks });
            sw.Reset(); laps.Clear();
        }
        static Stopwatch sw = new Stopwatch(); static List<TimeSpan> laps = new List<TimeSpan>();
        static string toolsTab = "timer";
        static Dictionary<string, double> rates; static DateTime ratesAt; static string ratesError; static bool ratesBusy;

        public static string Span(TimeSpan t, bool cs = false)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            var s = (t.TotalHours >= 1 ? (int)t.TotalHours + ":" + t.Minutes.ToString("00") : t.Minutes.ToString("00")) + ":" + t.Seconds.ToString("00");
            return cs ? s + "," + (t.Milliseconds / 10).ToString("00") : s;
        }
        public static string Ago(DateTime t, DateTime now)
        {
            var d = now - t;
            if (d.TotalMinutes < 1) return "только что";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + " мин назад";
            if (t.Date == now.Date) return (int)d.TotalHours + " ч назад";
            if (t.Date == now.Date.AddDays(-1)) return "вчера";
            return t.ToString(t.Year == now.Year ? "d MMM" : "d MMM yyyy");
        }
        static string Bytes(long b) { return b < 1 << 20 ? Math.Max(1, b / 1024) + " КБ" : b < 1L << 30 ? (b / 1048576.0).ToString("0.#") + " МБ" : (b / 1073741824.0).ToString("0.##") + " ГБ"; }



        // Кнопка карточки: основная — в цвете акцента, обычная — полупрозрачная
        static Border Btn(string text, Action a, bool primary = false)
        {
            var t = Txt(text, 12, true); t.HorizontalAlignment = HorizontalAlignment.Center;
            var fg = CardFg;
            var b = new Press { Invoke = a, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, Child = t,
                Background = primary ? (Brush)new SolidColorBrush(AccentColor) : new SolidColorBrush(Color.FromArgb(0x1C, fg.R, fg.G, fg.B)) };
            if (primary) t.Foreground = Frozen(OnAccent, 255);
            // сброс и отмена — контурные кнопки: заливка у главного действия («Применить»), у них только тонкая рамка
            if (!primary && (text.StartsWith("Сбросить") || text.StartsWith("Отмена") || text == "Раскладка по умолчанию")) { b.Background = Brushes.Transparent; b.BorderBrush = Frozen(fg, 110); b.BorderThickness = new Thickness(1); b.Padding = new Thickness(13, 6, 13, 6); }
            System.Windows.Automation.AutomationProperties.SetName(b, text);
            b.MouseEnter += (s, e) => b.Opacity = 0.85; b.MouseLeave += (s, e) => b.Opacity = 1;
            b.MouseLeftButtonUp += (s, e) => a();
            return b;
        }
        static Border Chip(string text, Action a, string tip = null)
        {
            var b = Btn(text, a); b.Padding = new Thickness(10, 4, 10, 4); b.Margin = new Thickness(0, 0, 6, 6); b.CornerRadius = new CornerRadius(12);
            ((TextBlock)b.Child).FontWeight = FontWeights.Normal; if (tip != null) b.ToolTip = tip;
            return b;
        }

        void ShowTools(string tab)
        {
            if (tab != null) toolsTab = tab;
            var body = new StackPanel();
            var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            var host = new Border();
            Action liveTick = null; Action<string> open = null;
            var names = new[] { "timer|Таймер", "stopwatch|Секундомер", "currency|Валюта", "files|Файлы" };
            Action renderTabs = () =>
            {
                tabs.Children.Clear();
                foreach (var n in names)
                {
                    var p = n.Split('|'); var key = p[0]; bool sel = key == toolsTab;
                    var b = Chip(p[1], () => open(key));
                    if (sel) { b.Background = new SolidColorBrush(AccentColor); ((TextBlock)b.Child).Foreground = Frozen(OnAccent, 255); ((TextBlock)b.Child).FontWeight = FontWeights.SemiBold; }
                    tabs.Children.Add(b);
                }
            };
            open = key =>
            {
                toolsTab = key; renderTabs(); Action t = null;
                host.Child = key == "timer" ? TimerView(out t) : key == "stopwatch" ? StopwatchView(out t) : key == "currency" ? CurrencyView() : FilesView();
                liveTick = t; StyleInputs(host, CardFg);
            };
            body.Children.Add(tabs); body.Children.Add(host);
            open(toolsTab);
            ShowPanel("Инструменты", body, 360, null);
            if (panel != null && panelTitle == "Инструменты")
            {
                // цифры таймера и секундомера обновляются, пока карточка открыта
                var live = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                live.Tick += (s, e) => { if (liveTick != null) liveTick(); };
                live.Start(); panel.Closed += (s, e) => live.Stop();
            }
        }

        FrameworkElement TimerView(out Action tick)
        {
            var v = new StackPanel();
            var big = Txt("00:00", 40, true); big.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"); big.HorizontalAlignment = HorizontalAlignment.Center; big.Margin = new Thickness(0, 0, 0, 10);
            v.Children.Add(big);
            int last; if (!int.TryParse(C("timer.last", "300"), out last)) last = 300;
            var fields = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            Func<string, int, TextBox> num = (label, val) =>
            {
                var tb = new TextBox { Text = val.ToString(), Width = 52, TextAlignment = TextAlignment.Center, ToolTip = label };
                System.Windows.Automation.AutomationProperties.SetName(tb, label);
                fields.Children.Add(tb);
                var l = Txt(label, 11, false); l.VerticalAlignment = VerticalAlignment.Center; l.Margin = new Thickness(6, 0, 14, 0); l.Opacity = 0.7; fields.Children.Add(l);
                return tb;
            };
            TextBox h = num("ч", last / 3600), m = num("мин", last / 60 % 60), s = num("сек", last % 60);
            v.Children.Add(fields);
            var err = Txt("", 11, false); err.Foreground = Frozen(Color.FromRgb(0xE8, 0x5A, 0x5A), 255); err.HorizontalAlignment = HorizontalAlignment.Center; err.Margin = new Thickness(0, 4, 0, 0);
            v.Children.Add(err);
            Func<int?> secs = () =>
            {
                int a, b, c;
                if (!int.TryParse(h.Text.Trim(), out a) || !int.TryParse(m.Text.Trim(), out b) || !int.TryParse(s.Text.Trim(), out c) || a < 0 || a > 23 || b < 0 || b > 59 || c < 0 || c > 59) return null;
                return a * 3600 + b * 60 + c;
            };
            Action<int> setFields = x => { h.Text = (x / 3600).ToString(); m.Text = (x / 60 % 60).ToString(); s.Text = (x % 60).ToString(); };
            Action start = () =>
            {
                err.Text = "";
                if (timerRunning) { timerLeft = timerEnd - DateTime.Now; timerRunning = false; return; } // пауза
                TimeSpan d = timerLeft;
                if (d <= TimeSpan.Zero)
                {
                    var x = secs();
                    if (x == null) { err.Text = "Часы 0–23, минуты и секунды 0–59"; return; }
                    if (x == 0) { err.Text = "Укажите длительность больше нуля"; return; }
                    d = TimeSpan.FromSeconds(x.Value); cfg["timer.last"] = x.Value.ToString(); SaveCfg();
                }
                timerEnd = DateTime.Now + d; timerLeft = TimeSpan.Zero; timerRunning = true;
            };

            // готовые и сохранённые длительности
            var presets = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) };
            Action renderPresets = null;
            renderPresets = () =>
            {
                presets.Children.Clear();
                var custom = C("timer.presets", "").Split(',').Select(x => { int n; return int.TryParse(x, out n) ? n : 0; }).Where(n => n > 0).ToList();
                foreach (var p in new[] { 60, 300, 600, 900, 1800 }.Concat(custom).Distinct())
                {
                    int sec = p; bool own = custom.Contains(p) && !new[] { 60, 300, 600, 900, 1800 }.Contains(p);
                    var label = sec % 60 == 0 ? sec / 60 + " мин" : Span(TimeSpan.FromSeconds(sec));
                    var chip = Chip(label, () => { timerRunning = false; timerLeft = TimeSpan.Zero; setFields(sec); start(); }, own ? "ПКМ — удалить" : null);
                    if (own) chip.MouseRightButtonUp += (o, e) => { e.Handled = true; cfg["timer.presets"] = string.Join(",", custom.Where(x => x != sec)); SaveCfg(); renderPresets(); };
                    presets.Children.Add(chip);
                }
            };
            renderPresets();
            v.Children.Add(presets);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var startB = Btn("Старт", start, true);
            btns.Children.Add(startB);
            btns.Children.Add(Btn("Сброс", () => { timerRunning = false; timerLeft = TimeSpan.Zero; err.Text = ""; }));
            btns.Children.Add(Btn("Сохранить", () =>
            {
                var x = secs(); if (x == null || x == 0) { err.Text = "Сначала введите длительность"; return; }
                var list = C("timer.presets", "").Split(',').Where(p => p != "").ToList();
                if (!list.Contains(x.Value.ToString())) { list.Add(x.Value.ToString()); cfg["timer.presets"] = string.Join(",", list.Skip(Math.Max(0, list.Count - 6))); SaveCfg(); renderPresets(); }
            }));
            v.Children.Add(btns);
            var hint = Txt("Сигнал прозвучит и в тихие часы — таймер вы заводите сами", 11, false); hint.Opacity = 0.55; hint.Margin = new Thickness(0, 10, 0, 0); hint.TextWrapping = TextWrapping.Wrap;
            v.Children.Add(hint);

            tick = () =>
            {
                var x = secs();
                big.Text = timerRunning ? Span(timerEnd - DateTime.Now) : timerLeft > TimeSpan.Zero ? Span(timerLeft) : Span(TimeSpan.FromSeconds(x ?? 0));
                ((TextBlock)startB.Child).Text = timerRunning ? "Пауза" : timerLeft > TimeSpan.Zero ? "Продолжить" : "Старт";
            };
            tick();
            return v;
        }

        FrameworkElement StopwatchView(out Action tick)
        {
            var v = new StackPanel();
            var big = Txt("00:00,00", 38, true); big.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"); big.HorizontalAlignment = HorizontalAlignment.Center; big.Margin = new Thickness(0, 0, 0, 12);
            v.Children.Add(big);
            var lapsPanel = new StackPanel(); var history = new StackPanel();
            Action renderLaps = () =>
            {
                lapsPanel.Children.Clear();
                for (int i = laps.Count - 1; i >= 0; i--)
                {
                    var d = laps[i] - (i > 0 ? laps[i - 1] : TimeSpan.Zero);
                    var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                    var r = Txt("+" + Span(d, true), 11, false); r.Opacity = 0.65; DockPanel.SetDock(r, Dock.Right); row.Children.Add(r);
                    row.Children.Add(Txt("Круг " + (i + 1) + "    " + Span(laps[i], true), 12, false));
                    lapsPanel.Children.Add(row);
                }
                history.Children.Clear();
                var hist = Lines("stopwatch.txt").AsEnumerable().Reverse().Take(5).ToList();
                if (hist.Count > 0) history.Children.Add(H("История"));
                foreach (var l in hist)
                {
                    var p = l.Split('|'); long ticksV;
                    if (p.Length == 2 && long.TryParse(p[1], out ticksV)) { var t = Txt(p[0] + "  ·  " + Span(new TimeSpan(ticksV), true), 11, false); t.Opacity = 0.75; history.Children.Add(t); }
                }
            };
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            var startB = Btn("Старт", () => { if (sw.IsRunning) sw.Stop(); else sw.Start(); }, true);
            btns.Children.Add(startB);
            var lapB = Btn("Круг", () => { if (sw.IsRunning) { laps.Add(sw.Elapsed); renderLaps(); } });
            btns.Children.Add(lapB);
            btns.Children.Add(Btn("Сброс", () =>
            {
                if (sw.Elapsed > TimeSpan.Zero) File.AppendAllLines(F("stopwatch.txt"), new[] { DateTime.Now.ToString("d MMM HH:mm") + "|" + sw.Elapsed.Ticks });
                sw.Reset(); laps.Clear(); renderLaps();
            }));
            v.Children.Add(btns);
            v.Children.Add(lapsPanel); lapsPanel.Margin = new Thickness(0, 10, 0, 0);
            v.Children.Add(history);
            renderLaps();
            tick = () =>
            {
                big.Text = Span(sw.Elapsed, true);
                ((TextBlock)startB.Child).Text = sw.IsRunning ? "Пауза" : sw.Elapsed > TimeSpan.Zero ? "Продолжить" : "Старт";
                lapB.Opacity = sw.IsRunning ? 1 : 0.45; // неактивна, пока секундомер стоит
            };
            tick();
            return v;
        }

        // ---------- валюта ----------
        // Русские названия популярных валют для поиска; остальные — по коду и английскому названию
        static readonly Dictionary<string, string> RuCur = new Dictionary<string, string> {
            { "RUB", "российский рубль" }, { "USD", "доллар США" }, { "EUR", "евро" }, { "CNY", "китайский юань" }, { "GBP", "фунт стерлингов" },
            { "JPY", "японская иена" }, { "KZT", "казахстанский тенге" }, { "BYN", "белорусский рубль" }, { "UAH", "украинская гривна" }, { "TRY", "турецкая лира" },
            { "CHF", "швейцарский франк" }, { "AED", "дирхам ОАЭ" }, { "GEL", "грузинский лари" }, { "AMD", "армянский драм" }, { "UZS", "узбекский сум" },
            { "KGS", "киргизский сом" }, { "AZN", "азербайджанский манат" }, { "THB", "тайский бат" }, { "INR", "индийская рупия" }, { "KRW", "южнокорейская вона" },
            { "PLN", "польский злотый" }, { "CZK", "чешская крона" }, { "CAD", "канадский доллар" }, { "AUD", "австралийский доллар" }, { "HKD", "гонконгский доллар" },
        };
        static Dictionary<string, string> curNames;
        static string CurName(string code)
        {
            if (curNames == null)
            {
                curNames = new Dictionary<string, string>();
                foreach (var c in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
                    try { var r = new RegionInfo(c.Name); if (!curNames.ContainsKey(r.ISOCurrencySymbol)) curNames[r.ISOCurrencySymbol] = r.CurrencyEnglishName; } catch { }
            }
            string ru, en; RuCur.TryGetValue(code, out ru); curNames.TryGetValue(code, out en);
            return ru ?? en ?? code;
        }
        public static double ConvertAmount(double amount, string from, string to, Dictionary<string, double> r)
        {
            return amount / r[from] * r[to]; // все курсы — к доллару
        }
        public static Dictionary<string, double> ParseRates(string json, out DateTime at)
        {
            var o = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
            if ((o["result"] as string) != "success") throw new Exception("сервис курсов вернул ошибку");
            at = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(o["time_last_update_unix"], CultureInfo.InvariantCulture)).LocalDateTime;
            return ((Dictionary<string, object>)o["rates"]).ToDictionary(kv => kv.Key, kv => Convert.ToDouble(kv.Value, CultureInfo.InvariantCulture));
        }
        async Task LoadRates()
        {
            if (rates == null && SafeFile.Read(F("currency.json")) != null) try { rates = ParseRates(SafeFile.Read(F("currency.json")), out ratesAt); } catch { }
            if (ratesBusy || (rates != null && DateTime.Now - ratesAt < TimeSpan.FromHours(12))) return;
            ratesBusy = true;
            try
            {
                var json = await Get("https://open.er-api.com/v6/latest/USD");
                rates = ParseRates(json, out ratesAt); ratesError = null;
                SafeFile.Write(F("currency.json"), json);
            }
            catch (Exception ex) { ratesError = ex.Message; } // остаётся кэш, если он был
            finally { ratesBusy = false; }
        }

        // карточка модуля «Валюта»: сумма, «из» и «в», обмен местами; ниже — поиск и список валют (популярные сверху), выбранная отмечена
        void ShowFx()
        {
            string from = C("cur.from", "USD"), to = C("cur.to", "EUR"); bool pickFrom = false; string query = "";
            var body = new StackPanel();
            var amount = new TextBox { Text = C("cur.amount", "100"), FontSize = 15, Padding = new Thickness(6, 3, 6, 3), ToolTip = "Сумма" };
            System.Windows.Automation.AutomationProperties.SetName(amount, "Сумма");
            var result = Txt("…", 20, true); result.Margin = new Thickness(0, 8, 0, 0);
            var pairHost = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var searchTb = new TextBox { Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 12, 0, 6), ToolTip = "Страна, валюта или код" };
            System.Windows.Automation.AutomationProperties.SetName(searchTb, "Поиск валюты");
            var list = new StackPanel(); var status = Txt("", 10, false); status.Opacity = 0.55; status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 8, 0, 0);
            var popular = new[] { "USD", "EUR", "GBP", "CHF", "JPY", "CNY", "RUB", "KZT", "UZS", "TRY", "AED", "UAH", "BYN" };
            Action render = null;
            render = () =>
            {
                double a; bool ok = double.TryParse(amount.Text.Replace(" ", "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out a) && a >= 0;
                result.Text = rates == null ? (ratesBusy ? "Загрузка курсов…" : "Нет курсов") : !ok ? "Введите сумму числом" : !rates.ContainsKey(from) || !rates.ContainsKey(to) ? "Нет курса" : ConvertAmount(a, from, to, rates).ToString("N2") + " " + to;
                if (ok) cfg["cur.amount"] = amount.Text.Trim();
                cfg["cur.from"] = from; cfg["cur.to"] = to; SaveCfg(); All(b => b.Render());
                pairHost.Children.Clear();
                Func<string, bool, Border> chip = (code, isFrom) => { var c = Chip((isFrom ? "Из " : "В ") + code, () => { pickFrom = isFrom; render(); }); c.Opacity = pickFrom == isFrom ? 1 : 0.6; return c; };
                pairHost.Children.Add(chip(from, true));
                var sw = Chip("⇅", () => { var x = from; from = to; to = x; render(); }); sw.Margin = new Thickness(6, 0, 6, 0); sw.ToolTip = "Поменять местами"; pairHost.Children.Add(sw);
                pairHost.Children.Add(chip(to, false));
                list.Children.Clear();
                if (rates != null)
                {
                    var q = query.Trim().ToLowerInvariant(); var sel = pickFrom ? from : to;
                    var codes = rates.Keys.Where(c => q == "" ? popular.Contains(c) : c.ToLowerInvariant().StartsWith(q) || CurName(c).ToLowerInvariant().Contains(q))
                        .OrderBy(c => q == "" ? Array.IndexOf(popular, c) : c.ToLowerInvariant() == q ? 0 : 1).ThenBy(c => c).Take(q == "" ? 13 : 30).ToList();
                    var hd = Txt(q == "" ? "ПОПУЛЯРНЫЕ" : "НАЙДЕНО", 9.5, true); hd.Opacity = 0.5; hd.Margin = new Thickness(2, 2, 0, 4); list.Children.Add(hd);
                    foreach (var c0 in codes)
                    {
                        var c = c0; bool on = c == sel;
                        var row = new DockPanel();
                        var right = Txt(on ? c + "  ✓" : c, 11, on); right.Opacity = on ? 1 : 0.6; if (on) right.Foreground = Accent; right.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(right, Dock.Right); row.Children.Add(right);
                        var codeT = Txt(c, 9, true); codeT.HorizontalAlignment = HorizontalAlignment.Center; codeT.VerticalAlignment = VerticalAlignment.Center;
                        row.Children.Add(new Border { Width = 32, Height = 24, CornerRadius = new CornerRadius(6), Background = Frozen(CardFg, 0x14), Margin = new Thickness(0, 0, 10, 0), Child = codeT });
                        var nm = Txt(CurName(c), 12, false); nm.VerticalAlignment = VerticalAlignment.Center; nm.TextTrimming = TextTrimming.CharacterEllipsis; row.Children.Add(nm);
                        var p = Act(row, CurName(c) + " " + c, () => { if (pickFrom) from = c; else to = c; render(); }); p.Padding = new Thickness(6, 4, 8, 4); p.CornerRadius = new CornerRadius(8); p.Margin = new Thickness(0, 1, 0, 1);
                        if (on) p.Background = Frozen(CardFg, 0x12);
                        list.Children.Add(p);
                    }
                    if (codes.Count == 0) { var n = Txt("Ничего не найдено", 11, false); n.Opacity = 0.6; list.Children.Add(n); }
                }
                status.Text = (rates == null ? "" : "Курс от " + ratesAt.ToString("d MMM, HH:mm") + " · open.er-api.com · не котировка в реальном времени") + (ratesError != null ? "\nНет связи: " + ratesError + (rates != null ? " — показан сохранённый курс" : "") : "");
            };
            searchTb.TextChanged += (s, e) => { query = searchTb.Text; render(); };
            amount.TextChanged += (s, e) => render();
            body.Children.Add(amount); body.Children.Add(result); body.Children.Add(pairHost); body.Children.Add(searchTb);
            body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); body.Children.Add(status);
            render();
            LoadRates().ContinueWith(t => Dispatcher.BeginInvoke(render));
            ShowPanel("Валюта", body, 340, amount);
        }

        FrameworkElement CurrencyView()
        {
            var v = new StackPanel();
            string from = C("cur.from", "USD"), to = C("cur.to", "RUB");
            var amount = new TextBox { Text = C("cur.amount", "100"), FontSize = 16, ToolTip = "Сумма" };
            System.Windows.Automation.AutomationProperties.SetName(amount, "Сумма");
            var la = Txt("Сумма", 11, false); la.Opacity = 0.7; la.Margin = new Thickness(0, 0, 0, 4);
            v.Children.Add(la); v.Children.Add(amount);
            var result = Txt("…", 22, true); result.Margin = new Thickness(0, 12, 0, 0); result.TextWrapping = TextWrapping.Wrap;
            var rate = Txt("", 11, false); rate.Opacity = 0.7;
            var status = Txt("", 11, false); status.Opacity = 0.55; status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 10, 0, 0);
            Action recompute = () =>
            {
                double a;
                var ok = double.TryParse(amount.Text.Replace(" ", "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out a);
                if (rates == null) { result.Text = ratesBusy ? "Загрузка курсов…" : "Нет курсов"; rate.Text = ""; }
                else if (!ok || a < 0) { result.Text = "Введите сумму числом"; rate.Text = ""; }
                else if (!rates.ContainsKey(from) || !rates.ContainsKey(to)) { result.Text = "Нет курса для этой валюты"; rate.Text = ""; }
                else
                {
                    result.Text = ConvertAmount(a, from, to, rates).ToString("N2") + " " + to;
                    rate.Text = "1 " + from + " = " + ConvertAmount(1, from, to, rates).ToString("N4") + " " + to;
                    cfg["cur.amount"] = amount.Text.Trim(); cfg["cur.from"] = from; cfg["cur.to"] = to; SaveCfg();
                }
                status.Text = (rates == null ? "" : "Курс от " + ratesAt.ToString("d MMM, HH:mm") + " · open.er-api.com · " + rates.Count + " валют") + (ratesError != null ? "\nНет связи: " + ratesError : "");
            };
            // поле валюты с поиском по коду и названию
            Func<string, Func<string>, Action<string>, TextBox> field = (label, get, set) =>
            {
                var l = Txt(label, 11, false); l.Opacity = 0.7; l.Margin = new Thickness(0, 10, 0, 4); v.Children.Add(l);
                var tb = new TextBox { Text = get() + " — " + CurName(get()), ToolTip = "Код или название валюты" };
                System.Windows.Automation.AutomationProperties.SetName(tb, label);
                var sugg = new StackPanel();
                tb.GotKeyboardFocus += (s, e) => tb.SelectAll();
                tb.TextChanged += (s, e) =>
                {
                    sugg.Children.Clear();
                    if (!tb.IsKeyboardFocused || rates == null) return;
                    var q = tb.Text.Trim().ToLowerInvariant(); if (q == "" || q.Contains(" — ")) return;
                    foreach (var code in rates.Keys.Where(c => c.ToLowerInvariant().StartsWith(q) || CurName(c).ToLowerInvariant().Contains(q)).OrderBy(c => c.ToLowerInvariant() == q ? 0 : 1).ThenBy(c => c).Take(6))
                    {
                        var cd = code;
                        var item = Txt(cd + " — " + CurName(cd), 12, false); item.Padding = new Thickness(8, 4, 8, 4); item.Cursor = Cursors.Hand;
                        item.MouseEnter += (o, a) => item.TextDecorations = TextDecorations.Underline; item.MouseLeave += (o, a) => item.TextDecorations = null;
                        sugg.Children.Add(Act(item, item.Text, () => { set(cd); sugg.Children.Clear(); tb.Text = cd + " — " + CurName(cd); recompute(); }));
                    }
                    if (sugg.Children.Count == 0) { var n = Txt("Ничего не найдено", 11, false); n.Opacity = 0.6; n.Margin = new Thickness(8, 4, 0, 0); sugg.Children.Add(n); }
                };
                v.Children.Add(tb); v.Children.Add(sugg);
                return tb;
            };
            TextBox fromTb = null, toTb = null;
            fromTb = field("Из", () => from, c => from = c);
            var swap = Chip("⇅ Поменять местами", () => { var t = from; from = to; to = t; fromTb.Text = from + " — " + CurName(from); toTb.Text = to + " — " + CurName(to); recompute(); });
            swap.Margin = new Thickness(0, 8, 0, 0); swap.HorizontalAlignment = HorizontalAlignment.Left;
            v.Children.Add(swap);
            toTb = field("В", () => to, c => to = c);
            v.Children.Add(result); v.Children.Add(rate); v.Children.Add(status);
            amount.TextChanged += (s, e) => recompute();
            recompute();
            LoadRates().ContinueWith(t => Dispatcher.BeginInvoke(recompute)); // свежие курсы подставятся, когда загрузятся
            return v;
        }

        // ---------- закрепление программ из меню «Пуск» ----------
        public static bool IsProgram(string f) { return Regex.IsMatch(f, @"\.(lnk|exe|url|appref-ms)$", RegexOptions.IgnoreCase); }
        static List<string> StartMenuApps()
        {
            var all = new List<string>();
            foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.Programs) })
                try { all.AddRange(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Where(IsProgram)); } catch { }
            Func<string, string> name = System.IO.Path.GetFileNameWithoutExtension;
            // деинсталляторы и справку прячем; одинаковые названия (общий и личный ярлык) — один раз
            return all.Where(f => !Regex.IsMatch(name(f), @"uninstall|удал|деинстал|readme|help|справк", RegexOptions.IgnoreCase))
                .GroupBy(f => name(f).ToLowerInvariant()).Select(g => g.First())
                .OrderBy(f => name(f), StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        void ShowPinPicker()
        {
            var v = new StackPanel();
            var search = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
            System.Windows.Automation.AutomationProperties.SetName(search, "Поиск программы");
            v.Children.Add(search);
            var list = new StackPanel();
            v.Children.Add(new ScrollViewer { Content = list, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var progs = StartMenuApps();
            Action render = null;
            render = () =>
            {
                list.Children.Clear();
                var q = search.Text.Trim();
                foreach (var f in progs.Where(f => System.IO.Path.GetFileNameWithoutExtension(f).IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0).Take(150))
                {
                    var file = f; bool on = pinned.Contains(f);
                    var g = new DockPanel { Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = on ? "Открепить" : "Закрепить на панели" };
                    var mark = Txt(on ? "Закреплено" : "", 11, false); mark.Foreground = Accent; mark.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(mark, Dock.Right); g.Children.Add(mark);
                    var ic = IconFor(Target(f)) ?? IconFor(f);
                    g.Children.Add(ic != null ? (UIElement)new Image { Source = ic, Width = 20, Height = 20, Margin = new Thickness(0, 2, 10, 2) } : G("\uE71D", 14));
                    var nm = Txt(System.IO.Path.GetFileNameWithoutExtension(f), 12, false); nm.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(nm);
                    System.Windows.Automation.AutomationProperties.SetName(g, nm.Text);
                    g.MouseEnter += (s, e) => nm.TextDecorations = TextDecorations.Underline; g.MouseLeave += (s, e) => nm.TextDecorations = null;
                    list.Children.Add(Act(g, nm.Text + (on ? ", закреплено" : ""), () => { if (on) pinned.Remove(file); else pinned.Add(file); SavePinned(); render(); }));
                }
                if (list.Children.Count == 0) { var n = Txt("Ничего не найдено", 11, false); n.Opacity = 0.6; list.Children.Add(n); }
            };
            search.TextChanged += (s, e) => render();
            render();
            var browse = Btn("Другая программа…", () =>
            {
                if (panel != null) panel.Close();
                var d = new OpenFileDialog { Filter = "Программы и ярлыки|*.exe;*.lnk;*.url|Все файлы|*.*" };
                if (d.ShowDialog() == true && !pinned.Contains(d.FileName)) { pinned.Add(d.FileName); SavePinned(); }
            });
            browse.HorizontalAlignment = HorizontalAlignment.Left; browse.Margin = new Thickness(0, 10, 0, 0);
            v.Children.Add(browse);
            ShowPanel("Закрепить программу", v, 340, search);
        }

        // ---------- файлы ----------
        static string KnownFolder(string guid)
        {
            IntPtr p; if (N.SHGetKnownFolderPath(new Guid(guid), 0, IntPtr.Zero, out p) != 0) return null;
            try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); }
        }
        static List<string> Newest(string dir, int n, Func<string, bool> filter)
        {
            if (dir == null || !Directory.Exists(dir)) return new List<string>();
            try
            {
                return new DirectoryInfo(dir).EnumerateFileSystemInfos().Where(f => (f.Attributes & FileAttributes.Hidden) == 0 && filter(f.FullName))
                    .OrderByDescending(f => f.LastWriteTime).Take(n).Select(f => f.FullName).ToList();
            }
            catch { return new List<string>(); }
        }

        FrameworkElement FilesView()
        {
            var v = new StackPanel(); var now = DateTime.Now;
            var shot = Btn("Снимок экрана", () => { if (panel != null) panel.Close(); Keys(0x5B, 0x10, 0x53); }, true); // Win+Shift+S: экран, окно или область; в «Ножницах» можно извлечь текст
            shot.HorizontalAlignment = HorizontalAlignment.Left; shot.ToolTip = "Win+Shift+S — весь экран, окно или область";
            v.Children.Add(shot);
            Action<string, DateTime> row = (path, time) =>
            {
                var g = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = path };
                var ago = Txt(Ago(time, now), 11, false); ago.Opacity = 0.6; ago.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(ago, Dock.Right); g.Children.Add(ago);
                var ic = IconFor(path);
                g.Children.Add(ic != null ? (UIElement)new Image { Source = ic, Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0) } : G(Directory.Exists(path) ? "" : "", 13));
                var name = Txt(System.IO.Path.GetFileName(path.TrimEnd('\\')), 12, false); name.Margin = new Thickness(0, 0, 8, 0); g.Children.Add(name);
                System.Windows.Automation.AutomationProperties.SetName(g, name.Text);
                g.MouseEnter += (s, e) => name.TextDecorations = TextDecorations.Underline; g.MouseLeave += (s, e) => name.TextDecorations = null;
                var menu = new ContextMenu(); menu.Items.Add(Item("Показать в папке", () => Start("explorer.exe", "/select,\"" + path + "\""))); g.ContextMenu = menu;
                v.Children.Add(Act(g, name.Text, () => { if (panel != null) panel.Close(); Launch(path); }));
            };

            v.Children.Add(H("Недавние"));
            var recent = new List<string>();
            foreach (var lnk in Newest(Environment.GetFolderPath(Environment.SpecialFolder.Recent), 30, p => p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)))
            {
                var t = Target(lnk);
                if (t != lnk && (File.Exists(t) || Directory.Exists(t)) && !recent.Contains(t)) recent.Add(t);
                if (recent.Count == 7) break;
            }
            foreach (var p in recent) row(p, File.GetLastAccessTime(p) > File.GetLastWriteTime(p) ? File.GetLastAccessTime(p) : File.GetLastWriteTime(p));
            if (recent.Count == 0) { var n = Txt("Пусто", 11, false); n.Opacity = 0.6; v.Children.Add(n); }

            var dl = KnownFolder("374DE290-123F-4565-9164-39C4925E467B");
            var dlHead = new DockPanel(); var openDl = Txt("Открыть папку", 11, false); openDl.Foreground = Accent; openDl.Cursor = Cursors.Hand; openDl.VerticalAlignment = VerticalAlignment.Bottom; openDl.Margin = new Thickness(0, 0, 0, 6);
            var openDlB = Act(openDl, "Открыть папку загрузок", () => { if (dl != null) Launch(dl); });
            DockPanel.SetDock(openDlB, Dock.Right); dlHead.Children.Add(openDlB); dlHead.Children.Add(H("Загрузки"));
            v.Children.Add(dlHead);
            var dls = Newest(dl, 5, p => !p.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase));
            foreach (var p in dls) row(p, File.GetLastWriteTime(p));
            if (dls.Count == 0) { var n = Txt("Пусто", 11, false); n.Opacity = 0.6; v.Children.Add(n); }

            var shotsDir = KnownFolder("B7BEDE81-DF94-4682-A7D8-57A52620B86F") ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
            v.Children.Add(H("Скриншоты"));
            var shots = Newest(shotsDir, 6, p => Regex.IsMatch(p, @"\.(png|jpe?g)$", RegexOptions.IgnoreCase));
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
            foreach (var p in shots)
            {
                var path = p;
                try
                {
                    var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 200; bi.UriSource = new Uri(path); bi.EndInit(); bi.Freeze();
                    var cell = new Press { Height = 64, Margin = new Thickness(2), CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, ToolTip = System.IO.Path.GetFileName(path) + " · " + Ago(File.GetLastWriteTime(path), now),
                        Background = new ImageBrush(bi) { Stretch = Stretch.UniformToFill } };
                    System.Windows.Automation.AutomationProperties.SetName(cell, "Скриншот " + System.IO.Path.GetFileName(path));
                    cell.Invoke = () => { if (panel != null) panel.Close(); Launch(path); }; cell.MouseLeftButtonUp += (s, e) => cell.Invoke();
                    var menu = new ContextMenu(); menu.Items.Add(Item("Показать в папке", () => Start("explorer.exe", "/select,\"" + path + "\""))); cell.ContextMenu = menu;
                    grid.Children.Add(cell);
                }
                catch { } // повреждённый файл пропускаем
            }
            if (shots.Count == 0) { var n = Txt("В папке «Изображения\\Снимки экрана» пока пусто", 11, false); n.Opacity = 0.6; v.Children.Add(n); }
            else v.Children.Add(grid);
            return v;
        }

        void ShowNotes()
        {
            var box = new TextBox { Text = unsavedNote ?? SafeFile.Read(F("notes.txt")) ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 220, Padding = new Thickness(6), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            // сохраняем через полсекунды после последнего нажатия и при закрытии карточки, а не на каждую букву
            var save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            bool dirty = unsavedNote != null;
            var status = Txt(dirty ? "Не сохранено с прошлого раза" : "", 10, false); status.Opacity = 0.7; status.Margin = new Thickness(0, 6, 0, 0); status.TextWrapping = TextWrapping.Wrap;
            Border retry = null;
            Func<bool> flush = () =>
            {
                save.Stop(); if (!dirty) return true;
                string err;
                if (SaveText(F("notes.txt"), box.Text, out err)) { dirty = false; unsavedNote = null; live.Note = null; status.Text = "Сохранено"; retry.Visibility = Visibility.Collapsed; return true; }
                unsavedNote = box.Text; status.Text = "Не сохранено: " + err; retry.Visibility = Visibility.Visible; return false; // флаг не сбрасываем — повторим
            };
            retry = Btn("Повторить сохранение", () => flush(), true); retry.HorizontalAlignment = HorizontalAlignment.Left; retry.Margin = new Thickness(0, 8, 0, 0); retry.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
            save.Tick += (s, e) => flush();
            box.TextChanged += (s, e) => { dirty = true; save.Stop(); save.Start(); };
            box.Unloaded += (s, e) => { if (!flush()) Notify("Заметка не сохранена", "Текст сохранён в памяти FlowBar и не потерян. " + status.Text, true, "\uE7BA", "Повторить", RetryNote, "notes"); };
            var nb = new StackPanel(); nb.Children.Add(box); nb.Children.Add(status); nb.Children.Add(retry);
            ShowPanel("Заметки", nb, 340, box);
        }

        static string unsavedNote; // текст, который не удалось записать: не теряется до удачной записи
        public static bool SaveText(string path, string text, out string err)
        {
            try { SafeFile.Write(path, text); err = null; return true; }
            catch (Exception ex) { Log(ex); err = ex.Message; return false; }
        }
        void RetryNote()
        {
            string err; if (unsavedNote == null) return;
            if (SaveText(F("notes.txt"), unsavedNote, out err)) { unsavedNote = null; live.Note = null; Notify("Заметка сохранена", "Текст записан в notes.txt.", true, "\uE73E"); }
            else Notify("Заметка всё ещё не сохранена", err, true, "\uE7BA", "Повторить", RetryNote, "notes");
        }

        // ---------- календари iCal ----------
        internal static bool IsFeedUrl(string url) { Uri u; return Uri.TryCreate(FeedUrl(url), UriKind.Absolute, out u) && u.Scheme == "https"; } // http не берём: закрытый адрес календаря ушёл бы открытым текстом
        static string FeedUrl(string feed) { var u = feed.Split('\t')[0].Trim(); return u.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ? "https://" + u.Substring(9) : u; }
        static string FeedName(string feed)
        {
            var p = feed.Split('\t');
            if (p.Length > 1 && p[1].Trim() != "") return p[1].Trim();
            Uri u; return Uri.TryCreate(FeedUrl(feed), UriKind.Absolute, out u) ? u.Host : "Календарь";
        }
        static void SaveFeeds() { SafeFile.WriteLines(F("calendars.txt"), calFeeds); }

        async void UpdateCalendars()
        {
            if (calBusy) return;
            if (calFeeds.Count == 0) { calEvents = new List<CalEvent>(); calError = null; All(b => b.Render()); return; }
            calBusy = true;
            var feeds = calFeeds.ToList(); DateTime from = DateTime.Today.AddDays(-31), to = DateTime.Today.AddDays(63);
            try
            {
                var r = await Task.Run(() =>
                {
                    var all = new List<CalEvent>(); var names = new Dictionary<string, string>(); var errs = new List<string>();
                    foreach (var f in feeds)
                    {
                        try
                        {
                            string text; var cache = CachePath("cal", FeedUrl(f));
                            try
                            {
                                text = GetSync(FeedUrl(f));
                                if (!text.Contains("BEGIN:VCALENDAR")) throw new Exception("по ссылке не календарь iCal");
                                try { SafeFile.Write(cache, text); } catch { }
                            }
                            catch (Exception ex)
                            {
                                // источник недоступен — его события не пропадают: берём последнюю удачную загрузку
                                text = SafeFile.Read(cache); if (text == null) throw;
                                errs.Add(FeedName(f) + ": " + ex.Message + " — показаны данные от " + Ago(File.GetLastWriteTime(cache)));
                            }
                            string nm; var evs = Ics.Parse(text, from, to, out nm);
                            if (nm != null) names[f] = nm;
                            var label = f.Contains("\t") ? FeedName(f) : nm ?? FeedName(f);
                            foreach (var e in evs) e.Cal = label;
                            all.AddRange(evs);
                        }
                        catch (Exception ex) { errs.Add(FeedName(f) + ": " + ex.Message); }
                    }
                    return Tuple.Create(all, names, errs);
                });
                calEvents = r.Item1.OrderBy(e => e.Start).ToList();
                calError = r.Item3.Count == 0 ? null : string.Join("\n", r.Item3);
                calUpdated = DateTime.Now;
                // запоминаем названия календарей из X-WR-CALNAME
                bool changed = false;
                for (int i = 0; i < calFeeds.Count; i++)
                    if (!calFeeds[i].Contains("\t") && r.Item2.ContainsKey(calFeeds[i])) { calFeeds[i] = calFeeds[i] + "\t" + r.Item2[calFeeds[i]]; changed = true; }
                if (changed) SaveFeeds();
            }
            catch (Exception ex) { Log(ex); }
            finally { calBusy = false; }
            All(b => b.Render());
        }

        // события календарей + сбросы лимитов ИИ в общей ленте
        static List<CalEvent> AllEvents()
        {
            var list = calEvents.ToList(); var now = DateTime.Now;
            if (codex != null)
            {
                if (codex.R5 > now) list.Add(new CalEvent { Title = "Сброс 5-часового лимита Codex", Cal = "Лимиты ИИ", Start = codex.R5, End = codex.R5 });
                if (codex.RW > now) list.Add(new CalEvent { Title = "Сброс недельного лимита Codex", Cal = "Лимиты ИИ", Start = codex.RW, End = codex.RW });
            }
            var bs = ClaudeUsage.BlockStart(claude.Msgs, now);
            if (bs != null) list.Add(new CalEvent { Title = "Сброс 5-часового окна Claude", Cal = "Лимиты ИИ", Start = bs.Value.AddHours(5), End = bs.Value.AddHours(5) });
            return list.OrderBy(e => e.Start).ToList();
        }
        static bool OnDay(CalEvent e, DateTime d) { return e.Start < d.AddDays(1) && (e.End > d || (e.End == e.Start && e.Start >= d)); }
        static string When(CalEvent e)
        {
            if (e.AllDay) return (e.End - e.Start).TotalDays > 1 ? "до " + e.End.AddDays(-1).ToString("d MMM") : "весь день";
            return e.End > e.Start ? e.Start.ToString("HH:mm") + "–" + e.End.ToString("HH:mm") : e.Start.ToString("HH:mm");
        }
        static Brush CalBrush(string cal)
        {
            if (cal == "Лимиты ИИ") return new SolidColorBrush(Color.FromRgb(0xA0, 0x80, 0xF0));
            var pal = new[] { "#4CC2FF", "#6CCB5F", "#F7B955", "#F06E6E", "#C586F7", "#4FD1C5" };
            int h = 0; foreach (var ch in cal ?? "") h = h * 31 + ch;
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(pal[Math.Abs(h % pal.Length)]));
        }


        // ---------- уведомления и автоматизации ----------
        // Каждая автоматизация — отдельный переключатель в settings.txt (auto.<ключ>=1/0)
        static readonly string[][] AutoDefs = {
            new[] { "meetRemind", "1", "Напоминание о встрече", "Звук и уведомление за 5 минут до события из календаря" },
            new[] { "pauseMusic", "1", "Пауза музыки перед встречей", "Ставит музыку на паузу за указанное число минут до начала события" },
            new[] { "aiWarn", "1", "Предупреждение о лимите Codex", "Уведомляет, когда 5-часовой или недельный лимит Codex превышает порог" },
            new[] { "subs", "1", "Продление подписок", "За сутки до события календаря со словами «подписка», «продление», «оплата», «renewal», «subscription»" },
            new[] { "goal", "0", "Цель по экранному времени", "Уведомляет один раз в день, когда экранное время достигает заданного числа часов" },
            new[] { "quiet", "0", "Тихие часы", "В указанное время FlowBar не показывает уведомления и не издаёт звуков" },
        };
        static bool On(string key) { var d = AutoDefs.First(x => x[0] == key); return C("auto." + key, d[1]) == "1"; }
        static HashSet<string> autoFired = new HashSet<string>();
        static List<string> autoLog = new List<string>();
        static Window toast;
        static readonly Regex SubsRx = new Regex(@"подписк|продлен|оплат|renew|subscription", RegexOptions.IgnoreCase);

        public static int? ParseHm(string s)
        {
            DateTime t; return DateTime.TryParseExact((s ?? "").Trim(), new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? t.Hour * 60 + t.Minute : (int?)null;
        }
        // Интервал может переходить через полночь: 22:00–08:00
        public static bool InWindow(DateTime now, int from, int to)
        {
            int m = now.Hour * 60 + now.Minute;
            return from == to ? false : from < to ? m >= from && m < to : m >= from || m < to;
        }
        static bool QuietNow
        {
            get
            {
                if (!On("quiet")) return false;
                int? f = ParseHm(C("auto.quietFrom", "22:00")), t = ParseHm(C("auto.quietTo", "08:00"));
                return f != null && t != null && InWindow(DateTime.Now, f.Value, t.Value);
            }
        }



        async void RunAutomations()
        {
            if (DemoMode) return; // в примере уведомления и автоматизации не срабатывают
            var now = DateTime.Now;
            try
            {
                // пауза музыки перед встречей
                int lead; if (!int.TryParse(C("auto.pauseMin", "1"), out lead)) lead = 1;
                var meet = calEvents.FirstOrDefault(e => !e.AllDay && e.Start > now && e.Start - now <= TimeSpan.FromMinutes(Math.Max(0, lead) + 0.1));
                if (On("pauseMusic") && meet != null && mediaMgr != null && !autoFired.Contains("pause|" + meet.Title + meet.Start.ToString("s")))
                {
                    var s = mediaMgr.GetCurrentSession();
                    if (s != null && s.GetPlaybackInfo().PlaybackStatus == MC.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && await s.TryPauseAsync())
                    {
                        autoFired.Add("pause|" + meet.Title + meet.Start.ToString("s"));
                        Notify("Музыка на паузе", "Автоматизация «Пауза музыки перед встречей»: через " + Until(meet.Start - now) + " начнётся «" + meet.Title + "».", false, "\uE769", "Продолжить", () => Keys(0xB3), "media");
                    }
                }
                // лимиты Codex
                int pct; if (!int.TryParse(C("auto.aiPct", "80"), out pct)) pct = 80;
                if (On("aiWarn") && codex != null)
                {
                    Action<string, double, DateTime> warn = (name, used, reset) =>
                    {
                        if (double.IsNaN(used)) return;
                        foreach (var level in new[] { Math.Max(1, pct), 95 }.Distinct().OrderByDescending(x => x))
                        {
                            if (used < level) continue;
                            if (autoFired.Add("ai|" + name + "|" + level + "|" + reset.ToString("s")))
                                Notify("Codex: " + Math.Round(used) + "% " + name, "Лимит сбросится " + reset.ToString(reset.Date == now.Date ? "в HH:mm" : "d MMM в HH:mm") + " (через " + Until(reset - now) + ")", false, "\uE7BA", "Подробнее", ShowCodex, "codex");
                            break; // за раз — одно уведомление о самом высоком пороге
                        }
                    };
                    warn("5-часового окна", codex.Eff5(now), codex.R5);
                    warn("недельного лимита", codex.EffW(now), codex.RW);
                }
                // цель по экранному времени — одно уведомление в день
                int goalH; if (!int.TryParse(C("auto.goalH", "4"), out goalH)) goalH = 4;
                if (On("goal") && screen.Total >= goalH * 3600)
                    Notify("Цель по экранному времени", "Сегодня за компьютером уже " + ScreenTime.Fmt(screen.Total) + " — больше " + goalH + " ч.", false, "\uE823", "Подробнее", ShowScreenTime, "screen", "goal|" + Today);
                // продление подписок — за сутки
                if (On("subs"))
                    foreach (var e in calEvents.Where(e => e.Start > now && e.Start - now <= TimeSpan.FromHours(24) && SubsRx.IsMatch(e.Title ?? "")))
                        if (autoFired.Add("subs|" + e.Title + e.Start.ToString("s")))
                            Notify("Скоро продление подписки", e.Title + " — " + e.Start.ToString(e.AllDay ? "d MMMM" : "d MMMM, HH:mm"), false, "\uE787", "Календарь", ShowCalendar, "event");
            }
            catch (Exception ex) { Log(ex); }
        }

        void ShowAutomations() { ShowPanel("Автоматизации", AutomationsBody(), 340, null); }
        StackPanel AutomationsBody()
        {
            var body = new StackPanel();
            foreach (var def in AutoDefs)
            {
                var key = def[0];
                var box = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
                var cb = new CheckBox { Content = Txt(def[2], 12, true), IsChecked = On(key), VerticalContentAlignment = VerticalAlignment.Center };
                RoutedEventHandler onA = (s, e) => { cfg["auto." + key] = cb.IsChecked == true ? "1" : "0"; SaveCfg(); }; cb.Checked += onA; cb.Unchecked += onA;
                box.Children.Add(cb);
                var desc = Txt(def[3], 11, false); desc.TextWrapping = TextWrapping.Wrap; desc.Opacity = 0.7; desc.Margin = new Thickness(20, 2, 0, 0);
                box.Children.Add(desc);

                // параметры: подпись + поле, сохраняется при вводе корректного значения
                Action<string, string, string, Func<string, bool>> field = (label, k, d, valid) =>
                {
                    var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 6, 0, 0) };
                    var l = Txt(label, 11, false); l.VerticalAlignment = VerticalAlignment.Center; l.Margin = new Thickness(0, 0, 8, 0); line.Children.Add(l);
                    var tb = new TextBox { Text = C(k, d), Width = 64, ToolTip = label };
                    System.Windows.Automation.AutomationProperties.SetName(tb, label);
                    var err = Txt("", 11, false); err.Foreground = Frozen(Color.FromRgb(0xE8, 0x5A, 0x5A), 255); err.VerticalAlignment = VerticalAlignment.Center; err.Margin = new Thickness(8, 0, 0, 0);
                    tb.TextChanged += (s, e) =>
                    {
                        if (valid(tb.Text)) { cfg[k] = tb.Text.Trim(); SaveCfg(); err.Text = ""; }
                        else err.Text = "неверное значение";
                    };
                    line.Children.Add(tb); line.Children.Add(err);
                    box.Children.Add(line);
                };
                Func<string, bool> minutes = v => { int n; return int.TryParse(v, out n) && n >= 0 && n <= 60; };
                if (key == "pauseMusic") field("За сколько минут", "auto.pauseMin", "1", minutes);
                if (key == "goal") field("Часов в день", "auto.goalH", "4", v => { int n; return int.TryParse(v, out n) && n >= 1 && n <= 16; });
                if (key == "aiWarn") field("Порог, %", "auto.aiPct", "80", v => { int n; return int.TryParse(v, out n) && n >= 1 && n <= 100; });
                if (key == "quiet")
                {
                    field("С", "auto.quietFrom", "22:00", v => ParseHm(v) != null);
                    field("До", "auto.quietTo", "08:00", v => ParseHm(v) != null);
                }
                body.Children.Add(box);
            }

            var test = Txt("Показать пример уведомления", 12, true); test.Foreground = Accent; test.Cursor = Cursors.Hand; test.Margin = new Thickness(0, 2, 0, 0);
            body.Children.Add(Act(test, test.Text, () => Notify("Пример уведомления", QuietNow ? "Сейчас тихие часы — уведомление записано в журнал без показа" : "Так будут выглядеть напоминания FlowBar")));

            body.Children.Add(H("Последние срабатывания"));
            if (autoLog.Count == 0) { var none = Txt("Пока ничего", 11, false); none.Opacity = 0.6; body.Children.Add(none); }
            foreach (var l in autoLog) { var t = Txt(l, 11, false); t.Opacity = 0.8; body.Children.Add(t); }
            var foot = Txt("Сценарии заданы заранее — это не конструктор автоматизаций. Обработка запросов на ревью появится вместе с интеграцией GitHub.", 11, false);
            foot.TextWrapping = TextWrapping.Wrap; foot.Opacity = 0.55; foot.Margin = new Thickness(0, 14, 0, 0); body.Children.Add(foot);
            return body;
        }

        // Календарь: месяц слева, повестка выбранного дня справа; на узком экране — одна колонка.
        // Подключение ссылок iCal — в «Подключения → Календари» (CalFeedsEditor); здесь просмотр и фильтр календарей.
        void ShowCalendar()
        {
            DateTime shown = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), selected = DateTime.Today;
            var hidden = new HashSet<string>(C("cal.hidden", "").Split('|').Where(x => x != ""));
            Func<CalEvent, bool> vis = e => !hidden.Contains(e.Cal ?? "");
            bool wide = scr.WorkingArea.Width / MonScale >= 760;
            var monthHost = new StackPanel(); var filterHost = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var agenda = new StackPanel(); var foot = Txt("", 10, false); foot.Opacity = 0.6; foot.TextWrapping = TextWrapping.Wrap; foot.Margin = new Thickness(0, 10, 0, 0);
            var left = new StackPanel { Width = 300 }; left.Children.Add(monthHost); left.Children.Add(filterHost);
            var right = new StackPanel { Width = wide ? 264 : 300 };
            right.Children.Add(new ScrollViewer { Content = agenda, MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); right.Children.Add(foot);
            Action render = null, renderMonth = null, renderFilters = null;
            Func<string, string, Action, Press> navBtn = (glyph, name, a) =>
            {
                var b = new Press { MinWidth = 30, Height = 28, Padding = new Thickness(8, 0, 8, 0), CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Colors.Transparent), Cursor = Cursors.Hand, ToolTip = name, Invoke = a, Margin = new Thickness(2, 0, 0, 0) };
                b.Child = glyph.Length == 1 && glyph[0] >= '' ? (UIElement)G(glyph, 11) : Txt(glyph, 11, true);
                System.Windows.Automation.AutomationProperties.SetName(b, name);
                b.MouseEnter += (s, e) => Fade(b, Color.FromArgb(0x22, CardFg.R, CardFg.G, CardFg.B), 120);
                b.MouseLeave += (s, e) => Fade(b, Colors.Transparent, 160);
                b.MouseLeftButtonUp += (s, e) => a();
                return b;
            };
            renderMonth = () =>
            {
                monthHost.Children.Clear();
                var fmt = CultureInfo.CurrentCulture.DateTimeFormat;
                // шапка: месяц и год, «Сегодня», переключение месяцев — без дублирующей крупной даты
                var hdr = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var next = navBtn("", "Следующий месяц", () => { shown = shown.AddMonths(1); renderMonth(); }); DockPanel.SetDock(next, Dock.Right); hdr.Children.Add(next);
                var prev = navBtn("", "Предыдущий месяц", () => { shown = shown.AddMonths(-1); renderMonth(); }); DockPanel.SetDock(prev, Dock.Right); hdr.Children.Add(prev);
                var today = navBtn("Сегодня", "Перейти к сегодняшнему дню", () => { selected = DateTime.Today; shown = new DateTime(selected.Year, selected.Month, 1); renderMonth(); render(); }); DockPanel.SetDock(today, Dock.Right); hdr.Children.Add(today);
                var mname = fmt.GetMonthName(shown.Month);
                var title = Txt(char.ToUpper(mname[0]) + mname.Substring(1) + " " + shown.Year, 15, true); title.VerticalAlignment = VerticalAlignment.Center;
                hdr.Children.Add(title);
                monthHost.Children.Add(hdr);

                var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7 };
                for (int i = 0; i < 7; i++)
                {
                    var dn = fmt.GetShortestDayName((DayOfWeek)((i + 1) % 7));
                    var t = Txt(char.ToUpper(dn[0]) + dn.Substring(1), 10, false); t.HorizontalAlignment = HorizontalAlignment.Center; t.Opacity = 0.55; t.Margin = new Thickness(0, 0, 0, 4);
                    grid.Children.Add(t);
                }
                // метки дня — цвета календарей, в которых есть события (до трёх)
                var marks = new Dictionary<DateTime, List<string>>();
                foreach (var e in AllEvents().Where(vis))
                    for (var d = e.Start.Date; d <= (e.End > e.Start ? e.End.AddTicks(-1) : e.Start).Date && d < shown.AddMonths(2); d = d.AddDays(1))
                    {
                        if (d < shown.AddDays(-7)) continue;
                        List<string> l; if (!marks.TryGetValue(d, out l)) marks[d] = l = new List<string>();
                        if (!l.Contains(e.Cal ?? "")) l.Add(e.Cal ?? "");
                    }
                var first = shown.AddDays(-(((int)shown.DayOfWeek + 6) % 7));
                for (int i = 0; i < 42; i++)
                {
                    var d = first.AddDays(i); var day = d;
                    bool isToday = d == DateTime.Today, sel = d == selected, inMonth = d.Month == shown.Month;
                    List<string> cals; marks.TryGetValue(d, out cals);
                    var cellText = Txt(d.Day.ToString(), 12, isToday || cals != null); cellText.HorizontalAlignment = HorizontalAlignment.Center;
                    if (isToday) cellText.Foreground = Frozen(OnAccent, 255);
                    var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    stack.Children.Add(cellText);
                    var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Height = 5, Margin = new Thickness(0, 2, 0, 0) };
                    if (cals != null) foreach (var cal in cals.Take(3)) dots.Children.Add(new Ellipse { Width = 4.5, Height = 4.5, Margin = new Thickness(1, 0, 1, 0), Fill = isToday ? Frozen(OnAccent, 255) : CalBrush(cal) });
                    stack.Children.Add(dots);
                    var cell = new Press
                    {
                        Height = 38, Margin = new Thickness(1.5), CornerRadius = new CornerRadius(9), Cursor = Cursors.Hand, Child = stack,
                        Background = isToday ? (Brush)Accent : new SolidColorBrush(inMonth ? Color.FromArgb(0x0E, CardFg.R, CardFg.G, CardFg.B) : Colors.Transparent),
                        BorderThickness = new Thickness(sel && !isToday ? 1.5 : 0), BorderBrush = Accent, Opacity = inMonth ? 1 : 0.35, ToolTip = d.ToString("D")
                    };
                    if (!isToday)
                    {
                        var rest = ((SolidColorBrush)cell.Background).Color;
                        cell.MouseEnter += (s, e) => Fade(cell, Color.FromArgb(0x24, CardFg.R, CardFg.G, CardFg.B), 100);
                        cell.MouseLeave += (s, e) => Fade(cell, rest, 140);
                    }
                    cell.Invoke = () => { selected = day; if (day.Month != shown.Month) shown = new DateTime(day.Year, day.Month, 1); renderMonth(); render(); };
                    cell.MouseLeftButtonUp += (s, e) => cell.Invoke();
                    System.Windows.Automation.AutomationProperties.SetName(cell, d.ToString("D") + (cals != null ? ", есть события" : ""));
                    grid.Children.Add(cell);
                }
                monthHost.Children.Add(grid);
            };
            // фильтр календарей — компактные метки; скрытый календарь пропадает и из меток дней, и из повестки
            renderFilters = () =>
            {
                filterHost.Children.Clear();
                var names = AllEvents().Select(e => e.Cal ?? "").Concat(calFeeds.Select(FeedName)).Where(n => n != "").Distinct().ToList();
                if (names.Count < 2) return;
                foreach (var n0 in names)
                {
                    var n = n0; bool on = !hidden.Contains(n);
                    var chip = new StackPanel { Orientation = Orientation.Horizontal };
                    chip.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = CalBrush(n), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                    var nt = Txt(n, 10, false); nt.MaxWidth = 130; nt.TextTrimming = TextTrimming.CharacterEllipsis; chip.Children.Add(nt);
                    var b = new Press { Child = chip, Padding = new Thickness(8, 3, 9, 4), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, Opacity = on ? 1 : 0.4,
                        Background = new SolidColorBrush(Color.FromArgb(0x14, CardFg.R, CardFg.G, CardFg.B)), ToolTip = (on ? "Скрыть " : "Показать ") + n };
                    System.Windows.Automation.AutomationProperties.SetName(b, (on ? "Скрыть календарь " : "Показать календарь ") + n);
                    b.Invoke = () => { if (!hidden.Remove(n)) hidden.Add(n); cfg["cal.hidden"] = string.Join("|", hidden); SaveCfg(); renderFilters(); renderMonth(); render(); };
                    b.MouseLeftButtonUp += (s, e) => b.Invoke();
                    filterHost.Children.Add(b);
                }
            };
            Action<CalEvent, StackPanel, bool> row = (e, into, withDate) =>
            {
                var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var time = Txt(withDate ? e.Start.ToString("d MMM") : When(e), 10, false); time.Opacity = 0.7; time.Margin = new Thickness(0, 1, 4, 0); time.TextWrapping = TextWrapping.Wrap;
                var bar = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = CalBrush(e.Cal), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 1) };
                var info = new StackPanel();
                var t = Txt(e.Title, 12, true); t.TextTrimming = TextTrimming.CharacterEllipsis; t.ToolTip = e.Title; info.Children.Add(t); // длинное название не растягивает карточку
                var sub = Txt((withDate ? When(e) + " · " : "") + e.Cal + (string.IsNullOrEmpty(e.Location) ? "" : " · " + e.Location), 10, false); sub.Opacity = 0.6; sub.TextTrimming = TextTrimming.CharacterEllipsis; info.Children.Add(sub);
                Grid.SetColumn(bar, 1); Grid.SetColumn(info, 2);
                g.Children.Add(time); g.Children.Add(bar); g.Children.Add(info);
                into.Children.Add(g);
            };
            render = () =>
            {
                agenda.Children.Clear();
                var d = selected.Date; var all = AllEvents().Where(vis).ToList();
                var day = all.Where(e => OnDay(e, d)).OrderBy(e => !e.AllDay).ThenBy(e => e.Start).ToList();
                var hd = new DockPanel { Margin = new Thickness(0, 2, 0, 8) };
                var cnt = Txt(day.Count == 0 ? "" : day.Count + " " + (day.Count % 10 == 1 && day.Count % 100 != 11 ? "событие" : day.Count % 10 >= 2 && day.Count % 10 <= 4 && (day.Count % 100 < 12 || day.Count % 100 > 14) ? "события" : "событий"), 10, false);
                cnt.Opacity = 0.55; cnt.VerticalAlignment = VerticalAlignment.Bottom; DockPanel.SetDock(cnt, Dock.Right); hd.Children.Add(cnt);
                var dname = d == DateTime.Today ? "Сегодня" : d == DateTime.Today.AddDays(1) ? "Завтра" : d.ToString("dddd, d MMMM");
                hd.Children.Add(Txt(char.ToUpper(dname[0]) + dname.Substring(1), 13, true)); agenda.Children.Add(hd);
                if (day.Count == 0)
                {
                    // нет подключений и пустой день — разные состояния
                    if (calFeeds.Count == 0)
                    {
                        var none = Txt("Календари не подключены. Пока видны только сбросы лимитов ИИ.", 11, false); none.Opacity = 0.65; none.TextWrapping = TextWrapping.Wrap; agenda.Children.Add(none);
                        var cb = Btn("Подключить календарь", () => ShowSettingsPage("connect", 1), true); cb.HorizontalAlignment = HorizontalAlignment.Left; cb.Margin = new Thickness(0, 8, 0, 0); agenda.Children.Add(cb);
                    }
                    else { var none = Txt("Событий нет", 11, false); none.Opacity = 0.6; agenda.Children.Add(none); }
                }
                foreach (var e in day) row(e, agenda, false);
                var later = all.Where(e => e.Start >= d.AddDays(1) && e.Start < d.AddDays(15)).OrderBy(e => e.Start).Take(4).ToList();
                if (later.Count > 0)
                {
                    var lh = Txt("Далее", 11, true); lh.Opacity = 0.8; lh.Margin = new Thickness(0, 12, 0, 2); agenda.Children.Add(lh);
                    foreach (var e in later) row(e, agenda, true);
                }
                foot.Text = calBusy ? "Обновление календарей…" : calError != null ? "Ошибки: " + calError :
                    calFeeds.Count == 0 ? "" :
                    calUpdated == default(DateTime) ? "" : "Обновлено в " + calUpdated.ToString("HH:mm") + " · только просмотр, обновление раз в 15 минут";
            };
            renderMonth(); renderFilters(); render();
            // календари обновились — перерисовать, сохранив выбранный день
            var seen = calUpdated; bool seenBusy = calBusy;
            Action tick = () => { if (calUpdated != seen || calBusy != seenBusy) { seen = calUpdated; seenBusy = calBusy; renderMonth(); renderFilters(); render(); } };
            FrameworkElement body;
            if (wide)
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(33) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var sep = new Border { Width = 1, Background = new SolidColorBrush(Color.FromArgb(0x22, CardFg.R, CardFg.G, CardFg.B)), HorizontalAlignment = HorizontalAlignment.Center };
                Grid.SetColumn(sep, 1); Grid.SetColumn(right, 2);
                g.Children.Add(left); g.Children.Add(sep); g.Children.Add(right); body = g;
            }
            else { var sp = new StackPanel(); right.Margin = new Thickness(0, 14, 0, 0); sp.Children.Add(left); sp.Children.Add(right); body = new ScrollViewer { Content = sp, MaxHeight = 760, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; }
            if (C("clock.daylight", "0") == "1") right.Children.Add(DaylightDetails());
            ShowPanel("Календарь", body, wide ? 640 : 330, null, tick);
        }

        // подключённые календари iCal: список, удаление, добавление ссылки — для «Настройки → Подключения»
        FrameworkElement CalFeedsEditor()
        {
            var box = new StackPanel { Margin = new Thickness(0, 6, 0, 0), MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
            var feedsPanel = new StackPanel(); box.Children.Add(feedsPanel);
            var status = Txt("", 10, false); status.Opacity = 0.6; status.TextWrapping = TextWrapping.Wrap;
            Action renderFeeds = null;
            renderFeeds = () =>
            {
                feedsPanel.Children.Clear();
                foreach (var feed in calFeeds.ToList())
                {
                    var f = feed;
                    var dp = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                    var del = Cross("Отключить календарь " + FeedName(f), () => { calFeeds.Remove(f); SaveFeeds(); renderFeeds(); UpdateCalendars(); });
                    DockPanel.SetDock(del, Dock.Right); dp.Children.Add(del);
                    dp.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = CalBrush(FeedName(f)), Margin = new Thickness(0, 0, 8, 0) });
                    dp.Children.Add(Txt(FeedName(f), 12, false));
                    feedsPanel.Children.Add(dp);
                }
                status.Text = calBusy ? "Обновление…" : calError != null ? "Ошибки: " + calError :
                    calFeeds.Count == 0 ? "Вставьте ссылку и нажмите Enter. Google: настройки календаря → «Закрытый адрес в формате iCal». Outlook: Параметры → Общие календари → ICS. iCloud: общий доступ → публичный календарь." :
                    calUpdated == default(DateTime) ? "" : "Обновлено в " + calUpdated.ToString("HH:mm") + " · только просмотр, обновление раз в 15 минут";
            };
            renderFeeds();
            var input = new TextBox { Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 6, 0, 4), ToolTip = "Ссылка на календарь (https://… или webcal://…) — Enter, чтобы добавить" };
            System.Windows.Automation.AutomationProperties.SetName(input, "Ссылка на календарь iCal");
            input.KeyDown += async (s, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Enter) return;
                var url = input.Text.Trim();
                if (!IsFeedUrl(url))
                { status.Text = "Нужна ссылка вида https://… или webcal://…"; return; }
                if (calFeeds.Any(x => FeedUrl(x) == FeedUrl(url))) { status.Text = "Этот календарь уже подключён"; return; }
                calFeeds.Add(url); SaveFeeds(); input.Text = ""; status.Text = "Загрузка…";
                UpdateCalendars();
                while (calBusy) await Task.Delay(200);
                renderFeeds();
            };
            box.Children.Add(input); box.Children.Add(status);
            var open = Btn("Открыть календарь", () => Primary.ShowCalendar()); open.HorizontalAlignment = HorizontalAlignment.Left; open.Margin = new Thickness(0, 8, 0, 0); box.Children.Add(open);
            return box;
        }

        static int stPeriod; // 0 — сегодня, 1 — 7 дней
        // понятное название вместо технического: «WhatsApp.Root» → «WhatsApp», «SnippingTool.exe» → «SnippingTool», системные хосты — «Windows»
        public static string NiceApp(string n)
        {
            if (string.IsNullOrEmpty(n)) return "Другое";
            var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "SearchHost", "Windows" }, { "ShellHost", "Windows" }, { "ShellExperienceHost", "Windows" }, { "StartMenuExperienceHost", "Windows" },
                { "Windows Terminal Host", "Терминал" }, { "ApplicationFrameHost", "Приложения Windows" }, { "LockApp", "Экран блокировки" }, { "Система", "Windows" }, { "explorer", "Проводник" }, { "SnippingTool", "Ножницы" } };
            var s = n.Trim();
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            if (s.EndsWith(".Root", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 5);
            string k; return known.TryGetValue(s, out k) ? k : s;
        }
        void ShowScreenTime()
        {
            // пути программ для значков: у запущенных берём сразу, остальные запомнятся при следующем использовании
            foreach (var pr in Process.GetProcesses()) { try { var ep = ExePath((uint)pr.Id); if (ep != null) ScreenTime.Seen(AppName(ep), ep); } catch { } finally { pr.Dispose(); } }
            screen.Save();
            var days = Enumerable.Range(0, 14).Select(d => d == 0 ? screen : ScreenTime.Load(DateTime.Now.AddDays(-d).ToString("yyyy-MM-dd"))).ToList();
            var body = new StackPanel(); var segHost = new StackPanel(); var host = new StackPanel(); body.Children.Add(segHost); body.Children.Add(host);
            Action render = null;
            render = () =>
            {
                // период меняет и график, и список приложений — данные одни и те же
                segHost.Children.Clear(); segHost.Children.Add(Seg(new[] { "Сегодня", "7 дней", "Месяц", "Лента" }, stPeriod, i => { stPeriod = i; render(); }, true));
                host.Children.Clear();
                if (stPeriod == 2) { host.Children.Add(ScreenMonth()); return; }
                if (stPeriod == 3) { host.Children.Add(ScreenTimeline(screen)); return; }
                bool day = stPeriod == 0;
                int total = day ? days[0].Total : days.Take(7).Sum(x => x.Total), prev = day ? days[1].Total : days.Skip(7).Sum(x => x.Total);
                var big = Txt(ScreenTime.Fmt(total), 30, true); big.FontFamily = Display; big.Margin = new Thickness(0, 12, 0, 0); host.Children.Add(big);
                var cmp = Txt((day ? "вчера " : "предыдущие 7 дней: ") + ScreenTime.Fmt(prev) + (prev >= 600 ? " · " + (total >= prev ? "+" : "−") + Math.Abs(Math.Round(100.0 * (total - prev) / prev)) + "%" : ""), 11.5, false); cmp.Opacity = 0.7; host.Children.Add(cmp);
                int n = day ? 24 : 7;
                var vals = day ? days[0].Hours.Select(x => (double)x).ToArray() : Enumerable.Range(0, 7).Select(k => (double)days[6 - k].Total).ToArray();
                double mx = Math.Max(day ? 60 : 600, vals.Max());
                var chart = new Grid { Height = 70, Margin = new Thickness(0, 12, 0, 2) }; var marks = new Grid { Opacity = 0.6 };
                for (int k = 0; k < n; k++)
                {
                    chart.ColumnDefinitions.Add(new ColumnDefinition()); marks.ColumnDefinitions.Add(new ColumnDefinition());
                    bool cur = day ? k == DateTime.Now.Hour : k == 6;
                    var r = new Rectangle { Fill = Accent, RadiusX = 2, RadiusY = 2, Margin = new Thickness(day ? 1 : 7, 0, day ? 1 : 7, 0), VerticalAlignment = VerticalAlignment.Bottom, Height = Math.Max(2, 70 * vals[k] / mx), Opacity = cur ? 1 : 0.55,
                        ToolTip = (day ? k + ":00" : DateTime.Now.AddDays(k - 6).ToString("ddd, d MMM")) + " — " + ScreenTime.Fmt((int)vals[k]) };
                    double restingOpacity = r.Opacity;
                    var column = new Grid { Background = Brushes.Transparent, ToolTip = r.ToolTip };
                    r.ToolTip = null;
                    column.Children.Add(r);
                    column.MouseEnter += (s, e) => { r.Opacity = 1; };
                    column.MouseLeave += (s, e) => { r.Opacity = restingOpacity; };
                    Grid.SetColumn(column, k); chart.Children.Add(column);
                    if (!day || k % 6 == 0)
                    {
                        var t = Txt(day ? k.ToString("00") + ":00" : DateTime.Now.AddDays(k - 6).ToString("ddd"), 9, cur); Grid.SetColumn(t, k);
                        if (day) Grid.SetColumnSpan(t, 6); else t.HorizontalAlignment = HorizontalAlignment.Center;
                        marks.Children.Add(t);
                    }
                }
                host.Children.Add(chart); host.Children.Add(marks);
                var apps = new Dictionary<string, int>();
                var pathOf = new Dictionary<string, string>();
                foreach (var dd in days.Take(day ? 1 : 7)) foreach (var kv in dd.Apps)
                {
                    var nice = NiceApp(kv.Key); int cc; apps.TryGetValue(nice, out cc); apps[nice] = cc + kv.Value;
                    string pth; if (!pathOf.ContainsKey(nice) && ScreenTime.Paths.TryGetValue(kv.Key, out pth)) pathOf[nice] = pth;
                }
                var head = Txt(day ? "Приложения сегодня" : "Приложения за 7 дней", 12, true); head.Margin = new Thickness(0, 12, 0, 0); host.Children.Add(head);
                if (apps.Count == 0) { var none = Txt("Пока нет данных", 11, false); none.Opacity = 0.6; host.Children.Add(none); }
                int top = apps.Count == 0 ? 1 : apps.Values.Max();
                foreach (var kv in apps.OrderByDescending(a => a.Value).Take(8))
                {
                    // строка: значок, название, время; под ней — полоса доли
                    var g = new Grid { Margin = new Thickness(0, 7, 0, 0) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); g.ColumnDefinitions.Add(new ColumnDefinition());
                    string ip; ImageSource ic = pathOf.TryGetValue(kv.Key, out ip) ? ShellIcon(ip, IconPxFor(20, MonScale)) : null;
                    FrameworkElement icon = ic != null ? (FrameworkElement)new Image { Source = ic, Width = 20, Height = 20 } : new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Background = Frozen(CardFg, 0x18), Child = new TextBlock { Text = kv.Key.Substring(0, 1).ToUpper(), FontSize = 10, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                    icon.VerticalAlignment = VerticalAlignment.Center; icon.HorizontalAlignment = HorizontalAlignment.Left;
                    var col = new StackPanel(); Grid.SetColumn(col, 1);
                    var dp = new DockPanel();
                    var t = Txt(ScreenTime.Fmt(kv.Value), 11, true); DockPanel.SetDock(t, Dock.Right);
                    var nm = Txt(kv.Key, 11.5, false); nm.TextTrimming = TextTrimming.CharacterEllipsis;
                    dp.Children.Add(t); dp.Children.Add(nm); col.Children.Add(dp);
                    col.Children.Add(new Rectangle { Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = Accent, Opacity = 0.85, HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(2, 296.0 * kv.Value / top), Margin = new Thickness(0, 3, 0, 0) });
                    g.Children.Add(icon); g.Children.Add(col);
                    host.Children.Add(g);
                }
            };
            render();
            // способ подсчёта — короткой подписью с подсказкой, а не абзацем
            var rule = Txt("ⓘ Как считается", 10.5, false); rule.Opacity = 0.55; rule.Margin = new Thickness(0, 12, 0, 0); rule.HorizontalAlignment = HorizontalAlignment.Left;
            rule.ToolTip = "Считается время активного окна, пока с компьютером работают: если клавиатурой и мышью не пользовались больше 2 минут, время не идёт — так и для видео без касаний."; rule.Visibility = Visibility.Collapsed; body.Children.Add(rule); // пояснение о подсчёте — только во всплывающей подсказке модуля
            ShowPanel("Экранное время", body, 360, null);
        }
    }

    // Логотип «Пуск»: четыре квадрата
    class StartLogo : Grid
    {
        public StartLogo()
        {
            Width = 16; Height = 16; HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Center;
            for (int i = 0; i < 4; i++)
                Children.Add(new Rectangle { Width = 7, Height = 7, RadiusX = 1.5, RadiusY = 1.5, HorizontalAlignment = i % 2 == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right, VerticalAlignment = i < 2 ? VerticalAlignment.Top : VerticalAlignment.Bottom });
        }
        public void SetColor(Brush b) { foreach (Rectangle r in Children) r.Fill = b; } // логотип в цвете акцента стиля
    }

    // Панель Windows — только через Win32, без WPF, чтобы сторож оставался крошечным.
    // Скрытая панель Windows продолжает держать свою полосу экрана, поэтому на время работы FlowBar она переводится
    // в автоскрытие. Исходное состояние (видимость и автоскрытие) хранится в taskbar.txt и возвращается при выходе,
    // а после сбоя — сторожем. Если прошлый запуск не завершился штатно, исходным считается сохранённое тогда.
    static class Taskbar
    {
        public static bool WasVisible = true; public static int OrigState = -1;
        static readonly string StateFile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlowBar", "taskbar.txt");
        static N.APPBARDATA Data(int lParam) { return new N.APPBARDATA { cbSize = Marshal.SizeOf(typeof(N.APPBARDATA)), hWnd = Tray.Explorer(), lParam = (IntPtr)lParam }; }
        public static int GetState() { var d = Data(0); return (int)N.SHAppBarMessage(4 /* ABM_GETSTATE */, ref d).ToUInt32(); }
        static void SetState(int st) { var d = Data(st); N.SHAppBarMessage(10 /* ABM_SETSTATE */, ref d); }
        static bool LoadSaved()
        {
            try { var t = File.ReadAllText(StateFile).Trim().Split('|'); OrigState = int.Parse(t[0]); WasVisible = t[1] == "1"; return true; } catch { return false; }
        }
        public static void Remember()
        {
            if (File.Exists(StateFile) && LoadSaved()) return;
            var h = Tray.Explorer(); WasVisible = h == IntPtr.Zero || N.IsWindowVisible(h); OrigState = GetState();
            try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StateFile)); File.WriteAllText(StateFile, OrigState + "|" + (WasVisible ? "1" : "0")); } catch { }
        }
        // вернуть как было до FlowBar; final — FlowBar завершается, файл состояния больше не нужен
        public static void Restore(bool force, bool final)
        {
            if (OrigState < 0) LoadSaved();
            if (OrigState >= 0 && GetState() != OrigState) SetState(OrigState);
            if (WasVisible || force) Set(true);
            if (final) try { File.Delete(StateFile); } catch { }
        }
        public static void Set(bool show)
        {
            if (!show) { int st = GetState(); if ((st & 1 /* ABS_AUTOHIDE */) == 0) SetState(st | 1); }
            int cmd = show ? 5 : 0;
            var h = Tray.Explorer(); // панель Проводника, а не окно своего трея
            if (h != IntPtr.Zero && N.IsWindowVisible(h) != show) N.ShowWindow(h, cmd);
            for (var s = N.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_SecondaryTrayWnd", null); s != IntPtr.Zero; s = N.FindWindowEx(IntPtr.Zero, s, "Shell_SecondaryTrayWnd", null))
                if (N.IsWindowVisible(s) != show) N.ShowWindow(s, cmd);
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            var me = Process.GetCurrentProcess();
            if (args.Contains("--restore"))
            {
                // сначала останавливаем работающую панель, иначе она через секунду снова спрячет панель Windows
                foreach (var p in Process.GetProcessesByName(me.ProcessName)) if (p.Id != me.Id) try { p.Kill(); p.WaitForExit(3000); } catch { }
                Taskbar.Restore(true, true); return 0;
            }
            int wi = Array.IndexOf(args, "--watch");
            if (wi >= 0 && wi + 1 < args.Length)
            {
                // сторож: отдельный процесс ждёт завершения панели — даже аварийного или через «Снять задачу» — и возвращает панель Windows
                int pid, st = -1; int.TryParse(args[wi + 1], out pid);
                if (wi + 2 < args.Length) int.TryParse(args[wi + 2], out st);
                Taskbar.OrigState = st; Taskbar.WasVisible = !(wi + 3 < args.Length && args[wi + 3] == "0");
                Process p = null; try { p = Process.GetProcessById(pid); } catch { }
                try { System.Threading.EventWaitHandle ev; if (p != null && System.Threading.EventWaitHandle.TryOpenExisting("FlowBar_watch_ready_" + pid, out ev)) { ev.Set(); ev.Dispose(); } } catch { }
                if (p != null) try { p.WaitForExit(); } catch { }
                Taskbar.Restore(true, true); return 0;
            }
            // команды работающей панели: открыть раздел или сохранить снимки окон (для приёмки и ярлыков)
            int oi = Array.IndexOf(args, "--open"), si = Array.IndexOf(args, "--shot");
            if (oi >= 0 && oi + 1 < args.Length)
            {
                int code = Array.IndexOf(Bar.OpenNames, args[oi + 1]), bi = 0;
                if (code <= 0) { Console.WriteLine("Неизвестный раздел. Доступно: " + string.Join(", ", Bar.OpenNames.Skip(1))); return 2; }
                if (oi + 2 < args.Length) int.TryParse(args[oi + 2], out bi);
                PostToBar(N.RegisterWindowMessage("FlowBarOpen"), (IntPtr)code, (IntPtr)bi); return 0;
            }
            if (si >= 0)
            {
                int dpi = 96; if (si + 1 < args.Length) int.TryParse(args[si + 1], out dpi);
                PostToBar(N.RegisterWindowMessage("FlowBarShot"), (IntPtr)dpi, IntPtr.Zero); return 0;
            }
            if (args.Contains("--mcp")) return Agent.RunMcp(); // сервер для агентов: действия выполняет работающая панель
            if (args.Contains("--selftest")) return SelfTest();
            // запуск нового exe откуда угодно обновляет установленную копию и передаёт работу ей; --here — запустить прямо отсюда (разработка)
            // Запущен из приложения Microsoft Store (Claude, Telegram из Store и т. п.): Windows тихо уводит записи в %APPDATA% в папку того приложения —
            // настройки и заметки расходились с обычным запуском при входе в Windows (так панель Windows и переставала прятаться). Перезапуск через Проводник — вне изоляции.
            if (!args.Contains("--fromshell") && InPackage())
                try { Bar.Start(Assembly.GetExecutingAssembly().Location, string.Join(" ", args.Concat(new[] { "--fromshell" }).Select(a => "\"" + a + "\"")), null, true); return 0; }
                catch (Exception ex) { Bar.Log(ex); }
            if (!Bar.DemoMode && !args.Contains("--here") && (Setup.HandOver() || Setup.Elevate(args))) return 0;
            return RunBar(me);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetCurrentPackageFullName(ref int len, StringBuilder name);
        static bool InPackage() { try { int n = 0; return GetCurrentPackageFullName(ref n, null) != 15700 /* APPMODEL_ERROR_NO_PACKAGE */; } catch { return false; } }

        // команда — окнам уже работающего FlowBar (широковещательные сообщения до окон-инструментов доходят не всегда)
        internal static void PostToBar(uint msg, IntPtr w, IntPtr l)
        {
            var me = Process.GetCurrentProcess();
            var pids = new HashSet<uint>(Process.GetProcessesByName(me.ProcessName).Where(p => p.Id != me.Id).Select(p => (uint)p.Id));
            N.EnumWindows((h, x) => { uint pid; N.GetWindowThreadProcessId(h, out pid); if (pids.Contains(pid)) N.PostMessage(h, msg, w, l); return true; }, IntPtr.Zero);
        }

        // Сторож: крошечный flowbar-watch.exe (Rust), если лежит рядом; иначе — сам FlowBar в режиме --watch.
        // Ждём подтверждения готовности (сторож открыл процесс FlowBar); нет подтверждения — false, панель Windows не скрываем.
        public static Process Watch;
        public static bool StartWatch()
        {
            var me = Process.GetCurrentProcess(); var exe = Assembly.GetExecutingAssembly().Location;
            var rust = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(exe), "flowbar-watch.exe");
            var a = me.Id + " " + Taskbar.OrigState + " " + (Taskbar.WasVisible ? "1" : "0");
            using (var ready = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset, "FlowBar_watch_ready_" + me.Id))
                foreach (var psi in new[] { File.Exists(rust) ? new ProcessStartInfo(rust, a) : null, new ProcessStartInfo(exe, "--watch " + a) })
                {
                    if (psi == null) continue;
                    psi.UseShellExecute = false; psi.CreateNoWindow = true;
                    try
                    {
                        Watch = Process.Start(psi);
                        if (ready.WaitOne(10000)) return true; // на чужом компьютере первый запуск неподписанного exe задерживает проверка Защитника — 3 с не хватало
                        Bar.Log(new Exception("Сторож не подтвердил готовность: " + psi.FileName));
                        try { Watch.Kill(); } catch { }
                    }
                    catch (Exception ex) { Bar.Log(ex); }
                }
            Watch = null; return false;
        }

        [DllImport("user32.dll")] static extern bool ChangeWindowMessageFilter(uint msg, uint flag);
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int RunBar(Process me)
        {
            bool created;
            var mtx = new System.Threading.Mutex(true, Bar.DemoMode ? "FlowBar_demo_instance" : "FlowBar_single_instance", out created);
            // перезапуск после сбоя: старый процесс ещё умирает и держит мьютекс (из другого потока его не отпустить) — ждём, пока ОС его освободит
            if (!created && Environment.GetCommandLineArgs().Contains("--afterfail"))
                try { created = mtx.WaitOne(8000); } catch (System.Threading.AbandonedMutexException) { created = true; }
            if (!created) return 0;
            // с правами администратора сообщения от обычных процессов (Проводник, команды --open, установщик новой версии) иначе не доходят
            foreach (var m in new[] { "FlowBarOpen", "FlowBarShot", "FlowBarAppBar", "TaskbarCreated" }) ChangeWindowMessageFilter(N.RegisterWindowMessage(m), 1);
            if (!Bar.DemoMode) { Taskbar.Remember(); Bar.WatchOk = StartWatch(); }
            System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072; // TLS 1.2
            // Панель Windows возвращается при любом завершении, чтобы не оставить пользователя без неё
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Bar.Info("сбой, панель закрывается: " + e.ExceptionObject);
                Bar.Shutdown();
                if (Bar.DemoMode) return;
                try
                {
                    var mark = System.IO.Path.Combine(Bar.Dir, "restart.txt");
                    if (!File.Exists(mark) || (DateTime.Now - File.GetLastWriteTime(mark)).TotalSeconds > 60)
                    {
                        File.WriteAllText(mark, DateTime.Now.ToString("s"));
                        Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--fromshell --afterfail") { UseShellExecute = true });
                        Process.GetCurrentProcess().Kill(); // без окна сбоя Windows (оно держит блокировку секундами): новая копия получает её сразу
                    }
                }
                catch { }
            };
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) => { Bar.Log(e.Exception); e.Handled = true; };
            app.Exit += (s, e) => { Bar.Info("выход: " + (Bar.ExitWhy ?? "приложение завершено без названной причины")); Bar.Shutdown(); };
            app.SessionEnding += (s, e) => { Bar.ExitWhy = "завершение сеанса Windows (" + e.ReasonSessionEnding + ")"; Bar.Info("выход: " + Bar.ExitWhy); Bar.Shutdown(); };
            Bar.Init();
            if (!Bar.DemoMode)
            {
                Agent.StartServer();
                if (Bar.C("ownTray", "1") == "1") Tray.Start(); // свой трей до создания панелей
            }
            foreach (var scr in System.Windows.Forms.Screen.AllScreens) new Bar(scr).Show();
            Bar.WatchScreens();
            var started = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; started.Tick += (s, e) => { started.Stop(); Bar.Info("запуск:\r\n" + Bar.SysInfo()); if (!Bar.DemoMode) Bar.PlaceStartNow(); }; started.Start();
            if (!Bar.DemoMode)
            {
                var note = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) }; note.Tick += (s, e) => { note.Stop(); Bar.ShowUpdateNote(); }; note.Start();
            }
            app.Run();
            GC.KeepAlive(mtx);
            return 0;
        }

        static int SelfTest()
        {
            Func<bool, string, int> check = (ok, name) => { Console.WriteLine((ok ? "OK   " : "FAIL ") + name); return ok ? 0 : 1; };
            int fails = 0;
            // обновление: резервная копия берёт личные файлы (и вложенные папки), но не кэш, журнал и прежние копии
            {
                var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "flowbar-selftest-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(System.IO.Path.Combine(tmp, "screentime")); Directory.CreateDirectory(System.IO.Path.Combine(tmp, "cache")); Directory.CreateDirectory(System.IO.Path.Combine(tmp, "backups", "old"));
                    File.WriteAllText(System.IO.Path.Combine(tmp, "notes.txt"), "заметка"); File.WriteAllText(System.IO.Path.Combine(tmp, "screentime", "d.txt"), "1");
                    File.WriteAllText(System.IO.Path.Combine(tmp, "cache", "x.png"), "x"); File.WriteAllText(System.IO.Path.Combine(tmp, "error.log"), "e"); File.WriteAllText(System.IO.Path.Combine(tmp, "backups", "old", "notes.txt"), "старое");
                    var dst = Setup.Backup(tmp, System.IO.Path.Combine(tmp, "backups"), "1.0.0");
                    fails += check(File.ReadAllText(System.IO.Path.Combine(dst, "notes.txt")) == "заметка" && File.Exists(System.IO.Path.Combine(dst, "screentime", "d.txt"))
                        && !Directory.Exists(System.IO.Path.Combine(dst, "cache")) && !File.Exists(System.IO.Path.Combine(dst, "error.log")) && !Directory.Exists(System.IO.Path.Combine(dst, "backups")), "Обновление: резервная копия данных");
                    { var nw = Bar.ParseNews("# t\n\nвступление\n\n## 1.1 — дата\n- Пункт **один**\n  - вложенный\n## 1.0\n- старый"); fails += check(nw.Count == 5 && nw[0].Item1 == 0 && nw[1].Item2 == "Пункт один" && nw[2].Item1 == 2 && nw[3].Item2 == "1.0", "Что нового: разбор списка изменений"); }
                    fails += check(Bar.HostExe(@"C:\x\notepad.exe") == @"C:\x\notepad.exe" && Bar.HostExe(null) == null && Bar.HostExe(@"Z:\nowhere\bin\steamwebhelper.exe") == @"Z:\nowhere\bin\steamwebhelper.exe", "Окна: основной exe программы");
                    {
                        // агенты: протокол MCP без панели — инструмент подменён
                        Func<string, Dictionary<string, object>, KeyValuePair<bool, string>> fake = (n, a) => new KeyValuePair<bool, string>(true, n + ":" + a["text"]);
                        var init = Agent.HandleLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}", fake);
                        var list = Agent.HandleLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}", fake);
                        var call = Agent.HandleLine("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"add_task\",\"arguments\":{\"text\":\"молоко\"}}}", fake);
                        var bad = Agent.HandleLine("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"nope\"}", fake);
                        fails += check(init.Contains("\"protocolVersion\":\"2025-06-18\"") && list.Contains("\"add_reminder\"") && list.Contains("\"required\":[\"text\"]") && call.Contains("add_task:молоко") && call.Contains("\"isError\":false")
                            && bad.Contains("-32601") && Agent.HandleLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", fake) == null, "Агенты: протокол MCP");
                        var t0 = new DateTime(2026, 11, 11, 12, 0, 0);
                        fails += check(Bar.AgentWhen(new Dictionary<string, object> { { "in_minutes", 30 } }, t0) == t0.AddMinutes(30) && Bar.AgentWhen(new Dictionary<string, object> { { "at", "2026-11-12T10:00" } }, t0) == new DateTime(2026, 11, 12, 10, 0, 0)
                            && Bar.AgentWhen(new Dictionary<string, object>(), t0) == null, "Агенты: время напоминания");
                    }
                    { var rc0 = Bar.RemindChoices(new DateTime(2026, 11, 11, 12, 0, 0)); var rc1 = Bar.RemindChoices(new DateTime(2026, 11, 11, 18, 30, 0));
                      fails += check(rc0.Count == 4 && rc0[2].Item2 == new DateTime(2026, 11, 11, 19, 0, 0) && rc0[3].Item2 == new DateTime(2026, 11, 12, 9, 0, 0) && rc1.Count == 3, "Напоминания: варианты времени"); }
                    fails += check(Bar.BadgeOf("(3) WhatsApp", "x.exe") == 3 && Bar.BadgeOf("Chat (5)", @"C:\Telegram.exe") == 5 && Bar.BadgeOf("Новая папка (2)", "explorer.exe") == 0 && Bar.BadgeOf("(7 426) Mail", null) == 7426 && Bar.BadgeOf("Doc", null) == 0, "Ярлык: счётчик из заголовка окна");
                    fails += check(Bar.IsVpnAdapter(NetworkInterfaceType.Ethernet, "Wintun Userspace Tunnel") && Bar.IsVpnAdapter(NetworkInterfaceType.Ppp, "x") && !Bar.IsVpnAdapter(NetworkInterfaceType.Ethernet, "Realtek PCIe 5GbE Family Controller") && Bar.IsVirtualAdapter("Hyper-V Virtual Ethernet Adapter"), "Сеть: адаптер VPN отличается от обычного");
                    fails += check(Setup.V3(new Version(1, 2)) == "1.2.0" && new Version("1.1.0.0") > new Version("1.0.0.0"), "Обновление: сравнение версий");
                }
                finally { try { Directory.Delete(tmp, true); } catch { } }
            }
            // свой трей: разбор сообщения Shell_NotifyIcon — добавление со всплывающей подсказкой, затем удаление
            {
                IntPtr buf = Marshal.AllocHGlobal(8 + 956);
                try
                {
                    for (int i = 0; i < 8 + 956; i++) Marshal.WriteByte(buf, i, 0);
                    Marshal.WriteInt32(buf, 0, 0x34753423); Marshal.WriteInt32(buf, 4, 0); Marshal.WriteInt32(buf, 8, 956); Marshal.WriteInt32(buf, 12, 0x1234); Marshal.WriteInt32(buf, 16, 7);
                    Marshal.WriteInt32(buf, 20, 1 | 4); Marshal.WriteInt32(buf, 24, 0x8001);
                    var tip = "Тест\0".ToCharArray(); for (int i = 0; i < tip.Length; i++) Marshal.WriteInt16(buf, 8 + 24 + i * 2, tip[i]);
                    bool added = Tray.Handle(buf, 8 + 956); var ti = Tray.Icons.FirstOrDefault(x => x.HWnd == 0x1234 && x.Id == 7);
                    fails += check(added && ti != null && ti.Tip == "Тест" && ti.CbMsg == 0x8001, "Свой трей: NIM_ADD с подсказкой");
                    Marshal.WriteInt32(buf, 4, 2); bool del = Tray.Handle(buf, 8 + 956);
                    fails += check(del && !Tray.Icons.Any(x => x.HWnd == 0x1234), "Свой трей: NIM_DELETE");
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            fails += check(Bar.Rate(2048) == "2K", "Rate KB");
            fails += check(Bar.Rate(1572864) == "1.5M", "Rate MB");
            var t = Bar.ParseTodo("1|купить | хлеб");
            fails += check(t.Done && t.Text == "купить | хлеб", "ParseTodo");
            fails += check(Bar.TodoLine(t) == "1|купить | хлеб", "TodoLine roundtrip");
            fails += check(!Bar.ParseTodo("без флага").Done, "ParseTodo legacy");
            var dated = Bar.ParseTodo("0@20261112|задача | тест");
            fails += check(dated.Due == new DateTime(2026, 11, 12) && Bar.TodoLine(dated) == "0@20261112|задача | тест", "Task due date preserves old separators");
            var st = new ScreenTime { Day = "2026-01-01" };
            st.Add("Chrome | Web", 9); st.Add("Chrome | Web", 9); st.Add("Code", 23);
            var back = ScreenTime.Parse("2026-01-01", st.Lines().Concat(new[] { "мусор", "h|99|5" }));
            fails += check(back.Total == 3 && back.Hours[9] == 2 && back.Apps["Chrome | Web"] == 2 && back.Apps["Code"] == 1, "ScreenTime roundtrip");
            var tracked = new ScreenTime { Day = "2026-01-01" };
            var from = new DateTime(2026, 1, 1, 9, 0, 0);
            tracked.Track("Browser | test", from); tracked.Track("Browser | test", from.AddSeconds(1));
            tracked.Track("Browser | test", from.AddSeconds(8)); tracked.Track("Editor", from.AddSeconds(9));
            var trackedBack = ScreenTime.Parse(tracked.Day, tracked.Lines());
            fails += check(trackedBack.Total == 4 && trackedBack.Intervals.Count == 3 && trackedBack.Intervals[0].End == 32402 &&
                trackedBack.Intervals[1].Start == 32408 && trackedBack.Intervals[0].App == "Browser | test", "ScreenTime keeps idle gaps and app transitions");
            { var sk = new ScreenTime { Day = "2026-01-01" }; sk.Track("A", from); sk.Track("A", from.AddSeconds(2)); sk.Track("A", from.AddSeconds(5));
              fails += check(sk.Intervals.Count == 2 && sk.Intervals[0].End == 32403, "ScreenTime: пропущенная секунда опроса не рвёт интервал, пауза 3 с — рвёт"); }
            fails += check(Bar.PresetName("  мой=набор" + "\n" + "1  ") == "мойнабор1" && Bar.PresetName(null) == "", "Свои наборы: название очищается от служебных символов");
            {
                string te; var ok = Bar.ParseTheme(new[] { "// тест", "$c = #FF112233", "name = Закат", "bg = $c", "fg = #FFFFFFFF", "accent = #A490C2", "blur = 4   // акрил", "opacity = 80", "material = frost", "align = left" }, out te);
                fails += check(ok != null && ok.Name == "Закат" && ok.Style.Blur == 4 && ok.Style.Bg.R == 0x11 && ok.Extras["opacity"] == "80" && ok.Extras["align"] == "left" && te == null, "Файл темы: разбор, переменные и значения вида");
                string e1, e2, e3, e4;
                fails += check(Bar.ParseTheme(new[] { "bg = #FF000000", "fg = #FFFFFFFF", "accent = #FFFFFF" }, out e1) == null && e1 != null
                    && Bar.ParseTheme(new[] { "name = Х", "bg = красный", "fg = #FFFFFFFF", "accent = #FFFFFF" }, out e2) == null && e2.Contains("цвет")
                    && Bar.ParseTheme(new[] { "name = Х", "bg = #FF000000", "fg = #FFFFFFFF", "accent = #FFFFFF", "align = вверх" }, out e3) == null && e3.StartsWith("align")
                    && Bar.ParseTheme(new[] { "name = Х", "bg = $нет", "fg = #FFFFFFFF", "accent = #FFFFFF" }, out e4) == null && e4.Contains("переменная"), "Файл темы: ошибки названы, битая тема не загружается");
            }
            fails += check(Bar.GroupName(" а\tб\r\nв  ") == "абв" && Bar.GroupName(null) == "" && Bar.GroupName(new string('я', 50)).Length == 32, "Группы: название очищается от табуляции и переводов строки, не длиннее 32");
            fails += check(ScreenTime.Fmt(3725) == "1ч 2м" && ScreenTime.Fmt(59) == "0м", "ScreenTime.Fmt");
            fails += check(Weather.Deg(-0.4) == "0°" && Weather.Deg(-2.6) == "-3°" && Weather.Deg(12.5) == "13°", "Weather.Deg");
            fails += check(Weather.Wmo(0, true).Item2 == "Ясно" && Weather.Wmo(95, true).Item2 == "Гроза" && Weather.Wmo(73, true).Item1 == "snow" && Weather.Wmo(1, false).Item1 == "partly-night", "Weather.Wmo");
            var wj = "{\"current\":{\"time\":\"2026-11-04T23:15\",\"temperature_2m\":-1.6,\"weather_code\":3,\"wind_speed_10m\":4.2,\"is_day\":0}," +
                "\"hourly\":{\"time\":[\"2026-11-04T23:00\",\"2026-11-05T00:00\"],\"temperature_2m\":[-1.5,-2],\"weather_code\":[3,71],\"precipitation_probability\":[null,40]}," +
                "\"daily\":{\"time\":[\"2026-11-04\"],\"weather_code\":[3],\"temperature_2m_max\":[2.1],\"temperature_2m_min\":[-3],\"precipitation_probability_max\":[40],\"precipitation_sum\":[0.4],\"sunrise\":[\"2026-11-04T07:20\"],\"sunset\":[\"2026-11-04T16:35\"]}}";
            var w = Weather.Parse(wj);
            fails += check(w.Temp == -1.6 && !w.Day && w.HStart == 1 && w.HProb[0] == 0 && w.HCode[1] == 71 && w.Sunset.Hour == 16, "Weather.Parse");
            var cities = Weather.ParseCities("{\"results\":[{\"name\":\"Казань\",\"latitude\":55.79,\"longitude\":49.12,\"country\":\"Россия\",\"admin1\":\"Татарстан\"}]}");
            fails += check(cities.Count == 1 && cities[0].Label == "Казань, Татарстан, Россия" && cities[0].Lat == "55.79", "ParseCities");
            fails += check(Weather.ParseCities("{\"generationtime_ms\":0.1}").Count == 0, "ParseCities empty");

            string id;
            var cl = ClaudeUsage.ParseLine("{\"type\":\"assistant\",\"message\":{\"model\":\"claude-opus-5-5\",\"id\":\"msg_1\",\"content\":[{\"type\":\"text\",\"text\":\"\\\"usage\\\":{\\\"input_tokens\\\":999}\"}],\"usage\":{\"input_tokens\":2,\"cache_creation_input_tokens\":100,\"cache_read_input_tokens\":50,\"output_tokens\":30,\"iterations\":[{\"input_tokens\":7}]}},\"requestId\":\"req_1\",\"timestamp\":\"2026-11-04T19:57:52.445Z\"}", out id);
            fails += check(cl != null && cl.In == 2 && cl.CacheW == 100 && cl.CacheR == 50 && cl.Out == 30 && cl.Weight == 132 && id == "msg_1req_1", "ClaudeUsage.ParseLine");
            fails += check(ClaudeUsage.ParseLine("{\"type\":\"user\",\"message\":{}}", out id) == null, "ClaudeUsage skips user");
            fails += check(ClaudeUsage.ModelName("claude-sonnet-4-5-20250929") == "Sonnet 4.5" && ClaudeUsage.ModelName("claude-opus-5-5") == "Opus 5.5", "ModelName");
            Func<int, int, ClaudeUsage.Msg> at = (h, m) => new ClaudeUsage.Msg { T = new DateTime(2026, 1, 1, h, m, 0) };
            var ms = new[] { at(9, 40), at(10, 5), at(15, 10), at(16, 0) };
            fails += check(ClaudeUsage.BlockStart(ms, new DateTime(2026, 1, 1, 16, 30, 0)) == new DateTime(2026, 1, 1, 15, 0, 0), "BlockStart new window");
            fails += check(ClaudeUsage.BlockStart(ms, new DateTime(2026, 1, 1, 20, 0, 0)) == null, "BlockStart expired");
            var cx = CodexLimits.ParseLine("{\"timestamp\":\"2026-11-04T20:40:00.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"primary\":{\"used_percent\":16.0,\"window_minutes\":300,\"resets_at\":1793841299},\"secondary\":{\"used_percent\":5.0,\"window_minutes\":10080,\"resets_at\":1794381026},\"plan_type\":\"plus\"}}}");
            fails += check(cx != null && cx.P5 == 16 && cx.PW == 5 && cx.Plan == "plus" && cx.R5 < cx.RW && double.IsNaN(cx.Eff5(cx.R5.AddSeconds(1))) && cx.Expired5(cx.R5.AddSeconds(1)), "CodexLimits.ParseLine (expired window is unknown, not 0%)");
            fails += check(CodexLimits.ParseLine("{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":null}}") == null, "CodexLimits null limits");
            fails += check(Bar.Tok(950) == "950" && Bar.Tok(12345) == "12K" && Bar.Tok(1250000) == "1.3M", "Tok");

            var ics = string.Join("\r\n", new[] {
                "BEGIN:VCALENDAR", "X-WR-CALNAME:Работа",
                "BEGIN:VTIMEZONE", "TZID:Custom/Plus3", "BEGIN:STANDARD", "TZOFFSETTO:+0300", "END:STANDARD", "END:VTIMEZONE",
                "BEGIN:VEVENT", "UID:a", "SUMMARY:Планёрка\\, команда",
                "DTSTART;TZID=Custom/Plus3:20260105T100000", "DTEND;TZID=Custom/Plus3:20260105T103000",
                "RRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=4", "EXDATE;TZID=Custom/Plus3:20260107T100000",
                "BEGIN:VALARM", "SUMMARY:alarm", "END:VALARM", "END:VEVENT",
                "BEGIN:VEVENT", "UID:a", "RECURRENCE-ID;TZID=Custom/Plus3:20260112T100000", "SUMMARY:Планёрка перенесена",
                "DTSTART;TZID=Custom/Plus3:20260112T120000", "DTEND;TZID=Custom/Plus3:20260112T123000", "END:VEVENT",
                "BEGIN:VEVENT", "UID:b", "SUMMARY:Отпуск", "DTSTART;VALUE=DATE:20260110", "DTEND;VALUE=DATE:20260112", "END:VEVENT",
                "BEGIN:VEVENT", "UID:c", "SUMMARY:Длинное наз", " вание", "DTSTART:20260106T090000Z", "DURATION:PT1H", "END:VEVENT",
                "BEGIN:VEVENT", "UID:d", "STATUS:CANCELLED", "SUMMARY:Отменено", "DTSTART:20260106T090000Z", "END:VEVENT",
                "END:VCALENDAR" });
            string calName;
            var evs = Ics.Parse(ics, new DateTime(2026, 1, 1), new DateTime(2026, 2, 1), out calName);
            var plan = evs.Where(e => e.Title.StartsWith("Планёрка")).OrderBy(e => e.Start).ToList();
            fails += check(calName == "Работа", "Ics name");
            fails += check(plan.Count == 3 && plan[0].Title == "Планёрка, команда" && plan[1].Title == "Планёрка перенесена" && plan[2].Start.Day == 14, "Ics weekly+EXDATE+override");
            fails += check(plan[1].Start - plan[0].Start == TimeSpan.FromDays(7) + TimeSpan.FromHours(2) && plan.All(e => e.End - e.Start == TimeSpan.FromMinutes(30)), "Ics times");
            var vac = evs.FirstOrDefault(e => e.Title == "Отпуск");
            fails += check(vac != null && vac.AllDay && vac.Start == new DateTime(2026, 1, 10) && vac.End == new DateTime(2026, 1, 12), "Ics all-day");
            var longEv = evs.FirstOrDefault(e => e.Title == "Длинное название");
            fails += check(longEv != null && longEv.End - longEv.Start == TimeSpan.FromHours(1) && !evs.Any(e => e.Title == "Отменено" || e.Title == "alarm"), "Ics folding/duration/cancelled");
            var lastFri = Ics.Occurrences(new DateTime(2026, 1, 30, 9, 0, 0), new Dictionary<string, string> { { "FREQ", "MONTHLY" }, { "BYDAY", "-1FR" } }, null, new DateTime(2026, 1, 1), new DateTime(2026, 3, 31)).ToList();
            fails += check(lastFri.Count == 3 && lastFri[1] == new DateTime(2026, 2, 27, 9, 0, 0) && lastFri[2] == new DateTime(2026, 3, 27, 9, 0, 0), "Ics monthly last Friday");
            var daily = Ics.Occurrences(new DateTime(2015, 1, 1, 8, 0, 0), new Dictionary<string, string> { { "FREQ", "DAILY" } }, null, new DateTime(2026, 1, 10), new DateTime(2026, 1, 12)).ToList();
            fails += check(daily.Count > 0 && daily.Last() == new DateTime(2026, 1, 11, 8, 0, 0) && daily.Count <= 4, "Ics old daily skip-ahead");
            var weekdays = Ics.Occurrences(new DateTime(2026, 1, 9, 9, 0, 0), new Dictionary<string, string> { { "FREQ", "DAILY" }, { "BYDAY", "MO,TU,WE,TH,FR" }, { "COUNT", "3" } }, null, new DateTime(2026, 1, 1), new DateTime(2026, 2, 1)).ToList();
            fails += check(string.Join(",", weekdays.Select(d => d.Day)) == "9,12,13", "Ics daily BYDAY weekdays: " + string.Join(",", weekdays.Select(d => d.Day)));
            var rd = Ics.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:r\r\nSUMMARY:R\r\nDTSTART:20260105T100000\r\nDURATION:PT1H\r\nRDATE:20260108T150000\r\nEND:VEVENT\r\nEND:VCALENDAR", new DateTime(2026, 1, 1), new DateTime(2026, 2, 1), out calName);
            fails += check(rd.Count == 2 && rd[1].Start == new DateTime(2026, 1, 8, 15, 0, 0), "Ics RDATE");
            // Codex: свежесть по timestamp записи, а не по времени изменения файла
            var cxDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fb-codex-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(cxDir);
            try
            {
                Func<string, int, string> rec = (ts, pct) => "{\"timestamp\":\"" + ts + "\",\"payload\":{\"rate_limits\":{\"primary\":{\"used_percent\":" + pct + ",\"resets_at\":1793841299}}}}\n";
                File.WriteAllText(System.IO.Path.Combine(cxDir, "a.jsonl"), rec("2026-11-04T10:00:00Z", 90)); File.SetLastWriteTime(System.IO.Path.Combine(cxDir, "a.jsonl"), new DateTime(2026, 11, 4, 9, 0, 0));
                File.WriteAllText(System.IO.Path.Combine(cxDir, "b.jsonl"), rec("2026-11-04T08:00:00Z", 10)); File.SetLastWriteTime(System.IO.Path.Combine(cxDir, "b.jsonl"), new DateTime(2026, 11, 4, 12, 0, 0));
                var lt = CodexLimits.ReadLatest(cxDir);
                fails += check(lt != null && lt.P5 == 90 && double.IsNaN(lt.EffW(DateTime.Now)) && CodexLimits.Pct(lt.EffW(DateTime.Now)) == "—", "Codex newest record + missing window");
            }
            finally { Directory.Delete(cxDir, true); }
            fails += check(Ics.Zone("Europe/Moscow", new Dictionary<string, TimeSpan>()) != null && Ics.Zone("Russian Standard Time", new Dictionary<string, TimeSpan>()) != null, "Ics IANA+Windows zones");
            fails += check(Bar.ParseHm("22:00") == 1320 && Bar.ParseHm("8:05") == 485 && Bar.ParseHm("25:00") == null && Bar.ParseHm("abc") == null, "ParseHm");
            Func<int, int, DateTime> hm = (h, m) => new DateTime(2026, 1, 1, h, m, 0);
            fails += check(Bar.InWindow(hm(23, 0), 1320, 480) && Bar.InWindow(hm(7, 59), 1320, 480) && !Bar.InWindow(hm(8, 0), 1320, 480) && !Bar.InWindow(hm(12, 0), 1320, 480), "InWindow overnight");
            fails += check(Bar.InWindow(hm(13, 0), 720, 840) && !Bar.InWindow(hm(14, 0), 720, 840) && !Bar.InWindow(hm(13, 0), 600, 600), "InWindow daytime/empty");
            fails += check(Bar.Span(TimeSpan.FromSeconds(65)) == "01:05" && Bar.Span(new TimeSpan(0, 1, 2, 3, 450)) == "1:02:03" && Bar.Span(TimeSpan.FromMilliseconds(5430), true) == "00:05,43" && Bar.Span(TimeSpan.FromSeconds(-3)) == "00:00", "Span");
            var nowT = new DateTime(2026, 3, 10, 15, 0, 0);
            fails += check(Bar.Ago(nowT.AddSeconds(-20), nowT) == "только что" && Bar.Ago(nowT.AddMinutes(-5), nowT) == "5 мин назад" && Bar.Ago(nowT.AddHours(-3), nowT) == "3 ч назад" && Bar.Ago(nowT.AddDays(-1), nowT) == "вчера", "Ago");
            DateTime ratesTime;
            var rt = Bar.ParseRates("{\"result\":\"success\",\"time_last_update_unix\":1793836801,\"rates\":{\"USD\":1,\"EUR\":0.5,\"RUB\":90}}", out ratesTime);
            fails += check(rt.Count == 3 && Math.Abs(Bar.ConvertAmount(10, "EUR", "RUB", rt) - 1800) < 1e-9 && Math.Abs(Bar.ConvertAmount(90, "RUB", "USD", rt) - 1) < 1e-9 && ratesTime.Year == 2026, "Currency convert");
            Bar.appOrder = new List<string> { "p|a", "r|b", "r|c" }; Bar.Retag("r|b", "p|b");
            fails += check(string.Join(",", Bar.appOrder) == "p|a,p|b,r|c" && Bar.IsProgram("C:/x/App.LNK") && !Bar.IsProgram("C:/doc.pdf"), "Pin order / IsProgram");
            var blob = new byte[] { 7 }.Concat(Encoding.Unicode.GetBytes("xxFile Explorer.lnk\0\0WhatsApp_cv1g1gvanyjgm!App\0Google Chrome.lnk\0")).Concat(new byte[] { 9 }).Concat(Encoding.Unicode.GetBytes("Claude_pzs8sxrjxfjjc!Claude\0Steam.lnk")).ToArray();
            var ord = Bar.OrderPins(blob, new List<string> { @"C:\p\Steam.lnk", @"C:\p\Chrome.lnk", @"C:\p\Google Chrome.lnk", @"C:\p\File Explorer.lnk", @"C:\p\Unused.lnk" });
            fails += check(string.Join(",", ord.Select(x => x.Substring(x.LastIndexOf('\\') + 1))) == "File Explorer.lnk,WhatsApp_cv1g1gvanyjgm!App,Google Chrome.lnk,Claude_pzs8sxrjxfjjc!Claude,Steam.lnk", "Windows pin order: " + string.Join(",", ord));
            fails += check(Bar.Key(@"shell:AppsFolder\5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App") == Bar.Key(@"C:\Program Files\WindowsApps\5319275A.WhatsAppDesktop_2.2540.6.0_x64__cv1g1gvanyjgm\WhatsApp.exe") && Bar.Key(@"C:\A\B.exe") == @"c:\a\b.exe", "Store app key");
            fails += check(Bar.Key(Bar.SquirrelTarget(@"C:\U\Discord\Update.exe", "--processStart Discord.exe")) == Bar.Key(@"C:\U\Discord\app-1.0.9213\Discord.exe") && Bar.SquirrelTarget(@"C:\A\b.exe", "") == @"C:\A\b.exe", "Squirrel shortcut key");
            var lay = Bar.ParseLayout("ram, -cpu,zzz,ram", new[] { "cpu", "ram", "net" });
            fails += check(string.Join(",", lay.Select(x => (x.Value ? "" : "-") + x.Key)) == "ram,-cpu,zzz,net", "Widget layout parse");
            var lay2 = Bar.ParseLayout("cpu", new[] { "cpu", "-temp", "ram" });
            fails += check(string.Join(",", lay2.Select(x => (x.Value ? "" : "-") + x.Key)) == "cpu,-temp,ram", "Widget layout: new widgets hidden by default");
            fails += check(Bar.RateRu(512 * 1024) == "512 КБ/с" && Bar.RateRu(double.NaN) == "—" && Bar.Gb(double.NaN) == "—", "RateRu / unknown values show a dash");
            fails += check(Bar.Clamp("abc", 10, 0, 32) == 10 && Bar.Clamp("50", 10, 0, 32) == 32 && Bar.Clamp("12", 10, 0, 32) == 12, "Settings clamp");
            fails += check(Bar.Clamp("NaN", 10, 0, 32) == 10 && Bar.Clamp("Infinity", 10, 0, 32) == 10 && Bar.Clamp("-Infinity", 10, 0, 32) == 10, "Settings clamp rejects NaN/Infinity");
            fails += check(Bar.MemLimitGb(32) == 3.2 && Bar.MemLimitGb(8) == 1.5, "Memory saver limit: 10% of RAM, at least 1.5 GB");
            fails += check(Bar.IsLowMem(3.0, 3.2, false) && !Bar.IsLowMem(3.3, 3.2, false) && Bar.IsLowMem(4.0, 3.2, true) && !Bar.IsLowMem(4.9, 3.2, true), "Memory saver: switches on below limit, off only above limit x1.5");
            var noWid = Bar.ParseLayout(Bar.NoWidgets, new[] { "zzz-new" });
            fails += check(noWid.Count > 10 && noWid.Where(x => x.Key != "zzz-new").All(x => !x.Value), "First install layout: every widget hidden, only shortcuts");
            var shr = new Dictionary<string, string> { { "style", "Midnight" }, { "size", "L" }, { "wxCity", "Сухум" }, { "cl.prevStatus", "powershell -File x" }, { "accent", "#4CC2FF" }, { "align", "diagonal" }, { "view.cpu", "min" }, { "view.net@\\\\.\\DISPLAY2", "min" }, { "menus", "glass" }, { "sliders", "bogus" } };
            int shSkip; string shErr; var shBack = Bar.ReadShareCode(Bar.MakeShareCode(shr), out shSkip, out shErr);
            fails += check(shBack != null && shBack.Count == 5 && shBack["menus"] == "glass" && !shBack.ContainsKey("sliders") && shBack["size"] == "L" && shBack["accent"] == "#4CC2FF" && shBack["view.cpu"] == "min" && !shBack.ContainsKey("wxCity") && !shBack.ContainsKey("cl.prevStatus") && !shBack.ContainsKey("align") && !shBack.Keys.Any(k => k.Contains("@")), "Settings code: round trip, private, invalid and per-monitor keys dropped");
            fails += check(Bar.ReadShareCode("мусор", out shSkip, out shErr) == null && shErr != null && Bar.ReadShareCode("FBS1:AAAA", out shSkip, out shErr) == null && shErr != null && Bar.ReadShareCode(new string('A', 200000), out shSkip, out shErr) == null && shErr.Contains("большой"), "Settings code: garbage and oversized text rejected");
            // номера версий: буква в конце — мелкая правка и новее той же версии без буквы, но старее следующего номера
            Version tv7, tv7a, tv8;
            fails += check(Setup.TryParseTag("v1.6.7", out tv7) && Setup.TryParseTag("v1.6.7a", out tv7a) && Setup.TryParseTag("1.6.8", out tv8) && tv7 < tv7a && tv7a < tv8
                && Setup.V3(tv7) == "1.6.7" && Setup.V3(tv7a) == "1.6.7a" && Setup.V3(new Version(1, 6, 7, 2)) == "1.6.7b" && !Setup.TryParseTag("v1.6.7ab", out tv7) && !Setup.TryParseTag("мусор", out tv7), "Version letters: 1.6.7 < 1.6.7a < 1.6.8, display and tag parsing");
            // ползунок-рогатка: метка идёт за мышью один к одному (без ускорения), вправо — вправо, влево — влево, края и шаг соблюдаются
            Func<double, double, double> aim = (pullX, tick) => { double tn; const double rail = 280; return Bar.ElasticSlider.ValueAt(10 + rail * 0.6 + Bar.ElasticSlider.Shot(new Vector(pullX, 0), rail, out tn), rail, 0, 100, false, tick, 60); };
            fails += check(Math.Abs(aim(20, 0) - (60 + 20 / 2.8)) < 1e-6 && Math.Abs(aim(60, 0) - (60 + 60 / 2.8)) < 1e-6 && Math.Abs(aim(-40, 0) - (60 - 40 / 2.8)) < 1e-6 && aim(500, 0) == 100 && aim(-500, 0) == 0 && Math.Abs(aim(20, 1) - Math.Round(aim(20, 1))) < 1e-9, "Slider aim: marker moves 1:1 with the mouse (no acceleration), clamped, snaps to step");
            // волна меню: кадр полной волны на 3600 квадратов (программная отрисовка — верхняя оценка; без кистей по уровням было около 23 мс, сейчас около 3 мс)
            double waveMs = double.NaN; try { waveMs = Bar.MenuMotion.BenchFrameMs(true); } catch (Exception ex) { Bar.Log(ex); }
            fails += check(!double.IsNaN(waveMs) && waveMs < 12, "Menu wave frame renders in " + (double.IsNaN(waveMs) ? "?" : Math.Round(waveMs, 1).ToString()) + " ms (limit 12)");
            // задание автозапуска: XML корректен, путь с «&» экранирован, вход пользователя, без повышения прав
            string tx = Setup.LogonTaskXml("S-1-5-21-1-2-3-1001", @"C:\Program Files\Fl&wBar\FlowBar.exe"); var txDoc = new System.Xml.XmlDocument(); bool txOk = true; try { txDoc.LoadXml(tx.Substring(tx.IndexOf("<Task"))); } catch (Exception) { txOk = false; }
            fails += check(txOk && tx.Contains("<LogonTrigger>") && tx.Contains("<RunLevel>LeastPrivilege</RunLevel>") && tx.Contains("Fl&amp;wBar") && !tx.Contains("<Delay>"), "Autostart task XML: valid, logon trigger, no elevation, no delay, path escaped");
            // перезапуск ради значка трея: только обычные программы, не магазинные, не сам FlowBar, файл должен существовать
            fails += check(Bar.CanRestartForTray(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe")) && !Bar.CanRestartForTray(@"C:\Program Files\WindowsApps\X\app.exe")
                && !Bar.CanRestartForTray(null) && !Bar.CanRestartForTray(@"C:\nope\missing.exe") && !Bar.CanRestartForTray(Setup.InstalledExe.Replace("FlowBar.exe", "FlowBar.exe"))
                && !Bar.CanRestartForTray(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")), "Tray restart: only plain existing programs, never Store apps, FlowBar or shared hosts (python, node, cmd)");
            // календарь: только https и webcal; http, чужие схемы и мусор — нет
            fails += check(Bar.IsFeedUrl("https://example.com/a.ics") && Bar.IsFeedUrl("webcal://example.com/a.ics") && Bar.IsFeedUrl("WEBCAL://example.com/a.ics") && !Bar.IsFeedUrl("http://example.com/a.ics") && !Bar.IsFeedUrl("ftp://example.com/a.ics") && !Bar.IsFeedUrl("example.com") && !Bar.IsFeedUrl(""), "Calendar feed link: https and webcal only");
            // время работы Windows: 64-битный счётчик, не обнуляется через 24,9 дня (32-битный TickCount & int.MaxValue давал «0м» на 25-й день)
            fails += check(unchecked((uint)Bar.GetTickCount64() - (uint)Environment.TickCount) < 2000u && Bar.Until(TimeSpan.FromMilliseconds(30.0 * 86400000)) == "30д 0ч" && Bar.Until(TimeSpan.FromMilliseconds((double)(25UL * 86400000))) == "25д 0ч", "Uptime: 64-bit tick count, 30 days shown as 30д 0ч");
            // склонение: 1 модуль, 2–4 модуля, 5–20 и 11–14 модулей, 21 модуль, 102 модуля, 111 модулей
            fails += check(Bar.Pl(1, "модуль", "модуля", "модулей") == "модуль" && Bar.Pl(2, "модуль", "модуля", "модулей") == "модуля" && Bar.Pl(5, "модуль", "модуля", "модулей") == "модулей" && Bar.Pl(11, "модуль", "модуля", "модулей") == "модулей"
                && Bar.Pl(14, "модуль", "модуля", "модулей") == "модулей" && Bar.Pl(21, "модуль", "модуля", "модулей") == "модуль" && Bar.Pl(31, "модуль", "модуля", "модулей") == "модуль" && Bar.Pl(102, "модуль", "модуля", "модулей") == "модуля" && Bar.Pl(111, "модуль", "модуля", "модулей") == "модулей" && Bar.Pl(0, "модуль", "модуля", "модулей") == "модулей", "Russian plural forms");
            // шаблоны переключателя, флажка и поля ввода разбираются без ошибок:
            fails += check(Bar.CheckTemplate() != null, "Check template parses");
            // пружина выделения: старт в нуле, к концу в единице, перелёт небольшой, в начале растёт без отката
            var spring = new Bar.SpringEase(); double springMax = 0; for (int q = 0; q <= 100; q++) springMax = Math.Max(springMax, spring.Ease(q / 100.0));
            fails += check(Math.Abs(spring.Ease(0)) < 1e-9 && Math.Abs(spring.Ease(1) - 1) < 0.015 && springMax > 1.0 && springMax < 1.03 && spring.Ease(0.1) > spring.Ease(0.05) && spring.Ease(0.05) > 0, "Selection spring: 0 to 1, overshoot under 3%");
            // миксер стилей: строки списка по центру плитки, линия входит в плитку не ближе 31 от края; сдвиг оттенка возвращается в тот же цвет
            fails += check(Math.Abs(Bar.MixGeometry.RowY(1, 3, 100) - 100) < 1e-9 && Math.Abs(Bar.MixGeometry.RowY(0, 4, 100) - 34) < 1e-9 && Math.Abs(Bar.MixGeometry.AttachY(56, 100) - 77) < 1e-9 && Math.Abs(Bar.MixGeometry.AttachY(100, 100) - 100) < 1e-9 && Math.Abs(Bar.MixGeometry.AttachY(144, 100) - 123) < 1e-9
                && Bar.HueShift(Colors.Red, 360) == Colors.Red && Bar.HueShift(Colors.Red, 120) == Colors.Lime && Bar.HueShift(Colors.Blue, -120) == Colors.Lime && Bar.HueShift(Color.FromRgb(0x80, 0x80, 0x80), 90) == Color.FromRgb(0x80, 0x80, 0x80), "Style mixer geometry and hue shift");
            // шаблон переключателя разбирается без ошибок: опечатка в XAML иначе всплыла бы только при открытии настроек
            fails += check(Bar.SwitchTemplate() != null, "Switch template parses");
            // поиск по настройкам: слова запроса, регистр, пустой запрос, ничего не найдено, разделы индекса существуют
            var sk1 = Bar.SearchSettings("ЗВУК"); var sk2 = Bar.SearchSettings("прозрач"); var sk3 = Bar.SearchSettings("рогатка ползунки");
            var skInfl = Bar.SearchSettings("преломление"); // склонение: находит и «Сила преломления», и «Ширина преломления»
            var okSections = new[] { "look", "bar", "monitors", "widgets", "connect", "auto", "notify", "general" };
            fails += check(skInfl.Any(e => e[2] == "Сила преломления") && skInfl.Any(e => e[2] == "Ширина преломления") && skInfl.Any(e => e[2] == "Преломление фона"), "Settings search: Russian word forms");
            fails += check(sk1.Any(e => e[2] == "Звук") && sk1.Any(e => e[2] == "Микшер по нажатию на громкость") && sk2.Any(e => e[2] == "Плотность заливки панели") && sk3.Count > 0 && sk3[0][2] == "Ползунки"
                && Bar.SearchSettings("  ").Count == 0 && Bar.SearchSettings("несуществующееслово").Count == 0 && Bar.SearchSettings("настройки").Count <= 12
                && Bar.SettingsIndex.All(e => okSections.Contains(e[0]) && e[1].Length > 0 && e[2].Length > 0), "Settings search: words, case, empty, no match, index sections valid");
            // число, набранное в поле справа от ползунка: цифры из текста, границы и шаг
            double pt1, pt2, pt3, pt4, pt5;
            fails += check(Bar.ParseTyped("56%", 30, 100, 1, out pt1) && pt1 == 56 && Bar.ParseTyped("7", 30, 100, 1, out pt2) && pt2 == 30 && Bar.ParseTyped("500", 30, 100, 1, out pt3) && pt3 == 100
                && Bar.ParseTyped("56,5 px", 0, 100, 1, out pt4) && pt4 == 57 && Bar.ParseTyped("52", 0, 100, 5, out pt5) && pt5 == 50 && !Bar.ParseTyped("нет", 0, 100, 1, out pt1) && !Bar.ParseTyped("", 0, 100, 1, out pt1), "Typed slider value: digits, clamp, step, junk rejected");
            // запись на диск: сбой не бросает исключение, успех сбрасывает признак «уже предупредили»
            bool persisted = false; Bar.saveWarned = true; Bar.Persist("test", () => { throw new IOException("test"); }); Bar.Persist("test", () => { persisted = true; });
            fails += check(persisted && !Bar.saveWarned, "Persist: failure is swallowed, success resets the warning");
            // заметка: неудачная запись сообщается и текст не теряется; повтор после восстановления доступа проходит
            var ro = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fb-note-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(ro, "old"); File.SetAttributes(ro, FileAttributes.ReadOnly); string er;
                bool failed = !Bar.SaveText(ro, "new", out er) && er != null && File.ReadAllText(ro) == "old";
                File.SetAttributes(ro, FileAttributes.Normal);
                fails += check(failed && Bar.SaveText(ro, "new", out er) && File.ReadAllText(ro) == "new", "Note save: failure reported, retry succeeds");
            }
            finally { foreach (var x in new[] { ro, ro + ".bak", ro + ".tmp" }) try { if (File.Exists(x)) { File.SetAttributes(x, FileAttributes.Normal); File.Delete(x); } } catch { } }
            var mseq = Bar.MergeSeq(new List<string> { "search", "w:cpu", "p|a", "clock" }, new List<string> { "p|a", "r|b" }, new List<string> { "w:cpu", "w:ram" }, true, true);
            fails += check(string.Join(",", mseq) == "search,w:cpu,w:ram,p|a,r|b,clock", "Bar order: widgets and apps mixed; new app after last app, new widget after its neighbour: " + string.Join(",", mseq));
            var md = Bar.MergeSeq(new List<string>(), new List<string> { "p|a", "r|b" }, new List<string> { "w:cpu" }, true, true);
            fails += check(string.Join(",", md) == "search,p|a,r|b,w:cpu,clock", "Bar order: default without saved order");
            var fr = Bar.FitRect(new N.RECT { L = 100, T = 100, R = 900, B = 700 }, new N.RECT { L = 0, T = 0, R = 2560, B = 1380 }, new N.RECT { L = -1920, T = 173, R = 0, B = 1205 }, 1.25, 1.0);
            var big = Bar.FitRect(new N.RECT { L = 0, T = 0, R = 2500, B = 1300 }, new N.RECT { L = 0, T = 0, R = 2560, B = 1380 }, new N.RECT { L = -1920, T = 173, R = 0, B = 1205 }, 1.0, 1.0);
            fails += check(fr.R - fr.L == 640 && fr.B - fr.T == 480 && fr.L >= -1920 && fr.R <= 0 && fr.T >= 173 && fr.B <= 1205 && big.R - big.L == 1920 && big.L == -1920 && big.B <= 1205,
                "Window to another monitor: same logical size across scales, clamped to the work area");
            fails += check(Bar.NiceTop(0) == 1 && Bar.NiceTop(double.NaN) == 1 && Bar.NiceTop(5) == 5 && Bar.NiceTop(5.1) == 10 && Bar.NiceTop(1300000) == 2000000 && Bar.NiceTop(180) == 200,
                "Graph scale: steps 1-2-5, never zero or NaN");
            fails += check(Bar.ConnAccess(2, 3) == "есть интернет" && Bar.ConnAccess(1, 1) == "без интернета" && Bar.ConnAccess(2, 2) == "интернет ограничен" && Bar.ConnAccess(0, 0) == "нет сети" && Bar.ConnKind(1) == "Ethernet",
                "Network state: connection type and internet access texts");
            var cbp = ClaudeBridge.Parse("{\"model\":{\"id\":\"x\"},\"context_window\":{\"used_percentage\":37},\"rate_limits\":{\"five_hour\":{\"used_percentage\":23.5,\"resets_at\":4102444800},\"seven_day\":{\"used_percentage\":41.2}}}", DateTime.Now);
            fails += check(cbp != null && cbp.P5 == 23.5 && cbp.PW == 41.2 && cbp.Ctx == 37 && cbp.R5.Year == 2100 && cbp.RW == DateTime.MaxValue && double.IsNaN(ClaudeBridge.Eff(50, DateTime.Now.AddMinutes(-1), DateTime.Now)),
                "Claude limits from status line: 5h, week, context; expired window unknown");
            var noRl = ClaudeBridge.Parse("{\"model\":{}}", DateTime.Now);
            fails += check(noRl != null && double.IsNaN(noRl.P5) && double.IsNaN(noRl.PW) && ClaudeBridge.Left(noRl.P5, noRl.R5, DateTime.Now) == "—", "Claude limits: absent windows show a dash");
            var pj = "{\"a\":[1,{\"b\":\"x,y:{z}\"}],\"c\":{},\"d\":true,\"e\":\"q\\\"w\"}";
            var js = new JavaScriptSerializer();
            fails += check(js.Serialize(js.DeserializeObject(Bar.JsonPretty(pj))) == js.Serialize(js.DeserializeObject(pj)), "Settings JSON written back unchanged in content");
            fails += check(Bar.ShortDev("Динамики (Realtek(R) Audio)") == "Динамики" && Bar.ShortDev(null) == "Устройство вывода" && Bar.ShortDev("Наушники") == "Наушники", "Volume: short output device name");
            fails += check(Bar.IconPxFor(36, 1.5) == 64 && Bar.IconPxFor(30, 1) == 32 && Bar.IconPxFor(36, 2) == 96 && Bar.IconPxFor(24, 1.25) == 32, "Icon: requested in physical pixels for size and DPI");
            fails += check(Bar.ProbeSwitch() == "Проба, выключено|отказ", "Switch: failed action leaves it off and reports why: " + Bar.ProbeSwitch());
            fails += check(Bar.Ago(TimeSpan.FromSeconds(45)) == "45 с" && Bar.Ago(TimeSpan.FromMinutes(2)) == "2 мин" && Bar.Ago(TimeSpan.FromSeconds(0)) == "1 с", "Graph label: real accumulated duration");
            var mo = Bar.MergeOrder(Bar.ParseLayout("a,-b,c,d", new string[0]), new List<string> { "d", "a", "c" });
            fails += check(string.Join(",", mo.Select(x => (x.Value ? "" : "-") + x.Key)) == "d,-b,a,c", "Widget drag keeps hidden in place");
            var sfp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fb-safe-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                SafeFile.WriteLines(sfp, new[] { "a=1" }); SafeFile.WriteLines(sfp, new[] { "a=2", "b=3" });
                bool bak = File.ReadAllText(sfp + ".bak").Trim() == "a=1", cur = string.Join(",", SafeFile.ReadLines(sfp)) == "a=2,b=3";
                File.Delete(sfp); // основной файл пропал — читаем резервную копию
                fails += check(bak && cur && string.Join(",", SafeFile.ReadLines(sfp)) == "a=1" && !File.Exists(sfp + ".tmp"), "SafeFile replace + backup");
            int hits = 0; var pr = new Press { Invoke = () => hits++ };
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(pr);
            var inv = peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke) as System.Windows.Automation.Provider.IInvokeProvider;
            if (inv != null) inv.Invoke();
            var arc = Bar.ArcGeo(10, 10, 8, -90, 90).Bounds;
            fails += check(Math.Abs(arc.Left - 10) < 0.01 && Math.Abs(arc.Top - 2) < 0.01 && Math.Abs(arc.Right - 18) < 0.01 && Math.Abs(arc.Bottom - 10) < 0.01, "Ring arc: quarter from the top, clockwise");
            fails += check(hits == 1 && peer.GetAutomationControlType() == System.Windows.Automation.Peers.AutomationControlType.Button && pr.Focusable, "Accessible button (Invoke)");
            var sw0 = Stopwatch.StartNew(); bool netThrew = false;
            try { Bar.GetSync("http://127.0.0.1:9/"); } catch (System.Net.WebException) { netThrew = true; }
            fails += check(netThrew && sw0.Elapsed.TotalSeconds < 20, "Network: no-connection fails fast with one retry (" + sw0.Elapsed.TotalSeconds.ToString("0.0") + " s)");
            }
            finally { foreach (var x in new[] { sfp, sfp + ".bak", sfp + ".tmp" }) if (File.Exists(x)) File.Delete(x); }
            Bar.appOrder.Clear();
            bool threw = false; try { Bar.ParseRates("{\"result\":\"error\"}", out ratesTime); } catch { threw = true; }
            fails += check(threw, "Currency error result");
            fails += check(Ics.Duration("P1DT2H30M") == new TimeSpan(1, 2, 30, 0) && Ics.Duration("P1W") == TimeSpan.FromDays(7), "Ics duration");
            return fails;
        }
    }
}
