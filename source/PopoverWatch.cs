using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace LumaDock;

// Закрытие карточки виджета щелчком вне её. Основной путь — событие Deactivated, но в одном из случаев у пользователя плеер
// висел, пока не нажмёшь на его плитку: Deactivated не пришёл (точная причина не установлена, у меня вживую он приходил).
// Это страховка независимо от активации окон.
// Раз в 40 мс проверяется новое нажатие кнопки мыши: если оно вне карточки и вне всплывающих меню программы, карточка закрывается.
internal static class PopoverWatch
{
    [StructLayout(LayoutKind.Sequential)] private struct Pt { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Box { public int Left, Top, Right, Bottom; }
    [DllImport("user32")] private static extern bool GetWindowRect(nint window, out Box box);
    [DllImport("user32")] private static extern nint WindowFromPoint(Pt point);
    [DllImport("user32")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);

    // Под курсором всплывающее меню или список этой же программы (не окно): щелчок по нему карточку не закрывает.
    // Любое окно программы, в том числе док и другие карточки, щелчком вне карточки её закрывает.
    private static bool OverOwnMenu(Point cursor)
    {
        var root = GetAncestor(WindowFromPoint(new Pt { X = (int)cursor.X, Y = (int)cursor.Y }), 2);
        if (root == 0) return false;
        GetWindowThreadProcessId(root, out uint pid);
        return pid == Environment.ProcessId && HwndSource.FromHwnd(root)?.RootVisual is not Window;
    }

    public static void Attach(Window window, Action close)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        bool wasDown = MenuWatch.ButtonDown();
        window.Closed += (_, _) => timer.Stop();
        timer.Tick += (_, _) =>
        {
            bool down = MenuWatch.ButtonDown();
            bool pressed = down && !wasDown;
            wasDown = down;
            if (!pressed || !window.IsVisible) return;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == 0 || !GetWindowRect(handle, out var box)) return;
            var cursor = MenuWatch.Cursor();
            if (new Rect(box.Left, box.Top, box.Right - box.Left, box.Bottom - box.Top).Contains(cursor) || OverOwnMenu(cursor)) return;
            close();
        };
        timer.Start();
    }
}
