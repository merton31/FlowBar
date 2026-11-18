using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using static LumaDock.Visuals;

namespace LumaDock;

internal sealed partial class DockWindow
{
    private void WorldClockSettings(DockPanel content, Window window, string id)
    {
        var panel = Stack();
        var spec = Widgets.Available[id];
        var preview = Widgets.Face(spec, model, true, out var update);
        var card = Card(preview, 14, Brush("#121213"), Line);
        card.Padding = new(15, 10, 15, 10);
        card.MinHeight = 61;
        panel.Children.Add(card);
        int slots = id == "World Time" ? 1 : 3;
        for (int index = 0; index < slots; index++)
        {
            int slot = index;
            string slotLabel = slots == 1 ? L10n.T("City") : L10n.F($"Clock {index + 1}");
            var dropdown = ComboField(153, Accent(P), slotLabel);
            foreach (var city in Widgets.ClockCities) dropdown.Items.Add(L10n.T(city.Name));
            dropdown.SelectedIndex = Array.IndexOf(Widgets.ClockCities, Widgets.SelectedCity(P, slots == 1 ? -1 : slot));
            dropdown.SelectionChanged += (_, _) =>
            {
                if (dropdown.SelectedIndex < 0) return;
                string key = Widgets.ClockCities[dropdown.SelectedIndex].Key;
                if (slots == 1) P.WorldTimeCity = key;
                else
                {
                    var cities = Enumerable.Range(0, 3).Select(i => Widgets.SelectedCity(P, i).Key).ToArray();
                    cities[slot] = key;
                    P.WorldClockCities = cities;
                }
                model.Save();
                update();
            };
            AddRow(panel, slotLabel, dropdown);
        }
        content.Children.Add(Scroll(panel));
        Live(window, update);
    }

    internal Window OpenWidgetStyles(string current, FrameworkElement? anchor = null)
        => Surface("Widget style", 520, 396, true, (window, shell) =>
        {
            var heading = Right(Text("Widget style", 17, true), CloseButton(window));
            var variants = new WrapPanel { Margin = new(0, 10, 0, 0) };
            var actions = new List<Action>();
            foreach (string id in Widgets.Styles(current))
            {
                var spec = Widgets.Available[id];
                var preview = Widgets.Face(spec, model, true, out var update);
                actions.Add(update);
                var choose = Button(preview, () =>
                {
                    if (id == current || model.SelectWidgetStyle(current, id)) window.Close();
                }, Brush("#151518"), 13);
                choose.Width = id == "Segment Clock" ? 191 : Math.Clamp(spec.Width * 1.15, 80, 205);
                choose.Height = 66;
                choose.Padding = new(6, 1, 6, 1);
                AutomationProperties.SetName(choose, L10n.T(spec.Name));
                var caption = Text(spec.Name, 11, id == current, id == current ? Accent(P) : Muted);
                caption.HorizontalAlignment = HorizontalAlignment.Center;
                caption.Margin = new(0, 5, 0, 0);
                var option = Stack(Card(choose, 14, Brushes.Transparent, id == current ? Accent(P) : Line), caption);
                option.Margin = new(0, 0, 12, 14);
                variants.Children.Add(option);
            }
            var page = Stack(heading, variants);
            page.Margin = new(12);
            shell.Child = Scroll(page);
            Live(window, () => { foreach (var update in actions) update(); });
        }, anchor);
}
