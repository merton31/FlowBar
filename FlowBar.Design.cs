// FlowBar — оформление: размеры S/M/L, капсула, модули виджетов, данные для них, переполнение.
// Карточки, каталог, настройки и уведомления — в FlowBar.Cards.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;
using MC = Windows.Media.Control;
using RD = Windows.Devices.Radios;
using NC = Windows.Networking.Connectivity;

namespace FlowBar
{
    partial class Bar
    {
        // ---------- токены: размер, отступ, прозрачность, акцент ----------
        string SizeKey { get { var s = MonitorSetting("size", "M"); return s == "S" || s == "L" || s == "XL" ? s : "M"; } }
        double BarH { get { return SizeKey == "S" ? 48 : SizeKey == "L" ? 72 : SizeKey == "XL" ? 88 : 60; } }
        double TileH { get { return BarH - (SizeKey == "XL" ? 10 : 14); } }
        double IconPx { get { return SizeKey == "S" ? 24 : SizeKey == "L" ? 36 : SizeKey == "XL" ? 42 : 30; } }
        double Fs { get { return SizeKey == "S" ? 1 : SizeKey == "L" ? 1.25 : SizeKey == "XL" ? 1.5 : 1.12; } }
        public static double Clamp(string v, double d, double min, double max)
        {
            double x; return double.TryParse(v ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out x) && !double.IsNaN(x) && !double.IsInfinity(x) ? Math.Max(min, Math.Min(max, x)) : d;
        }
        static double Gap { get { return Flush ? 0 : Clamp(C("gap", ""), 10, 0, 32); } } // вплотную — без отступа, как панель Windows
        // ширина: fit — по содержимому; wide — широкая плавающая (отступ и скругление сохраняются); full — во всю ширину вплотную к краю
        static bool FullWidth { get { return C("width", "fit") != "fit"; } }
        static bool Flush { get { return C("width", "fit") == "full"; } }                // отступ капсулы от края экрана
        static double Opac { get { return Clamp(C("opacity", ""), 100, 30, 100) / 100; } }
        const double SM = 14; // поле окна под тень капсулы
        static Color AccentColor
        {
            get { var a = C("accent", ""); if (a != "") try { return (Color)ColorConverter.ConvertFromString(a); } catch { } return Cur.Ac; }
        }
        // чёткость: макет и текст по пиксельной сетке монитора; ставится на каждое окно FlowBar при загрузке
        public static void ConfigureSharpness(FrameworkElement root)
        {
            root.UseLayoutRounding = true; root.SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
            TextOptions.SetTextHintingMode(root, TextHintingMode.Fixed);
        }
        static bool sharpHooked;
        static void HookSharpness()
        {
            if (sharpHooked) return; sharpHooked = true;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) => { if (s == e.OriginalSource) ConfigureSharpness((Window)s); }));
        }
        static Color Fa(byte a) { var f = Cur.Fg; return Color.FromArgb(a, f.R, f.G, f.B); }
        // подложка модуля-карточки; на стекле — противоположная тексту (тёмная под светлым текстом, светлая под тёмным), чтобы надписи читались на любом фоне за панелью
        static Color Surface { get { return GlassOn ? (Cur.LightText ? Color.FromArgb(0x40, 0, 0, 0) : Color.FromArgb(0xB0, 255, 255, 255)) : Fa(Cur.LightText ? (byte)0x19 : (byte)0x0C); } }
        // запас лимита ИИ — зелёным, как на образце; ближе к лимиту — оранжевый и красный (LevelBrush)
        static readonly Brush AiOk = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
        static Brush ChipEdge { get { return new SolidColorBrush(Fa(Cur.LightText ? (byte)0x16 : (byte)0x1A)); } } // тонкая рамка карточки
        // цвета модулей, как на образцах: цвет означает модуль, а не украшение
        static readonly Color CRed = Color.FromRgb(0xFF, 0x45, 0x3A), COrange = Color.FromRgb(0xFF, 0x9F, 0x0A), CBlue = Color.FromRgb(0x0A, 0x84, 0xFF);
        // подпись над значением: мелкая, полужирная, в цвете модуля (или приглушённая)
        static TextBlock Cap(string text, double f, Color? c = null) { var t = L(text, 9.5 * f, c != null, c == null ? 0.5 : 1); // подпись тише значения, как на образце
             if (c != null) t.Foreground = new SolidColorBrush(c.Value); t.Margin = new Thickness(0, 0, 0, -2 * f); return t; } // вплотную к значению
        static Color Lift(Color c, int add) { var f = Cur.Fg; return Color.FromArgb((byte)Math.Min(255, c.A + add), f.R, f.G, f.B); }

        // основные темы — по образцам в настройках; темы Theme Factory остаются в разделе «Ещё»
        static readonly string[][] Themes = {
            new[] { "Black", "Тёмная" }, new[] { "White", "Светлая" }, new[] { "Midnight", "Синяя ночь" },
            new[] { "Frosted Dark", "Матовое стекло, тёмное" }, new[] { "iOS Glass", "Матовое стекло, светлое" },
            new[] { "Liquid Glass", "Прозрачное стекло" }, new[] { "Clear", "Без фона" } };
        static string ThemeName(string key) { var t = Themes.FirstOrDefault(x => x[0] == key); return t != null ? t[1] : key; }
        static readonly string[] AccentPalette = { "", "#4CC2FF", "#7AA2FF", "#A78BFA", "#F472B6", "#F87171", "#FB923C", "#FACC15", "#4ADE80", "#2DD4BF" };

        // ---------- каталог: id, название, описание, группа на панели, категория каталога ----------
        static readonly string[][] WidgetDefs = {
            new[] { "cpu", "Процессор", "Загрузка процессора: кольцо и проценты", "sys", "sys" },
            new[] { "ram", "Память", "Занятая оперативная память", "sys", "sys" },
            new[] { "temp", "Температура", "Датчик ACPI и нагрузка процессора и памяти", "sys", "sys" },
            new[] { "net", "Сеть", "Скорость приёма и отдачи с графиком", "sys", "sys" },
            new[] { "disk", "Накопитель", "Свободное место на системном диске", "sys", "sys" },
            new[] { "bat", "Батарея", "Заряд и признак зарядки (на ноутбуке)", "sys", "sys" },
            new[] { "claude", "Claude", "Claude Code: токены сессии и недели, время до сброса", "ai", "ai" },
            new[] { "codex", "Codex", "Лимиты Codex: сессия и неделя, время до сброса", "ai", "ai" },
            new[] { "media", "Музыка", "Обложка, трек, переключение и прогресс", "media", "media" },
            new[] { "vol", "Громкость", "Устройство вывода, ползунок и выключение звука", "media", "media" },
            new[] { "pomo", "Фокус", "Помодоро: отсчёт, этап и номер интервала", "focus", "prod" },
            new[] { "todos", "Задачи", "Ближайшая задача и выполнение списка (Alt+T)", "focus", "prod" },
            new[] { "notes", "Заметки", "Начало последней заметки", "focus", "prod" },
            new[] { "event", "Календарь", "Ближайшее событие, начало и «через …»", "focus", "prod" },
            new[] { "screen", "Экранное время", "Время за компьютером и активность по часам", "focus", "prod" },
            new[] { "timer", "Таймер", "Обратный отсчёт: старт, пауза и сброс прямо на панели", "focus", "prod" },
            new[] { "stopwatch", "Секундомер", "Старт, пауза и сброс прямо на панели", "focus", "prod" },
            new[] { "pday", "Прогресс дня", "Точка — час суток: прошедшие закрашены", "info", "clock" },
            new[] { "pmonth", "Прогресс месяца", "Точка — день месяца", "info", "clock" },
            new[] { "pyear", "Прогресс года", "Точка — неделя года", "info", "clock" },
            new[] { "tools", "Инструменты", "Таймер, секундомер, валюта, файлы", "apps", "apps" },
            new[] { "fx", "Валюта", "Сумма в другой валюте прямо на панели; клик — выбор валют. Курс open.er-api.com, обновление раз в 12 часов", "info", "finance" },
            new[] { "bin", "Корзина", "Открыть, очистить, бросить файлы для удаления", "apps", "apps" },
            new[] { "weather", "Погода", "Значок, температура, город и диапазон дня", "info", "weather" },
            new[] { "tray", "Трей", "Показать значки трея Windows", "tray", "sys" },
            new[] { "conn", "Сеть и интернет", "Тип подключения, уровень сигнала Wi-Fi и есть ли интернет — как в панели Windows", "tray", "sys" },
            new[] { "radios", "Wi-Fi и Bluetooth", "Переключатели радиомодулей", "tray", "sys" },
            new[] { "quick", "Быстрые настройки", "Wi-Fi, звук, яркость (Win+A)", "tray", "sys" },
            new[] { "notif", "Уведомления", "Центр уведомлений Windows (Win+N)", "tray", "sys" },
            new[] { "lang", "Раскладка", "Язык ввода, клик — сменить", "tray", "sys" },
        };
        static readonly HashSet<string> HiddenByDefault = new HashSet<string> { "temp", "disk", "notes", "screen", "tools", "bin", "vol", "timer", "stopwatch", "pday", "pmonth", "pyear", "fx" };
        static readonly HashSet<string> Variants = new HashSet<string> { "cpu", "ram", "temp", "net", "disk", "bat", "claude", "codex", "media", "vol", "event", "weather", "conn", "clock", "pomo", "timer", "stopwatch", "notes", "todos", "screen" };
        static string[] Def(string id) { return WidgetDefs.FirstOrDefault(d => d[0] == id); }
        static IEnumerable<string> DefaultLayout { get { return WidgetDefs.Select(d => (HiddenByDefault.Contains(d[0]) ? "-" : "") + d[0]); } }

        // раскладка: у основного монитора своя; у остальных — нет виджетов, как на основном или своя
        string MonMode { get { return primary ? "own" : C("mon." + scr.DeviceName, "none"); } }
        bool HasWidgets { get { return MonMode != "none"; } }
        string LayoutKey { get { return primary || MonMode != "own" ? "widgets" : "widgets@" + scr.DeviceName; } }
        List<KeyValuePair<string, bool>> Layout() { return ParseLayout(C(LayoutKey, ""), DefaultLayout); }
        void SaveLayout(List<KeyValuePair<string, bool>> l)
        {
            var key = LayoutKey;
            cfg[key] = string.Join(",", l.Select(x => (x.Value ? "" : "-") + x.Key)); MarkCustom(); SaveCfg();
            All(b => { if (b.HasWidgets && b.LayoutKey == key) { b.ResetFit(false); b.RenderWidgets(); } });
        }
        static Bar Primary { get { return bars.FirstOrDefault(b => b.primary) ?? bars.FirstOrDefault(); } }

        // вид модуля: 0 — минимальный, 1 — компактный, 2 — расширенный; по умолчанию следует размеру панели
        int squeeze; // при нехватке места виды временно упрощаются
        // настройки раскладки панели: у монитора со «Своей раскладкой» — свои ключи (ключ@монитор), иначе общие с основным;
        // незаданный свой ключ наследует общий
        string MK(string k) { return primary || MonMode != "own" ? k : k + "@" + scr.DeviceName; }
        string CM(string k, string d) { return C(MK(k), C(k, d)); }
        // ручная правка раскладки — набор превращается в «Своя»
        void MarkCustom() { cfg.Remove(MK("preset")); }
        int View(string id)
        {
            if (VerticalBar) return 0;
            var v = CM("view." + id, id == "conn" ? "min" : ""); // сеть по умолчанию — значок, как в панели Windows
            int d = SizeKey == "S" ? 0 : SizeKey == "L" || SizeKey == "XL" ? 2 : 1;
            int r = v == "min" ? 0 : v == "compact" ? 1 : v == "full" ? 2 : d;
            if (v != "" || Keep(id)) return r; // вид, выбранный вручную, и «Оставлять видимым» не упрощаются молча
            return id == "clock" && r >= 1 ? Math.Max(1, r - squeeze) : Math.Max(0, r - squeeze); // у часов дата не пропадает при нехватке места
        }
        bool Keep(string id) { return CM("keep." + id, id == "conn" ? "1" : "") == "1"; } // состояние сети не прячется в «…»
        bool Squeezable(string id) { return Variants.Contains(id) && CM("view." + id, "") == "" && !Keep(id); }
        int AppMax { get { int n; return int.TryParse(CM("appMax", "0"), out n) ? Math.Max(0, n) : 0; } }

        // стартовые наборы панели; «Своя» — раскладка пользователя, сохраняется перед первым выбором набора
        static readonly string[] PresetKeys = { "", "compact", "info", "minimal" };
        static readonly string[] PresetNames = { "Своя", "Компактная", "Информативная", "Минимальная" };
        static readonly string[] PresetHints = {
            "Ваша раскладка и виды модулей.",
            "Модули коротко: значок и главный показатель, подробности — по нажатию. Значкам программ больше места.",
            "Музыка и ближайшее событие читаются прямо на панели и не прячутся; значков программ до 10, остальные — в «…».",
            "До 6 значков программ, музыка, погода, раскладка и часы." };
        int PresetIndex { get { return Math.Max(0, Array.IndexOf(PresetKeys, C(MK("preset"), ""))); } }
        // набор и снимок «Своя» — у каждой панели свои (у монитора со своей раскладкой); у «Как на основном» — общие с основным
        void ApplyPreset(int i)
        {
            var key = PresetKeys[i]; if (C(MK("preset"), "") == key) return;
            var lk = LayoutKey; string sfx = MK("");
            Func<string, bool> mine = k => k == lk || ((sfx == "" ? !k.Contains("@") : k.EndsWith(sfx)) && (k.StartsWith("view.") || k.StartsWith("keep.") || k.StartsWith("appMax")));
            if (C(MK("preset"), "") == "") cfg[MK("preset.custom")] = string.Join("\u001F", cfg.Where(kv => mine(kv.Key)).Select(kv => kv.Key + "\u001E" + kv.Value));
            foreach (var k in cfg.Keys.Where(mine).ToList()) cfg.Remove(k);
            if (key == "")
            {
                foreach (var p in C(MK("preset.custom"), "").Split('\u001F')) { var kv = p.Split('\u001E'); if (kv.Length == 2) cfg[kv[0]] = kv[1]; }
                cfg.Remove(MK("preset"));
            }
            else
            {
                cfg[MK("preset")] = key;
                string[] show, keep = new string[0]; var views = new Dictionary<string, string>(); int apps = 0;
                if (key == "compact")
                {
                    show = new[] { "cpu", "ram", "net", "claude", "codex", "media", "event", "weather", "conn", "quick", "notif", "lang" };
                    foreach (var v in Variants) views[v] = "min"; views["clock"] = "compact";
                }
                else if (key == "info")
                {
                    show = new[] { "cpu", "ram", "claude", "codex", "media", "event", "weather", "conn", "quick", "lang" };
                    views["media"] = "full"; views["event"] = "full"; views["claude"] = "compact"; views["codex"] = "compact";
                    views["cpu"] = "compact"; views["ram"] = "compact"; views["weather"] = "compact"; views["clock"] = "compact";
                    keep = new[] { "media", "event" }; apps = 10;
                }
                else
                {
                    show = new[] { "media", "weather", "conn", "lang" };
                    views["media"] = "compact"; views["weather"] = "min"; views["clock"] = "compact"; apps = 6;
                }
                foreach (var kv in views) cfg[MK("view." + kv.Key)] = kv.Value;
                foreach (var k in keep) cfg[MK("keep." + k)] = "1";
                if (apps > 0) cfg[MK("appMax")] = apps.ToString();
                cfg[lk] = string.Join(",", WidgetDefs.Select(x => (show.Contains(x[0]) ? "" : "-") + x[0]));
            }
            SaveCfg(); RebuildAll(); RestyleWindows();
        }
        static readonly string[] ViewKeys = { "", "min", "compact", "full" };
        static readonly string[] ViewNames = { "Авто (по размеру панели)", "Компактный · S", "Обычный · M", "Расширенный · L" };

        // ---------- мелкие строительные блоки ----------
        static TextBlock L(string text, double size, bool bold = false, double op = 1)
        {
            // не мельче 10.5: вместо дальнейшего уменьшения букв модуль сокращает текст или меняет вид
            return new TextBlock { Text = text, FontSize = Math.Max(10.5, size), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Opacity = op, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        }
        static StackPanel HS(params UIElement[] els) { var s = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; foreach (var e in els) if (e != null) s.Children.Add(e); return s; }
        static StackPanel VS(params UIElement[] els) { var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; foreach (var e in els) if (e != null) s.Children.Add(e); return s; }
        static T Mg<T>(T e, double l, double t = 0, double r = 0, double b = 0) where T : FrameworkElement { e.Margin = new Thickness(l, t, r, b); return e; }
        static T Wd<T>(T e, double w) where T : FrameworkElement { e.Width = w; return e; }
        static void FgBind(FrameworkElement el, DependencyProperty p)
        {
            if (previewCfg != null) { el.SetValue(p, Frozen(Cur.Fg, 255)); return; }
            el.SetBinding(p, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Window), 1) });
        }
        public static Geometry ArcGeo(double cx, double cy, double r, double startDeg, double sweepDeg)
        {
            if (sweepDeg >= 359.9) return new EllipseGeometry(new Point(cx, cy), r, r);
            if (sweepDeg <= 0.01) return Geometry.Empty;
            double a0 = startDeg * Math.PI / 180, a1 = (startDeg + sweepDeg) * Math.PI / 180;
            var fig = new PathFigure { StartPoint = new Point(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0)) };
            fig.Segments.Add(new ArcSegment(new Point(cx + r * Math.Cos(a1), cy + r * Math.Sin(a1)), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true));
            var g = new PathGeometry(); g.Figures.Add(fig); return g;
        }
        public static string RateRu(double b)
        {
            if (double.IsNaN(b)) return "—";
            return b < 1048576 ? Math.Round(b / 1024).ToString(CultureInfo.CurrentCulture) + " КБ/с" : (b / 1048576).ToString(b < 10485760 ? "0.0" : "0", CultureInfo.CurrentCulture) + " МБ/с";
        }
        public static string Gb(double g) { return double.IsNaN(g) ? "—" : g.ToString(g < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " ГБ"; }

        // Плавный переход к новому значению за 180–300 мс: начинается от показанного сейчас, новые значения не встают в очередь.
        // При выключенных анимациях Windows значение ставится сразу.
        class Tw
        {
            static readonly List<Tw> run = new List<Tw>(); static bool hooked;
            readonly Action<double> apply; readonly double ms; double from, to; DateTime t0;
            public double Cur = double.NaN;
            public Tw(Action<double> apply, double ms = 240) { this.apply = apply; this.ms = ms; }
            public void Jump(double v) { run.Remove(this); Cur = v; apply(v); }
            public void To(double v)
            {
                bool going = run.Contains(this);
                if (going && v == to) return;
                if (double.IsNaN(Cur) || !Motion || (!going && v == Cur)) { Jump(v); return; }
                from = Cur; to = v; t0 = DateTime.Now;
                if (!going) run.Add(this);
                if (!hooked) { hooked = true; CompositionTarget.Rendering += Step; }
            }
            static void Step(object s, EventArgs e)
            {
                var now = DateTime.Now;
                foreach (var t in run.ToList())
                {
                    double p = Math.Min(1, (now - t.t0).TotalMilliseconds / t.ms);
                    t.Cur = t.from + (t.to - t.from) * (1 - Math.Pow(1 - p, 3));
                    try { t.apply(t.Cur); } catch { p = 1; }
                    if (p >= 1) run.Remove(t);
                }
                if (run.Count == 0) { hooked = false; CompositionTarget.Rendering -= Step; }
            }
        }
        // мягкая смена содержимого (трек, обложка): короткое проявление
        static void Soft(UIElement e) { if (e != null && Motion) e.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(260))); }

        // кольцо или дуга с подписью в центре
        class Gauge : Grid
        {
            readonly Path val; public readonly TextBlock Text; readonly double r, cx, start, sweep; readonly Tw tw;
            public bool Plain; // прогресс (не нагрузка): всегда цвет акцента
            public Brush Force; // свой цвет дуги (температура), иначе — по уровню
            public Gauge(double d, double thick, double start, double sweep, double font, string sub = null)
            {
                Width = d; Height = d; this.start = start; this.sweep = sweep; r = (d - thick) / 2; cx = d / 2;
                var track = new Path { StrokeThickness = thick, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Data = ArcGeo(cx, cx, r, start, sweep), Opacity = 0.18 };
                FgBind(track, Shape.StrokeProperty);
                val = new Path { StrokeThickness = thick, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Stroke = Accent };
                Text = new TextBlock { FontSize = font, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Children.Add(track); Children.Add(val);
                if (sub == null) Children.Add(Text);
                else
                {
                    // подпись «ЦП» / «ОЗУ» под числом, внутри кольца
                    var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    sp.Children.Add(Text); sp.Children.Add(new TextBlock { Text = sub, FontSize = Math.Max(7, font * 0.48), FontWeight = FontWeights.SemiBold, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) });
                    Children.Add(sp);
                }
                tw = new Tw(x => val.Data = ArcGeo(cx, cx, r, this.start, this.sweep * x));
            }
            public void Set(double frac, string text)
            {
                if (double.IsNaN(frac)) frac = 0; frac = Math.Max(0, Math.Min(1, frac));
                tw.To(frac); val.Stroke = Force ?? (Plain ? Accent : LevelBrush(frac * 100) ?? Accent);
                Text.Text = text;
            }
        }
        // мини-график: линия и мягкая заливка. Новая точка въезжает справа; шкала меняется плавно и редко (шаги 1-2-5).
        // Fixed > 0 — постоянная шкала (например, 100 %); Max — текущий верх шкалы, для подписи
        class Spark : Canvas
        {
            readonly Polyline line; readonly Polygon area; readonly TranslateTransform shift = new TranslateTransform();
            readonly Tw scale, slide; double[] vals = new double[0]; double lead = double.NaN;
            public double Fixed, Max = 1;
            public Spark(double w, double h, Color? color = null)
            {
                Width = w; Height = h; ClipToBounds = true;
                var ac = color ?? AccentColor;
                area = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x30, ac.R, ac.G, ac.B)), RenderTransform = shift };
                line = new Polyline { StrokeThickness = 1.4, Stroke = new SolidColorBrush(ac), StrokeLineJoin = PenLineJoin.Round, RenderTransform = shift };
                Children.Add(area); Children.Add(line);
                scale = new Tw(x => Draw(), 300); slide = new Tw(x => shift.X = x, 300);
            }
            public static double Nice(double v)
            {
                if (double.IsNaN(v) || v <= 1) return 1;
                double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
                foreach (var k in new[] { 1.0, 2, 5 }) if (k * p >= v) return k * p;
                return 10 * p;
            }
            public void Set(IList<double> v)
            {
                var nv = v.Select(x => double.IsNaN(x) ? 0 : x).ToArray();
                bool slid = nv.Length == vals.Length && nv.Length > 2;
                for (int i = 0; slid && i < nv.Length - 1; i++) slid = nv[i] == vals[i + 1];
                lead = slid ? vals[0] : double.NaN; vals = nv;
                double peak = vals.Length == 0 ? 0 : vals.Max(), m = Fixed > 0 ? Fixed : Nice(peak);
                if (Fixed <= 0 && m < Max && peak > Max * 0.4) m = Max; // шкала уменьшается, только когда пик заметно ниже: без скачков туда-сюда
                Max = m; scale.To(m); Draw();
                if (slid) { slide.Jump(Width / (nv.Length - 1)); slide.To(0); }
            }
            void Draw()
            {
                int n = vals.Length; double m = double.IsNaN(scale.Cur) || scale.Cur <= 0 ? Max : scale.Cur, step = n < 2 ? 0 : Width / (n - 1);
                Func<double, double> y = x => Height - 1 - (Height - 3) * Math.Min(1, x / m);
                var pts = new PointCollection();
                if (!double.IsNaN(lead) && n > 1) pts.Add(new Point(-step, y(lead))); // ушедшая точка — у левого края, пока ряд сдвигается
                for (int i = 0; i < n; i++) pts.Add(new Point(step * i, y(vals[i])));
                line.Points = pts;
                var poly = new PointCollection(pts); if (pts.Count > 0) { poly.Add(new Point(pts[pts.Count - 1].X, Height)); poly.Add(new Point(pts[0].X, Height)); }
                area.Points = poly;
            }
        }
        // столбики активности
        class Cols : StackPanel
        {
            readonly List<Rectangle> rs = new List<Rectangle>(); readonly List<Tw> tws = new List<Tw>(); readonly double h;
            public Cols(int n, double w, double h)
            {
                Orientation = Orientation.Horizontal; Height = h; this.h = h; VerticalAlignment = VerticalAlignment.Center;
                for (int i = 0; i < n; i++)
                {
                    var r = new Rectangle { Width = w, RadiusX = w / 2, RadiusY = w / 2, Fill = Accent, Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
                    rs.Add(r); Children.Add(r); tws.Add(new Tw(x => r.Height = x));
                }
            }
            public void Set(IList<double> v, int current)
            {
                double max = Math.Max(1, v.Count == 0 ? 1 : v.Max());
                for (int i = 0; i < rs.Count; i++) { double x = i < v.Count ? v[i] : 0; tws[i].To(Math.Max(2, h * x / max)); rs[i].Opacity = i == current ? 1 : 0.5; }
            }
        }
        // полоса заполнения; now — без перехода (перемотка пальцем)
        class FillBar : Grid
        {
            readonly Rectangle fill; readonly double h; readonly Tw tw; double w, last;
            public FillBar(double w, double h)
            {
                Width = w; Height = h; this.w = w; this.h = h; VerticalAlignment = VerticalAlignment.Center; HorizontalAlignment = HorizontalAlignment.Left;
                var t = new Rectangle { RadiusX = h / 2, RadiusY = h / 2, Opacity = 0.18 }; FgBind(t, Shape.FillProperty);
                fill = new Rectangle { RadiusX = h / 2, RadiusY = h / 2, HorizontalAlignment = HorizontalAlignment.Left, Fill = Accent, Width = 0 };
                Children.Add(t); Children.Add(fill);
                tw = new Tw(x => { last = x; fill.Width = x <= 0.001 ? 0 : Math.Max(h, this.w * x); });
            }
            // пауза: полоса «сжимается» — становится тоньше и бледнее; воспроизведение — распрямляется
            bool squeezed;
            public void Squeeze(bool on)
            {
                if (on == squeezed) return; squeezed = on;
                var st = RenderTransform as ScaleTransform; if (st == null) { st = new ScaleTransform(1, 1); RenderTransform = st; RenderTransformOrigin = new Point(0.5, 0.5); }
                var dur = TimeSpan.FromMilliseconds(Motion ? 220 : 0); var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(on ? 0.45 : 1, dur) { EasingFunction = ease });
                st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(on ? 0.97 : 1, dur) { EasingFunction = ease });
                BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 0.6 : 1, dur));
            }
            // ширина по месту (полоса во всю ширину модуля)
            public void Resize(double nw) { if (nw <= h || Math.Abs(nw - w) < 0.5) return; w = nw; Width = nw; fill.Width = last <= 0.001 ? 0 : Math.Max(h, w * last); }
            public void Set(double frac, Brush b = null, bool now = false)
            {
                frac = double.IsNaN(frac) ? 0 : Math.Max(0, Math.Min(1, frac));
                if (now) tw.Jump(frac); else tw.To(frac);
                fill.Fill = b ?? LevelBrush(frac * 100) ?? Accent;
            }
        }
        // аналоговый циферблат: часовая и минутная стрелки, метки 12/3/6/9
        class Dial : Canvas
        {
            readonly RotateTransform hr = new RotateTransform(), mr = new RotateTransform();
            public Dial(double d)
            {
                Width = d; Height = d; VerticalAlignment = VerticalAlignment.Center; double c = d / 2;
                var face = new Ellipse { Width = d, Height = d, StrokeThickness = 1.3, Opacity = 0.5 }; FgBind(face, Shape.StrokeProperty); Children.Add(face);
                for (int i = 0; i < 4; i++)
                {
                    double a = i * Math.PI / 2;
                    var tk = new Line { X1 = c + Math.Cos(a) * (c - 2.5), Y1 = c + Math.Sin(a) * (c - 2.5), X2 = c + Math.Cos(a) * (c - 5.5), Y2 = c + Math.Sin(a) * (c - 5.5), StrokeThickness = 1.3, Opacity = 0.6 };
                    FgBind(tk, Shape.StrokeProperty); Children.Add(tk);
                }
                hr.CenterX = mr.CenterX = c; hr.CenterY = mr.CenterY = c;
                var hh = new Line { X1 = c, Y1 = c, X2 = c, Y2 = c - d * 0.25, StrokeThickness = 2.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = hr };
                FgBind(hh, Shape.StrokeProperty);
                var mh = new Line { X1 = c, Y1 = c, X2 = c, Y2 = c - d * 0.37, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = mr, Stroke = Accent };
                var pin = new Ellipse { Width = 3.6, Height = 3.6, Fill = Accent }; Canvas.SetLeft(pin, c - 1.8); Canvas.SetTop(pin, c - 1.8);
                Children.Add(hh); Children.Add(mh); Children.Add(pin);
            }
            public void Set(DateTime t) { mr.Angle = t.Minute * 6 + t.Second * 0.1; hr.Angle = (t.Hour % 12) * 30 + t.Minute * 0.5; }
        }
        static readonly string[][] Zones = {
            new[] { "", "Нет" }, new[] { "GMT Standard Time", "Лондон" }, new[] { "W. Europe Standard Time", "Берлин" }, new[] { "Russian Standard Time", "Москва" },
            new[] { "Arabian Standard Time", "Дубай" }, new[] { "China Standard Time", "Пекин" }, new[] { "Tokyo Standard Time", "Токио" },
            new[] { "Eastern Standard Time", "Нью-Йорк" }, new[] { "Pacific Standard Time", "Лос-Анджелес" } };
        public static double NiceTop(double v) { return Spark.Nice(v); }
        // Общий вид ползунков (громкость, яркость, отступы, прозрачность): тонкая дорожка, пройденная часть — акцент, круглая ручка с точкой акцента, как в Windows 11
        static Style sliderSty; static string sliderKey;
        internal static Style SliderSty()
        {
            Color a = AccentColor, f = CardFg; string acc = "#" + a.R.ToString("X2") + a.G.ToString("X2") + a.B.ToString("X2"), tr = "#38" + f.R.ToString("X2") + f.G.ToString("X2") + f.B.ToString("X2");
            if (sliderSty != null && sliderKey == acc + tr) return sliderSty;
            var x = "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'>" +
                "<Setter Property='Height' Value='26'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'><Grid Background='Transparent'>" +
                "<Border Height='4' CornerRadius='2' Background='" + tr + "' VerticalAlignment='Center' Margin='2,0'/>" +
                "<Track x:Name='PART_Track'>" +
                "<Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False' IsTabStop='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'><Border Height='4' CornerRadius='2' Background='" + acc + "' VerticalAlignment='Center' Margin='2,0,-9,0'/></Border></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>" +
                "<Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False' IsTabStop='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>" +
                "<Track.Thumb><Thumb Width='20' Height='20' Cursor='Hand'><Thumb.Template><ControlTemplate TargetType='Thumb'><Grid>" +
                "<Ellipse x:Name='o' Fill='White' Stroke='#33000000' StrokeThickness='1' Margin='1'><Ellipse.Effect><DropShadowEffect BlurRadius='5' ShadowDepth='1' Opacity='0.35'/></Ellipse.Effect></Ellipse>" +
                "<Ellipse x:Name='i' Width='8' Height='8' Fill='" + acc + "'/></Grid>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='i' Property='Width' Value='11'/><Setter TargetName='i' Property='Height' Value='11'/></Trigger>" +
                "<Trigger Property='IsDragging' Value='True'><Setter TargetName='i' Property='Width' Value='7'/><Setter TargetName='i' Property='Height' Value='7'/><Setter TargetName='o' Property='Margin' Value='0'/></Trigger></ControlTemplate.Triggers>" +
                "</ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></Grid>" +
                "<ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>";
            sliderSty = (Style)System.Windows.Markup.XamlReader.Parse(x); sliderKey = acc + tr; return sliderSty;
        }
        static void VolBeep() { if (C("volBeep", "1") == "1" && !live.Muted && live.Vol > 0) Audio.Beep(); }
        // пункты «Устройство вывода» — в меню модуля громкости
        static void DeviceItems(ItemsControl m)
        {
            foreach (var dv in Audio.Devices()) { var x = dv; m.Items.Add(Item(ShortDev(x.Item2), () => { var err = Audio.SetDefault(x.Item1); if (err != null) MessageBox.Show(err, "FlowBar"); ReadVolume(); All(b => b.Render()); }, x.Item3)); }
        }
        static void ReadVolume() { double vv; bool mu; string vn; Audio.Read(out vv, out mu, out vn); live.Vol = vv; live.Muted = mu; live.VolName = vn; }
        // «Динамики (Realtek(R) Audio)» → «Динамики»: полное имя — в подсказке и карточке
        public static string ShortDev(string n) { if (string.IsNullOrEmpty(n)) return "Устройство вывода"; int i = n.IndexOf(" ("); return i > 0 ? n.Substring(0, i) : n; }
        // График по реальному времени: окно span до «сейчас»; до первого измерения — пусто, разрыв дольше gap (сон, сбой датчика) не соединяется линией
        class TimeChart : Canvas
        {
            readonly Path line = new Path { StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round }; readonly double lo, hi;
            public TimeChart(double w, double h, double lo, double hi)
            {
                Width = w; Height = h; ClipToBounds = true; this.lo = lo; this.hi = hi; line.Stroke = Accent;
                var axis = FgShape(new Rectangle { Width = w, Height = 1 }, 0.18); Canvas.SetTop(axis, h - 1); Children.Add(axis); Children.Add(line);
            }
            public void Set(IList<double> v, IList<DateTime> t, TimeSpan span, TimeSpan gap)
            {
                var g = new PathGeometry(); PathFigure fig = null; var now = DateTime.Now; var prev = DateTime.MinValue;
                for (int i = 0; i < v.Count && i < t.Count; i++)
                {
                    double age = (now - t[i]).TotalSeconds; if (age > span.TotalSeconds || age < 0) continue;
                    var p = new Point(Width * (1 - age / span.TotalSeconds), Height - 2 - (Height - 4) * Math.Max(0, Math.Min(1, (v[i] - lo) / (hi - lo))));
                    if (fig == null || t[i] - prev > gap) { fig = new PathFigure { StartPoint = p }; g.Figures.Add(fig); fig.Segments.Add(new LineSegment(new Point(p.X + 0.01, p.Y), true)); } // одиночная точка тоже видна
                    else fig.Segments.Add(new LineSegment(p, true));
                    prev = t[i];
                }
                line.Data = g;
            }
        }
        public static string Ago(TimeSpan t) { return t.TotalSeconds < 60 ? Math.Max(1, (int)t.TotalSeconds) + " с" : (int)t.TotalMinutes + " мин" + (t.TotalMinutes < 10 && t.Seconds > 0 ? " " + t.Seconds + " с" : ""); }
        // сетка точек: доля реального периода — по точке на час, день или неделю
        class Dots : Canvas
        {
            readonly int rows; readonly double d, gap; int built = -1; readonly List<Ellipse> es = new List<Ellipse>();
            public Dots(int rows, double d, double gap) { this.rows = rows; this.d = d; this.gap = gap; VerticalAlignment = VerticalAlignment.Center; HorizontalAlignment = HorizontalAlignment.Left; }
            public void Set(int total, int done)
            {
                if (total != built)
                {
                    built = total; Children.Clear(); es.Clear(); int cols = (total + rows - 1) / rows;
                    Width = cols * (d + gap) - gap; Height = rows * (d + gap) - gap;
                    for (int i = 0; i < total; i++)
                    {
                        var e = new Ellipse { Width = d, Height = d }; FgBind(e, Shape.FillProperty);
                        Canvas.SetLeft(e, (i % cols) * (d + gap)); Canvas.SetTop(e, (i / cols) * (d + gap)); es.Add(e); Children.Add(e);
                    }
                }
                for (int i = 0; i < es.Count; i++) es[i].Opacity = i < done ? 0.95 : i == done ? 0.5 : 0.16;
            }
        }
        // перекидные цифры: каждая на своей плашке; сменившаяся переворачивается
        class FlipClock : StackPanel
        {
            readonly List<TextBlock> ds = new List<TextBlock>(); readonly List<ScaleTransform> sts = new List<ScaleTransform>();
            public FlipClock(double h)
            {
                Orientation = Orientation.Horizontal; VerticalAlignment = VerticalAlignment.Center; HorizontalAlignment = HorizontalAlignment.Center; double font = Math.Max(10.5, h * 0.7);
                for (int i = 0; i < 4; i++)
                {
                    if (i == 2) Children.Add(new TextBlock { Text = ":", FontSize = font, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 2), Opacity = 0.7 });
                    var t = new TextBlock { FontSize = font, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    var st = new ScaleTransform();
                    var cell = new Grid { Width = font * 0.78, Height = h, Margin = new Thickness(i == 1 || i == 3 ? 2 : 0, 0, 0, 0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = st };
                    cell.Children.Add(FgShape(new Rectangle { RadiusX = h * 0.22, RadiusY = h * 0.22 }, 0.13)); cell.Children.Add(t);
                    Children.Add(cell); ds.Add(t); sts.Add(st);
                }
            }
            public void Set(string hhmm)
            {
                for (int i = 0; i < 4 && i < hhmm.Length; i++)
                {
                    var c = hhmm[i].ToString(); if (ds[i].Text == c) continue;
                    bool first = ds[i].Text == ""; ds[i].Text = c;
                    if (!first && Motion) sts[i].BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                }
            }
        }
        // строки модуля Claude: проценты — от Claude Code через строку состояния, токены — по локальным журналам
        static readonly string[][] ClRows = {
            new[] { "5h", "5-часовой лимит (сессия), %", "5 часов" }, new[] { "week", "Недельный лимит, %", "Неделя" }, new[] { "ctx", "Контекст текущей сессии, %", "Контекст" },
            new[] { "tok5", "Токены за 5 часов", "Ток. 5 ч" }, new[] { "tokw", "Токены за 7 дней", "Ток. 7 дн" } };
        static bool IsPctRow(string k) { return k == "5h" || k == "week" || k == "ctx" || k == "c5" || k == "cw"; }
        static List<string> ClSel
        {
            get
            {
                var l = C("cl.rows", File.Exists(ClaudeBridge.DataPath) ? "5h,week" : "tok5,tokw").Split(',').Where(x => ClRows.Any(r => r[0] == x)).ToList();
                return l.Count > 0 ? l : new List<string> { "tok5" };
            }
        }
        static void SetClRow(string k, bool on)
        {
            var l = ClSel; if (on && !l.Contains(k)) l.Add(k); if (!on && l.Count > 1) l.Remove(k);
            cfg["cl.rows"] = string.Join(",", ClRows.Select(r => r[0]).Where(l.Contains)); SaveCfg(); RebuildAll(); RestyleWindows();
        }
        static string RowName(string k) { if (k == "c5") return "Сессия"; if (k == "cw") return "Неделя"; var r = ClRows.FirstOrDefault(x => x[0] == k); return r == null ? k : r[2]; }
        // значение строки: процент (NaN — нет), текст, время до сброса
        static Tuple<double, string, string> RowVal(Snap s, string k)
        {
            switch (k)
            {
                case "5h": return Tuple.Create(s.ClP5, CodexLimits.Pct(s.ClP5), s.ClR5);
                case "week": return Tuple.Create(s.ClPW, CodexLimits.Pct(s.ClPW), s.ClRW);
                case "ctx": return Tuple.Create(s.ClCtx, CodexLimits.Pct(s.ClCtx), "сессия");
                case "tok5": return Tuple.Create(double.NaN, s.ClS == "—" ? "—" : s.ClS + " ток.", s.ClReset);
                case "tokw": return Tuple.Create(double.NaN, s.ClW == "—" ? "—" : s.ClW + " ток.", "7 дн.");
                case "c5": return Tuple.Create(s.Cx5, CodexLimits.Pct(s.Cx5), s.Cx5Reset);
                default: return Tuple.Create(s.CxW, CodexLimits.Pct(s.CxW), s.CxWReset);
            }
        }
        static void Warn(TextBlock t, double pct) { var b = LevelBrush(pct); if (b != null) t.Foreground = b; else t.ClearValue(TextBlock.ForegroundProperty); }
        // значок батареи: корпус, заполнение и молния зарядки
        class BatIcon : Grid
        {
            readonly Rectangle fill; readonly TextBlock bolt; readonly double iw;
            public BatIcon(double w, double h)
            {
                Width = w + 3; Height = h; iw = w - 4; VerticalAlignment = VerticalAlignment.Center;
                var body = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(h / 4), BorderThickness = new Thickness(1.3), HorizontalAlignment = HorizontalAlignment.Left };
                body.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Window), 1) });
                var cap = new Rectangle { Width = 2, Height = h / 2.5, RadiusX = 1, RadiusY = 1, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
                FgBind(cap, Shape.FillProperty);
                fill = new Rectangle { Height = h - 5, RadiusX = 1.5, RadiusY = 1.5, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2.5, 0, 0, 0), Fill = Accent };
                bolt = new TextBlock { Text = "", FontFamily = Icons, FontSize = h * 0.8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0), Visibility = Visibility.Collapsed };
                Children.Add(body); Children.Add(cap); Children.Add(fill); Children.Add(bolt);
            }
            public void Set(double frac, bool charging)
            {
                if (double.IsNaN(frac)) frac = 0;
                fill.Width = Math.Max(1, (iw - 1) * Math.Max(0, Math.Min(1, frac)));
                fill.Fill = frac < 0.15 && !charging ? new SolidColorBrush(Color.FromRgb(0xF0, 0x50, 0x50)) : Accent;
                bolt.Visibility = charging ? Visibility.Visible : Visibility.Collapsed; fill.Opacity = charging ? 0.55 : 1;
            }
        }

        // круглая кнопка внутри модуля: нажатие не доходит до модуля
        static Press RoundBtn(string glyph, double d, bool primary, Action a, string tip, Color? col = null)
        {
            var g = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = Math.Round(d * 0.42), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var ring = new SolidColorBrush(Colors.Transparent);
            var b = new Press { Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Cursor = Cursors.Hand, ToolTip = tip, Child = g, VerticalAlignment = VerticalAlignment.Center,
                BorderBrush = ring, BorderThickness = new Thickness(1) };
            Action invoke = () =>
            {
                if (a == null) return;
                if (Motion && !b.IsKeyboardFocused)
                    ring.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0xD8, 255, 255, 255), Colors.Transparent, TimeSpan.FromMilliseconds(220))
                    { FillBehavior = FillBehavior.Stop });
                a();
            };
            b.Invoke = a == null ? null : invoke;
            b.Base = primary ? col ?? AccentColor : Colors.Transparent; b.Background = new SolidColorBrush(b.Base);
            if (primary) g.Foreground = Frozen(Sty.Lum(b.Base) > 150 ? Color.FromRgb(0x10, 0x10, 0x10) : Colors.White, 255);
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            b.MouseEnter += (s, e) => Fade(b, primary ? Color.FromArgb(0xD8, b.Base.R, b.Base.G, b.Base.B) : Fa(0x22), 110);
            b.MouseLeave += (s, e) => Fade(b, b.Base, 130);
            b.PreviewMouseLeftButtonDown += (s, e) => e.Handled = false;
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; invoke(); };
            return b;
        }

        // ---------- данные для модулей: собирает основная панель, рисуют все ----------
        public class Snap
        {
            public DateTime Now = DateTime.Now;
            public double Cpu = double.NaN, Ram = double.NaN, RamUsed = double.NaN, RamTotal = double.NaN, Rx = double.NaN, Tx = double.NaN, Temp = double.NaN;
            public string TempNote; public int Threads = Environment.ProcessorCount;
            public List<double> NetHist = new List<double>(), CpuHist = new List<double>(), RamHist = new List<double>(), RxHist = new List<double>(), TxHist = new List<double>();
            public string Disk = "C:", DiskLabel = ""; public double DiskFree = double.NaN, DiskTotal = double.NaN;
            public List<Tuple<string, string, double, double>> Disks = new List<Tuple<string, string, double, double>>();
            public bool HasBat, Charging; public double Bat = double.NaN; public int BatSec = -1;
            public bool HasMedia, Playing, Remote; public string Title, Artist, App; public ImageSource Cover; public double Pos = double.NaN, Dur = double.NaN;
            public string NextTodo; public int TodoDone, TodoTotal;
            public string Note;
            public int PomoMode, PomoRound; public TimeSpan PomoLeft = TimeSpan.FromMinutes(25);
            public int ScreenSec; public int[] Hours = new int[24];
            public CalEvent Next;
            public bool WxOk, WxStale; public string WxKind = "cloud", WxTemp = "—", WxCity, WxDesc, WxRange, WxAge;
            public bool CanPrev, CanNext, CanToggle = true, CanSeek; public string MediaError; public List<Tuple<string, string>> Sessions = new List<Tuple<string, string>>();
            public string Lang = "…";
            // подключение: вид −1 — ещё не проверено, 0 — нет, 1 — кабель, 2 — Wi-Fi, 3 — сотовая; доступ 0 — нет, 1 — только локальная сеть, 2 — ограничен, 3 — интернет
            public int NetKind = -1, NetLevel, NetBars = -1; public string NetName; public bool NetVpn;
            public double Vol = double.NaN; public bool Muted; public string VolName;
            public List<double> TempHist = new List<double>(); public List<DateTime> TempTimes = new List<DateTime>(); public List<Tuple<string, double>> TempZones = new List<Tuple<string, double>>();
            public string ToolLabel, ToolTime;
            public long BinItems = -1, BinBytes;
            public bool ClHas; public string ClS = "—", ClW = "—", ClReset = "—";
            public bool ClBridge, ClPctStale; public double ClP5 = double.NaN, ClPW = double.NaN, ClCtx = double.NaN; public string ClR5 = "—", ClRW = "—", ClPctSeen;
            public bool CxHas, CxStale, CxData; public double Cx5 = double.NaN, CxW = double.NaN; public string Cx5Reset = "—", CxWReset = "—", CxSeen;
        }
        static Snap live = DemoMode ? DemoFixed() : new Snap();
        // данные примера для сверки с эталоном: не меняются, пока работает режим --demo
        static Snap DemoFixed()
        {
            var s = Demo(); s.ClBridge = true; s.ClP5 = 34; s.ClR5 = "2ч 13м"; s.ClPW = 18; s.CxData = true; s.Cx5 = 48; s.CxW = 21; s.Cx5Reset = "3ч 5м"; s.CxWReset = "4д 6ч";
            s.DiskFree = 21; s.DiskTotal = 281; s.Cpu = 36; s.Ram = 73; s.Temp = 73; // числа образца target-widgets s.ScreenSec = 5 * 3600 + 48 * 60; s.Title = "Golden Hour Drive"; s.Artist = "Nova Lane"; s.Pos = 70; s.Dur = 214;
            return s;
        }
        // правдоподобные числа для превью в каталоге — там они подписаны «пример»
        static Snap Demo()
        {
            var r = new Random(7); var s = new Snap { Cpu = 23, Ram = 61, RamUsed = 9.8, RamTotal = 16, Rx = 1.8 * 1048576, Tx = 240 * 1024, Temp = 54, TempNote = "пример",
                Disk = "C:", DiskLabel = "Локальный диск", DiskFree = 182, DiskTotal = 476, HasBat = true, Bat = 0.76, Charging = true, BatSec = -1,
                HasMedia = true, Playing = true, Title = "Северное сияние", Artist = "Ансамбль «Пример»", Pos = 83, Dur = 214,
                NextTodo = "Подготовить отчёт для команды", TodoDone = 3, TodoTotal = 7, Note = "Купить батарейки для мыши",
                PomoMode = 1, PomoRound = 2, PomoLeft = new TimeSpan(0, 18, 42), ScreenSec = 4 * 3600 + 25 * 60,
                Next = new CalEvent { Title = "Планёрка с командой", Start = DateTime.Now.AddMinutes(25), End = DateTime.Now.AddMinutes(55) },
                WxOk = true, WxKind = "partly", WxTemp = "+7°", WxCity = "Москва", WxDesc = "переменная облачность", WxRange = "↑9° ↓2°", Lang = "RU",
                NetKind = 2, NetLevel = 3, NetBars = 4, NetName = "Домашняя сеть", Vol = 0.64, VolName = "Динамики", ClHas = true, ClS = "1.4M", ClW = "9.2M", ClReset = "2ч 10м", ClBridge = true, ClP5 = 42, ClPW = 18, ClCtx = 37, ClR5 = "2ч 10м", ClRW = "4д 2ч", CxHas = true, Cx5 = 34, CxW = 12, Cx5Reset = "3ч 5м", CxWReset = "4д 6ч", BinItems = 12 };
            for (int i = 0; i < 30; i++) { s.NetHist.Add(r.Next(2, 30) * 65536.0); s.CpuHist.Add(r.Next(5, 60)); s.RamHist.Add(58 + r.Next(0, 6)); s.RxHist.Add(r.Next(2, 26) * 65536.0); s.TxHist.Add(r.Next(1, 6) * 65536.0); }
            for (int h = 0; h < 24; h++) s.Hours[h] = h >= 8 && h <= 18 ? r.Next(600, 3600) : 0;
            for (int i = 0; i < 40; i++) { s.TempHist.Add(50 + 6 * Math.Sin(i / 5.0) + r.Next(0, 3)); s.TempTimes.Add(DateTime.Now.AddSeconds(-15 * (40 - i))); }
            s.TempZones.Add(Tuple.Create("Зона ACPI TZ00", 54.0)); s.TempZones.Add(Tuple.Create("Зона ACPI TZ01", 41.0));
            return s;
        }
        static void Push(List<double> l, double v, int cap = 30) { l.Add(v); while (l.Count > cap) l.RemoveAt(0); } // 30 точек — график на панели, 120 — история в карточке (2 минуты)

        static long pIdle, pKernel, pUser, pRx, pTx; static DateTime pNet;
        static MC.GlobalSystemMediaTransportControlsSessionManager mediaMgr; static bool mediaBusy; static string lastMedia;
        static Weather wx; static bool wxBusy; static string wxError; static DateTime wxTried; // wxTried — время последней попытки загрузки
        static int pomoMode, pomoRound; static DateTime pomoEnd; // 0 — стоп, 1 — фокус, 2 — перерыв
        static bool tempBusy;

        void Collect()
        {
            if (DemoMode) return; // пример: данные не собираются — между снимками ничего не меняется
            var s = live; var now = DateTime.Now; s.Now = now;
            long i, k, u;
            if (N.GetSystemTimes(out i, out k, out u))
            {
                long di = i - pIdle, dt = (k - pKernel) + (u - pUser); // kernel включает idle
                if (pKernel != 0 && dt > 0) s.Cpu = 100.0 * (dt - di) / dt;
                pIdle = i; pKernel = k; pUser = u;
            }
            Push(s.CpuHist, double.IsNaN(s.Cpu) ? 0 : s.Cpu, 120);
            var mem = new N.MEMSTAT { Len = (uint)Marshal.SizeOf(typeof(N.MEMSTAT)) };
            if (N.GlobalMemoryStatusEx(ref mem)) { s.Ram = mem.Load; s.RamTotal = mem.TotalPhys / 1073741824.0; s.RamUsed = (mem.TotalPhys - mem.AvailPhys) / 1073741824.0; }
            Push(s.RamHist, double.IsNaN(s.Ram) ? 0 : s.Ram, 120);
            long rx = 0, tx = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                { var st = ni.GetIPStatistics(); rx += st.BytesReceived; tx += st.BytesSent; }
            if (pNet != default(DateTime)) { double sec = Math.Max(0.1, (now - pNet).TotalSeconds); s.Rx = Math.Max(0, rx - pRx) / sec; s.Tx = Math.Max(0, tx - pTx) / sec; Push(s.NetHist, s.Rx + s.Tx); Push(s.RxHist, s.Rx, 120); Push(s.TxHist, s.Tx, 120); }
            pRx = rx; pTx = tx; pNet = now;

            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            s.HasBat = (ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) == 0 && ps.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.Unknown;
            s.Bat = ps.BatteryLifePercent; s.Charging = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online; s.BatSec = ps.BatteryLifeRemaining;

            if (ticks % 30 == 1)
            {
                var list = new List<Tuple<string, string, double, double>>();
                var sys = System.IO.Path.GetPathRoot(Environment.SystemDirectory);
                foreach (var d in DriveInfo.GetDrives())
                    try { if (d.DriveType == DriveType.Fixed && d.IsReady) list.Add(Tuple.Create(d.Name.TrimEnd('\\'), d.VolumeLabel, d.AvailableFreeSpace / 1073741824.0, d.TotalSize / 1073741824.0)); } catch { }
                s.Disks = list;
                var main = list.FirstOrDefault(d => string.Equals(d.Item1 + "\\", sys, StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault();
                if (main != null) { s.Disk = main.Item1; s.DiskLabel = main.Item2; s.DiskFree = main.Item3; s.DiskTotal = main.Item4; }
            }
            if (ticks % 15 == 2) ReadTemp();

            uint pid; uint tid = N.GetWindowThreadProcessId(N.GetForegroundWindow(), out pid);
            try { s.Lang = new CultureInfo((int)(N.GetKeyboardLayout(tid).ToInt64() & 0xFFFF)).TwoLetterISOLanguageName.ToUpperInvariant(); } catch { s.Lang = "?"; }

            TrackScreen(); s.ScreenSec = screen.Total; Array.Copy(screen.Hours, s.Hours, 24);
            Pomo(); s.PomoMode = pomoMode; s.PomoRound = pomoRound; s.PomoLeft = pomoMode == 0 ? TimeSpan.FromMinutes(25) : pomoEnd - now;
            var nt = NextDueTodo(); s.NextTodo = nt == null ? null : nt.Text; s.TodoDone = todos.Count(t => t.Done); s.TodoTotal = todos.Count;
            if (ticks % 10 == 5 || s.Note == null)
            {
                var text = SafeFile.Read(F("notes.txt")) ?? "";
                s.Note = text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l != "") ?? "";
            }
            UpdateTools(); if (ticks % 10 == 4) UpdateBin();
            if (ticks % 10 == 0 || ticks < 3) CollectAi();
            s.Next = calEvents.Where(e => !e.AllDay && e.End > now && e.Start < now.AddHours(24)).OrderBy(e => e.Start).FirstOrDefault();
            if (ticks % 5 == 0) CheckMeeting();
            if (connDirty || ticks % 10 == 3) { connDirty = false; ReadConn(); }
            { double vv; bool mu; string vn; Audio.Read(out vv, out mu, out vn); s.Vol = vv; s.Muted = mu; s.VolName = vn; }
            if (ticks % 15 == 5 && !double.IsNaN(s.Temp)) { Push(s.TempHist, s.Temp, 40); s.TempTimes.Add(now); while (s.TempTimes.Count > 40) s.TempTimes.RemoveAt(0); } // точка каждые 15 с со временем измерения; сбой датчика — пропуск, а не ноль
            CollectWeather();
            UpdateMedia();
        }

        // Подключение — как значок сети в панели Windows: профиль интернета Windows, при его отсутствии — работающие адаптеры
        static bool connBusy, connHooked; static volatile bool connDirty = true;
        static void ReadConn()
        {
            if (!connHooked) { connHooked = true; try { NC.NetworkInformation.NetworkStatusChanged += o => connDirty = true; } catch { } } // смена сети видна через секунду
            if (connBusy) return; connBusy = true;
            Task.Run(() =>
            {
                int kind = 0, level = 0, bars = -1; string name = null;
                try
                {
                    var p = NC.NetworkInformation.GetInternetConnectionProfile();
                    if (p != null)
                    {
                        var lv = p.GetNetworkConnectivityLevel();
                        level = lv == NC.NetworkConnectivityLevel.InternetAccess ? 3 : lv == NC.NetworkConnectivityLevel.ConstrainedInternetAccess ? 2 : lv == NC.NetworkConnectivityLevel.LocalAccess ? 1 : 0;
                        kind = p.IsWlanConnectionProfile ? 2 : p.IsWwanConnectionProfile ? 3 : 1;
                        try { name = kind == 2 ? p.WlanConnectionProfileDetails.GetConnectedSsid() : p.ProfileName; } catch { }
                        try { var sb = p.GetSignalBars(); if (sb.HasValue) bars = sb.Value; } catch { }
                    }
                }
                catch { }
                // VPN (Happ, Cloudflare WARP, WireGuard…): профилем интернета становится адаптер VPN — Wi-Fi показывался как Ethernet, а доступ как «без интернета».
                // Показываем настоящее подключение (кабель или Wi-Fi) и отдельно отметку VPN; доступ — лучший из всех профилей.
                bool vpn = false;
                try
                {
                    NetworkInterface phys = null;
                    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                        if (IsVpnAdapter(ni.NetworkInterfaceType, ni.Description + " " + ni.Name)) { vpn = true; continue; }
                        if (IsVirtualAdapter(ni.Description)) continue;
                        bool gw = false; try { gw = ni.GetIPProperties().GatewayAddresses.Any(g => g.Address != null && !g.Address.Equals(System.Net.IPAddress.Any)); } catch { }
                        if (gw && phys == null) phys = ni;
                    }
                    if (vpn && phys != null)
                    {
                        kind = phys.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 2 : 1; name = phys.Name; bars = -1;
                        foreach (var q in NC.NetworkInformation.GetConnectionProfiles())
                        {
                            var ql = q.GetNetworkConnectivityLevel();
                            level = Math.Max(level, ql == NC.NetworkConnectivityLevel.InternetAccess ? 3 : ql == NC.NetworkConnectivityLevel.ConstrainedInternetAccess ? 2 : ql == NC.NetworkConnectivityLevel.LocalAccess ? 1 : 0);
                            if (kind == 2 && q.IsWlanConnectionProfile && ql != NC.NetworkConnectivityLevel.None) { try { name = q.WlanConnectionProfileDetails.GetConnectedSsid(); var sb = q.GetSignalBars(); if (sb.HasValue) bars = sb.Value; } catch { } }
                        }
                    }
                }
                catch { }
                // проверка Windows (NCSI) иногда опаздывает: сеть есть, а «без интернета» — раз в 10 с спрашиваем сами, ждём не дольше 3 с
                if (kind > 0 && level < 3)
                    try
                    {
                        var rq = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("http://www.msftconnecttest.com/connecttest.txt"); rq.Timeout = 3000; rq.ReadWriteTimeout = 3000; rq.AllowAutoRedirect = false;
                        using (var rs = (System.Net.HttpWebResponse)rq.GetResponse()) if ((int)rs.StatusCode == 200) level = 3;
                    }
                    catch { }
                live.NetVpn = vpn;
                if (kind == 0 || level == 0)
                    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                        bool gw = false; try { gw = ni.GetIPProperties().GatewayAddresses.Any(g => g.Address != null && !g.Address.Equals(System.Net.IPAddress.Any)); } catch { }
                        if (!gw) continue; // адаптеры виртуальных машин и VPN без шлюза не считаем подключением
                        if (kind == 0) { kind = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 2 : 1; name = ni.Name; }
                        level = Math.Max(level, 1); break;
                    }
                live.NetKind = kind; live.NetLevel = kind == 0 ? 0 : level; live.NetBars = bars; live.NetName = name; connBusy = false;
            });
        }
        // адаптер VPN: туннель, PPP или известные драйверы (Happ и sing-box — wintun; WARP; WireGuard; OpenVPN — TAP)
        public static bool IsVpnAdapter(NetworkInterfaceType t, string text)
        {
            if (t == NetworkInterfaceType.Ppp) return true;
            return System.Text.RegularExpressions.Regex.IsMatch(text ?? "", @"wintun|wireguard|\btap\b|tap-windows|\bvpn\b|openvpn|cloudflare|warp|sing-?box|happ|tailscale|zerotier|nordlynx|fortinet|anyconnect|outline|amnezia|v2ray|xray|tun2", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        // виртуальные адаптеры, которые не подключение и не VPN: виртуальные машины, WSL
        public static bool IsVirtualAdapter(string desc) { return System.Text.RegularExpressions.Regex.IsMatch(desc ?? "", @"hyper-v|vethernet|vmware|virtualbox|virtual switch|wsl|bluetooth", System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
        public static string ConnKind(int kind) { return kind == 1 ? "Ethernet" : kind == 2 ? "Wi-Fi" : kind == 3 ? "Сотовая сеть" : kind == 0 ? "Нет подключения" : "Проверка сети…"; }
        public static string ConnAccess(int kind, int level) { return kind < 0 ? "проверка…" : kind == 0 ? "нет сети" : level >= 3 ? "есть интернет" : level == 2 ? "интернет ограничен" : "без интернета"; }

        static void ReadTemp()
        {
            if (tempBusy) return; tempBusy = true;
            Task.Run(() =>
            {
                double best = double.NaN; string note = "датчик ACPI"; var zones = new List<Tuple<string, double>>();
                try
                {
                    using (var q = new System.Management.ManagementObjectSearcher(@"root\cimv2", "SELECT Name, HighPrecisionTemperature, Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation"))
                        foreach (System.Management.ManagementObject o in q.Get())
                        {
                            double kel = Convert.ToDouble(o["HighPrecisionTemperature"] ?? 0) / 10; if (kel <= 0) kel = Convert.ToDouble(o["Temperature"] ?? 0);
                            double c = kel - 273.15; if (c > -40 && c < 130 && (double.IsNaN(best) || c > best)) best = c;
                            if (c > -40 && c < 130) zones.Add(Tuple.Create("Зона ACPI " + ((o["Name"] as string) ?? "").Split('.').Last(), c));
                        }
                    if (double.IsNaN(best)) note = "датчик не найден";
                }
                catch (Exception ex) { note = "датчик недоступен: " + ex.Message; }
                live.Temp = best; live.TempNote = note; live.TempZones = zones; tempBusy = false;
            });
        }

        void CollectAi()
        {
            if (DemoMode) return; // данные примера фиксированы
            var s = live; var now = DateTime.Now;
            s.ClHas = Directory.Exists(ClaudeUsage.Root);
            var bs = ClaudeUsage.BlockStart(claude.Msgs, now);
            s.ClS = bs == null ? "—" : Tok(claude.Msgs.Where(m => m.T >= bs.Value).Sum(m => m.Weight));
            s.ClReset = bs == null ? "—" : Until(bs.Value.AddHours(5) - now);
            var week = claude.Msgs.Where(m => m.T >= now.AddDays(-7)).Sum(m => m.Weight);
            s.ClW = week == 0 ? "—" : Tok(week);
            var cb = ClaudeBridge.Read(); s.ClBridge = cb != null;
            s.ClP5 = cb == null ? double.NaN : ClaudeBridge.Eff(cb.P5, cb.R5, now); s.ClPW = cb == null ? double.NaN : ClaudeBridge.Eff(cb.PW, cb.RW, now);
            s.ClR5 = cb == null ? "—" : ClaudeBridge.Left(cb.P5, cb.R5, now); s.ClRW = cb == null ? "—" : ClaudeBridge.Left(cb.PW, cb.RW, now);
            s.ClCtx = cb == null || (now - cb.Seen).TotalHours > 2 ? double.NaN : cb.Ctx; // контекст — последней активной сессии, пока она свежая
            s.ClPctSeen = cb == null ? null : cb.Seen.ToString("d MMM HH:mm"); s.ClPctStale = cb != null && (now - cb.Seen).TotalHours > 6;
            s.CxHas = Directory.Exists(CodexLimits.Root);
            s.CxData = codex != null;
            if (codex == null) { s.Cx5 = s.CxW = double.NaN; s.Cx5Reset = s.CxWReset = "—"; return; }
            s.Cx5 = codex.Eff5(now); s.CxW = codex.EffW(now);
            // окно сброшено, а новой записи ещё нет — процент неизвестен, ждём обновления
            s.Cx5Reset = codex.Expired5(now) ? "ждём" : double.IsNaN(s.Cx5) ? "—" : Until(codex.R5 - now);
            s.CxWReset = codex.ExpiredW(now) ? "ждём" : double.IsNaN(s.CxW) ? "—" : Until(codex.RW - now);
            s.CxStale = (now - codex.Seen).TotalHours > 6; s.CxSeen = codex.Seen.ToString("d MMM HH:mm");
        }

        void CollectWeather()
        {
            var s = live; var city = C("wxCity", "");
            s.WxCity = city == "" ? null : city;
            if (wx == null) { s.WxOk = false; s.WxTemp = "—"; s.WxDesc = wxError != null ? "нет связи" : "выберите город"; return; }
            var d = Weather.Wmo(wx.Code, wx.Day);
            s.WxStale = wxError != null; s.WxAge = Ago(wx.Updated);
            s.WxOk = true; s.WxKind = d.Item1; s.WxTemp = Weather.Deg(wx.Temp); s.WxDesc = d.Item2.ToLower();
            s.WxRange = wx.DMax.Length > 0 ? "↑" + Weather.Deg(wx.DMax[0]) + " ↓" + Weather.Deg(wx.DMin[0]) : "";
        }

        async void UpdateWeather()
        {
            if (wxBusy || C("wxLat", "") == "") { CollectWeather(); return; }
            wxBusy = true; wxTried = DateTime.Now;
            var cache = CachePath("weather", C("wxLat", "") + "," + C("wxLon", ""));
            try
            {
                var json = await Get("https://api.open-meteo.com/v1/forecast?latitude=" + C("wxLat", "") + "&longitude=" + C("wxLon", "") + Weather.Fields);
                wx = Weather.Parse(json); wxError = null;
                try { SafeFile.Write(cache, json); } catch (Exception ex) { Log(ex); }
            }
            catch (Exception ex)
            {
                Log(ex); wxError = ex.Message; // остаются последние полученные данные
                if (wx == null && SafeFile.Read(cache) != null) // после перезапуска без сети — сохранённый прогноз
                    try { wx = Weather.Parse(SafeFile.Read(cache)); wx.Updated = File.GetLastWriteTime(cache); wxError = "показаны данные от " + Ago(wx.Updated) + " (" + ex.Message + ")"; } catch { } // время данных — время кэша, а не чтения
            }
            finally { wxBusy = false; }
            CollectWeather(); All(b => b.Render());
        }

        async void UpdateAi()
        {
            if (aiBusy) return;
            bool cl = Directory.Exists(ClaudeUsage.Root), cx = Directory.Exists(CodexLimits.Root);
            if (!cl && !cx) return;
            aiBusy = true;
            try
            {
                var r = await Task.Run(() => Tuple.Create(cl ? claude.Scan() : new List<ClaudeUsage.Msg>(), cx ? CodexLimits.ReadLatest() : null));
                claude.Add(r.Item1); codex = r.Item2;
            }
            catch (Exception ex) { Log(ex); }
            finally { aiBusy = false; }
            CollectAi();
        }

        void TrackScreen()
        {
            if (screen.Day != Today) { screen.Save(); screen = ScreenTime.Load(Today); }
            var li = new N.LASTINPUT { Size = 8 };
            // shortcut: без ввода дольше 2 минут время не считается — просмотр видео без касаний тоже не учитывается
            if (N.GetLastInputInfo(ref li) && unchecked((uint)Environment.TickCount - li.Time) < 120000)
            {
                var fg = N.GetForegroundWindow(); uint pid;
                if (fg != IntPtr.Zero) { N.GetWindowThreadProcessId(fg, out pid); var ep = ExePath(pid); var an = AppName(ep); screen.Track(an, DateTime.Now); ScreenTime.Seen(an, ep); }
            }
            if (ticks % 60 == 0) screen.Save();
        }

        void PomoNext()
        {
            pomoMode = pomoMode == 1 ? 2 : 1;
            if (pomoMode == 1) pomoRound++;
            pomoEnd = DateTime.Now.AddMinutes(pomoMode == 1 ? 25 : 5);
            live.PomoMode = pomoMode; live.PomoRound = pomoRound; live.PomoLeft = pomoEnd - DateTime.Now;
        }
        void PomoReset() { pomoMode = 0; pomoRound = 0; live.PomoMode = 0; live.PomoRound = 0; live.PomoLeft = TimeSpan.FromMinutes(25); }
        void Pomo()
        {
            if (pomoMode == 0 || pomoEnd > DateTime.Now) return;
            PomoNext();
            if (pomoMode == 2) Notify("Фокус завершён", "Интервал " + pomoRound + " готов. Перерыв 5 минут.", false, "", "Пропустить перерыв", () => { PomoNext(); All(b => b.Render()); }, "pomo");
            else Notify("Перерыв окончен", "Начинается интервал " + pomoRound + " — 25 минут фокуса.", false, "", "Остановить", () => { PomoReset(); All(b => b.Render()); }, "pomo");
        }
        void UpdateTodoTile()
        {
            var nt = NextDueTodo(); live.NextTodo = nt == null ? null : nt.Text; live.TodoDone = todos.Count(t => t.Done); live.TodoTotal = todos.Count;
            All(b => b.Render());
        }
        void UpdateBin()
        {
            var i = new N.RBINFO { Size = Marshal.SizeOf(typeof(N.RBINFO)) };
            if (N.SHQueryRecycleBin(null, ref i) != 0) return;
            live.BinItems = i.Items; live.BinBytes = i.Bytes;
        }
        void UpdateTools()
        {
            if (timerRunning && timerEnd <= DateTime.Now)
            {
                timerRunning = false; timerLeft = TimeSpan.Zero;
                Notify("Таймер", "Время вышло", true, "", "Ещё 5 минут", () => { timerEnd = DateTime.Now.AddMinutes(5); timerRunning = true; }, "tools");
            }
            if (timerRunning) { live.ToolLabel = "Таймер"; live.ToolTime = Span(timerEnd - DateTime.Now); }
            else if (sw.IsRunning) { live.ToolLabel = "Секундомер"; live.ToolTime = Span(sw.Elapsed); }
            else live.ToolLabel = live.ToolTime = null;
        }
        void CheckMeeting()
        {
            var now = DateTime.Now; var next = live.Next;
            if (next == null || !On("meetRemind") || next.Start <= now || next.Start - now > TimeSpan.FromMinutes(5)) return;
            Notify("Встреча через " + Until(next.Start - now), next.Title + (string.IsNullOrEmpty(next.Location) ? "" : " · " + next.Location), false, "",
                "Календарь", ShowCalendar, "event", "meet|" + next.Title + "|" + next.Start.ToString("s"));
        }

        static DateTime mediaBusySince, silentSince = DateTime.MinValue; static MC.GlobalSystemMediaTransportControlsSession hooked;
        static bool mediaAgain, wantPlaying; static bool? titleWas; static DateTime titleAt, wantUntil;
        // null — окна Spotify нет (свёрнут в трей): остаётся состояние от Windows
        static bool? SpotifyTitlePlaying()
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("Spotify"))
                try { var t = p.MainWindowTitle; if (!string.IsNullOrEmpty(t)) return !t.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase) || t.Contains(" - "); }
                catch { } finally { p.Dispose(); }
            return null;
        }
        static void KickMedia() { var p = Primary; if (p != null) p.Dispatcher.BeginInvoke(new Action(() => { if (mediaBusy) mediaAgain = true; else p.UpdateMedia(); })); } // событие во время обновления — ещё одно сразу после него
        async void UpdateMedia()
        {
            // запрос к Windows не вернулся больше 10 с — не ждём его вечно: менеджер плееров пересоздаётся, иначе модуль навсегда замирал на «Ничего не играет»
            if (mediaBusy && (DateTime.Now - mediaBusySince).TotalSeconds < 10) return;
            if (mediaBusy) { mediaMgr = null; lastMedia = null; }
            mediaBusy = true; mediaBusySince = DateTime.Now;
            try
            {
                if (mediaMgr == null) { mediaMgr = await MC.GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); mediaMgr.SessionsChanged += (a, b) => KickMedia(); mediaMgr.CurrentSessionChanged += (a, b) => KickMedia(); hooked = null; }
                var ss = PickSession(); var s = live; bool wasPlaying = s.Playing, wasRemote = s.Remote;
                if (ss != null && !ReferenceEquals(ss, hooked)) { hooked = ss; ss.PlaybackInfoChanged += (a, b) => KickMedia(); ss.MediaPropertiesChanged += (a, b) => { lastMedia = null; KickMedia(); }; } // пауза, перемотка и смена трека — сразу, а не при следующем опросе
                s.Sessions = mediaMgr.GetSessions().Where(x => !SpotifyOnly || IsSpotify(x.SourceAppUserModelId)).Select(x => Tuple.Create(x.SourceAppUserModelId, AppTitle(x.SourceAppUserModelId))).ToList();
                { var sp = System.Diagnostics.Process.GetProcessesByName("Spotify"); spotifyUp = sp.Length > 0; foreach (var x in sp) x.Dispose(); }
                if (ss == null) { s.HasMedia = s.Playing = false; s.Title = s.Artist = null; s.Cover = null; s.Pos = s.Dur = double.NaN; lastMedia = null; return; }
                var info = ss.GetPlaybackInfo();
                s.Playing = info.PlaybackStatus == MC.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; bool smtcPlaying = s.Playing;
                // Spotify сообщает Windows о паузе и воспроизведении через 5–10 с, а заголовок окна меняет сразу: «Spotify Premium» — пауза, «Исполнитель - трек» — играет
                bool? byTitle = IsSpotify(ss.SourceAppUserModelId) ? SpotifyTitlePlaying() : null;
                if (byTitle != titleWas) { titleWas = byTitle; titleAt = DateTime.Now; }
                if (byTitle == true) s.Playing = true;
                else if (byTitle == false && s.Playing && (DateTime.Now - titleAt).TotalSeconds < 15) s.Playing = false; // дольше — играет на другом устройстве (ниже)
                if (DateTime.Now < wantUntil) { if (byTitle == wantPlaying) wantUntil = DateTime.MinValue; else s.Playing = wantPlaying; } // нажали ▶/⏸ в FlowBar — сразу, пока плеер не подтвердил
                s.CanPrev = info.Controls.IsPreviousEnabled; s.CanNext = info.Controls.IsNextEnabled; s.CanToggle = info.Controls.IsPlayPauseToggleEnabled || info.Controls.IsPlayEnabled || info.Controls.IsPauseEnabled;
                s.CanSeek = info.Controls.IsPlaybackPositionEnabled; s.MediaError = null;
                bool? here = s.Playing ? Audio.Playing(new HashSet<uint>(System.Diagnostics.Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(ss.SourceAppUserModelId.Contains("!") ? "Spotify" : ss.SourceAppUserModelId)).Select(pr => { var id = (uint)pr.Id; pr.Dispose(); return id; }))) : null;
                if (here == false) { if (silentSince == DateTime.MinValue) silentSince = DateTime.Now; } else silentSince = DateTime.MinValue;
                s.Remote = here == false && (DateTime.Now - silentSince).TotalSeconds > 3;
                s.App = ss.SourceAppUserModelId;
                try
                {
                    var tl = ss.GetTimelineProperties();
                    double dur = (tl.EndTime - tl.StartTime).TotalSeconds, pos = tl.Position.TotalSeconds;
                    if (smtcPlaying) pos += (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds;
                    s.Dur = dur > 1 ? dur : double.NaN; s.Pos = dur > 1 ? Math.Max(0, Math.Min(dur, pos)) : double.NaN;
                }
                catch { s.Pos = s.Dur = double.NaN; }
                var p = await ss.TryGetMediaPropertiesAsync();
                var key = s.App + "\u0001" + p.Artist + "\u0001" + p.Title;
                if (key == lastMedia) { if (wasPlaying != s.Playing || wasRemote != s.Remote) All(b => b.Render()); return; }
                lastMedia = key;
                s.Title = p.Title; s.Artist = p.Artist; s.HasMedia = !string.IsNullOrEmpty(p.Title);
                s.Cover = p.Thumbnail == null ? null : await LoadImage(p.Thumbnail);
                All(b => b.Render());
            }
            catch (Exception ex) { Log(ex); var s = live; s.MediaError = ex.Message; s.HasMedia = s.Playing = false; s.Cover = null; lastMedia = null; } // источник сломался — не держим старую обложку
            finally { mediaBusy = false; if (mediaAgain) { mediaAgain = false; Dispatcher.BeginInvoke(new Action(UpdateMedia)); } }
        }
        // плеер, который показан и которым управляют кнопки; null — текущий по мнению Windows
        static string mediaPick;
        // модуль музыки — для Spotify: браузер, Telegram и другие плееры не подхватываются (выключается в меню модуля)
        static bool SpotifyOnly { get { return C("media.spotifyOnly", "1") == "1"; } }
        static bool IsSpotify(string aumid) { return aumid != null && aumid.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0; }
        static bool spotifyUp; // процесс Spotify запущен (сеанс в Windows появляется только после начала воспроизведения)
        static string SpotifyExe { get { var p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify", "Spotify.exe"); return File.Exists(p) ? p : null; } }
        static void OpenSpotify() { try { Start(SpotifyExe ?? "spotify:"); } catch (Exception ex) { Log(ex); } }
        static MC.GlobalSystemMediaTransportControlsSession PickSession()
        {
            if (mediaMgr == null) return null;
            if (SpotifyOnly) return mediaMgr.GetSessions().FirstOrDefault(x => IsSpotify(x.SourceAppUserModelId));
            if (mediaPick != null) { foreach (var x in mediaMgr.GetSessions()) if (x.SourceAppUserModelId == mediaPick) return x; mediaPick = null; }
            // «текущий» по мнению Windows — не всегда тот, что играет (вкладка браузера, видео без названия) или его нет вовсе: тогда — играющий плеер
            var cur = mediaMgr.GetCurrentSession();
            if (cur != null && IsPlaying(cur)) return cur;
            var all = mediaMgr.GetSessions();
            return all.FirstOrDefault(IsPlaying) ?? cur ?? all.FirstOrDefault();
        }
        static bool IsPlaying(MC.GlobalSystemMediaTransportControlsSession x)
        {
            try { return x.GetPlaybackInfo().PlaybackStatus == MC.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; } catch { return false; }
        }
        static async void MediaCmd(string cmd, double frac = 0)
        {
            var s = PickSession();
            if (s == null && SpotifyOnly && !spotifyUp) { if (cmd == "toggle") OpenSpotify(); return; } // Spotify не запущен — ▶ открывает его
            if (s == null) { if (cmd == "toggle") Keys(0xB3); else if (cmd == "next") Keys(0xB0); else if (cmd == "prev") Keys(0xB1); return; }
            try
            {
                switch (cmd)
                {
                    case "toggle":
                        { var l = live; l.Playing = wantPlaying = !l.Playing; l.Remote = false; wantUntil = DateTime.Now.AddSeconds(3); All(b => { b.Render(); if (b.panelTick != null) b.panelTick(); }); }
                        await s.TryTogglePlayPauseAsync(); break;
                    case "next": await s.TrySkipNextAsync(); break;
                    case "prev": await s.TrySkipPreviousAsync(); break;
                    case "seek": var tl = s.GetTimelineProperties(); await s.TryChangePlaybackPositionAsync((tl.StartTime + TimeSpan.FromTicks((long)((tl.EndTime - tl.StartTime).Ticks * Math.Max(0, Math.Min(1, frac))))).Ticks); break;
                }
            }
            catch (Exception ex) { Log(ex); }
            lastMedia = null;
        }
        static void Enable(Press b, bool on) { b.Opacity = on ? 1 : 0.35; b.IsEnabled = on; if (!on) b.ToolTip = "Плеер не поддерживает это действие"; }
        static string AppTitle(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return "";
            var a = aumid.Split('!').Last(); if (a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) a = a.Substring(0, a.Length - 4);
            var dot = a.LastIndexOf('.'); if (dot >= 0 && dot < a.Length - 1) a = a.Substring(dot + 1);
            return a.Length > 0 ? char.ToUpper(a[0]) + a.Substring(1) : a;
        }

        // радиомодули Wi-Fi и Bluetooth: список общий, плитки — у каждой панели свои
        static List<RD.Radio> radioList; static bool radiosAsked;
        List<Tuple<RD.Radio, TextBlock>> radioTiles = new List<Tuple<RD.Radio, TextBlock>>();
        async void InitRadios()
        {
            if (radiosAsked) return; radiosAsked = true;
            try
            {
                if (await RD.Radio.RequestAccessAsync() != RD.RadioAccessStatus.Allowed) return;
                radioList = (await RD.Radio.GetRadiosAsync()).Where(r => r.Kind == RD.RadioKind.WiFi || r.Kind == RD.RadioKind.Bluetooth).ToList();
                All(b => b.Rebuild());
            }
            catch (Exception ex) { Log(ex); }
        }

        // ---------- обновление раз в секунду ----------
        readonly List<Action> ups = new List<Action>();
        static readonly HashSet<string> renderErrors = new HashSet<string>();
        void Render()
        {
            foreach (var a in ups.ToList())
                try { a(); }
                catch (Exception ex) { if (renderErrors.Add(ex.GetType().Name + ex.Message)) Log(ex); }
        }
        Action panelTick; // живое обновление открытой карточки
        Action panelBuild; double cx0Back = double.NaN; // карточка, к которой ведёт стрелка «назад», и где её показать
        static bool watchRetry;
        // Пуск и поиск Windows — окно SearchHost (StartMenuExperienceHost в прежних сборках) — по центру над нажатой кнопкой, а не у края экрана.
        // Windows помнит место окна, поэтому ставим его до открытия и поправляем ещё несколько раз, пока оно появляется.
        void ShellOver(FrameworkElement tile, params byte[] keys)
        {
            bool opening = !(shellAtDown && lastShellKeys == string.Join(",", keys)); // повторное нажатие закрывает — не двигаем
            if (opening && tile != null) PlaceShell(tile);
            ShellToggle(keys);
            if (!opening || tile == null) return;
            int n = 0; var t = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(16) }; // Windows ставит окно на своё место в момент открытия — поправляем сразу, пока не видно скачка
            t.Tick += (o, e) => { PlaceShell(tile); if (++n >= 50) t.Stop(); };
            t.Start();
        }
        public static void PlaceStartNow() { var p = Primary; if (p != null && p.startEl != null) p.PlaceShell(p.startEl); } // и клавиша Win открывает Пуск над кнопкой
        void PlaceShell(FrameworkElement tile)
        {
            try
            {
                if (!tile.IsVisible) return;
                var a = tile.PointToScreen(new Point(0, 0)); var b2 = tile.PointToScreen(new Point(tile.ActualWidth, tile.ActualHeight)); // физические пиксели
                double cx = (a.X + b2.X) / 2, gap = 8 * MonScale; var m = scr.Bounds;
                for (var h = N.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null); h != IntPtr.Zero; h = N.FindWindowEx(IntPtr.Zero, h, "Windows.UI.Core.CoreWindow", null))
                {
                    uint pid; N.GetWindowThreadProcessId(h, out pid); var exe = ExePath(pid);
                    var name = exe == null ? "" : System.IO.Path.GetFileName(exe).ToLowerInvariant();
                    if (name != "searchhost.exe" && name != "startmenuexperiencehost.exe") continue;
                    N.RECT r; if (!N.GetWindowRect(h, out r)) continue;
                    int w = r.R - r.L, hh = r.B - r.T; if (w < 200 || hh < 200) continue;
                    int x = (int)Math.Round(Math.Max(m.Left + gap, Math.Min(m.Right - gap - w, cx - w / 2.0)));
                    int y = (int)Math.Round(IsTop ? b2.Y + gap : a.Y - gap - hh); y = Math.Max(m.Top, Math.Min(m.Bottom - hh, y));
                    if (x != r.L || y != r.T) N.SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010); // без смены размера, порядка и фокуса
                }
            }
            catch (Exception ex) { Log(ex); }
        }
        void Tick()
        {
            try
            {
                if (WantsGlass(Cur)) UpdateRegion();
                if (primary && !DemoMode)
                {
                    ticks++; CheckHide("опрос"); CheckReminders(); CheckUpdateQuietly();
                    if (DateTime.Now < peekUntil && TrayOpen()) peekUntil = DateTime.Now.AddSeconds(3); // пока открыты скрытые значки — панель Windows не прячем
                    bool peek = DateTime.Now < peekUntil;
                    if (HideTb && !peek) SetTaskbar(false);
                    if (peekWas && !peek) All(b => b.Place());
                    peekWas = peek;
                    Collect();
                    SyncAppearanceSchedule();
                    PollAppProgress();
                    if (ticks % 900 == 1) UpdateWeather(); // раз в 15 минут
                    if (ticks % 60 == 2) UpdateAi();
                    if (ticks % 900 == 3) UpdateCalendars(); // ссылки iCal — раз в 15 минут
                    if (ticks % 5 == 1) RunAutomations();
                    // сторож не подтвердил запуск (например, первый запуск задержала проверка Защитника) — пробуем снова в фоне; получилось — прячем панель Windows
                    if (!WatchOk && !watchRetry && ticks % 15 == 7 && C("hideTaskbar", "1") == "1")
                    {
                        watchRetry = true;
                        Task.Run(() => Program.StartWatch()).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() => { watchRetry = false; if (t.Result) { WatchOk = true; SetTaskbar(false); All(b => { b.reserveKey = null; b.Place(); }); } })));
                    }                    if (ticks % 5 == 3 && WatchOk && (Program.Watch == null || Program.Watch.HasExited))
                    {
                        WatchOk = Program.StartWatch(); // сторож пропал — перезапускаем; не вышло — возвращаем панель Windows
                        if (!WatchOk) { All(b => { b.reserveKey = null; b.Place(); }); Taskbar.Restore(false, false); Notify("Сторож перестал работать", "Панель Windows возвращена, чтобы не остаться без неё при сбое FlowBar.", true, "\uE7BA"); }
                    }
                    if (ticks % 600 == 0) pathCache.Clear(); // id процессов переиспользуются
                }
                Fullscreen(); RefreshApps(); Render(); EnsureWorkArea();
                if (panelTick != null) panelTick();
            }
            catch (Exception ex) { Log(ex); }
        }
        void Rebuild()
        {
            ClosePreview();
            Build(); ApplyStyle(); lastKey = null; RefreshApps(); Render();
        }
        static Style CardScrollStyle;
        static void UseCardScroll(FrameworkElement w) { if (CardScrollStyle != null) w.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = CardScrollStyle; }
        static void ApplyAppTheme() { try { ApplyAppThemeCore(); } catch (Exception ex) { Log(ex); } } // оформление подсказок не должно ронять панель
        static void ApplyAppThemeCore()
        {
            if (Application.Current == null) return;
            var tt = new Style(typeof(ToolTip));
            tt.Setters.Add(new Setter(Control.BackgroundProperty, Frozen(CardBg, 0xF8))); tt.Setters.Add(new Setter(Control.ForegroundProperty, Frozen(CardFg, 255)));
            tt.Setters.Add(new Setter(Control.BorderBrushProperty, Frozen(CardFg, 0x30))); tt.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
            Application.Current.Resources[typeof(ToolTip)] = tt;
            // тонкая вертикальная полоса прокрутки в цветах темы (системная светлая выбивалась из тёмных карточек)
            var fgc = CardFg; var thumbColor = string.Format("#48{0:X2}{1:X2}{2:X2}", fgc.R, fgc.G, fgc.B);
            var sbT = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ScrollBar'>" +
                "<Track Name='PART_Track' IsDirectionReversed='True'><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'>" +
                "<Border CornerRadius='3' Margin='2,0,2,0' Background='" + thumbColor + "'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></ControlTemplate>");
            var sb = new Style(typeof(System.Windows.Controls.Primitives.ScrollBar));
            sb.Setters.Add(new Setter(Control.TemplateProperty, sbT)); sb.Setters.Add(new Setter(FrameworkElement.WidthProperty, 8.0)); sb.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 8.0));
            var horiz = new Trigger { Property = System.Windows.Controls.Primitives.ScrollBar.OrientationProperty, Value = Orientation.Horizontal };
            horiz.Setters.Add(new Setter(FrameworkElement.WidthProperty, double.NaN)); horiz.Setters.Add(new Setter(FrameworkElement.HeightProperty, 0.0)); // горизонтальные полосы у нас скрыты
            sb.Triggers.Add(horiz);
            CardScrollStyle = sb; // только для карточек и окон FlowBar: на самой панели полоса отнимала бы высоту у модулей
            ApplyMenuTheme();
        }

        // контекстные меню и подменю — в цветах карточек: скругление, мягкая тень отдельным слоем (текст остаётся резким), подсветка строки, галочка акцентом
        static void ApplyMenuTheme()
        {
            Func<Color, byte, string> hex = (c, a) => string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", a, c.R, c.G, c.B);
            string bg = hex(CardBg, 0xFA), fg = hex(CardFg, 0xFF), edge = hex(CardFg, 0x22), hover = hex(CardFg, 0x16), acc = hex(AccentColor, 0xFF);
            const string ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' ";
            // поверхность меню: тень — отдельный слой позади, без эффекта на тексте
            Func<string, string> surface = inner =>
                "<Grid Margin='4,2,14,16'><Grid.RenderTransform><TranslateTransform/></Grid.RenderTransform><Grid.Triggers><EventTrigger RoutedEvent='FrameworkElement.Loaded'><BeginStoryboard><Storyboard>" +
                "<DoubleAnimation Storyboard.TargetProperty='Opacity' From='0' To='1' Duration='0:0:0.11'/><DoubleAnimation Storyboard.TargetProperty='RenderTransform.Y' From='6' To='0' Duration='0:0:0.16' DecelerationRatio='0.8'/>" +
                "</Storyboard></BeginStoryboard></EventTrigger></Grid.Triggers>" +
                "<Border CornerRadius='10' Background='" + bg + "'><Border.Effect><DropShadowEffect BlurRadius='16' ShadowDepth='4' Direction='270' Opacity='0.35'/></Border.Effect></Border>" +
                "<Border CornerRadius='10' Background='" + bg + "' BorderBrush='" + edge + "' BorderThickness='1' Padding='4'>" + inner + "</Border></Grid>";
            var menuT = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate " + ns + "TargetType='ContextMenu'>" +
                surface("<ScrollViewer VerticalScrollBarVisibility='Auto'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle' Grid.IsSharedSizeScope='True'/></ScrollViewer>") + "</ControlTemplate>");
            var itemT = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate " + ns + "TargetType='MenuItem'>" +
                "<Grid SnapsToDevicePixels='True'>" +
                "<Border Name='Bd' CornerRadius='7' Background='Transparent' Padding='9,7,10,7' MinWidth='200'><Grid>" +
                "<Grid.ColumnDefinitions><ColumnDefinition Width='27'/><ColumnDefinition Width='*'/><ColumnDefinition Width='18'/></Grid.ColumnDefinitions>" +
                "<TextBlock Name='Chk' Text='&#xE73E;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='12' Foreground='" + acc + "' VerticalAlignment='Center' Visibility='Collapsed'/>" +
                "<ContentPresenter Name='Ico' ContentSource='Icon' VerticalAlignment='Center' HorizontalAlignment='Left' Opacity='0.78'/>" +
                "<ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>" +
                "<TextBlock Name='Arrow' Grid.Column='2' Text='&#xE76C;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='9' Opacity='0.7' HorizontalAlignment='Right' VerticalAlignment='Center' Visibility='Collapsed'/>" +
                "</Grid></Border>" +
                "<Popup Name='PART_Popup' Placement='Right' HorizontalOffset='-4' VerticalOffset='-7' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade' " +
                "IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'>" +
                surface("<ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle' Grid.IsSharedSizeScope='True'/>") + "</Popup>" +
                "</Grid><ControlTemplate.Triggers>" +
                "<Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Bd' Property='Background' Value='" + hover + "'/></Trigger>" +
                "<Trigger Property='IsChecked' Value='True'><Setter TargetName='Chk' Property='Visibility' Value='Visible'/><Setter TargetName='Ico' Property='Visibility' Value='Collapsed'/></Trigger>" +
                "<Trigger Property='Tag' Value='danger'><Setter Property='Foreground' Value='#F0705F'/></Trigger>" +
                "<Trigger Property='Role' Value='SubmenuHeader'><Setter TargetName='Arrow' Property='Visibility' Value='Visible'/></Trigger>" +
                "<Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate>");
            var font = new FontFamily("Segoe UI Variable Text, Segoe UI");
            var cm = new Style(typeof(ContextMenu));
            cm.Setters.Add(new Setter(Control.TemplateProperty, menuT)); cm.Setters.Add(new Setter(ContextMenu.HasDropShadowProperty, true)); // HasDropShadow — прозрачное окно меню под свою тень
            cm.Setters.Add(new Setter(Control.ForegroundProperty, Frozen(CardFg, 255))); cm.Setters.Add(new Setter(Control.FontFamilyProperty, font)); cm.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            var mi = new Style(typeof(MenuItem));
            mi.Setters.Add(new Setter(Control.TemplateProperty, itemT)); mi.Setters.Add(new Setter(Control.ForegroundProperty, Frozen(CardFg, 255)));
            mi.Setters.Add(new Setter(Control.FontFamilyProperty, font)); mi.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            var sep = new Style(typeof(Separator));
            sep.Setters.Add(new Setter(Control.TemplateProperty, System.Windows.Markup.XamlReader.Parse("<ControlTemplate " + ns + "TargetType='Separator'><Border Height='1' Margin='9,5,9,5' Background='" + edge + "'/></ControlTemplate>")));
            var r = Application.Current.Resources;
            r[typeof(ContextMenu)] = cm; r[typeof(MenuItem)] = mi; r[MenuItem.SeparatorStyleKey] = sep;
        }
        static void RebuildAll() { ApplyAppTheme(); All(b => { b.ResetFit(true); b.Rebuild(); }); }

        // ---------- плитка ----------
        // ---------- подсказка панели ----------
        Window tipWin; Border tipBubble; TextBlock tipTitle, tipDetail; DispatcherTimer tipShowT, tipHideT; FrameworkElement tipFor; bool tipOn;
        Backdrop tipBackdrop;
        Rect tipBackdropRect = Rect.Empty;
        DateTime tipTrackingUntil;
        const double TipH = 145, TipWidth = 340;
        void TipEnsure()
        {
            if (tipWin != null) return;
            tipTitle = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, LineHeight = 18 };
            tipDetail = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, LineHeight = 16, Opacity = 0.8, Margin = new Thickness(0, 3, 0, 0) };
            tipBubble = new Border { Child = VS(tipTitle, tipDetail), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 12, 9), Opacity = 0,
                HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = new TranslateTransform() };
            var host = new Grid(); host.Children.Add(tipBubble);
            tipWin = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false, ResizeMode = ResizeMode.NoResize,
                Width = 1, Height = 1, Content = host, IsHitTestVisible = false, Focusable = false };
            StylePopupText(tipWin);
            tipWin.SourceInitialized += (s, e) => { var ph = new WindowInteropHelper(tipWin).Handle; N.SetWindowLongPtr(ph, -20, new IntPtr(N.GetWindowLongPtr(ph, -20).ToInt64() | 0x08000000 | 0x80 | 0x20)); }; // не забирает фокус и пропускает мышь
            Closed += (s, e) =>
            {
                CompositionTarget.Rendering -= SyncTipBackdrop;
                if (tipBackdrop != null) { tipBackdrop.Dispose(); tipBackdrop = null; }
                if (tipWin != null) { tipWin.Close(); tipWin = null; }
            };
            tipWin.Show();
        }
        void TipShow(FrameworkElement el)
        {
            var text = el.ToolTip as string;
            if (string.IsNullOrEmpty(text) || dragging) { TipHide(true); return; }
            tipFor = el; if (tipHideT != null) tipHideT.Stop(); if (tipShowT != null) tipShowT.Stop();
            if (tipOn) { TipMove(el, text); return; }
            tipShowT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            tipShowT.Tick += (s, e) => { tipShowT.Stop(); if (tipFor == el && el.IsMouseOver && !dragging && preview == null) TipMove(el, el.ToolTip as string ?? text); };
            tipShowT.Start();
        }
        void TipMove(FrameworkElement el, string text)
        {
            TipEnsure();
            bool vertical = VerticalBar;
            var bounds = scr.Bounds;
            double sc = MonScale;
            double screenW = bounds.Width / sc, screenH = bounds.Height / sc;
            tipWin.Width = vertical ? Math.Min(TipWidth + 16, screenW) : screenW;
            tipWin.Height = vertical ? screenH : TipH;
            tipBubble.HorizontalAlignment = vertical && !LeftBar ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            tipBubble.VerticalAlignment = !vertical && !IsTop ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            tipBubble.Margin = vertical ? new Thickness(0) : new Thickness(0, 7, 0, 7);
            var start = root.PointToScreen(new Point()); var end = root.PointToScreen(new Point(root.ActualWidth, root.ActualHeight));
            double left = !vertical ? bounds.Left : LeftBar ? end.X + 7 * sc : start.X - (tipWin.Width + 7) * sc;
            double top = vertical ? bounds.Top : IsTop ? end.Y : start.Y - TipH * sc;
            N.SetWindowPos(new WindowInteropHelper(tipWin).Handle, IntPtr.Zero, (int)Math.Round(left), (int)Math.Round(top), 0, 0, 0x1 | 0x4 | 0x10);
            tipBubble.Background = CardFill; tipBubble.BorderBrush = CardEdge; tipWin.Foreground = Frozen(CardFg, 255);
            int line = text.IndexOf('\n');
            tipTitle.Text = line < 0 ? text : text.Substring(0, line);
            tipDetail.Text = line < 0 ? "" : text.Substring(line + 1);
            tipDetail.Visibility = line < 0 ? Visibility.Collapsed : Visibility.Visible;
            var content = (FrameworkElement)tipBubble.Child;
            content.Measure(new Size(Math.Max(30, Math.Min(tipWin.Width, TipWidth) - 28), double.PositiveInfinity));
            double width = Math.Ceiling(content.DesiredSize.Width) + 26;
            double height = Math.Ceiling(content.DesiredSize.Height) + 19;
            var center = el.PointToScreen(new Point(el.ActualWidth / 2, el.ActualHeight / 2));
            double span = vertical ? tipWin.Height : tipWin.Width, length = vertical ? height : width;
            double cursor = vertical ? (center.Y - bounds.Top) / sc : (center.X - bounds.Left) / sc;
            double offset = Math.Round(Math.Max(5, Math.Min(span - length - 5, cursor - length / 2)));
            var tt = (TranslateTransform)tipBubble.RenderTransform;
            var axis = vertical ? TranslateTransform.YProperty : TranslateTransform.XProperty;
            var otherAxis = vertical ? TranslateTransform.XProperty : TranslateTransform.YProperty;
            tt.BeginAnimation(otherAxis, null); tt.SetValue(otherAxis, 0d);
            if (!tipOn || !Motion)
            {
                tt.BeginAnimation(axis, null); tipBubble.BeginAnimation(WidthProperty, null); tt.SetValue(axis, offset); tipBubble.Width = width;
                tipBubble.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(Motion ? 120 : 0)));
            }
            else
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut }; var dur = TimeSpan.FromMilliseconds(190);
                tt.BeginAnimation(axis, new DoubleAnimation(offset, dur) { EasingFunction = ease });
                tipBubble.BeginAnimation(WidthProperty, new DoubleAnimation(width, dur) { EasingFunction = ease });
                tipBubble.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(80)));
            }
            tipOn = true;
            CompositionTarget.Rendering -= SyncTipBackdrop;
            if (GlassOn)
            {
                tipTrackingUntil = DateTime.UtcNow.AddMilliseconds(260);
                CompositionTarget.Rendering += SyncTipBackdrop;
            }
            else HideTipBackdrop();
        }
        void SyncTipBackdrop(object sender, EventArgs args)
        {
            if (!tipOn || tipWin == null || !GlassOn) { HideTipBackdrop(); return; }
            if (DateTime.UtcNow >= tipTrackingUntil) CompositionTarget.Rendering -= SyncTipBackdrop;
            if (tipBubble.ActualWidth < 1 || tipBubble.ActualHeight < 1) return;
            if (tipBackdrop == null && (tipBackdrop = Backdrop.Create()) == null)
            {
                tipBubble.Background = PopupFill(false); tipBubble.BorderBrush = MaterialEdge(Cur, false, false);
                HideTipBackdrop(); return;
            }
            var start = tipBubble.PointToScreen(new Point());
            var end = tipBubble.PointToScreen(new Point(tipBubble.ActualWidth, tipBubble.ActualHeight));
            var rect = new Rect(new Point(Math.Round(start.X), Math.Round(start.Y)), new Point(Math.Round(end.X), Math.Round(end.Y)));
            if (rect == tipBackdropRect) return;
            tipBackdropRect = rect;
            double sc = VisualTreeHelper.GetDpi(tipBubble).DpiScaleX;
            tipBackdrop.Set(new WindowInteropHelper(tipWin).Handle, (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height,
                tipBubble.CornerRadius.TopLeft * sc, true, GlassBlurPixels(sc));
        }
        void HideTipBackdrop()
        {
            CompositionTarget.Rendering -= SyncTipBackdrop;
            tipBackdropRect = Rect.Empty;
            if (tipBackdrop != null) tipBackdrop.Set(IntPtr.Zero, 0, 0, 0, 0, 0, false);
        }
        void TipHide(bool now)
        {
            if (tipShowT != null) tipShowT.Stop(); if (tipHideT != null) tipHideT.Stop();
            if (!tipOn && !now) { tipFor = null; return; }
            Action hide = () =>
            {
                tipOn = false; tipFor = null;
                CompositionTarget.Rendering -= SyncTipBackdrop;
                if (tipBubble == null || now || !Motion)
                {
                    HideTipBackdrop();
                    if (tipBubble != null) { tipBubble.BeginAnimation(OpacityProperty, null); tipBubble.Opacity = 0; }
                    return;
                }
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
                fade.Completed += (s, e) => { if (!tipOn) HideTipBackdrop(); };
                tipBubble.BeginAnimation(OpacityProperty, fade);
            };
            if (now) { hide(); return; }
            tipHideT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) }; // переход на соседний значок не гасит подсказку
            tipHideT.Tick += (s, e) => { tipHideT.Stop(); hide(); };
            tipHideT.Start();
        }
        Press Tile(UIElement content, string tip, Action click, Action<ContextMenu> menu, bool wide = false)
        {
            double th = TileH;
            var b = new Press { Height = th, CornerRadius = new CornerRadius(wide ? Math.Round(th * 0.22) : th / 2), Margin = new Thickness(2, 0, 2, 0), Child = content, Cursor = Cursors.Hand };
            b.Base = wide ? Surface : Color.FromArgb(0, Cur.Fg.R, Cur.Fg.G, Cur.Fg.B); b.Background = new SolidColorBrush(b.Base);
            if (wide) { b.BorderThickness = new Thickness(1); b.BorderBrush = ChipEdge; }
            if (click != null) b.Invoke = () => { anchor = b; anchorAt = DateTime.Now; click(); };
            if (menu != null) b.Menu = x => { var cm = new ContextMenu(); menu(cm); if (cm.Items.Count > 0) { cm.PlacementTarget = x; cm.IsOpen = true; } };
            bool xl = SizeKey == "XL"; // у XL поля карточек плотнее, как на образце: восемь блоков помещаются в одну строку
            if (wide) { b.MinWidth = th; b.Padding = new Thickness(Math.Round(th * (xl ? 0.16 : 0.26)), 0, Math.Round(th * (xl ? 0.19 : 0.3)), 0); } else b.Width = Math.Min(th, IconPx + 22); // у XL плитка значка уже высоты, как на образце
            if (tip != null) { b.ToolTip = tip; System.Windows.Automation.AutomationProperties.SetName(b, tip.Split('\n')[0]); }
            b.MouseEnter += (s, e) => Fade(b, Lift(b.Base, 0x16), 120);
            b.MouseLeave += (s, e) => Fade(b, b.Base, 140);
            if (Material == "metal" && content != null)
            {
                var pressDepth = new TranslateTransform();
                content.RenderTransform = pressDepth;
                b.PreviewMouseLeftButtonDown += (s, e) => pressDepth.Y = 1;
                b.PreviewMouseLeftButtonUp += (s, e) => pressDepth.Y = 0;
                b.MouseLeave += (s, e) => pressDepth.Y = 0;
            }
            // подсказка — своя, одна на панель: перетекает от значка к значку (стандартная остаётся у плиток в карточке «Ещё»)
            b.MouseEnter += (s, e) => { bool mine = Window.GetWindow(b) == this; ToolTipService.SetIsEnabled(b, !mine); if (mine) TipShow(b); };
            b.MouseLeave += (s, e) => TipHide(false); b.PreviewMouseDown += (s, e) => TipHide(true);
            b.PreviewMouseLeftButtonDown += (s, e) => { shellAtDown = ShellFlyoutOpen(); Fade(b, Lift(b.Base, 0x2C), 60); };  // нажатие
            b.MouseLeftButtonUp += (s, e) =>
            {
                Fade(b, Lift(b.Base, 0x16), 120); e.Handled = true; anchor = b; anchorAt = DateTime.Now; if (click == null) return;
                // открыт Пуск или поиск: панель не забирает фокус, поэтому он сам не закрывается, а Windows не даёт вывести окно программы — закрываем и действуем следом
                if (shellAtDown && !b.ShellKey && ShellFlyoutOpen())
                {
                    Keys(0x1B); int n = 0; var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
                    t.Tick += (o2, e2) => { if (++n < 12 && ShellFlyoutOpen()) return; t.Stop(); anchor = b; anchorAt = DateTime.Now; click(); };
                    t.Start();
                }
                else click();
            };
            if (menu != null) b.MouseRightButtonUp += (s, e) =>
            {
                e.Handled = true;
                var m = new ContextMenu(); menu(m);
                if (m.Items.Count > 0) { m.PlacementTarget = b; m.IsOpen = true; }
            };
            return b;
        }
        // меню модуля: вид, убрать с панели, каталог
        Action<ContextMenu> WMenu(string id, Action<ContextMenu> extra = null)
        {
            return m =>
            {
                if (extra != null) { extra(m); if (m.Items.Count > 0) m.Items.Add(new Separator()); }
                if (Variants.Contains(id))
                {
                    // размер этого модуля на этой панели (у «Своей раскладки» другой экран не затрагивается); не зависит от S/M/L всей панели
                    var vm = new MenuItem { Header = "Размер" }; var cur = C(MK("view." + id), primary || MonMode != "own" ? "" : C("view." + id, ""));
                    for (int i = 0; i < ViewKeys.Length; i++) { var k = ViewKeys[i]; vm.Items.Add(Item(ViewNames[i], () => { cfg[MK("view." + id)] = k; MarkCustom(); SaveCfg(); RebuildAll(); RestyleWindows(); }, cur == k)); }
                    m.Items.Add(vm);
                }
                if (id != "clock") m.Items.Add(Item("Оставлять видимым", () => { cfg[MK("keep." + id)] = Keep(id) ? "0" : "1"; MarkCustom(); SaveCfg(); RebuildAll(); }, Keep(id)));
                if (id != "clock") m.Items.Add(Item("Убрать с панели", () => SetVisible(id, false)));
                m.Items.Add(Item("Каталог виджетов…", () => ShowCatalog(this)));
            };
        }
        void SetVisible(string id, bool on)
        {
            if (CompactMonitor)
            {
                var key = "mon.compact." + scr.DeviceName;
                var selected = new HashSet<string>(C(key, "conn,vol,bat").Split(','));
                if (on) selected.Add(id); else selected.Remove(id);
                cfg[key] = string.Join(",", selected); SaveCfg(); Rebuild(); Place(); return;
            }
            var l = Layout(); int i = l.FindIndex(x => x.Key == id);
            if (i < 0) l.Add(new KeyValuePair<string, bool>(id, on)); else l[i] = new KeyValuePair<string, bool>(id, on);
            SaveLayout(l);
        }

        // ---------- модули ----------
        // live = false: превью для каталога — без действий, данные из s
        UIElement MakeWidget(string id, Snap s, bool live, List<Action> up)
        {
            double th = TileH, f = Fs; int v = View(id);
            Func<Action, Action> act = a => live ? a : null;
            Func<Action<ContextMenu>, Action<ContextMenu>> menu = m => live ? WMenu(id, m) : null;
            switch (id)
            {
                case "cpu":
                case "ram":
                    {
                        bool cpu = id == "cpu";
                        // число и подпись «ЦП» / «ОЗУ» внутри кольца — два кольца рядом не спутать; в расширенном виде справа подробности
                        var g = new Gauge(th - 4, Math.Max(3.5, th / 10), -90, 360, Math.Max(10, (th - 4) * 0.3), cpu ? "ЦП" : "ОЗУ");
                        var meter = BoardTheme ? new DotMeter(8, 42 * f) : null;
                        var boardValue = L("", 12 * f, true);
                        TextBlock det = L("", 11 * f, true);
                        UIElement gauge = meter == null ? (UIElement)g : VS(boardValue, meter);
                        UIElement content = v >= 2 ? HS(gauge, Mg(VS(Cap(cpu ? "Процессор" : "Память", f), det), 6 * f, 0, 2, 0)) : gauge;
                        var t = Tile(content, cpu ? "Процессор — подробности и история" : "Оперативная память — подробности и история", act(() => { sysMetric = cpu ? 0 : 1; ShowSystem(); }), menu(m => m.Items.Add(Item("Диспетчер задач", () => Launch("taskmgr.exe")))), true);
                        up.Add(() =>
                        {
                            double p = cpu ? s.Cpu : s.Ram;
                            g.Set(p / 100, double.IsNaN(p) ? "—" : Math.Round(p).ToString());
                            if (meter != null)
                            {
                                meter.Update(p / 100, LevelBrush(p) ?? Accent);
                                boardValue.Text = (cpu ? "ЦП " : "ОЗУ ") + (double.IsNaN(p) ? "—" : Math.Round(p) + "%");
                            }
                            det.Text = cpu ? s.Threads + " потоков" : Gb(s.RamUsed) + " из " + Gb(s.RamTotal);
                            t.ToolTip = (cpu ? "Процессор: " : "Память: ") + (double.IsNaN(p) ? "—" : Math.Round(p) + "%") + (cpu ? "" : " · " + Gb(s.RamUsed) + " из " + Gb(s.RamTotal)) + "\nКлик — подробности и история";
                        });
                        return t;
                    }
                case "temp":
                    {
                        double gd = v == 0 ? th - 4 : Math.Round(th * 0.66); // рядом с ЦП и ОЗУ — компактная дуга, как на образце
                        var g = v == 0 ? new Gauge(gd, Math.Max(3, th / 11), 135, 270, Math.Max(9, gd * 0.27)) : new Gauge(gd, Math.Max(3, th / 17), 135, 270, Math.Max(9, gd * 0.34), "ЦП");
                        FillBar cb = new FillBar(30 * f, 3), rb = new FillBar(30 * f, 3);
                        TextBlock cpT = L("", 10 * f, true), rpT = L("", 10 * f, true); cpT.TextAlignment = rpT.TextAlignment = TextAlignment.Right;
                        var note = L("", 9 * f, false, 0.6);
                        Func<string, FillBar, TextBlock, FrameworkElement> row = (n, bar, pt) => HS(Wd(L(n, 9.5 * f, false, 0.7), 26 * f), bar, Mg(Wd(pt, 34 * f), 4 * f, 0, 0, 0));
                        UIElement content = v == 0 ? (UIElement)g : HS(g, Mg(VS(row("ЦП", cb, cpT), Mg(row("ОЗУ", rb, rpT), 0, 4 * f), v >= 2 ? Mg(note, 0, 2 * f) : null), 10 * f, 0, 2, 0));
                        var t = Tile(content, "Температура (датчик ACPI) и нагрузка", act(ShowTemp), menu(null), v > 0);
                        up.Add(() =>
                        {
                            g.Set(double.IsNaN(s.Temp) ? double.NaN : (s.Temp - 20) / 80, double.IsNaN(s.Temp) ? "—" : Math.Round(s.Temp) + "°");
                            g.Force = double.IsNaN(s.Temp) || s.Temp < 60 ? null : new SolidColorBrush(s.Temp >= 80 ? CRed : COrange); g.Set(double.IsNaN(s.Temp) ? double.NaN : (s.Temp - 20) / 80, g.Text.Text); // тёплая дуга — горячо
                            cb.Set(s.Cpu / 100); rb.Set(s.Ram / 100); cpT.Text = double.IsNaN(s.Cpu) ? "—" : Math.Round(s.Cpu) + "%"; rpT.Text = double.IsNaN(s.Ram) ? "—" : Math.Round(s.Ram) + "%"; note.Text = s.TempNote ?? "";
                            t.ToolTip = "Температура: " + (double.IsNaN(s.Temp) ? "нет данных" : Math.Round(s.Temp) + " °C") + " (" + (s.TempNote ?? "датчик ACPI") + ")\nЭто датчик платы, а не ядра процессора";
                        });
                        return t;
                    }
                case "net":
                    {
                        TextBlock dn = Wd(L("", 11 * f, true), 66 * f), upT = Wd(L("", 9.5 * f, false, 0.72), 66 * f);
                        var sp = new Spark(48 * f, th - 16);
                        var dots = new Border { Width = 54 * f, Height = th - 12, ClipToBounds = true };
                        UIElement content = v == 0 ? (UIElement)dn : v == 1 ? VS(dn, upT) : HS(VS(dn, upT), Mg(BoardTheme ? (FrameworkElement)dots : sp, 6 * f));
                        var t = Tile(content, "Сеть: приём и отдача", act(ShowNetworkDots), menu(m => m.Items.Add(Item("Параметры сети", () => Launch("ms-settings:network")))), true);
                        up.Add(() =>
                        {
                            dn.Text = "↓ " + RateRu(s.Rx); upT.Text = "↑ " + RateRu(s.Tx);
                            if (v < 2) return;
                            if (BoardTheme) dots.Child = NetworkDots(s.NetHist.Skip(Math.Max(0, s.NetHist.Count - 8)).ToList(), "Сеть", Accent);
                            else sp.Set(s.NetHist);
                        });
                        return t;
                    }
                case "disk":
                    {
                        // на панели — свободное место крупно и тонкая полоса заполнения; клик — своя карточка накопителя
                        var head = Cap("", f); var pctT = L("", 10 * f, true);
                        var dIcon = G("", Math.Round(10 * f)); dIcon.Margin = new Thickness(0, 0, 4 * f, -2 * f); dIcon.VerticalAlignment = VerticalAlignment.Bottom; // значок диска цвета заполнения, как на образце
                        var free = L("", 17.5 * f, true); var of = L("", 10 * f, false, 0.72);
                        double bw = (v == 0 ? 56 : v == 1 ? 104 : 132) * f; // образец: накопитель ≈ 1/10 ширины панели
                        var bar = new FillBar(bw, Math.Max(3, Math.Round(th / 14))); // на XL — заметная полоса, как на образце
                        var top = new DockPanel { Width = bw }; DockPanel.SetDock(pctT, Dock.Right); top.Children.Add(pctT); DockPanel.SetDock(dIcon, Dock.Left); top.Children.Add(dIcon); top.Children.Add(head);
                        UIElement content = v == 0 ? VS(free, Mg(bar, 0, 3 * f)) : VS(top, HS(free, Mg(of, 4 * f, 2 * f, 0, 0)), Mg(bar, 0, 2 * f));
                        var t = Tile(content, "Накопитель", act(ShowStorage), menu(m => { m.Items.Add(Item("Открыть диск", () => Launch(s.Disk + "\\"))); m.Items.Add(Item("Параметры хранилища", () => Launch("ms-settings:storagesense"))); }), true);
                        up.Add(() =>
                        {
                            double used = s.DiskTotal > 0 ? 1 - s.DiskFree / s.DiskTotal : double.NaN;
                            head.Text = "Диск " + s.Disk; pctT.Text = double.IsNaN(used) ? "—" : Math.Round(used * 100) + (v >= 2 ? "% занято" : "%");
                            free.Text = v == 0 ? s.Disk + " " + Gb(s.DiskFree) : Gb(s.DiskFree); of.Text = (v >= 2 ? "свободно из " : "из ") + Gb(s.DiskTotal);
                            bar.Set(used); dIcon.Foreground = LevelBrush(used * 100) ?? Accent;
                            t.ToolTip = (s.DiskLabel != "" ? s.DiskLabel + " " : "") + s.Disk + ": свободно " + Gb(s.DiskFree) + " из " + Gb(s.DiskTotal) + "\nКлик — подробности накопителя";
                        });
                        return t;
                    }
                case "bat":
                    {
                        var icon = new BatIcon(22 * f, 11 * f); var pct = L("", 11.5 * f, true); var sub = L("", 9.5 * f, false, 0.72);
                        var chargeBar = new FillBar(42 * f, 2);
                        var dots = BoardTheme ? new DotMeter(8, 42 * f) : null;
                        var readout = VS(pct, dots == null ? (UIElement)chargeBar : dots);
                        pct.FontSize = 14 * f;
                        UIElement content = v == 0 ? HS(icon, Mg(readout, 6 * f)) : HS(icon, Mg(VS(readout, sub), 7 * f));
                        var t = Tile(content, "Батарея", act(ShowBattery), menu(m => m.Items.Add(Item("Параметры питания", () => Launch("ms-settings:batterysaver")))), true);
                        bool? charging = null;
                        up.Add(() =>
                        {
                            t.Visibility = s.HasBat || !live ? Visibility.Visible : Visibility.Collapsed;
                            icon.Set(s.Bat, s.Charging); pct.Text = double.IsNaN(s.Bat) ? "—" : Math.Round(s.Bat * 100) + "%";
                            var chargeColor = s.Charging ? Frozen(Color.FromRgb(73, 164, 102), 255) : Accent;
                            if (dots != null) dots.Update(s.Bat, chargeColor); else chargeBar.Set(s.Bat, chargeColor);
                            if (charging == false && s.Charging) Soft(readout);
                            charging = s.Charging;
                            sub.Text = s.Charging ? "заряжается" : v >= 2 && s.BatSec > 0 ? "≈ " + Until(TimeSpan.FromSeconds(s.BatSec)) + ", оценка" : "от батареи";
                        });
                        return t;
                    }
                case "claude":
                case "codex":
                    {
                        bool cl = id == "claude";
                        if (live && !Directory.Exists(cl ? ClaudeUsage.Root : CodexLimits.Root)) return null;
                        var stale = G("\uE7BA", Math.Round(8 * f)); stale.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xA0, 0x30)); stale.HorizontalAlignment = HorizontalAlignment.Right; stale.VerticalAlignment = VerticalAlignment.Bottom; stale.Visibility = Visibility.Collapsed;
                        var icon = new Grid(); icon.Children.Add(AiTile(cl, th - 14)); icon.Children.Add(stale);
                        // вид: 0 — только главный показатель, 1 — строки без таймеров, 2 — строки и время до сброса.
                        // Строки Claude выбирает пользователь (до трёх, на S — до двух), у Codex — сессия и неделя.
                        var keys = cl ? ClSel.Take(SizeKey == "S" ? 2 : 3).ToList() : new List<string> { "c5", "cw" };
                        bool anyBar = keys.Any(IsPctRow);
                        var rows = new Grid { VerticalAlignment = VerticalAlignment.Center };
                        foreach (var w in new[] { 52.0, anyBar ? 30 : 0, anyBar ? 40 : 62, v < 2 ? 0 : 50 }) rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w * f) });
                        var vals = new List<TextBlock>(); var rsts = new List<TextBlock>(); var bars = new List<FillBar>();
                        for (int r = 0; r < keys.Count; r++)
                        {
                            rows.RowDefinitions.Add(new RowDefinition());
                            var lab = L(RowName(keys[r]), 10 * f, false, 0.72); var bar = new FillBar(26 * f, 3); var val = L("", 10.5 * f, true); var rst = L("", 10 * f, false, 0.6);
                            val.TextAlignment = TextAlignment.Right; val.HorizontalAlignment = HorizontalAlignment.Right; rst.Margin = new Thickness(6 * f, 0, 0, 0);
                            if (v < 2) rst.Visibility = Visibility.Collapsed;
                            if (!IsPctRow(keys[r])) bar.Visibility = Visibility.Collapsed;
                            int c = 0; foreach (var el in new FrameworkElement[] { lab, bar, val, rst }) { Grid.SetRow(el, r); Grid.SetColumn(el, c++); rows.Children.Add(el); }
                            vals.Add(val); rsts.Add(rst); bars.Add(bar);
                        }
                        // мини-вид: подпись и время до сброса, крупное значение, полоса — как на образце
                        var one = L("", 17 * f, true); var oneCap = Cap(RowName(keys[0]), f); var oneRst = L("", 10 * f, false, 0.6); var oneBar = new FillBar(72 * f, 3);
                        var oneTop = new DockPanel { Width = 72 * f }; DockPanel.SetDock(oneRst, Dock.Right); oneTop.Children.Add(oneRst); oneTop.Children.Add(oneCap);
                        if (!IsPctRow(keys[0])) oneBar.Visibility = Visibility.Collapsed;
                        var mini = VS(oneTop, one, Mg(oneBar, 0, 2 * f));
                        var t = Tile(HS(icon, Mg(v == 0 ? (FrameworkElement)mini : rows, 8 * f)), cl ? "Claude — клик для подробностей" : "Codex: лимиты — клик для подробностей", act(cl ? (Action)ShowClaude : ShowCodex),
                            menu(cl ? (Action<ContextMenu>)(mm =>
                            {
                                var show = new MenuItem { Header = "Показывать" }; var sel = ClSel;
                                foreach (var r in ClRows) { var k = r[0]; show.Items.Add(Item(r[1], () => SetClRow(k, !ClSel.Contains(k)), sel.Contains(k))); }
                                mm.Items.Add(show);
                                if (!ClaudeBridge.On) mm.Items.Add(Item("Подключить проценты лимитов…", ConnectClaudePct));
                                mm.Items.Add(Item("Открыть claude.ai → Использование", () => Launch("https://claude.ai/settings/usage")));
                            }) : null), true);
                        up.Add(() =>
                        {
                            double worst = 0; var tip = new List<string>();
                            for (int r = 0; r < keys.Count; r++)
                            {
                                var rv = RowVal(s, keys[r]); vals[r].Text = rv.Item2; rsts[r].Text = rv.Item3;
                                if (IsPctRow(keys[r])) { bars[r].Set(rv.Item1 / 100, LevelBrush(rv.Item1) ?? AiOk); Warn(vals[r], rv.Item1); if (!double.IsNaN(rv.Item1)) worst = Math.Max(worst, rv.Item1); } // приближение к лимиту — предупреждение прямо в блоке
                                tip.Add(RowName(keys[r]) + ": " + rv.Item2 + (rv.Item3 != "—" && rv.Item3 != "" && rv.Item3 != "сессия" && rv.Item3 != "7 дн." ? " · сброс через " + rv.Item3 : ""));
                            }
                            var strongest = (cl ? new[] { "5h", "week" } : new[] { "c5", "cw" }).Select(k => Tuple.Create(k, RowVal(s, k)))
                                .Where(p => !double.IsNaN(p.Item2.Item1)).OrderByDescending(p => p.Item2.Item1).FirstOrDefault();
                            var r0 = strongest == null ? RowVal(s, keys[0]) : strongest.Item2;
                            oneCap.Text = RowName(strongest == null ? keys[0] : strongest.Item1);
                            oneBar.Visibility = double.IsNaN(r0.Item1) ? Visibility.Collapsed : Visibility.Visible;
                            one.Text = r0.Item2; Warn(one, r0.Item1); oneRst.Text = r0.Item3 == "—" || r0.Item3 == "сессия" || r0.Item3 == "7 дн." ? "" : r0.Item3; oneBar.Set(r0.Item1 / 100, LevelBrush(r0.Item1) ?? AiOk);
                            bool old = cl ? s.ClPctStale && anyBar : s.CxStale;
                            rows.Opacity = one.Opacity = old ? 0.6 : 1; stale.Visibility = old ? Visibility.Visible : Visibility.Collapsed;
                            if (cl)
                                t.ToolTip = "Claude\n" + string.Join("\n", tip) + (anyBar && !s.ClBridge ? "\nПроценты появятся после подключения: ПКМ → «Подключить проценты лимитов»" : anyBar && s.ClPctSeen != null ? "\nПроценты от Claude Code · " + s.ClPctSeen : "") + "\nКлик — подробности · ПКМ — что показывать";
                            else
                                t.ToolTip = !s.CxData ? "Codex: данных пока нет — они появятся после первого ответа Codex CLI" : (s.CxStale ? "Данные устарели — последняя запись Codex " + s.CxSeen + "\n" : "") + "Codex\n" + string.Join("\n", tip) + "\n«ждём» — окно сброшено, новой записи ещё нет\nКлик — подробности";
                        });
                        return t;
                    }
                case "media":
                    {
                        double cd = th - 8;
                        var cover = new Border { Width = cd, Height = cd, CornerRadius = new CornerRadius(Math.Max(6, cd / 4)), VerticalAlignment = VerticalAlignment.Center };
                        var note = G("", cd * 0.42); note.Opacity = 0.7; cover.Child = note;
                        double tf = f > 1.3 ? 1.1 : f; // у XL шрифт крупнее, а место под название — как у L: музыка не вытесняет соседей
                        var title = Wd(L("", 11 * f, true), (v >= 2 ? 150 : 110) * tf); var artist = Wd(L("", 9.5 * f, false, 0.72), (v >= 2 ? 150 : 110) * tf);
                        var prog = new FillBar((v >= 2 ? 150 : 110) * tf, Math.Max(2.5, Math.Round(th / 24)));
                        TextBlock playG = null;
                        double pd = Math.Min(th - 14, Math.Round(th * 0.52 + 6)), nd = Math.Min(th - 16, Math.Round(th * 0.45 + 6)); // кнопки по пропорции образца: на XL не разрастаются
                        var play = RoundBtn("\uE768", pd, true, act(() => MediaCmd("toggle")), "Воспроизведение / пауза"); playG = (TextBlock)play.Child;
                        Press prevB = RoundBtn("\uE892", nd, false, act(() => MediaCmd("prev")), "Предыдущий трек"), nextB = RoundBtn("\uE893", nd, false, act(() => MediaCmd("next")), "Следующий трек");
                        foreach (var rb0 in new[] { prevB, nextB }) { rb0.Base = Fa(0x16); rb0.Background = new SolidColorBrush(rb0.Base); } // светлые круги, как на образце
                        var body = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                        body.Children.Add(cover);
                        var texts = Mg(VS(title, artist), 8 * f, 0, 6 * f, 0);
                        if (v >= 1) body.Children.Add(texts);
                        if (v >= 2) body.Children.Add(prevB);
                        Gauge ring = null; FrameworkElement playEl = play;
                        if (v == 0) { ring = new Gauge(th - 8, 2.4, -90, 360, 1) { Plain = true }; play.Width = play.Height = th - 16; var pg = new Grid { VerticalAlignment = VerticalAlignment.Center }; pg.Children.Add(ring); pg.Children.Add(play); playEl = pg; }
                        body.Children.Add(Mg(playEl, v == 0 ? 6 * f : 2, 0, 2, 0));
                        if (v >= 2) body.Children.Add(nextB);
                        // расширенный вид: полоса прогресса по нижнему краю модуля, во всю ширину — как на образце
                        FrameworkElement mediaContent = body;
                        if (v >= 2)
                        {
                            var mg = new Grid { Height = th - 2 }; mg.Children.Add(body);
                            prog.VerticalAlignment = VerticalAlignment.Bottom; prog.Margin = new Thickness(2, 0, 0, Math.Round(3 * f)); mg.Children.Add(prog);
                            mg.SizeChanged += (o, e) => prog.Resize(mg.ActualWidth - 4);
                            mediaContent = mg;
                        }
                        var t = Tile(mediaContent, "Музыка — подробности", act(ShowMusic), menu(m => { m.Items.Add(Item("Только Spotify", () => { cfg["media.spotifyOnly"] = SpotifyOnly ? "0" : "1"; SaveCfg(); mediaPick = null; lastMedia = null; UpdateMedia(); }, SpotifyOnly)); m.Items.Add(Item("Открыть Spotify", OpenSpotify)); m.Items.Add(Item("Микшер громкости", () => Launch("sndvol.exe"))); m.Items.Add(Item("Без звука", () => Keys(0xAD))); }), true);
                        t.Padding = new Thickness(4, 0, 6 * f, 0);
                        if (live) t.MouseWheel += (o, e) => { byte k = (byte)(e.Delta > 0 ? 0xAF : 0xAE); Keys(k); Keys(k); e.Handled = true; };
                        ImageSource shown = null; bool first = true; string shownTrack = null;
                        up.Add(() =>
                        {
                            var track = s.HasMedia ? s.Title + "\n" + s.Artist : "";
                            if (shownTrack != null && track != shownTrack) Soft(texts); shownTrack = track;
                            playG.Text = s.Playing ? "\uE769" : "\uE768";
                            Enable(play, !s.HasMedia || s.CanToggle); Enable(prevB, s.HasMedia && s.CanPrev); Enable(nextB, s.HasMedia && s.CanNext);
                            title.Text = s.HasMedia ? s.Title : SpotifyOnly ? (spotifyUp ? "Spotify: ничего не играет" : "Spotify не запущен") : "Ничего не играет";
                            artist.Text = s.HasMedia ? (s.Remote ? "На другом устройстве" : string.IsNullOrEmpty(s.Artist) ? AppTitle(s.App) : s.Artist) : SpotifyOnly ? (spotifyUp ? "Включите трек в Spotify" : "Нажмите ▶, чтобы открыть") : "Запустите музыку";
                            prog.Set(s.HasMedia && !double.IsNaN(s.Dur) ? s.Pos / s.Dur : double.NaN); prog.Squeeze(s.HasMedia && !s.Playing);
                            if (ring != null) ring.Set(s.HasMedia && !double.IsNaN(s.Dur) && s.Dur > 0 ? s.Pos / s.Dur : 0, "");
                            if (first || s.Cover != shown)
                            {
                                if (!first) Soft(cover);
                                first = false; shown = s.Cover;
                                if (s.Cover != null) { cover.Background = new ImageBrush(s.Cover) { Stretch = Stretch.UniformToFill }; cover.Child = null; }
                                else { var ac = AccentColor; cover.Background = new LinearGradientBrush(Color.FromArgb(0x60, ac.R, ac.G, ac.B), Color.FromArgb(0x20, ac.R, ac.G, ac.B), 45); cover.Child = note; }
                            }
                            t.ToolTip = s.HasMedia ? (string.IsNullOrEmpty(s.Artist) ? "" : s.Artist + " — ") + s.Title + "\nКолесо — громкость, клик — подробности" : "Ничего не играет";
                        });
                        return t;
                    }
                case "pomo":
                    {
                        var cap = Cap("Фокус", f, CRed); var big = L("25:00", 17 * f, true);
                        Action toggle = () => { PomoNext(); All(b => b.Render()); };
                        var btn = RoundBtn("\uE768", th - 14, true, act(toggle), "Старт или следующий этап", CRed);
                        var t = Tile(HS(v == 0 ? (UIElement)big : VS(cap, big), Mg(btn, (v == 0 ? 8 : 12) * f)), "Фокус: клик — старт или следующий этап, ПКМ — сброс", act(toggle),
                            menu(m => m.Items.Add(Item("Сбросить", () => { PomoReset(); All(b => b.Render()); }))), true);
                        var red = new SolidColorBrush(CRed); var edge = t.BorderBrush;
                        up.Add(() =>
                        {
                            var left = s.PomoLeft < TimeSpan.Zero ? TimeSpan.Zero : s.PomoLeft;
                            big.Text = ((int)left.TotalMinutes).ToString("00") + ":" + left.Seconds.ToString("00");
                            cap.Text = s.PomoMode == 0 ? "Фокус" : (s.PomoMode == 1 ? "Фокус" : "Перерыв") + " · " + Math.Max(1, s.PomoRound);
                            ((TextBlock)btn.Child).Text = s.PomoMode == 0 ? "\uE768" : "\uE893";
                            // идёт фокус — модуль в красной рамке, как на образце
                            if (!t.IsKeyboardFocused) { t.BorderBrush = s.PomoMode == 1 ? red : edge; t.BorderThickness = new Thickness(s.PomoMode == 1 ? 2 : 1); }
                        });
                        return t;
                    }
                case "todos":
                    {
                        var box = new Press { Width = 15 * f, Height = 15 * f, CornerRadius = new CornerRadius(4 * f), BorderThickness = new Thickness(1.4), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Отметить выполненной" };
                        box.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Window), 1) });
                        System.Windows.Automation.AutomationProperties.SetName(box, "Отметить ближайшую задачу выполненной");
                        Action done = () => { var nt = NextDueTodo(); if (nt != null) { nt.Done = true; SaveTodos(); } };
                        box.Invoke = act(done); box.MouseLeftButtonUp += (o, e) => { e.Handled = true; if (live) done(); };
                        var text = Wd(L("", 11 * f, false), (v == 0 ? 72 : v == 1 ? 130 : 210) * f); var cnt = L("", 9 * f, false, 0.65); var bar = new FillBar(40 * f, 3);
                        var prog = Mg(HS(cnt, Mg(bar, 6 * f)), 0, 3 * f);
                        var t = Tile(HS(box, Mg(VS(text, prog), 8 * f)), "Задачи (Alt+T)", act(ShowTodos), menu(null), true);
                        up.Add(() =>
                        {
                            prog.Visibility = s.TodoTotal > 0 ? Visibility.Visible : Visibility.Collapsed;
                            text.Text = s.NextTodo ?? (s.TodoTotal > 0 ? "Всё сделано" : "Нет задач");
                            box.Visibility = s.NextTodo != null ? Visibility.Visible : Visibility.Collapsed;
                            cnt.Text = s.TodoTotal > s.TodoDone ? "ещё " + (s.TodoTotal - s.TodoDone - 1) : "готово";
                            bar.Set(s.TodoTotal == 0 ? double.NaN : (double)s.TodoDone / s.TodoTotal, Accent);
                        });
                        return t;
                    }
                case "notes":
                    {
                        var text = Wd(L("", 11 * f, false, 0.85), (v >= 2 ? 210 : 120) * f); if (v == 0) text.Visibility = Visibility.Collapsed; // компактный — только значок
                        var t = Tile(HS(G(""), Mg(text, 8 * f)), "Заметки", act(ShowNotes), menu(null), true);
                        up.Add(() => text.Text = string.IsNullOrEmpty(s.Note) ? "Новая заметка" : s.Note);
                        return t;
                    }
                case "event":
                    {
                        double bd = th - 8;
                        var day = L("", bd * 0.42, true); var wd = L("", Math.Max(7, bd * 0.2), false, 0.9);
                        day.HorizontalAlignment = wd.HorizontalAlignment = HorizontalAlignment.Center; day.Foreground = wd.Foreground = Frozen(OnAccent, 255);
                        var block = new Border { Width = bd, Height = bd, CornerRadius = new CornerRadius(bd / 4), Background = Accent, Child = VS(wd, day), VerticalAlignment = VerticalAlignment.Center };
                        double ew = (v >= 2 ? 184 : 140) * f;
                        var title = Wd(L("", 11 * f, true), ew); var when = Wd(L("", 10 * f, false, 0.72), v == 0 ? 78 * f : ew);
                        var t = Tile(HS(block, Mg(v == 0 ? (FrameworkElement)when : VS(title, when), 8 * f)), null, act(ShowCalendar), menu(m => m.Items.Add(Item("Обновить календари", UpdateCalendars))), true);
                        t.Padding = new Thickness(4, 0, 10 * f, 0); t.BorderThickness = new Thickness(1.5); t.BorderBrush = ChipEdge;
                        up.Add(() =>
                        {
                            var now = DateTime.Now; day.Text = now.Day.ToString(); wd.Text = now.ToString("ddd").ToUpper();
                            var n = s.Next;
                            // встреча скоро (10 минут) — мягкая рамка акцентом, без мигания
                            bool soon = n != null && n.Start > now && n.Start - now <= TimeSpan.FromMinutes(10);
                            if (!t.IsKeyboardFocused) t.BorderBrush = soon ? Accent : ChipEdge;
                            if (n == null) { title.Text = "Событий нет"; when.Text = v == 0 ? "нет событий" : calFeeds.Count == 0 && live ? "добавьте календарь" : "свободно до конца дня"; t.ToolTip = "Календарь"; return; }
                            title.Text = n.Title;
                            when.Text = v == 0 ? (n.Start <= now ? "идёт" : "через " + Until(n.Start - now))
                                : n.Start <= now ? "идёт · ещё " + Until(n.End - now) : n.Start.ToString("HH:mm") + " · через " + Until(n.Start - now);
                            t.ToolTip = n.Title + "\n" + n.Start.ToString("ddd, d MMM") + " · " + When(n) + (string.IsNullOrEmpty(n.Location) ? "" : "\n" + n.Location) + (n.Cal != null ? "\n" + n.Cal : "");
                        });
                        return t;
                    }
                case "screen":
                    {
                        int nCols = v >= 2 ? 12 : 8; var dur = L("", 18 * f, true); var cols = new Cols(nCols, 3.5 * f, th - 18); if (v == 0) cols.Visibility = Visibility.Collapsed;
                        var stIcon = G("\uE823", Math.Round(16 * f)); stIcon.Foreground = new SolidColorBrush(CBlue); stIcon.Margin = new Thickness(0, 0, 8 * f, 0); if (v == 0) stIcon.Visibility = Visibility.Collapsed;
                        var t = Tile(HS(stIcon, VS(Cap("Экранное время", f), dur), Mg(cols, 10 * f)), "Экранное время за сегодня", act(ShowScreenTime), menu(null), true);
                        up.Add(() =>
                        {
                            dur.Text = ScreenTime.Fmt(s.ScreenSec);
                            int h = DateTime.Now.Hour; var vals = new List<double>();
                            for (int i = h - nCols + 1; i <= h; i++) vals.Add(i < 0 ? 0 : s.Hours[i]);
                            cols.Set(vals, nCols - 1);
                        });
                        return t;
                    }
                case "weather":
                    {
                        var host = new Border { Width = th - 12, Height = th - 12, VerticalAlignment = VerticalAlignment.Center };
                        var wstale = G("\uE7BA", Math.Round(8 * f)); wstale.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xA0, 0x30)); wstale.HorizontalAlignment = HorizontalAlignment.Right; wstale.VerticalAlignment = VerticalAlignment.Bottom;
                        var hostG = new Grid(); hostG.Children.Add(host); hostG.Children.Add(wstale);
                        var temp = L("", 13 * f, true); var city = Wd(L("", 9.5 * f, false, 0.72), 96 * f); var desc = L("", 9.5 * f, false, 0.72);
                        var rainTime = L("", 9.5 * f, false, 0.78);
                        UIElement content = v == 0 ? HS(hostG, Mg(VS(temp, rainTime), 6 * f)) : v == 1 ? HS(hostG, Mg(VS(temp, city), 7 * f)) : HS(hostG, Mg(VS(HS(temp, Mg(desc, 6 * f)), city), 7 * f));
                        var t = Tile(content, "Погода", act(ShowWeather), menu(m => m.Items.Add(Item("Обновить", UpdateWeather))), true);
                        string kind = null;
                        up.Add(() =>
                        {
                            if (kind != s.WxKind || host.Child == null) { kind = s.WxKind; host.Child = WxIcon(s.WxOk ? s.WxKind : "cloud", th - 12); }
                            host.Opacity = s.WxOk ? 1 : 0.5; wstale.Visibility = s.WxStale ? Visibility.Visible : Visibility.Collapsed;
                            temp.Text = s.WxTemp; desc.Text = s.WxOk ? s.WxRange : "";
                            int rainHour = s.WxOk ? NextRain(wx) : -1;
                            rainTime.Text = rainHour < 0 ? "" : "☂ " + wx.HTime[rainHour].ToString("HH:mm");
                            rainTime.Visibility = rainHour < 0 ? Visibility.Collapsed : Visibility.Visible;
                            var rain = RainSummary(wx);
                            city.Text = s.WxOk && rain.Contains(" · ") ? rain : s.WxCity ?? "Выберите город";
                            if (v >= 2 && s.WxOk && !rain.Contains(" · ")) city.Text = (s.WxCity ?? "") + " · " + s.WxDesc;
                            t.ToolTip = s.WxOk ? (s.WxStale ? "Нет связи · данные от " + s.WxAge + "\n" : "") + (s.WxCity ?? "") + ": " + s.WxDesc + ", " + s.WxTemp + " (" + s.WxRange + ")" : "Погода — " + s.WxDesc;
                        });
                        return t;
                    }
                case "clock":
                    {
                        bool analog = C("clock.analog", "0") == "1", flip = C("clock.flip", "0") == "1"; var tz = C("clock.tz2", ""); TimeZoneInfo zone = null;
                        var zdef = Zones.FirstOrDefault(z => z[0] == tz && tz != ""); if (zdef != null) try { zone = TimeZoneInfo.FindSystemTimeZoneById(tz); } catch { }
                        var time = L("", (v >= 2 ? 16 : 13.5) * f, true); var date = L("", 9.5 * f, false, 0.72);
                        time.HorizontalAlignment = date.HorizontalAlignment = HorizontalAlignment.Center;
                        var dial = analog ? new Dial(th - 12) : null;
                        var fc = flip ? new FlipClock(v == 0 ? th - 12 : th - 22) : null; // перекидные цифры на плашках
                        FrameworkElement timeEl = fc != null ? (FrameworkElement)fc : time;
                        bool plate = C("clock.plate", "0") == "1";
                        if (plate && fc == null) { time.Foreground = Brushes.White; timeEl = new Border { Background = new SolidColorBrush(CRed), CornerRadius = new CornerRadius(7 * f), Padding = new Thickness(7 * f, 1, 7 * f, 2), Child = time, HorizontalAlignment = HorizontalAlignment.Center }; } // время на красной плашке, как на образце
                        var wxT = L("", 9 * f, true); var wxDot = new Border { Width = 22 * f, Height = 22 * f, CornerRadius = new CornerRadius(11 * f), Background = new SolidColorBrush(Fa(0x30)), Child = wxT, Margin = new Thickness(6 * f, 0, 0, 0), Visibility = plate ? Visibility.Visible : Visibility.Collapsed };
                        wxT.HorizontalAlignment = HorizontalAlignment.Center;
                        var daylight = C("clock.daylight", "0") == "1" ? DaylightArc() : null;
                        FrameworkElement main = daylight != null ? VS(timeEl, daylight) : v == 0 ? timeEl : VS(timeEl, plate ? (FrameworkElement)HS(date, wxDot) : date);
                        UIElement content = dial == null ? main : v == 0 ? (UIElement)dial : HS(dial, Mg(main, 8 * f));
                        TextBlock zt = null;
                        if (zone != null && v >= 1)
                        {
                            zt = L("", 11.5 * f, true); var zl = L(zdef[1], 9.5 * f, false, 0.72); zt.HorizontalAlignment = zl.HorizontalAlignment = HorizontalAlignment.Center;
                            var line = new Rectangle { Width = 1, Height = th * 0.5, Opacity = 0.2, Margin = new Thickness(10 * f, 0, 10 * f, 0), VerticalAlignment = VerticalAlignment.Center }; FgBind(line, Shape.FillProperty);
                            content = HS(content, line, VS(zt, zl));
                        }
                        var t = Tile(content, null, act(ShowCalendar), menu(m =>
                        {
                            m.Items.Add(Item("Циферблат", () => { cfg["clock.analog"] = analog ? "0" : "1"; SaveCfg(); RebuildAll(); }, analog));
                            m.Items.Add(Item("Перекидные цифры", () => { cfg["clock.flip"] = flip ? "0" : "1"; SaveCfg(); RebuildAll(); }, flip));
                            m.Items.Add(Item("Время на плашке", () => { cfg["clock.plate"] = plate ? "0" : "1"; SaveCfg(); RebuildAll(); }, plate));
                            m.Items.Add(Item("Дуга светового дня", () => { cfg["clock.daylight"] = daylight == null ? "1" : "0"; SaveCfg(); RebuildAll(); }, daylight != null));
                            var zm = new MenuItem { Header = "Второй часовой пояс" };
                            foreach (var z in Zones) { var zz = z; zm.Items.Add(Item(z[1], () => { if (zz[0] == "") cfg.Remove("clock.tz2"); else cfg["clock.tz2"] = zz[0]; SaveCfg(); RebuildAll(); }, tz == z[0])); }
                            m.Items.Add(zm);
                        }), true);
                        t.Padding = new Thickness(Math.Round(th * 0.14), 0, Math.Round(th * 0.14), 0); // часы — плотнее, без широких полей
                        up.Add(() =>
                        {
                            var now = DateTime.Now; time.Text = now.ToString("HH:mm");
                            if (daylight != null) UpdateDaylight(daylight);
                            date.Text = v >= 2 ? now.ToString("dddd, d MMMM") : now.ToString("ddd, d MMM");
                            if (dial != null) dial.Set(now); if (fc != null) fc.Set(now.ToString("HHmm")); wxT.Text = s.WxOk ? s.WxTemp.Replace("+", "") : "—"; wxDot.ToolTip = s.WxOk ? s.WxDesc : null;
                            string zs = null; if (zone != null) { zs = TimeZoneInfo.ConvertTime(now, zone).ToString("HH:mm"); if (zt != null) zt.Text = zs; }
                            t.ToolTip = now.ToString("HH:mm, dddd, d MMMM yyyy") + (zs != null ? "\n" + zdef[1] + " — " + zs : "") + "\nКлик — календарь";
                        });
                        return t;
                    }
                case "fx":
                    {
                        // сумма подписью, результат крупно; курс — из общего кэша конвертера
                        var cap = Cap("", f); var val = L("…", 15 * f, true);
                        var t = Tile(VS(cap, val), "Валюта — клик: выбор валют и сумма", act(ShowFx), menu(null), true);
                        if (live && rates == null && !ratesBusy) LoadRates().ContinueWith(x => Dispatcher.BeginInvoke(new Action(() => All(b => b.Render()))));
                        up.Add(() =>
                        {
                            string fr = C("cur.from", "USD"), to = C("cur.to", "EUR"); double a; double.TryParse(C("cur.amount", "100").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out a);
                            var r = rates ?? (live ? null : new Dictionary<string, double> { { "USD", 1 }, { "EUR", 0.92 } });
                            cap.Text = a.ToString("0.##") + " " + fr;
                            val.Text = r == null ? (ratesBusy ? "загрузка…" : "нет курса") : !r.ContainsKey(fr) || !r.ContainsKey(to) ? "нет курса" : ConvertAmount(a, fr, to, r).ToString("N2") + " " + to;
                            t.ToolTip = r == null ? "Курсы ещё не загружены" + (ratesError != null ? " · нет связи: " + ratesError : "") : "Курс от " + ratesAt.ToString("d MMM, HH:mm") + " · open.er-api.com — не котировка в реальном времени\nКлик — выбор валют и сумма";
                        });
                        return t;
                    }
                case "tools":
                    {
                        var lab = L("", 9 * f, false, 0.72); var tm = L("", 11.5 * f, true); var stat = Mg(VS(lab, tm), 8 * f);
                        var t = Tile(HS(G(""), stat), "Инструменты: таймер, секундомер, валюта, файлы", act(() => ShowTools(null)), menu(null), true);
                        up.Add(() => { stat.Visibility = s.ToolLabel != null ? Visibility.Visible : Visibility.Collapsed; lab.Text = s.ToolLabel ?? ""; tm.Text = s.ToolTime ?? ""; });
                        return t;
                    }
                case "bin":
                    {
                        var g = G("");
                        var t = Tile(g, "Корзина", act(() => Launch("shell:RecycleBinFolder")),
                            menu(m => { m.Items.Add(Item("Открыть корзину", () => Launch("shell:RecycleBinFolder"))); m.Items.Add(Item("Очистить корзину…", () => { N.SHEmptyRecycleBin(hwnd, null, 0); UpdateBin(); })); m.Items.Add(Item("Спрашивать перед удалением", () => { cfg["binConfirm"] = C("binConfirm", "1") == "1" ? "0" : "1"; SaveCfg(); }, C("binConfirm", "1") == "1")); })); // Windows сам спросит подтверждение
                        if (live)
                        {
                            t.AllowDrop = true; t.Drop += BinDrop;
                            // файлы над корзиной: подсветка и подсказка, сколько объектов удалится
                            t.DragEnter += (o, e) =>
                            {
                                var fs = e.Data.GetData(DataFormats.FileDrop) as string[]; if (fs == null) return;
                                Fade(t, Color.FromArgb(0x66, AccentColor.R, AccentColor.G, AccentColor.B), 100); g.Opacity = 1;
                                t.ToolTip = "Удалить в корзину: " + fs.Length + (fs.Length == 1 ? " объект" : fs.Length < 5 ? " объекта" : " объектов"); tipOn = true; TipMove(t, (string)t.ToolTip);
                            };
                            t.DragOver += (o, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Move; e.Handled = true; } };
                            t.DragLeave += (o, e) => { Fade(t, t.Base, 140); TipHide(true); };
                            t.Drop += (o, e) => { Fade(t, t.Base, 140); TipHide(true); };
                        }
                        up.Add(() =>
                        {
                            g.Opacity = s.BinItems > 0 ? 1 : 0.55; // пустая корзина — приглушённый значок
                            t.ToolTip = s.BinItems <= 0 ? "Корзина пуста\nПеретащите файлы сюда, чтобы удалить" : "Корзина: " + s.BinItems + " шт., " + Bytes(s.BinBytes) + "\nКлик — открыть · ПКМ — очистить · перетащите файлы, чтобы удалить";
                        });
                        return t;
                    }
                case "tray": return Tile(G(""), "Показать трей Windows на 15 секунд", act(TogglePeek), menu(null));
                case "quick": return Tile(G(""), "Быстрые переключатели: Wi-Fi, Bluetooth, звук, яркость", act(ShowQuick), menu(m => m.Items.Add(Item("Быстрые настройки Windows (Win+A)", () => ShellToggle(0x5B, 0x41)))));
                case "notif": return Tile(G(""), "Уведомления и календарь Windows (Win+N)", act(() => ShellToggle(0x5B, 0x4E)), menu(null)).Shell();
                case "lang":
                    {
                        var lt = L("", 11.5 * f, true); lt.HorizontalAlignment = HorizontalAlignment.Center;
                        var t = Tile(lt, "Раскладка клавиатуры — клик меняет", act(NextLang), menu(null));
                        up.Add(() => lt.Text = s.Lang);
                        return t;
                    }
                case "pday":
                case "pmonth":
                case "pyear":
                    {
                        // точка — час суток, день месяца или неделя года: прошедшие закрашены, текущая — наполовину
                        bool yr = id == "pyear";
                        double dd = Math.Max(4, Math.Min(7.3 * f, (th - 12 * f - 10) / 3.9)); // образец: 3 ряда по 10–11 точек
                        var dots = yr ? new Dots(4, 4 * f, 1.6 * f) : new Dots(3, dd, dd * 0.46);
                        var lab = L("", 10 * f, false, 0.72); var pct = L("", 10 * f, true);
                        var foot = new DockPanel { Margin = new Thickness(0, 3 * f, 0, 0) }; DockPanel.SetDock(pct, Dock.Right); foot.Children.Add(pct); foot.Children.Add(lab);
                        var t = Tile(VS(dots, foot), null, act(ShowCalendar), menu(null), true);
                        up.Add(() =>
                        {
                            var now = DateTime.Now; int total, done; double frac; string name;
                            if (id == "pday") { total = 24; done = now.Hour; frac = now.TimeOfDay.TotalHours / 24; name = now.ToString("d MMM"); }
                            else if (id == "pmonth") { total = DateTime.DaysInMonth(now.Year, now.Month); done = now.Day - 1; frac = (done + now.TimeOfDay.TotalHours / 24) / total; name = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(now.Month); }
                            else { int days = DateTime.IsLeapYear(now.Year) ? 366 : 365; total = 52; done = Math.Min(51, (now.DayOfYear - 1) / 7); frac = (now.DayOfYear - 1 + now.TimeOfDay.TotalHours / 24) / days; name = now.Year.ToString(); }
                            dots.Set(total, done); lab.Text = name; pct.Text = Math.Floor(frac * 100) + "%"; foot.Width = dots.Width;
                            t.ToolTip = (id == "pday" ? "Прошло " + done + " ч из 24 — точка = час" : id == "pmonth" ? "Прошло дней: " + done + " из " + total + " — точка = день" : "Идёт неделя " + (done + 1) + " из 52 — точка = неделя")
                                + "\nПрошло " + Math.Round(frac * 100, 1) + "% периода";
                        });
                        return t;
                    }
                case "timer":
                case "stopwatch":
                    {
                        // основные действия прямо на панели; цифры фиксированной ширины не двигают соседей
                        bool tm = id == "timer";
                        var lab = Cap("", f, tm ? CBlue : COrange);
                        var big = Wd(L("", 17 * f, true), 70 * f);
                        var play = RoundBtn("\uE768", th - 14, true, act(() => { if (tm) TimerToggle(); else if (sw.IsRunning) sw.Stop(); else sw.Start(); All(b => b.Render()); }), tm ? "Старт или пауза таймера" : "Старт или пауза секундомера", tm ? CBlue : COrange);
                        var reset = RoundBtn("\uE72C", th - 18, false, act(() => { if (tm) TimerReset(); else StopwatchReset(); All(b => b.Render()); }), "Сброс");
                        var t = Tile(v == 0 ? HS(big, Mg(play, 6 * f, 0, 0, 0)) : HS(VS(lab, big), Mg(reset, 6 * f), Mg(play, 4, 0, 0, 0)), tm ? "Таймер — клик: длительность и пресеты" : "Секундомер — клик: круги и история", act(() => ShowTools(tm ? "timer" : "stopwatch")),
                            menu(tm ? (Action<ContextMenu>)(m => { foreach (var mi in new[] { 1, 5, 10, 15, 25, 45 }) { var mm = mi; m.Items.Add(Item(mm + " мин", () => { TimerReset(); cfg["timer.last"] = (mm * 60).ToString(); SaveCfg(); TimerToggle(); All(b => b.Render()); })); } }) : null), true);
                        up.Add(() =>
                        {
                            bool run = tm ? timerRunning : sw.IsRunning, paused = tm ? !run && timerLeft > TimeSpan.Zero : !run && sw.Elapsed > TimeSpan.Zero;
                            big.Text = tm ? (timerRunning ? Span(timerEnd - DateTime.Now) : timerLeft > TimeSpan.Zero ? Span(timerLeft) : Span(TimeSpan.FromSeconds(TimerLast))) : Span(sw.Elapsed);
                            ((TextBlock)play.Child).Text = run ? "\uE769" : "\uE768";
                            Enable(reset, run || paused);
                            lab.Text = (tm ? "Таймер" : "Секундомер") + (run ? " · идёт" : paused ? " · пауза" : "");
                        });
                        return t;
                    }
                case "vol":
                    {
                        // устройство вывода, ползунок и выключение звука; колесо — громкость
                        var mb = RoundBtn("\uE767", th - 14, false, act(() => { Audio.SetMute(!s.Muted); s.Muted = !s.Muted; All(b => b.Render()); }), "Выключить или включить звук"); var mg = (TextBlock)mb.Child;
                        double bw = (v >= 2 ? 124 : 88) * f;
                        var bar = new FillBar(bw, 4); var hit = new Border { Background = Brushes.Transparent, Height = 14, Child = bar, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
                        var name = L("", 10 * f, false, 0.72); var pct = L("", 10.5 * f, true);
                        var top = new DockPanel { Width = bw }; if (v > 0) { DockPanel.SetDock(pct, Dock.Right); top.Children.Add(pct); top.Children.Add(name); } // в мини-виде процент стоит у значка
                        UIElement content = v == 0 ? HS(mb, Mg(pct, 4 * f, 0, 4 * f, 0)) : HS(mb, Mg(VS(top, hit), 6 * f));
                        var t = Tile(content, "Громкость", act(() => { if (C("widget.mixer", "1") == "1") ShowAppMixer(); else ShowQuick(); }), menu(m => { var dm = new MenuItem { Header = "Устройство вывода" }; DeviceItems(dm); if (dm.Items.Count > 1) m.Items.Add(dm); m.Items.Add(Item("Звук при изменении громкости", () => { cfg["volBeep"] = C("volBeep", "1") == "1" ? "0" : "1"; SaveCfg(); }, C("volBeep", "1") == "1")); m.Items.Add(Item("Микшер Flowbar", ShowAppMixer)); m.Items.Add(Item("Микшер Windows", () => Launch("sndvol.exe"))); m.Items.Add(Item("Параметры звука", () => Launch("ms-settings:sound"))); }), true);
                        bool drag = false; var gray = new SolidColorBrush(Color.FromArgb(0x90, 0x90, 0x90, 0x98));
                        Func<MouseEventArgs, float> at = e => (float)Math.Max(0, Math.Min(1, e.GetPosition(bar).X / bar.Width));
                        if (live)
                        {
                            hit.PreviewMouseLeftButtonDown += (o, e) => dragTok = null; // ползунок, а не перестановка модуля
                            hit.MouseLeftButtonDown += (o, e) => { drag = true; hit.CaptureMouse(); var x = at(e); Audio.SetVolume(x); bar.Set(x, Accent, true); e.Handled = true; };
                            hit.MouseMove += (o, e) => { if (!drag) return; var x = at(e); Audio.SetVolume(x); bar.Set(x, Accent, true); };
                            hit.MouseLeftButtonUp += (o, e) => { if (!drag) return; drag = false; hit.ReleaseMouseCapture(); e.Handled = true; ReadVolume(); All(b => b.Render()); VolBeep(); };
                            t.MouseWheel += (o, e) => { if (double.IsNaN(s.Vol)) return; Audio.SetVolume((float)Math.Max(0, Math.Min(1, s.Vol + (e.Delta > 0 ? 0.02 : -0.02)))); ReadVolume(); All(b => b.Render()); VolBeep(); e.Handled = true; };
                        }
                        up.Add(() =>
                        {
                            bool none = double.IsNaN(s.Vol);
                            mg.Text = none || s.Muted || s.Vol <= 0.001 ? "\uE74F" : s.Vol > 0.66 ? "\uE995" : s.Vol > 0.33 ? "\uE994" : "\uE993";
                            pct.Text = none ? "—" : s.Muted ? "без звука" : Math.Round(s.Vol * 100) + "%";
                            name.Text = none ? "Нет устройства" : ShortDev(s.VolName);
                            if (!drag) bar.Set(none ? 0 : s.Vol, s.Muted ? (Brush)gray : Accent);
                            t.ToolTip = none ? "Устройство вывода не найдено" : (s.VolName ?? "Устройство вывода") + " — " + (s.Muted ? "звук выключен" : Math.Round(s.Vol * 100) + "%") + "\nКолесо — громкость · значок — без звука · клик — быстрые переключатели";
                        });
                        return t;
                    }
                case "conn":
                    {
                        // веер Wi-Fi с делениями сигнала, значок кабеля или сотовой; без интернета — оранжевый знак, без сети — крестик
                        double gs = Math.Round(th * 0.42);
                        var back = G("\uE701", gs); back.Opacity = 0.25; var front = G("\uE701", gs);
                        var mark = G("\uE7BA", Math.Round(gs * 0.55)); mark.HorizontalAlignment = HorizontalAlignment.Right; mark.VerticalAlignment = VerticalAlignment.Bottom; mark.Margin = new Thickness(0, 0, -3, -2); mark.Visibility = Visibility.Collapsed;
                        var ic = new Grid { VerticalAlignment = VerticalAlignment.Center }; ic.Children.Add(back); ic.Children.Add(front); ic.Children.Add(mark);
                        double nw = (v >= 2 ? 136 : 100) * f;
                        var name = Wd(L("", 11 * f, true), nw); var stat = Wd(L("", 10 * f, false, 0.72), nw);
                        UIElement content = v == 0 ? (UIElement)ic : HS(ic, Mg(VS(name, stat), 8 * f));
                        var t = Tile(content, "Сеть", act(ShowWifi),
                            menu(m => { m.Items.Add(Item("Список сетей Windows", () => Launch("ms-availablenetworks:"))); m.Items.Add(Item("Параметры сети и интернета", () => Launch("ms-settings:network"))); m.Items.Add(Item("Состояние сети", () => Launch("ms-settings:network-status"))); }), v > 0);
                        up.Add(() =>
                        {
                            int k = s.NetKind; bool fan = k == 2 || k <= 0;
                            front.Text = k == 1 ? "\uE839" : k == 3 ? "\uE8EA" : s.NetBars >= 4 || s.NetBars < 0 ? "\uE701" : s.NetBars == 3 ? "\uE874" : s.NetBars == 2 ? "\uE873" : "\uE872";
                            back.Visibility = fan ? Visibility.Visible : Visibility.Collapsed; front.Visibility = k <= 0 ? Visibility.Hidden : Visibility.Visible;
                            bool noNet = k == 0, limited = k > 0 && s.NetLevel < 3;
                            mark.Text = noNet ? "\uE711" : "\uE7BA"; mark.Visibility = noNet || limited ? Visibility.Visible : Visibility.Collapsed;
                            if (limited) mark.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xA0, 0x30)); else mark.ClearValue(TextBlock.ForegroundProperty);
                            name.Text = k > 0 ? (string.IsNullOrEmpty(s.NetName) ? ConnKind(k) : s.NetName) : ConnKind(k);
                            stat.Text = k > 0 ? ConnKind(k) + " · " + ConnAccess(k, s.NetLevel) : ConnAccess(k, s.NetLevel);
                            Warn(stat, limited ? 80 : 0);
                            t.ToolTip = (k > 0 ? ConnKind(k) + (s.NetVpn ? " + VPN" : "") + (string.IsNullOrEmpty(s.NetName) ? "" : ": " + s.NetName) + (k == 2 && s.NetBars >= 0 ? ", сигнал " + s.NetBars + " из 5" : "") + "\n" + char.ToUpper(ConnAccess(k, s.NetLevel)[0]) + ConnAccess(k, s.NetLevel).Substring(1) : ConnKind(k))
                                + "\nКлик — карточка сети · ПКМ — параметры";
                        });
                        return t;
                    }
                case "radios":
                    {
                        var panel = new StackPanel { Orientation = Orientation.Horizontal };
                        if (!live) { panel.Children.Add(Tile(G(""), "Wi-Fi", null, null)); panel.Children.Add(Tile(G(""), "Bluetooth", null, null)); return panel; }
                        radioTiles.Clear();
                        var pills = new List<Tuple<RD.Radio, Press>>();
                        foreach (var r in radioList ?? new List<RD.Radio>())
                        {
                            var radio = r; bool wifi = r.Kind == RD.RadioKind.WiFi;
                            var g = G(wifi ? "\uE701" : "\uE702");
                            var chev = RoundBtn("\uE76C", th - 20, false, () => { if (wifi) ShowWifi(); else ShowBluetooth(); }, wifi ? "Сети Wi-Fi" : "Устройства Bluetooth");
                            ((TextBlock)chev.Child).FontSize = Math.Round(9 * f);
                            var pill = Tile(HS(g, Mg(chev, 6 * f, 0, 0, 0)), (wifi ? "Wi-Fi" : "Bluetooth") + ": клик — вкл/выкл, «›» — подробности", () => ToggleRadio(radio),
                                WMenu(id, m => { m.Items.Add(Item(wifi ? "Сети Wi-Fi…" : "Устройства Bluetooth…", () => { if (wifi) ShowWifi(); else ShowBluetooth(); })); m.Items.Add(Item("Параметры", () => Launch(wifi ? "ms-settings:network-wifi" : "ms-settings:bluetooth"))); }), true);
                            pill.CornerRadius = new CornerRadius(th / 2); pill.Padding = new Thickness(Math.Round(th * 0.32), 0, 4, 0);
                            panel.Children.Add(pill); radioTiles.Add(Tuple.Create(radio, g)); pills.Add(Tuple.Create(radio, pill));
                        }
                        var edge = ChipEdge; var ac = AccentColor; var onB = new SolidColorBrush(Color.FromArgb(0x80, ac.R, ac.G, ac.B));
                        up.Add(() =>
                        {
                            foreach (var rt in radioTiles) rt.Item2.Opacity = rt.Item1.State == RD.RadioState.On ? 1 : 0.4;
                            foreach (var pt in pills)
                            {
                                bool on = pt.Item1.State == RD.RadioState.On; var bs = on ? Color.FromArgb(0x33, ac.R, ac.G, ac.B) : Surface;
                                if (pt.Item2.Base != bs) { pt.Item2.Base = bs; pt.Item2.Background = new SolidColorBrush(bs); } // цвет — состояние: включено подсвечено акцентом
                                if (!pt.Item2.IsKeyboardFocused) pt.Item2.BorderBrush = on ? onB : edge;
                            }
                        });
                        return panel;
                    }
            }
            return null;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT { public IntPtr hwnd; public uint wFunc; [MarshalAs(UnmanagedType.LPWStr)] public string pFrom; [MarshalAs(UnmanagedType.LPWStr)] public string pTo; public ushort fFlags; public bool fAnyOperationsAborted; public IntPtr hNameMappings; [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
        // файлы и папки, брошенные на «Корзину»: одно обычное подтверждение Windows на всю группу; только в Корзину — удаление можно отменить (Ctrl+Z в Проводнике)
        void BinDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[]; if (files == null || files.Length == 0) return;
            e.Handled = true; // не закреплять на панели
            var op = new SHFILEOPSTRUCT { wFunc = 3 /* FO_DELETE */, pFrom = string.Join("\0", files) + "\0\0", fFlags = (ushort)(0x40 /* FOF_ALLOWUNDO */ | (C("binConfirm", "1") == "1" ? 0 : 0x10 /* FOF_NOCONFIRMATION */ | 0x4000 /* FOF_WANTNUKEWARNING: мимо Корзины (сеть, флешка) — всё же спросить */)) };
            int rc = SHFileOperation(ref op);
            if (rc != 0 && rc != 1223 && !op.fAnyOperationsAborted) Notify("Корзина", "Не удалось удалить (код " + rc + ")", true);
            else if (rc == 0 && !op.fAnyOperationsAborted)
            {
                var gone = files.Where(f => !File.Exists(f) && !Directory.Exists(f)).ToArray();
                if (gone.Length > 0) Notify("Корзина", "Удалено: " + (gone.Length == 1 ? System.IO.Path.GetFileName(gone[0]) : gone.Length + " объектов"), true, "\uE74D", "Отменить",
                    () => { int n = RestoreFromBin(gone); UpdateBin(); All(b => b.Render()); if (n < gone.Length) Notify("Корзина", n == 0 ? "Не удалось вернуть — откройте Корзину и восстановите вручную" : "Возвращено " + n + " из " + gone.Length, true, "\uE74D"); }, "bin");
            }
            UpdateBin(); All(b => b.Render());
        }
        // Вернуть из Корзины Windows объекты по их прежним путям (как «Восстановить» в Проводнике). Возвращает, сколько вернули.
        // shortcut: если Проводник скрывает расширения, имя сверяется без расширения; два удалённых файла с одним именем в одной папке не различаются — тогда смотреть дату удаления
        static int RestoreFromBin(string[] paths)
        {
            int n = 0; const System.Reflection.BindingFlags call = System.Reflection.BindingFlags.InvokeMethod, get = System.Reflection.BindingFlags.GetProperty;
            try
            {
                var want = new List<string>(paths);
                var sh = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                var bin = sh.GetType().InvokeMember("NameSpace", call, null, sh, new object[] { 10 });
                var items = bin.GetType().InvokeMember("Items", call, null, bin, null);
                int count = (int)items.GetType().InvokeMember("Count", get, null, items, null);
                for (int i = count - 1; i >= 0 && want.Count > 0; i--)
                {
                    var it = items.GetType().InvokeMember("Item", call, null, items, new object[] { i }); if (it == null) continue;
                    var from = it.GetType().InvokeMember("ExtendedProperty", call, null, it, new object[] { "System.Recycle.DeletedFrom" }) as string;
                    var name = it.GetType().InvokeMember("Name", get, null, it, null) as string;
                    if (from == null || name == null) continue;
                    int k = want.FindIndex(w => string.Equals((System.IO.Path.GetDirectoryName(w) ?? "").TrimEnd('\\'), from.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                        && (string.Equals(System.IO.Path.GetFileName(w), name, StringComparison.OrdinalIgnoreCase) || string.Equals(System.IO.Path.GetFileNameWithoutExtension(w), name, StringComparison.OrdinalIgnoreCase)));
                    if (k < 0) continue;
                    it.GetType().InvokeMember("InvokeVerb", call, null, it, new object[] { "undelete" });
                    want.RemoveAt(k); n++;
                }
            }
            catch (Exception ex) { Log(ex); }
            return n;
        }
        // значок сервиса ИИ: Claude — восьмилучевая звезда, Codex — символ кода в круге
        // значок модуля ИИ на цветной скруглённой плитке, как значок приложения
        static FrameworkElement AiTile(bool claudeIcon, double d)
        {
            var bg = claudeIcon ? Color.FromRgb(0xD9, 0x77, 0x57) : Color.FromRgb(0x10, 0xA3, 0x7F);
            var inner = AiIcon(claudeIcon, d * 0.62);
            var p = inner is Viewbox ? ((Viewbox)inner).Child as Canvas : null;
            if (p != null) foreach (var sh in p.Children.OfType<Path>()) sh.Stroke = Brushes.White;
            var b = inner as Border; if (b != null) { b.Background = Brushes.Transparent; var tb = b.Child as TextBlock; if (tb != null) { tb.Foreground = Brushes.White; tb.FontSize = Math.Round(d * 0.4); } } // значок на плитке — крупно, как у Claude
            return new Border { Width = d, Height = d, CornerRadius = new CornerRadius(d * 0.26), Background = new SolidColorBrush(bg), Child = inner, VerticalAlignment = VerticalAlignment.Center };
        }
        static FrameworkElement AiIcon(bool claudeIcon, double d)
        {
            if (claudeIcon)
            {
                var g = new GeometryGroup();
                for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.Children.Add(new LineGeometry(new Point(12 + Math.Cos(a) * 3, 12 + Math.Sin(a) * 3), new Point(12 + Math.Cos(a) * 10, 12 + Math.Sin(a) * 10))); }
                var p = new Path { Data = g, Stroke = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x57)), StrokeThickness = 2.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                var c = new Canvas { Width = 24, Height = 24 }; c.Children.Add(p);
                return new Viewbox { Width = d, Height = d, Child = c, VerticalAlignment = VerticalAlignment.Center };
            }
            var t = G("", d * 0.5);
            return new Border { Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = new SolidColorBrush(Color.FromArgb(0x30, 0x10, 0xA3, 0x7F)), Child = t, VerticalAlignment = VerticalAlignment.Center };
        }

        // ---------- единый ряд панели: поиск, значки программ, модули и часы в любом порядке ----------
        // Между «Пуск» и «Показать рабочий стол» — один ряд. seq — его порядок для этого монитора:
        // "search", "clock", "w:<виджет>", "p|<закреплённая>", "r|<запущенная>". Перетаскивается любой элемент куда угодно.
        readonly Dictionary<string, FrameworkElement> badges = new Dictionary<string, FrameworkElement>();
        static bool editMode;
        StackPanel strip; ScrollViewer stripScroll; FrameworkElement searchEl, clockEl, startEl;
        Dictionary<string, FrameworkElement> appEls = new Dictionary<string, FrameworkElement>();
        Dictionary<string, List<Win>> appWins = new Dictionary<string, List<Win>>();
        List<string> seq; List<string> shownTokens = new List<string>();
        readonly Dictionary<FrameworkElement, string> tokenOf = new Dictionary<FrameworkElement, string>();
        string SeqKey { get { return primary ? "seq" : "seq@" + scr.DeviceName; } }
        void LoadSeq() { seq = C(SeqKey, "").Split('\t').Where(x => x != "").ToList(); }
        void SaveSeq() { cfg[SeqKey] = string.Join("\t", seq.Where(x => !x.StartsWith("r|") && x != "__drop")); SaveCfg(); } // запущенные без закрепления — только в памяти
        static bool IsApp(string t) { return t.StartsWith("p|") || t.StartsWith("r|"); }
        // Порядок показа: сохранённый + новое. Новая программа — после последнего значка, новый модуль — за соседом по каталогу.
        public static List<string> MergeSeq(IList<string> seq, IList<string> apps, IList<string> widgets, bool search, bool clock, bool start = false)
        {
            Func<string, bool> present = t => t == "__drop" || (t == "search" ? search : t == "start" ? start : t == "clock" ? clock : t.StartsWith("w:") ? widgets.Contains(t) : apps.Contains(t));
            var res = new List<string>();
            foreach (var t in seq) if (present(t) && !res.Contains(t)) res.Add(t);
            if (search && !res.Contains("search")) res.Insert(0, "search");
            if (start && !res.Contains("start")) res.Insert(0, "start"); // по умолчанию — в начале ряда, как раньше
            foreach (var a in apps) if (!res.Contains(a))
            {
                int last = res.FindLastIndex(x => IsApp(x)); if (last < 0) last = res.IndexOf("search");
                res.Insert(last + 1, a);
            }
            for (int i = 0; i < widgets.Count; i++)
            {
                var w = widgets[i]; if (res.Contains(w)) continue;
                int at = -1;
                for (int j = i - 1; j >= 0 && at < 0; j--) at = res.IndexOf(widgets[j]);
                if (at < 0) { at = res.FindLastIndex(x => IsApp(x)); if (at < 0) at = res.IndexOf("search"); }
                int clockAt = res.IndexOf("clock");
                res.Insert(at >= 0 ? at + 1 : (clockAt >= 0 ? clockAt : res.Count), w);
            }
            if (clock && !res.Contains("clock")) res.Add("clock");
            return res;
        }
        static string GroupOf(string t)
        {
            if (t == "search" || t == "start" || IsApp(t)) return "apps";
            if (t == "clock") return "clock";
            if (t.StartsWith("w:")) { var d = Def(t.Substring(2)); return d != null ? d[3] : "w"; }
            return null;
        }
        void W(string id, UIElement el)
        {
            // обёртка: модуль + кнопка «убрать» для режима правки
            var wrap = new Grid(); wrap.Children.Add(el);
            var x = new TextBlock { Text = "", FontFamily = Icons, FontSize = 7, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Frozen(OnAccent, 255) };
            var badge = new Press { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Accent, Child = x, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 4, 0), Cursor = Cursors.Hand, ToolTip = "Убрать с панели", Visibility = Visibility.Collapsed };
            badge.Invoke = () => SetVisible(id, false); badge.MouseLeftButtonUp += (s, e) => { e.Handled = true; SetVisible(id, false); };
            System.Windows.Automation.AutomationProperties.SetName(badge, "Убрать «" + Def(id)[1] + "» с панели");
            wrap.Children.Add(badge); badges[id] = badge;
            widgetEls[id] = wrap;
            wrap.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = el }); // модуль скрыт (нет батареи) — без пустого места и одинокого ×
        }
        static void Detach(UIElement el) { var par = VisualTreeHelper.GetParent(el); var p = par as Panel; if (p != null) p.Children.Remove(el); var dc = par as Decorator; if (dc != null && dc.Child == el) dc.Child = null; }
        Border DropSlot()
        {
            return new Border { Width = VerticalBar ? TileH * 0.65 : 3, Height = VerticalBar ? 3 : TileH * 0.65,
                CornerRadius = new CornerRadius(1.5), Margin = VerticalBar ? new Thickness(0, 3, 0, 3) : new Thickness(3, 0, 3, 0),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = Accent };
        }
        // шаги сжатия ('s' — виды проще, 'w' — модуль в «…», 'a' — значок в «…») и сколько места освободил каждый
        int overflowN, appCut; readonly List<KeyValuePair<char, double>> cuts = new List<KeyValuePair<char, double>>(); char cutKind; double cutNeed;
        const double FitSlack = 24; // запас ширины перед возвратом скрытого: новое окно не раскачивает композицию туда-сюда
        // защита от раскачки: если сразу после возврата места снова не хватило, возвраты замирают на 2 минуты,
        // а у этого шага запоминается запас ещё на одну плитку — иначе ряд «прыгал» по экрану несколько раз в секунду
        DateTime lastRestoreAt = DateTime.MinValue, fitHoldUntil = DateTime.MinValue; bool fitFlapped;
        const int MinApps = 4;
        List<string> overflowIds = new List<string>(), overflowApps = new List<string>(); Press overflowTile;
        void ResetFit(bool views)
        {
            overflowN = 0; appCut = 0; cutKind = '\0'; fitHoldUntil = DateTime.MinValue; lastRestoreAt = DateTime.MinValue; fitFlapped = false; // явное изменение раскладки — подгонка сразу с нуля
            if (views) { squeeze = 0; cuts.Clear(); } else cuts.RemoveAll(x => x.Key != 's');
        }
        double FreeWidth { get { return root == null || rowPanel == null || root.ActualWidth <= 0 ? 0 : scr.Bounds.Width / MonScale - 2 * Gap - (rowPanel.ActualWidth + root.Padding.Left * 2 + 2); } }
        // order — временный порядок (во время перетаскивания или вставки из каталога); null — сохранённый
        void Compose(List<string> order = null)
        {
            if (strip == null) return;
            if (seq == null) LoadSeq();
            var toks = order;
            if (toks == null)
            {
                var apps = appEls.Keys.OrderBy(k => { int i = appOrder.IndexOf(k); return i < 0 ? int.MaxValue : i; }).ToList();
                var widgets = HasWidgets ? Layout().Where(x => (CompactMonitor ? LayoutShows(x.Key) : x.Value) && widgetEls.ContainsKey(x.Key)).Select(x => "w:" + x.Key).ToList() : new List<string>();
                toks = MergeSeq(seq, apps, widgets, searchEl != null, clockEl != null, startEl != null);
                if (UseSections) toks = toks.OrderBy(SectionFor).ToList();
                fullToks = toks.ToList();
                // не хватает места — в «…» уходят модули без «Оставлять видимым» (самые правые),
                // затем значки сверх бюджета (настройка или набор) и, если есть закреплённые модули, ещё значки — не меньше четырёх
                var realW = toks.Where(t => t.StartsWith("w:") && !Keep(t.Substring(2))).ToList();
                overflowIds = overflowN > 0 && !editMode ? realW.Skip(Math.Max(0, realW.Count - overflowN)).Select(t => t.Substring(2)).ToList() : new List<string>();
                var realA = toks.Where(IsApp).ToList();
                int keepA = AppMax > 0 ? Math.Min(realA.Count, AppMax) : realA.Count;
                keepA = Math.Max(Math.Min(realA.Count, MinApps), keepA - appCut);
                var fgw = N.GetForegroundWindow(); // активная программа остаётся на панели, как в Windows
                var cand = Enumerable.Reverse(realA).Where(t => { List<Win> ws; return !(appWins.TryGetValue(t, out ws) && ws.Any(w => w.H == fgw)); });
                overflowApps = editMode ? new List<string>() : cand.Take(Math.Max(0, realA.Count - keepA)).ToList();
            }
            // перестройка состава — соседи плавно занимают новые места, новые элементы проявляются
            Dictionary<string, double> oldX = null; var oldToks = shownTokens;
            if (order == null && !dragging && !sDragging && Motion && strip.IsLoaded)
            {
                oldX = new Dictionary<string, double>();
                foreach (var kv in tokenOf) if (!oldX.ContainsKey(kv.Value)) oldX[kv.Value] = SlotStart(kv.Key);
            }
            foreach (var el in tokenOf.Keys.ToList()) Detach(el);
            strip.Children.Clear(); tokenOf.Clear(); shownTokens = new List<string>();
            int previousSection = -1; bool overflowPut = false; overflowTile = null;
            Action putOverflow = () =>
            {
                if (overflowPut || overflowIds.Count + overflowApps.Count == 0) return; overflowPut = true;
                strip.Children.Add(overflowTile = Tile(G(""), "Ещё " + (overflowIds.Count + overflowApps.Count) + " — не поместились на панели", ShowOverflow, null));
            };
            foreach (var t in toks)
            {
                if (t.StartsWith("w:") && overflowIds.Contains(t.Substring(2))) continue;
                if (IsApp(t) && overflowApps.Contains(t)) continue;
                FrameworkElement el;
                if (t == "__drop") el = DropSlot();
                else if (t == "search") el = searchEl;
                else if (t == "start") el = startEl;
                else if (t == "clock") { if (!UseSections && shownTokens.Count > 0) putOverflow(); el = clockEl; }
                else if (t.StartsWith("w:")) { if (!widgetEls.TryGetValue(t.Substring(2), out el)) continue; }
                else if (!appEls.TryGetValue(t, out el)) continue;
                if (el == null) continue;
                int nextSection = t == "__drop" ? previousSection : SectionFor(t);
                if (UseSections && previousSection >= 0 && nextSection != previousSection)
                    strip.Children.Add(new Border { Width = 12, IsHitTestVisible = false });
                Detach(el); strip.Children.Add(el); tokenOf[el] = t; shownTokens.Add(t); previousSection = nextSection;
                if (t.StartsWith("w:")) { FrameworkElement bd; if (badges.TryGetValue(t.Substring(2), out bd)) bd.Visibility = editMode ? Visibility.Visible : Visibility.Collapsed; }
            }
            putOverflow();
            if (doneTile != null) doneTile.Visibility = editMode && !collapsed ? Visibility.Visible : Visibility.Collapsed;
            if (oldX != null && !oldToks.SequenceEqual(shownTokens))
            {
                strip.UpdateLayout();
                foreach (var kv in tokenOf)
                {
                    double ox; var c = kv.Key;
                    if (!oldX.TryGetValue(kv.Value, out ox)) { c.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))); continue; }
                    double dx = ox - SlotStart(c); if (Math.Abs(dx) < 0.5) continue;
                    var tt = new TranslateTransform(); c.RenderTransform = tt;
                    tt.BeginAnimation(SlideAxis, new DoubleAnimation(dx, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuadraticEase(), FillBehavior = FillBehavior.Stop });
                }
            }
        }
        void RenderWidgets() { Compose(); }
        // новый порядок ряда: сохраняем его, порядок закреплённых в pinned.txt и порядок модулей в раскладке
        void CommitOrder(List<string> order)
        {
            seq = order.Where(t => t != "__drop").ToList(); SaveSeq();
            var appsNow = seq.Where(IsApp).ToList();
            appOrder = appsNow.Concat(appOrder.Where(t => !appsNow.Contains(t))).ToList();
            if (HasWidgets) SaveLayout(MergeOrder(Layout(), seq.Where(t => t.StartsWith("w:")).Select(t => t.Substring(2)).ToList()));
            SavePinned();
        }

        // Нехватка места, по шагам: модули с видом «Авто» упрощаются (до двух шагов), затем модули без «Оставлять видимым»
        // уходят в «…» (самые правые), затем — только если на панели есть закреплённые модули — лишние значки программ.
        // Каждый шаг помнит, сколько места освободил; обратно — когда это место есть с запасом FitSlack. Дальше ряд листается колесом.
        bool fitQueued;
        void Fit()
        {
            if (strip == null || root == null || root.ActualWidth <= 0 || collapsed || folding) return;
            if (VerticalBar)
            {
                double available = scr.Bounds.Height / MonScale - 2 * Gap - root.Padding.Top * 2 - 2;
                var buttons = rowPanel.Children.OfType<FrameworkElement>().Where(el => el.IsVisible && el != stripScroll).Sum(el => el.ActualHeight);
                double maxHeight = Math.Max(TileH * 2, available - buttons);
                if (Math.Abs(stripScroll.MaxHeight - maxHeight) > 0.5) stripScroll.MaxHeight = maxHeight;
                return;
            }
            if (!strip.IsMeasureValid || !rowPanel.IsMeasureValid) { DeferFit(); return; } // раскладка ещё идёт — проверим сразу после неё, иначе сжатие останавливалось на полпути
            double sc = MonScale, avail = scr.Bounds.Width / sc - 2 * Gap;
            // ширина содержимого, а не капсулы: во всю ширину капсула шире ряда
            double rest = rowPanel.ActualWidth + root.Padding.Left * 2 + 2 - stripScroll.ActualWidth, need = rest + strip.ActualWidth;
            double max = Math.Max(TileH * 3, avail - rest);
            if (Math.Abs(stripScroll.MaxWidth - max) > 0.5) stripScroll.MaxWidth = max;
            if (editMode || dragging) return; // правка и перетаскивание: ничего не прячем — ряд прокручивается, «Готово» и края видны
            if (cutKind != '\0') { cuts.Add(new KeyValuePair<char, double>(cutKind, Math.Max(TileH / 2, cutNeed - need) + (fitFlapped ? TileH : 0))); cutKind = '\0'; fitFlapped = false; }
            var shownW = shownTokens.Where(t => t.StartsWith("w:")).Select(t => t.Substring(2)).ToList();
            if (need > avail + 1)
            {
                if ((DateTime.Now - lastRestoreAt).TotalSeconds < 5) { fitFlapped = true; fitHoldUntil = DateTime.Now.AddMinutes(2); }
                if (squeeze < 2 && shownW.Any(Squeezable)) { cutKind = 's'; cutNeed = need; squeeze++; Defer(Rebuild); return; }
                if (shownW.Any(x => !Keep(x))) { cutKind = 'w'; cutNeed = need; overflowN++; Compose(); DeferFit(); return; }
                if (shownW.Any(Keep) && shownTokens.Count(IsApp) > MinApps) { cutKind = 'a'; cutNeed = need; appCut++; Compose(); DeferFit(); return; }
            }
            else if (cuts.Count > 0)
            {
                var last = cuts[cuts.Count - 1];
                if (need + last.Value + FitSlack > avail || DateTime.Now < fitHoldUntil) return;
                cuts.RemoveAt(cuts.Count - 1); lastRestoreAt = DateTime.Now;
                if (last.Key == 's') { squeeze = Math.Max(0, squeeze - 1); Defer(Rebuild); }
                else { if (last.Key == 'w') overflowN = Math.Max(0, overflowN - 1); else appCut = Math.Max(0, appCut - 1); Compose(); DeferFit(); }
            }
        }
        bool fitPending;
        void DeferFit() { if (fitPending) return; fitPending = true; Dispatcher.BeginInvoke(new Action(() => { fitPending = false; Fit(); }), DispatcherPriority.Background); }
        void Defer(Action a)
        {
            if (fitQueued) return; fitQueued = true;
            Dispatcher.BeginInvoke(new Action(() => { fitQueued = false; a(); }), DispatcherPriority.Background);
        }

        // ---------- перетаскивание по всему ряду; значок программы — на панель другого монитора ----------
        string dragTok; Point dragDown; bool sDragging; FrameworkElement dragEl; Bar dropBar; List<string> dragOrder;
        Canvas insertionOverlay; Border insertionLine;
        string TokenAt(DependencyObject d)
        {
            while (d != null && d != strip)
            {
                var fe = d as FrameworkElement; string t;
                if (fe != null && tokenOf.TryGetValue(fe, out t)) return t;
                d = (d is Visual ? VisualTreeHelper.GetParent(d) : null) ?? LogicalTreeHelper.GetParent(d);
            }
            return null;
        }
        FrameworkElement ElOf(string t) { foreach (var kv in tokenOf) if (kv.Value == t) return kv.Key; return null; }
        static Rect Slot(FrameworkElement el) { return System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(el); }
        double Axis(Point point) { return VerticalBar ? point.Y : point.X; }
        double SlotStart(FrameworkElement el) { var r = Slot(el); return VerticalBar ? r.Y : r.X; }
        double SlotLength(FrameworkElement el) { var r = Slot(el); return VerticalBar ? r.Height : r.Width; }
        double Mid(FrameworkElement el) { return SlotStart(el) + SlotLength(el) / 2; }
        DependencyProperty SlideAxis { get { return VerticalBar ? TranslateTransform.YProperty : TranslateTransform.XProperty; } }
        void PaintInsertion(FrameworkElement target)
        {
            if (target == null) { if (insertionLine != null) insertionLine.Visibility = Visibility.Collapsed; return; }
            if (insertionLine == null)
            {
                insertionLine = new Border { Background = Accent, CornerRadius = new CornerRadius(1.5) };
                insertionOverlay.Children.Add(insertionLine);
            }
            var pt = target.TranslatePoint(new Point(), insertionOverlay);
            var vp = stripScroll.TranslatePoint(new Point(), insertionOverlay);
            if (VerticalBar ? pt.Y < vp.Y || pt.Y + target.ActualHeight > vp.Y + stripScroll.ActualHeight
                            : pt.X < vp.X || pt.X + target.ActualWidth > vp.X + stripScroll.ActualWidth)
            { insertionLine.Visibility = Visibility.Collapsed; return; }
            insertionLine.Width = VerticalBar ? TileH * 0.65 : 3;
            insertionLine.Height = VerticalBar ? 3 : TileH * 0.65;
            Canvas.SetLeft(insertionLine, VerticalBar ? pt.X + (target.ActualWidth - insertionLine.Width) / 2 : pt.X);
            Canvas.SetTop(insertionLine, VerticalBar ? pt.Y : pt.Y + (target.ActualHeight - insertionLine.Height) / 2);
            insertionLine.Visibility = Visibility.Visible;
        }
        void InitStripDrag()
        {
            strip.PreviewMouseLeftButtonDown += (s, e) => { if (Locked) return; dragTok = TokenAt(e.OriginalSource as DependencyObject); dragDown = e.GetPosition(strip); };
            strip.PreviewMouseMove += (s, e) =>
            {
                if (dragTok == null || e.LeftButton != MouseButtonState.Pressed) { if (!sDragging) dragTok = null; return; }
                var p = e.GetPosition(strip);
                if (!sDragging)
                {
                    if (Math.Abs(p.X - dragDown.X) < 6 && Math.Abs(p.Y - dragDown.Y) < 6) return;
                    dragEl = ElOf(dragTok); if (dragEl == null) { dragTok = null; return; }
                    sDragging = true; dragging = true; ClosePreview(); Mouse.Capture(strip, CaptureMode.SubTree);
                    dragOrder = shownTokens.ToList();
                    // значок «берётся в руку»: копия едет за курсором по всему экрану, на панели остаётся место, куда он встанет
                    ShowGhost(dragEl, dragDown - (Vector)dragEl.TranslatePoint(new Point(), strip));
                    dragEl.Opacity = 0;
                }
                e.Handled = true;
                MoveGhost();
                // курсор над панелью другого монитора — туда можно бросить значок программы с открытыми окнами
                List<Win> ws;
                var target = IsApp(dragTok) && appWins.TryGetValue(dragTok, out ws) && ws.Count > 0 ? BarUnderCursor() : null;
                if (target != dropBar) { if (dropBar != null) dropBar.MarkDrop(false); dropBar = target; if (target != null) target.MarkDrop(true); }
                if (target != null || !CursorOverSelf()) { HoverGroup(null); PaintInsertion(null); return; }
                double at = Axis(p);
                // над серединой другого значка — не переставляем: после выдержки он станет целью группировки
                string over = null;
                if (DragPath() != null)
                    foreach (var t0 in dragOrder)
                    {
                        if (t0 == dragTok || !IsApp(t0)) continue; var el0 = ElOf(t0); if (el0 == null) continue;
                        double start = SlotStart(el0), length = SlotLength(el0);
                        if (at > start + length * 0.25 && at < start + length * 0.75) { over = t0; break; }
                    }
                HoverGroup(over);
                if (over != null) { PaintInsertion(null); return; }
                int from = dragOrder.IndexOf(dragTok);
                int to = dragOrder.Count(t => { if (t == dragTok) return false; var el = ElOf(t); return el != null && at > Mid(el); });
                if (from >= 0 && to != from)
                {
                    var old = strip.Children.OfType<FrameworkElement>().ToDictionary(c => c, SlotStart);
                    dragOrder.RemoveAt(from); dragOrder.Insert(to, dragTok); Compose(dragOrder); strip.UpdateLayout();
                    foreach (var c in strip.Children.OfType<FrameworkElement>())
                    {
                        double ox; if (c == dragEl || !old.TryGetValue(c, out ox) || !Motion) continue;
                        double dx = ox - SlotStart(c); if (Math.Abs(dx) < 0.5) continue;
                        var tt = new TranslateTransform(); c.RenderTransform = tt; // соседи плавно освобождают место
                        tt.BeginAnimation(SlideAxis, new DoubleAnimation(dx, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase(), FillBehavior = FillBehavior.Stop });
                    }
                }
                PaintInsertion(dragEl);
            };
            strip.PreviewMouseLeftButtonUp += (s, e) => { if (sDragging) { e.Handled = true; FinishStripDrag(); } dragTok = null; };
            strip.LostMouseCapture += (s, e) => { if (sDragging && !strip.IsMouseCaptureWithin) FinishStripDrag(); };
        }
        void FinishStripDrag()
        {
            PaintInsertion(null);
            sDragging = false; lastDragEnd = DateTime.Now;
            if (strip.IsMouseCaptureWithin) Mouse.Capture(null);
            string groupTo = groupArmed ? hoverTok : null, groupPath = groupTo != null ? DragPath() : null; HoverGroup(null);
            var target = dropBar; dropBar = null; var tok = dragTok; dragTok = null; var el = dragEl; var order = dragOrder;
            Action shown = () => { CloseGhost(); dragging = false; if (el != null) { el.RenderTransform = null; el.Opacity = 1; Panel.SetZIndex(el, 0); } };
            if (groupTo != null && groupPath != null) { shown(); GroupDrop(groupTo, groupPath); return; } // бросок на значок — одна операция: группа и порядок сохраняются вместе
            if (target != null)
            {
                shown(); target.MarkDrop(false);
                List<Win> ws; if (tok != null && appWins.TryGetValue(tok, out ws)) foreach (var w in ws) MoveToMonitor(w.H, target);
                Compose(); lastKey = null; return;
            }
            // копия плавно садится в своё место на панели, затем на её месте появляется сам значок — без мигания
            LandGhost(el, () => { shown(); if (order != null) CommitOrder(order); });
        }
        // ---------- группы перетаскиванием: бросок на значок — новая группа, на группу — добавить в неё ----------
        string hoverTok; bool groupArmed; DispatcherTimer groupT; Brush hoverEdge; Thickness hoverThick;
        // путь перетаскиваемой программы; у групп его нет — группу в группу не вкладываем
        string DragPath()
        {
            if (dragTok == null || !IsApp(dragTok)) return null;
            if (dragTok.StartsWith("p|")) { var p = dragTok.Substring(2); return p.StartsWith("group:") ? null : p; }
            List<Win> ws; return appWins.TryGetValue(dragTok, out ws) && ws.Count > 0 ? ws[0].Path : null;
        }
        void HoverGroup(string tok)
        {
            if (tok == hoverTok) return;
            var old = hoverTok == null ? null : ElOf(hoverTok) as Border;
            if (old != null && groupArmed) { old.BorderBrush = hoverEdge; old.BorderThickness = hoverThick; }
            hoverTok = tok; groupArmed = false;
            if (groupT == null) { groupT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) }; groupT.Tick += (s, e) => ArmGroup(); }
            groupT.Stop(); if (tok != null) groupT.Start();
        }
        // выдержка прошла и курсор всё ещё над серединой — цель подсвечивается рамкой акцента
        void ArmGroup()
        {
            groupT.Stop(); if (!sDragging || hoverTok == null) return;
            var b = ElOf(hoverTok) as Border; if (b == null) return;
            hoverEdge = b.BorderBrush; hoverThick = b.BorderThickness; b.BorderBrush = Accent; b.BorderThickness = new Thickness(2); groupArmed = true;
        }
        void GroupDrop(string targetTok, string path)
        {
            var tp = targetTok.Substring(2);
            if (targetTok.StartsWith("p|") && tp.StartsWith("group:"))
            {
                AddToGroup(path, tp.Substring(6)); AnimateGroupTile(targetTok); return;
            }
            string tpath = targetTok.StartsWith("p|") ? tp : null;
            if (tpath == null) { List<Win> ws; if (appWins.TryGetValue(targetTok, out ws) && ws.Count > 0) tpath = ws[0].Path; }
            if (tpath == null || string.Equals(tpath, path, StringComparison.OrdinalIgnoreCase)) return;
            var name = NewGroup(tpath, path);
            AnimateGroupTile("p|group:" + name);
        }
        void AnimateGroupTile(string token)
        {
            if (!Motion) return;
            var tile = ElOf(token) as Border;
            var icon = tile == null ? null : tile.Child as FrameworkElement;
            if (icon == null) return;
            var scale = new ScaleTransform(1, 1);
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            icon.RenderTransform = scale;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, SettingsMotion(0.78, 1, 200));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, SettingsMotion(0.78, 1, 200));
            icon.BeginAnimation(OpacityProperty, SettingsMotion(0.65, 1, 160));
        }
        void LandGhost(FrameworkElement el, Action done)
        {
            var gh = ghost == null ? IntPtr.Zero : new WindowInteropHelper(ghost).Handle;
            if (gh == IntPtr.Zero || el == null || !Motion || PresentationSource.FromVisual(el) == null) { done(); return; }
            N.RECT r; N.GetWindowRect(gh, out r); var to = el.PointToScreen(new Point(0, 0));
            double x0 = r.L, y0 = r.T; var start = DateTime.Now;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(12) };
            t.Tick += (s, e) =>
            {
                double k = Math.Min(1, (DateTime.Now - start).TotalMilliseconds / 150), q = 1 - Math.Pow(1 - k, 3); // 150 мс, замедление к концу
                N.SetWindowPos(gh, IntPtr.Zero, (int)Math.Round(x0 + (to.X - x0) * q), (int)Math.Round(y0 + (to.Y - y0) * q), 0, 0, 0x1 | 0x4 | 0x10);
                if (k >= 1) { t.Stop(); done(); }
            };
            t.Start();
        }
        // копия перетаскиваемого элемента в прозрачном окне поверх всего: следует за курсором и за пределами панели
        Window ghost; double ghostDx, ghostDy;
        void ShowGhost(FrameworkElement el, Point grab)
        {
            CloseGhost();
            var host = Window.GetWindow(el) ?? this; // панель или карточка «…»
            double sc = Scale, w = el.ActualWidth, h = el.ActualHeight; var top = VisualTreeHelper.GetChildrenCount(host) > 0 ? VisualTreeHelper.GetChild(host, 0) as FrameworkElement : null; if (w < 1 || h < 1 || top == null) return;
            // снимок всей панели и вырезка элемента: снимок отдельного элемента внутри ряда выходил пустым
            double fw = top.ActualWidth, fh = top.ActualHeight;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
                dc.DrawRectangle(new VisualBrush(top) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, Viewbox = new Rect(0, 0, fw, fh), ViewboxUnits = BrushMappingMode.Absolute }, null, new Rect(0, 0, fw, fh));
            var full = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(fw * sc), (int)Math.Ceiling(fh * sc), 96 * sc, 96 * sc, PixelFormats.Pbgra32);
            // на миг без подложек вокруг (панель, карточка) и тени — в руке только сам элемент
            var undo = new List<Action>();
            for (var a = VisualTreeHelper.GetParent(el); a != null; a = VisualTreeHelper.GetParent(a))
            {
                var bd = a as Border; if (bd == null) continue;
                var b0 = bd.Background; var e0 = bd.BorderBrush; var f0 = bd.Effect;
                bd.Background = Brushes.Transparent; bd.BorderBrush = Brushes.Transparent; bd.Effect = null;
                undo.Add(() => { bd.Background = b0; bd.BorderBrush = e0; bd.Effect = f0; });
            }
            var sv = shadow.Visibility; if (host == this) shadow.Visibility = Visibility.Hidden; top.UpdateLayout();
            full.Render(dv);
            foreach (var u in undo) u(); shadow.Visibility = sv;
            var at = el.TranslatePoint(new Point(0, 0), top);
            var cut = new Int32Rect((int)Math.Max(0, Math.Round(at.X * sc)), (int)Math.Max(0, Math.Round(at.Y * sc)), (int)Math.Ceiling(w * sc), (int)Math.Ceiling(h * sc));
            cut.Width = Math.Min(cut.Width, full.PixelWidth - cut.X); cut.Height = Math.Min(cut.Height, full.PixelHeight - cut.Y);
            if (cut.Width <= 0 || cut.Height <= 0) return;
            var bmp = new System.Windows.Media.Imaging.CroppedBitmap(full, cut); bmp.Freeze();
            var g = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
                ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight, IsHitTestVisible = false,
                Content = new Image { Source = bmp, Width = w, Height = h, Opacity = 0.92 }
            };
            g.SourceInitialized += (s, e) =>
            {
                var gh = new WindowInteropHelper(g).Handle;
                N.SetWindowLongPtr(gh, -20, new IntPtr(N.GetWindowLongPtr(gh, -20).ToInt64() | 0x20 | 0x80 | 0x08000000)); // сквозное для мыши, без фокуса, не в Alt+Tab
            };
            ghostDx = grab.X * sc; ghostDy = grab.Y * sc;
            ghost = g; g.Show(); MoveGhost();
        }
        void MoveGhost()
        {
            if (ghost == null) return;
            var gh = new WindowInteropHelper(ghost).Handle; if (gh == IntPtr.Zero) return;
            N.POINT p; N.GetCursorPos(out p);
            N.SetWindowPos(gh, IntPtr.Zero, (int)Math.Round(p.X - ghostDx), (int)Math.Round(p.Y - ghostDy), 0, 0, 0x1 | 0x4 | 0x10);
        }
        void CloseGhost() { if (ghost != null) { ghost.Close(); ghost = null; } }
        bool CursorOverSelf()
        {
            N.POINT p; N.GetCursorPos(out p); N.RECT r;
            return hwnd != IntPtr.Zero && N.GetWindowRect(hwnd, out r) && p.X >= r.L && p.X < r.R && p.Y >= r.T - 24 && p.Y < r.B + 24;
        }
        Bar BarUnderCursor()
        {
            N.POINT p; N.GetCursorPos(out p);
            foreach (var b in bars)
            {
                if (b == this || b.hwnd == IntPtr.Zero || !b.IsVisible) continue;
                N.RECT r; if (N.GetWindowRect(b.hwnd, out r) && p.X >= r.L && p.X < r.R && p.Y >= r.T && p.Y < r.B) return b;
            }
            return null;
        }
        void MarkDrop(bool on)
        {
            if (root == null) return;
            if (on) { root.BorderBrush = Accent; root.BorderThickness = new Thickness(2); }
            else { root.BorderThickness = new Thickness(1); ApplyStyle(); }
        }

        // ---------- перенос окна на монитор другой панели ----------
        [StructLayout(LayoutKind.Sequential)] struct MonInfo { public int cbSize; public N.RECT rcMonitor, rcWork; public uint dwFlags; }
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MonInfo mi);
        static N.RECT WorkOf(IntPtr mon) { var mi = new MonInfo { cbSize = Marshal.SizeOf(typeof(MonInfo)) }; GetMonitorInfo(mon, ref mi); return mi.rcWork; }
        static double DpiOf(IntPtr mon) { uint x, y; try { if (GetDpiForMonitor(mon, 0, out x, out y) == 0 && x > 0) return x / 96.0; } catch { } return 1; }
        // Размер сохраняется в логических единицах (на экране с другим масштабом окно выглядит так же), но не больше
        // рабочей области; место — то же относительное положение в рабочей области.
        public static N.RECT FitRect(N.RECT r, N.RECT from, N.RECT to, double kFrom, double kTo)
        {
            int ow = r.R - r.L, oh = r.B - r.T, fw = from.R - from.L, fh = from.B - from.T, tw = to.R - to.L, th = to.B - to.T;
            int w = Math.Min(tw, (int)Math.Round(ow * kTo / kFrom)), h = Math.Min(th, (int)Math.Round(oh * kTo / kFrom));
            double fx = fw - ow > 0 ? (double)(r.L - from.L) / (fw - ow) : 0.5, fy = fh - oh > 0 ? (double)(r.T - from.T) / (fh - oh) : 0.5;
            fx = Math.Max(0, Math.Min(1, fx)); fy = Math.Max(0, Math.Min(1, fy));
            int x = to.L + (int)Math.Round((tw - w) * fx), y = to.T + (int)Math.Round((th - h) * fy);
            return new N.RECT { L = x, T = y, R = x + w, B = y + h };
        }
        static void MoveToMonitor(IntPtr h, Bar target)
        {
            var src = N.MonitorFromWindow(h, 2); if (src == target.mon) return;
            bool max = N.IsZoomed(h), min = N.IsIconic(h);
            if (max || min) N.ShowWindow(h, 9); // SW_RESTORE: переносим обычное положение, состояние вернём на новом экране
            N.RECT r; if (!N.GetWindowRect(h, out r)) return;
            var dest = FitRect(r, WorkOf(src), WorkOf(target.mon), DpiOf(src), DpiOf(target.mon));
            const uint NoZNoAct = 0x4 | 0x10;
            N.SetWindowPos(h, IntPtr.Zero, dest.L, dest.T, r.R - r.L, r.B - r.T, NoZNoAct); // на новом экране окно получит WM_DPICHANGED и пересчитает себя
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                N.SetWindowPos(h, IntPtr.Zero, dest.L, dest.T, dest.R - dest.L, dest.B - dest.T, NoZNoAct); // итог — задуманные место и размер
                if (max) N.ShowWindow(h, 3); else if (min) N.ShowWindow(h, 6);
            };
            t.Start();
        }

        // перетаскивание модуля из каталога на панель: место вставки показывается пунктиром
        int dropAt = -1; DateTime dropSeen;
        // Приём модуля: из каталога или из «…» (своей панели или другого монитора). Промежуток — вставка,
        // середина другого модуля — «вместо него»: модули меняются местами, заменённый уходит туда, где был перенесённый.
        List<string> fullToks = new List<string>(); string dropOver;
        void MarkOver(string t)
        {
            if (t == dropOver) return; FrameworkElement el;
            if (dropOver != null && (el = ElOf(dropOver)) != null) el.Opacity = 1;
            dropOver = t;
            if (t != null && (el = ElOf(t)) != null) el.Opacity = 0.35;
        }
        void InitCatalogDrop()
        {
            strip.AllowDrop = true;
            strip.DragOver += (s, e) =>
            {
                if (!e.Data.GetDataPresent("FlowBarWidget")) return;
                e.Effects = DragDropEffects.Copy; e.Handled = true; dropSeen = DateTime.Now;
                var tok = "w:" + (string)e.Data.GetData("FlowBarWidget");
                double x0 = Axis(e.GetPosition(strip));
                string over = null;
                foreach (var t in shownTokens)
                {
                    if (!t.StartsWith("w:") || t == tok) continue; var el = ElOf(t); if (el == null) continue;
                    double begin = SlotStart(el), length = SlotLength(el);
                    if (x0 > begin + length * 0.3 && x0 < begin + length * 0.7) { over = t; break; }
                }
                if (over != null) { if (dropAt >= 0) { dropAt = -1; Compose(shownTokens.Where(t => t != "__drop").ToList()); } MarkOver(over); return; }
                MarkOver(null);
                var order = shownTokens.Where(t => t != tok && t != "__drop").ToList();
                int at = order.Count(t => { var el = ElOf(t); return el != null && x0 > Mid(el); });
                if (at == dropAt) return;
                dropAt = at; order.Insert(at, "__drop"); Compose(order);
            };
            strip.DragLeave += (s, e) =>
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
                t.Tick += (o, a) => { t.Stop(); if ((DateTime.Now - dropSeen).TotalMilliseconds > 140) { MarkOver(null); if (dropAt >= 0) { dropAt = -1; Compose(); } } };
                t.Start();
            };
            strip.Drop += (s, e) =>
            {
                if (!e.Data.GetDataPresent("FlowBarWidget")) return;
                e.Handled = true;
                var id = (string)e.Data.GetData("FlowBarWidget"); var tok = "w:" + id;
                var from = e.Data.GetDataPresent("FlowBarFrom") ? e.Data.GetData("FlowBarFrom") as string : null;
                var src = from == null ? null : bars.FirstOrDefault(b => b.scr.DeviceName == from);
                var over = dropOver; MarkOver(null);
                // полный порядок ряда — вместе с ушедшим в «…», а не только видимое
                var full = fullToks.Where(t => t != "__drop").ToList(); int was = full.IndexOf(tok);
                if (over != null && full.Contains(over))
                {
                    int q = full.IndexOf(over);
                    if (was >= 0) { full[was] = over; full[q] = tok; }
                    else { full[q] = tok; int ci = full.IndexOf("clock"); full.Insert(ci >= 0 ? ci : full.Count, over); }
                }
                else
                {
                    int di = shownTokens.IndexOf("__drop");
                    string next = di >= 0 ? shownTokens.Skip(di + 1).FirstOrDefault(t => t != tok) : null;
                    full.Remove(tok);
                    int ni = next != null ? full.IndexOf(next) : -1; if (ni < 0) { ni = full.IndexOf("clock"); if (ni < 0) ni = full.Count; }
                    full.Insert(ni, tok);
                }
                dropAt = -1; seq = full; SaveSeq();
                if (!HasWidgets)
                {
                    // монитор был без виджетов — у него появляется своя раскладка с этим модулем
                    cfg["mon." + scr.DeviceName] = "own"; cfg["widgets@" + scr.DeviceName] = string.Join(",", WidgetDefs.Select(x => (x[0] == id ? "" : "-") + x[0])); SaveCfg();
                    ResetFit(true); Rebuild(); Place();
                }
                else
                {
                    var l = Layout(); l.RemoveAll(x => x.Key == id); l.Add(new KeyValuePair<string, bool>(id, true));
                    SaveLayout(MergeOrder(l, full.Where(t => t.StartsWith("w:")).Select(t => t.Substring(2)).ToList()));
                }
                // перенос с панели другого монитора со своей раскладкой: там модуль убираем
                if (src != null && src != this && src.LayoutKey != LayoutKey) src.SetVisible(id, false);
            };
        }

        // ---------- сборка панели ----------
        Grid frame; Border shadow; Press doneTile;
        void Build()
        {
            HookSharpness(); ConfigureSharpness(this);
            ClearSectionBackdrops(); sectionKey = null;
            ups.Clear(); badges.Clear();
            double rad = Flush ? 0 : BarH / 2; // вплотную к краю — ровный прямоугольник
            root = new Border { CornerRadius = new CornerRadius(rad), Padding = new Thickness(SizeKey == "XL" ? 4 : 6), BorderThickness = new Thickness(1), AllowDrop = true, Height = VerticalBar ? double.NaN : BarH, Width = VerticalBar ? SideWidth : double.NaN };
            shadow = new Border { CornerRadius = new CornerRadius(rad), Height = VerticalBar ? double.NaN : BarH, IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = IsTop ? 90 : 270, Opacity = 0.32 } };
            frame = new Grid { Margin = new Thickness(SM) }; frame.Children.Add(shadow);
            sectionSurface = new Canvas { IsHitTestVisible = false };
            frame.Children.Add(sectionSurface); frame.Children.Add(root);
            insertionOverlay = new Canvas { IsHitTestVisible = false };
            insertionLine = null; frame.Children.Add(insertionOverlay);
            sectionSurface.LayoutUpdated += (s, e) => { if (ReferenceEquals(s, sectionSurface) || s == null) PaintSections(); };
            if (DemoMode) frame.Children.Add(new TextBlock { Text = "ПРИМЕР", FontSize = 8, FontWeight = FontWeights.SemiBold, Opacity = 0.55, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 2, 0, 0), Foreground = Brushes.White });
            var orientation = VerticalBar ? Orientation.Vertical : Orientation.Horizontal;
            var row = new StackPanel { Orientation = orientation, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            root.Child = row; rowPanel = row;
            KeyboardNavigation.SetDirectionalNavigation(root, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);

            startLogo = new StartLogo();
            startEl = Tile(new Viewbox { Width = 16 * Fs, Height = 16 * Fs, Child = startLogo }, "Пуск", () => ShellOver(startEl, 0x5B), null); ((Press)startEl).ShellKey = true; // в ряду: его можно поставить между модулями
            searchEl = C("search", "1") == "1" ? Tile(G(""), "Поиск (Win+S)", () => ShellOver(searchEl, 0x5B, 0x53), null) : null; if (searchEl != null) ((Press)searchEl).ShellKey = true;
            strip = new StackPanel { Orientation = orientation };
            stripScroll = new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = VerticalBar ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = VerticalBar ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled, Focusable = false };
            if (VerticalBar) stripScroll.MaxHeight = Math.Max(TileH * 2, scr.Bounds.Height / MonScale - 2 * Gap - 4 * TileH);
            strip.SizeChanged += (s, e) => { if (e.WidthChanged || VerticalBar && e.HeightChanged) Fit(); };
            // колесо мыши листает ряд, если он не поместился
            stripScroll.PreviewMouseWheel += (s, e) => { if (!VerticalBar && stripScroll.ScrollableWidth > 0) { stripScroll.ScrollToHorizontalOffset(stripScroll.HorizontalOffset - e.Delta / 3.0); e.Handled = true; } };
            row.Children.Add(stripScroll);
            // стрелка трея — вне ряда модулей, поэтому никогда не уходит в «…»
            var trayArrow = Tile(G(IsTop ? "" : "", Math.Round(11 * Fs)), "Показать значки трея", () => { if (Tray.On) ShowTrayIcons(); else ShowTray(); }, null); // свой трей; не запустился — трей Windows
            trayArrow.Width = Math.Round(TileH * 0.6); ((TextBlock)trayArrow.Child).Opacity = 0.75;
            row.Children.Add(trayArrow);
            var doneT = L("Готово", 11.5 * Fs, true); doneT.Foreground = Frozen(OnAccent, 255);
            doneTile = Tile(doneT, "Завершить правку панели (закрыть каталог)", () => { if (catalog != null) catalog.Close(); }, null, true);
            doneTile.Base = AccentColor; doneTile.Background = new SolidColorBrush(doneTile.Base); doneTile.Visibility = editMode ? Visibility.Visible : Visibility.Collapsed;
            row.Children.Add(doneTile);
            widgetEls.Clear(); tokenOf.Clear(); appEls = new Dictionary<string, FrameworkElement>(); appWins = new Dictionary<string, List<Win>>();
            if (HasWidgets) foreach (var d in WidgetDefs) { try { var el = MakeWidget(d[0], live, true, ups); if (el != null) W(d[0], el); } catch (Exception ex) { Log(new Exception("Модуль " + d[0] + " не построен", ex)); } } // сбой одного модуля не ломает панель
            clockEl = MakeWidget("clock", live, true, ups) as FrameworkElement;
            var desk = Tile(FgShape(new Rectangle { Width = 2, Height = TileH * 0.45, RadiusX = 1, RadiusY = 1 }, 0.45), "Показать рабочий стол", ToggleDesktop,
                m => m.Items.Add(Item("Настроить фон рабочего стола", () => ShowSettings("bar"))));
            // свернуть панель в значок — рядом с «Показать рабочий стол»
            var foldBtn = Tile(G("", Math.Round(11 * Fs)), "Свернуть панель в значок", () => SetCollapsed(true), null);
            foldBtn.Width = Math.Round(TileH * 0.72); ((TextBlock)foldBtn.Child).Opacity = 0.7; desk.Width = 14;
            if (C("edgeBtns", "1") == "1") { row.Children.Add(foldBtn); row.Children.Add(desk); } // можно скрыть: свернуть — и из меню панели, рабочий стол — Win+D
            InitStripDrag();
            InitCatalogDrop(); Compose();
            // свёрнутая панель — один значок; клик раскрывает её с прежним составом
            foldTile = Tile(G("\uE700"), "Развернуть панель FlowBar", () => SetCollapsed(false), m => m.Items.Add(Item("Развернуть панель", () => SetCollapsed(false))));
            foldTile.Base = AccentColor; foldTile.Background = new SolidColorBrush(foldTile.Base); ((TextBlock)foldTile.Child).Foreground = Frozen(OnAccent, 255);
            row.Children.Insert(0, foldTile);
            collapsed = C("fold@" + scr.DeviceName, "0") == "1"; FoldVis();

            root.Drop += (s, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || Locked) return;
                foreach (var p in files) if (!pinned.Contains(p)) pinned.Add(p);
                SavePinned();
            };
            root.MouseRightButtonUp += (s, e) => { var m = BarMenu(); m.PlacementTarget = root; m.IsOpen = true; };
            root.SizeChanged += (s, e) => { Place(); };
            root.Loaded += (s, e) => Place();
            Content = frame;
        }

        // ---------- свёртка в значок: отдельное состояние, не автоскрытие; состав, таймеры и музыка продолжают работать ----------
        bool collapsed, folding; Press foldTile; StackPanel rowPanel;
        double FullW { get { return scr.Bounds.Width / MonScale - 2 * Gap; } }
        void FoldVis()
        {
            if (rowPanel == null) return;
            // форма — по фактическому состоянию: свёрнутая — всегда круг, раскрытая — как выбрано в оформлении; тень и регион размытия вместе с ней
            var rad = new CornerRadius(collapsed || !Flush ? BarH / 2 : 0);
            if (root.CornerRadius != rad) { root.CornerRadius = rad; shadow.CornerRadius = rad; }
            foreach (UIElement ch in rowPanel.Children)
                ch.Visibility = ch == foldTile ? (collapsed ? Visibility.Visible : Visibility.Collapsed)
                    : collapsed ? Visibility.Collapsed : ch == doneTile ? (editMode ? Visibility.Visible : Visibility.Collapsed) : Visibility.Visible;
            ApplyStyle();
        }
        void ApplyWidth()
        {
            if (root == null || folding) return;
            double w = VerticalBar ? (collapsed ? BarH : SideWidth) : FullWidth && !collapsed ? FullW : double.NaN;
            if (VerticalBar) root.Height = collapsed ? BarH : FullWidth ? scr.Bounds.Height / MonScale - 2 * Gap : double.NaN;
            if (!(double.IsNaN(w) && double.IsNaN(root.Width)) && !(Math.Abs(root.Width - w) < 0.5)) root.Width = w;
        }
        void SetCollapsed(bool on)
        {
            if (collapsed == on || rowPanel == null) return;
            ClosePreview(); if (panel != null) panel.Close();
            double from = root.ActualWidth;
            collapsed = on; cfg["fold@" + scr.DeviceName] = on ? "1" : "0"; SaveCfg();
            FoldVis();
            if (VerticalBar) { ApplyWidth(); Place(); return; }
            root.BeginAnimation(WidthProperty, null); root.Width = double.NaN; root.UpdateLayout();
            double to = !on && FullWidth ? FullW : root.ActualWidth;
            if (!Motion || from <= 0) { ApplyWidth(); Place(); return; }
            // ширина меняется плавно; содержимое обрезается по капсуле, пока она растёт или сжимается
            folding = true; root.ClipToBounds = true;
            var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            a.Completed += (s, e) => { folding = false; root.ClipToBounds = false; root.BeginAnimation(WidthProperty, null); root.Width = double.NaN; ApplyWidth(); Place(); };
            root.BeginAnimation(WidthProperty, a);
            rowPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(240)));
        }
        ContextMenu BarMenu()
        {
            var m = new ContextMenu();
            m.Items.Add(Item(collapsed ? "Развернуть панель" : "Свернуть в значок", () => SetCollapsed(!collapsed)));
            m.Items.Add(Item("Во всю ширину", () => { cfg["width"] = FullWidth ? "fit" : "full"; SaveCfg(); RebuildAll(); All(b => { b.reserveKey = null; b.Place(); }); RestyleWindows(); }, FullWidth));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Диспетчер задач", () => Launch("taskmgr.exe")));
            m.Items.Add(Item("Настройки FlowBar…", () => ShowSettings(null)));
            m.Items.Add(Item("Каталог виджетов…", () => ShowCatalog(this)));
            m.Items.Add(Item("Оформление…", () => ShowSettings("look")));
            m.Items.Add(Item("Закрепить программу…", () => Primary.ShowPinPicker()));
            m.Items.Add(Item("Заблокировать расположение значков", () => { cfg["lock"] = Locked ? "0" : "1"; SaveCfg(); }, Locked));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Выход", () => { ExitWhy = "пункт «Выход» в меню панели"; Application.Current.Shutdown(); }));
            return m;
        }

        // ---------- автоскрытие: «Никогда» или «Когда не используется» ----------
        static bool AutoHide { get { return C("autohide", "0") == "1"; } }
        static int menusOpen; static bool menuHooked;
        bool autoHidden, fsHidden; DispatcherTimer hideT; DateTime lastUse = DateTime.Now;
        void InitAutoHide()
        {
            if (!menuHooked)
            {
                menuHooked = true; // открытое контекстное меню удерживает панель
                EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler((s, e) => menusOpen++));
                EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent, new RoutedEventHandler((s, e) => menusOpen = Math.Max(0, menusOpen - 1)));
                EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler((s, e) => { if (s == e.OriginalSource) GuardMenu((ContextMenu)s); }));
            }
            hideT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            hideT.Tick += (s, e) => AutoHideTick(); hideT.Start();
        }
        // Меню закрывается кликом в другом месте и уходом в другую программу.
        // Сами панели не активируются (WS_EX_NOACTIVATE), поэтому WPF не узнаёт о клике снаружи и меню оставалось висеть.
        delegate IntPtr LLProc(int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LLProc p, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string m);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(N.POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        static ContextMenu guarded; static DispatcherTimer menuT; static IntPtr menuFg, mouseHook; static LLProc hookProc;
        static void GuardMenu(ContextMenu m)
        {
            guarded = m;
            menuFg = N.GetForegroundWindow(); // фокус не забираем: активация окна меню снимает у него захват мыши, и WPF сразу закрывает меню
            // перехват мыши — только пока меню открыто: видит каждое нажатие, даже самое быстрое
            if (mouseHook == IntPtr.Zero) { hookProc = MouseLL; mouseHook = SetWindowsHookEx(14 /* WH_MOUSE_LL */, hookProc, GetModuleHandle(null), 0); }
            if (menuT == null) { menuT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) }; menuT.Tick += (s, e) => MenuTick(); }
            menuT.Start();
        }
        static IntPtr MouseLL(int code, IntPtr w, IntPtr l)
        {
            int msg = w.ToInt32();
            if (code >= 0 && (msg == 0x201 || msg == 0x204 || msg == 0x207 || msg == 0x20B)) // нажатие левой, правой, средней, боковой кнопки
            {
                var p = new N.POINT { X = Marshal.ReadInt32(l, 0), Y = Marshal.ReadInt32(l, 4) };
                var h = GetAncestor(WindowFromPoint(p), 2 /* GA_ROOT */); var src = h == IntPtr.Zero ? null : HwndSource.FromHwnd(h);
                // мимо меню и его подменю (всплывающие окна FlowBar) — закрыть; сам клик проходит дальше как обычно
                if (src == null || src.RootVisual == null || src.RootVisual.GetType().Name != "PopupRoot")
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => CloseGuarded()));
            }
            return CallNextHookEx(mouseHook, code, w, l);
        }
        static void CloseGuarded()
        {
            if (guarded != null) guarded.IsOpen = false;
            guarded = null; if (menuT != null) menuT.Stop();
            if (mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
        }
        static void MenuTick()
        {
            // меню закрылось само (выбран пункт, Esc) — снимаем перехват; ушли в другое окно (Alt+Tab) — закрываем
            if (guarded == null || !guarded.IsOpen || N.GetForegroundWindow() != menuFg) CloseGuarded();
        }
        // пока открыта карточка или меню, идёт перетаскивание, правка или фокус клавиатуры на панели — не скрываемся
        bool InUse { get { return IsMouseOver || panel != null || preview != null || dragging || sDragging || editMode || menusOpen > 0 || IsKeyboardFocusWithin || (toast != null && primary); } }
        void AutoHideTick()
        {
            if (!AutoHide) { if (autoHidden) { autoHidden = false; ApplyVis(); } return; }
            N.POINT p; N.GetCursorPos(out p); var b = scr.Bounds;
            bool atEdge = VerticalBar ? p.Y >= b.Top && p.Y < b.Bottom && (LeftBar ? p.X <= b.Left + 1 && p.X >= b.Left : p.X >= b.Right - 2 && p.X < b.Right)
                                      : p.X >= b.Left && p.X < b.Right && (IsTop ? p.Y <= b.Top + 1 : p.Y >= b.Bottom - 2);
            if (autoHidden) { if (atEdge) { autoHidden = false; lastUse = DateTime.Now; ApplyVis(); } return; }
            if (InUse || atEdge) { lastUse = DateTime.Now; return; }
            if ((DateTime.Now - lastUse).TotalMilliseconds > 900) { autoHidden = true; ApplyVis(); }
        }
        void ApplyVis()
        {
            var v = fsHidden || autoHidden ? Visibility.Hidden : Visibility.Visible;
            if (Visibility == v) return;
            Visibility = v;
            if (v == Visibility.Visible && Motion && frame != null) frame.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        }

        // ---------- стиль и позиция ----------
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr mon, int type, out uint x, out uint y);
        // масштаб монитора этой панели (не окна: при переносе на экран с другим масштабом окно узнаёт его позже)
        double MonScale { get { uint x, y; try { if (GetDpiForMonitor(mon, 0, out x, out y) == 0 && x > 0) return x / 96.0; } catch { } return Scale; } }
        // окно FlowBar — в точку в DIP этой панели; ставим в физических пикселях, чтобы окно не съезжало между мониторами с разным масштабом
        void PlaceWin(Window w, double left, double top)
        {
            var h = new WindowInteropHelper(w).Handle; double sc = Scale;
            if (h == IntPtr.Zero) { w.Left = left; w.Top = top; return; }
            N.SetWindowPos(h, IntPtr.Zero, (int)Math.Round(left * sc), (int)Math.Round(top * sc), 0, 0, 0x1 | 0x4 | 0x10); // NOSIZE | NOZORDER | NOACTIVATE
        }
        double CapTop { get { return Top + SM; } }
        double CapBottom { get { return Top + SM + (root != null ? root.ActualHeight : BarH); } }
        double SideWidth { get { return Math.Max(90, BarH + 36); } }
        void ApplyStyle()
        {
            var s = Cur; bool glass = GlassOn;
            sectionPrismPulse = UseSections && Material == "prism";
            root.Background = UseSections ? Brushes.Transparent : MaterialFill(s, glass);
            root.BorderBrush = UseSections ? Brushes.Transparent : MaterialEdge(s, glass, true);
            shadow.Background = Frozen(s.Bg, (byte)Math.Max(1, s.Bg.A * Opac));
            shadow.Visibility = UseSections || glass || s.Bg.A < 0x60 || (Flush && !collapsed) ? Visibility.Collapsed : Visibility.Visible;
            Foreground = new SolidColorBrush(s.Fg);
            if (startLogo != null) startLogo.SetColor(Accent);
            Press.Ring = Accent;
            lastKey = null;
            if (hwnd == IntPtr.Zero) return;
            UpdateRegion();
        }
        // стекло включено и доступно; иначе стеклянная тема рисуется читаемой сплошной поверхностью
        static bool GlassOn { get { return WantsGlass(Cur) && Backdrop.Available; } }
        static Brush CardFill { get { return PopupFill(GlassOn); } }
        static Brush CardEdge { get { return MaterialEdge(Cur, GlassOn, false); } }
        static Brush PopupFill(bool glass)
        {
            if (Material == "metal") return MaterialFill(Cur, false);
            var color = MaterialTint(CardBg);
            if (!glass) return Frozen(color, 255);
            byte alpha = Material == "frost" ? (byte)0xDC : (byte)0xC8;
            var top = Color.FromArgb((byte)(alpha + 12), (byte)Math.Min(255, color.R + 9), (byte)Math.Min(255, color.G + 9), (byte)Math.Min(255, color.B + 9));
            return new LinearGradientBrush(top, Frozen(color, alpha).Color, 90);
        }
        static readonly FontFamily PopupFont = new FontFamily("Segoe UI Variable Text, Segoe UI");
        static void StylePopupText(Window window)
        {
            window.FontFamily = PopupFont;
            window.UseLayoutRounding = true;
            window.SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        }
        void Glass(Window w, Border card, double radius, double margin, UIElement shade = null)
        {
            if (!GlassOn) return;
            card.Effect = null; if (shade != null) shade.Visibility = Visibility.Collapsed; // за стеклом тень не нужна: материал и так отделяет карточку
            Backdrop bd = null; bool gone = false;
            Action sync = () =>
            {
                var h = new WindowInteropHelper(w).Handle; if (gone || h == IntPtr.Zero || card.ActualWidth < 1) return;
                if (bd == null && (bd = Backdrop.Create()) == null)
                {
                    card.Background = PopupFill(false); card.BorderBrush = MaterialEdge(Cur, false, false);
                    if (shade != null) shade.Visibility = Visibility.Visible;
                    return;
                }
                var src = PresentationSource.FromVisual(w); double sc = src == null ? 1 : src.CompositionTarget.TransformToDevice.M11; // масштаб окна карточки, а не панели
                N.RECT r; N.GetWindowRect(h, out r); int m = (int)Math.Round(margin * sc);
                bd.Set(h, r.L + m, r.T + m, (int)Math.Round(card.ActualWidth * sc), (int)Math.Round(card.ActualHeight * sc), radius * sc, w.IsVisible, GlassBlurPixels(sc));
            };
            card.SizeChanged += (o, e) => sync(); w.Loaded += (o, e) => sync(); w.LocationChanged += (o, e) => sync(); w.IsVisibleChanged += (o, e) => sync();
            w.Closing += (o, e) => { if (bd != null) bd.Fade(); };
            w.Closed += (o, e) => { gone = true; if (bd != null) bd.Dispose(); };
        }
        // уход окна в сторону панели за 120 мс; повторный Close во время ухода ничего не делает
        static void FadeClose(Window w, FrameworkElement card, double dy)
        {
            bool going = false, done = false;
            w.Closing += (o, e) =>
            {
                if (done || !Motion) return;
                e.Cancel = true; if (going) return; going = true; w.IsHitTestVisible = false;
                var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
                a.Completed += (o2, e2) => { done = true; w.Close(); };
                var tt = card.RenderTransform as TranslateTransform;
                if (tt != null) tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
                card.BeginAnimation(OpacityProperty, a);
            };
        }
        // стеклянный материал — окно-подложка ровно под капсулой (см. FlowBar.Glass.cs); поля окна под тень остаются прозрачными
        Backdrop glassBg; bool glassHooked, glassWas;
        void UpdateRegion()
        {
            if (hwnd == IntPtr.Zero || root == null) return;
            if (!glassHooked)
            {
                glassHooked = true;
                LocationChanged += (o, e) => UpdateRegion(); IsVisibleChanged += (o, e) => UpdateRegion(); root.SizeChanged += (o, e) => UpdateRegion();
                Closed += (o, e) => { if (glassBg != null) glassBg.Dispose(); glassBg = null; ClearSectionBackdrops(); if (primary) CloseDesktopMedia(true); };
            }
            bool gon = GlassOn;
            if (gon != glassWas) { glassWas = gon; ApplyStyle(); return; } // стекло стало (не)доступно — перерисовать заливку под новое состояние
            if (UseSections)
            {
                if (glassBg != null) glassBg.Set(hwnd, 0, 0, 0, 0, 0, false);
                PaintSections(true);
                return;
            }
            PaintSections();
            if (!gon) { if (glassBg != null) glassBg.Set(hwnd, 0, 0, 0, 0, 0, false); return; }
            if (glassBg == null) { glassBg = Backdrop.Create(); if (glassBg == null) { ApplyStyle(); return; } } // материал недоступен — сплошная заливка
            N.RECT wr; N.GetWindowRect(hwnd, out wr);
            double sc = Scale; var o0 = root.TranslatePoint(new Point(0, 0), this);
            glassBg.Set(hwnd, wr.L + (int)Math.Round(o0.X * sc), wr.T + (int)Math.Round(o0.Y * sc), (int)Math.Round(root.ActualWidth * sc), (int)Math.Round(root.ActualHeight * sc),
                root.CornerRadius.TopLeft * sc, IsVisible && root.ActualWidth > 0, GlassBlurPixels(sc));
        }
        void Place()
        {
            if (root == null || stripScroll == null) return;
            ApplyWidth(); Fit();
            double sc = MonScale; var b = scr.Bounds; var wa = scr.WorkingArea;
            bool hidden = HideTb && DateTime.Now >= peekUntil;
            Reserve(); EnsureWorkArea();
            double ch = root.ActualHeight > 0 ? root.ActualHeight : BarH;
            // физические пиксели монитора панели: размеры в DIP переводим его масштабом
            double x = b.Left + ((b.Width / sc - root.ActualWidth) / 2 - SM) * sc;
            double y = appBar ? (IsTop ? reserved.T + (Gap - SM) * sc : reserved.B - (Gap + ch + SM) * sc)
                              : (IsTop ? b.Top + (Gap - SM) * sc : (hidden ? b.Bottom : wa.Bottom) - (Gap + ch + SM) * sc);
            if (VerticalBar)
            {
                int left = appBar ? reserved.L : hidden ? b.Left : wa.Left;
                int right = appBar ? reserved.R : hidden ? b.Right : wa.Right;
                x = LeftBar ? left + (Gap - SM) * sc : right - (Gap + root.ActualWidth + SM) * sc;
                y = b.Top + (b.Height - ch * sc) / 2 - SM * sc;
            }
            if (hwnd != IntPtr.Zero) N.SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(x), (int)Math.Round(y), 0, 0, 0x1 | 0x4 | 0x10);
            else { Left = Math.Round(x / sc); Top = Math.Round(y / sc); }
            UpdateRegion();
        }
    }
}
