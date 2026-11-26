using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace LumaDock;

internal static partial class Program
{
    [DllImport("user32.dll", EntryPoint = "GetWindow")] private static extern nint RevealAdjacent(nint window, uint command);
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint RevealForeground();

    private static async System.Threading.Tasks.Task VerifyRevealOrder(DockWindow dock)
    {
        var fixture = new Window
        {
            Title = "Reveal order fixture", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, ShowActivated = false, Topmost = true, Background = Brushes.Black,
            Left = dock.Left, Top = dock.Top, Width = dock.Width, Height = dock.Height
        };
        static bool Above(nint upper, nint lower)
        {
            for (nint window = RevealAdjacent(lower, 3); window != 0; window = RevealAdjacent(window, 3))
                if (window == upper) return true;
            return false;
        }
        try
        {
            fixture.Show();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            nint overlay = new WindowInteropHelper(fixture).Handle;
            nint panel = new WindowInteropHelper(dock).Handle;
            if (WindowsShell.RaiseWithoutActivation(0)) throw new InvalidOperationException("An empty window handle was accepted.");
            // Другой Topmost появился позже дока; проверяем и повторный выезд после перехвата порядка окон.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!WindowsShell.RaiseWithoutActivation(overlay) || !Above(overlay, panel))
                    throw new InvalidOperationException("Could not reproduce the panel behind another topmost window.");
                nint foreground = RevealForeground();
                var bounds = dock.RestoreBounds;
                if (!WindowsShell.RaiseWithoutActivation(panel) || !Above(panel, overlay))
                    throw new InvalidOperationException("Revealing did not raise the panel above the covering window.");
                if (RevealForeground() != foreground || dock.RestoreBounds != bounds)
                    throw new InvalidOperationException("Revealing changed focus or panel bounds.");
            }
        }
        finally { fixture.Close(); }
    }
}
