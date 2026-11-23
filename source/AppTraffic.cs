using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace LumaDock;

// Сеть по приложениям: байты берутся из событий ядра Microsoft-Windows-Kernel-Network (трассировка ETW).
// Windows разрешает такую трассировку только администратору или члену группы «Пользователи журналов производительности»,
// поэтому без этого права Start() возвращает false, а окно «Сеть» предлагает один раз выдать право (GrantAccess).
internal sealed class AppTraffic
{
    private const string SessionName = "FlowBarNetwork";
    private static readonly Guid KernelNetwork = new("7DD42A49-5329-4832-8DFD-43D979153A88");
    private const int PropertiesSize = 120; // sizeof(EVENT_TRACE_PROPERTIES) для x64
    private const int LogfileSize = 512;    // с запасом над sizeof(EVENT_TRACE_LOGFILEW) = 448

    private readonly ConcurrentDictionary<int, long[]> totals = new(); // номер процесса -> [получено, отправлено] байт
    private readonly Dictionary<int, string> names = new();
    private readonly List<(long Ticks, Dictionary<string, long[]> Bytes)> snapshots = new();
    private readonly object gate = new();
    private int users;
    private ulong session, trace;
    private IntPtr properties, logfile;
    private Thread? worker;
    private EventRecordCallback? callback; // делегат нужен живым, пока работает трассировка

    public bool Running { get; private set; }
    // true — Windows отказала в доступе (нужны права); иначе трассировка не запустилась по другой причине
    public bool Denied { get; private set; }

    // Окна «Сеть» открываются и закрываются: трассировка работает, пока открыто хотя бы одно
    public bool Start()
    {
        lock (gate)
        {
            if (users++ == 0) Running = Begin();
            return Running;
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            if (users > 0 && --users == 0 && Running) { End(); Running = false; }
        }
    }

    // Раз в секунду снимает накопленные байты по именам программ; true — снимок новый
    public bool Update()
    {
        long now = Environment.TickCount64;
        if (snapshots.Count > 0 && now - snapshots[^1].Ticks < 1000) return false;
        var bytes = new Dictionary<string, long[]>();
        foreach (var (pid, counters) in totals)
        {
            string name = NameOf(pid);
            if (!bytes.TryGetValue(name, out var sum)) bytes[name] = sum = new long[2];
            sum[0] += Interlocked.Read(ref counters[0]);
            sum[1] += Interlocked.Read(ref counters[1]);
        }
        snapshots.Add((now, bytes));
        if (snapshots.Count > 6) snapshots.RemoveAt(0); // скорость считается за последние ~5 секунд
        return true;
    }

    // Самые «тратящие» программы: байты в секунду за окно снимков; одноимённые процессы (вкладки браузера) складываются
    public List<(string Name, double Rate)> Top(bool upload, int count = 5)
    {
        if (snapshots.Count < 2) return [];
        var (from, before) = snapshots[0];
        var (to, after) = snapshots[^1];
        double seconds = (to - from) / 1000.0;
        int side = upload ? 1 : 0;
        return after.Select(item => (item.Key, Rate: (item.Value[side] - (before.TryGetValue(item.Key, out var old) ? old[side] : 0)) / seconds))
            .Where(item => item.Rate > 0).OrderByDescending(item => item.Rate).Take(count).ToList();
    }

    // Для самопроверки: сколько байт [получено, отправлено] насчитано по процессу
    internal long[] Bytes(int pid) => totals.TryGetValue(pid, out var counters) ? [Interlocked.Read(ref counters[0]), Interlocked.Read(ref counters[1])] : [0, 0];

    // Разбор одного события (поток ETW). Для всех событий отправки и приёма TCP/UDP (IPv4 и IPv6) первые два поля —
    // номер процесса и число байт: 10/26 и 42/58 отправка, 11/27 и 43/59 приём
    internal void Handle(IntPtr record)
    {
        ushort id = (ushort)Marshal.ReadInt16(record, 40);
        int side = id is 11 or 27 or 43 or 59 ? 0 : id is 10 or 26 or 42 or 58 ? 1 : -1;
        if (side < 0 || (ushort)Marshal.ReadInt16(record, 86) < 8) return;
        IntPtr data = Marshal.ReadIntPtr(record, 96);
        int pid = Marshal.ReadInt32(data, 0), size = Marshal.ReadInt32(data, 4);
        if (size <= 0) return;
        Interlocked.Add(ref totals.GetOrAdd(pid, _ => new long[2])[side], size);
    }

