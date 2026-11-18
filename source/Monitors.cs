using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Screen = System.Windows.Forms.Screen;

namespace LumaDock;

// Док на каждом мониторе: главный док стоит на основном мониторе, для остальных создаются дополнительные. Они появляются и исчезают
// при подключении и отключении монитора, при смене разрешения и при выключении настройки «Все мониторы».
internal static class Monitors
{
    private static readonly Dictionary<string, DockWindow> docks = new(StringComparer.OrdinalIgnoreCase);
    private static DockModel? model;
    private static Dispatcher? dispatcher;

    public static void Attach(DockModel dockModel, Window primary)
    {
        model = dockModel;
        dispatcher = primary.Dispatcher;
        Sync();
        model.Changed += Sync;
        SystemEvents.DisplaySettingsChanged += (_, _) => dispatcher.BeginInvoke(Sync);
    }

    public static void Sync()
    {
        if (model is null) return;
        var wanted = model.Settings.AllMonitors ? Screen.AllScreens.Where(screen => !screen.Primary).ToList() : [];
        foreach (string device in docks.Keys.Where(device => wanted.All(screen => screen.DeviceName != device)).ToList())
        {
            docks[device].Close();
            docks.Remove(device);
        }
        foreach (var screen in wanted)
        {
            if (docks.TryGetValue(screen.DeviceName, out var existing))
            {
                existing.Relayout(); // разрешение или положение монитора могли измениться
                continue;
            }
            var dock = new DockWindow(model, false, false, screen);
            docks[screen.DeviceName] = dock;
            dock.Closed += (_, _) => { if (docks.TryGetValue(screen.DeviceName, out var current) && current == dock) docks.Remove(screen.DeviceName); };
            dock.Show();
        }
    }
}
