using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Screen = System.Windows.Forms.Screen;
using PixelRect = System.Drawing.Rectangle;

namespace FlowBar
{
    partial class Bar
    {
        static bool directEdit;
        static Action undoPanelEdit;
        Press undoEditTile;

        void SetDirectEdit(bool enabled)
        {
            if (enabled && collapsed) SetCollapsed(false);
            directEdit = enabled;
            if (!enabled) undoPanelEdit = null;
            editMode = enabled;
            All(b => { b.ResetFit(false); b.RenderWidgets(); b.PaintSections(true); });
        }

        bool EditKey(string key)
        {
            return bars.Any(b => b.MineKey(key) || key == b.SeqKey || key == b.MK("preset") ||
                key == b.SecKey(1) || key == b.SecKey(2) || key == "secorder@" + b.scr.DeviceName ||
                key == "widgets@" + b.scr.DeviceName ||
                key == "secmap@" + b.scr.DeviceName || key == "mon." + b.scr.DeviceName || key == "mon.compact." + b.scr.DeviceName);
        }

        void RememberPanelEdit()
        {
            if (!directEdit) return;
            var settings = cfg.Where(kv => EditKey(kv.Key)).ToArray();
            var order = appOrder.ToList();
            var sequences = bars.ToDictionary(b => b, b => b.seq == null ? null : b.seq.ToList());
            undoPanelEdit = () =>
            {
                foreach (var k in cfg.Keys.Where(EditKey).ToArray()) cfg.Remove(k);
                foreach (var kv in settings) cfg[kv.Key] = kv.Value;
                appOrder = order;
                All(b => { List<string> saved; if (sequences.TryGetValue(b, out saved)) b.seq = saved; else b.LoadSeq(); });
                SaveCfg(); SavePinned(); RebuildAll(); RestyleWindows();
            };
            All(b => b.UpdateEditControls());
        }

        void UndoPanelEdit()
        {
            if (dragging || undoPanelEdit == null) return;
            var undo = undoPanelEdit;
            undoPanelEdit = null;
            undo();
        }

        void EditShortcut(object sender, KeyEventArgs e)
        {
            if (!directEdit || e.Key != System.Windows.Input.Key.Z || Keyboard.Modifiers != ModifierKeys.Control) return;
            e.Handled = true;
            UndoPanelEdit();
        }

        void UpdateEditControls()
        {
            if (undoEditTile != null)
            {
                undoEditTile.Visibility = directEdit && !collapsed ? Visibility.Visible : Visibility.Collapsed;
                undoEditTile.IsEnabled = undoPanelEdit != null;
                undoEditTile.Opacity = undoPanelEdit == null ? 0.48 : 1;
            }
        }

        void ResizePanelWidget(string id, string view)
        {
            RememberPanelEdit();
            cfg[MK("view." + id)] = view;
            MarkCustom(); SaveCfg(); RebuildAll(); RestyleWindows();
        }

        ContextMenu EditWidgetSizes(string id)
        {
            var m = new FlowMenu();
            var current = CM("view." + id, id == "conn" ? "min" : "");
            for (int i = 0; i < ViewKeys.Length; i++)
            {
                string key = ViewKeys[i];
                m.Items.Add(Item(ViewNames[i], () => ResizePanelWidget(id, key), current == key));
            }
            return m;
        }

        static readonly string[] ScenarioNames = { "Работа", "Игра", "Ноутбук" };
        static readonly string[] ScenarioHints = { "Задачи, таймер и заметки. Панель всегда видна.",
            "Компактная тёмная панель с автоскрытием. Обычные всплывающие уведомления приглушены.",
            "Заряд, Wi-Fi и звук на панели. Автоскрытие освобождает место." };
        static readonly int[] ScenarioLayouts = { 4, 8, 6 };
        static readonly int[] ScenarioStyles = { 2, 0, 3 };

        bool ScenarioKey(string k)
        {
            return PresetMaterialKeys.Contains(k) || k == "appearance.schedule" || k == "autohide" || k == "notify" ||
                MineKey(k) || k == MK("preset") || k == MK("preset.custom");
        }

        void ApplyScenario(int index) { ApplyScenarioCore(index, false); }

