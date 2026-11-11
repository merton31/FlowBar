// FlowBar — карточки (система, музыка, переполнение, группы программ), каталог виджетов, окно настроек,
// уведомления и снимки для приёмки.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;
using System.Threading.Tasks;
using RD = Windows.Devices.Radios;
using NC = Windows.Networking.Connectivity;

namespace FlowBar
{
    partial class Bar
    {
        static readonly FontFamily Display = new FontFamily("Segoe UI Variable Display, Segoe UI");

        // ---------- единая структура карточки: основной показатель, подробности, действия, статус ----------
        static TextBlock CardHead(string s) { var t = Txt(s, 13, true); t.Margin = new Thickness(0, 16, 0, 6); return t; }
        static WrapPanel CardActions(params Border[] bs) { var w = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) }; foreach (var b in bs) { b.Margin = new Thickness(0, 0, 8, 8); w.Children.Add(b); } return w; }
        static TextBlock CardStatus(string s) { var t = Txt(s, 11.5, false); t.TextWrapping = TextWrapping.Wrap; t.LineHeight = 16; t.Opacity = 0.76; t.Margin = new Thickness(0, 6, 0, 0); return t; }
        static DockPanel KV(string k, string v, out TextBlock val)
        {
            var dp = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            val = Txt(v, 12, true); DockPanel.SetDock(val, Dock.Right); dp.Children.Add(val);
            var kt = Txt(k, 12, false); kt.Opacity = 0.75; dp.Children.Add(kt); return dp;
        }

        static int sysMetric; // 0 — процессор, 1 — память, 2 — сеть: какой показатель открыт в истории
        void ShowSystem()
        {
            var s = live; var body = new StackPanel();
            Func<string, Gauge> ring = name => new Gauge(64, 6, -90, 360, 14);
            Gauge gc = ring("ЦП"), gr = ring("ОЗУ"), gt = new Gauge(64, 6, 135, 270, 14);
            Func<Gauge, string, StackPanel> cell = (g, name) => { var sp = VS(g, L(name, 11, false, 0.75)); ((TextBlock)sp.Children[1]).HorizontalAlignment = HorizontalAlignment.Center; ((TextBlock)sp.Children[1]).Margin = new Thickness(0, 4, 0, 0); sp.Margin = new Thickness(0, 0, 22, 0); return sp; };
            body.Children.Add(HS(cell(gc, "Процессор"), cell(gr, "Память"), cell(gt, "Температура")));
            TextBlock memV, thrV, netV, tmpV, upV;
            // история: процессор и память — постоянная шкала 0–100 %, сеть — приём и отдача на общей шкале с подписью верха
            body.Children.Add(CardHead("История (до 2 минут)"));
            var segHost = new StackPanel(); body.Children.Add(segHost);
            var histG = new Grid { Height = 64, Margin = new Thickness(0, 10, 0, 4) };
            var fgc = CardFg; Spark hA = new Spark(340, 64), hB = new Spark(340, 64, Color.FromArgb(0xB0, fgc.R, fgc.G, fgc.B));
            histG.Children.Add(hA); histG.Children.Add(hB); body.Children.Add(histG);
            var scaleT = L("", 10.5, false, 0.65); scaleT.TextWrapping = TextWrapping.Wrap; body.Children.Add(scaleT);
            Action hist = () =>
            {
                hB.Visibility = sysMetric == 2 ? Visibility.Visible : Visibility.Collapsed;
                if (sysMetric < 2)
                {
                    var l = sysMetric == 0 ? s.CpuHist : s.RamHist; double now = sysMetric == 0 ? s.Cpu : s.Ram;
                    hA.Fixed = 100; hA.Set(l);
                    scaleT.Text = "За " + Ago(TimeSpan.FromSeconds(l.Count)) + " · " + (sysMetric == 0 ? "Процессор" : "Память") + " · сейчас " + (double.IsNaN(now) ? "—" : Math.Round(now) + " %") + " · макс. " + (l.Count == 0 ? "—" : Math.Round(l.Max()) + " %") + " · шкала 0–100 %";
                }
                else
                {
                    double top = Spark.Nice(Math.Max(s.RxHist.DefaultIfEmpty(0).Max(), s.TxHist.DefaultIfEmpty(0).Max()));
                    hA.Fixed = hB.Fixed = top; hA.Set(s.RxHist); hB.Set(s.TxHist);
                    scaleT.Text = "↓ приём — линия акцента, ↑ отдача — серая · шкала до " + RateRu(top);
                }
            };
            Action seg = null;
            seg = () => { segHost.Children.Clear(); segHost.Children.Add(Seg(new[] { "Процессор", "Память", "Сеть" }, sysMetric, i => { sysMetric = i; seg(); hist(); }, true)); };
            seg();
            body.Children.Add(CardHead("Подробности"));
            body.Children.Add(KV("Память", "", out memV)); body.Children.Add(KV("Потоков процессора", s.Threads.ToString(), out thrV));
            body.Children.Add(KV("Сеть", "", out netV));
            body.Children.Add(KV("Температура", "", out tmpV)); body.Children.Add(KV("Без перезагрузки", "", out upV));
            body.Children.Add(CardHead("Накопители"));
            foreach (var d in s.Disks)
            {
                var used = d.Item4 > 0 ? 1 - d.Item3 / d.Item4 : double.NaN; TextBlock dv;
                body.Children.Add(KV((d.Item2 != "" ? d.Item2 : "Локальный диск") + " (" + d.Item1 + ")", Gb(d.Item3) + " свободно из " + Gb(d.Item4), out dv));
                var fb = new FillBar(340, 5); fb.Set(used); fb.Margin = new Thickness(0, 0, 0, 6); body.Children.Add(fb);
            }
            if (s.Disks.Count == 0) body.Children.Add(Txt("—", 12, false));
            if (s.HasBat)
            {
                TextBlock bv; body.Children.Add(CardHead("Батарея"));
                body.Children.Add(KV(s.Charging ? "Заряжается" : "От батареи", Math.Round(s.Bat * 100) + "%" + (s.BatSec > 0 && !s.Charging ? " · ≈ " + Until(TimeSpan.FromSeconds(s.BatSec)) + " (оценка Windows)" : ""), out bv));
            }
            body.Children.Add(CardActions(Btn("Диспетчер задач", () => Launch("taskmgr.exe"), true), Btn("Хранилище", () => Launch("ms-settings:storagesense")), Btn("Сеть", () => Launch("ms-settings:network"))));
            var status = CardStatus(""); body.Children.Add(status);
            Action tick = () =>
            {
                gc.Set(s.Cpu / 100, double.IsNaN(s.Cpu) ? "—" : Math.Round(s.Cpu) + "%");
                gr.Set(s.Ram / 100, double.IsNaN(s.Ram) ? "—" : Math.Round(s.Ram) + "%");
                gt.Set(double.IsNaN(s.Temp) ? double.NaN : (s.Temp - 20) / 80, double.IsNaN(s.Temp) ? "—" : Math.Round(s.Temp) + "°");
                memV.Text = Gb(s.RamUsed) + " из " + Gb(s.RamTotal);
                netV.Text = "↓ " + RateRu(s.Rx) + "   ↑ " + RateRu(s.Tx); hist();
                tmpV.Text = double.IsNaN(s.Temp) ? "— (" + (s.TempNote ?? "нет данных") + ")" : Math.Round(s.Temp) + " °C · " + (s.TempNote ?? "");
                upV.Text = Until(TimeSpan.FromMilliseconds(Environment.TickCount & int.MaxValue));
                status.Text = "Обновлено " + DateTime.Now.ToString("HH:mm:ss") + " · данные раз в секунду. Температура — датчик платы (ACPI), не ядра процессора.";
            };
            tick();
            ShowPanel("Система", body, 380, null, tick);
        }

        // сеансы звука процессов плеера в микшере Windows: «Spotify.exe» → Spotify, «Издатель.Пакет!Spotify» → Spotify, «Chrome» → chrome.exe
        static List<Audio.MixerSession> PlayerSessions(Snap s)
        {
            var player = !s.HasMedia || string.IsNullOrEmpty(s.App) ? null : s.App.Contains("!") ? s.App.Substring(s.App.LastIndexOf('!') + 1) : System.IO.Path.GetFileNameWithoutExtension(s.App);
            if (player == null) return new List<Audio.MixerSession>();
            return Audio.MixerSessions().Where(x => { var ep = ExePath(x.Pid); return ep != null && string.Equals(System.IO.Path.GetFileNameWithoutExtension(ep), player, StringComparison.OrdinalIgnoreCase); }).ToList();
        }
        void ShowMusic()
        {
            var s = live; var body = new StackPanel();
            var tint = new Border { Child = body, CornerRadius = new CornerRadius(10), Padding = new Thickness(4) };
            var cover = new Border { Width = 112, Height = 112, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 16, 0) };
            var title = Txt("", 15, true); title.TextWrapping = TextWrapping.Wrap; title.FontFamily = Display;
            var artist = Txt("", 12, false); artist.Opacity = 0.75; artist.TextWrapping = TextWrapping.Wrap;
            var app = Txt("", 10, false); app.Opacity = 0.55; app.Margin = new Thickness(0, 6, 0, 0);
            var top = new DockPanel(); DockPanel.SetDock(cover, Dock.Left); top.Children.Add(cover); top.Children.Add(VS(title, artist, app));
            var trackHeader = new Grid { ClipToBounds = true }; trackHeader.Children.Add(top);
            body.Children.Add(trackHeader);
            // источники: при нескольких плеерах — выбор, кнопки управляют выбранным
            var srcHost = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; body.Children.Add(srcHost); string srcKey = null;
            var prog = new FillBar(348, 4); var hit = new Border { Height = 16, Background = Brushes.Transparent, Child = prog, Margin = new Thickness(0, 8, 0, 0) }; body.Children.Add(hit);
            // перемотка: нажать или протянуть по полосе, если плеер её поддерживает
            bool seeking = false;
            Func<MouseEventArgs, double> fracAt = e => Math.Max(0, Math.Min(1, e.GetPosition(prog).X / prog.Width));
            hit.MouseLeftButtonDown += (o, e) => { if (!s.CanSeek || double.IsNaN(s.Dur)) return; seeking = true; hit.CaptureMouse(); prog.Set(fracAt(e), Accent, true); e.Handled = true; };
            hit.MouseMove += (o, e) => { if (seeking) prog.Set(fracAt(e), Accent, true); };
            hit.MouseLeftButtonUp += (o, e) => { if (!seeking) return; seeking = false; hit.ReleaseMouseCapture(); MediaCmd("seek", fracAt(e)); };
            var times = new DockPanel(); var t1 = Txt("", 10, false); var t2 = Txt("", 10, false); t1.Opacity = t2.Opacity = 0.6;
            DockPanel.SetDock(t2, Dock.Right); times.Children.Add(t2); times.Children.Add(t1); body.Children.Add(times);
            var play = RoundBtn("\uE768", 48, true, () => MediaCmd("toggle"), "Воспроизведение / пауза");
            Press prevB = RoundBtn("\uE892", 40, false, () => MediaCmd("prev"), "Предыдущий трек"), nextB = RoundBtn("\uE893", 40, false, () => MediaCmd("next"), "Следующий трек");
            var ctr = HS(prevB, Mg(play, 10, 0, 10, 0), nextB);
            ctr.HorizontalAlignment = HorizontalAlignment.Center; ctr.Margin = new Thickness(0, 8, 0, 4); body.Children.Add(ctr);
            // громкость самого плеера (его сеанс в микшере Windows); общая громкость не меняется
            var volIcon = G("", 14); volIcon.Opacity = 0.7;
            var vol = new Slider { Minimum = 0, Maximum = 100, Width = 250, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true }; vol.Style = SliderSty();
            System.Windows.Automation.AutomationProperties.SetName(vol, "Громкость плеера");
            var volPct = Txt("", 11, false); volPct.Text = "0%"; volPct.Width = 40; volPct.Opacity = 0.7; volPct.TextAlignment = TextAlignment.Right;
            var volRow = HS(volIcon, Mg(vol, 10, 0, 6, 0), volPct); volRow.HorizontalAlignment = HorizontalAlignment.Center; volRow.Margin = new Thickness(0, 4, 0, 2); volRow.Visibility = Visibility.Collapsed;
            volRow.ToolTip = "Громкость только этого плеера — общая громкость Windows не меняется";
            body.Children.Add(volRow);
            var volSess = new List<Audio.MixerSession>(); bool volSync = false;
            vol.ValueChanged += (o, e) =>
            {
                volPct.Text = Math.Round(e.NewValue) + "%"; volIcon.Text = e.NewValue < 1 ? "" : "";
                if (volSync) return;
                foreach (var x in volSess) { if (x.Muted && e.NewValue >= 1) x.SetMute(false); x.SetVolume((float)(e.NewValue / 100)); }
            };
            var status = CardStatus(""); body.Children.Add(status);
            ImageSource shown = null; bool first = true; string shownTrack = null;
            Func<double, string> mmss = x => double.IsNaN(x) ? "—" : ((int)(x / 60)) + ":" + ((int)x % 60).ToString("00");
            Action tick = () =>
            {
                var track = s.HasMedia ? s.Title + "\n" + s.Artist : "";
                bool transitioning = shownTrack != null && track != shownTrack && Motion;
                if (transitioning) CrossfadeTrackHeader(trackHeader, top, tint.Background);
                shownTrack = track;
                title.Text = s.HasMedia ? s.Title : SpotifyOnly ? (spotifyUp ? "Spotify: ничего не играет" : "Spotify не запущен") : "Ничего не играет";
                artist.Text = s.HasMedia ? s.Artist ?? "" : SpotifyOnly ? (spotifyUp ? "Включите трек в Spotify" : "Нажмите ▶, чтобы открыть") : "Включите музыку в любом плеере — FlowBar подхватит её через Windows.";
                app.Text = s.HasMedia ? "Источник: " + AppTitle(s.App) : "";
                ((TextBlock)play.Child).Text = s.Playing ? "\uE769" : "\uE768";
                Enable(play, !s.HasMedia || s.CanToggle); Enable(prevB, s.HasMedia && s.CanPrev); Enable(nextB, s.HasMedia && s.CanNext);
                bool stream = s.HasMedia && double.IsNaN(s.Dur);
                if (!seeking) prog.Set(s.HasMedia && !stream ? s.Pos / s.Dur : double.NaN, Accent); prog.Squeeze(s.HasMedia && !s.Playing && !seeking);
                hit.Cursor = s.CanSeek && !stream ? Cursors.Hand : null; hit.ToolTip = stream ? null : s.CanSeek ? "Нажмите или протяните, чтобы перемотать" : "Плеер не поддерживает перемотку";
                t1.Text = stream ? "Трансляция — длительность неизвестна" : mmss(s.Pos); t2.Text = stream ? "" : mmss(s.Dur);
                var key = string.Join("|", s.Sessions.Select(x => x.Item1)) + "#" + (mediaPick ?? "");
                if (key != srcKey)
                {
                    srcKey = key; srcHost.Children.Clear();
                    if (s.Sessions.Count > 1)
                    {
                        var cur = s.App; int sel = Math.Max(0, s.Sessions.FindIndex(x => x.Item1 == cur));
                        srcHost.Children.Add(Seg(s.Sessions.Select(x => x.Item2).ToArray(), sel, i => { mediaPick = s.Sessions[i].Item1; lastMedia = null; srcKey = null; UpdateMedia(); }, true));
                        StyleInputs(srcHost, CardFg);
                    }
                }
                if (first || shown != s.Cover)
                {
                    if (!first && !transitioning) Soft(cover);
                    first = false; shown = s.Cover; var ac = AccentColor;
                    tint.Background = MusicTint(s.Cover);
                    cover.Background = s.Cover != null ? (Brush)new ImageBrush(s.Cover) { Stretch = Stretch.UniformToFill } : new LinearGradientBrush(Color.FromArgb(0x90, ac.R, ac.G, ac.B), Color.FromArgb(0x20, ac.R, ac.G, ac.B), 45);
                    cover.Child = s.Cover != null ? null : G("", 36);
                }
                status.Text = s.MediaError != null ? "Источник недоступен: " + s.MediaError : s.Sessions.Count > 1 ? "Плееров: " + s.Sessions.Count : "";
                volSess = PlayerSessions(s);
                volRow.Visibility = volSess.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (volSess.Count > 0 && !vol.IsMouseCaptureWithin)
                {
                    var v = volSess.Max(x => x.Muted ? 0 : x.Volume) * 100.0;
                    if (Math.Abs(v - vol.Value) > 0.5) { volSync = true; vol.Value = v; volSync = false; } // громкость поменяли в самом плеере или в микшере
                }
            };
            tick();
            ShowPanel("Музыка", tint, 394, null, tick);
        }

        static void CrossfadeTrackHeader(Grid host, FrameworkElement current, Brush oldTint)
        {
            if (!current.IsLoaded || current.ActualWidth <= 0 || current.ActualHeight <= 0) return;
            while (host.Children.Count > 1) host.Children.RemoveAt(host.Children.Count - 1);
            current.BeginAnimation(OpacityProperty, null);
            var dpi = VisualTreeHelper.GetDpi(current);
            int w = (int)Math.Ceiling(current.ActualWidth * dpi.DpiScaleX), h = (int)Math.Ceiling(current.ActualHeight * dpi.DpiScaleY);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var area = new Rect(0, 0, current.ActualWidth, current.ActualHeight);
                dc.DrawRectangle(oldTint, null, area);
                dc.DrawRectangle(new VisualBrush(current), null, area);
            }
            var bitmap = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(visual); bitmap.Freeze();
            var previous = new Image { Source = bitmap, Stretch = Stretch.None, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left };
            host.Children.Add(previous);
            int duration = ExpressiveMotion ? 300 : 190;
            var fade = SettingsMotion(1, 0, duration);
            fade.Completed += (s, e) => host.Children.Remove(previous);
            previous.BeginAnimation(OpacityProperty, fade);
            current.BeginAnimation(OpacityProperty, SettingsMotion(0, 1, duration));
        }

        static void ClosePanels() { foreach (var b in bars) if (b.panel != null) b.panel.Close(); }
        // Переключатель с подтверждённым состоянием: выключено / выполняется / включено. Пока действие идёт — повторный клик
        // не принимается; после него положение берётся из настоящего состояния (read), при отказе — возвращается и показывается пояснение.
        // Tag — Action синхронизации: карточка вызывает её каждую секунду, чтобы поймать изменение извне.
        static Press Switch(Func<bool> read, Func<bool, Task<string>> act, string name, Action<string> err = null)
        {
            var knob = new Ellipse { Width = 16, Height = 16, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.25 } };
            var b = new Press { Width = 40, Height = 22, CornerRadius = new CornerRadius(11), Child = knob, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            bool state = read(), busy = false;
            Action<bool> paint = anim =>
            {
                var to = new Thickness(busy ? 12 : state ? 21 : 3, 0, 0, 0);
                if (anim && Motion) knob.BeginAnimation(FrameworkElement.MarginProperty, new ThicknessAnimation(to, TimeSpan.FromMilliseconds(150))); else { knob.BeginAnimation(FrameworkElement.MarginProperty, null); knob.Margin = to; }
                b.Background = state && !busy ? (Brush)new SolidColorBrush(AccentColor) : Frozen(CardFg, 0x30); b.Opacity = busy ? 0.6 : 1;
                System.Windows.Automation.AutomationProperties.SetName(b, name + (busy ? ", выполняется" : state ? ", включено" : ", выключено"));
            };
            paint(false);
            Action flip = async () =>
            {
                if (busy) return; busy = true; paint(true); string e = null;
                try { e = await act(!state); } catch (Exception ex) { e = ex.Message; }
                busy = false; try { state = read(); } catch { } paint(true);
                if (e != null && err != null) err(e);
            };
            b.Invoke = flip; b.MouseLeftButtonUp += (s, e2) => { e2.Handled = true; flip(); };
            b.Tag = (Action)(() => { if (busy) return; bool now; try { now = read(); } catch { return; } if (now != state) { state = now; paint(true); } });
            return b;
        }
        // для самопроверки: действие отказало — переключатель должен остаться выключенным и сообщить причину
        public static string ProbeSwitch()
        {
            string got = null; bool dev = false;
            var b = Switch(() => dev, v => Done("отказ"), "Проба", e => got = e);
            b.Invoke();
            return System.Windows.Automation.AutomationProperties.GetName(b) + "|" + got;
        }
        static Task<string> Done(string err = null) { return Task.FromResult(err); }
        static void SyncSwitches(Panel p) { foreach (var ch in p.Children.OfType<FrameworkElement>()) { var a = ch.Tag as Action; if (a != null) a(); var sub = ch as Panel; if (sub != null) SyncSwitches(sub); } }
        // строка быстрой карточки: значок в круге, подпись и состояние, переключатель, «›» — подробная карточка
        static DockPanel QRow(string glyph, string title, FrameworkElement ctl, Action more, out TextBlock sub, out Border ic)
        {
            ic = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Child = G(glyph, 14), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            ((TextBlock)ic.Child).HorizontalAlignment = HorizontalAlignment.Center;
            var dp = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
            DockPanel.SetDock(ic, Dock.Left); dp.Children.Add(ic);
            if (more != null) { var mb = Act(G("\uE76C", 11), title + " — подробнее", more); mb.Padding = new Thickness(10, 8, 4, 8); mb.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(mb, Dock.Right); dp.Children.Add(mb); }
            if (ctl != null) { ctl.Margin = new Thickness(8, 0, 0, 0); DockPanel.SetDock(ctl, Dock.Right); dp.Children.Add(ctl); }
            var vs = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; vs.Children.Add(Txt(title, 12.5, false));
            sub = Txt("", 11, false); sub.Opacity = 0.65; sub.TextWrapping = TextWrapping.Wrap; vs.Children.Add(sub);
            dp.Children.Add(vs);
            return dp;
        }
        static void PaintIcon(Border ic, bool on) { ic.Background = on ? (Brush)new SolidColorBrush(AccentColor) : Frozen(CardFg, 0x14); ((TextBlock)ic.Child).Foreground = on ? Frozen(OnAccent, 255) : Frozen(CardFg, 0xE0); }
        // ползунок уровня — настоящий Slider: фокус по Tab, стрелки, Home/End, имя для экранного диктора; пока тянут — внешнее обновление его не трогает
        static Slider LevelSlider(double val, double width, string name, Action<double> moving, Action<double> done)
        {
            var sl = new Slider { Minimum = 0, Maximum = 100, Value = double.IsNaN(val) ? 0 : Math.Round(val * 100), Width = width, SmallChange = 1, LargeChange = 10, IsMoveToPointEnabled = true, Margin = new Thickness(44, 0, 0, 6), Focusable = true, HorizontalAlignment = HorizontalAlignment.Left }; sl.Style = SliderSty(); sl.AutoToolTipPlacement = System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft;
            System.Windows.Automation.AutomationProperties.SetName(sl, name);
            bool quiet = false;
            sl.ValueChanged += (o, e) => { if (!quiet && moving != null) moving(sl.Value / 100); };
            Action commit = () => { if (done != null) done(sl.Value / 100); };
            sl.PreviewMouseLeftButtonUp += (o, e) => commit(); sl.KeyUp += (o, e) => commit();
            sl.Tag = (Action<double>)(x => { if (sl.IsMouseCaptureWithin || sl.IsKeyboardFocusWithin || double.IsNaN(x)) return; quiet = true; sl.Value = Math.Round(x * 100); quiet = false; });
            return sl;
        }
        static RD.Radio RadioOf(RD.RadioKind k) { return radioList == null ? null : radioList.FirstOrDefault(r => r.Kind == k); }

        // ---------- быстрые переключатели: каждый действительно меняет своё состояние ----------
        void ShowQuick()
        {
            var body = new StackPanel(); var s = live; var err = Txt("", 11, false); err.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0x6A, 0x5A)); err.TextWrapping = TextWrapping.Wrap;
            Action<string> fail = e => err.Text = e;
            var wr = RadioOf(RD.RadioKind.WiFi); var br = RadioOf(RD.RadioKind.Bluetooth);
            Func<RD.Radio, Func<bool, Task<string>>> radioAct = r => async v =>
            {
                var st = await r.SetStateAsync(v ? RD.RadioState.On : RD.RadioState.Off);
                return st == RD.RadioAccessStatus.Allowed ? null : "Windows не разрешила переключить: " + st;
            };
            var upd = new List<Action>(); TextBlock sub; Border ic;
            body.Children.Add(QRow("\uE701", "Wi-Fi", wr == null ? null : Switch(() => wr.State == RD.RadioState.On, radioAct(wr), "Wi-Fi", fail), ShowWifi, out sub, out ic));
            { var t = sub; var i = ic; upd.Add(() => { bool on = wr != null && wr.State == RD.RadioState.On; PaintIcon(i, on); t.Text = wr == null ? "Адаптер Wi-Fi не найден" : !on ? "Выключен" : s.NetKind == 2 ? (s.NetName ?? "Wi-Fi") + " · " + ConnAccess(2, s.NetLevel) : "Включён, не подключён"; }); }
            body.Children.Add(QRow("\uE702", "Bluetooth", br == null ? null : Switch(() => br.State == RD.RadioState.On, radioAct(br), "Bluetooth", fail), ShowBluetooth, out sub, out ic));
            { var t = sub; var i = ic; upd.Add(() => { bool on = br != null && br.State == RD.RadioState.On; PaintIcon(i, on); t.Text = br == null ? "Адаптер Bluetooth не найден" : on ? "Включён" : "Выключен"; }); }
            body.Children.Add(QRow("\uE767", "Звук", Switch(() => !double.IsNaN(live.Vol) && !live.Muted, v => { Audio.SetMute(!v); ReadVolume(); All(b => b.Render()); return Done(double.IsNaN(live.Vol) ? "Устройство вывода не найдено" : live.Muted == v ? "Windows не изменила звук" : null); }, "Звук", fail), null, out sub, out ic));
            var vol = LevelSlider(s.Vol, 290, "Громкость", x => { Audio.SetVolume((float)x); }, x => { ReadVolume(); All(b => b.Render()); VolBeep(); });
            body.Children.Add(vol);
            // устройство вывода: список включённых, клик — сделать основным
            var devHost = new StackPanel { Margin = new Thickness(44, 2, 0, 6) }; body.Children.Add(devHost); string devKey = null;
            Action devFill = () =>
            {
                var ds = Audio.Devices(); var key = string.Join("|", ds.Select(x => x.Item1 + x.Item3)); if (key == devKey) return; devKey = key;
                devHost.Children.Clear(); if (ds.Count < 2) return;
                foreach (var d0 in ds)
                {
                    var dv = d0; var row = new DockPanel { Margin = new Thickness(6, 4, 6, 4) };
                    var chk = new TextBlock { Text = dv.Item3 ? "" : "", FontFamily = Icons, FontSize = 11, Width = 18, VerticalAlignment = VerticalAlignment.Center, Foreground = Accent };
                    var nm = Txt(dv.Item2, 12, dv.Item3); nm.TextTrimming = TextTrimming.CharacterEllipsis; if (!dv.Item3) nm.Opacity = 0.8;
                    row.Children.Add(chk); row.Children.Add(nm);
                    var p = Act(row, "Устройство вывода: " + dv.Item2, () => { var e2 = Audio.SetDefault(dv.Item1); if (e2 != null) fail(e2); ReadVolume(); All(b => b.Render()); devKey = null; });
                    p.MouseEnter += (o, e) => Fade(p, Color.FromArgb(0x1C, CardFg.R, CardFg.G, CardFg.B), 100); p.MouseLeave += (o, e) => Fade(p, Colors.Transparent, 140);
                    devHost.Children.Add(p);
                }
            };
            devFill(); upd.Add(devFill);
            { var t = sub; var i = ic; upd.Add(() => { bool none = double.IsNaN(s.Vol); PaintIcon(i, !none && !s.Muted); ((TextBlock)i.Child).Text = none || s.Muted ? "\uE74F" : "\uE767"; vol.IsEnabled = !none;
                // подпись — по ползунку, пока его тянут; иначе — подтверждённое значение устройства
                double shown = vol.IsMouseCaptureWithin || vol.IsKeyboardFocusWithin ? vol.Value / 100 : s.Vol;
                t.Text = none ? "Устройство вывода не найдено" : (s.VolName ?? "Устройство вывода") + " · " + (s.Muted ? "выключен" : Math.Round(shown * 100) + "%"); ((Action<double>)vol.Tag)(s.Vol); }); }
            int bl = Bright.Get();
            body.Children.Add(QRow("\uE706", "Яркость", null, null, out sub, out ic)); PaintIcon(ic, bl >= 0);
            sub.Text = bl >= 0 ? bl + "%" : "Внешний монитор: Windows не регулирует его яркость — кнопками на мониторе";
            if (bl >= 0) { var bt = sub; body.Children.Add(LevelSlider(bl / 100.0, 290, "Яркость", x => bt.Text = Math.Round(x * 100) + "%", x => Bright.Set((int)Math.Round(x * 100)))); }
            body.Children.Add(QRow("\uE7E8", "Не засыпать", Switch(() => Desk.Awake, v => { Desk.SetAwake(v); return Done(); }, "Не засыпать", fail), null, out sub, out ic));
            sub.Text = "Компьютер и экран не уходят в сон, пока включено"; { var i = ic; upd.Add(() => PaintIcon(i, Desk.Awake)); }
            body.Children.Add(QRow("\uE7F4", "Значки рабочего стола", Switch(() => Desk.IconsShown, async v => { if (v != Desk.IconsShown) Desk.ToggleIcons(); await Task.Delay(400); return Desk.IconsShown == v ? null : "Проводник не переключил значки"; }, "Значки рабочего стола", fail), null, out sub, out ic));
            { var t = sub; var i = ic; upd.Add(() => { bool on = Desk.IconsShown; PaintIcon(i, on); t.Text = on ? "Показаны" : "Скрыты"; }); }
            body.Children.Add(QRow("\uE790", "Тёмная тема FlowBar", Switch(() => Cur.LightText, v =>
            {
                var cur = C("style", "Black");
                if (v) { cfg["style.light"] = cur; cfg["style"] = C("style.dark", "Black"); } else { cfg["style.dark"] = cur; cfg["style"] = C("style.light", "White"); }
                SaveCfg(); ClosePanels(); RebuildAll(); RestyleWindows(); return Done();
            }, "Тёмная тема FlowBar", fail), null, out sub, out ic));
            sub.Text = ThemeName(C("style", "Black")); PaintIcon(ic, Cur.LightText);
            body.Children.Add(err);
            body.Children.Add(CardActions(Btn("Быстрые настройки Windows", () => { if (panel != null) panel.Close(); ShellToggle(0x5B, 0x41); }), Btn("Параметры", () => Launch("ms-settings:"))));
            // открытая карточка следит за изменениями извне раз в секунду: обновляются подписи, значки и переключатели, ползунок — если его не тянут
            Action tick = () => { foreach (var u in upd) u(); SyncSwitches(body); };
            tick();
            ShowPanel("Быстрые переключатели", body, 380, null, tick);
        }

        // ---------- Wi-Fi: состояние радио, сеть, сигнал и список доступных сетей ----------
        async void ShowWifi()
        {
            var body = new StackPanel(); var s = live; var wr = RadioOf(RD.RadioKind.WiFi);
            var list = new StackPanel(); var state = Txt("", 13, true); var sub = Txt("", 11.5, false); sub.Opacity = 0.7; sub.TextWrapping = TextWrapping.Wrap;
            Func<Task> load = null;
            var head = new DockPanel();
            if (wr != null) { var swt = Switch(() => wr.State == RD.RadioState.On, async v => { var st = await wr.SetStateAsync(v ? RD.RadioState.On : RD.RadioState.Off); await load(); return st == RD.RadioAccessStatus.Allowed ? null : "Windows не разрешила переключить: " + st; }, "Wi-Fi", e => sub.Text = e); DockPanel.SetDock(swt, Dock.Right); head.Children.Add(swt); }
            head.Children.Add(state); body.Children.Add(head); body.Children.Add(sub);
            body.Children.Add(CardHead("Доступные сети")); body.Children.Add(list);
            body.Children.Add(CardActions(Btn("Подключиться через Windows", () => { if (panel != null) panel.Close(); Launch("ms-availablenetworks:"); }, true), Btn("Параметры Wi-Fi", () => Launch("ms-settings:network-wifi"))));
            load = async () =>
            {
                bool on = wr != null && wr.State == RD.RadioState.On;
                state.Text = wr == null ? "Адаптер Wi-Fi не найден" : !on ? "Wi-Fi выключен" : s.NetKind == 2 ? "Подключено: " + (s.NetName ?? "Wi-Fi") : "Wi-Fi включён, не подключён";
                // «нет интернета» и «Wi-Fi выключен» — разные состояния
                sub.Text = s.NetKind == 2 && on ? "Сигнал " + (s.NetBars >= 0 ? s.NetBars + " из 5" : "—") + " · " + ConnAccess(2, s.NetLevel) : s.NetKind == 1 ? "Интернет сейчас идёт по кабелю (Ethernet) · " + ConnAccess(1, s.NetLevel) : s.NetKind == 0 ? "Нет подключения к сети" : "";
                list.Children.Clear();
                if (!on) { list.Children.Add(Txt(wr == null ? "—" : "Включите Wi-Fi, чтобы увидеть сети.", 12, false)); return; }
                list.Children.Add(Txt("Поиск сетей…", 12, false));
                try
                {
                    if (await Windows.Devices.WiFi.WiFiAdapter.RequestAccessAsync() != Windows.Devices.WiFi.WiFiAccessStatus.Allowed)
                    { list.Children.Clear(); var t = Txt("Windows не дала FlowBar список сетей. Разрешите «Расположение» для классических приложений (Параметры → Конфиденциальность → Расположение) или откройте список Windows.", 11.5, false); t.TextWrapping = TextWrapping.Wrap; list.Children.Add(t); return; }
                    var ads = await Windows.Devices.WiFi.WiFiAdapter.FindAllAdaptersAsync();
                    if (ads.Count == 0) { list.Children.Clear(); list.Children.Add(Txt("Адаптер Wi-Fi не найден", 12, false)); return; }
                    var ad = ads[0]; try { await ad.ScanAsync(); } catch { }
                    var nets = ad.NetworkReport.AvailableNetworks.Where(x => !string.IsNullOrEmpty(x.Ssid)).GroupBy(x => x.Ssid).Select(g => g.OrderByDescending(x => x.SignalBars).First())
                        .OrderByDescending(x => s.NetKind == 2 && x.Ssid == s.NetName).ThenByDescending(x => x.SignalBars).Take(12).ToList();
                    list.Children.Clear();
                    if (nets.Count == 0) list.Children.Add(Txt("Сети не найдены", 12, false));
                    foreach (var x in nets)
                    {
                        bool cur = s.NetKind == 2 && x.Ssid == s.NetName; var auth = x.SecuritySettings.NetworkAuthenticationType;
                        bool open = auth == NC.NetworkAuthenticationType.Open80211 || auth == NC.NetworkAuthenticationType.None;
                        var dp = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                        var bars = x.SignalBars; var gl = G(bars >= 4 ? "\uE701" : bars == 3 ? "\uE874" : bars == 2 ? "\uE873" : "\uE872", 15); gl.Margin = new Thickness(0, 0, 12, 0); DockPanel.SetDock(gl, Dock.Left); dp.Children.Add(gl);
                        var rt = Txt((cur ? "подключено · " : "") + (open ? "открытая" : "защищена"), 11, false); rt.Opacity = 0.65; DockPanel.SetDock(rt, Dock.Right); dp.Children.Add(rt);
                        dp.Children.Add(Txt(x.Ssid, 12.5, cur));
                        var ssid = x.Ssid; list.Children.Add(Act(dp, ssid + (cur ? ", подключено" : ""), () => { if (panel != null) panel.Close(); Launch("ms-availablenetworks:"); }));
                    }
                }
                catch (Exception ex) { list.Children.Clear(); var t = Txt("Не удалось получить список сетей: " + ex.Message, 11.5, false); t.TextWrapping = TextWrapping.Wrap; list.Children.Add(t); }
            };
            // карточка открыта — радио, сеть и доступ отслеживаются: при изменении извне список и подписи обновляются
            string seen = null; bool loading = false;
            Action tick = async () =>
            {
                SyncSwitches(head); var k = (wr == null ? "" : wr.State.ToString()) + "|" + s.NetKind + "|" + s.NetName + "|" + s.NetLevel;
                if (k == seen || loading) return; seen = k; loading = true; try { await load(); } finally { loading = false; }
            };
            ShowPanel("Wi-Fi", body, 380, null, tick);
            tick();
        }

        // ---------- Bluetooth: переключатель и сопряжённые устройства с состоянием подключения ----------
        async void ShowBluetooth()
        {
            var body = new StackPanel(); var br = RadioOf(RD.RadioKind.Bluetooth);
            var list = new StackPanel(); var state = Txt("", 13, true);
            Func<Task> load = null;
            var head = new DockPanel();
            if (br != null) { var swt = Switch(() => br.State == RD.RadioState.On, async v => { var st = await br.SetStateAsync(v ? RD.RadioState.On : RD.RadioState.Off); await load(); return st == RD.RadioAccessStatus.Allowed ? null : "Windows не разрешила переключить: " + st; }, "Bluetooth", e => state.Text = e); DockPanel.SetDock(swt, Dock.Right); head.Children.Add(swt); }
            head.Children.Add(state); body.Children.Add(head);
            body.Children.Add(CardHead("Сопряжённые устройства")); body.Children.Add(list);
            body.Children.Add(CardActions(Btn("Добавить устройство", () => { if (panel != null) panel.Close(); Launch("ms-settings:connecteddevices"); }, true), Btn("Параметры Bluetooth", () => Launch("ms-settings:bluetooth"))));
            load = async () =>
            {
                bool on = br != null && br.State == RD.RadioState.On;
                state.Text = br == null ? "Адаптер Bluetooth не найден" : on ? "Bluetooth включён" : "Bluetooth выключен";
                list.Children.Clear();
                if (!on) { var t = Txt(br == null ? "—" : "Включите Bluetooth, чтобы подключить наушники и другие устройства.", 12, false); t.TextWrapping = TextWrapping.Wrap; list.Children.Add(t); return; }
                list.Children.Add(Txt("Поиск устройств…", 12, false));
                try
                {
                    var found = new Dictionary<string, bool>();
                    foreach (var di in await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true)))
                    {
                        bool conn = false;
                        try { var dv = await Windows.Devices.Bluetooth.BluetoothDevice.FromIdAsync(di.Id); if (dv != null) { conn = dv.ConnectionStatus == Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected; dv.Dispose(); } } catch { }
                        if (!string.IsNullOrEmpty(di.Name)) found[di.Name] = (found.ContainsKey(di.Name) && found[di.Name]) || conn;
                    }
                    foreach (var di in await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)))
                    {
                        bool conn = false;
                        try { var dv = await Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(di.Id); if (dv != null) { conn = dv.ConnectionStatus == Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected; dv.Dispose(); } } catch { }
                        if (!string.IsNullOrEmpty(di.Name)) found[di.Name] = (found.ContainsKey(di.Name) && found[di.Name]) || conn;
                    }
                    list.Children.Clear();
                    if (found.Count == 0) { var t = Txt("Сопряжённых устройств нет. «Добавить устройство» откроет поиск Windows.", 12, false); t.TextWrapping = TextWrapping.Wrap; list.Children.Add(t); }
                    foreach (var kv in found.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
                    {
                        var dp = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
                        var gl = G("\uE702", 14); gl.Margin = new Thickness(0, 0, 12, 0); gl.Opacity = kv.Value ? 1 : 0.5; DockPanel.SetDock(gl, Dock.Left); dp.Children.Add(gl);
                        var rt = Txt(kv.Value ? "подключено" : "не подключено", 11, kv.Value); rt.Opacity = kv.Value ? 1 : 0.6; if (kv.Value) rt.Foreground = Accent; DockPanel.SetDock(rt, Dock.Right); dp.Children.Add(rt);
                        dp.Children.Add(Txt(kv.Key, 12.5, false));
                        list.Children.Add(dp);
                    }
                }
                catch (Exception ex) { list.Children.Clear(); list.Children.Add(Txt("Не удалось получить устройства: " + ex.Message, 11.5, false)); }
            };
            // открытая карточка: смена радио извне — сразу, состояние подключения устройств — раз в 15 с
            string seen = null; bool loading = false; DateTime last = DateTime.MinValue;
            Action tick = async () =>
            {
                SyncSwitches(head); var k = br == null ? "" : br.State.ToString();
                if (loading || (k == seen && (DateTime.Now - last).TotalSeconds < 15)) return; seen = k; last = DateTime.Now; loading = true; try { await load(); } finally { loading = false; }
            };
            ShowPanel("Bluetooth", body, 380, null, tick);
            tick();
        }

        // ---------- накопитель: выбранный диск, занято/свободно, открытие и очистка ----------
        static int storeSel;
        void ShowStorage()
        {
            var s = live; var body = new StackPanel(); var disks = s.Disks.ToList();
            if (disks.Count == 0) { body.Children.Add(Txt("Локальные диски не найдены.", 12, false)); ShowPanel("Накопитель", body, 380, null); return; }
            int pick = disks.FindIndex(x => x.Item1 == s.Disk); if (storeSel <= 0 && pick >= 0) storeSel = pick; if (storeSel >= disks.Count) storeSel = 0;
            var segHost = new StackPanel(); var host = new StackPanel(); body.Children.Add(segHost); body.Children.Add(host);
            Action render = null;
            render = () =>
            {
                segHost.Children.Clear(); host.Children.Clear();
                if (disks.Count > 1) { segHost.Children.Add(Seg(disks.Select(x => x.Item1).ToArray(), storeSel, k => { storeSel = k; render(); }, true)); segHost.Margin = new Thickness(0, 0, 0, 12); }
                var dk = disks[storeSel]; double used = dk.Item4 - dk.Item3, frac = dk.Item4 > 0 ? used / dk.Item4 : 0;
                var nm = Txt((dk.Item2 != "" ? dk.Item2 : "Локальный диск") + " (" + dk.Item1 + ")", 12, false); nm.Opacity = 0.7; host.Children.Add(nm);
                var big = Txt(Gb(dk.Item3), 32, true); big.FontFamily = Display;
                var fr = Txt("свободно", 13, false); fr.Opacity = 0.7; fr.Margin = new Thickness(8, 0, 0, 6); fr.VerticalAlignment = VerticalAlignment.Bottom;
                host.Children.Add(HS(big, fr));
                var fb = new FillBar(348, 8); fb.Set(frac); fb.Margin = new Thickness(0, 8, 0, 8); host.Children.Add(fb);
                TextBlock tmp; host.Children.Add(KV("Занято", Gb(used) + " · " + Math.Round(frac * 100) + "%", out tmp)); host.Children.Add(KV("Всего", Gb(dk.Item4), out tmp));
                try { var di = new DriveInfo(dk.Item1); host.Children.Add(KV("Файловая система", di.DriveFormat, out tmp)); } catch { }
                var letter = dk.Item1;
                host.Children.Add(CardActions(Btn("Открыть", () => Launch(letter + "\\"), true), Btn("Очистка диска", () => Launch("cleanmgr.exe", "/d " + letter.TrimEnd(':'))), Btn("Анализ места", () => Launch("ms-settings:storagesense"))));
            };
            render();
            ShowPanel("Накопитель", body, 380, null);
        }

        // ---------- температура: значение и статус, история за 10 минут, датчики, нагрузка отдельно ----------
        void ShowTemp()
        {
            var s = live; var body = new StackPanel();
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel(); var right = new StackPanel(); Grid.SetColumn(right, 2); grid.Children.Add(left); grid.Children.Add(right); body.Children.Add(grid);
            var src = Txt("Датчик платы (ACPI)", 12, false); src.Opacity = 0.7; left.Children.Add(src);
            var big = Txt("", 36, true); big.FontFamily = Display; var stat = Txt("", 12, true); stat.Margin = new Thickness(10, 0, 0, 8); stat.VerticalAlignment = VerticalAlignment.Bottom;
            left.Children.Add(HS(big, stat));
            var note = Txt("", 11.5, false); note.Opacity = 0.7; note.TextWrapping = TextWrapping.Wrap; left.Children.Add(note);
            var chart = new TimeChart(300, 110, 0, 100); chart.Margin = new Thickness(0, 14, 0, 2); left.Children.Add(chart);
            var axis = new DockPanel { Opacity = 0.6 }; var a2 = Txt("сейчас", 10, false); DockPanel.SetDock(a2, Dock.Right); axis.Children.Add(a2); axis.Children.Add(Txt("10 мин назад", 10, false)); left.Children.Add(axis);
            var sc = Txt("", 10, false); sc.Opacity = 0.6; sc.TextWrapping = TextWrapping.Wrap; left.Children.Add(sc);
            right.Children.Add(CardHead("Датчики")); var sensors = new StackPanel(); right.Children.Add(sensors);
            right.Children.Add(CardHead("Нагрузка")); TextBlock cpuV, ramV; FillBar cpuB = new FillBar(240, 4), ramB = new FillBar(240, 4);
            right.Children.Add(KV("Процессор", "", out cpuV)); cpuB.Margin = new Thickness(0, 4, 0, 8); right.Children.Add(cpuB);
            right.Children.Add(KV("Память", "", out ramV)); ramB.Margin = new Thickness(0, 4, 0, 8); right.Children.Add(ramB);
            string zkey = null;
            Action tick = () =>
            {
                bool none = double.IsNaN(s.Temp), hot = s.Temp >= 80, warm = s.Temp >= 65;
                big.Text = none ? "—" : Math.Round(s.Temp) + " °C";
                stat.Text = none ? (s.TempNote ?? "нет данных") : hot ? "● горячо" : warm ? "● тепло" : "● норма";
                stat.Foreground = none ? Frozen(CardFg, 0xA0) : new SolidColorBrush(hot ? Color.FromRgb(0xF0, 0x50, 0x50) : warm ? Color.FromRgb(0xF0, 0xA0, 0x30) : Color.FromRgb(0x3C, 0xB8, 0x6E));
                note.Text = none ? "Windows не сообщила температуру." : hot ? "Высокая — проверьте нагрузку и охлаждение." : warm ? "Повышенная, под нагрузкой." : "Обычная рабочая температура.";
                chart.Set(s.TempHist, s.TempTimes, TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(45));
                // подпись — по накопленным данным, а не обещанные 10 минут
                sc.Text = s.TempTimes.Count == 0 ? "Измерений ещё нет: первое — через 15 с после запуска · шкала 0–100 °C" : "История за " + Ago(DateTime.Now - s.TempTimes[0]) + " (" + s.TempTimes.Count + " изм., каждые 15 с) · шкала 0–100 °C · разрывы — пропуски";
                var zs = s.TempZones ?? new List<Tuple<string, double>>(); var key = string.Join("|", zs.Select(z => z.Item1 + Math.Round(z.Item2)));
                if (key != zkey)
                {
                    zkey = key; sensors.Children.Clear();
                    if (zs.Count == 0) sensors.Children.Add(Txt("Датчиков не найдено", 12, false));
                    foreach (var z in zs) { TextBlock zv; sensors.Children.Add(KV(z.Item1, Math.Round(z.Item2) + "°", out zv)); var fb = new FillBar(240, 4); fb.Set((z.Item2 - 20) / 80); fb.Margin = new Thickness(0, 4, 0, 8); sensors.Children.Add(fb); }
                }
                cpuV.Text = double.IsNaN(s.Cpu) ? "—" : Math.Round(s.Cpu) + "%"; cpuB.Set(s.Cpu / 100);
                ramV.Text = (double.IsNaN(s.Ram) ? "—" : Math.Round(s.Ram) + "%") + " · " + Gb(s.RamUsed) + " из " + Gb(s.RamTotal); ramB.Set(s.Ram / 100);
            };
            tick();
            ShowPanel("Температура", body, 600, null, tick);
        }

        void ShowOverflow()
        {
            var body = new StackPanel();
            Func<IEnumerable<FrameworkElement>, WrapPanel> grid = els =>
            {
                var wp = new WrapPanel();
                foreach (var el in els) { Detach(el); wp.Children.Add(new Border { Margin = new Thickness(0, 0, 6, 8), Child = el }); } // обёртка с отступом: свой Margin плитки не меняем
                return wp;
            };
            if (overflowApps.Count > 0)
            {
                body.Children.Add(CardHead("Программы"));
                body.Children.Add(grid(overflowApps.Where(appEls.ContainsKey).Select(t => appEls[t]).ToList()));
            }
            if (overflowIds.Count > 0)
            {
                body.Children.Add(CardHead("Модули"));
                var wp = new WrapPanel();
                foreach (var id0 in overflowIds.Where(widgetEls.ContainsKey).ToList())
                {
                    var id = id0; var el = widgetEls[id]; Detach(el);
                    var holder = new Border { Margin = new Thickness(0, 0, 6, 8), Child = el, Background = Brushes.Transparent };
                    // модуль можно утащить на панель: между модулями, на середину другого модуля (вместо него) или на панель другого монитора
                    Point? down = null;
                    holder.PreviewMouseLeftButtonDown += (o, e) => down = e.GetPosition(el);
                    holder.PreviewMouseLeftButtonUp += (o, e) => down = null;
                    holder.PreviewMouseMove += (o, e) =>
                    {
                        if (down == null || e.LeftButton != MouseButtonState.Pressed || Locked) return;
                        var p = e.GetPosition(el); if (Math.Abs(p.X - down.Value.X) + Math.Abs(p.Y - down.Value.Y) < 8) return;
                        var grab = down.Value; down = null; e.Handled = true;
                        ShowGhost(el, grab); el.Opacity = 0.35; dragging = true;
                        var data = new DataObject("FlowBarWidget", id); data.SetData("FlowBarFrom", scr.DeviceName);
                        GiveFeedbackEventHandler fb = (o2, e2) => MoveGhost(); holder.GiveFeedback += fb;
                        DragDropEffects res = DragDropEffects.None;
                        try { res = DragDrop.DoDragDrop(holder, data, DragDropEffects.Copy | DragDropEffects.Move); }
                        finally { holder.GiveFeedback -= fb; dragging = false; CloseGhost(); el.Opacity = 1; }
                        if (res != DragDropEffects.None && panel != null) panel.Close();
                    };
                    wp.Children.Add(holder);
                }
                body.Children.Add(wp);
            }
            body.Children.Add(CardActions(Btn("Каталог виджетов", () => ShowCatalog(this), true), Btn("Настройки панели", () => ShowSettings("bar"))));
            ShowPanel("Ещё", body, 400, null);
            if (panel != null && panelTitle == "Ещё") panelBuild = ShowOverflow;
        }

        // ---------- свой трей: значки области уведомлений программ (FlowBar.Tray.cs) ----------
        void ShowTrayIcons()
        {
            // компактная сетка с ровными ячейками; подсказка — во всплывающих подписях значков
            var body = new StackPanel(); var wp = new System.Windows.Controls.Primitives.UniformGrid { Columns = 6, HorizontalAlignment = HorizontalAlignment.Left };
            var empty = CardStatus("Значков пока нет");
            var missHost = new StackPanel();
            body.Children.Add(wp); body.Children.Add(missHost); body.Children.Add(empty);
            int shown = -1;
            Action fill = () =>
            {
                if (shown == Tray.Version) return; shown = Tray.Version;
                wp.Children.Clear(); var list = Tray.Visible();
                empty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                foreach (var t0 in list)
                {
                    var t = t0; var exe = ExePath(Tray.Pid(t)); string tip = t.Tip != "" ? t.Tip : AppName(exe);
                    var img = new Image { Source = t.Image ?? (exe != null ? ShellIcon(exe, IconPxFor(20, MonScale)) : null), Width = 20, Height = 20, Stretch = Stretch.Uniform };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    var b = Act(new Border { Width = 44, Height = 44, Child = img }, tip, () => { stickyPid = Tray.Pid(t); Tray.Click(t, false, false); });
                    b.ToolTip = tip + "\nКлик и двойной клик — как в трее Windows, правая кнопка — меню программы"; b.CornerRadius = new CornerRadius(10); b.Margin = new Thickness(2);
                    b.MouseEnter += (o, e) => Fade(b, Color.FromArgb(0x1C, CardFg.R, CardFg.G, CardFg.B), 100); b.MouseLeave += (o, e) => Fade(b, Colors.Transparent, 140);
                    b.PreviewMouseLeftButtonDown += (o, e) => { if (e.ClickCount == 2) { stickyPid = Tray.Pid(t); Tray.Click(t, false, true); e.Handled = true; } };
                    // меню программы: карточку не закрываем сами — пока она активна, программе можно выйти вперёд; карточка закроется, когда меню заберёт фокус
                    b.MouseRightButtonUp += (o, e) => { e.Handled = true; stickyPid = Tray.Pid(t); Tray.Click(t, true, false); };
                    wp.Children.Add(b);
                }
                // запущенные программы, чей значок FlowBar не получил: открыть окно программы (меню трея у них недоступно)
                missHost.Children.Clear();
                var miss = Tray.MissedApps(list.Select(x => ExePath(Tray.Pid(x))));
                if (miss.Count > 0)
                {
                    var mg = new System.Windows.Controls.Primitives.UniformGrid { Columns = 6, HorizontalAlignment = HorizontalAlignment.Left };
                    foreach (var m0 in miss)
                    {
                        var m = m0; string name = m.Name != "" ? m.Name : AppName(m.Exe);
                        var img = new Image { Source = m.Image ?? ShellIcon(m.Exe, IconPxFor(20, MonScale)), Width = 20, Height = 20, Stretch = Stretch.Uniform, Opacity = 0.85 };
                        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                        var b = Act(new Border { Width = 44, Height = 44, Child = img }, name, () => OpenApp(m.Exe));
                        b.ToolTip = name + "\nКлик — открыть программу. Меню трея недоступно: значок был добавлен до запуска FlowBar"; b.CornerRadius = new CornerRadius(10); b.Margin = new Thickness(2);
                        b.MouseEnter += (o, e) => Fade(b, Color.FromArgb(0x1C, CardFg.R, CardFg.G, CardFg.B), 100); b.MouseLeave += (o, e) => Fade(b, Colors.Transparent, 140);
                        mg.Children.Add(b);
                    }
                    missHost.Children.Add(mg);
                }
                empty.Visibility = list.Count == 0 && miss.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            fill();
            ShowPanel("Значки трея", body, 320, null, fill);
        }

        // окно запущенной программы — вперёд; спрятана в трей — повторный запуск (программы с одним экземпляром показывают своё окно)
        static void OpenApp(string exe)
        {
            try
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(exe);
                var w = Process.GetProcessesByName(name).Select(p => p.MainWindowHandle).FirstOrDefault(h => h != IntPtr.Zero);
                if (w != IntPtr.Zero) ActivateWin(w); else Start(exe, null, System.IO.Path.GetDirectoryName(exe));
            }
            catch (Exception ex) { Log(ex); }
        }

        // ---------- группы программ: значок из миниатюр, сетка с названиями ----------
        static Dictionary<string, List<string>> appGroups = new Dictionary<string, List<string>>();
        static void LoadGroups()
        {
            appGroups.Clear();
            foreach (var l in Lines("groups.txt")) { var p = l.Split('\t'); if (p[0] != "") appGroups[p[0]] = p.Skip(1).Where(x => x != "").ToList(); }
        }
        static void SaveGroups() { SafeFile.WriteLines(F("groups.txt"), appGroups.Select(kv => kv.Key + "\t" + string.Join("\t", kv.Value))); }
        static string GroupsKey() { return string.Join("|", appGroups.Select(kv => kv.Key + ":" + string.Join(",", kv.Value))); }
        static string NewGroup(string path, string second = null)
        {
            int n = 1; while (appGroups.ContainsKey("Группа " + n)) n++;
            var name = "Группа " + n; appGroups[name] = new List<string> { path };
            int i = pinned.IndexOf(path); if (i >= 0) pinned[i] = "group:" + name; else pinned.Add("group:" + name);
            if (appOrder.Contains("p|" + path)) Retag("p|" + path, "p|group:" + name); else { int at = appOrder.IndexOf("r|" + Key(Target(path))); if (at >= 0) appOrder[at] = "p|group:" + name; }
            if (second != null) AttachGroupMember(second, name);
            SaveGroups(); SavePinned();
            return name;
        }
        static void AttachGroupMember(string path, string name)
        {
            if (!appGroups[name].Contains(path)) appGroups[name].Add(path);
            pinned.Remove(path); appOrder.Remove("p|" + path);
        }
        static void AddToGroup(string path, string name)
        {
            AttachGroupMember(path, name);
            SaveGroups(); SavePinned();
        }
        static void Ungroup(string name)
        {
            List<string> ms; if (!appGroups.TryGetValue(name, out ms)) return;
            int i = pinned.IndexOf("group:" + name); if (i < 0) i = pinned.Count; else pinned.RemoveAt(i);
            pinned.InsertRange(Math.Min(i, pinned.Count), ms.Where(x => !pinned.Contains(x)));
            int oi = appOrder.IndexOf("p|group:" + name);
            if (oi >= 0) { appOrder.RemoveAt(oi); appOrder.InsertRange(oi, ms.Select(x => "p|" + x).Where(x => !appOrder.Contains(x))); }
            appGroups.Remove(name); SaveGroups(); SavePinned();
        }
        void GroupMenuItems(ContextMenu m, string path)
        {
            var gm = new MenuItem { Header = "Добавить в группу" };
            foreach (var g in appGroups.Keys.ToList()) { var gn = g; gm.Items.Add(Item(gn, () => AddToGroup(path, gn))); }
            if (appGroups.Count > 0) gm.Items.Add(new Separator());
            gm.Items.Add(Item("Новая группа", () => NewGroup(path)));
            m.Items.Add(gm);
        }
        Border GroupTile(string name, Dictionary<string, List<Win>> wins, IntPtr fg)
        {
            var members = appGroups.ContainsKey(name) ? appGroups[name] : new List<string>();
            var holder = new Grid(); holder.Children.Add(GroupIcon(members));
            int running = members.Count(m => wins.ContainsKey(Key(Target(m))));
            bool active = members.Any(m => { List<Win> ws; return wins.TryGetValue(Key(Target(m)), out ws) && ws.Any(w => w.H == fg); });
            if (running > 0) holder.Children.Add(Indicator(active, running > 1));
            var t = Tile(holder, "Группа «" + name + "»: " + members.Count + " прогр.", () => ShowGroup(name), m =>
            {
                m.Items.Add(Item("Открыть группу", () => ShowGroup(name)));
                m.Items.Add(Item("Разгруппировать", () => Ungroup(name)));
            });
            if (active) { t.Base = Fa(0x1E); t.Background = new SolidColorBrush(t.Base); }
            return t;
        }

        FrameworkElement GroupIcon(IEnumerable<string> members)
        {
            var grid = new UniformGrid { Rows = 2, Columns = 2, Width = IconPx, Height = IconPx };
            foreach (var member in members.Take(4))
            {
                var icon = IconFor(member) ?? IconFor(Target(member));
                grid.Children.Add(icon != null ? (UIElement)new Image { Source = icon, Margin = new Thickness(1) } : G("\uE737", IconPx * 0.3));
            }
            return new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(IconPx / 4), Background = new SolidColorBrush(Fa(0x1A)), Child = grid,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        void ShowGroup(string name)
        {
            List<string> members; if (!appGroups.TryGetValue(name, out members)) return;
            var wins = GetWindows().GroupBy(w => Key(w.Path)).ToDictionary(g => g.Key, g => g.ToList());
            var grid = new WrapPanel();
            foreach (var m in members.ToList())
            {
                var path = m; List<Win> ws; if (!wins.TryGetValue(Key(Target(path)), out ws)) ws = new List<Win>();
                var ic = IconFor(path) ?? IconFor(Target(path));
                var nameT = Txt(PinName(path), 11, false); nameT.TextWrapping = TextWrapping.Wrap; nameT.TextAlignment = TextAlignment.Center; nameT.MaxHeight = 32; nameT.HorizontalAlignment = HorizontalAlignment.Center;
                var iconEl = ic != null ? (UIElement)new Image { Source = ic, Width = 32, Height = 32 } : G("", 24);
                var dot = new Ellipse { Width = 5, Height = 5, Fill = Accent, Margin = new Thickness(0, 3, 0, 0), Visibility = ws.Count > 0 ? Visibility.Visible : Visibility.Hidden };
                var cellBody = VS(iconEl, dot, Mg(nameT, 0, 2)); ((FrameworkElement)iconEl).HorizontalAlignment = HorizontalAlignment.Center;
                var cell = new Press { Width = 104, Padding = new Thickness(6, 10, 6, 8), CornerRadius = new CornerRadius(10), Child = cellBody, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = PinName(path) };
                System.Windows.Automation.AutomationProperties.SetName(cell, PinName(path));
                cell.Invoke = () => { if (panel != null) panel.Close(); Activate(path, ws); };
                cell.MouseEnter += (s, e) => cell.Background = Frozen(CardFg, 0x14); cell.MouseLeave += (s, e) => cell.Background = Brushes.Transparent;
                cell.MouseLeftButtonUp += (s, e) => { if (panel != null) panel.Close(); Activate(path, ws); };
                cell.MouseRightButtonUp += (s, e) =>
                {
                    e.Handled = true; var cm = new ContextMenu();
                    cm.Items.Add(Item("Убрать из группы", () => { members.Remove(path); if (!pinned.Contains(path)) pinned.Add(path); SaveGroups(); SavePinned(); if (panel != null) panel.Close(); }));
                    cm.PlacementTarget = cell; cm.IsOpen = true;
                };
                grid.Children.Add(cell);
            }
            var body = new StackPanel(); body.Children.Add(grid);
            if (Motion)
                grid.Loaded += (s, e) =>
                {
                    for (int i = 0; i < grid.Children.Count; i++)
                    {
                        var animation = SettingsMotion(0, 1, 145);
                        animation.BeginTime = TimeSpan.FromMilliseconds(Math.Min(i, 5) * 20);
                        grid.Children[i].BeginAnimation(OpacityProperty, animation);
                    }
                };
            body.Children.Add(CardHead("Название группы"));
            var nameBox = new TextBox { Text = name, ToolTip = "Новое название — Enter" };
            nameBox.KeyDown += (s, e) =>
            {
                var nn = nameBox.Text.Trim();
                if (e.Key != System.Windows.Input.Key.Enter || nn == "" || nn == name || appGroups.ContainsKey(nn)) return;
                appGroups[nn] = members; appGroups.Remove(name);
                int i = pinned.IndexOf("group:" + name); if (i >= 0) pinned[i] = "group:" + nn; Retag("p|group:" + name, "p|group:" + nn);
                SaveGroups(); SavePinned(); if (panel != null) panel.Close();
            };
            body.Children.Add(nameBox);
            body.Children.Add(CardActions(Btn("Разгруппировать", () => { if (panel != null) panel.Close(); Ungroup(name); })));
            ShowPanel(name, body, 340, null);
        }
        // отметка под значком: одно окно — одна черта; несколько окон — две равные черты рядом; hollow — окна на другом мониторе (полые)
        FrameworkElement Indicator(bool active, bool many, bool hollow = false)
        {
            Func<Rectangle> bar = () =>
            {
                var r = new Rectangle { Width = active ? Math.Round(TileH * (many ? 0.17 : 0.36)) : (hollow ? 8 : 6), Height = hollow ? 4 : 3, RadiusX = hollow ? 2 : 1.5, RadiusY = hollow ? 2 : 1.5 };
                if (hollow) { r.StrokeThickness = 1; FgBind(r, Shape.StrokeProperty); r.Opacity = 0.6; }
                else if (active) r.Fill = Accent;
                else { FgBind(r, Shape.FillProperty); r.Opacity = 0.5; } // фоновое приложение — заметно скромнее
                return r;
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1), IsHitTestVisible = false };
            sp.Children.Add(bar());
            if (many) { var b2 = bar(); b2.Margin = new Thickness(3, 0, 0, 0); sp.Children.Add(b2); }
            return sp;
        }

        // ---------- окна FlowBar: каталог и настройки ----------
        static Window AppWindow(string title, double w, double h)
        {
            var win = new Window
            {
                Title = "FlowBar — " + title, Width = w, Height = h, MinWidth = 620, MinHeight = 440, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Frozen(CardBg, 255), Foreground = Frozen(CardFg, 255), FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), ShowInTaskbar = true,
                UseLayoutRounding = true, SnapsToDevicePixels = true
            };
            System.Windows.Documents.Typography.SetNumeralAlignment(win, FontNumeralAlignment.Tabular); UseCardScroll(win); ShowOnBar(win);
            win.SourceInitialized += (s, e) => { var hw = new WindowInteropHelper(win).Handle; int dark = Sty.Lum(CardBg) < 128 ? 1 : 0; N.DwmSetWindowAttribute(hw, 20, ref dark, 4); };
            win.PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape && !(Keyboard.FocusedElement is TextBox && ((TextBox)Keyboard.FocusedElement).Text != "")) { e.Handled = true; win.Close(); } };
            return win;
        }
        static Grid SearchBox(string hint, Action<string> changed)
        {
            var g = new Grid();
            var tb = new TextBox { ToolTip = hint, Padding = new Thickness(30, 7, 10, 7), VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(tb, hint);
            var ph = Txt(hint, 12, false); ph.Opacity = 0.5; ph.IsHitTestVisible = false; ph.Margin = new Thickness(31, 0, 0, 0); ph.VerticalAlignment = VerticalAlignment.Center;
            var ic = G("", 12); ic.HorizontalAlignment = HorizontalAlignment.Left; ic.Margin = new Thickness(10, 0, 0, 0); ic.Opacity = 0.6; ic.IsHitTestVisible = false;
            tb.TextChanged += (s, e) => { ph.Visibility = tb.Text == "" ? Visibility.Visible : Visibility.Collapsed; changed(tb.Text.Trim()); };
            g.Children.Add(tb); g.Children.Add(ph); g.Children.Add(ic);
            return g;
        }
        static Press NavItem(string glyph, string text, bool sel, Action a, string badge = null, bool dim = false)
        {
            var dp = new DockPanel();
            var gl = G(glyph, 14); gl.Margin = new Thickness(0, 0, 12, 0); DockPanel.SetDock(gl, Dock.Left); dp.Children.Add(gl);
            if (badge != null) { var bt = Txt(badge, 10, false); bt.Opacity = 0.55; DockPanel.SetDock(bt, Dock.Right); dp.Children.Add(bt); }
            dp.Children.Add(Txt(text, 12.5, sel));
            var b = new Press { Child = dp, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 1, 0, 1), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Invoke = a, Opacity = dim ? 0.5 : 1 };
            var ac = AccentColor; b.Base = sel ? Color.FromArgb(0x30, ac.R, ac.G, ac.B) : Colors.Transparent; b.Background = new SolidColorBrush(b.Base);
            if (sel) { var bar = new Rectangle { Width = 3, Height = 16, RadiusX = 1.5, RadiusY = 1.5, Fill = Accent, Margin = new Thickness(-12, 0, 9, 0) }; DockPanel.SetDock(bar, Dock.Left); dp.Children.Insert(0, bar); }
            System.Windows.Automation.AutomationProperties.SetName(b, text);
            b.MouseEnter += (s, e) => { if (!sel) b.Background = Frozen(CardFg, 0x12); }; b.MouseLeave += (s, e) => b.Background = new SolidColorBrush(b.Base);
            b.MouseLeftButtonUp += (s, e) => a();
            return b;
        }

        static readonly string[][] Categories = {
            new[] { "all", "Все", "" }, new[] { "apps", "Приложения и файлы", "" }, new[] { "sys", "Система", "" },
            new[] { "media", "Музыка и звук", "" }, new[] { "prod", "Продуктивность", "" }, new[] { "clock", "Часы", "" },
            new[] { "weather", "Погода", "" }, new[] { "ai", "ИИ", "" }, new[] { "mail", "Почта", "" },
            new[] { "dev", "Разработка", "" }, new[] { "analytics", "Аналитика", "" }, new[] { "social", "Социальные сети", "" },
            new[] { "finance", "Финансы", "" } };
        static readonly string[] ClockDef = { "clock", "Часы", "Время и дата; всегда справа на панели", "", "clock" };

        static Window catalog; static Bar catTarget; static Action catalogRender;
        static readonly Dictionary<string, int> catState = new Dictionary<string, int>();
        static readonly string[] StateNames = { "Данные", "Пусто", "Нет связи", "Внимание" };
        // примеры состояний для каталога: 0 — обычные данные, 1 — нет данных / загрузка, 2 — нет связи, устарело, 3 — предупреждение
        static Snap DemoState(int st)
        {
            if (st == 1) return new Snap { Lang = "RU", WxDesc = "загрузка…" };
            var s = Demo();
            if (st == 2) { s.WxStale = true; s.WxAge = "2 ч назад"; s.CxStale = true; s.CxSeen = "вчера"; s.HasMedia = false; s.MediaError = "плеер не отвечает"; s.NetLevel = 1; s.ClPctStale = true; s.ClPctSeen = "вчера"; s.Rx = s.Tx = 0; for (int i = 0; i < s.NetHist.Count; i++) s.NetHist[i] = 0; }
            if (st == 3)
            {
                s.Cpu = 94; s.Ram = 91; s.Temp = 88; s.Cx5 = 93; s.ClP5 = 91; s.ClPW = 76; s.CxW = 78; s.DiskFree = 9; s.Bat = 0.07; s.Charging = false; s.BatSec = 1500; s.PomoLeft = new TimeSpan(0, 0, 40);
                s.Next = new CalEvent { Title = "Планёрка с командой", Start = DateTime.Now.AddMinutes(4), End = DateTime.Now.AddMinutes(34) };
            }
            return s;
        }
        // Каталог — плотная витрина: слева поиск и категории, в центре сетка предпросмотров, справа подробности выбранного модуля.
        // Тестовые состояния и техническая ширина — только в «Режиме проверки»; наборы раскладок — в настройках «Панель».
        static string catSel; static bool catDev;
        void ShowCatalog(Bar target)
        {
            catTarget = target ?? this;
            if (catalog != null) { catalog.Activate(); if (catalogRender != null) catalogRender(); return; }
            var w = AppWindow("Каталог виджетов", 1180, 700);
            var dock = new DockPanel();
            string query = "", cat = "all";
            Action render = null;
            var all = WidgetDefs.Concat(new[] { ClockDef }).ToList();
            // шапка: заголовок, честное число модулей и сколько на панели, режим проверки
            var head = new DockPanel { Margin = new Thickness(18, 16, 22, 10) };
            var devBox = CheckRow("Режим проверки", null, catDev, on => { catDev = on; if (render != null) render(); }); devBox.VerticalAlignment = VerticalAlignment.Center; devBox.ToolTip = "Тестовые состояния модулей и примерная ширина — для проверки оформления";
            DockPanel.SetDock(devBox, Dock.Right); head.Children.Add(devBox);
            var ttl = Txt("Все виджеты", 20, true); ttl.FontFamily = Display; ttl.VerticalAlignment = VerticalAlignment.Center;
            var count = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(10, 0, 0, 0), Background = Frozen(CardFg, 0x14), VerticalAlignment = VerticalAlignment.Center };
            var countT = Txt("", 11, true); count.Child = countT;
            var hint = Txt("Перетащите модуль на панель или нажмите «+». Пока каталог открыт, модули на панели можно двигать и убирать.", 11, false); hint.Opacity = 0.6; hint.Margin = new Thickness(0, 4, 0, 0); hint.TextWrapping = TextWrapping.Wrap;
            head.Children.Add(VS(HS(ttl, count), hint));
            DockPanel.SetDock(head, Dock.Top); dock.Children.Add(head);
            // слева: поиск и категории
            var leftCol = new StackPanel { Width = 210, Margin = new Thickness(14, 0, 0, 14) };
            var search = SearchBox("Поиск виджетов", q => { query = q; if (render != null) render(); }); search.Margin = new Thickness(0, 0, 0, 10);
            var cats = new StackPanel(); leftCol.Children.Add(search); leftCol.Children.Add(cats);
            var leftScroll = new ScrollViewer { Content = leftCol, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Width = 228 }; DockPanel.SetDock(leftScroll, Dock.Left); dock.Children.Add(leftScroll);
            // справа: подробности выбранного
            var detail = new StackPanel { Width = 262, Margin = new Thickness(6, 0, 18, 14) };
            var detailScroll = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; DockPanel.SetDock(detailScroll, Dock.Right); dock.Children.Add(detailScroll);
            var grid = new WrapPanel { Margin = new Thickness(6, 0, 6, 14) };
            dock.Children.Add(new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            render = () =>
            {
                cats.Children.Clear(); grid.Children.Clear(); detail.Children.Clear();
                var tb = catTarget ?? this;
                countT.Text = all.Count + " модулей · на панели " + all.Count(d => d[0] == "clock" || tb.Layout().Any(x => x.Key == d[0] && x.Value));
                foreach (var c in Categories)
                {
                    var key = c[0]; int n = key == "all" ? all.Count : all.Count(d => d[4] == key);
                    cats.Children.Add(NavItem(c[2], c[1], cat == key, () => { cat = key; render(); }, n == 0 ? "скоро" : n.ToString(), n == 0));
                }
                var list = all.Where(d => (cat == "all" || d[4] == cat) && (query == "" || (d[1] + " " + d[2]).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)).ToList();
                if (catSel == null || !all.Any(d => d[0] == catSel)) catSel = list.Count > 0 ? list[0][0] : null;
                foreach (var d in list) grid.Children.Add(CatalogCard(d, render));
                if (list.Count == 0)
                {
                    var none = Txt(query != "" ? "Ничего не найдено по запросу «" + query + "»" : "В этой категории виджетов пока нет — они появятся вместе с подключениями.", 13, false);
                    none.Opacity = 0.6; none.Margin = new Thickness(8, 20, 0, 0); none.TextWrapping = TextWrapping.Wrap; grid.Children.Add(none);
                }
                var sd = all.FirstOrDefault(d => d[0] == catSel);
                if (sd != null) CatalogDetail(sd, detail, render);
                StyleInputs(w, CardFg);
            };
            w.Content = dock; render(); catalogRender = render; // стили полей применяются к дереву окна — сначала содержимое
            editMode = true; All(b => b.RenderWidgets());
            w.Closed += (s, e) => { catalog = null; catalogRender = null; editMode = false; All(b => b.RenderWidgets()); };
            catalog = w; w.Show(); w.Activate();
        }
        // состояние модуля для витрины: на панели / подключить / недоступно / можно добавить
        bool CatShown(string id) { var tb = catTarget ?? this; return id == "clock" || tb.Layout().Any(x => x.Key == id && x.Value); }
        static bool CatAvail(string id) { return id == "claude" ? Directory.Exists(ClaudeUsage.Root) : id == "codex" ? Directory.Exists(CodexLimits.Root) : id == "bat" ? live.HasBat || DemoMode : true; }
        // предпросмотр модуля на капсуле текущей темы
        FrameworkElement CatPreview(string id, double h, double maxH, double maxW)
        {
            int st = 0; if (catDev) catState.TryGetValue(id, out st);
            var tmp = new List<Action>();
            var el = MakeWidget(id, DemoState(st), false, tmp) as FrameworkElement;
            foreach (var a in tmp) try { a(); } catch { }
            var host = new Grid { Height = h };
            host.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = new LinearGradientBrush(Color.FromRgb(0x1A, 0x22, 0x33), Color.FromRgb(0x2E, 0x1E, 0x30), 20) });
            if (el != null)
            {
                el.IsHitTestVisible = false; var ss = Cur;
                var cap = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(Color.FromArgb((byte)Math.Max(0x70, Math.Round(ss.Bg.A * Opac)), ss.Bg.R, ss.Bg.G, ss.Bg.B)), BorderThickness = new Thickness(1), BorderBrush = Frozen(ss.Fg, 0x26),
                    Child = new Viewbox { Child = el, StretchDirection = StretchDirection.DownOnly, MaxHeight = maxH, MaxWidth = maxW } };
                System.Windows.Documents.TextElement.SetForeground(cap, new SolidColorBrush(ss.Fg));
                host.Children.Add(cap);
            }
            return host;
        }
        Border CatalogCard(string[] d, Action rerender)
        {
            var id = d[0]; var tb = catTarget ?? this;
            bool fixedW = id == "clock", shown = CatShown(id), avail = CatAvail(id), sel = id == catSel;
            var body = new StackPanel();
            body.Children.Add(CatPreview(id, 60, 34, 122));
            var line = new DockPanel { Margin = new Thickness(2, 8, 0, 0) };
            // быстрое действие: «+» — добавить, «✓» — на панели (нажатие убирает), «Подключить» — нет источника
            FrameworkElement quick;
            if (fixedW) quick = Txt("всегда", 10, false);
            else if (!avail) { var q = Txt("Подключить", 10, true); q.Foreground = Accent; quick = Act(q, "Как подключить " + d[1], () => Primary.ShowSettings("connect")); }
            else quick = RoundBtn(shown ? "" : "", 24, !shown, () => { tb.SetVisible(id, !shown); rerender(); }, shown ? "Убрать с панели" : "Добавить на панель");
            quick.Opacity = fixedW ? 0.5 : 1; quick.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(quick, Dock.Right); line.Children.Add(quick);
            var name = Txt(d[1], 12, true); name.TextTrimming = TextTrimming.CharacterEllipsis; name.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(name);
            body.Children.Add(line);
            var stT = Txt(fixedW ? "На панели" : !avail ? "Недоступно на этом компьютере" : shown ? "Добавлен" : "", 10, false); stT.Opacity = 0.55; stT.Margin = new Thickness(2, 1, 0, 0); stT.Height = 14; body.Children.Add(stT);
            var card = new Border { Width = 146, Margin = new Thickness(4), Padding = new Thickness(8), CornerRadius = new CornerRadius(12), Background = Frozen(CardFg, sel ? (byte)0x12 : (byte)0x07), BorderThickness = new Thickness(sel ? 1.5 : 1),
                BorderBrush = sel ? Accent : Frozen(CardFg, shown && !fixedW ? (byte)0x30 : (byte)0x12), Child = body, Cursor = Cursors.Hand };
            System.Windows.Automation.AutomationProperties.SetName(card, d[1] + (shown ? ", на панели" : avail ? "" : ", недоступно"));
            // клик — подробности справа; перетаскивание — на панель
            Point? down = null;
            card.PreviewMouseLeftButtonDown += (s, e) => down = e.GetPosition(card);
            card.MouseMove += (s, e) =>
            {
                if (down == null || e.LeftButton != MouseButtonState.Pressed || fixedW || !avail) return;
                var p = e.GetPosition(card);
                if (Math.Abs(p.X - down.Value.X) + Math.Abs(p.Y - down.Value.Y) < 8) return;
                down = null; card.Opacity = 0.6;
                DragDrop.DoDragDrop(card, new DataObject("FlowBarWidget", id), DragDropEffects.Copy);
                card.Opacity = 1; rerender();
            };
            card.MouseLeftButtonUp += (s, e) => { if (down != null && catSel != id) { catSel = id; rerender(); } down = null; };
            return card;
        }
        // подробности выбранного модуля: крупный предпросмотр, описание, размер, добавление, «Оставлять видимым»
        void CatalogDetail(string[] d, StackPanel into, Action rerender)
        {
            var id = d[0]; var tb = catTarget ?? this;
            bool fixedW = id == "clock", shown = CatShown(id), avail = CatAvail(id);
            into.Children.Add(CatPreview(id, 118, 64, 236));
            var name = Txt(d[1], 15, true); name.Margin = new Thickness(2, 12, 0, 2); into.Children.Add(name);
            var desc = Txt(d[2], 11.5, false); desc.TextWrapping = TextWrapping.Wrap; desc.Opacity = 0.72; desc.Margin = new Thickness(2, 0, 0, 10); into.Children.Add(desc);
            if (Variants.Contains(id))
            {
                var cur = tb.CM("view." + id, ""); int sv = Array.IndexOf(ViewKeys, cur); if (sv < 0) sv = 0;
                into.Children.Add(Mg(Txt("Размер", 11, true), 2, 0, 0, 4));
                into.Children.Add(Seg(new[] { "Авто", "S", "M", "L" }, sv, i => { cfg[tb.MK("view." + id)] = ViewKeys[i]; tb.MarkCustom(); SaveCfg(); RebuildAll(); rerender(); }, true));
            }
            Border action;
            if (fixedW) { action = Btn("✓ Всегда на панели", () => { }); action.Opacity = 0.7; }
            else if (!avail) { action = Btn("Как подключить", () => Primary.ShowSettings("connect")); action.ToolTip = id == "claude" ? "Не найден Claude Code (~/.claude) на этом компьютере" : id == "codex" ? "Не найден Codex (~/.codex) на этом компьютере" : "Нет источника данных на этом компьютере"; }
            else if (shown) { action = Btn("Убрать с панели", () => { tb.SetVisible(id, false); rerender(); }); }
            else action = Btn("Добавить на панель", () => { tb.SetVisible(id, true); rerender(); }, true);
            action.Margin = new Thickness(0, 12, 0, 0); action.HorizontalAlignment = HorizontalAlignment.Left;
            into.Children.Add(action);
            if (!fixedW && avail) into.Children.Add(Mg(CheckRow("Оставлять видимым", null, tb.Keep(id), on => { cfg[tb.MK("keep." + id)] = on ? "1" : "0"; tb.MarkCustom(); SaveCfg(); RebuildAll(); }), 0, 8, 0, 0));
            if (id == "bat" && !live.HasBat) into.Children.Add(CardStatus("На этом компьютере нет батареи — модуль будет скрыт."));
            if (catDev)
            {
                // режим проверки: тестовые состояния и примерная ширина
                int st; catState.TryGetValue(id, out st);
                into.Children.Add(Mg(Txt("Режим проверки", 11, true), 2, 16, 0, 4));
                into.Children.Add(Seg(StateNames, st, i => { catState[id] = i; rerender(); }, true));
                var tmp = new List<Action>(); var el = MakeWidget(id, DemoState(st), false, tmp) as FrameworkElement; foreach (var a in tmp) try { a(); } catch { }
                double ew = 0; if (el != null) { el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); ew = el.DesiredSize.Width; }
                double free = tb.FreeWidth;
                into.Children.Add(CardStatus((ew > 0 ? "Ширина ≈ " + Math.Round(ew) + " DIP" : "") + (free > 0 ? " · свободно на панели ≈ " + Math.Round(free) + " DIP" : "")));
            }
        }

        // ---------- элементы настроек ----------
        static StackPanel Seg(string[] labels, int sel, Action<int> pick, bool small = false)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var box = new Border { CornerRadius = new CornerRadius(10), Background = Frozen(CardFg, 0x0E), Padding = new Thickness(3), Child = sp, HorizontalAlignment = HorizontalAlignment.Left };
            var selectedBackground = Frozen(AccentColor, 255);
            var selectedForeground = Frozen(OnAccent, 255);
            Action<int, bool> paint = (index, selected) =>
            {
                var button = (Press)sp.Children[index];
                var text = (TextBlock)button.Child;
                button.Background = selected ? selectedBackground : Brushes.Transparent;
                if (selected) text.Foreground = selectedForeground;
                else text.ClearValue(TextBlock.ForegroundProperty);
                text.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                System.Windows.Automation.AutomationProperties.SetName(button, labels[index] + (selected ? ", выбрано" : ""));
            };
            for (int i = 0; i < labels.Length; i++)
            {
                int k = i;
                var t = Txt(labels[i], small ? 11 : 12, false); t.HorizontalAlignment = HorizontalAlignment.Center;
                Action select = () =>
                {
                    pick(k);
                    if (sel == k) return;
                    if (sel >= 0 && sel < sp.Children.Count) paint(sel, false);
                    sel = k;
                    paint(sel, true);
                };
                var b = new Press { Child = t, Padding = new Thickness(small ? 9 : 14, small ? 4 : 6, small ? 9 : 14, small ? 4 : 6), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Invoke = select };
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; select(); };
                sp.Children.Add(b);
                paint(i, i == sel);
            }
            var outer = new StackPanel(); outer.Children.Add(box); return outer;
        }
        static Border SettingsRow(UIElement child)
        {
            return new Border
            {
                Child = child, Background = Frozen(CardFg, 0x09), BorderBrush = Frozen(CardFg, 0x19),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 8, 0, 0)
            };
        }
        static Border SetRow(string label, string hint, UIElement control)
        {
            var sp = new StackPanel();
            sp.Children.Add(Txt(label, 12.5, true));
            if (hint != null) { var h = Txt(hint, 11, false); h.Opacity = 0.65; h.TextWrapping = TextWrapping.Wrap; h.Margin = new Thickness(0, 2, 0, 6); sp.Children.Add(h); }
            else ((FrameworkElement)control).Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(control); return SettingsRow(sp);
        }
        static CheckBox CheckRow(string label, string hint, bool value, Action<bool> set)
        {
            var sp = VS(Txt(label, 12.5, false));
            if (hint != null) { var h = Txt(hint, 11, false); h.Opacity = 0.65; h.TextWrapping = TextWrapping.Wrap; sp.Children.Add(h); }
            var cb = new CheckBox { Content = sp, IsChecked = value, Margin = new Thickness(0, 8, 0, 4), VerticalContentAlignment = VerticalAlignment.Top };
            System.Windows.Automation.AutomationProperties.SetName(cb, label);
            cb.Checked += (s, e) => set(true); cb.Unchecked += (s, e) => set(false); // переключение мышью, клавиатурой и экранным диктором
            return cb;
        }
        static StackPanel SliderRow(double min, double max, double value, double step, Func<double, string> fmt, Action<double> set, string name)
        {
            var val = Txt(fmt(value), 12, true); val.Width = 70; val.Margin = new Thickness(12, 0, 0, 0);
            var sl = new Slider { Minimum = min, Maximum = max, Value = value, Width = 300, IsSnapToTickEnabled = true, TickFrequency = step, SmallChange = step, LargeChange = step * 2, VerticalAlignment = VerticalAlignment.Center }; sl.Style = SliderSty(); sl.AutoToolTipPlacement = System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft;
            System.Windows.Automation.AutomationProperties.SetName(sl, name);
            sl.ValueChanged += (s, e) => { val.Text = fmt(sl.Value); set(sl.Value); };
            return HS(sl, val);
        }
        static TextBlock SH(string s) { var t = Txt(s, 22, true); t.FontFamily = Display; t.Margin = new Thickness(0, 0, 0, 6); return t; }
        static TextBlock SSub(string s) { var t = Txt(s, 14, true); t.Margin = new Thickness(0, 22, 0, 2); return t; }
        static TextBlock SHint(string s) { var t = Txt(s, 11.5, false); t.Opacity = 0.7; t.TextWrapping = TextWrapping.Wrap; return t; }

        static readonly string[][] FeaturedThemes = {
            new[] { "Black", "Уголь", "Тёмная", "♨", "#FF7049" },
            new[] { "iOS Glass", "Солнце", "Светлое стекло", "☼", "#FFCF60" },
            new[] { "Liquid Glass", "Ветер", "Прозрачное стекло", "≋", "#6AF4D0" },
            new[] { "Midnight", "Сумерки", "Синяя ночь", "☾", "#B39BFF" }
        };

        static DoubleAnimation SettingsMotion(double from, double to, int duration)
        {
            return new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(duration))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
        }

        sealed class ThemeMotionArt : FrameworkElement
        {
            static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
                "Phase", typeof(double), typeof(ThemeMotionArt),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
            readonly string key;
            readonly Brush light, color;
            readonly Pen core, edge, trail;
            bool playing;

            public ThemeMotionArt(string themeKey, Color tint)
            {
                key = themeKey;
                Width = 136; Height = 132;
                HorizontalAlignment = HorizontalAlignment.Center;
                IsHitTestVisible = false;
                Visibility = Visibility.Hidden;
                light = Frozen(Color.FromRgb(
                    (byte)((tint.R + 255) / 2), (byte)((tint.G + 255) / 2), (byte)((tint.B + 255) / 2)), 255);
                color = Frozen(tint, 255);
                core = new Pen(light, 1.2); core.Freeze();
                edge = new Pen(color, 1); edge.Freeze();
                trail = new Pen(Frozen(tint, 0x52), 4); trail.Freeze();
            }

            public void SetActive(bool active, bool animate)
            {
                Visibility = active ? Visibility.Visible : Visibility.Hidden;
                bool run = active && animate;
                if (run == playing) return;
                playing = run;
                if (!run)
                {
                    BeginAnimation(PhaseProperty, null);
                    return;
                }
                int duration = key == "Black" ? 1950 : key == "iOS Glass" ? 2700 : key == "Liquid Glass" ? 2100 : 3300;
                BeginAnimation(PhaseProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(duration))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                });
            }

            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                double phase = (double)GetValue(PhaseProperty);
                double pulse = 0.5 - 0.5 * Math.Cos(phase * 2 * Math.PI);
                dc.PushOpacity(0.34 + 0.28 * pulse);
                dc.DrawRoundedRectangle(null, trail, new Rect(34, 10, 68, 112), 34, 34);
                dc.Pop();
                switch (key)
                {
                    case "Black": DrawEmbers(dc, phase); break;
                    case "iOS Glass": DrawSun(dc, phase, pulse); break;
                    case "Liquid Glass": DrawWind(dc, phase); break;
                    default: DrawTwilight(dc, phase); break;
                }
            }

            void DrawEmbers(DrawingContext dc, double phase)
            {
                // Внешние языки пламени поднимаются вдоль контура капсулы.
                for (int i = 0; i < 5; i++)
                {
                    double wave = Math.Sin((phase + i * 0.17) * 2 * Math.PI);
                    double x = 32 + i * 18;
                    var curve = new StreamGeometry();
                    using (var path = curve.Open())
                    {
                        path.BeginFigure(new Point(x, 63 - Math.Abs(2 - i) * 5), false, false);
                        path.BezierTo(new Point(x - 13, 48), new Point(x + 13 * wave, 31), new Point(x + 7 * wave, 7 + Math.Abs(2 - i) * 8), true, false);
                    }
                    dc.PushOpacity(0.3 + 0.23 * wave);
                    dc.DrawGeometry(null, trail, curve);
                    dc.DrawGeometry(null, edge, curve);
                    dc.Pop();
                }
                for (int i = 0; i < 22; i++)
                {
                    double life = (phase + i * 0.61803398875) % 1;
                    double spread = 23 + i * 7 % 25;
                    double x = 68 + (i % 2 == 0 ? -1 : 1) * (spread + 8 * life) + 3 * Math.Sin(life * 8 + i);
                    double y = 100 - life * (76 + i % 6 * 6);
                    double radius = (1 + i % 3 * 0.45) * (1 - life * 0.5);
                    dc.PushOpacity(Math.Sin(life * Math.PI) * 0.9);
                    dc.DrawEllipse(i % 3 == 0 ? light : color, null, new Point(x, y), radius, radius * 1.3);
                    dc.Pop();
                }
            }

            void DrawSun(DrawingContext dc, double phase, double pulse)
            {
                for (int i = 0; i < 32; i++)
                {
                    double angle = i * Math.PI / 16 + Math.Sin(phase * 2 * Math.PI) * 0.12;
                    double x = Math.Cos(angle), y = Math.Sin(angle);
                    double start = 1 / Math.Sqrt(x * x / (32 * 32) + y * y / (54 * 54));
                    double length = 10 + (i * 11 % 18) + 9 * pulse;
                    var near = new Point(68 + x * start, 66 + y * start);
                    var middle = new Point(68 + x * (start + length * 0.52), 66 + y * (start + length * 0.52));
                    var end = new Point(68 + x * (start + length), 66 + y * (start + length));
                    dc.PushOpacity(0.2 + 0.55 * (0.5 + 0.5 * Math.Sin(i * 2.1 + phase * 2 * Math.PI)));
                    dc.DrawLine(trail, near, middle);
                    dc.DrawLine(core, near, middle);
                    dc.DrawLine(edge, middle, end);
                    dc.Pop();
                }
            }

            void DrawWind(DrawingContext dc, double phase)
            {
                for (int i = 0; i < 4; i++)
                {
                    double progress = (phase + i * 0.25) % 1;
                    double x = -52 + 70 * progress, y = 26 + i * 23 + 4 * Math.Sin(progress * Math.PI * 2);
                    var curve = new StreamGeometry();
                    using (var path = curve.Open())
                    {
                        path.BeginFigure(new Point(x, y + 12), false, false);
                        path.BezierTo(new Point(x + 36, y - 17), new Point(x + 101, y + 25), new Point(x + 125, y - 2), true, false);
                        path.BezierTo(new Point(x + 143, y - 21), new Point(x + 113, y - 23), new Point(x + 118, y - 8), true, false);
                    }
                    dc.PushOpacity(0.88 * Math.Sin(progress * Math.PI));
                    dc.DrawGeometry(null, trail, curve);
                    dc.DrawGeometry(null, i % 2 == 0 ? core : edge, curve);
                    dc.Pop();
                }
            }

            void DrawTwilight(DrawingContext dc, double phase)
            {
                for (int i = 0; i < 17; i++)
                {
                    double wave = 0.5 - 0.5 * Math.Cos((phase + i * 0.61803398875) * Math.PI * 2);
                    double x = 68 + (i % 2 == 0 ? -1 : 1) * (30 + i * 7 % 34);
                    double y = 10 + i * 29 % 113 + 2.5 * Math.Sin(phase * 2 * Math.PI + i);
                    double size = (1.1 + i % 4) * (0.6 + 0.4 * wave);
                    dc.PushOpacity(0.1 + 0.85 * wave * wave);
                    dc.DrawEllipse(i % 3 == 0 ? light : color, null, new Point(x, y), 1.05, 1.05);
                    if (i % 3 == 0)
                    {
                        dc.DrawLine(core, new Point(x - size, y), new Point(x + size, y));
                        dc.DrawLine(core, new Point(x, y - size * 1.25), new Point(x, y + size * 1.25));
                    }
                    dc.Pop();
                }
            }
        }

        static Press ElementTheme(string[] theme, bool selected, Action pick, Action<bool> preview)
        {
            var tint = (Color)ColorConverter.ConvertFromString(theme[4]);
            var art = new Grid { Height = 132, ClipToBounds = true, IsHitTestVisible = false };
            var halo = new Grid { Width = 148, Height = 132, HorizontalAlignment = HorizontalAlignment.Center };
            var wash = new RadialGradientBrush(Frozen(tint, 0xA0).Color, Frozen(tint, 0).Color);
            halo.Children.Add(new Rectangle { Fill = wash });
            art.Children.Add(halo);
            var effect = new ThemeMotionArt(theme[0], tint);
            art.Children.Add(effect);
            var icon = Txt(theme[3], 31, false);
            icon.Foreground = Brushes.White; icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center;
            var capsule = new Border
            {
                Width = 62, Height = 106, CornerRadius = new CornerRadius(31), BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(Color.FromRgb(17, 17, 24), Frozen(tint, 0x40).Color, 115),
                Child = icon, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1)
            };
            art.Children.Add(capsule);
            var label = Txt(theme[1], 14, true); label.TextAlignment = TextAlignment.Center;
            var material = Txt(theme[2], 11, false); material.TextAlignment = TextAlignment.Center; material.Opacity = 0.72;
            var face = VS(art, label, material);
            var content = new Grid(); content.Children.Add(face);
            if (selected)
            {
                var check = Txt("✓", 13, true); check.Foreground = Frozen(CardFg, 255);
                check.HorizontalAlignment = HorizontalAlignment.Right; check.VerticalAlignment = VerticalAlignment.Top;
                check.Margin = new Thickness(0, 2, 4, 0); content.Children.Add(check);
            }
            var button = new Press
            {
                Width = 150, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(6, 4, 6, 10),
                CornerRadius = new CornerRadius(14), Background = Frozen(CardFg, selected ? (byte)0x13 : (byte)0x07),
                BorderThickness = new Thickness(1), Child = content, Cursor = Cursors.Hand, Invoke = pick, Tag = "settings-theme:" + theme[0]
            };
            bool hover = false;
            Action<bool> paint = animate =>
            {
                bool focused = button.IsKeyboardFocused;
                double opacity = hover || focused ? 0.9 : selected ? 0.32 : 0;
                var scale = (ScaleTransform)capsule.RenderTransform;
                double from = halo.Opacity, fromScale = scale.ScaleX, size = hover && Motion ? 1.05 : 1;
                halo.BeginAnimation(OpacityProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                halo.Opacity = opacity; scale.ScaleX = size; scale.ScaleY = size;
                if (animate && Motion)
                {
                    halo.BeginAnimation(OpacityProperty, SettingsMotion(from, opacity, 240));
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, SettingsMotion(fromScale, size, 240));
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, SettingsMotion(fromScale, size, 240));
                }
                capsule.BorderBrush = hover || focused ? Brushes.White : Frozen(tint, selected ? (byte)0xFF : (byte)0x66);
                button.BorderBrush = focused ? Frozen(CardFg, 255) : selected ? Frozen(tint, 0xC0) : Frozen(CardFg, 0x22);
                effect.SetActive(hover && button.IsVisible, Motion);
            };
            paint(false);
            button.MouseEnter += (s, e) => { hover = true; paint(true); preview(true); };
            button.MouseLeave += (s, e) => { hover = false; paint(true); preview(false); };
            button.IsKeyboardFocusedChanged += (s, e) => paint(true);
            button.IsVisibleChanged += (s, e) => { if (!button.IsVisible) hover = false; paint(false); };
            button.Unloaded += (s, e) => effect.SetActive(false, false);
            button.MouseLeftButtonUp += (s, e) => { e.Handled = true; pick(); };
            System.Windows.Automation.AutomationProperties.SetName(button, theme[1] + ", " + theme[2] + (selected ? ", выбрана" : ""));
            return button;
        }

        // Локальная примерка меняет только это представление. Настройки панели пишутся обработчиками выбора.
        Action<string> AddSettingsPreview(StackPanel parent)
        {
            var title = SSub("Предпросмотр панели");
            title.Margin = new Thickness(0, 0, 0, 6);
            var scene = new Border
            {
                Height = 112, CornerRadius = new CornerRadius(14), ClipToBounds = true,
                Background = new LinearGradientBrush(Color.FromRgb(66, 45, 117), Color.FromRgb(24, 82, 98), 15)
            };
            var otherScene = new Border { Height = scene.Height, CornerRadius = scene.CornerRadius, ClipToBounds = true };
            var otherTitle = SSub("После");
            otherTitle.Margin = title.Margin;
            var stages = new Grid();
            var beforeColumn = new ColumnDefinition(); var afterColumn = new ColumnDefinition();
            stages.ColumnDefinitions.Add(beforeColumn); stages.ColumnDefinitions.Add(afterColumn);
            var after = VS(otherTitle, otherScene); after.Margin = new Thickness(7, 0, 0, 0);
            var before = VS(title, scene);
            Grid.SetColumn(after, 1); stages.Children.Add(before); stages.Children.Add(after);
            parent.Children.Add(stages);
            var apply = Btn("Применить тему", () => ChangeTheme(candidateTheme), true);
            parent.Children.Add(apply);
            var restore = CardActions(Btn("Восстановить вариант", RestorePreviewAppearance, true),
                Btn("Закрыть примерку", () => { candidateAppearance = null; if (refreshPreview != null) refreshPreview(null); }));
            parent.Children.Add(restore);
            Action<string> render = key =>
            {
                var saved = key == null ? candidateAppearance : null;
                bool compare = CompareThemes || saved != null;
                after.Visibility = compare ? Visibility.Visible : Visibility.Collapsed;
                afterColumn.Width = compare ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                apply.Visibility = saved == null && compare && candidateTheme != null ? Visibility.Visible : Visibility.Collapsed;
                restore.Visibility = saved != null ? Visibility.Visible : Visibility.Collapsed;
                scene.Background = otherScene.Background = PreviewScene();
                string next = key ?? candidateTheme;
                var featured = FeaturedThemes.FirstOrDefault(item => item[0] == next);
                string name = next == null ? "" : featured == null ? ThemeName(next) : featured[1];
                title.Text = compare ? "Сейчас" : next == null ? "Предпросмотр панели" : "Примерка · " + name;
                otherTitle.Text = saved != null ? "Сохранено " + candidateAppearanceDate : next == null ? "После" : "После · " + name;
                DrawSettingsBar(scene, compare ? null : next);
                if (compare) DrawSettingsBar(otherScene, saved != null ? null : next, saved);
            };
            render(null);
            return render;
        }

        void DrawSettingsBar(Border scene, string key, Dictionary<string, string> saved = null)
        {
            var target = settingsPreviewMonitor != null && bars.Contains(settingsPreviewMonitor) ? settingsPreviewMonitor : Primary ?? this;
            var view = new Dictionary<string, string>(cfg);
            if (saved != null)
            {
                foreach (var setting in view.Keys.Where(IsAppearanceKey).ToList()) view.Remove(setting);
                foreach (var pair in saved) view[pair.Key] = pair.Value;
            }
            if (key != null) view["style"] = key;
            view["motion"] = "off";
            var previous = previewCfg;
            previewCfg = view;
            try { target.DrawPanelSample(scene, saved != null); }
            finally { previewCfg = previous; }
        }

        void DrawPanelSample(Border scene, bool saved)
        {
            bool vertical = VerticalBar, split = Sections && !vertical && !Flush, glass = GlassOn;
            var size = VerticalBar ? new Thickness(Gap, 0, Gap, 0) : new Thickness(0, Gap, 0, Gap);
            var row = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Documents.TextElement.SetForeground(row, Frozen(Cur.Fg, 255));
            var widgets = HasWidgets ? Layout().Where(item => CompactMonitor ? LayoutShows(item.Key) : item.Value)
                .Select(item => "w:" + item.Key).ToList() : new List<string>();
            var apps = appEls == null ? new List<string>() : appEls.Keys.OrderBy(t =>
            {
                int i = appOrder.IndexOf(t); return i < 0 ? int.MaxValue : i;
            }).ToList();
            var order = saved || shownTokens == null || shownTokens.Count == 0
                ? MergeSeq(C(SeqKey, "").Split('\t').ToList(), apps, widgets, C("search", "1") == "1", true, true)
                : shownTokens.Where(t => t != "__drop" && ElOf(t) != null && ElOf(t).Visibility == Visibility.Visible).ToList();
            if (!saved && overflowTile != null) order.Add("preview:overflow");
            order.Add("preview:tray");
            if (C("edgeBtns", "1") == "1") { order.Add("preview:fold"); order.Add("preview:desktop"); }
            if (split) order = order.OrderBy(PreviewSection).ToList();

            var updates = new List<Action>();
            var surfaces = new List<PanelSampleSurface>();
            StackPanel section = null;
            int lastSection = -1;
            foreach (var token in order)
            {
                var tile = PreviewTile(token, updates);
                if (tile == null) continue;
                if (split && (section == null || lastSection != PreviewSection(token)))
                {
                    lastSection = PreviewSection(token);
                    section = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    var cap = PreviewCapsule(scene, section, glass);
                    surfaces.Add(cap);
                    cap.Margin = new Thickness(row.Children.Count == 0 ? 0 : 7, 0, 0, 0);
                    row.Children.Add(cap);
                }
                tile.IsHitTestVisible = false;
                var id = token.StartsWith("w:") ? token.Substring(2) : null;
                if (id != null && !saved && !CompactMonitor)
                {
                    var grip = new Border { Child = tile, Background = Brushes.Transparent };
                    SetupPreviewWidget(grip, this, id);
                    tile = grip;
                }
                (section ?? row).Children.Add(tile);
            }
            foreach (var update in updates) try { update(); } catch (Exception ex) { Log(ex); }

            if (!split) surfaces.Add(PreviewCapsule(scene, row, glass));
            var content = split ? (FrameworkElement)row : surfaces[0];
            var scroll = new ScrollViewer
            {
                Content = content, Focusable = false,
                HorizontalScrollBarVisibility = vertical ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            scroll.HorizontalAlignment = vertical ? (LeftBar ? HorizontalAlignment.Left : HorizontalAlignment.Right) : FullWidth ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
            scroll.VerticalAlignment = vertical ? VerticalAlignment.Stretch : IsTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            scroll.Margin = size;
            scroll.ScrollChanged += (s, e) => { foreach (var surface in surfaces) surface.InvalidateVisual(); };
            if (!vertical) scroll.PreviewMouseWheel += (s, e) =>
            {
                if (scroll.ScrollableWidth <= 0) return;
                scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta / 2.0); e.Handled = true;
            };
            scene.Child = scroll;
        }

        static int PreviewSection(string token) { return token.StartsWith("preview:") ? 2 : SectionFor(token); }

        PanelSampleSurface PreviewCapsule(Border scene, FrameworkElement child, bool glass)
        {
            var capsule = new PanelSampleSurface(scene, glass ? PreviewScene(true) : null)
            {
                Child = child, Background = MaterialFill(Cur, glass), BorderBrush = MaterialEdge(Cur, glass, false),
                BorderThickness = new Thickness(1), Padding = new Thickness(SizeKey == "XL" ? 4 : 6),
                CornerRadius = new CornerRadius(Flush ? 0 : BarH / 2), Height = VerticalBar ? double.NaN : BarH,
                Width = VerticalBar ? SideWidth : double.NaN
            };
            if (VerticalBar && !FullWidth) capsule.VerticalAlignment = VerticalAlignment.Center;
            return capsule;
        }

        FrameworkElement PreviewTile(string token, List<Action> updates)
        {
            if (token == "clock" || token.StartsWith("w:"))
            {
                string id = token == "clock" ? token : token.Substring(2);
                try { return MakeWidget(id, live, false, updates) as FrameworkElement; }
                catch (Exception ex) { Log(ex); return null; }
            }
            if (IsApp(token))
            {
                string path = token.Substring(2);
                List<Win> wins = null;
                if (appWins != null) appWins.TryGetValue(token, out wins);
                if (token.StartsWith("r|") && wins != null && wins.Count != 0)
                    path = wins[0].Path;
                List<string> members;
                var cell = new Grid();
                if (!string.IsNullOrEmpty(path) && path.StartsWith("group:") && appGroups.TryGetValue(path.Substring(6), out members)) cell.Children.Add(GroupIcon(members));
                else
                {
                    var icon = string.IsNullOrEmpty(path) ? null : IconFor(path) ?? IconFor(Target(path));
                    cell.Children.Add(icon == null ? (UIElement)G("\uE737", 15 * Fs) : new Image { Source = icon, Width = IconPx, Height = IconPx });
                }
                var foreground = N.GetForegroundWindow();
                bool active = wins != null && wins.Any(w => w.H == foreground);
                if (wins != null && wins.Count > 0) cell.Children.Add(Indicator(active, wins.Count > 1));
                var tile = Tile(cell, null, null, null);
                if (active) tile.Background = Frozen(Cur.Fg, 0x1E);
                return tile;
            }
            if (token == "start")
            {
                var start = new StartLogo(); start.SetColor(Accent);
                return Tile(new Viewbox { Width = 16 * Fs, Height = 16 * Fs, Child = start }, null, null, null);
            }
            if (token == "preview:desktop")
            {
                var tile = Tile(FgShape(new Rectangle { Width = 2, Height = TileH * 0.45, RadiusX = 1, RadiusY = 1 }, 0.45), null, null, null);
                tile.Width = 14; return tile;
            }
            string glyph = token == "search" ? "\uE721" : token == "preview:tray" ? (IsTop ? "\uE70D" : "\uE70E") : token == "preview:fold" ? "\uE921" : "\uE712";
            return Tile(G(glyph, 14 * Fs), null, null, null);
        }

        sealed class PanelSampleSurface : Border
        {
            readonly Border scene;
            readonly Brush softWallpaper;
            public PanelSampleSurface(Border backdrop, Brush soft) { scene = backdrop; softWallpaper = soft; }
            protected override void OnRender(DrawingContext dc)
            {
                if (softWallpaper != null && scene.ActualWidth > 0 && scene.ActualHeight > 0 && IsDescendantOf(scene))
                {
                    var start = scene.TranslatePoint(new Point(), this);
                    dc.PushClip(new RectangleGeometry(new Rect(RenderSize), CornerRadius.TopLeft, CornerRadius.TopLeft));
                    dc.DrawRectangle(softWallpaper, null, new Rect(start, scene.RenderSize));
                    dc.Pop();
                }
                base.OnRender(dc);
            }
        }

        // образец темы: маленькая капсула в её цветах
        static Press ThemeSwatch(string key, string name, bool sel, Action pick)
        {
            var st = Styles[key];
            var cap = new Border { Height = 30, CornerRadius = new CornerRadius(15), Background = new SolidColorBrush(Color.FromArgb(Math.Max(st.Bg.A, (byte)0x90), st.Bg.R, st.Bg.G, st.Bg.B)), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, st.Fg.R, st.Fg.G, st.Fg.B)), Margin = new Thickness(10, 12, 10, 0) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            row.Children.Add(new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(st.Ac), Margin = new Thickness(0, 0, 6, 0) });
            foreach (var w in new[] { 26.0, 18, 34 }) row.Children.Add(new Border { Width = w, Height = 16, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0x26, st.Fg.R, st.Fg.G, st.Fg.B)), Margin = new Thickness(0, 0, 5, 0) });
            cap.Child = row;
            var backdrop = new Border { Height = 56, CornerRadius = new CornerRadius(10), Background = new LinearGradientBrush(Color.FromRgb(0x4A, 0x5F, 0x8C), Color.FromRgb(0x8C, 0x5F, 0x7A), 20), Child = cap };
            var label = Txt(name, 11.5, sel); label.Margin = new Thickness(2, 8, 0, 0);
            var b = new Press { Width = 168, Padding = new Thickness(6), Margin = new Thickness(0, 0, 10, 10), CornerRadius = new CornerRadius(12), Child = VS(backdrop, label), Cursor = Cursors.Hand, Invoke = pick, BorderThickness = new Thickness(2) };
            b.BorderBrush = sel ? Accent : Brushes.Transparent; b.Background = Frozen(CardFg, sel ? (byte)0x10 : (byte)0x06);
            System.Windows.Automation.AutomationProperties.SetName(b, "Тема " + name + (sel ? ", выбрана" : ""));
            b.MouseLeftButtonUp += (s, e) => pick();
            return b;
        }

        static Window settings; static Action<string> settingsGo;
        static FrameworkElement SettingsFocusTarget(DependencyObject root, string tag)
        {
            var control = root as FrameworkElement;
            if (control != null && Equals(control.Tag, tag)) return control;
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                var match = SettingsFocusTarget(child, tag);
                if (match != null) return match;
            }
            return null;
        }
        static readonly string[][] SettingsSections = {
            new[] { "look", "Оформление", "" }, new[] { "bar", "Панель", "" }, new[] { "monitors", "Мониторы", "" }, new[] { "widgets", "Виджеты", "" },
            new[] { "connect", "Подключения", "\uE71B" }, new[] { "auto", "Автоматизации", "" }, new[] { "notify", "Уведомления", "" }, new[] { "general", "Общие", "" } };
        void ShowSettings(string section)
        {
            if (settings != null) { settings.Activate(); if (section != null) settingsGo(section); return; }
            var w = AppWindow("Настройки", 940, 720);
            var dock = new DockPanel();
            var nav = new StackPanel { Width = 192, Margin = new Thickness(12, 24, 12, 14) };
            var navHead = Txt("Flowbar", 21, true); navHead.FontFamily = Display; navHead.Margin = new Thickness(12, 0, 0, 22); nav.Children.Add(navHead);
            var navLayer = new Grid(); nav.Children.Add(navLayer);
            var selection = new Border { Height = 38, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(9), IsHitTestVisible = false, RenderTransform = new TranslateTransform() };
            var navList = new StackPanel(); navLayer.Children.Add(selection); navLayer.Children.Add(navList);
            var navSide = new Border { Child = nav, BorderThickness = new Thickness(0, 0, 1, 0) };
            DockPanel.SetDock(navSide, Dock.Left); dock.Children.Add(navSide);
            var content = new StackPanel { Margin = new Thickness(26, 24, 24, 30), MaxWidth = 720 };
            var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var previewBody = new StackPanel { MaxWidth = 720, Margin = new Thickness(26, 10, 24, 12) };
            var previewFooter = new Border { Child = previewBody, BorderThickness = new Thickness(0, 1, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(previewFooter, "Закреплённый предпросмотр панели");
            var right = new DockPanel();
            DockPanel.SetDock(previewFooter, Dock.Bottom);
            right.Children.Add(previewFooter);
            right.Children.Add(scroll);
            dock.Children.Add(right);
            var preview = AddSettingsPreview(previewBody);
            refreshPreview = preview;
            string cur = section ?? "look";
            Action<string> go = null;
            go = key =>
            {
                bool changed = key != cur;
                var focused = Keyboard.FocusedElement as FrameworkElement;
                var focusTag = focused == null ? null : focused.Tag as string;
                cur = key; lastSection = key; double off = changed ? 0 : scroll.VerticalOffset;
                w.Background = Frozen(CardBg, 255); w.Foreground = Frozen(CardFg, 255);
                navSide.Background = Frozen(CardFg, 0x08); navSide.BorderBrush = Frozen(CardFg, 0x22);
                previewFooter.BorderBrush = Frozen(CardFg, 0x22);
                selection.Background = Frozen(AccentColor, 0x36);
                var marker = (TranslateTransform)selection.RenderTransform;
                double y = Math.Max(0, Array.FindIndex(SettingsSections, item => item[0] == key)) * 40;
                double from = marker.Y;
                marker.BeginAnimation(TranslateTransform.YProperty, null); marker.Y = y;
                if (changed && Motion) marker.BeginAnimation(TranslateTransform.YProperty, SettingsMotion(from, y, 200));
                navList.Children.Clear();
                foreach (var sct in SettingsSections)
                {
                    var k = sct[0];
                    var item = NavItem(sct[2], sct[1], false, () => go(k));
                    item.Height = 38; item.Tag = "settings-section:" + k;
                    if (k == cur) System.Windows.Automation.AutomationProperties.SetItemStatus(item, "Выбран");
                    navList.Children.Add(item);
                }
                content.Children.Clear();
                Action again = () => go(cur);
                switch (key)
                {
                    case "look": SettingsLook(content, again, preview); break;
                    case "bar": SettingsBar(content, again, preview); break;
                    case "monitors": SettingsMonitors(content, again); break;
                    case "widgets": SettingsWidgets(content, again); break;
                    case "connect": SettingsConnect(content); break;
                    case "auto": content.Children.Add(SH("Автоматизации")); content.Children.Add(AutomationsBody()); break;
                    case "notify": SettingsNotify(content, again); break;
                    default: SettingsGeneral(content, again); break;
                }
                StyleInputs(content, CardFg);
                preview(null);
                scroll.UpdateLayout(); scroll.ScrollToVerticalOffset(off);
                if (focusTag != null && focused != null && Window.GetWindow(focused) != w)
                {
                    var match = SettingsFocusTarget(dock, focusTag);
                    if (match != null) match.Focus();
                }
                if (changed && Motion) content.BeginAnimation(OpacityProperty, SettingsMotion(0.55, 1, 160));
            };
            settingsGo = go; go(cur);
            w.Content = dock;
            w.Activated += (s, e) => preview(null);
            w.Closed += (s, e) => { settings = null; settingsGo = null; refreshPreview = null; settingsPreviewMonitor = null; candidateTheme = null; candidateAppearance = null; };
            settings = w; w.Show(); w.Activate();
        }
        static void RestyleWindows() { if (settingsGo != null) settingsGo(settingsCur()); if (catalog != null) { catalog.Background = Frozen(CardBg, 255); catalog.Foreground = Frozen(CardFg, 255); if (catalogRender != null) catalogRender(); } }
        static string settingsCur() { return lastSection ?? "look"; }
        static string lastSection;

        void SettingsLook(StackPanel c, Action again, Action<string> preview)
        {
            lastSection = "look";
            c.Children.Add(SH("Оформление"));
            c.Children.Add(SHint(CompareThemes ? "Выберите тему и подтвердите её в предпросмотре." : "Наведите на тему для примерки. Нажмите, чтобы применить."));
            c.Children.Add(SSub("Тема"));
            var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var curKey = C("style", "Black");
            foreach (var t in FeaturedThemes)
            {
                var k = t[0];
                wrap.Children.Add(ElementTheme(t, curKey == k, () => PickTheme(k, preview),
                    over => preview(over ? k : null)));
            }
            c.Children.Add(wrap);
            var more = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var k in Styles.Keys.Where(k => !FeaturedThemes.Any(t => t[0] == k))) { var kk = k; more.Children.Add(ThemeSwatch(kk, ThemeName(kk), curKey == kk, () => PickTheme(kk, preview))); }
            // своё раскрытие вместо Expander: системный стиль Expander перебивал цвета темы
            more.Visibility = FeaturedThemes.Any(t => t[0] == curKey) ? Visibility.Collapsed : Visibility.Visible;
            var arrow = G(more.Visibility == Visibility.Visible ? "\uE70D" : "\uE76C", 10);
            var toggle = Act(HS(arrow, Mg(Txt("Другие темы", 12.5, true), 8)), "Другие темы", () =>
            {
                more.Visibility = more.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                arrow.Text = more.Visibility == Visibility.Visible ? "\uE70D" : "\uE76C";
            });
            toggle.Padding = new Thickness(4); toggle.Margin = new Thickness(0, 12, 0, 0); toggle.HorizontalAlignment = HorizontalAlignment.Left;
            c.Children.Add(toggle); c.Children.Add(more);
            c.Children.Add(SSub("Акцент"));
            c.Children.Add(SHint("Цвет индикаторов, шкал, кнопок и «сегодня» в календаре."));
            var acc = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) }; var curA = C("accent", "");
            foreach (var a in AccentPalette)
            {
                var hex = a; Color col = hex == "" ? Cur.Ac : (Color)ColorConverter.ConvertFromString(hex);
                var dot = new Ellipse { Width = 28, Height = 28, Fill = new SolidColorBrush(col) };
                var ring = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), BorderThickness = new Thickness(2), BorderBrush = curA == hex ? Frozen(CardFg, 0xC0) : Brushes.Transparent, Child = dot };
                var b = new Press { Child = hex == "" ? (UIElement)VS(ring, Mg(Txt("тема", 9.5, false), 6, 2)) : ring, Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, Tag = "settings-accent:" + hex, ToolTip = hex == "" ? "Как в теме" : hex, Invoke = () => { if (hex == "") cfg.Remove("accent"); else cfg["accent"] = hex; SaveCfg(); RebuildAll(); again(); } };
                System.Windows.Automation.AutomationProperties.SetName(b, hex == "" ? "Акцент как в теме" : "Акцент " + hex);
                b.MouseLeftButtonUp += (s, e) => b.Invoke();
                acc.Children.Add(b);
            }
            c.Children.Add(acc);
            c.Children.Add(SSub("Плотность заливки панели"));
            c.Children.Add(SliderRow(30, 100, Opac * 100, 5, v => Math.Round(v) + "%", v => { cfg["opacity"] = Math.Round(v).ToString(); SaveCfg(); All(b => b.ApplyStyle()); preview(null); }, "Плотность заливки панели"));
            MaterialSettings(c, again, preview);
        }
        void SettingsBar(StackPanel c, Action again, Action<string> preview)
        {
            lastSection = "bar";
            c.Children.Add(SH("Панель"));
            var sections = Seg(new[] { "Сплошная", "Раздельная" }, Sections ? 1 : 0, i => { cfg["bar.sections"] = i.ToString(); SaveCfg(); RebuildAll(); again(); });
            sections.IsEnabled = !Flush && bars.Any(b => !b.VerticalBar);
            c.Children.Add(SetRow("Секции", "Приложения, виджеты и системные кнопки получают отдельные подложки на плавающей горизонтальной панели.", sections));
            c.Children.Add(CheckRow("Стопка миниатюр", "На значке программы видно, что открыто несколько окон.", C("apps.stack", "0") == "1",
                v => { cfg["apps.stack"] = v ? "1" : "0"; SaveCfg(); RebuildAll(); }));
            c.Children.Add(CheckRow("Прогресс под значками приложений", "Показывает прогресс из системной панели задач и доступные индикаторы в окнах.", C("apps.progress", "0") == "1",
                v => { cfg["apps.progress"] = v ? "1" : "0"; if (v) Tray.EnableTaskbarProgress(); SaveCfg(); RebuildAll(); }));
            var sizes = new[] { "S", "M", "L", "XL" };
            c.Children.Add(SetRow("Общий размер", "S — 48, M — 60, L — 72, XL — 88 логических пикселей. Размер отдельного экрана можно задать в разделе «Мониторы».",
                Seg(sizes, Math.Max(0, Array.IndexOf(sizes, C("size", "M"))), i => { cfg["size"] = sizes[i]; SaveCfg(); RebuildAll(); again(); })));
            c.Children.Add(SetRow("Компоновка", PresetHints[PresetIndex] + " Набор — только начало: потом всё можно переставить, а «Своя» вернёт прежнюю раскладку.",
                VS(Seg(PresetNames.Take(4).ToArray(), PresetIndex < 4 ? PresetIndex : -1, i => { ApplyPreset(i); again(); }),
                   Mg(Seg(PresetNames.Skip(4).ToArray(), PresetIndex >= 4 ? PresetIndex - 4 : -1, i => { ApplyPreset(i + 4); again(); }), 0, 6, 0, 0)))); // два ряда: в один все наборы не помещаются
            var budgets = new[] { 0, 6, 8, 10, 14 }; int bi = Math.Max(0, Array.IndexOf(budgets, AppMax));
            c.Children.Add(SetRow("Значков программ на панели", "Остальные — в «…» (активная программа всегда на панели). «Все» — значки не прячутся; если места нет, ряд листается колесом.",
                Seg(new[] { "Все", "6", "8", "10", "14" }, bi, i => { cfg[MK("appMax")] = budgets[i].ToString(); MarkCustom(); SaveCfg(); All(b => { b.ResetFit(false); b.Compose(); }); again(); })));
            c.Children.Add(SetRow("Отступ от края экрана", null, SliderRow(0, 32, Gap, 1, v => Math.Round(v) + " px", v => { cfg["gap"] = Math.Round(v).ToString(); SaveCfg(); All(b => { b.reserveKey = null; b.Place(); }); preview(null); }, "Отступ от края экрана")));
            var aligns = new[] { "left", "center", "right" };
            c.Children.Add(SetRow("Выравнивание", "Где стоит панель вдоль края экрана. При ширине «во всю ширину» — где стоят значки внутри неё. Для боковой панели не действует.",
                Seg(new[] { "Слева", "По центру", "Справа" }, Math.Max(0, Array.IndexOf(aligns, BarAlign)), i => { cfg["align"] = aligns[i]; SaveCfg(); RebuildAll(); All(b => b.Place()); preview(null); })));
            var edges = new[] { "bottom", "top", "left", "right" };
            c.Children.Add(SetRow("Общее расположение", "В разделе «Мониторы» можно выбрать свой край для каждого экрана.",
                Seg(new[] { "Снизу", "Сверху", "Слева", "Справа" }, Math.Max(0, Array.IndexOf(edges, C("pos", "bottom"))),
                    i => { cfg["pos"] = edges[i]; SaveCfg(); ClosePanels(); RebuildAll(); All(b => { b.reserveKey = null; b.Place(); }); again(); })));
            var widths = new[] { "fit", "wide", "full" }; int wi = Math.Max(0, Array.IndexOf(widths, C("width", "fit")));
            c.Children.Add(SetRow("Размер вдоль края", "В режиме «Вплотную» отступ не используется.",
                Seg(new[] { "По содержимому", "Вдоль края", "Вплотную" }, wi, i => { cfg["width"] = widths[i]; SaveCfg(); RebuildAll(); All(b => { b.reserveKey = null; b.Place(); }); again(); })));
            var fold = Btn("Свернуть панель в значок", () => All(b => b.SetCollapsed(true))); fold.HorizontalAlignment = HorizontalAlignment.Left; fold.Margin = new Thickness(0, 6, 0, 4);
            c.Children.Add(SetRow("Свёртка", "Панель сжимается до одного значка; нажатие на него раскрывает её с прежним составом. Таймеры и музыка продолжают работать. Это не автоскрытие.", fold));
            c.Children.Add(SetRow("Автоскрытие", "«Когда не используется» — панель исчезает и появляется, когда курсор у края экрана. Тогда она накладывается поверх окон, место не резервируется.",
                Seg(new[] { "Никогда", "Когда не используется" }, AutoHide ? 1 : 0, i => { cfg["autohide"] = i == 1 ? "1" : "0"; SaveCfg(); All(b => { b.reserveKey = null; b.Place(); }); again(); })));
            c.Children.Add(CheckRow("Резервировать место под панель", "Развёрнутые окна заканчиваются перед FlowBar у выбранного края — на каждом мониторе, с учётом размера, отступа и масштаба.", C("reserve", "1") == "1", v => { cfg["reserve"] = v ? "1" : "0"; SaveCfg(); All(b => { b.reserveKey = null; b.Place(); }); }));
            c.Children.Add(CheckRow("Скрывать панель задач Windows", WatchOk ? "FlowBar заменяет её; при закрытии или сбое FlowBar сторож вернёт панель Windows в исходное состояние." : "Недоступно: сторож не запустился, поэтому панель Windows остаётся видимой.", C("hideTaskbar", "1") == "1",
                v => { cfg["hideTaskbar"] = v ? "1" : "0"; SaveCfg(); All(b => { b.reserveKey = null; b.Place(); }); if (HideTb) SetTaskbar(false); else Taskbar.Restore(false, false); })); // сначала рабочая область, потом панель Windows: иначе её полоса затрётся
            c.Children.Add(CheckRow("Заблокировать расположение значков", "Запрещает перетаскивать значки и модули.", Locked, v => { cfg["lock"] = v ? "1" : "0"; SaveCfg(); }));
            DesktopMediaSettings(c, again);
        }
        void SettingsMonitors(StackPanel c, Action again)
        {
            lastSection = "monitors";
            c.Children.Add(SH("Мониторы"));
            c.Children.Add(SHint("Панель есть на каждом мониторе. Для дополнительных мониторов выберите, какие модули показывать."));
            c.Children.Add(MonitorMap(again));
            // как в Windows: «Показывать кнопки панели задач»
            var modes = new[] { "all", "main", "own" }; int wm = Array.IndexOf(modes, WinMode);
            Action refresh = () => All(b => { b.lastKey = null; b.RefreshApps(); });
            c.Children.Add(SetRow("Окна на панелях", "Где показывать значки открытых окон. Значок, перетащенный на панель другого монитора, переносит туда окна программы.",
                Seg(new[] { "На всех панелях", "На основной и там, где открыто", "Только там, где открыто" }, wm, i => { cfg["winMode"] = modes[i]; SaveCfg(); refresh(); again(); })));
            c.Children.Add(CheckRow("Закреплённые значки на всех панелях", "Если выключено, на дополнительных мониторах закреплённая программа видна, только когда её окно открыто на этом мониторе.",
                C("pinsAll", "1") == "1", v => { cfg["pinsAll"] = v ? "1" : "0"; SaveCfg(); refresh(); }));
            int n = 1;
            foreach (var sc in System.Windows.Forms.Screen.AllScreens.OrderByDescending(x => x.Primary))
            {
                var dev = sc.DeviceName;
                var monitorBar = bars.FirstOrDefault(x => x.scr.DeviceName == dev);
                c.Children.Add(SSub("Монитор " + n++ + (sc.Primary ? " · основной" : "") + " · " + sc.Bounds.Width + "×" + sc.Bounds.Height));
                if (monitorBar != null) MonitorGeometrySettings(c, monitorBar, again);
                if (sc.Primary) { c.Children.Add(SHint("Основная раскладка — её меняют каталог и перетаскивание на панели.")); continue; }
                var mode = C("mon." + dev, "none"); int sel = mode == "same" ? 1 : mode == "own" ? 2 : mode == "compact" ? 3 : 0;
                c.Children.Add(Mg(Seg(new[] { "Без виджетов", "Как на основном", "Своя раскладка", "Компактная" }, sel, i =>
                {
                    cfg["mon." + dev] = new[] { "none", "same", "own", "compact" }[i]; SaveCfg();
                    var b = bars.FirstOrDefault(x => x.scr.DeviceName == dev); if (b != null) { b.ResetFit(true); b.Rebuild(); b.Place(); }
                    again();
                }), 0, 6));
                if (mode == "compact") CompactMonitorSettings(c, dev, again);
                if (mode == "own")
                {
                    var b = bars.FirstOrDefault(x => x.scr.DeviceName == dev);
                    var open = Btn("Настроить в каталоге", () => { if (b != null) b.ShowCatalog(b); }, true); open.HorizontalAlignment = HorizontalAlignment.Left; open.Margin = new Thickness(0, 10, 0, 0);
                    c.Children.Add(open);
                }
            }
        }
        void SettingsWidgets(StackPanel c, Action again)
        {
            lastSection = "widgets";
            c.Children.Add(SH("Виджеты"));
            c.Children.Add(SHint("Состав и порядок задаются в каталоге и перетаскиванием прямо на панели. Здесь — вид модулей."));
            var open = Btn("Открыть каталог виджетов", () => ShowCatalog(Primary), true); open.HorizontalAlignment = HorizontalAlignment.Left; open.Margin = new Thickness(0, 12, 0, 4);
            c.Children.Add(open);
            c.Children.Add(CheckRow("Микшер по нажатию на громкость", "Громкость и отключение звука для приложений на текущем устройстве.", C("widget.mixer", "1") == "1",
                v => { cfg["widget.mixer"] = v ? "1" : "0"; SaveCfg(); }));
            c.Children.Add(CheckRow("Дуга светового дня", "Рассвет и закат для города из виджета погоды. Дуга заменяет строку даты в панели.", C("clock.daylight", "0") == "1",
                v => { cfg["clock.daylight"] = v ? "1" : "0"; SaveCfg(); RebuildAll(); again(); }));
            AddWidgetStateGallery(c, again);
            c.Children.Add(SSub("Вид модулей"));
            foreach (var id in new[] { "clock", "media", "weather", "cpu", "ram", "temp", "net", "disk", "bat" })
            {
                var k = id; var name = k == "clock" ? "Часы" : Def(k)[1];
                var cur = C("view." + k, ""); int sel = Math.Max(0, Array.IndexOf(ViewKeys, cur));
                var line = new DockPanel();
                var seg = Seg(new[] { "Авто", "Мин", "Комп", "Расш" }, sel, i => { if (i == 0) cfg.Remove("view." + k); else cfg["view." + k] = ViewKeys[i]; SaveCfg(); RebuildAll(); again(); }, true);
                DockPanel.SetDock(seg, Dock.Right); line.Children.Add(seg);
                var t = Txt(name, 12.5, false); t.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(t);
                c.Children.Add(SettingsRow(line));
            }
            var reset = Btn("Раскладка по умолчанию", () => { foreach (var b in bars) cfg.Remove(b.LayoutKey); SaveCfg(); RebuildAll(); again(); });
            reset.HorizontalAlignment = HorizontalAlignment.Left; reset.Margin = new Thickness(0, 18, 0, 0); c.Children.Add(reset);
        }
        void SettingsConnect(StackPanel c)
        {
            lastSection = "connect";
            c.Children.Add(SH("Подключения"));
            // агенты: MCP-сервер внутри FlowBar
            c.Children.Add(SSub("Агенты · MCP"));
            c.Children.Add(SHint("Claude Code, Codex и другие агенты могут добавлять задачи, заметки и напоминания, запускать таймер и показывать уведомления. Работает только на этом компьютере, без сети. Удалять данные агенты не могут."));
            c.Children.Add(CheckRow("Разрешить агентам управлять FlowBar", null, C("agent", "1") == "1", v => { cfg["agent"] = v ? "1" : "0"; SaveCfg(); }));
            c.Children.Add(CheckRow("Разрешить агентам читать задачи, заметки и календарь", "Без этого агенты могут только добавлять.", C("agent.read", "0") == "1", v => { cfg["agent.read"] = v ? "1" : "0"; SaveCfg(); }));
            {
                string exe = Setup.InstalledExe, cmd = "claude mcp add flowbar -- \"" + exe + "\" --mcp";
                var box = new TextBox { Text = cmd, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 6, 0, 0), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11.5 };
                c.Children.Add(SHint("Подключение к Claude Code — выполните один раз в терминале:")); c.Children.Add(box);
                c.Children.Add(CardActions(Btn("Скопировать команду", () => { try { Clipboard.SetText(cmd); } catch (Exception ex) { Log(ex); } })));
                c.Children.Add(SHint("Другим агентам укажите команду запуска сервера: \"" + exe + "\" --mcp"));
            }
            Action<string, string, string, Border> item = (title, state, hint, btn) =>
            {
                c.Children.Add(SSub(title));
                var st = Txt(state, 12, false); st.Margin = new Thickness(0, 2, 0, 2); c.Children.Add(st);
                if (hint != null) c.Children.Add(SHint(hint));
                if (btn != null) { btn.HorizontalAlignment = HorizontalAlignment.Left; btn.Margin = new Thickness(0, 8, 0, 0); c.Children.Add(btn); }
            };
            var city = C("wxCity", "");
            item("Погода · Open-Meteo", city == "" ? "Город не выбран" : "Город: " + city + (wxError != null ? " · нет связи: " + wxError : wx != null ? " · обновлено " + wx.Updated.ToString("HH:mm") : ""), "Бесплатный сервис без ключа и регистрации.", Btn(city == "" ? "Выбрать город" : "Изменить город", () => Primary.ShowWeather()));
            item("Календари · iCal", calFeeds.Count == 0 ? "Не подключены" : "Подключено: " + calFeeds.Count + (calError != null ? " · есть ошибки" : ""), "Ссылки iCal из Google, Outlook, Яндекса и других календарей. Только просмотр, обновление раз в 15 минут.", null);
            c.Children.Add(CalFeedsEditor());
            bool bon = ClaudeBridge.On;
            item("Claude Code", (Directory.Exists(ClaudeUsage.Root) ? "Найден: " + ClaudeUsage.Root : "Не найден на этом компьютере") +
                (bon ? (live.ClBridge ? " · проценты: обновлено " + live.ClPctSeen : " · проценты: ждём первого ответа Claude Code") : " · проценты не подключены"),
                "Токены — по локальным журналам. Проценты 5-часового и недельного лимитов и заполнение контекста Claude Code сообщает только своей строке состояния: " +
                "FlowBar подключается к ней, а ваша нынешняя строка состояния продолжает работать (её команда сохраняется; копия настроек — settings.json.flowbar-backup). " +
                "Нужна подписка Pro или Max; данные приходят после каждого ответа Claude Code в терминале.",
                bon ? Btn("Отключить проценты", () => { var err = ClaudeBridgeRemove(); if (err != null) Primary.Notify("Не удалось отключить", err); RestyleWindows(); })
                    : Btn("Подключить проценты", () => ConnectClaudePct(), true));
            c.Children.Add(Mg(Txt("Что показывать в модуле Claude", 12.5, true), 0, 12, 0, 0));
            foreach (var r in ClRows) { var k = r[0]; c.Children.Add(CheckRow(r[1], null, ClSel.Contains(k), on => SetClRow(k, on))); }
            item("Codex", Directory.Exists(CodexLimits.Root) ? "Найден: " + CodexLimits.Root : "Не найден на этом компьютере", "Лимиты берутся из журналов Codex CLI.", null);
            item("Музыка", "Системное управление медиа Windows", "Работает со Spotify, браузерами и другими плеерами без настройки.", null);
            c.Children.Add(SHint("Почта, разработка, аналитика, соцсети и финансы появятся в каталоге вместе с соответствующими подключениями."));
        }
        // Мост строки состояния Claude Code: прежняя команда сохраняется и вызывается из моста; отключение возвращает её
        static string ClaudeBridgeInstall()
        {
            try
            {
                var js = new System.Web.Script.Serialization.JavaScriptSerializer(); var path = ClaudeBridge.SettingsPath;
                var s = File.Exists(path) ? js.DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object> : new Dictionary<string, object>();
                if (s == null) return "Не удалось прочитать " + path;
                var sl = s.ContainsKey("statusLine") ? s["statusLine"] as Dictionary<string, object> : null;
                string prev = sl != null && sl.ContainsKey("command") ? sl["command"] as string : null;
                if (prev != null && prev.IndexOf("claude-statusline.ps1", StringComparison.OrdinalIgnoreCase) >= 0) return null; // уже подключено
                Directory.CreateDirectory(Dir);
                File.WriteAllText(ClaudeBridge.Script, ClaudeBridge.ScriptText(prev), new System.Text.UTF8Encoding(true)); // с BOM: PowerShell 5 читает русский текст
                cfg["cl.prevStatus"] = prev ?? ""; SaveCfg();
                if (File.Exists(path) && !File.Exists(path + ".flowbar-backup")) File.Copy(path, path + ".flowbar-backup");
                var nsl = sl != null ? new Dictionary<string, object>(sl) : new Dictionary<string, object>();
                nsl["type"] = "command"; nsl["command"] = "powershell -NoProfile -ExecutionPolicy Bypass -File \"" + ClaudeBridge.Script + "\"";
                s["statusLine"] = nsl;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonPretty(js.Serialize(s)), new System.Text.UTF8Encoding(false));
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
        static string ClaudeBridgeRemove()
        {
            try
            {
                var js = new System.Web.Script.Serialization.JavaScriptSerializer(); var path = ClaudeBridge.SettingsPath;
                if (!File.Exists(path)) return null;
                var s = js.DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>; if (s == null) return "Не удалось прочитать " + path;
                var sl = s.ContainsKey("statusLine") ? s["statusLine"] as Dictionary<string, object> : null;
                var cur = sl != null && sl.ContainsKey("command") ? sl["command"] as string : null;
                if (cur == null || cur.IndexOf("claude-statusline.ps1", StringComparison.OrdinalIgnoreCase) < 0) return null; // строку состояния уже сменили — не трогаем
                var prev = C("cl.prevStatus", "");
                if (prev == "") s.Remove("statusLine"); else { var nsl = new Dictionary<string, object>(sl); nsl["command"] = prev; s["statusLine"] = nsl; }
                File.WriteAllText(path, JsonPretty(js.Serialize(s)), new System.Text.UTF8Encoding(false));
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
        void ConnectClaudePct()
        {
            var err = ClaudeBridgeInstall();
            if (err != null) Notify("Не удалось подключить проценты Claude", err, false, "\uE783");
            else Notify("Проценты Claude подключены", "Появятся после следующего ответа Claude Code (подписка Pro или Max). Ваша строка состояния работает как прежде.", false, "\uE73E", null, null, "claude");
            RestyleWindows();
        }
        // отступы для записанного обратно settings.json: сериализатор пишет одной строкой
        public static string JsonPretty(string j)
        {
            var sb = new System.Text.StringBuilder(); int ind = 0; bool str = false;
            for (int i = 0; i < j.Length; i++)
            {
                char ch = j[i];
                if (str) { sb.Append(ch); if (ch == '\\' && i + 1 < j.Length) sb.Append(j[++i]); else if (ch == '"') str = false; continue; }
                switch (ch)
                {
                    case '"': str = true; sb.Append(ch); break;
                    case '{': case '[':
                        if (i + 1 < j.Length && (j[i + 1] == '}' || j[i + 1] == ']')) { sb.Append(ch).Append(j[++i]); break; }
                        ind++; sb.Append(ch).Append('\n').Append(' ', ind * 2); break;
                    case '}': case ']': ind--; sb.Append('\n').Append(' ', ind * 2).Append(ch); break;
                    case ',': sb.Append(ch).Append('\n').Append(' ', ind * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString() + "\n";
        }
        void SettingsNotify(StackPanel c, Action again)
        {
            lastSection = "notify";
            c.Children.Add(SH("Уведомления"));
            c.Children.Add(SHint("Небольшая карточка у панели: значок, заголовок, пояснение и одно действие. Одно событие показывается один раз."));
            c.Children.Add(CheckRow("Показывать уведомления FlowBar", "Таймер, который вы завели сами, звучит всегда.", C("notify", "1") == "1", v => { cfg["notify"] = v ? "1" : "0"; SaveCfg(); }));
            c.Children.Add(CheckRow("Звук", null, C("notifySound", "1") == "1", v => { cfg["notifySound"] = v ? "1" : "0"; SaveCfg(); }));
            c.Children.Add(SetRow("Сколько показывать", null, SliderRow(4, 20, Clamp(C("notifyDur", ""), 8, 4, 20), 1, v => Math.Round(v) + " с", v => { cfg["notifyDur"] = Math.Round(v).ToString(); SaveCfg(); }, "Время показа уведомления")));
            c.Children.Add(SHint("Тихие часы и сценарии — в разделе «Автоматизации»."));
            var demo = Btn("Показать пример", () => Primary.NotifyDemo(), true); demo.HorizontalAlignment = HorizontalAlignment.Left; demo.Margin = new Thickness(0, 14, 0, 0); c.Children.Add(demo);
            c.Children.Add(SSub("Последние"));
            if (autoLog.Count == 0) c.Children.Add(SHint("Пока ничего"));
            foreach (var l in autoLog) c.Children.Add(SHint(l));
        }
        void SettingsGeneral(StackPanel c, Action again)
        {
            lastSection = "general";
            c.Children.Add(SH("Общие"));
            c.Children.Add(SHint("FlowBar " + Setup.VersionText + (Setup.IsInstalledCopy ? " · установлен в " + Setup.InstallDir : " · запущен не из папки установки") + ". Личные данные — в " + Dir + ", обновления их не меняют; перед каждым обновлением делается резервная копия в папке backups. Обновить — запустите новый FlowBar.exe."));
            // обновления: проверка последнего выпуска на GitHub и установка в один клик
            {
                Setup.Release found = null; Border inst = null;
                var upSt = SHint(Setup.RepoKnown ? "Выпуски: github.com/" + Setup.Repo : "Адрес выпусков ещё не задан");
                Func<Exception, string> why = ex => { var e0 = ex is AggregateException && ex.InnerException != null ? ex.InnerException : ex; return e0.Message; };
                var chk = Btn("Проверить обновления", () =>
                {
                    upSt.Text = "Проверяем…"; inst.Visibility = Visibility.Collapsed;
                    Task.Run(() => Setup.Latest()).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (t.IsFaulted) { upSt.Text = "Не удалось проверить: " + why(t.Exception); return; }
                        found = t.Result;
                        if (found.Ver > Setup.Current) { upSt.Text = "Доступна версия " + Setup.V3(found.Ver) + " — у вас " + Setup.VersionText + ". Настройки и записи при обновлении сохранятся."; inst.Visibility = Visibility.Visible; }
                        else upSt.Text = "У вас последняя версия " + Setup.VersionText + " (проверено " + DateTime.Now.ToString("HH:mm") + ")";
                    })));
                });
                inst = Btn("Скачать и установить", () =>
                {
                    if (found == null) return;
                    upSt.Text = "Скачиваем " + Setup.V3(found.Ver) + "…"; inst.IsEnabled = false;
                    var rel = found;
                    Task.Run(() => Setup.InstallRelease(rel)).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (t.IsFaulted) { upSt.Text = "Не удалось обновить: " + why(t.Exception); inst.IsEnabled = true; }
                        else upSt.Text = "Устанавливаем " + Setup.V3(rel.Ver) + " — FlowBar перезапустится сам.";
                    })));
                }, true);
                inst.Visibility = Visibility.Collapsed; inst.Margin = new Thickness(8, 0, 0, 0);
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) }; row.Children.Add(chk); row.Children.Add(inst);
                c.Children.Add(row); c.Children.Add(upSt);
                c.Children.Add(CheckRow("Сообщать о новой версии", "Раз в несколько часов FlowBar проверяет GitHub и показывает уведомление с кнопкой «Установить».", C("update.notify", "1") == "1", v => { cfg["update.notify"] = v ? "1" : "0"; SaveCfg(); }));
            }
            c.Children.Add(CardActions(Btn("Что нового в этой версии", () => { if (Primary != null) Primary.ShowNews(); })));
            c.Children.Add(CheckRow("Запускать вместе с Windows", null, AutoStart, v => AutoStart = v));
            c.Children.Add(CheckRow("Работать с правами администратора", (Setup.Elevated ? "Сейчас с правами. " : "Сейчас без прав. ")
                + "Нужно, только чтобы панель переключала окна программ, запущенных от администратора. Перетаскивать файлы из Проводника на панель Windows при этом не даёт. Действует после перезапуска FlowBar.",
                C("admin", "0") == "1", v => { cfg["admin"] = v ? "1" : "0"; SaveCfg(); }));
            c.Children.Add(SSub("Закрепления"));
            var imp = Btn("Перенести закреплённые с панели Windows", () => { foreach (var p in WindowsPins()) if (!pinned.Contains(p)) pinned.Add(p); SavePinned(); });
            var pick = Btn("Закрепить программу…", () => Primary.ShowPinPicker());
            c.Children.Add(CardActions(imp, pick));
            c.Children.Add(SSub("Данные"));
            c.Children.Add(SHint("Настройки, задачи, заметки, календари и экранное время хранятся в " + Dir));
            c.Children.Add(CardActions(Btn("Открыть папку данных", () => Launch(Dir)), Btn("Отчёт для разработчика", SaveReport)));
            c.Children.Add(SHint("Если что-то работает не так, нажмите «Отчёт для разработчика» и пришлите файл с рабочего стола. В нём версия, Windows, мониторы, настройки и журнал ошибок; город и ссылки скрыты."));
            c.Children.Add(SSub("Клавиатура"));
            c.Children.Add(SHint("Ctrl+Alt+B — перейти на панель; стрелки и Tab — по элементам; Enter или Пробел — нажать; Shift+F10 — меню; Esc — вернуться в прежнее окно.\nAlt+T — задачи. Win+S — поиск Windows."));
            c.Children.Add(SSub("Панель Windows"));
            // только до следующего запуска: выход и так возвращает панель Windows; запись «не скрывать» оставляла обе панели при каждом запуске
            c.Children.Add(CardActions(Btn("Вернуть панель Windows и выйти", () => { ExitWhy = "кнопка «Вернуть панель Windows и выйти»"; Application.Current.Shutdown(); })));
            var exe = Assembly.GetExecutingAssembly().Location;
            c.Children.Add(CardStatus("FlowBar · сборка от " + File.GetLastWriteTime(exe).ToString("d MMMM yyyy, HH:mm") + " · " + exe));
        }

        // ---------- уведомления ----------
        class Toast { public string Title, Text, Glyph, ActLabel, Widget; public Action Act; }
        static Queue<Toast> toastQueue = new Queue<Toast>();
        static HashSet<string> notifyKeys = new HashSet<string>();
        // force — сигнал, который пользователь завёл сам (таймер): звучит и в тихие часы. key — одно событие = одно уведомление
        // после установки или обновления — одно уведомление «было → стало»
        // раз в 6 часов (первый раз через 2 минуты после запуска): новая версия на GitHub — одно уведомление на версию, кнопка «Установить»
        static DateTime nextUpdateCheck = DateTime.Now.AddMinutes(2);
        internal static void CheckUpdateQuietly()
        {
            if (DateTime.Now < nextUpdateCheck || Primary == null) return;
            nextUpdateCheck = DateTime.Now.AddHours(6);
            if (C("update.notify", "1") != "1" || !Setup.RepoKnown) return;
            var b = Primary;
            Task.Run(() => Setup.Latest()).ContinueWith(t => b.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (t.IsFaulted) return; // нет сети или GitHub недоступен — молча, проверим в следующий раз
                var r = t.Result; var v = Setup.V3(r.Ver);
                if (r.Ver <= Setup.Current || C("update.told", "") == v) return;
                cfg["update.told"] = v; SaveCfg();
                b.Notify("Доступно обновление", "FlowBar " + v + " — у вас " + Setup.VersionText + ". Настройки и записи сохранятся.", false, "", "Установить", () =>
                {
                    b.Notify("FlowBar", "Скачиваем " + v + "… FlowBar перезапустится сам.", true, "");
                    Task.Run(() => Setup.InstallRelease(r)).ContinueWith(t2 => b.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!t2.IsFaulted) return;
                        var e0 = t2.Exception.InnerException ?? t2.Exception;
                        b.Notify("Обновление не установлено", e0.Message + ". Можно повторить: «Настройки» → «Общие» → «Проверить обновления».", true, "");
                    })));
                });
            })));
        }
        internal static void ShowUpdateNote() { var n = Setup.TakeUpdateNote(); if (n != null && Primary != null) Primary.Notify("FlowBar", n, true, "", "Что нового", () => Primary.ShowNews()); }
        // «Что нового»: CHANGELOG.md встроен в exe при сборке. Разделы «## версия», пункты «- …», вложенные — с отступом
        internal static List<Tuple<int, string>> ParseNews(string md)
        {
            var res = new List<Tuple<int, string>>(); bool started = false;
            foreach (var raw in (md ?? "").Replace("\r", "").Split('\n'))
            {
                var l = raw.TrimEnd(); if (l.StartsWith("## ")) { started = true; res.Add(Tuple.Create(0, l.Substring(3).Trim())); continue; }
                if (!started || l.Trim() == "") continue;
                var s = l.TrimStart(); int lvl = l.Length - s.Length >= 2 ? 2 : 1;
                if (s.StartsWith("- ")) s = s.Substring(2);
                res.Add(Tuple.Create(lvl, s.Replace("**", "").Replace("`", "")));
            }
            return res;
        }
        internal void ShowNews()
        {
            var body = new StackPanel(); string md = null;
            try { using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("FlowBar.CHANGELOG.md")) if (st != null) using (var rd = new StreamReader(st, System.Text.Encoding.UTF8)) md = rd.ReadToEnd(); } catch (Exception ex) { Log(ex); }
            var items = ParseNews(md); int vers = 0;
            if (items.Count == 0) body.Children.Add(Txt("Список изменений в эту сборку не вложен.", 12, false));
            foreach (var it in items)
            {
                if (it.Item1 == 0)
                {
                    if (++vers > 6) break; // последние шесть версий
                    var h = Txt(it.Item2.Replace(" — не выпущена", ""), 13.5, true); h.FontFamily = Display; h.Margin = new Thickness(0, vers == 1 ? 0 : 16, 0, 6); body.Children.Add(h); continue;
                }
                var dot = Txt(it.Item1 == 1 ? "•" : "–", 12, false); dot.Opacity = 0.6; dot.Width = 14; dot.VerticalAlignment = VerticalAlignment.Top;
                var tx = Txt(it.Item2, 12, false); tx.TextWrapping = TextWrapping.Wrap; tx.Opacity = it.Item1 == 1 ? 0.92 : 0.78; tx.LineHeight = 17;
                var row = new DockPanel { Margin = new Thickness(it.Item1 == 1 ? 0 : 14, 0, 0, 5) }; DockPanel.SetDock(dot, Dock.Left); row.Children.Add(dot); row.Children.Add(tx);
                body.Children.Add(row);
            }
            ShowPanel("Что нового", body, 420, null);
        }
        void Notify(string title, string text, bool force = false, string glyph = null, string actLabel = null, Action act = null, string widget = null, string key = null)
        {
            if (key != null && !notifyKeys.Add(key)) return;
            bool mute = !force && (QuietNow || C("notify", "1") == "0");
            autoLog.Insert(0, DateTime.Now.ToString("HH:mm") + " · " + title + (mute ? " (не показано)" : ""));
            if (autoLog.Count > 8) autoLog.RemoveAt(8);
            if (mute) return;
            toastQueue.Enqueue(new Toast { Title = title, Text = text, Glyph = glyph ?? "", ActLabel = actLabel, Act = act, Widget = widget });
            if (toast == null) ShowToast();
        }
        void NotifyDemo()
        {
            Notify("Встреча через 5 мин", "Планёрка с командой · Переговорная 2 (пример)", false, "", "Календарь", ShowCalendar, "event");
        }
        // одно спокойное выделение с плавным затуханием; модуль в «…» — выделяется «…»
        void Highlight(string id)
        {
            if (id == null) return;
            FrameworkElement el; Press p = null;
            if (widgetEls.TryGetValue(id, out el) && el.IsVisible && VisualTreeHelper.GetParent(el) != null) { var g = el as Grid; p = g == null ? null : g.Children[0] as Press; }
            else if (overflowIds.Contains(id)) p = overflowTile;
            if (p == null) return;
            var ac = AccentColor; var to = Color.FromArgb(0x60, ac.R, ac.G, ac.B);
            var br = new SolidColorBrush(to); p.Background = br;
            if (!Motion)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
                t.Tick += (s, e) => { t.Stop(); br.Color = p.Base; }; t.Start(); return;
            }
            br.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, p.Base, TimeSpan.FromMilliseconds(1400)) { BeginTime = TimeSpan.FromMilliseconds(500), EasingFunction = new QuadraticEase() });
        }
        bool Shows(string id) { return ResolveWidgetAnchor(id) != null; }
        // Прямоугольник модуля на экране (физические пиксели), только если он виден в основной полосе этой панели:
        // окно показано и не свёрнуто, модуль стоит в ряду, не уехал за край прокрутки и лежит на мониторе. «…» видимостью не считается.
        Rect? ResolveWidgetAnchor(string id)
        {
            FrameworkElement el;
            if (id == null || collapsed || folding || !IsVisible || autoHidden || fsHidden || strip == null || !widgetEls.TryGetValue(id, out el)) return null;
            if (!tokenOf.ContainsKey(el) || !el.IsVisible || el.ActualWidth < 1 || PresentationSource.FromVisual(el) == null || !el.IsDescendantOf(strip)) return null;
            var r = el.TransformToAncestor(stripScroll).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
            double seen = VerticalBar ? Math.Min(r.Bottom, stripScroll.ViewportHeight) - Math.Max(r.Top, 0) : Math.Min(r.Right, stripScroll.ViewportWidth) - Math.Max(r.Left, 0);
            if (seen < (VerticalBar ? r.Height : r.Width) * 0.6) return null;
            var pr = new Rect(el.PointToScreen(new Point(0, 0)), el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight)));
            var mb = scr.Bounds;
            return pr.IntersectsWith(new Rect(mb.Left, mb.Top, mb.Width, mb.Height)) ? (Rect?)pr : null;
        }
        // панель, где модуль виден: сначала активного экрана, затем основная, затем любая. null — модуль нигде не виден
        static Bar BarFor(string id)
        {
            if (id == null) return null;
            var fgMon = N.MonitorFromWindow(N.GetForegroundWindow(), 2);
            return bars.Where(b => b.ResolveWidgetAnchor(id) != null).OrderByDescending(b => b.mon == fgMon).ThenByDescending(b => b.primary).FirstOrDefault();
        }
        // действие уведомления «Подробнее» — открыть подробности модуля там, где он виден сейчас, и у него самого,
        // а не у панели, создавшей уведомление, и без срока после клика
        static void RunAct(Action a, string wid)
        {
            var b = a.Target as Bar;
            if (b != null && wid != null && a.Method.Name.StartsWith("Show") && a.Method.GetParameters().Length == 0)
            { var host = BarFor(wid) ?? b; host.OpenNear(wid, () => a.Method.Invoke(host, null)); return; }
            a();
        }
        // уведомления по одному: следующее — после закрытия текущего
        void ShowToast()
        {
            if (toastQueue.Count == 0) return;
            // уведомление — над своим модулем, на той панели, где он действительно виден; иначе — в обычном месте основного экрана
            var w0 = toastQueue.Peek().Widget;
            var host = w0 != null && Shows(w0) ? this : BarFor(w0) ?? Primary;
            if (host != null && host != this) { host.ShowToast(); return; }
            var item = toastQueue.Dequeue();
            if (C("notifySound", "1") == "1") SystemSounds.Asterisk.Play();
            Color bg = CardBg, fg = CardFg; const double Shadow = 18;
            var w = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
                ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height, Width = 340 + Shadow * 2, FontFamily = FontFamily, Foreground = Frozen(fg, 255)
            };
            System.Windows.Documents.Typography.SetNumeralAlignment(w, FontNumeralAlignment.Tabular);
            var ac = AccentColor;
            var icon = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = new SolidColorBrush(Color.FromArgb(0x2C, ac.R, ac.G, ac.B)), Child = G(item.Glyph, 15), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0) };
            ((TextBlock)icon.Child).Foreground = Accent;
            var row = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
            var close = Cross("Закрыть уведомление", () => w.Close()); close.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(close, Dock.Right); row.Children.Add(close);
            var texts = new StackPanel();
            texts.Children.Add(Txt(item.Title, 13, true));
            var body = Txt(item.Text, 12, false); body.TextWrapping = TextWrapping.Wrap; body.Opacity = 0.82; body.Margin = new Thickness(0, 2, 0, 0); texts.Children.Add(body);
            if (item.ActLabel != null && item.Act != null)
            {
                var a = item.Act; var wid = item.Widget; var btn = Btn(item.ActLabel, () => { w.Close(); RunAct(a, wid); }, true);
                btn.Padding = new Thickness(12, 5, 12, 5); btn.Margin = new Thickness(0, 10, 0, 0); btn.HorizontalAlignment = HorizontalAlignment.Left; texts.Children.Add(btn);
            }
            row.Children.Add(texts);
            var face = new Border { Background = CardFill, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 12, 10, 12), BorderThickness = new Thickness(1), BorderBrush = CardEdge, Child = row };
            StylePopupText(w);
            // тень отдельным слоем — текст уведомления не проходит через промежуточный растр
            var shade = new Border { CornerRadius = new CornerRadius(14), Background = Frozen(CardBg, 255), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = IsTop ? 90 : 270, Opacity = 0.32 } };
            var card = new Grid { Margin = new Thickness(Shadow), RenderTransform = new TranslateTransform() }; card.Children.Add(shade); card.Children.Add(face);
            w.Content = card; Glass(w, face, 14, Shadow, shade); FadeClose(w, card, IsTop ? -8 : 8);
            System.Windows.Automation.AutomationProperties.SetName(card, item.Title + ". " + item.Text);
            w.SourceInitialized += (s, e) => { var h = new WindowInteropHelper(w).Handle; N.SetWindowLongPtr(h, -20, new IntPtr(N.GetWindowLongPtr(h, -20).ToInt64() | 0x08000000 | 0x80)); };
            // вся геометрия — в физических пикселях: прямоугольник модуля с экрана и размер самого окна уведомления в его DPI
            Action place = () =>
            {
                var hw = new WindowInteropHelper(w).Handle; if (hw == IntPtr.Zero) return;
                double ts = VisualTreeHelper.GetDpi(w).DpiScaleX, cw = w.ActualWidth * ts, ch = w.ActualHeight * ts, mg = Shadow * ts;
                var an = item.Widget != null ? ResolveWidgetAnchor(item.Widget) : null; var wa = scr.WorkingArea; var mb = scr.Bounds; double x, y;
                if (an != null && VerticalBar) { var r = an.Value; x = LeftBar ? r.Right + 8 * ts - mg : r.Left - cw - 8 * ts + mg; y = (r.Top + r.Bottom - ch) / 2; }
                else if (an != null) { var r = an.Value; x = (r.Left + r.Right) / 2 - cw / 2; y = IsTop ? r.Bottom + 8 * ts - mg : r.Top - 8 * ts - ch + mg; }
                else { x = wa.Right - 12 * ts - cw + mg; y = wa.Bottom - 12 * ts - ch + mg; } // обычное место: правый нижний угол рабочей области
                x = Math.Max(mb.Left + 4 * ts - mg, Math.Min(mb.Right - 4 * ts + mg - cw, x)); y = Math.Max(mb.Top + 4 * ts - mg, Math.Min(mb.Bottom - 4 * ts + mg - ch, y)); // у края — внутрь экрана
                N.SetWindowPos(hw, IntPtr.Zero, (int)Math.Round(x), (int)Math.Round(y), 0, 0, 0x1 | 0x4 | 0x10);
            };
            w.SizeChanged += (s, e) => { if (w.IsLoaded) place(); };
            w.DpiChanged += (s, e) => place();
            w.Loaded += (s, e) =>
            {
                place();
                if (!Motion) return;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                card.RenderTransform.BeginAnimation(VerticalBar ? TranslateTransform.XProperty : TranslateTransform.YProperty,
                    new DoubleAnimation((VerticalBar ? LeftBar : IsTop) ? -10 : 10, 0, TimeSpan.FromMilliseconds(ExpressiveMotion ? 275 : 170)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
                card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) { FillBehavior = FillBehavior.Stop });
            };
            w.Closed += (s, e) => { if (toast == w) toast = null; if (toast == null) ShowToast(); };
            var life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Clamp(C("notifyDur", ""), 8, 4, 20)) };
            life.Tick += (s, e) => { if (w.IsMouseOver) return; life.Stop(); if (toast == w) w.Close(); }; // пока курсор на карточке — не закрываем
            toast = w; w.Show(); life.Start();
            All(b => b.Highlight(item.Widget));
        }

        // ---------- команды извне: --open и снимки для приёмки ----------
        static readonly uint ShotMsg = N.RegisterWindowMessage("FlowBarShot"), OpenMsg = N.RegisterWindowMessage("FlowBarOpen");
        public static readonly string[] OpenNames = { "", "settings", "catalog", "system", "music", "calendar", "notify", "weather", "close", "screen", "look", "bar", "monitors", "widgets", "connect", "notes", "todos", "storage", "temp", "wifi", "bluetooth", "quick", "fold", "unfold", "quit", "currency", "traydump", "report", "news" };
        void OpenByCode(int code, int barIndex)
        {
            var b = barIndex > 0 && barIndex <= bars.Count ? bars.OrderByDescending(x => x.primary).ElementAt(barIndex - 1) : this;
            anchor = null;
            switch (code < OpenNames.Length ? OpenNames[code] : "")
            {
                case "settings": b.ShowSettings(null); break;
                case "quit": ExitWhy = "команда quit от другого запуска FlowBar (обновление или --open quit)"; Application.Current.Shutdown(); break;
                case "currency": b.ShowFx(); break;
                case "report": SaveReport(); break;
                case "news": b.ShowNews(); break;
                case "traydump": try { File.WriteAllLines(F("tray-list.txt"), Tray.Dump()); } catch (Exception ex) { Log(ex); } break; // обновление: новая версия бережно закрывает старую
                case "look": case "bar": case "monitors": case "widgets": case "connect": b.ShowSettings(OpenNames[code]); break;
                case "catalog": b.ShowCatalog(b); break;
                case "system": b.OpenNear("cpu", b.ShowSystem); break;
                case "music": b.OpenNear("media", b.ShowMusic); break;
                case "calendar": b.OpenNear("event", b.ShowCalendar); break;
                case "weather": b.OpenNear("weather", b.ShowWeather); break;
                case "screen": b.OpenNear("screen", b.ShowScreenTime); break;
                case "notify": b.NotifyDemo(); break;
                case "notes": b.OpenNear("notes", b.ShowNotes); break;
                case "todos": b.OpenNear("todos", b.ShowTodos); break;
                case "storage": b.OpenNear("disk", b.ShowStorage); break;
                case "temp": b.OpenNear("temp", b.ShowTemp); break;
                case "wifi": b.OpenNear("conn", b.ShowWifi); break;
                case "bluetooth": b.OpenNear("radios", b.ShowBluetooth); break;
                case "quick": b.OpenNear("quick", b.ShowQuick); break;
                case "fold": b.SetCollapsed(true); break;
                case "unfold": b.SetCollapsed(false); break;
                case "close": foreach (var bb in bars) if (bb.panel != null) bb.panel.Close(); if (settings != null) settings.Close(); if (catalog != null) catalog.Close(); break;
            }
        }
        void OpenNear(string id, Action show)
        {
            if (ResolveWidgetAnchor(id) != null) { anchor = widgetEls[id]; anchorAt = DateTime.Now; } // карточка — у модуля, только если он виден в ряду
            show();
        }
        // каждое окно FlowBar — в PNG при заданном DPI (96 = 100 %, 120 = 125 %, 144 = 150 %)
        void SaveShots(int dpi)
        {
            if (dpi < 72 || dpi > 288) dpi = 96;
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FlowBar-shots"); Directory.CreateDirectory(dir); var diag = new List<string>();
            int n = 0;
            foreach (Window w in Application.Current.Windows)
            {
                if (!w.IsVisible) continue;
                var el = VisualTreeHelper.GetChildrenCount(w) > 0 ? VisualTreeHelper.GetChild(w, 0) as FrameworkElement : null; if (el == null) continue;
                bool isBar = w is Bar;
                var name = isBar ? "bar" + (((Bar)w).primary ? "-main" : "-" + ((Bar)w).scr.DeviceName.Replace("\\", "").Replace(".", "")) : "window-" + (++n);
                try { SaveVisual(el, System.IO.Path.Combine(dir, name + "-" + dpi + ".png"), dpi, w.AllowsTransparency); } catch (Exception ex) { Log(ex); }
                try
                {
                    // монитор, DPI окна по Windows и по WPF, размер в DIP и в физических пикселях — для сверки чёткости
                    var h = new WindowInteropHelper(w).Handle; N.RECT wr; GetWindowRect(h, out wr); var ps = PresentationSource.FromVisual(w);
                    diag.Add(name + "\tмонитор " + System.Windows.Forms.Screen.FromHandle(h).DeviceName + "\tGetDpiForWindow " + GetDpiForWindow(h) + "\tWPF " + (ps == null ? "—" : ps.CompositionTarget.TransformToDevice.M11.ToString("0.###"))
                        + "\tDIP " + Math.Round(w.ActualWidth, 2) + "×" + Math.Round(w.ActualHeight, 2) + "\tпикс " + (wr.R - wr.L) + "×" + (wr.B - wr.T) + " @ " + wr.L + "," + wr.T);
                }
                catch (Exception ex) { diag.Add(name + "\t" + ex.Message); }
            }
            try { File.WriteAllLines(System.IO.Path.Combine(dir, "dpi-" + dpi + ".txt"), diag); } catch { }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out N.RECT r);
        static void SaveVisual(FrameworkElement el, string path, double dpi, bool backdrop)
        {
            double w = el.ActualWidth, h = el.ActualHeight; if (w < 1 || h < 1) return;
            double k = dpi / 96;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                if (backdrop) dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x2E, 0x3F, 0x5E), Color.FromRgb(0x5E, 0x3F, 0x55), 0), null, new Rect(0, 0, w, h)); // подложка вместо обоев
                dc.DrawRectangle(new VisualBrush(el) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, Viewbox = new Rect(0, 0, w, h), ViewboxUnits = BrushMappingMode.Absolute }, null, new Rect(0, 0, w, h));
            }
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * k), (int)Math.Ceiling(h * k), dpi, dpi, PixelFormats.Pbgra32); rtb.Render(dv);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
        }
    }
}
