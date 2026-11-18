using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace LumaDock;

// Закрытие меню щелчком вне его. Окно дока не получает фокус, поэтому WPF не замечает щелчков в других программах и меню оставалось бы открытым.
// Пока меню открыто, раз в 40 мс проверяется нажатие любой кнопки мыши: если оно произошло вне меню, меню закрывается.
internal static class MenuWatch
{
    [DllImport("user32")] private static extern short GetAsyncKeyState(int key);

    // В проверке --smoke заменяются, чтобы не нажимать на настоящие окна пользователя
    internal static Func<bool> ButtonDown = () => (GetAsyncKeyState(0x01) < 0) || (GetAsyncKeyState(0x02) < 0) || (GetAsyncKeyState(0x04) < 0);
    internal static Func<Point> Cursor = () => { var point = System.Windows.Forms.Cursor.Position; return new Point(point.X, point.Y); };

    // Нужно ли закрыть меню: кнопка только что нажата (раньше была отпущена) и курсор вне прямоугольника меню в экранных пикселях
    internal static bool ShouldClose(bool down, bool wasDown, Point cursor, Rect menu) => down && !wasDown && !menu.Contains(cursor);

    internal static Rect Bounds(ContextMenu menu)
    {
        var dpi = VisualTreeHelper.GetDpi(menu);
        var origin = menu.PointToScreen(new Point(0, 0));
        return new Rect(origin.X, origin.Y, menu.ActualWidth * dpi.DpiScaleX, menu.ActualHeight * dpi.DpiScaleY);
    }

    public static void Attach(ContextMenu menu)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        bool wasDown = false;
        menu.Opened += (_, _) => { wasDown = ButtonDown(); timer.Start(); };
        menu.Closed += (_, _) => timer.Stop();
        timer.Tick += (_, _) =>
        {
            bool down = ButtonDown();
            if (menu.IsOpen && ShouldClose(down, wasDown, Cursor(), Bounds(menu))) menu.IsOpen = false;
            wasDown = down;
        };
    }
}
