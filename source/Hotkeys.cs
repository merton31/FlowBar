using System;
using System.Linq;
using System.Windows.Input;

namespace LumaDock;

// Глобальные горячие клавиши из FlowBar: для каждого действия своё сочетание, которое задаётся в «Настройки → Общие».
// Хранится как «модификаторы,код клавиши» (Alt=1, Ctrl=2, Shift=4, Win=8, как в Windows), пустая строка — не задано.
// Перенесено из FlowBar.Next.cs (HotkeyNames, ApplyHotkeys, HotkeyLabel); действия подобраны под LumaDock.
internal static class Hotkeys
{
    // Окна, которые открывают сочетания (они же подписи в настройках); номер сочетания в Windows — позиция + 1
    public static readonly string[] Items = ["Todos", "Dictation", "Notes"];
    // Alt+T открывает задачи, как и в прежних версиях; остальные выключены, пока пользователь их не задаст
    public static readonly string[] Defaults = ["1,84", "", ""];

    public static string Get(Preferences p, int index) => p.Hotkeys.Length > index ? p.Hotkeys[index] : Defaults[index];

    public static void Set(Preferences p, int index, string value)
    {
        var all = Enumerable.Range(0, Items.Length).Select(i => Get(p, i)).ToArray();
        all[index] = value;
        p.Hotkeys = all;
    }

    // Разбор «модификаторы,код»: нужна хотя бы одна клавиша-модификатор и обычная клавиша (код 8–254)
    public static bool TryParse(string? value, out uint modifiers, out uint key)
    {
        modifiers = key = 0;
        var parts = (value ?? "").Split(',');
        return parts.Length == 2 && uint.TryParse(parts[0], out modifiers) && uint.TryParse(parts[1], out key)
            && modifiers is >= 1 and <= 15 && key is >= 8 and <= 254;
    }

    public static string Label(string? value)
    {
        if (!TryParse(value, out uint modifiers, out uint key)) return "Not set";
        string name = key == 32 ? "Space" : KeyInterop.KeyFromVirtualKey((int)key).ToString();
        return ((modifiers & 2) != 0 ? "Ctrl+" : "") + ((modifiers & 1) != 0 ? "Alt+" : "") + ((modifiers & 4) != 0 ? "Shift+" : "")
            + ((modifiers & 8) != 0 ? "Win+" : "") + name;
    }

    // Сочетание из нажатых клавиш; null, если нажаты только модификаторы или нет ни одного модификатора
    public static string? FromKeys(ModifierKeys modifiers, Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None) return null;
        int code = KeyInterop.VirtualKeyFromKey(key);
        uint bits = (uint)modifiers & 15;
        return bits == 0 || code is < 8 or > 254 ? null : bits + "," + code;
    }

    // Другое действие с тем же сочетанием: два действия на одну клавишу Windows не даёт
    public static bool Taken(Preferences p, int index, string value) =>
        Enumerable.Range(0, Items.Length).Any(i => i != index && Get(p, i) == value);
}