    // shortcut: имя берётся один раз на номер процесса; если номер потом достанется другой программе, подпись останется старой
    private string NameOf(int pid)
    {
        if (names.TryGetValue(pid, out var known)) return known;
        string name;
        try { name = pid == 4 ? "System" : Process.GetProcessById(pid).ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { name = $"PID {pid}"; }
        return names[pid] = name;
    }

    // Один раз выдаёт текущему пользователю право на трассировку: добавляет в группу «Пользователи журналов производительности»
    // (запрос Windows на повышение прав; после этого нужно заново войти в Windows)
    public static bool GrantAccess()
    {
        try
        {
            string account = new SecurityIdentifier("S-1-5-32-559").Translate(typeof(NTAccount)).Value; // имя группы зависит от языка Windows
            string group = account[(account.IndexOf('\\') + 1)..];
            Process.Start(new ProcessStartInfo("net.exe", $"localgroup \"{group}\" \"{Environment.UserDomainName}\\{Environment.UserName}\" /add")
                { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IdentityNotMappedException) { return false; }
    }

    private bool Begin()
    {
        Denied = false;
        properties = Marshal.AllocHGlobal(PropertiesSize + 512);
        FillProperties();
        uint code = StartTrace(out session, SessionName, properties);
        if (code == 183) // сеанс с таким именем остался от оборванного запуска: гасим и начинаем заново
        {
            FillProperties();
            ControlTrace(0, SessionName, properties, 1);
            FillProperties();
            code = StartTrace(out session, SessionName, properties);
        }
        if (code != 0) { Denied = code == 5; Free(); return false; }
        var provider = KernelNetwork;
        // ключевые слова 0x10 и 0x20 — события IPv4 и IPv6
        if (EnableTraceEx2(session, ref provider, 1, 4, 0x30, 0, 0, IntPtr.Zero) != 0) { Terminate(); return false; }
        callback = record =>
        {
            try { Handle(record); }
            catch (Exception ex) when (ex is AccessViolationException or InvalidOperationException) { } // поток ETW не должен падать из-за одного события
        };
        logfile = Marshal.AllocHGlobal(LogfileSize);
        Marshal.Copy(new byte[LogfileSize], 0, logfile, LogfileSize);
        Marshal.WriteIntPtr(logfile, 8, Marshal.StringToHGlobalUni(SessionName)); // LoggerName
        Marshal.WriteInt32(logfile, 28, 0x10000100); // ProcessTraceMode: сеанс в реальном времени + запись события EVENT_RECORD
        Marshal.WriteIntPtr(logfile, 424, Marshal.GetFunctionPointerForDelegate(callback)); // EventRecordCallback
        trace = OpenTrace(logfile);
        if (trace == ulong.MaxValue) { Terminate(); return false; }
        var handle = trace;
        worker = new Thread(() => ProcessTrace([handle], 1, IntPtr.Zero, IntPtr.Zero)) { IsBackground = true, Name = "FlowBar network trace" };
        worker.Start();
        return true;
    }

    private void End()
    {
        Terminate();
        totals.Clear();
        snapshots.Clear();
    }

    // Останавливает сеанс и освобождает память: ProcessTrace возвращается сам, когда сеанс остановлен
    private void Terminate()
    {
        if (session != 0)
        {
            FillProperties();
            ControlTrace(session, SessionName, properties, 1);
            session = 0;
        }
        worker?.Join(2000);
        worker = null;
        if (trace != 0 && trace != ulong.MaxValue) CloseTrace(trace);
        trace = 0;
        Free();
    }

    private void Free()
    {
        if (properties != IntPtr.Zero) { Marshal.FreeHGlobal(properties); properties = IntPtr.Zero; }
        if (logfile != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(Marshal.ReadIntPtr(logfile, 8));
            Marshal.FreeHGlobal(logfile);
            logfile = IntPtr.Zero;
        }
    }

    // EVENT_TRACE_PROPERTIES: сеанс в реальном времени без файла, имя сеанса лежит сразу за структурой
    private void FillProperties()
    {
        int total = PropertiesSize + 512;
        Marshal.Copy(new byte[total], 0, properties, total);
        Marshal.WriteInt32(properties, 0, total);          // Wnode.BufferSize
        Marshal.WriteInt32(properties, 40, 1);             // Wnode.ClientContext: часы QPC
        Marshal.WriteInt32(properties, 44, 0x00020000);    // Wnode.Flags: WNODE_FLAG_TRACED_GUID
        Marshal.WriteInt32(properties, 48, 64);            // BufferSize, КБ
        Marshal.WriteInt32(properties, 64, 0x100);         // LogFileMode: EVENT_TRACE_REAL_TIME_MODE
        Marshal.WriteInt32(properties, 68, 1);             // FlushTimer: сбрасывать буфер раз в секунду
        Marshal.WriteInt32(properties, 116, PropertiesSize); // LoggerNameOffset
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void EventRecordCallback(IntPtr record);
    [DllImport("advapi32", CharSet = CharSet.Unicode)] private static extern uint StartTrace(out ulong handle, string name, IntPtr properties);
    [DllImport("advapi32", CharSet = CharSet.Unicode)] private static extern uint ControlTrace(ulong handle, string name, IntPtr properties, uint code);
    [DllImport("advapi32")] private static extern uint EnableTraceEx2(ulong handle, ref Guid provider, uint control, byte level, ulong anyKeyword, ulong allKeyword, uint timeout, IntPtr parameters);
    [DllImport("advapi32", CharSet = CharSet.Unicode)] private static extern ulong OpenTrace(IntPtr logfile);
    [DllImport("advapi32")] private static extern uint ProcessTrace([In] ulong[] handles, uint count, IntPtr start, IntPtr end);
    [DllImport("advapi32")] private static extern uint CloseTrace(ulong handle);
}
