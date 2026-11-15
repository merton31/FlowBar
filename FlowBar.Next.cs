using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Screen = System.Windows.Forms.Screen;

namespace FlowBar
{
    partial class Bar
    {
        Dictionary<string, string> previousPresetAppearance;

        void UndoPresetAppearance()
        {
            if (previousPresetAppearance == null) return;
            foreach (var key in PresetMaterialKeys) cfg.Remove(key);
            cfg.Remove("appearance.schedule");
            foreach (var kv in previousPresetAppearance) cfg[kv.Key] = kv.Value;
            previousPresetAppearance = null;
            SaveCfg(); RebuildAll(); RestyleWindows();
            if (refreshPreview != null) refreshPreview(null);
        }

        Action undoWidgetReset;
        void ResetWidgetLayouts(Action refresh)
        {
            var targets = bars.Where(b => b.HasWidgets).GroupBy(b => b.LayoutKey).Select(g => g.First()).ToArray();
            if (targets.Length == 0) return;
            string summary = string.Join(", ", bars.Where(b => b.HasWidgets).Select(b => DisplayName(b.mon)));
            if (MessageBox.Show(settings, "Сбросить состав виджетов на экранах " + summary + "?", "Сброс раскладки",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            var previous = targets.Where(b => cfg.ContainsKey(b.LayoutKey)).ToDictionary(b => b.LayoutKey, b => cfg[b.LayoutKey]);
            foreach (var target in targets) cfg.Remove(target.LayoutKey);
            undoWidgetReset = () =>
            {
                foreach (var kv in previous) cfg[kv.Key] = kv.Value;
                undoWidgetReset = null; SaveCfg(); RebuildAll(); refresh();
            };
            SaveCfg(); RebuildAll(); refresh();
        }

        static FrameworkElement FormField(TextBox input, string prompt)
        {
            System.Windows.Automation.AutomationProperties.SetName(input, prompt);
            return VS(Mg(Txt(prompt, 12, true), 0, 8, 0, 0), input);
        }

        static ControlTemplate CompactComboTemplate;
        static void StyleChoice(ComboBox list, Color fg, Color? tint = null)
        {
            list.Foreground = Frozen(fg, 255); list.Background = Frozen(CardBg, 255);
            list.BorderBrush = Frozen(tint ?? fg, 0x50); list.MinHeight = 30;
            if (list.IsEditable) return;
            if (CompactComboTemplate == null) CompactComboTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ComboBox'>" +
                "<Grid><Border x:Name='Edge' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='8'/>" +
                "<ToggleButton Focusable='False' ClickMode='Press' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>" +
                "<ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='Transparent'/></ControlTemplate></ToggleButton.Template></ToggleButton>" +
                "<ContentPresenter Margin='9,0,26,0' VerticalAlignment='Center' IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'/>" +
                "<TextBlock Text='⌄' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,9,2' IsHitTestVisible='False'/>" +
                "<Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False'>" +
                "<Border MinWidth='{TemplateBinding ActualWidth}' MaxHeight='280' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='8' Padding='3'>" +
                "<ScrollViewer><ItemsPresenter/></ScrollViewer></Border></Popup></Grid>" +
                "<ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Edge' Property='BorderThickness' Value='2'/></Trigger>" +
                "<Trigger Property='IsEnabled' Value='False'><Setter TargetName='Edge' Property='Opacity' Value='.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            list.Template = CompactComboTemplate;
            list.ItemContainerStyle = ChoiceItemStyle(fg, tint ?? AccentColor);
        }

        static Style choiceItemStyle;
        static Color choiceForeground, choiceAccent;
        static Style ChoiceItemStyle(Color fg, Color accent)
        {
            if (choiceItemStyle != null && choiceForeground == fg && choiceAccent == accent) return choiceItemStyle;
            choiceForeground = fg; choiceAccent = accent;
            var style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Frozen(fg, 255)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.TemplateProperty, ChoiceItemTemplate.Value));
            var hover = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, Frozen(fg, 0x19)));
            style.Triggers.Add(hover);
            var selected = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, Frozen(accent, 0x32)));
            style.Triggers.Add(selected);
            return choiceItemStyle = style;
        }

        static readonly Lazy<ControlTemplate> ChoiceItemTemplate = new Lazy<ControlTemplate>(() =>
            (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ComboBoxItem'>" +
                "<Border Background='{TemplateBinding Background}' CornerRadius='6' Padding='8,6' Margin='1' MinHeight='27'>" +
                "<ContentPresenter VerticalAlignment='Center'/></Border></ControlTemplate>"));

        Press ThemeGridCard(string key, Action<string> select, Func<string> current, List<Action> refresh)
        {
            var theme = Styles[key];
            var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            for (int index = 0; index < 5; index++)
                line.Children.Add(new Border { Width = 12, Height = 12, Margin = new Thickness(3), CornerRadius = new CornerRadius(index == 3 ? 6 : 3),
                    Background = Frozen(index == 0 || index == 3 ? theme.Ac : theme.Fg, index == 0 || index == 3 ? (byte)255 : (byte)130) });
            var sample = new Border { Child = line, CornerRadius = new CornerRadius(11), Background = Frozen(theme.Bg, 239),
                BorderBrush = Frozen(theme.Fg, 0x44), BorderThickness = new Thickness(1) };
            var scene = new Border { Child = sample, Padding = new Thickness(9, 10, 9, 7), CornerRadius = new CornerRadius(8),
                Background = new LinearGradientBrush(Color.FromRgb(62, 70, 105), Color.FromRgb(175, 121, 99), 22) };
            var name = ThemeName(key);
            var state = Txt("", 11, false);
            var label = Txt(name, 12, true); label.TextWrapping = TextWrapping.Wrap; label.MinHeight = 32;
            var tile = Act(VS(scene, Mg(label, 0, 6, 0, 2), state), name, () => select(key));
            tile.Width = 136; tile.Margin = new Thickness(0, 0, 7, 7); tile.Padding = new Thickness(7);
            tile.CornerRadius = new CornerRadius(9); tile.BorderThickness = new Thickness(1);
            Action paint = () =>
            {
                bool selected = key == current(), applied = key == C("style", "Black");
                tile.Background = Frozen(CardFg, selected ? (byte)0x16 : (byte)0x07);
                tile.BorderBrush = selected ? Accent : Frozen(CardFg, 0x22);
                state.Text = applied ? "Текущая тема" : selected ? "В предпросмотре" : "";
                System.Windows.Automation.AutomationProperties.SetItemStatus(tile, state.Text);
            };
            refresh.Add(paint); paint();
            return tile;
        }

        const uint MinimizeStart = 0x0016, MinimizeEnd = 0x0017;
        delegate void MinimizeCallback(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint threadId, uint time);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, MinimizeCallback callback, uint process, uint thread, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool UnhookWinEvent(IntPtr hook);
        MinimizeCallback minimizeCallback, foregroundCallback;

        void HookForegroundChanges()
        {
            const uint foreground = 0x0003;
            foregroundCallback = (hook, kind, window, objectId, childId, threadId, time) =>
            {
                if (window == IntPtr.Zero || objectId != 0 || childId != 0) return;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (shuttingDown || !bars.Contains(this) || N.GetForegroundWindow() != window) return;
                    All(b =>
                    {
                        b.CancelPreviewOpen();
                        b.CheckPreviewForeground(window);
                        b.Fullscreen();
                    });
                }));
            };
            var eventHook = SetWinEventHook(foreground, foreground, IntPtr.Zero, foregroundCallback, 0, 0, 0);
            Closed += (s, e) => { if (eventHook != IntPtr.Zero) UnhookWinEvent(eventHook); };
        }
        HashSet<IntPtr> previewNormalWindows = new HashSet<IntPtr>();
        IntPtr lastMinimizedWindow;
        DateTime lastMinimizedUntil;
        DispatcherTimer resumePanelLens;

        void HookMinimizeEvents()
        {
            minimizeCallback = (hook, kind, window, objectId, childId, threadId, time) =>
            {
                if (window == IntPtr.Zero || objectId != 0 || childId != 0) return;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (shuttingDown || !bars.Contains(this)) return;
                    if (kind == MinimizeStart) SuppressMinimizedPreview(window);
                    else
                    {
                        All(b => { if (b.lastMinimizedWindow == window) b.lastMinimizedWindow = IntPtr.Zero; });
                        QueueWindowRefresh();
                    }
                }));
            };
            var eventHook = SetWinEventHook(MinimizeStart, MinimizeEnd, IntPtr.Zero, minimizeCallback, 0, 0, 0x2 /* skip own process */);
            Closed += (s, e) => { if (eventHook != IntPtr.Zero) UnhookWinEvent(eventHook); };
        }

        void SuppressMinimizedPreview(IntPtr window)
        {
            var monitor = MonOf(window);
            All(b =>
            {
                if (b.previewNormalWindows.Contains(window)) b.ClosePreview();
                b.lastMinimizedWindow = window;
                b.lastMinimizedUntil = DateTime.UtcNow.AddMilliseconds(650);
                if (b.mon == monitor || monitor == IntPtr.Zero) b.PausePanelRefraction();
            });
            QueueWindowRefresh();
        }

        bool HoverAfterMinimize(List<Win> windows)
        {
            return lastMinimizedWindow != IntPtr.Zero && DateTime.UtcNow < lastMinimizedUntil &&
                windows.Any(w => w.H == lastMinimizedWindow);
        }

        void PausePanelRefraction()
        {
            if (!GlassOn || !PanelLens(MonScale).Enabled || !IsVisible) return;
            if (resumePanelLens == null)
            {
                // DWM can still be drawing the minimize animation. Keep live system blur until
                // it settles, then start a fresh capture with no previous image to present.
                resumePanelLens = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(240) };
                resumePanelLens.Tick += (s, e) => { resumePanelLens.Stop(); UpdateRegion(); };
                Closed += (s, e) => resumePanelLens.Stop();
            }
            resumePanelLens.Stop();
            resumePanelLens.Start();
            UpdateRegion();
        }

        LensSettings PanelCaptureLens(double scale)
        {
            return resumePanelLens != null && resumePanelLens.IsEnabled ? default(LensSettings) : PanelLens(scale);
        }

        bool selectingThumbnail;
        Action<IntPtr> animateThumbnailChoice;
        bool previewEscapeDown;
        DispatcherTimer thumbnailSelection;
        Window placementOutline;

        void PreviewModeSettings(StackPanel c)
        {
            var values = new[] { "instant", "delay", "click" };
            c.Children.Add(SetRow("Открывать миниатюры", "Закреплённый предпросмотр закрывается по Esc, повторному клику или выбору окна.",
                Seg(new[] { "Сразу", "С задержкой", "По клику" }, Math.Max(0, Array.IndexOf(values, C("preview.open", "delay"))),
                    i => { cfg["preview.open"] = values[i]; SaveCfg(); })));
        }

        static IEnumerable<FrameworkElement> NextVisuals(DependencyObject root)
        {
            if (root is FrameworkElement) yield return (FrameworkElement)root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in NextVisuals(VisualTreeHelper.GetChild(root, i))) yield return child;
        }

        void ChooseThumbnail(IntPtr h)
        {
            if (selectingThumbnail) return;
            var w = preview;
            if (!Motion || w == null) { ClosePreview(); if (N.IsWindow(h)) ActivateWin(h); return; }
            selectingThumbnail = true;
            if (animateThumbnailChoice != null) animateThumbnailChoice(h);
            foreach (var cell in NextVisuals(w).OfType<Press>().Where(p => p.Tag is IntPtr))
            {
                if ((IntPtr)cell.Tag == h)
                {
                    cell.BorderBrush = Accent;
                    cell.BorderThickness = new Thickness(2);
                    Panel.SetZIndex(cell, 2);
                    var lift = cell.RenderTransform as TransformGroup;
                    var shift = lift == null ? null : lift.Children.OfType<TranslateTransform>().LastOrDefault();
                    if (shift == null) { shift = new TranslateTransform(); cell.RenderTransform = shift; }
                    shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(shift.Y, shift.Y + (IsTop ? 6 : -6), TimeSpan.FromMilliseconds(135)));
                }
                else if (animateThumbnailChoice == null) cell.BeginAnimation(OpacityProperty, new DoubleAnimation(cell.Opacity, 0, TimeSpan.FromMilliseconds(115)));
            }
            thumbnailSelection = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(145) };
            thumbnailSelection.Tick += (s, e) =>
            {
                thumbnailSelection.Stop();
                if (preview != w) return;
                ClosePreview();
                if (N.IsWindow(h)) ActivateWin(h);
            };
            thumbnailSelection.Start();
        }

        static readonly string[] ZoneNames = { "Левая половина", "Правая половина", "Левая треть", "Средняя треть", "Правая треть",
            "Слева сверху", "Справа сверху", "Слева снизу", "Справа снизу", "По центру широко" };
        static readonly int[,] WindowZones = { { 0, 0, 6, 6 }, { 6, 0, 12, 6 }, { 0, 0, 4, 6 }, { 4, 0, 8, 6 },
            { 8, 0, 12, 6 }, { 0, 0, 6, 3 }, { 6, 0, 12, 3 }, { 0, 3, 6, 6 }, { 6, 3, 12, 6 }, { 1, 0, 11, 6 } };

        internal static N.RECT ZoneBounds(N.RECT work, int zone)
        {
            if (zone < 0 || zone >= ZoneNames.Length) return work;
            int width = work.R - work.L, height = work.B - work.T;
            return new N.RECT { L = work.L + width * WindowZones[zone, 0] / 12, R = work.L + width * WindowZones[zone, 2] / 12,
                T = work.T + height * WindowZones[zone, 1] / 6, B = work.T + height * WindowZones[zone, 3] / 6 };
        }

        void HidePlacementOutline()
        {
            if (placementOutline == null) return;
            placementOutline.Close(); placementOutline = null;
        }

        void ShowPlacementOutline(IntPtr h, int zone)
        {
            HidePlacementOutline();
            if (!N.IsWindow(h)) return;
            var rect = ZoneBounds(WorkOf(MonOf(h)), zone);
            if (rect.R - rect.L < 10 || rect.B - rect.T < 10) return;
            var w = new Window { ShowActivated = false, ShowInTaskbar = false, AllowsTransparency = true, Topmost = true,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = Brushes.Transparent, IsHitTestVisible = false,
                Content = new Border { BorderBrush = Accent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12), Background = Frozen(AccentColor, 26) } };
            w.SourceInitialized += (s, e) =>
            {
                var handle = new WindowInteropHelper(w).Handle;
                N.SetWindowLongPtr(handle, -20, new IntPtr(N.GetWindowLongPtr(handle, -20).ToInt64() | 0x080000A0));
                N.SetWindowPos(handle, IntPtr.Zero, rect.L + 3, rect.T + 3, rect.R - rect.L - 6, rect.B - rect.T - 6, 0x14);
            };
            placementOutline = w; w.Show();
        }

        void AddZone(ItemsControl parent, IntPtr h, int index)
        {
            var item = Item(ZoneNames[index], () => { HidePlacementOutline(); TileWindow(h, index); });
            if (index >= 2)
            {
                var tile = new Canvas { Width = 23, Height = 16, Background = Frozen(CardFg, 0x21), IsHitTestVisible = false };
                var zone = ZoneBounds(new N.RECT { R = 23, B = 16 }, index);
                var area = new Border { Width = zone.R - zone.L, Height = zone.B - zone.T,
                    CornerRadius = new CornerRadius(2), Background = Accent };
                Canvas.SetLeft(area, zone.L); Canvas.SetTop(area, zone.T);
                tile.Children.Add(area); item.Icon = tile;
            }
            item.MouseEnter += (s, e) => ShowPlacementOutline(h, index);
            item.MouseLeave += (s, e) => HidePlacementOutline();
            item.IsVisibleChanged += (s, e) => { if (!item.IsVisible) HidePlacementOutline(); };
            parent.Items.Add(item);
        }

        static readonly string[] HotkeyNames = { "Задачи", "Фокус панели", "Быстрый поиск", "Уведомления FlowBar", "Правка панели", "Сменить сценарий" };
        static readonly string[] HotkeyDefaults = { "1,84", "3,66", "3,32", "", "", "" };
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
        readonly HashSet<int> activeHotkeys = new HashSet<int>();

        void ApplyHotkeys()
        {
            if (DemoMode || !primary || hwnd == IntPtr.Zero) return;
            foreach (var id in activeHotkeys) UnregisterHotKey(hwnd, id);
            activeHotkeys.Clear();
            for (int i = 0; i < HotkeyNames.Length; i++)
            {
                var parts = C("hotkey." + i, HotkeyDefaults[i]).Split(',');
                uint mods, key;
                if (parts.Length == 2 && uint.TryParse(parts[0], out mods) && uint.TryParse(parts[1], out key) &&
                    N.RegisterHotKey(hwnd, i + 1, mods | 0x4000, key)) activeHotkeys.Add(i + 1);
            }
        }

        bool DispatchHotkey(int id)
        {
            switch (id)
            {
                case 1: ShowTodos(); return true;
                case 2: FocusBar(); return true;
                case 3: QuickSearchFromHotkey(); return true;
                case 4: ShowNotificationCenter(); return true;
                case 5: SetDirectEdit(!directEdit); return true;
                case 6:
                    int previous;
                    if (!int.TryParse(C("scenario.active", ""), out previous)) previous = -1;
                    ApplyScenario(previous >= 2 ? -1 : previous + 1);
                    return true;
                default: return false;
            }
        }

        static string HotkeyLabel(string value)
        {
            var parts = value.Split(',');
            int mods, key;
            if (parts.Length != 2 || !int.TryParse(parts[0], out mods) || !int.TryParse(parts[1], out key)) return "Задать";
            return ((mods & 2) != 0 ? "Ctrl+" : "") + ((mods & 1) != 0 ? "Alt+" : "") +
                ((mods & 4) != 0 ? "Shift+" : "") + ((mods & 8) != 0 ? "Win+" : "") + (key == 32 ? "Пробел" : KeyInterop.KeyFromVirtualKey(key).ToString());
        }

        void HotkeySettings(StackPanel c)
        {
            var host = new StackPanel();
            for (int i = 0; i < HotkeyNames.Length; i++)
            {
                int index = i;
                var status = SHint("");
                var row = new TextBox { IsReadOnly = true, MinWidth = 155, Text = HotkeyLabel(C("hotkey." + i, HotkeyDefaults[i])),
                    ToolTip = "Нажмите сочетание с Ctrl, Alt или Win. Backspace сбрасывает назначение." };
                System.Windows.Automation.AutomationProperties.SetName(row, HotkeyNames[i]);
                row.PreviewKeyDown += (s, e) =>
                {
                    var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
                    var mods = Keyboard.Modifiers;
                    int vk = KeyInterop.VirtualKeyFromKey(key);
                    if (key == System.Windows.Input.Key.Escape) return;
                    e.Handled = true;
                    if (key != System.Windows.Input.Key.Back && ((mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0 ||
                        vk <= 0 || vk == 16 || vk == 17 || vk == 18 || vk == 91 || vk == 92 || vk >= 160 && vk <= 165)) return;
                    var value = key == System.Windows.Input.Key.Back && mods == 0 ? HotkeyDefaults[index] : (int)mods + "," + vk;
                    int clash = Enumerable.Range(0, HotkeyNames.Length).Where(n => n != index && value != "" && C("hotkey." + n, HotkeyDefaults[n]) == value).DefaultIfEmpty(-1).First();
                    if (clash >= 0) { status.Text = "Уже назначено на «" + HotkeyNames[clash] + "»."; return; }
                    string configKey = "hotkey." + index, old = C(configKey, HotkeyDefaults[index]);
                    cfg[configKey] = value;
                    var target = Primary ?? this;
                    target.ApplyHotkeys();
                    if (!DemoMode && value != "" && !target.activeHotkeys.Contains(index + 1))
                    { cfg[configKey] = old; target.ApplyHotkeys(); status.Text = "Сочетание занято Windows или другой программой."; return; }
                    SaveCfg(); row.Text = HotkeyLabel(value); status.Text = "Сохранено.";
                };
                host.Children.Add(SetRow(HotkeyNames[i], null, VS(row, status)));
            }
            c.Children.Add(new Expander { Header = "Горячие клавиши", Content = host });
        }

        static int autoScenario = -1;
        static int autoScenarioCandidate = -2;
        static int autoScenarioVotes;

        void SyncScenarioRules()
        {
            if (C("scenario.manual", "0") == "1" || C("scenario.rules", "0") == "0") return;
            var fg = N.GetForegroundWindow();
            uint pid; N.GetWindowThreadProcessId(fg, out pid);
            if (pid == selfPid || fg == IntPtr.Zero) return;
            int desired = C("scenario.fullscreen", "1") == "1" && bars.Any(b => b.fsHidden) ? 1 :
                C("scenario.laptop", "1") == "1" && live.HasBat && bars.Count == 1 ? 2 : -1;
            if (desired != autoScenarioCandidate) { autoScenarioCandidate = desired; autoScenarioVotes = 0; }
            if (autoScenarioVotes < 3) autoScenarioVotes++;
            if (autoScenarioVotes < 3 || desired >= 0 && desired == autoScenario) return;
            if (desired >= 0 || cfg.ContainsKey("scenario.automatic")) ApplyScenarioCore(desired, true);
        }

        void ScenarioRuleSettings(StackPanel content, Action again)
        {
            content.Children.Add(SwitchRow("Автовыбор сценария", "Включается после устойчивого изменения режима. Ручной выбор приостанавливает правила.",
                C("scenario.rules", "0") == "1", on =>
                {
                    cfg["scenario.rules"] = on ? "1" : "0";
                    if (!on && cfg.ContainsKey("scenario.automatic")) ApplyScenarioCore(-1, true);
                    cfg.Remove("scenario.manual"); autoScenarioVotes = 0; autoScenarioCandidate = -2; SaveCfg(); again();
                }));
            if (C("scenario.rules", "0") != "1") return;
            content.Children.Add(SwitchRow("Полноэкранное приложение → Игра", null, C("scenario.fullscreen", "1") == "1",
                on => { cfg["scenario.fullscreen"] = on ? "1" : "0"; SaveCfg(); }));
            content.Children.Add(SwitchRow("Один экран на ноутбуке → Ноутбук", null, C("scenario.laptop", "1") == "1",
                on => { cfg["scenario.laptop"] = on ? "1" : "0"; SaveCfg(); }));
            if (C("scenario.manual", "0") == "1")
                content.Children.Add(Btn("Снова применять правила", () => { if (cfg.ContainsKey("scenario.previous")) ApplyScenarioCore(-1, true); cfg.Remove("scenario.manual"); SaveCfg(); again(); }));
        }

        static bool NoticeInCenterOnly(string source, DateTime now)
        {
            var rule = C("notice.source." + source, "");
            if (rule == "center") return true;
            long until;
            return long.TryParse(rule, out until) && until > now.Ticks;
        }

        void NoticeSourceSettings(StackPanel content)
        {
            var items = new StackPanel();
            foreach (var key in notices.Select(n => NoticeKey(n.Toast)).Concat(new[] { "flowbar", "timer", "event", "claude", "codex" }).Distinct().OrderBy(NoticeSource))
            {
                var config = "notice.source." + key;
                int selected = C(config, "") == "center" ? 1 : NoticeInCenterOnly(key, DateTime.UtcNow) ? 2 : 0;
                items.Children.Add(SetRow(NoticeSource(key), null, Seg(new[] { "Показывать", "Только в центре", "На час в центр" }, selected, i =>
                {
                    if (i == 0) cfg.Remove(config);
                    else cfg[config] = i == 1 ? "center" : DateTime.UtcNow.AddHours(1).Ticks.ToString();
                    SaveCfg();
                })));
            }
            content.Children.Add(new Expander { Header = "Уведомления по источникам", Content = items });
        }

        sealed class WorkspaceWindow
        {
            public string Path { get; set; }
            public string Title { get; set; }
            public string Folder { get; set; }
            public int Screen { get; set; }
            public double[] Bounds { get; set; }
        }

        static Dictionary<IntPtr, string> OpenExplorerFolders()
        {
            var result = new Dictionary<IntPtr, string>();
            const System.Reflection.BindingFlags get = System.Reflection.BindingFlags.GetProperty;
            const System.Reflection.BindingFlags call = System.Reflection.BindingFlags.InvokeMethod;
            object shell = null, windows = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                windows = shell.GetType().InvokeMember("Windows", call, null, shell, null);
                int count = Convert.ToInt32(windows.GetType().InvokeMember("Count", get, null, windows, null));
                for (int i = 0; i < Math.Min(count, 40); i++)
                {
                    object window = null;
                    try
                    {
                        window = windows.GetType().InvokeMember("Item", call, null, windows, new object[] { i });
                        if (window == null) continue;
                        var type = window.GetType();
                        var handle = new IntPtr(Convert.ToInt64(type.InvokeMember("HWND", get, null, window, null)));
                        var address = type.InvokeMember("LocationURL", get, null, window, null) as string;
                        Uri uri;
                        if (Uri.TryCreate(address, UriKind.Absolute, out uri) && uri.IsFile && !uri.IsUnc)
                            result[handle] = uri.LocalPath;
                    }
                    catch (Exception) { }
                    finally { ReleaseWorkspaceCom(window); }
                }
            }
            catch (Exception) { }
            finally { ReleaseWorkspaceCom(windows); ReleaseWorkspaceCom(shell); }
            return result;
        }

        static void ReleaseWorkspaceCom(object value)
        {
            if (value != null && System.Runtime.InteropServices.Marshal.IsComObject(value))
                System.Runtime.InteropServices.Marshal.ReleaseComObject(value);
        }

        void WorkspaceSettings(StackPanel content, Action again)
        {
            var name = new TextBox { MaxLength = 40, MinWidth = 140, Text = "Проект" };
            var status = SHint("");
            var list = new StackPanel();
            Action fill = () =>
            {
                list.Children.Clear();
                foreach (var k in cfg.Keys.Where(k => k.StartsWith("workspace.")).OrderBy(k => k).ToArray())
                {
                    var title = k.Substring("workspace.".Length);
                    list.Children.Add(CardActions(Btn(title, () => RestoreWorkspace(k, status)), Cross("Удалить «" + title + "»", () => { cfg.Remove(k); SaveCfg(); again(); })));
                }
            };
            var capture = Btn("Сохранить открытые окна", () =>
            {
                string title = name.Text.Trim();
                if (title.Length == 0 || title.Contains("=") || title.Any(char.IsControl)) { status.Text = "Введите название без знака «=»."; return; }
                GetWindows();
                var records = new List<WorkspaceWindow>();
                var folders = OpenExplorerFolders();
                foreach (var win in allWins.Where(w => w.Pid != selfPid && w.Path != null && !N.IsIconic(w.H)).Take(20))
                {
                    var work = WorkOf(win.Mon); N.RECT r;
                    if (work.R <= work.L || work.B <= work.T || !N.GetWindowRect(win.H, out r)) continue;
                    string folder;
                    folders.TryGetValue(win.H, out folder);
                    records.Add(new WorkspaceWindow { Path = win.Path, Title = TitleOf(win.H), Folder = folder, Screen = DisplayNumber(win.Mon),
                        Bounds = new[] { (r.L - work.L) / (double)(work.R - work.L), (r.T - work.T) / (double)(work.B - work.T),
                            (r.R - work.L) / (double)(work.R - work.L), (r.B - work.T) / (double)(work.B - work.T) } });
                }
                if (records.Count == 0) { status.Text = "Откройте окна, которые хотите сохранить."; return; }
                cfg["workspace." + title] = new JavaScriptSerializer().Serialize(records); SaveCfg(); fill();
                status.Text = "Сохранено окон: " + records.Count + ".";
            }, true);
            fill();
            content.Children.Add(new Expander { Header = "Рабочие наборы окон", Content = VS(SHint("Запоминает приложения и их положение. Для уже открытых окон используется заголовок; закрытые приложения запускаются."), name, CardActions(capture), list, status) });
        }

        void RestoreWorkspace(string key, TextBlock status)
        {
            List<WorkspaceWindow> entries;
            try { entries = new JavaScriptSerializer().Deserialize<List<WorkspaceWindow>>(C(key, "")); }
            catch (Exception ex) { Log(ex); status.Text = "Не удалось открыть набор."; return; }
            if (entries == null) return;
            var waiting = entries.Where(w => w != null && w.Path != null && w.Bounds != null && w.Bounds.Length == 4 &&
                w.Bounds.All(v => !double.IsNaN(v) && Math.Abs(v) <= 4)).Take(20).ToList();
            var used = new HashSet<IntPtr>();
            var launched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int rounds = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            Action place = () =>
            {
                GetWindows();
                var folders = waiting.Any(w => !string.IsNullOrEmpty(w.Folder)) ? OpenExplorerFolders() : null;
                foreach (var entry in waiting.ToArray())
                {
                    var candidates = allWins.Where(w => w.Pid != selfPid && !used.Contains(w.H) && Key(w.Path) == Key(entry.Path)).ToList();
                    var match = !string.IsNullOrEmpty(entry.Folder)
                        ? candidates.FirstOrDefault(w => { string folder; return folders.TryGetValue(w.H, out folder) && string.Equals(folder, entry.Folder, StringComparison.OrdinalIgnoreCase); })
                        : candidates.FirstOrDefault(w => TitleOf(w.H) == entry.Title) ?? candidates.FirstOrDefault();
                    if (match == null)
                    {
                        if (!DemoMode && !string.IsNullOrEmpty(entry.Folder) && launched.Add(entry.Folder) && Directory.Exists(entry.Folder))
                            Launch(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "/n,\"" + entry.Folder + "\"");
                        else if (!DemoMode && string.IsNullOrEmpty(entry.Folder) && launched.Add(entry.Path) && (entry.Path.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase) ||
                            Path.IsPathRooted(entry.Path) && File.Exists(entry.Path) && Path.GetExtension(entry.Path).Equals(".exe", StringComparison.OrdinalIgnoreCase)))
                            Launch(entry.Path);
                        continue;
                    }
                    used.Add(match.H);
                    var monitor = Screen.AllScreens.Select(MonitorId).FirstOrDefault(m => DisplayNumber(m) == entry.Screen);
                    var work = WorkOf(monitor == IntPtr.Zero ? MonOf(match.H) : monitor);
                    if (N.IsIconic(match.H) || N.IsZoomed(match.H)) N.ShowWindow(match.H, 9);
                    int width = work.R - work.L, height = work.B - work.T;
                    if (width > 0 && height > 0)
                    {
                        int left = Math.Max(0, Math.Min(width - 100, (int)(entry.Bounds[0] * width)));
                        int top = Math.Max(0, Math.Min(height - 80, (int)(entry.Bounds[1] * height)));
                        int w = Math.Max(100, Math.Min(width - left, (int)((entry.Bounds[2] - entry.Bounds[0]) * width)));
                        int h = Math.Max(80, Math.Min(height - top, (int)((entry.Bounds[3] - entry.Bounds[1]) * height)));
                        N.SetWindowPos(match.H, IntPtr.Zero, work.L + left, work.T + top, w, h, 0x14);
                    }
                    waiting.Remove(entry);
                }
                if (waiting.Count == 0 || ++rounds >= 14)
                {
                    timer.Stop();
                    status.Text = waiting.Count == 0 ? "Окна размещены." : "Осталось окон: " + waiting.Count + ". Откройте их и повторите.";
                }
            };
            timer.Tick += (s, e) => place();
            status.Text = "Размещаем окна…"; timer.Start(); place();
        }

        sealed class TransferSettings
        {
            public int Version { get; set; }
            public Dictionary<string, string> Values { get; set; }
            public string[] Monitors { get; set; }
        }

        static bool TransferKey(string k)
        {
            var name = k.Split('@')[0];
            if (name.StartsWith("hotkey."))
            {
                int index;
                return int.TryParse(name.Substring(7), out index) && index >= 0 && index < HotkeyNames.Length && k == name;
            }
            return IsAppearanceKey(k) || name == "preset" || name == "preset.custom" || name.StartsWith("cpreset.") ||
                name == "autohide" || name == "notify" || name == "motion" || name == "preview.open" ||
                name == "scenario.rules" || name == "scenario.laptop" || name == "scenario.fullscreen" || name == "scenario.previous" ||
                name == "scenario.active" || name == "scenario.manual" || name == "scenario.automatic" || name.StartsWith("notice.source.") ||
                name == "secorder" || name == "secmap" || name == "sec1" || name == "sec2";
        }

        void TransferTools(StackPanel content)
        {
            var status = SHint("");
            var buttons = CardActions(Btn("Экспорт настроек", () =>
            {
                var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Настройки FlowBar|*.flowbar-settings", FileName = "FlowBar.flowbar-settings" };
                if (dialog.ShowDialog(settings) != true) return;
                try
                {
                    var file = new TransferSettings { Version = 1, Monitors = Screen.AllScreens.OrderByDescending(s => s.Primary).Select(s => s.DeviceName).ToArray(),
                        Values = cfg.Where(kv => TransferKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value) };
                    File.WriteAllText(dialog.FileName, new JavaScriptSerializer().Serialize(file)); status.Text = "Файл сохранён.";
                }
                catch (Exception ex) { Log(ex); status.Text = "Не удалось сохранить файл."; }
            }), Btn("Импорт настроек", () =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Настройки FlowBar|*.flowbar-settings" };
                if (dialog.ShowDialog(settings) != true) return;
                try
                {
                    if (new FileInfo(dialog.FileName).Length > 524288) throw new InvalidDataException();
                    var file = new JavaScriptSerializer().Deserialize<TransferSettings>(File.ReadAllText(dialog.FileName));
                    if (file == null || file.Version != 1 || file.Values == null) throw new InvalidDataException();
                    var accepted = file.Values.Where(kv => kv.Key.Length <= 180 && TransferKey(kv.Key) && kv.Value != null &&
                        kv.Key.IndexOfAny(new[] { '=', '\r', '\n' }) < 0 && kv.Value.IndexOfAny(new[] { '\r', '\n' }) < 0).ToArray();
                    ReviewSettingsImport("Файл настроек", accepted, file.Monitors, file.Values.Count - accepted.Length, status);
                }
                catch (Exception ex) { Log(ex); status.Text = "Файл настроек не удалось прочитать."; }
            }));
            var codeButtons = CardActions(Btn("Скопировать код", () =>
            {
                try { Clipboard.SetText(MakeShareCode(cfg)); status.Text = "Код скопирован."; }
                catch (Exception ex) { Log(ex); status.Text = "Не удалось скопировать код."; }
            }), Btn("Проверить код из буфера", () =>
            {
                try
                {
                    int skipped; string error;
                    var values = ReadShareCode(Clipboard.GetText(), out skipped, out error);
                    if (values == null) { status.Text = error; return; }
                    ReviewSettingsImport("Код оформления", values, null, skipped, status);
                }
                catch (Exception ex) { Log(ex); status.Text = "Не удалось прочитать код из буфера."; }
            }));
            status.Text = transferFeedback ?? "";
            transferFeedback = null;
            content.Children.Add(SetRow("Файл настроек", "Оформление, раскладка, сценарии и клавиши.", buttons));
            content.Children.Add(SetRow("Код оформления", "Тема, цвета и раскладка. Код можно отправить текстом.", codeButtons));
            content.Children.Add(status);
        }

        string transferFeedback;

        static string ImportScreen(string key)
        {
            int at = key.LastIndexOf('@');
            if (at >= 0) return key.Substring(at + 1);
            const string compact = "mon.compact.";
            if (key.StartsWith(compact, StringComparison.Ordinal)) return key.Substring(compact.Length);
            return key.StartsWith("mon.", StringComparison.Ordinal) ? key.Substring(4) : null;
        }

        internal static Dictionary<string, string> ImportChanges(IEnumerable<KeyValuePair<string, string>> entries,
            IDictionary<string, string> screens, IDictionary<string, string> current, out int skipped, out bool conflict)
        {
            var changes = new Dictionary<string, string>();
            var seen = new HashSet<string>();
            skipped = 0; conflict = false;
            foreach (var pair in entries)
            {
                string key = pair.Key, old = ImportScreen(key);
                if (old != null)
                {
                    string screen;
                    if (!screens.TryGetValue(old, out screen) || string.IsNullOrEmpty(screen)) { skipped++; continue; }
                    key = key.Substring(0, key.Length - old.Length) + screen;
                }
                if (!seen.Add(key)) { conflict = true; continue; }
                string previous;
                if (!current.TryGetValue(key, out previous) || pair.Value != previous) changes.Add(key, pair.Value);
            }
            return changes;
        }

        static string ImportLabel(string key)
        {
            string name = key.Split('@')[0];
            if (name.StartsWith("mon.")) return "Настройки монитора";
            if (name.StartsWith("view.") || name.StartsWith("keep."))
            {
                var id = name.Substring(5);
                var def = Def(id);
                return (name.StartsWith("view.") ? "Размер · " : "Видимость · ") +
                    (id == "clock" ? "Часы" : def != null ? def[1] : "виджет");
            }
            if (name.StartsWith("material.")) return "Стекло и материал";
            if (name.StartsWith("scenario.")) return "Сценарии";
            if (name.StartsWith("hotkey.")) return "Горячие клавиши";
            if (name.StartsWith("notice.source.") || name == "notify") return "Уведомления";
            if (name.StartsWith("sec") || name == "bar.sections") return "Секции панели";
            if (name.StartsWith("preset") || name.StartsWith("cpreset.")) return "Пресеты";
            switch (name)
            {
                case "style": return "Тема";
                case "opacity": return "Прозрачность";
                case "accent": return "Цвет акцента";
                case "pos": case "align": case "gap": return "Расположение панели";
                case "width": case "size": return "Размер панели";
                case "seq": return "Порядок значков и виджетов";
                case "widgets": return "Состав виджетов";
                case "motion": case "sliders": case "menus": return "Анимации и элементы управления";
                default: return "Другие параметры панели";
            }
        }

        void ReviewSettingsImport(string source, IEnumerable<KeyValuePair<string, string>> entries, string[] fileMonitors,
            int rejected, TextBlock status)
        {
            var incoming = entries.ToArray();
            var screens = Screen.AllScreens.OrderByDescending(s => s.Primary).ToArray();
            var devices = screens.Select(s => s.DeviceName).ToArray();
            var options = new[] { "Пропустить" }.Concat(screens.Select(s => DisplayName(MonitorId(s)) + (s.Primary ? " · основной" : ""))).ToArray();
            var oldScreens = incoming.Select(x => ImportScreen(x.Key)).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToArray();
            if (fileMonitors != null)
                oldScreens = fileMonitors.Where(oldScreens.Contains).Concat(oldScreens).Distinct().ToArray();
            var mapping = new Dictionary<string, ComboBox>();
            var win = AppWindow("Проверка импорта", 580, 540);
            win.MinWidth = 430; win.MinHeight = 360;
            win.Owner = settings; win.WindowStartupLocation = WindowStartupLocation.CenterOwner; win.ShowInTaskbar = false;
            var summary = SHint("");
            var changesList = new StackPanel();
            var body = VS(SH("Проверка импорта"), SHint(source));
            if (oldScreens.Length > 0)
            {
                body.Children.Add(SSub("Сопоставьте мониторы"));
                foreach (var old in oldScreens.Take(16))
                {
                    var combo = new ComboBox { ItemsSource = options, SelectedIndex = Math.Max(0, Array.IndexOf(devices, old) + 1) };
                    System.Windows.Automation.AutomationProperties.SetName(combo, "Импорт для монитора " + old);
                    var number = new string(old.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
                    mapping.Add(old, combo);
                    body.Children.Add(SetRow(number.Length > 0 ? "Исходный экран " + number : old, null, combo));
                }
            }
            body.Children.Add(SSub("Изменения")); body.Children.Add(summary); body.Children.Add(changesList);
            bool applied = false;
            var changes = new Dictionary<string, string>();
            bool canApply = false;
            var apply = Btn("Применить", () => { if (canApply) { applied = true; win.Close(); } }, true);
            Action preview = () =>
            {
                var selected = mapping.ToDictionary(x => x.Key, x => x.Value.SelectedIndex > 0 ? devices[x.Value.SelectedIndex - 1] : null);
                int skipped; bool conflict;
                changes = ImportChanges(incoming, selected, cfg, out skipped, out conflict);
                conflict |= selected.Values.Where(x => x != null).GroupBy(x => x).Any(g => g.Count() > 1);
                summary.Text = conflict ? "Выберите отдельный экран для каждого исходного монитора." :
                    (changes.Count == 0 ? "Нет изменений, которые можно применить." : "Будет изменено параметров: " + changes.Count + ".") +
                    (rejected + skipped > 0 ? " Пропущено: " + (rejected + skipped) + "." : "");
                canApply = !conflict && changes.Count > 0;
                apply.IsEnabled = canApply;
                apply.Opacity = apply.IsEnabled ? 1 : 0.55;
                changesList.Children.Clear();
                foreach (var group in changes.GroupBy(x => ImportLabel(x.Key)).OrderBy(g => g.Key))
                {
                    var pair = group.First();
                    string detail = group.Count() > 1 ? " · " + group.Count() + " параметров" : "";
                    if (group.Count() == 1 && (pair.Key == "style" || pair.Key == "accent" || pair.Key == "opacity"))
                    {
                        var old = C(pair.Key, "По умолчанию");
                        if (old.Length <= 32 && pair.Value.Length <= 32) detail = " · " + old + " → " + (pair.Value == "" ? "По умолчанию" : pair.Value);
                    }
                    changesList.Children.Add(CardStatus(group.Key + detail));
                }
            };
            foreach (var combo in mapping.Values) combo.SelectionChanged += (s, e) => preview();
            preview();
            var dock = new DockPanel { Margin = new Thickness(18) };
            var actions = CardActions(apply, Btn("Отмена", () => win.Close()));
            DockPanel.SetDock(actions, Dock.Bottom); dock.Children.Add(actions);
            dock.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            StyleInputs(dock, CardFg);
            win.Content = dock; win.ShowDialog();
            if (!applied) { status.Text = "Импорт отменён."; return; }
            foreach (var pair in changes) cfg[pair.Key] = pair.Value;
            transferFeedback = "Применено параметров: " + changes.Count + ".";
            status.Text = transferFeedback;
            SaveCfg(); ClosePanels();
            All(b => { b.LoadSeq(); b.reserveKey = null; });
            RebuildAll(); All(b => b.Place()); (Primary ?? this).ApplyHotkeys();
            RestyleWindows();
        }

        static readonly string[] GlassPerformanceKeys = { "quality", "balanced", "economy" };
        static readonly string[] GlassPerformanceNames = { "Качество", "Баланс", "Экономия" };
        internal static int GlassFrequency(int native)
        {
            switch (C("material.performance", "quality"))
            {
                case "balanced": return Math.Min(native, 120);
                case "economy": return Math.Min(native, 60);
                default: return native;
            }
        }

        internal static int GlassBlurLimit(int blur)
        {
            switch (C("material.performance", "quality"))
            {
                case "balanced": return Math.Min(blur, 8);
                case "economy": return Math.Min(blur, 4);
                default: return blur;
            }
        }

        void GlassPerformanceSettings(StackPanel c, Action again)
        {
            c.Children.Add(SetRow("Производительность стекла", "Ограничивает частоту и сложность преломления. Текст и значки рисуются отдельно.",
                Seg(GlassPerformanceNames, Math.Max(0, Array.IndexOf(GlassPerformanceKeys, C("material.performance", "quality"))), i =>
                { cfg["material.performance"] = GlassPerformanceKeys[i]; SaveCfg(); RebuildAll(); again(); })));
        }

        static long performanceUntil, glassSeen, glassShown, glassSkipped, glassDelayTotal, glassDelayCount, glassGpuBytes;
        static string performanceResult;
        static bool measuringPerformance;
        internal static bool MeasuringGlass { get { return Stopwatch.GetTimestamp() < Interlocked.Read(ref performanceUntil); } }

        internal static void MeasureGlassFrame(bool presented, long sourceStamp, int skipped, long gpuBytes, bool capture)
        {
            if (!MeasuringGlass) return;
            if (capture) Interlocked.Increment(ref glassSeen);
            if (skipped > 0) Interlocked.Add(ref glassSkipped, skipped);
            if (presented)
            {
                Interlocked.Increment(ref glassShown);
                if (sourceStamp > 0)
                {
                    Interlocked.Add(ref glassDelayTotal, Stopwatch.GetTimestamp() - sourceStamp);
                    Interlocked.Increment(ref glassDelayCount);
                }
            }
            if (gpuBytes > 0)
            {
                long current;
                do { current = Interlocked.Read(ref glassGpuBytes); }
                while (gpuBytes > current && Interlocked.CompareExchange(ref glassGpuBytes, gpuBytes, current) != current);
            }
        }

        void PerformanceSettings(StackPanel c)
        {
            var result = SHint(performanceResult ?? "Замер длится 10 секунд. Чтобы измерить стекло под нагрузкой, подвигайте окно за панелью.");
            var copy = Btn("Скопировать результат", () => { if (performanceResult != null) try { Clipboard.SetText(performanceResult); } catch (Exception ex) { Log(ex); } });
            copy.IsEnabled = performanceResult != null;
            c.Children.Add(SetRow("Диагностика производительности", null, VS(CardActions(Btn("Измерить 10 секунд", () =>
            {
                if (measuringPerformance) { result.Text = "Замер уже идёт."; return; }
                measuringPerformance = true;
                var process = Process.GetCurrentProcess();
                var cpu = process.TotalProcessorTime;
                var watch = Stopwatch.StartNew();
                foreach (var counter in new Action[] { () => Interlocked.Exchange(ref glassSeen, 0), () => Interlocked.Exchange(ref glassShown, 0),
                    () => Interlocked.Exchange(ref glassSkipped, 0), () => Interlocked.Exchange(ref glassDelayTotal, 0),
                    () => Interlocked.Exchange(ref glassDelayCount, 0), () => Interlocked.Exchange(ref glassGpuBytes, 0) }) counter();
                Interlocked.Exchange(ref performanceUntil, Stopwatch.GetTimestamp() + Stopwatch.Frequency * 10);
                result.Text = "Идёт замер…";
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop(); measuringPerformance = false; Interlocked.Exchange(ref performanceUntil, 0);
                    process.Refresh();
                    double elapsed = watch.Elapsed.TotalSeconds;
                    double usage = (process.TotalProcessorTime - cpu).TotalSeconds * 100 / (elapsed * Environment.ProcessorCount);
                    long sample = Interlocked.Read(ref glassDelayCount), vram = Interlocked.Read(ref glassGpuBytes);
                    int profile = Math.Max(0, Array.IndexOf(GlassPerformanceKeys, C("material.performance", "quality")));
                    performanceResult = "Профиль «" + GlassPerformanceNames[profile] + "» · " + elapsed.ToString("0.0") + " с\n" +
                        "CPU " + usage.ToString("0.0") + "% · RAM " + (process.WorkingSet64 / 1048576.0).ToString("0.0") + " МБ" +
                        (vram > 0 ? " · видеопамять процесса до " + (vram / 1048576.0).ToString("0.0") + " МБ" : " · видеопамять недоступна") + "\n" +
                        "Стекло по всем секциям: захваты " + glassSeen + ", показано кадров " + glassShown + ", объединено DXGI " + glassSkipped + "\n" +
                        (sample > 0 ? "Среднее время до передачи кадра Windows: " + (glassDelayTotal * 1000.0 / Stopwatch.Frequency / sample).ToString("0.0") + " мс" : "Показанных кадров для замера задержки нет.");
                    process.Dispose(); result.Text = performanceResult; copy.IsEnabled = true;
                };
                timer.Start();
            }, true), copy), result)));
        }

        static readonly string[][] Favorites = {
            new[] { "Сила преломления", "material.lensStrength", "look", "1" },
            new[] { "Размер панели", "size", "bar", "-1" },
            new[] { "Автоскрытие", "autohide", "bar", "-1" },
            new[] { "Цвет акцента", "accent", "look", "3" },
            new[] { "Производительность стекла", "material.performance", "look", "1" }
        };

        void FavoriteSettings(StackPanel c, Action<string, int> go)
        {
            var list = new StackPanel();
            foreach (var spec in Favorites)
            {
                string key = spec[1];
                var card = new StackPanel();
                var enabled = new CheckBox { Content = "Закрепить «" + spec[0] + "»", IsChecked = C("favorite." + key, "0") == "1" };
                var actions = CardActions(Btn("Открыть параметр", () => go(spec[2], int.Parse(spec[3]))));
                var reset = Btn("Сбросить значение", () =>
                {
                    cfg.Remove(key); SaveCfg(); RebuildAll(); All(b => { b.reserveKey = null; b.Place(); }); RestyleWindows(); go("start", -1);
                });
                reset.Visibility = cfg.ContainsKey(key) ? Visibility.Visible : Visibility.Collapsed;
                actions.Children.Add(reset);
                actions.Visibility = enabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                enabled.Checked += (s, e) => { cfg["favorite." + key] = "1"; SaveCfg(); actions.Visibility = Visibility.Visible; };
                enabled.Unchecked += (s, e) => { cfg.Remove("favorite." + key); SaveCfg(); actions.Visibility = Visibility.Collapsed; };
                card.Children.Add(enabled); card.Children.Add(actions); list.Children.Add(SettingsRow(card));
            }
            c.Children.Add(new Expander { Header = "Избранные настройки", Content = list, IsExpanded = Favorites.Any(f => C("favorite." + f[1], "0") == "1") });
        }

        static Color? SectionAccentColor(int section)
        {
            Color c;
            return TryReadColor(C("accent.section." + section, ""), out c) ? (Color?)c : null;
        }

        Brush AdaptivePanelSectionFill(bool glass, int section)
        {
            return SectionTint(AdaptivePanelFill(glass), section);
        }

        static Brush SectionTint(Brush source, int section)
        {
            var tint = SectionAccentColor(section);
            if (!tint.HasValue) return source;
            var gradient = source as LinearGradientBrush;
            if (gradient != null)
            {
                foreach (var stop in gradient.GradientStops) stop.Color = SectionTintColor(stop.Color, tint.Value);
                return gradient;
            }
            var solid = source as SolidColorBrush;
            return solid != null ? new SolidColorBrush(SectionTintColor(solid.Color, tint.Value)) : source;
        }

        static Color SectionTintColor(Color background, Color tint)
        {
            return Color.FromArgb(background.A, (byte)((background.R * 7 + tint.R) / 8),
                (byte)((background.G * 7 + tint.G) / 8), (byte)((background.B * 7 + tint.B) / 8));
        }

        void SectionAccentSettings(StackPanel c)
        {
            if (!Sections) return;
            var rows = new StackPanel();
            var names = new[] { "Виджеты", "Приложения", "Системные кнопки" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                string key = "accent.section." + index;
                var choices = new[] { "", "#4CC2FF", "#FF865F", "#A78BFA", "#34D399" };
                rows.Children.Add(SetRow(names[index], null, Seg(new[] { "Общий", "Голубой", "Тёплый", "Лиловый", "Мятный" },
                    Math.Max(0, Array.IndexOf(choices, C(key, ""))), n =>
                    {
                        if (n == 0) cfg.Remove(key); else cfg[key] = choices[n];
                        SaveCfg(); All(b => b.PaintSections(true)); if (refreshPreview != null) refreshPreview(null);
                    })));
            }
            c.Children.Add(new Expander { Header = "Акценты секций", Content = rows });
        }

        static void TransitionLabel(TextBlock label, string text, bool animate)
        {
            if (label.Text == text) return;
            label.Text = text;
            if (animate && label.IsVisible && Motion)
                label.BeginAnimation(OpacityProperty, new DoubleAnimation(label.Opacity * 0.48, label.Opacity, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
        }

        static void TransitionLabelWidth(TextBlock label, double compact, double full, bool expanded, bool animate)
        {
            double width = expanded ? full : compact;
            var previous = label.Tag as double?;
            if (previous.HasValue && previous.Value == width) return;
            double before = label.Width;
            label.Tag = width;
            label.BeginAnimation(WidthProperty, null);
            label.Width = width;
            if (animate && label.IsVisible && Motion && previous.HasValue && !double.IsNaN(before))
                label.BeginAnimation(WidthProperty, new DoubleAnimation(before, width, TimeSpan.FromMilliseconds(210))
                { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        }

        static bool appRefreshQueued;
        UIElement MakePanelWidget(string id)
        {
            var callbacks = new List<Action>();
            var widget = MakeWidget(id, live, true, callbacks) as FrameworkElement;
            if (widget == null) return null;
            foreach (var callback in callbacks)
            {
                ups.Add(callback);
                visualOwners.Add(callback, widget);
            }
            widget.Loaded += (s, e) =>
            {
                foreach (var callback in callbacks)
                    try { callback(); }
                    catch (Exception ex) { if (renderErrors.Add(ex.GetType().Name + ex.Message)) Log(ex); }
            };
            return widget;
        }

        DateTime nextAppPoll;
        void PollApps()
        {
            if (!primary || DateTime.UtcNow < nextAppPoll) return;
            nextAppPoll = DateTime.UtcNow.AddSeconds(DemoMode ? 2 : 4);
            QueueWindowRefresh();
        }

        void QueueWindowRefresh()
        {
            if (appRefreshQueued) return;
            appRefreshQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                appRefreshQueued = false;
                if (shuttingDown || dragging) return;
                GetWindows();
                var snapshot = allWins;
                All(b => { b.allWins = snapshot; b.RefreshAppsFrom(b.WindowsForBar(snapshot)); });
            }));
        }
    }
}
