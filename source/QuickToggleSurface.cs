using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using static LumaDock.Visuals;

namespace LumaDock;

internal sealed partial class DockWindow
{
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
        panel.Children.Add(status);
        var refresh = Button("Refresh", () => _ = Read());
        var quickSettings = Button("Windows quick settings", () => WindowsShell.Key(0x5B, 0x41));
        quickSettings.Margin = new(5, 0, 0, 0);
        panel.Children.Add(RowStack(refresh, quickSettings));
        shell.Child = Scroll(panel);
        Paint(false);
        if (!smoke) window.Loaded += (_, _) => _ = Read();

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
