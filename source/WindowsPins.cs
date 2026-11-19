using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LumaDock;

// Закреплённые значки панели задач Windows. Обычные программы лежат ярлыками в «User Pinned\TaskBar», а программы из магазина
// (ChatGPT, WhatsApp, Claude…) ярлыка не имеют: они есть только в двоичном значении реестра Taskband\Favorites по идентификатору AppUserModelID.
// Порядок значков тоже только там. В Windows отсюда ничего не пишем: закрепить в её панели из дока нельзя, импорт идёт в одну сторону.
internal static class WindowsPins
{
    private static readonly string PinFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
    private const string FavoritesKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";

    // имя пакета_издатель!приложение, например OpenAI.Codex_2p2nqsd0c76g0!App
    private static readonly Regex AppIdPattern = new(@"[A-Za-z0-9][A-Za-z0-9.\-]*_[a-z0-9]{13}![A-Za-z0-9._\-]+", RegexOptions.Compiled);

    // Значения идентификаторов и имён в двоичном блоке лежат то как ANSI, то как UTF-16 (с любой чётностью смещения), поэтому ищем во всех трёх видах.
    // Порядок — по первому появлению. Идентификаторы проверяет appExists: перед строкой стоит байт длины, который иногда оказывается буквой,
    // и она прилипает к началу; устаревшие записи (удалённые программы) тоже отсеиваются. Возвращает имена .lnk и «shell:AppsFolder\…» в порядке Windows.
    internal static List<string> Order(byte[] blob, IEnumerable<string> lnkNames, Func<string, bool> appExists)
    {
        var texts = new List<(string Text, int Start, int Step)> { (Encoding.Latin1.GetString(blob), 0, 1) };
        for (int parity = 0; parity < 2; parity++)
            texts.Add((Encoding.Unicode.GetString(blob, parity, (blob.Length - parity) / 2 * 2), parity, 2));
        var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void Note(string entry, int at) { if (!found.TryGetValue(entry, out int old) || at < old) found[entry] = at; }
        var names = lnkNames.ToList();
        foreach (var (text, start, step) in texts)
        {
            foreach (var name in names)
            {
                int at = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
                if (at >= 0) Note(name, start + step * at);
            }
            foreach (Match match in AppIdPattern.Matches(text))
                for (int skip = 0; skip <= 2 && skip < match.Length; skip++)
                {
                    string id = WindowsShell.StorePrefix + match.Value[skip..];
                    if (!appExists(id)) continue;
                    Note(id, start + step * (match.Index + skip));
                    break;
                }
        }
        var result = found.OrderBy(pair => pair.Value).Select(pair => pair.Key).ToList();
        result.AddRange(names.Where(name => !found.ContainsKey(name))); // ярлыки, которых нет в блоке: в конец
        return result;
    }

    // Добавляет в items закреплённые программы Windows, которых там ещё нет, и запоминает их в seen: убранная из дока программа обратно не возвращается,
    // а открепление в Windows ничего в доке не удаляет. Новая программа встаёт после предыдущей программы из панели Windows, которая уже есть в доке.
    internal static bool Merge(List<string> items, List<string> seen, IEnumerable<string> pins, Func<string, string> key)
    {
        bool changed = false;
        int previous = items.IndexOf("Start");
        if (previous >= 0 && previous + 1 < items.Count && items[previous + 1] == "Search") previous++; // программы идут после кнопки поиска
        foreach (var pin in pins)
        {
            string name = key(pin);
            int at = items.FindIndex(item => item.StartsWith("app:", StringComparison.Ordinal) && string.Equals(key(item), name, StringComparison.OrdinalIgnoreCase));
            bool known = seen.Contains(name, StringComparer.OrdinalIgnoreCase);
            if (at >= 0) { previous = at; }
            else if (!known) items.Insert(++previous, pin);
            if (!known) { seen.Add(name); changed = true; }
        }
        return changed;
    }

    private static byte[] Favorites() => Registry.GetValue(FavoritesKey, "Favorites", null) as byte[] ?? [];

    private static List<string> LinkNames() =>
        Directory.Exists(PinFolder) ? Directory.GetFiles(PinFolder, "*.lnk").Select(path => Path.GetFileName(path)).Order(StringComparer.OrdinalIgnoreCase).ToList() : [];

