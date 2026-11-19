using System;
using System.Drawing;
using System.Threading.Tasks;

namespace LumaDock;

// Системный трей Windows — единственный трей FlowBar. Своего окна со значками нет.
internal static partial class WindowsShell
{
    private const string OverflowClass = "TopLevelWindowForOverflowXamlIsland";

    // FindWindow это окно Проводника не находит (проверено), а EnumWindows находит; невидимые копии пропускаем
    private static nint OverflowWindow()
    {
        nint found = 0;
        var name = new System.Text.StringBuilder(64);
        EnumWindows((window, _) =>
        {
            GetClassName(window, name, name.Capacity);
            if (name.ToString() != OverflowClass || !IsWindowVisible(window)) return true;
            found = window;
            return false;
        }, 0);
        return found;
    }

    // Win+B ставит фокус на трей Windows, Enter открывает его окно скрытых значков (без паузы Windows пропускает Enter).
    // Проводник открывает окно у своей панели задач, поэтому сами переносим его над плиткой, по которой нажали.
    public static async Task OpenTrayFlyout(Rectangle anchor, Rectangle screen)
    {
        Key(0x5B, 0x42);
        await Task.Delay(400);
        Key(0x0D);
        // окно появляется не сразу, а Проводник может вернуть его на место сразу после показа, поэтому ставим несколько раз
        for (int attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(attempt < 4 ? 60 : 120);
            nint window = OverflowWindow();
            if (window == 0 || !GetWindowRect(window, out var box)) continue;
            int width = box.Right - box.Left, height = box.Bottom - box.Top;
            int x = Math.Clamp(anchor.Left + anchor.Width / 2 - width / 2, screen.Left + 8, Math.Max(screen.Left + 8, screen.Right - width - 8));
            int y = Math.Clamp(anchor.Top - height - 12, screen.Top + 8, Math.Max(screen.Top + 8, screen.Bottom - height - 8));
            if (Math.Abs(box.Left - x) > 2 || Math.Abs(box.Top - y) > 2)
                SetWindowPos(window, 0, x, y, 0, 0, 0x1 | 0x4 | 0x10); // не менять размер, порядок и активность
            else if (attempt >= 4) return; // встало и держится
        }
    }
}
