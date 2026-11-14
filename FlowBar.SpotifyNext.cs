using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlowBar
{
    partial class Bar
    {
        // Следующий трек Spotify без входа в аккаунт. Окно Spotify отдаёт дерево элементов (UI Automation), в нём у карточки
        // «Далее в очереди» лежат название, исполнитель и идентификатор трека spotify:track:…; язык интерфейса не важен.
        // Обложку по идентификатору отдаёт публичный oEmbed Spotify. Нужна открытая панель «Сейчас играет» (или очередь) справа.
        static bool SpotifyNextOn { get { return C("music.next", "1") == "1"; } }

        internal sealed class SpotifyNextTrack { public string Id, Title, Artist; public double Y; }

        // идентификатор трека из AutomationId строки списка вида «onClickHinttraits-list-row-spotify:track:0BXx…» (22 знака base62).
        // Строки таблиц («react-aria…-spotify:track:…», например блок клипов в той же панели) — не очередь, их не берём
        internal static string SpotifyTrackId(string automationId)
        {
            if (string.IsNullOrEmpty(automationId)) return null;
            var m = Regex.Match(automationId, @"list-row-spotify:track:([0-9A-Za-z]{22})(?![0-9A-Za-z])");
            return m.Success ? m.Groups[1].Value : null;
        }

        // кандидаты в порядке сверху вниз; если очередь открыта целиком, первой стоит играющая строка — её пропускаем
        internal static SpotifyNextTrack PickSpotifyNext(List<SpotifyNextTrack> candidates, string currentTitle)
        {
            if (candidates == null || candidates.Count == 0) return null;
            var sorted = candidates.OrderBy(c => c.Y).ToList();
            if (!string.IsNullOrEmpty(currentTitle) && string.Equals(sorted[0].Title, currentTitle, StringComparison.OrdinalIgnoreCase))
                return sorted.Count > 1 ? sorted[1] : null;
            return sorted[0];
        }

        // адрес обложки из ответа oEmbed; только https и только серверы изображений Spotify — ответ не должен уводить запрос куда попало
        internal static string SpotifyThumbUrl(string json)
        {
            try
            {
                var o = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                object v; if (o == null || !o.TryGetValue("thumbnail_url", out v)) return null;
                Uri u; if (!Uri.TryCreate(v as string, UriKind.Absolute, out u) || u.Scheme != Uri.UriSchemeHttps) return null;
                string h = u.Host.ToLowerInvariant();
                return h == "scdn.co" || h.EndsWith(".scdn.co") || h == "spotifycdn.com" || h.EndsWith(".spotifycdn.com") ? u.AbsoluteUri : null;
            }
            catch { return null; }
        }

        // читает окно Spotify; null — окна нет (свёрнуто в трей, не запущен) или следующего трека на панели нет
        internal static SpotifyNextTrack ReadSpotifyNext(string currentTitle)
        {
            try { return ReadSpotifyNextCore(currentTitle); }
            catch (ElementNotAvailableException) { return null; } // окно закрылось во время чтения
        }

        static SpotifyNextTrack ReadSpotifyNextCore(string currentTitle)
        {
            foreach (var p in Process.GetProcessesByName("Spotify"))
            {
                int pid; try { pid = p.Id; } finally { p.Dispose(); }
                var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, pid));
                foreach (AutomationElement window in windows)
                {
                    if (!window.Current.ClassName.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal)) continue;
                    var frame = window.Current.BoundingRectangle;
                    var found = new List<SpotifyNextTrack>();
                    var cache = new CacheRequest { TreeFilter = Automation.RawViewCondition };
                    cache.Add(AutomationElement.AutomationIdProperty); cache.Add(AutomationElement.BoundingRectangleProperty);
                    AutomationElementCollection all;
                    using (cache.Activate()) all = window.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
                    foreach (AutomationElement e in all)
                    {
                        string id;
                        try { id = SpotifyTrackId(e.Cached.AutomationId); }
                        catch (System.Runtime.InteropServices.COMException) { continue; } // у части элементов свойство не читается — у них нет идентификатора
                        catch (InvalidOperationException) { continue; }
                        if (id == null) continue;
                        if (found.Any(x => x.Id == id)) continue; // у карточки два таких элемента (щелчок и подпись)
                        var card = TreeWalker.RawViewWalker.GetParent(e); // строка списка: название — её имя, исполнители — ссылки внутри
                        if (card == null) continue;
                        var box = card.Current.BoundingRectangle;
                        if (frame.Width > 0 && box.X - frame.X < frame.Width * 0.6) continue; // строки списков в основной части окна — не очередь
                        var artists = new List<string>();
                        foreach (AutomationElement a in card.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink)))
                        { var n = a.Current.Name; if (!string.IsNullOrWhiteSpace(n) && !artists.Contains(n) && artists.Count < 4) artists.Add(n); }
                        found.Add(new SpotifyNextTrack { Id = id, Title = card.Current.Name, Artist = string.Join(", ", artists), Y = box.Y });
                    }
                    var pick = PickSpotifyNext(found, currentTitle);
                    if (pick != null) return pick;
                }
            }
            return null;
        }

        static readonly Dictionary<string, ImageSource> spotifyCovers = new Dictionary<string, ImageSource>();
        static readonly List<string> spotifyCoverOrder = new List<string>();

        // обложка 300×300 по идентификатору трека; null — нет связи или ответ не тот. Удачные ответы хранятся (последние 12).
        internal static ImageSource FetchSpotifyCover(string id)
        {
            lock (spotifyCovers) { ImageSource cached; if (spotifyCovers.TryGetValue(id, out cached)) return cached; }
            try
            {
                string json = Download("https://open.spotify.com/oembed?url=" + Uri.EscapeDataString("spotify:track:" + id), 64 * 1024, text: true) as string;
                string url = json == null ? null : SpotifyThumbUrl(json);
                if (url == null) return null;
                var bytes = Download(url, 2 * 1024 * 1024, text: false) as byte[];
                if (bytes == null || bytes.Length == 0) return null;
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = new MemoryStream(bytes); image.EndInit(); image.Freeze();
                lock (spotifyCovers)
                {
                    spotifyCovers[id] = image; spotifyCoverOrder.Add(id);
                    while (spotifyCoverOrder.Count > 12) { spotifyCovers.Remove(spotifyCoverOrder[0]); spotifyCoverOrder.RemoveAt(0); }
                }
                return image;
            }
            catch (Exception ex) { Log(ex); return null; }
        }

        static object Download(string url, int limit, bool text)
        {
            var rq = (HttpWebRequest)WebRequest.Create(url);
            rq.UserAgent = "FlowBar"; rq.Timeout = 6000; rq.ReadWriteTimeout = 6000; rq.AllowAutoRedirect = false; // переходов не нужно: адрес известен
            using (var rs = (HttpWebResponse)rq.GetResponse())
            {
                if (rs.StatusCode != HttpStatusCode.OK) return null;
                using (var src = rs.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    var buf = new byte[8192]; int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0) { ms.Write(buf, 0, n); if (ms.Length > limit) return null; }
                    return text ? (object)System.Text.Encoding.UTF8.GetString(ms.ToArray()) : ms.ToArray();
                }
            }
        }

        sealed class SpotifyNextState { public string ForKey; public MusicTrackCardData Card; }
        static volatile SpotifyNextState spotifyNext;
        static string nextPolledKey; static DateTime nextPollAt; static bool nextBusy; static int nextAttempts;

        // вызывается при каждом обновлении музыки (раз в секунду); тяжёлое — в фоне, при смене трека и затем раз в 20 секунд
        static void PollSpotifyNext(Snap s)
        {
            bool spotify = !DemoMode && SpotifyNextOn && s.HasMedia && s.App != null && s.App.IndexOf("Spotify", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!spotify) { spotifyNext = null; nextPolledKey = null; return; }
            string key = s.MusicTrack; bool changed = key != nextPolledKey;
            if (nextBusy || (!changed && DateTime.UtcNow < nextPollAt)) return;
            if (changed) { nextPolledKey = key; nextAttempts = 0; spotifyNext = null; }
            nextBusy = true; nextAttempts++;
            string title = s.Title, app = s.App;
            Task.Run(() =>
            {
                MusicTrackCardData card = null;
                try
                {
                    var n = ReadSpotifyNext(title);
                    if (n != null) card = new MusicTrackCardData("spotify:track:" + n.Id, app, n.Title, n.Artist, "В очереди Spotify", FetchSpotifyCover(n.Id)) /* альбом из окна не узнать — подпись вместо «Альбом не указан» */;
                }
                catch (Exception ex) { Log(ex); }
                var app0 = Application.Current;
                if (app0 == null) return;
                app0.Dispatcher.BeginInvoke(new Action(() =>
                {
                    nextBusy = false;
                    if (nextPolledKey != key) return; // трек уже сменился
                    var old = spotifyNext;
                    spotifyNext = card == null ? null : new SpotifyNextState { ForKey = key, Card = card };
                    // панель Spotify обновляется не сразу после смены трека: пока ничего нет — пробуем ещё несколько раз через 1,5 с
                    nextPollAt = DateTime.UtcNow.AddSeconds(card != null || nextAttempts >= 4 ? 20 : 1.5);
                    bool differs = (old == null) != (card == null) || (old != null && card != null && (old.Card.Key != card.Key || old.Card.Cover != card.Cover));
                    if (differs) All(b => { b.Render(); if (b.musicPanel == b.panel && b.panelTick != null) b.panelTick(); });
                    MaybePeekSkip(live); // панель очереди не помогла — заглядываем сразу, а не на следующем секундном тике
                }));
            });
        }

        // ---- Заглядывание в следующий трек ----
        // Если панель Spotify не открыта, следующий трек из окна не прочитать. Тогда в двух случаях FlowBar переключает вперёд и сразу назад,
        // запоминает следующий трек с обложкой и оставляет играть тот же трек, звук плеера на это время выключен:
        //  1) вы нажали «вперёд» в FlowBar — проход делается в момент нажатия, до того как новый трек стал слышен: A → B → C → B, и B
        //     звучит один раз с начала, с задержкой около секунды (без повтора);
        //  2) первое проигрывание после запуска Spotify (или ПК) — тут трек уже заиграл, поэтому повтор его начала возможен.
        static bool PeekSkipOn { get { return C("music.peek", "1") == "1"; } }
        static bool peekDone, peekBusy, peekSeen, peekBack, peekIssuing;
        static DateTime peekGoneAt = DateTime.MinValue, trackChangedAt = DateTime.MinValue;
        static string peekOriginal; static MusicTrackCardData peekObserved;
        static readonly List<Tuple<string, double>> peekQueued = new List<Tuple<string, double>>(); // кнопки, нажатые во время прохода: выполняются сразу после него
        static System.Windows.Threading.DispatcherTimer peekKick;

        // трек сменился: через 450 мс «тишины» можно заглядывать (если пользователь жмёт подряд, ждём последнего нажатия)
        static void NoteTrackChange()
        {
            trackChangedAt = DateTime.UtcNow;
            if (peekKick == null)
            {
                peekKick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                peekKick.Tick += (a, b) => { peekKick.Stop(); MaybePeekSkip(live); };
            }
            peekKick.Stop(); peekKick.Start();
        }

        // трек, увиденный во время прохода, попадает в историю сразу после исходного — но на экран (в live) не попадает
        static void PeekRecord(MusicTrackCardData card)
        {
            peekObserved = card;
            if (musicHistory.Count == 0 || musicHistory[musicHistoryAt].Key != peekOriginal) return;
            int at = musicHistoryAt + 1;
            if (at < musicHistory.Count) musicHistory[at] = card; else musicHistory.Add(card);
            while (musicHistory.Count > 8) { musicHistory.RemoveAt(0); musicHistoryAt--; }
        }

        // условия запуска: все сразу. Длинные записи (подкасты, миксы) и трек, который уже играет давно, не трогаем
        internal static bool PeekSkipWanted(bool on, bool spotify, bool playing, bool remote, bool canNext, bool canPrev, double pos, double dur,
            bool nextKnown, int uiAttempts, bool done, bool busy, bool recentSkip)
        {
            if (!on || !spotify || done || busy || recentSkip || nextKnown || remote || !playing || !canNext || !canPrev) return false;
            if (double.IsNaN(pos) || double.IsNaN(dur) || pos < 0.5 || pos > 12 || dur < 30 || dur > 1200) return false;
            return uiAttempts >= 1; // панель Spotify не помогла (закрыта), а ничего о следующем треке не известно (nextKnown — очередь или история)
        }

        // раз в секунду из обновления музыки
        static void MaybePeekSkip(Snap s)
        {
            bool spotify = s.HasMedia && (DemoMode || IsSpotify(s.App));
            if (!spotifyUp && !DemoMode) { peekDone = peekSeen = false; peekGoneAt = DateTime.MinValue; return; } // Spotify закрыт — следующий запуск снова «первый»
            if (!spotify)
            {
                if (peekGoneAt == DateTime.MinValue) peekGoneAt = DateTime.UtcNow;
                if ((DateTime.UtcNow - peekGoneAt).TotalSeconds > 20) { peekDone = peekSeen = false; }
                return;
            }
            peekGoneAt = DateTime.MinValue;
            if (!peekSeen)
            {
                peekSeen = true; // первый взгляд на Spotify в этом сеансе: если трек играет уже давно, «первого проигрывания» не было — не вмешиваемся и дальше
                if (double.IsNaN(s.Pos) || s.Pos > 12) peekDone = true;
            }
            bool nextKnown = NeighborTrack(s, 1) != null; // очередь Spotify или трек, уже встречавшийся в этом сеансе
            bool unsettled = (DateTime.UtcNow - trackChangedAt).TotalMilliseconds < 450; // трек только что сменился — возможно, пользователь жмёт дальше
            if (PeekSkipWanted(PeekSkipOn, spotify, s.Playing, s.Remote, s.CanNext, s.CanPrev, s.Pos, s.Dur, nextKnown, nextBusy ? 0 : nextAttempts, peekDone, peekBusy, unsettled))
                RunPeekSkip(s);
        }

        static async Task<bool> WaitFor(Func<bool> ok, int ms)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until) { if (ok()) return true; await Task.Delay(20); }
            return ok();
        }

        // вперёд, ждём следующий трек, сразу назад (Spotify в первые секунды трека «назад» возвращает на прошлый, а не перезапускает текущий).
        // Промежуточный трек на экран не попадает: UpdateMedia во время прохода только записывает его в историю.
        // В демо-режиме плеер меняется мгновенно и без событий Windows, поэтому там признак — сам live.
        internal static async void RunPeekSkip(Snap s)
        {
            peekBusy = true; peekBack = false; peekObserved = null; peekOriginal = s.MusicTrack;
            string original = s.MusicTrack; var muted = new List<Audio.MixerSession>(); var clock = System.Diagnostics.Stopwatch.StartNew();
            Action restore = () => { foreach (var x in muted) { try { x.SetMute(false); } catch { } } muted.Clear(); };
            var guard = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) }; // что бы ни случилось, плеер не останется без звука
            guard.Tick += (a, b) => { guard.Stop(); restore(); };
            guard.Start();
            Func<bool> sawNext = () => DemoMode ? live.MusicTrack != original : peekObserved != null;
            Func<bool> isBack = () => DemoMode ? live.MusicTrack == original : peekBack;
            bool back = false; long seenAt = 0;
            try
            {
                foreach (var x in PlayerSessions(s)) if (!x.Muted && x.SetMute(true)) muted.Add(x);
                peekIssuing = true; MediaCmd("next"); peekIssuing = false;
                if (await WaitFor(sawNext, 2000))
                {
                    seenAt = clock.ElapsedMilliseconds;
                    peekIssuing = true; MediaCmd("prev"); peekIssuing = false;
                    back = await WaitFor(isBack, 2500);
                }
            }
            catch (Exception ex) { Log(ex); }
            finally
            {
                peekIssuing = false; guard.Stop(); restore(); peekDone = true; peekBusy = false; peekOriginal = null;
            }
            Info("музыка: заглянули в следующий трек " + (back ? "и вернулись на исходный" : "— вернуться на исходный не удалось") + " за " + clock.ElapsedMilliseconds + " мс (следующий виден через " + seenAt + " мс)");
            if (!back && Primary != null) Primary.Notify("Музыка", "Не удалось вернуться на исходный трек после проверки следующего. Включите нужный трек вручную.");
            var queued = peekQueued.ToList(); peekQueued.Clear(); // кнопки, нажатые во время прохода, — теперь
            foreach (var q in queued) MediaCmd(q.Item1, q.Item2);
        }

        // «вперёд» в FlowBar: проход нужен, если панель Spotify не показывает очередь и трек после нового (C) ещё не известен.
        // Длинные записи (подкасты, миксы) не трогаем: там «вперёд» — не всегда следующий трек
        internal static bool PeekForwardWanted(bool on, bool spotify, bool canNext, bool canPrev, bool remote, double dur, bool panelWorking, bool afterKnown, bool busy)
        {
            if (!on || !spotify || !canNext || !canPrev || remote || busy || panelWorking || afterKnown) return false;
            return double.IsNaN(dur) || dur <= 1200;
        }

        static bool PeekInterceptNow(Snap s)
        {
            if (!s.HasMedia || !IsSpotify(s.App)) return false;
            bool panelWorking = spotifyNext != null && spotifyNext.ForKey == s.MusicTrack; // очередь читается из окна — заглядывать незачем
            bool afterKnown = musicHistory.Count > musicHistoryAt + 2 && musicHistory[musicHistoryAt].Key == s.MusicTrack; // после следующего трека история тоже есть
            return PeekForwardWanted(PeekSkipOn, true, s.CanNext, s.CanPrev, s.Remote, s.Dur, panelWorking, afterKnown, peekBusy);
        }

        // Нажатие «вперёд» без повтора: звук выключаем сразу, идём A → B (на экране B появляется как обычно) → C (только запоминаем) → обратно на B
        // и включаем звук. B звучит один раз, с начала.
        internal static async void RunSeamlessForward(Snap s)
        {
            peekBusy = true; peekBack = false; peekObserved = null; peekOriginal = null;
            string from = s.MusicTrack; var muted = new List<Audio.MixerSession>(); var clock = System.Diagnostics.Stopwatch.StartNew();
            Action restore = () => { foreach (var x in muted) { try { x.SetMute(false); } catch { } } muted.Clear(); };
            var guard = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) }; // что бы ни случилось, плеер не останется без звука
            guard.Tick += (a, b) => { guard.Stop(); restore(); };
            guard.Start();
            Func<bool> sawNext = () => DemoMode ? live.MusicTrack != peekOriginal : peekObserved != null;
            Func<bool> isBack = () => DemoMode ? live.MusicTrack == peekOriginal : peekBack;
            bool ok = false; long atB = 0, atC = 0;
            try
            {
                foreach (var x in PlayerSessions(s)) if (!x.Muted && x.SetMute(true)) muted.Add(x);
                peekIssuing = true; MediaCmd("next"); peekIssuing = false;                      // A → B
                if (await WaitFor(() => live.MusicTrack != from, 2000))
                {
                    atB = clock.ElapsedMilliseconds; peekOriginal = live.MusicTrack;
                    peekIssuing = true; MediaCmd("next"); peekIssuing = false;                  // B → C: запоминаем, на экран не выводим
                    if (await WaitFor(sawNext, 2000))
                    {
                        atC = clock.ElapsedMilliseconds;
                        peekIssuing = true; MediaCmd("prev"); peekIssuing = false;              // C → B
                        ok = await WaitFor(isBack, 2500);
                    }
                }
            }
            catch (Exception ex) { Log(ex); }
            finally { peekIssuing = false; guard.Stop(); restore(); peekBusy = false; peekOriginal = null; }
            Info("музыка: «вперёд» с заглядыванием " + (ok ? "прошёл без повтора" : "не завершён") + " за " + clock.ElapsedMilliseconds + " мс (B через " + atB + ", C через " + atC + ")");
            if (!ok && Primary != null) Primary.Notify("Музыка", "Не удалось вернуться на нужный трек после проверки следующего. Проверьте, что играет в Spotify.");
            var queued = peekQueued.ToList(); peekQueued.Clear(); // кнопки, нажатые во время прохода, — теперь
            foreach (var q in queued) MediaCmd(q.Item1, q.Item2);
        }

        // ---- Конец трека ----
        // Когда трек доходит до конца сам, следующим стартует B, и повтор его начала при заглядывании был бы слышен. Поэтому, если после B ещё
        // неизвестно, что играет, проход A → B → C → B делается за секунду до конца A, под выключенным звуком: B начинается один раз с начала.
        // Позиция приходит из Windows раз в секунду, поэтому между обновлениями она досчитывается по часам (posAt — момент последнего обновления).
        static DateTime posAt = DateTime.MinValue;
        static string endHandledKey; // трек, для которого проход у конца уже запускали: на один трек — один раз
        static System.Windows.Threading.DispatcherTimer endWatch;

        // до конца осталось от 0,35 до 1,4 с: раньше — отрежем слишком много конца трека, позже — Spotify сам успеет переключиться
        internal static bool TrackEndingNow(bool playing, double pos, double dur, double secondsSincePos)
        {
            if (!playing || double.IsNaN(pos) || double.IsNaN(dur) || dur < 15) return false;
            double left = dur - (pos + secondsSincePos);
            return left >= 0.35 && left <= 1.4;
        }

        static void EnsureEndWatch()
        {
            if (endWatch != null) return;
            endWatch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            endWatch.Tick += (a, b) => CheckTrackEnd();
            endWatch.Start();
        }

        // раз в 200 мс; дешёвые проверки идут первыми, пока не окажется, что трек Spotify играет и вот-вот закончится
        internal static void CheckTrackEnd()
        {
            var s = live;
            if (peekBusy || !s.HasMedia || !s.Playing || double.IsNaN(s.Pos) || posAt == DateTime.MinValue) return;
            if (!TrackEndingNow(s.Playing, s.Pos, s.Dur, (DateTime.UtcNow - posAt).TotalSeconds)) return;
            string key = s.MusicTrack;
            if (key == endHandledKey || !PeekInterceptNow(s)) return; // те же условия, что у нажатия «вперёд»: панель не помогает, после B ничего не известно
            endHandledKey = key;
            RunSeamlessForward(s);
        }
    }
}
