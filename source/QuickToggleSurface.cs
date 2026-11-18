using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using static LumaDock.Visuals;

namespace LumaDock;

internal sealed partial class DockWindow
{
    // Раздел со списком под переключателем: строки с названием, состоянием и кнопкой; возвращает функцию обновления списка (true — заново просканировать эфир Wi-Fi)
    private Func<bool, Task> AddDevices(StackPanel panel, string id, Window window)
    {
        bool wifi = id == "Wi-Fi";
        var heading = Text(wifi ? "Available networks" : "Paired devices", 13, true, Muted);
        heading.Margin = new(3, 12, 3, 6);
        var list = Stack();
        var note = Text("", 12, false, Muted);
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new(3, 6, 3, 4);
        panel.Children.Add(heading);
        panel.Children.Add(list);
        panel.Children.Add(note);
        bool busy = false;
        Func<bool, Task>? reload = null;

        // Bluetooth: «Добавить устройство» — поиск ещё не сопряжённых устройств поблизости и сопряжение, как в Windows
        if (!wifi)
        {
            var nearbyTitle = Text("Nearby devices", 13, true, Muted);
            nearbyTitle.Margin = new(3, 10, 3, 6);
            nearbyTitle.Visibility = Visibility.Collapsed;
            var nearby = Stack();
            var addLabel = Text("Add a device", 13, true, Paper);
            var add = Button(addLabel, () => { }, Brush("#39393D"), 10);
            add.Padding = new(14, 6, 14, 6);
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new(0, 8, 0, 0);
            System.Windows.Automation.AutomationProperties.SetName(add, L10n.T("Add a device"));
            panel.Children.Add(add);
            panel.Children.Add(nearbyTitle);
            panel.Children.Add(nearby);
            BluetoothScan? scan = null;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
            void StopScan(bool finished)
            {
                timer.Stop();
                scan?.Stop();
                addLabel.Text = L10n.T("Add a device");
                if (finished && nearby.Children.Count == 0)
                    note.Text = L10n.T("No new devices found. Put the device in pairing mode.");
                else if (finished) note.Text = "";
            }
            timer.Tick += (_, _) => StopScan(true);
            window.Closed += (_, _) => { timer.Stop(); scan?.Dispose(); };

            bool Ask(string kind, string pin) => window.Dispatcher.Invoke(() => kind == "match"
                ? MessageBox.Show(L10n.F($"Does this code match the one on the device: {pin}?"), "FlowBar", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes
                : MessageBox.Show(L10n.F($"Enter this code on the device: {pin}"), "FlowBar", MessageBoxButton.OK, MessageBoxImage.Information) == MessageBoxResult.OK);

            async Task Pair(BluetoothItem device, FrameworkElement row)
            {
                row.IsEnabled = false;
                note.Text = L10n.T("Pairing…");
                var (ok, message) = await scan!.PairAsync(device.Id, Ask);
                if (!window.IsLoaded) return;
                if (ok)
                {
                    nearby.Children.Remove(row);
                    note.Text = L10n.T("The device was paired.");
                    if (reload is not null) await reload(false);
                }
                else
                {
                    row.IsEnabled = true;
                    note.Text = L10n.T(message);
                }
            }

            add.Click += (_, _) =>
            {
                if (timer.IsEnabled) { StopScan(false); return; }
                scan?.Dispose();
                scan = new BluetoothScan();
                nearby.Children.Clear();
                nearbyTitle.Visibility = Visibility.Visible;
                addLabel.Text = L10n.T("Stop searching");
                note.Text = L10n.T("Searching for devices…");
                scan.Found += device => window.Dispatcher.BeginInvoke(() =>
                {
                    if (!window.IsLoaded || !timer.IsEnabled) return;
                    var icon = Symbol("\uE702", 17, Paper);
                    icon.Width = 28;
                    var name = RawText(device.Name, 14, true, Paper);
                    name.TextTrimming = TextTrimming.CharacterEllipsis;
                    name.VerticalAlignment = VerticalAlignment.Center;
                    FrameworkElement? card = null;
                    var pair = Button(Text("Pair", 12, true), () => _ = Pair(device, card!), Brush("#39393D"), 9);
                    pair.Padding = new(11, 4, 11, 4);
                    System.Windows.Automation.AutomationProperties.SetName(pair, L10n.T("Pair") + " " + device.Name);
                    var line = new DockPanel { Margin = new(8, 8, 8, 8) };
                    DockPanel.SetDock(icon, Dock.Left);
                    DockPanel.SetDock(pair, Dock.Right);
                    line.Children.Add(icon);
                    line.Children.Add(pair);
                    line.Children.Add(name);
                    card = Card(line, 12, Visuals.Row);
                    card.Margin = new(0, 0, 0, 4);
                    nearby.Children.Add(card);
                    note.Text = L10n.T("Searching for devices…");
                });
                timer.Start();
                scan.Start();
            };
        }

        FrameworkElement DeviceRow(string glyph, double glyphOpacity, bool accent, string title, string detail, Button? action)
        {
            var icon = Symbol(glyph, 17, accent ? Accent(P) : Paper);
            icon.Opacity = glyphOpacity;
            icon.Width = 28;
            var name = RawText(title, 14, true, Paper);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var state = RawText(detail, 12, false, Muted);
            var text = Stack(name, state);
            text.VerticalAlignment = VerticalAlignment.Center;
            var row = new DockPanel { Margin = new(8, 7, 8, 7) };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            if (action is not null)
            {
                action.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(action, Dock.Right);
                row.Children.Add(action);
            }
            row.Children.Add(text);
            return Card(row, 12, Visuals.Row);
        }

        async Task Act(WifiNetwork network)
        {
            if (network.Connected) await WifiNetworks.DisconnectAsync();
            else
            {
                note.Text = L10n.T("Connecting…");
                if (!await WifiNetworks.ConnectAsync(network))
                {
                    // пароль или первое подключение: список сетей Windows сам спросит пароль
                    WindowsShell.Launch("ms-availablenetworks:");
                    note.Text = L10n.T("Enter the password in the Windows network list.");
                    return;
                }
            }
            await Task.Delay(1800);
            if (reload is not null) await reload(false);
        }

        void ShowNetworks(List<WifiNetwork> networks)
        {
            list.Children.Clear();
            foreach (var network in networks.Take(14))
            {
                string state = network.Connected ? L10n.T("Connected") : network.Profile is not null ? L10n.T("Saved")
                    : network.Secured ? L10n.T("Secured") : L10n.T("Open network");
                var copy = network;
                var action = Button(Text(network.Connected ? "Disconnect" : "Connect", 12, true), () => _ = Act(copy), Brush("#39393D"), 9);
                action.Padding = new(11, 4, 11, 4);
                System.Windows.Automation.AutomationProperties.SetName(action, (network.Connected ? L10n.T("Disconnect") : L10n.T("Connect")) + " " + network.Ssid);
                var row = DeviceRow("\uE701", .4 + .6 * network.Signal / 100.0, network.Connected, network.Ssid, $"{state} · {network.Signal}%", action);
                row.Margin = new(0, 0, 0, 4);
                list.Children.Add(row);
            }
            if (networks.Count == 0) note.Text = L10n.T("No networks found");
            else if (note.Text == L10n.T("Scanning for networks…") || note.Text == L10n.T("Connecting…")) note.Text = ""; // сообщения о других действиях остаются
        }

        void ShowDevices(List<BluetoothItem> devices)
        {
            list.Children.Clear();
            foreach (var device in devices.Take(14))
            {
                string state = device.Connected ? L10n.T("Connected") : L10n.T("Paired");
                if (device.Battery is { } level) state += $" · {level}%";
                var action = Button(Text("Settings", 12, true), () => WindowsShell.Launch("ms-settings:bluetooth"), Brush("#39393D"), 9);
                action.Padding = new(11, 4, 11, 4);
                System.Windows.Automation.AutomationProperties.SetName(action, L10n.T("Settings") + " " + device.Name);
                var row = DeviceRow("\uE702", device.Connected ? 1 : .55, device.Connected, device.Name, state, action);
                row.Margin = new(0, 0, 0, 4);
                list.Children.Add(row);
            }
            if (devices.Count == 0) note.Text = L10n.T("No paired devices"); // сообщения о поиске и сопряжении остаются
        }

        reload = async scan =>
        {
            if (busy) return;
            busy = true;
            try
            {
                if (wifi)
                {
                    if (!await Task.Run(WifiNetworks.HasAdapter))
                    {
                        list.Children.Clear();
                        note.Text = L10n.T("This computer has no Wi-Fi adapter.");
                        return;
                    }
                    if (scan) note.Text = L10n.T("Scanning for networks…");
                    ShowNetworks(await WifiNetworks.ListAsync(scan));
                }
                else ShowDevices(await BluetoothDevices.ListAsync());
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
            {
                note.Text = L10n.T(wifi ? "Could not read the list of networks." : "Could not read Bluetooth devices.");
            }
            finally { busy = false; }
        };
        return reload;
    }

    private void QuickToggleSurface(Window window, Border shell, string id)
    {
        var panel = Stack();
        panel.Margin = new(12);
        var header = (FrameworkElement)Right(Text(id, 17, true), CloseButton(window));
        header.Margin = new(0, 0, 0, 10);
        panel.Children.Add(header);
        var controls = SettingsGroup(panel);
        var status = Text("", 12, false, Muted);
        status.Margin = new(3, 8, 3, 8);
        status.TextWrapping = TextWrapping.Wrap;
        bool pending = false;
        var radioRows = new List<(string Name, Button Change, TextBlock Label)>();
        foreach (var (name, glyph, address) in Widgets.QuickLinks)
        {
            if (id != "Quick Toggles" && name != id) continue;
            if (name == "Wi-Fi" && id == "Quick Toggles" && !WifiNetworks.HasAdapterCached) continue; // адаптера Wi-Fi нет — строки Wi-Fi нет
            var title = RowStack(Symbol(glyph, 16, Accent(P)), Text("   " + L10n.T(name), 14, true));
            var settings = Button(Text("Settings", 12, true), () => WindowsShell.Launch(address));
            AutomationProperties.SetName(settings, L10n.F($"{L10n.T(name)} settings"));
            if (name is "Wi-Fi" or "Bluetooth")
            {
                var state = Text("", 12, true);
                var toggle = Button(state, () =>
                {
                    var current = model.Radios.Read(name);
                    if (current is RadioController.State.On or RadioController.State.Off)
                        _ = Read(name, current == RadioController.State.Off);
                });
                toggle.Margin = new(0, 0, 5, 0);
                AutomationProperties.SetName(toggle, L10n.F($"{L10n.T(name)} radio"));
                radioRows.Add((name, toggle, state));
                SettingsRow(controls, title, RowStack(toggle, settings));
            }
            else SettingsRow(controls, title, settings);
        }
        // Списки как в быстрых настройках Windows: у Wi-Fi доступные сети, у Bluetooth сопряжённые устройства
        Func<bool, Task>? loadDevices = id is "Wi-Fi" or "Bluetooth" ? AddDevices(panel, id, window) : null;
        panel.Children.Add(status);
        var refresh = Button("Refresh", () => { _ = Read(); if (loadDevices is not null) _ = loadDevices(true); });
        var quickSettings = Button("Windows quick settings", () => WindowsShell.Key(0x5B, 0x41));
        quickSettings.Margin = new(5, 0, 0, 0);
        panel.Children.Add(RowStack(refresh, quickSettings));
        shell.Child = Scroll(panel);
        Paint(false);
        if (!smoke) window.Loaded += (_, _) => _ = Read();
        if (loadDevices is not null) window.Loaded += (_, _) => _ = loadDevices(!smoke);

        async Task Read(string? name = null, bool? enabled = null)
        {
            if (pending) return;
            pending = true;
            Paint(true);
            status.Text = name is null ? L10n.T("Reading Windows radios…") : L10n.F($"Changing {L10n.T(name)}…");
            string? result = await model.Radios.Refresh(name, enabled);
            pending = false;
            if (!window.IsLoaded) return;
            Paint(false);
            status.Text = L10n.T(result ?? "");
        }

        void Paint(bool pending)
        {
            foreach (var (name, change, label) in radioRows)
            {
                var current = model.Radios.Read(name);
                label.Text = L10n.T(current switch
                {
                    RadioController.State.On => "On",
                    RadioController.State.Off => "Off",
                    RadioController.State.Disabled => "Disabled",
                    RadioController.State.Missing => "Unavailable",
                    _ => "—"
                });
                change.IsEnabled = !pending && current is RadioController.State.On or RadioController.State.Off;
                change.Background = current == RadioController.State.On ? Accent(P) : Brush("#39393D");
                change.Opacity = change.IsEnabled ? 1 : .65;
                change.ToolTip = current switch
                {
                    RadioController.State.On => L10n.F($"Turn {L10n.T(name)} off"),
                    RadioController.State.Off => L10n.F($"Turn {L10n.T(name)} on"),
                    _ => L10n.T("Use Windows settings for this device")
                };
            }
        }
    }
}
