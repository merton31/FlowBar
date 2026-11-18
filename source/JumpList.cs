using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace LumaDock;

// Список переходов программы (то, что Windows показывает по правому щелчку на значке в панели задач): разделы вроде «Недавние» и «Tasks» с пунктами.
// Пункты берутся из файла Recent\CustomDestinations\<хеш>.customDestinations-ms, который программа ведёт сама; имя файла — CRC-64 от AppUserModelID окна
// (UTF-16, верхний регистр, обратный порядок битов, начальное значение из единиц). Читается только то, что лежит в профиле самого пользователя.
internal static class JumpList
{
    internal sealed record Entry(string Title, byte[] Link);
    internal sealed record Group(string Title, List<Entry> Entries);

    private const int MaxEntries = 8;
    private static readonly byte[] Footer = [0xAB, 0xFB, 0xBF, 0xBA];
    // начало записи: идентификатор ShellLink (16 байт) и заголовок .lnk: размер 0x4C, затем тот же идентификатор
    private static readonly byte[] LinkStart = [0x4C, 0, 0, 0, 0x01, 0x14, 0x02, 0, 0, 0, 0, 0, 0xC0, 0, 0, 0, 0, 0, 0, 0x46];

    // ---- Имя файла по AppUserModelID ----

    internal static string Hash(string appId)
    {
        const ulong poly = 0x92C64265D32139A4;
        ulong crc = ulong.MaxValue;
        foreach (byte value in Encoding.Unicode.GetBytes(appId.ToUpperInvariant()))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ poly : crc >> 1;
        }
        return crc.ToString("x16");
    }

    internal static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Recent", "CustomDestinations");

    // Разделы списка переходов для окна: пусто, если у программы нет явного AppUserModelID, файла или в нём нет пунктов
    public static List<Group> ForWindow(nint window)
    {
        try
        {
            string? appId = AppId(window);
            if (string.IsNullOrEmpty(appId)) return [];
            string file = Path.Combine(Folder, Hash(appId) + ".customDestinations-ms");
            if (!File.Exists(file)) return [];
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length is < 40 or > 4 * 1024 * 1024) return [];
            var data = new byte[stream.Length];
            stream.ReadExactly(data);
            return Parse(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or InvalidCastException) { return []; }
    }

    // ---- Разбор файла ----

    // Заголовок: версия 2, число разделов, 0. Раздел: тип (0 — свой с названием, 1 — частые, 2 — задачи), для типа 0 длина названия и название UTF-16,
    // число записей, записи, затем 0xBABFFBAB. Показываются свои разделы и задачи; пустые и частые пропускаются.
    internal static List<Group> Parse(byte[] data)
    {
        var groups = new List<Group>();
        if (data.Length < 16 || BitConverter.ToUInt32(data, 0) != 2) return groups;
        uint categories = BitConverter.ToUInt32(data, 4);
        int position = 12;
        for (uint category = 0; category < categories && category < 16; category++)
        {
            if (position + 8 > data.Length) break;
            uint type = BitConverter.ToUInt32(data, position);
            position += 4;
            string title = "";
            if (type == 0)
            {
                int length = BitConverter.ToUInt16(data, position);
                position += 2;
                if (position + length * 2 > data.Length) break;
                title = Encoding.Unicode.GetString(data, position, length * 2);
                position += length * 2;
            }
            position += 4; // число записей: ниже записи ищутся по заголовку, это надёжнее при повреждённом файле
            int end = IndexOf(data, Footer, position);
            if (end < 0) break;
            var entries = new List<Entry>();
            var starts = new List<int>();
            for (int at = IndexOf(data, LinkStart, position); at >= 0 && at < end; at = IndexOf(data, LinkStart, at + 4)) starts.Add(at);
            for (int index = 0; index < starts.Count && entries.Count < MaxEntries; index++)
            {
                int stop = index + 1 < starts.Count ? starts[index + 1] - 16 : end;
                if (stop <= starts[index]) continue;
                var link = data[starts[index]..stop];
                string name = Resource(ReadTitle(link) ?? "");
                if (name.Length > 0) entries.Add(new Entry(name, link));
            }
            if (type != 1 && entries.Count > 0)
                groups.Add(new Group(type == 2 ? "Tasks" : Resource(title), entries));
            position = end + 4;
        }
        return groups;
    }

    private static int IndexOf(byte[] data, byte[] pattern, int from) => from < 0 || from >= data.Length ? -1 : data.AsSpan(from).IndexOf(pattern) is var found and >= 0 ? from + found : -1;

    // ---- Запуск пункта ----

    // Пункт сохраняется как обычный ярлык во временную папку и открывается оболочкой, как в списке переходов Windows
    public static bool Launch(Entry entry)
    {
        string? file = null;
        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "LumaDock-jump");
            Directory.CreateDirectory(folder);
            foreach (string old in Directory.GetFiles(folder, "*.lnk").Where(path => File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddHours(-1)))
            { try { File.Delete(old); } catch (IOException) { } }
            file = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".lnk");
            var link = Load(entry.Link) ?? throw new InvalidOperationException();
            try { ((IPersistFile)link).Save(file, true); }
            finally { Marshal.ReleaseComObject(link); }
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    // ---- COM: ярлык из байтов, название из свойств, идентификатор приложения окна ----

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, nint data, uint flags);
        [PreserveSig] int GetIDList(out nint list);
        [PreserveSig] int SetIDList(nint list);
        [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int max);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int max);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int max);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        [PreserveSig] int GetHotkey(out short hotkey);
        [PreserveSig] int SetHotkey(short hotkey);
        [PreserveSig] int GetShowCmd(out int command);
        [PreserveSig] int SetShowCmd(int command);
        [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, out int index);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        [PreserveSig] int Resolve(nint window, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    // Для проверки: байты обычного ярлыка с названием, программой и параметрами
    internal static byte[] MakeTestLink(string description, string target, string arguments)
    {
        string file = Path.Combine(Path.GetTempPath(), "LumaDock-testlink-" + Guid.NewGuid().ToString("N") + ".lnk");
        var link = new ShellLink();
        try
        {
            var shell = (IShellLinkW)link;
            shell.SetPath(target);
            shell.SetArguments(arguments);
            shell.SetDescription(description);
            ((IPersistFile)link).Save(file, true);
            return File.ReadAllBytes(file);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("00000109-0000-0000-C000-000000000046")]
    private interface IPersistStream
    {
        [PreserveSig] int GetClassID(out Guid id);
        [PreserveSig] int IsDirty();
        [PreserveSig] int Load(IStream stream);
        [PreserveSig] int Save(IStream stream, [MarshalAs(UnmanagedType.Bool)] bool clear);
        [PreserveSig] int GetSizeMax(out long size);
    }

    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Pointer; }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [DllImport("shlwapi", EntryPoint = "SHCreateMemStream")]
    [return: MarshalAs(UnmanagedType.Interface)]
    private static extern IStream? CreateMemoryStream(byte[] data, uint size);
    [DllImport("shell32")] private static extern int SHGetPropertyStoreForWindow(nint window, ref Guid id, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);
    [DllImport("ole32")] private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("shlwapi", CharSet = CharSet.Unicode)] private static extern int SHLoadIndirectString(string source, StringBuilder result, int size, nint reserved);

    // Ярлык из байтов .lnk; null, если разобрать не удалось. Вызывающий освобождает объект
    private static object? Load(byte[] link)
    {
        var stream = CreateMemoryStream(link, (uint)link.Length);
        if (stream is null) return null;
        var shellLink = new ShellLink();
        try
        {
            if (((IPersistStream)shellLink).Load(stream) < 0) { Marshal.ReleaseComObject(shellLink); return null; }
            return shellLink;
        }
        finally { Marshal.ReleaseComObject(stream); }
    }

    private static readonly Guid TitleFormat = new("F29F85E0-4FF9-1068-AB91-08002B27B3D9");

    // Название пункта: свойство System.Title ярлыка, иначе описание
    private static string? ReadTitle(byte[] link)
    {
        object? shellLink = null;
        try
        {
            shellLink = Load(link);
            if (shellLink is null) return null;
            var key = new PropertyKey { Format = TitleFormat, Id = 2 };
            if (((IPropertyStore)shellLink).GetValue(ref key, out var value) >= 0)
            {
                try { if (value.Type == 31 && value.Pointer != 0) return Marshal.PtrToStringUni(value.Pointer); }
                finally { PropVariantClear(ref value); }
            }
            var description = new StringBuilder(512);
            return ((IShellLinkW)shellLink).GetDescription(description, description.Capacity) >= 0 && description.Length > 0 ? description.ToString() : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return null; }
        finally { if (shellLink is not null) Marshal.ReleaseComObject(shellLink); }
    }

    // Строки вида «@{пакет?ms-resource:…}» превращаются в обычный текст
    private static string Resource(string text)
    {
        text = text.Trim();
        if (!text.StartsWith("@{", StringComparison.Ordinal)) return text;
        var result = new StringBuilder(260);
        return SHLoadIndirectString(text, result, result.Capacity, 0) == 0 ? result.ToString().Trim() : "";
    }

    private static readonly Guid AppIdFormat = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("kernel32")] private static extern nint OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(nint process, ref int length, StringBuilder id);

    // AppUserModelID окна: свойство самого окна, иначе идентификатор пакета (у приложений из магазина и MSIX, например Claude, он есть у процесса)
    internal static string? AppId(nint window)
    {
        string? fromWindow = WindowAppId(window);
        if (!string.IsNullOrEmpty(fromWindow)) return fromWindow;
        GetWindowThreadProcessId(window, out uint process);
        if (process == 0) return null;
        nint handle = OpenProcess(0x1000, false, process);
        if (handle == 0) return null;
        try
        {
            int length = 256;
            var id = new StringBuilder(length);
            return GetApplicationUserModelId(handle, ref length, id) == 0 ? id.ToString() : null;
        }
        finally { CloseHandle(handle); }
    }

    private static string? WindowAppId(nint window)
    {
        IPropertyStore? store = null;
        try
        {
            var id = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(window, ref id, out store) < 0 || store is null) return null;
            var key = new PropertyKey { Format = AppIdFormat, Id = 5 };
            if (store.GetValue(ref key, out var value) < 0) return null;
            try { return value.Type == 31 && value.Pointer != 0 ? Marshal.PtrToStringUni(value.Pointer) : null; }
            finally { PropVariantClear(ref value); }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return null; }
        finally { if (store is not null) Marshal.ReleaseComObject(store); }
    }
}