        void ApplyScenarioCore(int index, bool automatic)
        {
            var target = Primary ?? this;
            if (target != this) { target.ApplyScenarioCore(index, automatic); return; }
            const string backup = "scenario.previous";
            if (index < 0)
            {
                if (!cfg.ContainsKey(backup)) return;
                Dictionary<string, string> saved;
                try { saved = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(cfg[backup]); }
                catch (ArgumentException ex) { Log(ex); return; }
                if (saved == null) return;
                foreach (var key in cfg.Keys.Where(ScenarioKey).ToArray()) cfg.Remove(key);
                foreach (var pair in saved.Where(kv => ScenarioKey(kv.Key))) cfg[pair.Key] = pair.Value;
                cfg.Remove(backup);
                cfg.Remove("scenario.active");
            }
            else
            {
                if (!cfg.ContainsKey(backup))
                    cfg[backup] = new JavaScriptSerializer().Serialize(cfg.Where(kv => ScenarioKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
                SetStylePreset(cfg, StylePresets[ScenarioStyles[index]]);
                cfg["appearance.schedule"] = "off";
                cfg["autohide"] = index == 0 ? "0" : "1";
                cfg["notify"] = index == 1 ? "0" : "1";
                cfg["scenario.active"] = index.ToString();
                ApplyPreset(ScenarioLayouts[index], false);
            }
            if (automatic)
            {
                if (index >= 0) cfg["scenario.automatic"] = "1"; else cfg.Remove("scenario.automatic");
                autoScenario = index;
            }
            else
            {
                autoScenario = -1; cfg.Remove("scenario.automatic");
                if (C("scenario.rules", "0") == "1") cfg["scenario.manual"] = "1";
            }
            if (directEdit) SetDirectEdit(false);
            SaveCfg(); ClosePanels(); RebuildAll();
            All(b => { b.reserveKey = null; b.AutoHideTick(); b.Place(); });
            RestyleWindows();
        }

        MenuItem ScenarioMenu()
        {
            var menu = MenuBranch("Сценарии панели");
            for (int i = 0; i < ScenarioNames.Length; i++)
            {
                int index = i;
                var item = Item(ScenarioNames[i], () => ApplyScenario(index), C("scenario.active", "") == i.ToString());
                item.ToolTip = ScenarioHints[i];
                menu.Items.Add(item);
            }
            if (cfg.ContainsKey("scenario.previous")) menu.Items.Add(Item("Вернуть мои настройки", () => ApplyScenario(-1)));
            return menu;
        }

        void ScenarioSettings(StackPanel content, Action again)
        {
            content.Children.Add(SetRow("Сценарии панели", "Меняют оформление, виджеты основной панели, автоскрытие и уведомления.",
                CardActions(ScenarioNames.Select((name, index) => Btn(name, () => { ApplyScenario(index); again(); },
                    C("scenario.active", "") == index.ToString())).ToArray())));
            int active;
            if (int.TryParse(C("scenario.active", ""), out active) && active >= 0 && active < ScenarioNames.Length)
                content.Children.Add(Txt(ScenarioHints[active], 12, false));
            if (cfg.ContainsKey("scenario.previous"))
                content.Children.Add(CardActions(Btn("Вернуть мои настройки", () => { ApplyScenario(-1); again(); })));
            ScenarioRuleSettings(content, again);
        }

        sealed class QuickResult
        {
            internal string Title, Detail;
            internal Action Open;
        }

        internal static int QuickTimerMinutes(string query)
        {
            var parts = query.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            int minutes;
            return parts.Length == 2 && parts[0].Equals("таймер", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(parts[1], out minutes) && minutes >= 1 && minutes <= 1440 ? minutes : 0;
        }

        static int DisplayNumber(IntPtr monitor)
        {
            foreach (var screen in Screen.AllScreens)
            {
                if (MonitorId(screen) != monitor) continue;
                int number;
                var suffix = new string(screen.DeviceName.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
                if (int.TryParse(suffix, out number)) return number;
            }
            return 0;
        }

        static string DisplayName(IntPtr monitor)
        {
            int n = DisplayNumber(monitor);
            return n > 0 ? "Экран " + n : "Экран";
        }

        void QuickSearchFromHotkey()
        {
            var screen = N.MonitorFromWindow(N.GetForegroundWindow(), 2);
            var target = bars.FirstOrDefault(b => b.mon == screen) ?? this;
            target.ShowQuickSearch();
        }

        void ShowQuickSearch()
        {
            if (autoHidden) { autoHidden = false; lastUse = DateTime.Now; ApplyVis(); }
            if (panel == null) lastClosedTitle = null;
            GetWindows();
            var windows = allWins.OrderBy(win => win.Mon != mon).Select(win => new QuickResult
            {
                Title = TitleOf(win.H), Detail = (win.Path == null ? "Приложение" : PinName(win.Path)) + " · " + DisplayName(win.Mon),
                Open = () => { if (N.IsWindow(win.H)) ActivateWin(win.H); }
            }).ToArray();
            var commands = new[] {
                new QuickResult { Title = "Настройки", Detail = "FlowBar", Open = () => ShowSettings(null) },
                new QuickResult { Title = "Каталог виджетов", Detail = "FlowBar", Open = () => ShowCatalog(this) },
                new QuickResult { Title = "Уведомления FlowBar", Detail = "FlowBar", Open = ShowNotificationCenter },
                new QuickResult { Title = "Правка панели", Detail = "FlowBar", Open = () => SetDirectEdit(true) }
            };
            var input = new TextBox { FontSize = 15, MinHeight = 34 };
            System.Windows.Automation.AutomationProperties.SetName(input, "Поиск открытых окон или команд");
            var options = new ListBox { MaxHeight = 300, BorderThickness = new Thickness(0), Margin = new Thickness(0, 6, 0, 0), Background = Brushes.Transparent,
                Foreground = Frozen(CardFg, 255), ItemContainerStyle = QuickResultStyle() };
            var hint = Txt("Название окна, приложение или команда «таймер 10»", 12, false);
            hint.Margin = new Thickness(0, 6, 0, 3);
            Action<QuickResult> open = result =>
            {
                if (result == null) return;
                if (panel != null) panel.Close();
                anchor = null;
                result.Open();
            };
            Action fill = () =>
            {
                string q = input.Text.Trim();
                int minutes = QuickTimerMinutes(q);
                var matches = new List<QuickResult>();
                if (minutes > 0) matches.Add(new QuickResult {
                    Title = "Таймер на " + minutes + " мин", Detail = timerRunning ? "Заменит текущий таймер" : "Запустить отсчёт",
                    Open = () => { timerEnd = DateTime.Now.AddMinutes(minutes); timerLeft = TimeSpan.Zero; timerRunning = true; }
                });
                matches.AddRange((q == "" ? windows.Concat(commands) : commands.Concat(windows)).Where(r =>
                    (r.Title + " " + r.Detail).IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0).Take(12 - matches.Count));
                options.Items.Clear();
                foreach (var result in matches)
                {
                    var item = new ListBoxItem { Content = VS(Txt(string.IsNullOrWhiteSpace(result.Title) ? result.Detail : result.Title, 13, true), Txt(result.Detail == "FlowBar" || result.Title.StartsWith("Таймер на ") ? "Команда · " + result.Detail : "Окно · " + result.Detail, 11, false)),
                        Foreground = Frozen(CardFg, 255), Padding = new Thickness(8, 4, 8, 4), Tag = result };
                    options.Items.Add(item);
                    item.MouseLeftButtonUp += (s, e) => open(result);
                }
                options.SelectedIndex = matches.Count == 0 ? -1 : 0;
                hint.Text = matches.Count == 0 ? "Совпадений нет. Формат таймера — «таймер 10»." : "↑ ↓ выбрать   ·   Enter открыть";
            };
            input.TextChanged += (s, e) => fill();
            var body = VS(input, hint, options);
            body.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter && options.SelectedItem is ListBoxItem)
                { e.Handled = true; open(((ListBoxItem)options.SelectedItem).Tag as QuickResult); }
                else if ((e.Key == System.Windows.Input.Key.Up || e.Key == System.Windows.Input.Key.Down) && options.Items.Count != 0)
                {
                    e.Handled = true;
                    int step = e.Key == System.Windows.Input.Key.Up ? -1 : 1;
                    options.SelectedIndex = (options.SelectedIndex + step + options.Items.Count) % options.Items.Count;
                    options.ScrollIntoView(options.SelectedItem);
                }
            };
            fill();
            ShowPanel("Быстрый поиск", body, 440, input);
        }

        Style QuickResultStyle()
        {
            var edge = new FrameworkElementFactory(typeof(Border), "Plate");
            edge.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            edge.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            edge.SetValue(Border.PaddingProperty, new Thickness(10, 6, 10, 6));
            edge.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = edge };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Frozen(CardFg, 0x14), "Plate"));
            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Border.BackgroundProperty, Frozen(AccentColor, 0x32), "Plate"));
            selected.Setters.Add(new Setter(Border.BorderBrushProperty, Frozen(AccentColor, 0xB0), "Plate"));
            selected.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1), "Plate"));
            edge.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            edge.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            template.Triggers.Add(hover); template.Triggers.Add(selected);
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 3)));
            return style;
        }

        static IntPtr MonitorId(Screen screen)
        {
            var box = screen.Bounds;
            var rect = new N.RECT { L = box.Left, T = box.Top, R = box.Right, B = box.Bottom };
            return MonitorFromRect(ref rect, 2);
        }

        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out N.RECT value, int size);

        static bool WindowCanTile(IntPtr h)
        {
            return N.IsWindow(h) && (N.GetWindowLongPtr(h, -16).ToInt64() & 0x00040000) != 0; // WS_THICKFRAME
        }

        internal static N.RECT WindowTileBounds(N.RECT area, int side)
        {
            return ZoneBounds(area, side);
        }

        void TileWindow(IntPtr h, int side)
        {
            ClosePreview();
            if (!WindowCanTile(h)) return;
            var work = WorkOf(MonOf(h));
            if (work.R <= work.L || work.B <= work.T) return;
            if (N.IsZoomed(h) || N.IsIconic(h)) N.ShowWindow(h, 9);
            var area = WindowTileBounds(work, side);
            N.RECT outer, visible;
            if (N.GetWindowRect(h, out outer) && DwmGetWindowAttribute(h, 9, out visible, Marshal.SizeOf(typeof(N.RECT))) == 0)
            {
                area.L -= visible.L - outer.L; area.R += outer.R - visible.R;
                area.T -= visible.T - outer.T; area.B += outer.B - visible.B;
            }
            N.SetWindowPos(h, IntPtr.Zero, area.L, area.T, area.R - area.L, area.B - area.T, 0x4 | 0x10);
            ActivateWin(h);
        }

        void AddWindowActions(ItemsControl menu, IntPtr h)
        {
            if (!N.IsWindow(h)) return;
            if (WindowCanTile(h))
            {
                AddZone(menu, h, 0);
                AddZone(menu, h, 1);
                var more = MenuBranch("Другие макеты");
                for (int i = 2; i < ZoneNames.Length; i++) AddZone(more, h, i);
                more.SubmenuClosed += (s, e) => HidePlacementOutline();
                menu.Items.Add(more);
                if ((N.GetWindowLongPtr(h, -16).ToInt64() & 0x10000) != 0)
                    menu.Items.Add(Item("Развернуть", () => { ClosePreview(); if (N.IsWindow(h)) { N.ShowWindow(h, 3); ActivateWin(h); } }));
            }
            var from = MonOf(h);
            foreach (var target in Screen.AllScreens.Select(MonitorId).Where(m => m != IntPtr.Zero && m != from))
                menu.Items.Add(Item("Перенести · " + DisplayName(target), () => { ClosePreview(); if (N.IsWindow(h)) MoveToMonitor(h, target); }));
        }

        sealed class Notice
        {
            internal Toast Toast;
            internal DateTime At;
            internal bool Read, Quiet;
        }

        static readonly List<Notice> notices = new List<Notice>();
        static event Action NoticeChanged;

        static string NoticeCountLabel()
        {
            int count = notices.Count(n => !n.Read);
            return count == 0 ? "" : " (" + count + ")";
        }

        static void UseNoticeAction(Toast toast)
        {
            var act = toast.Act;
            if (act == null) return;
            toast.Act = null;
            var notice = notices.FirstOrDefault(n => n.Toast == toast);
            if (notice != null) notice.Read = true;
            if (NoticeChanged != null) NoticeChanged();
            RunAct(act, toast.Widget);
        }

        static string NoticeSource(string id)
        {
            var def = id == "clock" ? ClockDef : Def(id);
            return def == null ? "FlowBar" : def[1];
        }

        static void RecordNotice(Toast toast, bool quiet)
        {
            notices.Insert(0, new Notice { Toast = toast, At = DateTime.Now, Quiet = quiet });
            if (notices.Count > 60) notices.RemoveAt(notices.Count - 1);
            if (NoticeChanged != null) NoticeChanged();
        }

        static string NoticeKey(Toast toast)
        {
            if (toast.Title == "Таймер" && toast.Widget == "tools") return "timer";
            return toast.Widget ?? "flowbar";
        }

        void BuildNotificationCenter(Panel body)
        {
            var sourceField = new ContentControl();
            var list = new StackPanel();
            var readButton = Btn("Прочитать все", () =>
            {
                foreach (var notice in notices) notice.Read = true;
                if (NoticeChanged != null) NoticeChanged();
            });
            readButton.HorizontalAlignment = HorizontalAlignment.Left;
            bool onlyUnread = false;
            string source = "";
            Action fill = null;
            var toggle = new CheckBox { Content = "Только непрочитанные", Margin = new Thickness(0, 8, 0, 8) };
            toggle.Checked += (s, e) => { onlyUnread = true; fill(); };
            toggle.Unchecked += (s, e) => { onlyUnread = false; fill(); };
            body.Children.Add(sourceField); body.Children.Add(toggle); body.Children.Add(readButton); body.Children.Add(list);
            fill = () =>
            {
                var keys = notices.Select(n => NoticeKey(n.Toast)).Distinct().OrderBy(NoticeSource).ToList();
                keys.Insert(0, "");
                if (!keys.Contains(source)) source = "";
                sourceField.Visibility = keys.Count > 2 ? Visibility.Visible : Visibility.Collapsed;
                toggle.Visibility = notices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                sourceField.Content = CardActions(keys.Select(k => Btn(k == "" ? "Все источники" : NoticeSource(k), () => { source = k; fill(); }, source == k)).ToArray());
                readButton.Visibility = notices.Any(n => !n.Read) ? Visibility.Visible : Visibility.Collapsed;
                list.Children.Clear();
                var visible = notices.Where(n => (!onlyUnread || !n.Read) && (source == "" || source == NoticeKey(n.Toast))).ToArray();
                if (visible.Length == 0) list.Children.Add(Txt(notices.Count == 0 ? "Здесь пока нет уведомлений." : "По выбранному фильтру уведомлений нет.", 13, false));
                foreach (var n in visible)
                {
                    var description = Txt(n.Toast.Text, 12, false);
                    description.TextWrapping = TextWrapping.Wrap;
                    var item = VS(Txt((n.Read ? "" : "● ") + n.Toast.Title, 14, true),
                        Txt(NoticeSource(NoticeKey(n.Toast)) + " · " + n.At.ToString("HH:mm") + (n.Quiet ? " · без всплывающего окна" : ""), 11, false),
                        description);
                    var actions = new List<Border>();
                    if (!n.Read) actions.Add(Btn("Прочитано", () => { n.Read = true; if (NoticeChanged != null) NoticeChanged(); }));
                    if (n.Toast.Act != null && !string.IsNullOrEmpty(n.Toast.ActLabel)) actions.Add(Btn(n.Toast.ActLabel, () =>
                    {
                        if (panel != null) panel.Close();
                        UseNoticeAction(n.Toast);
                    }, true));
                    if (actions.Count > 0) item.Children.Add(CardActions(actions.ToArray()));
                    list.Children.Add(new Border { Child = item, Padding = new Thickness(10), Margin = new Thickness(0, 7, 0, 0),
                        CornerRadius = new CornerRadius(9), Background = Frozen(CardFg, n.Read ? (byte)0x09 : (byte)0x16) });
                }
            };
            body.Loaded += (s, e) => { NoticeChanged += fill; fill(); };
            body.Unloaded += (s, e) => NoticeChanged -= fill;
        }

        void ShowNotificationCenter()
        {
            if (panel == null) lastClosedTitle = null;
            var body = new StackPanel();
            BuildNotificationCenter(body);
            ShowPanel("Уведомления FlowBar", body, 440, null);
        }

        DateTime nextGlassContrast;
        bool glassContrastPending;
        double glassDensity;
        Tw glassDensityTween;
        bool? glassLightText;
        static bool AdaptiveGlass { get { return C("material.adaptive", "1") == "1"; } }

        Brush AdaptivePanelFill(bool glass)
        {
            var brush = MaterialFill(Cur, glass);
            var gradient = brush as LinearGradientBrush;
            if (!glass || !AdaptiveGlass || gradient == null || glassDensity <= 0 || glassLightText != Cur.LightText) return brush;
            foreach (var stop in gradient.GradientStops)
            {
                var color = stop.Color;
                color.A = (byte)Math.Round(color.A + (255 - color.A) * glassDensity);
                stop.Color = color;
            }
            return brush;
        }

        internal static double GlassDensityTarget(double luminance, bool lightText)
        {
            double opposing = lightText ? luminance : 1 - luminance;
            return Math.Max(0, Math.Min(0.78, (opposing - 0.26) * 1.22));
        }

        void RefreshAdaptiveGlass()
        {
            if (!AdaptiveGlass || !GlassOn || !IsVisible || root == null || !root.IsLoaded) return;
            if (glassContrastPending || DateTime.UtcNow < nextGlassContrast) return;
            nextGlassContrast = DateTime.UtcNow.AddMilliseconds(620);
            var r = PixelRect.Intersect(IndicatorScreenRect(root), scr.Bounds);
            if (r.Width < 12 || r.Height < 12) return;
            // Read a narrow strip past the material, so the tint never samples itself.
            var band = VerticalBar ? new PixelRect(LeftBar ? r.Right + 7 : r.Left - 11, r.Top, 4, r.Height)
                : new PixelRect(r.Left, IsTop ? r.Bottom + 7 : r.Top - 11, r.Width, 4);
            band.Intersect(scr.Bounds);
            if (band.Width == 0 || band.Height == 0) return;
            glassContrastPending = true;
            var surface = root;
            bool vertical = VerticalBar, light = Cur.LightText;
            Task.Run(() =>
            {
                double target = -1;
                try
                {
                    using (var image = new LensDib()) if (image.CopyFromDesktop(band))
                    {
                        var values = new List<double>(24);
                        for (int i = 1; i <= 24; i++)
                        {
                            int x = vertical ? band.Width / 2 : band.Width * i / 25;
                            int y = vertical ? band.Height * i / 25 : band.Height / 2;
                            values.Add(AppWindowIndicator.Luminance(Marshal.ReadInt32(image.Pixels, (y * band.Width + x) * 4)));
                        }
                        values.Sort();
                        target = GlassDensityTarget(values[light ? 17 : 6], light);
                    }
                }
                catch (Exception) { }
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    glassContrastPending = false;
                    if (target < 0 || surface != root || !IsVisible || !AdaptiveGlass || !GlassOn || light != Cur.LightText) return;
                    if (glassDensityTween == null)
                    {
                        glassDensityTween = new Tw(value =>
                        {
                            glassDensity = value;
                            if (!AdaptiveGlass || !GlassOn) return;
                            if (UseSections)
                                foreach (var shape in sectionSurface.Children.OfType<Border>()) shape.Background = AdaptivePanelSectionFill(true, shape.Tag is int ? (int)shape.Tag : 2);
                            else root.Background = AdaptivePanelFill(true);
                        }, 490);
                        glassDensityTween.Jump(glassDensity);
                    }
                    if (glassLightText != light)
                    {
                        glassLightText = light;
                        glassDensityTween.Jump(0);
                    }
                    if (Math.Abs(target - glassDensity) < 0.045) return;
                    glassDensityTween.To(target);
                }));
            });
        }
    }
}
