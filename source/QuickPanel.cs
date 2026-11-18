using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using static LumaDock.Visuals;

namespace LumaDock;

// Быстрые настройки в виде панели Windows 11: плитки сети (Wi-Fi и Bluetooth переключаются, у них «›» открывает список), громкость и яркость ползунками
internal sealed partial class DockWindow
{
    private void QuickPanel(Window window, Border shell, FrameworkElement? anchor)
    {
        var panel = Stack();
        panel.Margin = new(12);
        var header = (FrameworkElement)Right(Text("Quick Toggles", 17, true), CloseButton(window));
        header.Margin = new(0, 0, 0, 8);
        panel.Children.Add(header);

        var status = Text("", 12, false, Muted);
        status.Margin = new(3, 8, 3, 0);
        status.TextWrapping = TextWrapping.Wrap;
        var neutral = Brush("#2B2B2E");
        var tiles = new UniformGrid { Columns = 2 };
        var radioTiles = new List<(string Name, Border Tile, TextBlock State)>();
        bool pending = false;

        foreach (var (name, glyph, address) in Widgets.QuickLinks)
        {
            if (name == "Wi-Fi" && !WifiNetworks.HasAdapterCached) continue; // адаптера Wi-Fi нет — плитки нет
            bool radio = name is "Wi-Fi" or "Bluetooth";
            var state = Text(radio ? "" : "Settings", 11.5, false, Paper);
            state.Opacity = .75;
            var texts = Stack(Text(name, 13, true), state);
            texts.VerticalAlignment = VerticalAlignment.Center;
            var icon = Symbol(glyph, 18);
            icon.Width = 40;
            var tile = Card(null, 11, neutral);
            tile.Height = 58;
            tile.Margin = new(3);
            var row = new Grid();
            row.ColumnDefinitions.Add(new());
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var main = Button(RowStack(icon, texts), () =>
            {
                var current = model.Radios.Read(name);
                if (radio && current is RadioController.State.On or RadioController.State.Off) _ = Change(name, current == RadioController.State.Off);
                else { window.Close(); WindowsShell.Launch(address); }
            }, Brushes.Transparent, 11);
            main.Padding = new(8, 0, 4, 0);
            main.Height = 58;
            AutomationProperties.SetName(main, L10n.T(name));
            row.Children.Add(main);
            if (radio)
            {
                var more = Button(Symbol("", 12), () => { window.Close(); OpenWidget(name, anchor); }, Brushes.Transparent, 11);
                more.Padding = new(0);
                more.Width = 34;
                more.Height = 58;
                AutomationProperties.SetName(more, L10n.F($"{L10n.T(name)} list"));
                Grid.SetColumn(more, 1);
                row.Children.Add(more);
                radioTiles.Add((name, tile, state));
            }
            tile.Child = row;
            tiles.Children.Add(tile);
        }
        panel.Children.Add(tiles);

        // громкость: кнопка с динамиком выключает звук, ползунок меняет уровень
        bool syncing = false;
        Button speaker = null!;
        Slider volume = null!;
        speaker = Button(Symbol("", 16), () => Sync(WindowsShell.Audio(toggleMute: true)), Brushes.Transparent, 10);
        speaker.Width = speaker.Height = 36;
        speaker.Padding = new(0);
        volume = FilledSlider(Accent(P), 0, 18, "Volume");
        volume.Margin = new(6, 0, 4, 0);
        volume.ValueChanged += (_, _) => { if (!syncing) WindowsShell.Audio(volume.Value / 100); };
        var volumeRow = new DockPanel { Margin = new(3, 12, 3, 0) };
        DockPanel.SetDock(speaker, Dock.Left);
        volumeRow.Children.Add(speaker);
        volumeRow.Children.Add(volume);
        panel.Children.Add(volumeRow);
        Sync(WindowsShell.Audio());
        AutomationProperties.SetName(speaker, L10n.T("Mute"));

        // яркость: строка появляется, только если монитор позволяет её менять
        var sun = Symbol("", 16, Muted);
        sun.Width = 36;
        var brightness = FilledSlider(Accent(P), 0, 18, "Brightness");
        brightness.Margin = new(6, 0, 4, 0);
        var brightnessRow = new DockPanel { Margin = new(3, 6, 3, 0), Visibility = Visibility.Collapsed };
        DockPanel.SetDock(sun, Dock.Left);
        brightnessRow.Children.Add(sun);
        brightnessRow.Children.Add(brightness);
        panel.Children.Add(brightnessRow);

        panel.Children.Add(status);
        var windows = Button("Windows quick settings", () => { window.Close(); WindowsShell.Key(0x5B, 0x41); });
        var all = Button("Settings", () => { window.Close(); WindowsShell.Launch("ms-settings:"); });
        all.Margin = new(5, 0, 0, 0);
        var footer = RowStack(windows, all);
        footer.Margin = new(0, 12, 0, 0);
        panel.Children.Add(footer);
        shell.Child = Scroll(panel);
        Paint();
        if (!smoke) window.Loaded += (_, _) => { _ = Change(null, null); _ = LoadBrightness(); };

        void Sync((double Volume, bool Muted)? audio)
        {
            syncing = true;
            volume.IsEnabled = speaker.IsEnabled = audio is not null;
            volume.Value = 100 * (audio?.Volume ?? 0);
            ((TextBlock)speaker.Content).Text = audio is { Muted: true } ? "" : "";
            syncing = false;
        }

        // name == null — просто обновить состояние радио; иначе включить или выключить указанное
        async Task Change(string? name, bool? enable)
        {
            if (pending) return;
            pending = true;
            Paint();
            status.Text = name is null ? "" : L10n.F($"Changing {L10n.T(name)}…");
            string? result = await model.Radios.Refresh(name, enable);
            pending = false;
            if (!window.IsLoaded) return;
            Paint();
            status.Text = L10n.T(result ?? "");
        }

        void Paint()
        {
            foreach (var (name, tile, state) in radioTiles)
            {
                var current = model.Radios.Read(name);
                state.Text = pending ? "…" : L10n.T(current switch
                {
                    RadioController.State.On => "On",
                    RadioController.State.Off => "Off",
                    RadioController.State.Disabled => "Disabled",
                    RadioController.State.Missing => "Unavailable",
                    _ => "—"
                });
                tile.Background = current == RadioController.State.On ? Accent(P) : neutral;
            }
        }

        async Task LoadBrightness()
        {
            var result = await Task.Run(() => WindowsShell.Brightness(handle));
            if (!window.IsLoaded || !result.Supported) return;
            model.BrightnessLevel = result.Brightness;
            brightness.Value = 100 * result.Brightness;
            brightnessRow.Visibility = Visibility.Visible;
            // изменения применяются с задержкой 250 мс: монитор не успевает за каждым шагом ползунка
            var apply = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            apply.Tick += async (_, _) =>
            {
                apply.Stop();
                double value = brightness.Value / 100;
                var updated = await Task.Run(() => WindowsShell.Brightness(handle, value));
                model.BrightnessLevel = updated.Supported ? updated.Brightness : null;
            };
            window.Closed += (_, _) => apply.Stop();
            brightness.ValueChanged += (_, _) => { apply.Stop(); apply.Start(); };
        }
    }
}
