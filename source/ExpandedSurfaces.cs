using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using static LumaDock.Visuals;

namespace LumaDock;

internal sealed partial class DockWindow
{
    private bool DrawExpanded(string id, Window window, DockPanel content)
    {
        switch (id)
        {
            case "Day Timeline": Timeline(content); break;
            case "Screen Time": ScreenTime(content, window); break;
            case "Storage": Storage(content); break;
            case "Recycle Bin": RecycleBin(content); break;
            case "Brightness": Brightness(content, window); break;
            case "Network": NetworkSurface(content, window); break;
            case "Recently Opened": Recent(content); break;
            case "Commands": Commands(content); break;
            case "Screenshot Capture": CaptureSurface(content, window); break;
            case "Wallpaper Gallery": WallpaperSurface(content, window); break;
            case "Currency": case "Crypto": case "GitHub": case "GitHub Overview": case "GitHub Activity": case "LeetCode Stats":
                Quote(content, window, id);
                break;
            default:
                if (AiMetrics.Supports(id)) { AiSurface(content, window, id); break; }
                if (!ConnectedData.Supports(id)) return false;
                ConnectedSurface(content, window, id);
                break;
        }
        return true;
    }

    private void Toast(string title, string message)
    {
        var note = new Window { Width = 336, Height = 93, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, ShowActivated = false, Topmost = true, AllowsTransparency = true, Background = Brushes.Transparent,
            Owner = this };
        var titleText = Text(title, 14, true);
        var body = RawText(message, 13, false, Muted);
        body.TextWrapping = TextWrapping.Wrap;
        var block = Card(Right(Stack(titleText, body), CloseButton(note)), 17, SurfaceInk, Line);
        block.Padding = new(14);
        note.Content = block;
        note.Left = SystemParameters.WorkArea.Right - note.Width - 16;
        note.Top = Math.Max(SystemParameters.WorkArea.Top, BarTop - note.Height - 12);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) => note.Close();
        note.Closed += (_, _) => timer.Stop();
        note.Show();
        timer.Start();
    }

    private void AutomationSettings(StackPanel parent)
    {
        var settings = P.Automations;
        var rules = SettingsGroup(parent);
        void Rule(string name, string summary, bool enabled, Action<bool> save)
        {
            var labels = Stack(Text(name, 14, true), Text(summary, 12, false, Muted));
            labels.VerticalAlignment = VerticalAlignment.Center;
            SettingsRow(rules, labels, ToggleSwitch(name, enabled, save));
        }
        Rule("Subscription reminders", "A reminder before your plan renews", settings.Subscriptions, value => settings.Subscriptions = value);
        Rule("Meeting mode", "Pause music and send a reminder five minutes before", settings.Meeting, value => settings.Meeting = value);
        Rule("AI fallback", "Suggest another AI when a recorded limit runs out", settings.AiFallback, value => settings.AiFallback = value);
        Rule("Focus music", "Pomodoro breaks pause the music, focus resumes it", settings.FocusMusic, value => settings.FocusMusic = value);
        Rule("Work inbox", "New assignments from the connected Linear account", settings.WorkInbox, value => settings.WorkInbox = value);
        Rule("Milestones", "Notify when connected revenue passes a milestone", settings.Milestones, value => settings.Milestones = value);
        Rule("Health alerts", "Warn about high temperatures and full disks", settings.Health, value => settings.Health = value);
        Rule("Sunset dark mode", "Dark theme at sunset, light at sunrise", settings.Sunset, value => settings.Sunset = value);
        var padding = new Border { Height = 11 };
        parent.Children.Add(padding);
        var quiet = SettingsGroup(parent);
        var range = RowStack(ClockStepper(settings.QuietFrom, value => settings.QuietFrom = value),
            Text(" – ", 13, false, Muted), ClockStepper(settings.QuietTo, value => settings.QuietTo = value),
            ToggleSwitch("Quiet hours", settings.Quiet, value => settings.Quiet = value));
        SettingsRow(quiet, Text("Quiet hours", 14, true), range);
        var connections = SettingsGroup(parent, "Manage");
        SettingsRow(connections, Text("Renewal dates", 14, true), Button("Open", Renewals));
        SettingsRow(connections, Text("Work inbox", 14, true), Button("Linear", () => OpenWidget("Linear")));

        FrameworkElement ClockStepper(string current, Action<string> set)
        {
            var label = Text(current, 13, true);
            label.Width = 43;
            label.TextAlignment = TextAlignment.Center;
            void Move(int direction)
            {
                TimeSpan.TryParseExact(label.Text, "hh\\:mm", CultureInfo.InvariantCulture, out var time);
                var minutes = ((int)time.TotalMinutes + direction * 30 + 1440) % 1440;
                label.Text = TimeSpan.FromMinutes(minutes).ToString("hh\\:mm", CultureInfo.InvariantCulture);
                set(label.Text);
                model.Save(false);
            }
            var pill = Card(RowStack(Button("−", () => Move(-1), System.Windows.Media.Brushes.Transparent),
                label, Button("+", () => Move(1), System.Windows.Media.Brushes.Transparent)), 15, Brush("#353537"));
            pill.Margin = new(0, 0, 5, 0);
            return pill;
        }
    }

    private void Renewals() => Surface("Renewal dates", 500, 370, false, (window, shell) =>
    {
        var list = Stack();
        list.Margin = new(12);
        var title = Input("Subscription");
        var date = DateField("Renewal date", 132, Accent(P), DateTime.Today.AddDays(1));
        void Draw()
        {
            list.Children.Clear();
            list.Children.Add(Right(Text("Renewal dates", 18, true), CloseButton(window)));
            list.Children.Add(Right(title, RowStack(date, Button("Add", () =>
            {
                if (string.IsNullOrWhiteSpace(title.Text) || !date.SelectedDate.HasValue) return;
                P.Automations.Renewals.Add(new Subscription { Name = title.Text.Trim(), Next = date.SelectedDate.Value.AddHours(9) });
                model.Save(false);
                title.Clear();
                Draw();
            }))));
            foreach (var entry in P.Automations.Renewals.OrderBy(item => item.Next))
                AddRow(list, $"{entry.Name} · {entry.Next:d}", Button("Remove", () => { P.Automations.Renewals.Remove(entry); model.Save(false); Draw(); }));
        }
        shell.Child = Scroll(list);
        Draw();
    });

    // Название приложения-источника для подписи: «Spotify.exe» -> «Spotify», «Microsoft.ZuneMusic_8weky!App» -> «ZuneMusic»
    internal static string SourceName(string source)
    {
        string name = source.Split('!')[0];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Contains('_')) name = name[..name.IndexOf('_')];
        if (name.Contains('.')) name = name[(name.LastIndexOf('.') + 1)..];
        return name;
    }

    private static string ClockText(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";
    }

    private void MediaSurface(DockPanel content, Window window)
    {
        // Крупная обложка с её размытой копией на фоне карточки; справа название, исполнитель и источник; ниже полоса времени с перемоткой и кнопки
        var backdrop = new Image { Stretch = Stretch.UniformToFill, Opacity = .55, Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 32 } };
        RenderOptions.SetBitmapScalingMode(backdrop, BitmapScalingMode.HighQuality);
        var shade = new Border { Background = new LinearGradientBrush(Color.FromArgb(0x40, 0x14, 0x14, 0x16), Color.FromArgb(0xE8, 0x14, 0x14, 0x16), 90) };
        var cover = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(cover, BitmapScalingMode.HighQuality);
        var placeholder = Symbol("\uE8D6", 44, Muted);
        var coverFrame = new Grid { Width = 150, Height = 150, Background = Brush("#2A2A2D"), Clip = new RectangleGeometry(new Rect(0, 0, 150, 150), 14, 14) };
        coverFrame.Children.Add(placeholder);
        coverFrame.Children.Add(cover);
        var coverShadow = new Border { Child = coverFrame, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Opacity = .6 } };
        var title = RawText("", 21, true, Paper);
        title.TextWrapping = TextWrapping.Wrap;
        title.MaxHeight = 58;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var artist = RawText("", 14, false, Muted);
        artist.TextTrimming = TextTrimming.CharacterEllipsis;
        artist.Margin = new(0, 3, 0, 0);
        var sourceText = RawText("", 11, true, Paper);
        var source = Card(sourceText, 9, Brush("#33FFFFFF"));
        source.Padding = new(9, 2, 9, 3);
        source.HorizontalAlignment = HorizontalAlignment.Left;
        source.Margin = new(0, 10, 0, 0);
        var info = Stack(title, artist, source);
        info.VerticalAlignment = VerticalAlignment.Center;
        info.Margin = new(18, 0, 0, 0);
        var head = new DockPanel();
        DockPanel.SetDock(coverShadow, Dock.Left);
        head.Children.Add(coverShadow);
        head.Children.Add(info);

        var bar = FilledMeter(Brushes.White, 5, "Now Playing", Brush("#40FFFFFF"));
        bar.Maximum = 1;
        bar.VerticalAlignment = VerticalAlignment.Center;
        var hit = new Grid { Height = 22, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, Margin = new(0, 14, 0, 0) };
        hit.Children.Add(bar);
        var elapsed = RawText("0:00", 12, false, Muted);
        var total = RawText("0:00", 12, false, Muted);
        var times = Right(elapsed, total);
        hit.MouseLeftButtonUp += (_, e) =>
        {
            var track = model.Media.Current;
            if (track is null || track.Duration <= 1 || hit.ActualWidth <= 0) return;
            _ = model.Media.SeekAsync(Math.Clamp(e.GetPosition(hit).X / hit.ActualWidth, 0, 1) * track.Duration);
        };

        Button Round(string glyph, double glyphSize, double size, Brush background, byte key, string name)
        {
            var button = Button(Symbol(glyph, glyphSize, Brushes.White), () => WindowsShell.Key(key), background, size / 2);
            button.Width = button.Height = size;
            button.Padding = new(0);
            button.Margin = new(7, 0, 7, 0);
            System.Windows.Automation.AutomationProperties.SetName(button, L10n.T(name));
            button.ToolTip = L10n.T(name);
            return button;
        }
        var previous = Round("\uE892", 16, 44, Brush("#2EFFFFFF"), 0xB1, "Prev");
        var play = Round("\uE768", 22, 56, Accent(P), 0xB3, "Play");
        var next = Round("\uE893", 16, 44, Brush("#2EFFFFFF"), 0xB0, "Next");
        var buttons = RowStack(previous, play, next);
        buttons.HorizontalAlignment = HorizontalAlignment.Center;
        buttons.Margin = new(0, 12, 0, 0);

        var body = Stack(head, hit, times, buttons);
        body.Margin = new(20);
        body.VerticalAlignment = VerticalAlignment.Center;
        var layers = new Grid { ClipToBounds = true };
        layers.Children.Add(backdrop);
        layers.Children.Add(shade);
        layers.Children.Add(body);
        layers.SizeChanged += (_, e) => layers.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 16, 16);
        var card = Card(layers, 16, Brush("#1A1A1C"));
        content.Children.Add(card);

        Live(window, () =>
        {
            var track = model.Media.Current;
            bool known = !string.IsNullOrWhiteSpace(track?.Title);
            title.Text = known ? track!.Title : L10n.T("Now Playing");
            artist.Text = known ? track!.Artist : L10n.T("No active media");
            source.Visibility = known && !string.IsNullOrWhiteSpace(track!.Source) ? Visibility.Visible : Visibility.Collapsed;
            sourceText.Text = known ? SourceName(track!.Source) : "";
            var art = model.Media.Artwork;
            if (!ReferenceEquals(cover.Source, art))
            {
                cover.Source = art;
                backdrop.Source = art;
                placeholder.Visibility = art is null ? Visibility.Visible : Visibility.Collapsed;
            }
            bool playing = track?.State == "Playing";
            ((TextBlock)play.Content).Text = playing ? "\uE769" : "\uE768";
            string playName = L10n.T(playing ? "Pause" : "Play");
            System.Windows.Automation.AutomationProperties.SetName(play, playName);
            play.ToolTip = playName;
            double length = track?.Duration ?? 0;
            bool timed = known && length > 1;
            hit.Visibility = times.Visibility = timed ? Visibility.Visible : Visibility.Collapsed;
            if (!timed) return;
            double position = Math.Clamp(track!.Position + (playing ? (DateTimeOffset.Now - model.Media.ReadAt).TotalSeconds : 0), 0, length);
            bar.Value = position / length;
            elapsed.Text = ClockText(position);
            total.Text = ClockText(length);
        });
    }

    private void Timeline(DockPanel content)
    {
        var now = DateTime.Today;
        var list = Stack(Text(now.ToString(L10n.Pair("dddd, MMMM d", "dddd, d MMMM"), L10n.Culture), 20, true));
        var events = P.Events.Where(item => item.Start.Date == now || item.Start < now && item.End > now).OrderBy(item => item.Start).ToArray();
        foreach (var entry in events)
        {
            var row = Card(Right(RawText(entry.Title, 14, true), Text(entry.AllDay ? "All day" : entry.Start.ToString("HH:mm"), 13, true, Accent(P))),
                10, Row);
            row.Padding = new(11, 8, 11, 8);
            row.Margin = new(0, 7, 0, 0);
            list.Children.Add(row);
        }
        if (events.Length == 0) list.Children.Add(Text("Nothing planned", 13, false, Muted));
        list.Children.Add(Button("Calendar", () => OpenWidget("Calendar")));
        content.Children.Add(Scroll(list));
    }

    private void ScreenTime(DockPanel content, Window window)
    {
        var list = Stack();
        var status = Text("", 13, false, Muted);
        var total = Text("", 28, true);
        var stats = new UniformGrid { Rows = 1, Margin = new(0, 5, 0, 11) };
        var focus = AddStat("Longest focus");
        var switchesStat = AddStat("App switches");
        var firstUse = AddStat("First use");
        var chart = new UniformGrid { Rows = 1, Height = 80, Margin = new(0, 7, 0, 4) };
        var axis = new UniformGrid { Rows = 1 };
        var apps = Stack();
        bool week = false;
        bool websites = false;
        void Paint()
        {
            var today = DateTime.Today;
            DateTime first = week ? today.AddDays(-((int)today.DayOfWeek + 6) % 7) : today;
            var slices = model.Activity.Between(first, today.AddDays(1));
            var hours = week
                ? Enumerable.Range(0, 7).Select(day => slices.Where(slice => slice.Start.LocalDateTime.Date == first.AddDays(day)).Sum(slice => slice.Seconds)).ToArray()
                : Enumerable.Range(0, 24).Select(hour => slices.Where(slice => slice.Start.LocalDateTime.Hour == hour).Sum(slice => slice.Seconds)).ToArray();
            var highlights = slices.Select(slice => (Key: websites ? slice.Site : slice.App, slice.Seconds))
                .Where(item => !string.IsNullOrEmpty(item.Key)).GroupBy(item => item.Key)
                .OrderByDescending(group => group.Sum(item => item.Seconds)).Take(8).ToArray();
            status.Text = $"{L10n.T(week ? "This week" : "Today")} · {L10n.T(P.TrackScreenTime ? "Recording" : "Paused")}";
            total.Text = Widgets.CompactDuration(slices.Sum(slice => slice.Seconds));
            var runs = new List<(string App, DateTimeOffset End, int Duration)>();
            foreach (var slice in slices)
            {
                if (runs.Count > 0 && runs[^1].App == slice.App && Math.Abs((slice.Start - runs[^1].End).TotalSeconds) <= 1)
                    runs[^1] = (slice.App, slice.Start.AddSeconds(slice.Seconds), runs[^1].Duration + slice.Seconds);
                else runs.Add((slice.App, slice.Start.AddSeconds(slice.Seconds), slice.Seconds));
            }
            var longest = runs.Count == 0 ? default : runs.MaxBy(run => run.Duration);
            int switches = slices.Zip(slices.Skip(1)).Count(pair => pair.First.App != pair.Second.App);
            focus.Text = longest.Duration > 0 ? Widgets.CompactDuration(longest.Duration) : "—";
            focus.ToolTip = longest.App;
            switchesStat.Text = switches.ToString(L10n.Culture);
            firstUse.Text = slices.Length == 0 ? "—" : slices[0].Start.LocalDateTime.ToString("HH:mm", L10n.Culture);
            chart.Children.Clear();
            axis.Children.Clear();
            foreach (string tick in week
                         ? Enumerable.Range(0, 7).Select(day => first.AddDays(day).ToString("ddd", L10n.Culture))
                         : new[] { "00", "06", "12", "18" })
                axis.Children.Add(Text(tick, 10, false, Muted));
            int peak = Math.Max(1, hours.Max());
            for (int index = 0; index < hours.Length; index++)
            {
                var bar = Card(null, 4, hours[index] == 0 ? Line : Accent(P));
                bar.Margin = new(2, 0, 2, 0);
                bar.Height = 2 + 72 * (double)hours[index] / peak;
                bar.VerticalAlignment = VerticalAlignment.Bottom;
                bar.ToolTip = $"{(week ? first.AddDays(index).ToString("ddd", L10n.Culture) : $"{index:00}:00")} · {Widgets.CompactDuration(hours[index])}";
                chart.Children.Add(bar);
            }
            apps.Children.Clear();
            foreach (var app in highlights)
                AddRow(apps, app.Key!, Text(Widgets.CompactDuration(app.Sum(item => item.Seconds)), 13, true, Muted), localizeTitle: false);
            if (websites && highlights.Length == 0)
                apps.Children.Add(Text(P.TrackWebsites ? "No domains recorded" : "Website tracking is off", 13, false, Muted));
        }
        list.Children.Add(Right(status, RowStack(Button("Day", () => { week = false; Paint(); }), Button("Week", () => { week = true; Paint(); }))));
        list.Children.Add(total);
        list.Children.Add(chart);
        list.Children.Add(axis);
        list.Children.Add(stats);
        list.Children.Add(Right(Text("MOST USED", 11, true, Muted), RowStack(
            Button("Apps", () => { websites = false; Paint(); }),
            Button("Websites", () => { websites = true; Paint(); }))));
        list.Children.Add(apps);
        AddToggle(list, "Screen time tracking", P.TrackScreenTime, enabled =>
        {
            P.TrackScreenTime = enabled;
            if (!enabled) { model.Activity.Stop(); browserSites.Reset(); }
            Paint();
        });
        AddToggle(list, "Track website domains", P.TrackWebsites, enabled =>
        {
            P.TrackWebsites = enabled;
            if (!enabled) { model.Activity.StopSites(); browserSites.Reset(); }
            Paint();
        });
        AddRow(list, "Website history", Button("Clear…", () =>
        {
            if (MessageBox.Show(window, L10n.T("Clear saved website domains?"), L10n.T("Screen Time"), MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes) return;
            model.Activity.ClearSites();
            browserSites.Reset();
            Paint();
        }));
        content.Children.Add(Scroll(list));
        Paint();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) => Paint();
        window.Closed += (_, _) => timer.Stop();
        timer.Start();

        TextBlock AddStat(string name)
        {
            var value = Text("—", 15, true);
            var card = Card(Stack(Text(name, 11, false, Muted), value), 10, Row);
            card.Padding = new(10, 7, 10, 7);
            card.Margin = new(0, 0, 5, 0);
            stats.Children.Add(card);
            return value;
        }
    }

    private void EditCalendar(DateTime selected, Action refresh, string? defaultTime = null) => Surface("New event", 500, 270, false, (window, shell) =>
    {
        var title = Input("Event name");
        var location = Input("Location");
        var day = DateField("Event date", 132, Accent(P), selected);
        var time = Input("HH:mm", defaultTime ?? "09:00", "Event time");
        var length = Input("Minutes", "60");
        time.Width = length.Width = 64;
        var status = Text("", 12, false, Muted);
        System.Windows.Automation.AutomationProperties.SetName(day, L10n.T("Event date"));
        bool invalidDate = false;
        day.DateValidationError += (_, e) =>
        {
            e.ThrowException = false;
            invalidDate = true;
            status.Text = L10n.T("Enter a valid date.");
        };
        day.SelectedDateChanged += (_, _) => { invalidDate = false; status.Text = ""; };
        var row = RowStack(day, time, length);
        row.Margin = new(0, 4, 0, 4);
        var popover = Stack(Right(Text("New event", 17, true), CloseButton(window)), title, location, row, status,
            Button("Save", () =>
            {
                if (string.IsNullOrWhiteSpace(title.Text) || invalidDate || !day.SelectedDate.HasValue ||
                    !TryReadClock(time.Text, out var at)
                    || !int.TryParse(length.Text, out var minutes) || minutes is < 1 or > 1440)
                { status.Text = L10n.T("Enter a name, date, time and duration in minutes."); return; }
                var start = day.SelectedDate.Value + at;
                P.Events.Add(new CalendarEvent { Title = title.Text.Trim(), Location = location.Text.Trim(), Start = start, End = start.AddMinutes(minutes) });
                model.Save();
                window.Close();
                refresh();
            }, Accent(P)));
        popover.Margin = new(14);
        shell.Child = popover;
    });

    private void CalendarFeeds(Action refresh) => Surface("Calendars", 570, 340, false, (window, shell) =>
    {
        var box = Input("HTTPS calendar URL");
        var status = Text("", 12, false, Muted);
        var list = Stack();
        async Task Import(string source)
        {
            status.Text = L10n.T("Loading…");
            try
            {
                var entries = await Calendars.Load(source);
                if (!P.CalendarSources.Contains(source)) P.CalendarSources.Add(source);
                P.Events.RemoveAll(item => item.Source == source);
                P.Events.AddRange(entries);
                status.Text = L10n.F($"{entries.Length} events");
                model.Save(false);
                Render();
                refresh();
            }
            catch (Exception ex) when (LiveData.IsDataError(ex) || ex is IOException or UnauthorizedAccessException)
            { status.Text = L10n.T("Calendar could not be loaded."); }
        }
        void Render()
        {
            list.Children.Clear();
            foreach (string source in P.CalendarSources)
            {
                string label = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? uri.Host : Path.GetFileName(source);
                AddRow(list, label, RowStack(Button("Refresh", () => _ = Import(source)), Button("Remove", () =>
                {
                    P.CalendarSources.Remove(source);
                    P.Events.RemoveAll(item => item.Source == source);
                    model.Save(false);
                    Render();
                    refresh();
                })));
            }
        }
        var open = Button("Import ICS", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = L10n.T("Calendar (*.ics)") + "|*.ics" };
            if (dialog.ShowDialog(window) == true) _ = Import(dialog.FileName);
        });
        var panel = Stack(Right(Text("Calendars", 18, true), CloseButton(window)),
            Right(box, Button("Add", () => _ = Import(box.Text.Trim()))), open, status, list);
        panel.Margin = new(13);
        shell.Child = Scroll(panel);
        Render();
    });

    private void Quote(DockPanel content, Window window, string id)
    {
        var input = Input(id == "Currency" ? "EUR/USD" : id == "Crypto" ? "BTCUSDT" : id == "LeetCode Stats" ? "LeetCode username" : "GitHub username",
            P.DataInputs.GetValueOrDefault(id) ?? "");
        var stat = Text("—", 25, true);
        var secondary = Text("", 13, false, Muted);
        void Paint()
        {
            var quote = model.Data.Quotes.GetValueOrDefault(id);
            stat.Text = L10n.Metric(quote?.Value ?? "—");
            secondary.Text = L10n.T(model.Data.Errors.GetValueOrDefault(id) ?? (quote is null ? "" : $"{quote.Title} · {L10n.Metric(quote.Detail)}"));
        }
        async Task Get(bool save)
        {
            string term = input.Text.Trim();
            if (term.Length == 0) return;
            if (save) { P.DataInputs[id] = term; model.Save(false); }
            secondary.Text = L10n.T("Loading…");
            await model.Data.RefreshQuote(id, term, save);
            Paint();
        }
        content.Children.Add(Stack(stat, secondary, Right(input, Button("Refresh", () => _ = Get(true))), Button("Open source", () =>
        {
            if (model.Data.Quotes.TryGetValue(id, out var quote)) WindowsShell.Launch(quote.Url);
        })));
        Paint();
        if (input.Text.Length > 0) _ = Get(false);
    }

    private void Storage(DockPanel content)
    {
        var panel = Stack();
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Removable))
        {
            var progress = FilledMeter(Accent(P), 8, "Storage");
            progress.Value = drive.TotalSize == 0 ? 0 : 100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
            progress.Margin = new(0, 2, 0, 12);
            panel.Children.Add(Right(Text(L10n.F($"{drive.Name}    {Bytes(drive.AvailableFreeSpace)} free of {Bytes(drive.TotalSize)}"), 13, true),
                Button("Open", () => WindowsShell.Launch(drive.RootDirectory.FullName))));
            panel.Children.Add(progress);
        }
        panel.Children.Add(Button("Storage settings", () => WindowsShell.Launch("ms-settings:storagesense")));
        content.Children.Add(Scroll(panel));
    }

    private void RecycleBin(DockPanel content)
    {
        var status = Text("", 20, true);
        void Update()
        {
            var items = WindowsShell.RecycleBin();
            status.Text = items is null ? L10n.T("Recycle Bin unavailable") : L10n.F($"{items.Value.Items} items · {Bytes(items.Value.Bytes)}");
        }
        Update();
        content.Children.Add(Stack(status, Button("Open Recycle Bin", () => WindowsShell.Launch("shell:RecycleBinFolder")),
            Button("Empty Recycle Bin…", () => { WindowsShell.EmptyRecycleBin(handle); Update(); })));
    }

    private static void Recent(DockPanel content)
    {
        string path = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        var list = Stack();
        if (Directory.Exists(path))
            foreach (var item in new DirectoryInfo(path).EnumerateFiles("*.lnk").OrderByDescending(item => item.LastWriteTime).Take(40))
                list.Children.Add(Button(RawText(Path.GetFileNameWithoutExtension(item.Name), 14, true), () => WindowsShell.Launch(item.FullName)));
        if (list.Children.Count == 0) list.Children.Add(Text("No recent items", 13, false, Muted));
        content.Children.Add(Scroll(list));
    }

    private static void Commands(DockPanel content)
    {
        var links = Stack();
        foreach (var (label, target) in new[] { ("Windows Terminal", "wt.exe"), ("PowerShell", "powershell.exe"),
            ("Task Manager", "taskmgr.exe"), ("File Explorer", "explorer.exe"), ("Windows Settings", "ms-settings:") })
            links.Children.Add(Button(label, () => WindowsShell.Launch(target)));
        content.Children.Add(links);
    }

    private void Brightness(DockPanel content, Window window)
    {
        var state = Text("Detecting monitor…", 13, false, Muted);
        var slider = FilledSlider(Accent(P), 0, 18, "Brightness");
        slider.IsEnabled = false;
        slider.Margin = new(0, 9, 0, 10);
        content.Children.Add(Stack(state, slider, Button("Display settings", () => WindowsShell.Launch("ms-settings:display"))));
        _ = Populate();
        async Task Populate()
        {
            var result = await Task.Run(() => WindowsShell.Brightness(handle));
            if (!window.IsLoaded) return;
            state.Text = result.Supported ? L10n.F($"Brightness  {result.Brightness:P0}") : L10n.T("Monitor brightness control is unavailable.");
            model.BrightnessLevel = result.Supported ? result.Brightness : null;
            slider.Value = 100 * result.Brightness;
            if (!result.Supported) return;
            slider.IsEnabled = true;
            var apply = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            apply.Tick += async (_, _) =>
            {
                apply.Stop();
                double value = slider.Value / 100;
                var updated = await Task.Run(() => WindowsShell.Brightness(handle, value));
                model.BrightnessLevel = updated.Supported ? updated.Brightness : null;
                if (window.IsLoaded) state.Text = updated.Supported ? L10n.F($"Brightness  {updated.Brightness:P0}") : L10n.T("Brightness unavailable");
            };
            window.Closed += (_, _) => apply.Stop();
            slider.ValueChanged += (_, _) => { apply.Stop(); apply.Start(); };
        }
    }

    private void NetworkSurface(DockPanel content, Window window)
    {
        var main = Stack();
        var name = Text("", 14, true);
        name.Margin = new(5, 5, 0, 10);
        main.Children.Add(name);
        var face = Widgets.Face(Widgets.Available["Network"], model, true, out var update);
        var chart = Card(face, 14, SurfaceInk, Line);
        chart.Padding = new(12);
        main.Children.Add(chart);
        main.Children.Add(Button("Network settings", () => WindowsShell.Launch("ms-settings:network-status")));
        content.Children.Add(main);
        Live(window, () => { name.Text = L10n.T(model.Network.Adapter); update(); });
    }

    private static string Bytes(long value) => value >= 1024L * 1024 * 1024
        ? L10n.F($"{value / (1024.0 * 1024 * 1024):0.0} GB") : L10n.F($"{value / (1024.0 * 1024):0.0} MB");
}