    // Отпечаток состояния панели Windows: пока он тот же, перечитывать нечего
    internal static string Signature()
    {
        try { return string.Join('|', LinkNames()) + "#" + Convert.ToHexString(SHA1.HashData(Favorites())); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { return ""; }
    }

    // Идентификаторы плиток дока («app:…») закреплённых программ Windows в её порядке
    internal static List<string> Read()
    {
        try
        {
            return Order(Favorites(), LinkNames(), WindowsShell.StoreAppExists)
                .Select(entry => "app:" + (entry.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? Path.Combine(PinFolder, entry) : entry))
                .Where(id => WindowsShell.IsStoreApp(id[4..]) || !WindowsShell.MatchApp(id[4..]).EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) // ярлык с мёртвой целью не берём
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { return []; }
    }

    // Проверка для --smoke: разбор блока и слияние без обращения к настоящей панели Windows
    internal static string Verify()
    {
        // ANSI-имя, UTF-16 с нечётным смещением (с прилипшей буквой «F» от байта длины), магазинный идентификатор и устаревшая запись
        var blob = new List<byte>();
        void Ansi(string text) { blob.AddRange(Encoding.Latin1.GetBytes(text)); blob.Add(0); }
        void Wide(string text) { blob.AddRange(Encoding.Unicode.GetBytes(text)); blob.AddRange(new byte[] { 0, 0 }); }
        Wide("File Explorer.lnk"); blob.Add(7);
        Wide("5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App"); blob.Add(9);
        Ansi("Telegram.lnk"); Wide("Telegram.lnk");
        Wide("FSpotify.lnk");
        Wide("Dead_aaaaaaaaaaaaa!Gone"); // такого приложения нет
        Wide("xClaude_pzs8sxrjxfjjc!Claude"); // лишняя буква спереди
        string[] store = ["5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App", "Claude_pzs8sxrjxfjjc!Claude"];
        var order = Order(blob.ToArray(), ["Spotify.lnk", "Telegram.lnk", "File Explorer.lnk", "Late.lnk"],
            id => store.Contains(id[WindowsShell.StorePrefix.Length..]));
        string[] expected = ["File Explorer.lnk", WindowsShell.StorePrefix + store[0], "Telegram.lnk", "Spotify.lnk", WindowsShell.StorePrefix + store[1], "Late.lnk"];
        if (!order.SequenceEqual(expected)) return "порядок закреплённых Windows разобран неверно: " + string.Join(", ", order);

        var items = new List<string> { "Todos", "Start", "app:E", "app:T", "app:Own" };
        var seen = new List<string>();
        string Key(string id) => id.ToUpperInvariant().Replace("APP:", "");
        string[] pins = ["app:E", "app:W", "app:T", "app:C"];
        if (!Merge(items, seen, pins, Key) || !items.SequenceEqual(["Todos", "Start", "app:E", "app:W", "app:T", "app:C", "app:Own"]))
            return "слияние закреплённых Windows поставило программы не туда: " + string.Join(", ", items);
        items.Remove("app:W"); // пользователь убрал программу из дока
        if (Merge(items, seen, pins, Key) || items.Contains("app:W")) return "убранная из дока программа вернулась";
        return "";
    }
}

internal static partial class WindowsShell
{
    internal const string StorePrefix = @"shell:AppsFolder\";
    private static readonly Dictionary<string, string?> StoreNames = new(StringComparer.OrdinalIgnoreCase);

    internal static bool IsStoreApp(string path) => path.StartsWith(StorePrefix, StringComparison.OrdinalIgnoreCase);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellNamedItem
    {
        [PreserveSig] int BindToHandler(nint context, ref Guid handler, ref Guid id, out nint result);
        [PreserveSig] int GetParent(out nint parent);
        [PreserveSig] int GetDisplayName(uint form, out nint name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(nint other, uint hint, out int order);
    }
    [DllImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int ParseShellItem(string path, nint context, ref Guid id, out IShellNamedItem item);

    // Название приложения из «Приложений» Windows («ChatGPT»); null — такого приложения нет
    internal static string? StoreAppName(string path)
    {
        if (StoreNames.TryGetValue(path, out var cached)) return cached;
        var id = typeof(IShellNamedItem).GUID;
        IShellNamedItem? item = null;
        string? name = null;
        try
        {
            if (ParseShellItem(path, 0, ref id, out item) == 0 && item is not null && item.GetDisplayName(0, out nint text) == 0 && text != 0)
            {
                name = Marshal.PtrToStringUni(text);
                Marshal.FreeCoTaskMem(text);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { }
        finally { if (item is not null) Marshal.ReleaseComObject(item); }
        return StoreNames[path] = string.IsNullOrWhiteSpace(name) ? null : name;
    }

    internal static bool StoreAppExists(string path) => StoreAppName(path) is not null;

    [StructLayout(LayoutKind.Sequential)] private struct ProcessBasic { public nint Exit, Peb, Affinity, Priority, Id, ParentId; }
    [DllImport("ntdll")] private static extern int NtQueryInformationProcess(nint process, int kind, ref ProcessBasic info, int size, out int length);

    private static uint ParentOf(uint pid)
    {
        var handle = OpenProcess(0x1000, false, pid);
        if (handle == 0) return 0;
        try
        {
            var info = new ProcessBasic();
            return NtQueryInformationProcess(handle, 0, ref info, Marshal.SizeOf<ProcessBasic>(), out _) == 0 ? (uint)info.ParentId : 0;
        }
        finally { CloseHandle(handle); }
    }

    // host запустил app и лежит в папке выше него: помощник и его главная программа (окно Steam принадлежит steamwebhelper.exe, закреплён steam.exe).
    // Папки Windows не считаются: иначе любое окно, запущенное из Проводника, сливалось бы с его значком.
    internal static bool IsHostOf(string app, string host)
    {
        var folder = Path.GetDirectoryName(host);
        return folder is not null && !host.Equals(app, StringComparison.OrdinalIgnoreCase)
            && app.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !(folder + Path.DirectorySeparatorChar).StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // Главные программы окна (до трёх родителей вверх по цепочке запуска), см. IsHostOf
    internal static List<string> HostApps(nint window, string app)
    {
        var hosts = new List<string>();
        GetWindowThreadProcessId(window, out uint current);
        for (int level = 0; level < 3 && current != 0; level++)
        {
            current = ParentOf(current);
            string path = current == 0 ? "" : ProcessPath(current);
            if (path.Length == 0) break;
            if (IsHostOf(app, path)) hosts.Add(ResolveApp(path));
        }
        return hosts;
    }

    // Подпись плитки программы: у приложений из магазина название из «Приложений», у остальных имя файла
    internal static string AppLabel(string path) => IsStoreApp(path) ? StoreAppName(path) ?? path : Path.GetFileNameWithoutExtension(path);
}
