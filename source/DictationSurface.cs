using System;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using static LumaDock.Visuals;

namespace LumaDock;

internal sealed partial class DockWindow
{
    // Окно «Диктовка»: нажатие на шарик начинает запись, второе нажатие останавливает её, Handy распознаёт речь, текст дописывается в заметки
    private void DictationSurface(Window window, DockPanel content, TextBlock subtitle)
    {
        var orb = new VoiceOrb { Width = 112, Height = 112 };
        var time = Text("00:00", 24, true, Paper);
        time.HorizontalAlignment = HorizontalAlignment.Center;
        var status = Text(L10n.T("Press the button and speak."), 13, false, Muted);
        status.TextWrapping = TextWrapping.Wrap;
        status.TextAlignment = TextAlignment.Center;
        status.HorizontalAlignment = HorizontalAlignment.Center;
        status.MaxWidth = 320;
        status.Margin = new(0, 6, 0, 0);
        var result = Text("", 12, false, Paper);
        result.TextWrapping = TextWrapping.Wrap;
        result.TextTrimming = TextTrimming.CharacterEllipsis;
        result.MaxHeight = 52;
        result.MaxWidth = 320;
        result.HorizontalAlignment = HorizontalAlignment.Center;
        result.Margin = new(0, 8, 0, 0);
        var record = Button(orb, () => { }, Brushes.Transparent, 60);
        record.Padding = new(0);
        record.HorizontalAlignment = HorizontalAlignment.Center;
        subtitle.Text = L10n.T("Speech to notes");
        bool busy = false, closed = false;
        bool LocalMode() => P.VoiceEngine == "local";

        // Кнопка скачивания Handy видна, только когда он не найден
        var install = Button(Text(L10n.T("Download Handy"), 13, false, Paper), () => _ = HandyInstaller.DownloadAsync(text => status.Text = text), Brush("#3A3A3C"), 12);
        install.Padding = new(14, 6, 14, 6);
        install.HorizontalAlignment = HorizontalAlignment.Center;
        install.Margin = new(0, 10, 0, 0);
        install.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(install, L10n.T("Download Handy"));

        // Проверка результата помощника: каждую задачу и заметку можно снять флажком, Save записывает оставшееся
        var review = new StackPanel { Margin = new(0, 12, 0, 0), Visibility = Visibility.Collapsed };
        // Текст, разбор которого ещё не сохранён: если окно закроется или начнётся новая диктовка, он целиком уходит в заметки и не пропадает
        string? unsaved = null;
        void KeepUnsaved()
        {
            if (unsaved is null) return;
            Dictation.AppendToNotes(model, unsaved);
            unsaved = null;
        }
        void ShowReview(VoiceAssistant.Plan plan, string raw)
        {
            KeepUnsaved();
            unsaved = raw;
            review.Children.Clear();
            var taskBoxes = new System.Collections.Generic.List<(CheckBox Box, VoiceAssistant.PlanTask Task)>();
            foreach (var task in plan.Tasks)
            {
                string label = task.Due is { } due ? $"{task.Text} — {due:dd.MM.yyyy HH:mm}" : task.Text;
                var box = CheckField(RawText(label, 13, false, Paper), true, Accent(P), task.Text);
                taskBoxes.Add((box, task));
                review.Children.Add(box);
            }
            CheckBox? noteBox = null;
            if (plan.Note.Length > 0)
            {
                noteBox = CheckField(RawText(plan.Note, 13, false, Paper), true, Accent(P), L10n.T("Note"));
                review.Children.Add(noteBox);
            }
            if (review.Children.Count == 0) review.Children.Add(Text(L10n.T("The assistant found nothing to add."), 13, false, Muted));
            var save = Button(Text(L10n.T("Save"), 13, false, Paper), () =>
            {
                foreach (var (box, task) in taskBoxes) if (box.IsChecked == true) model.AddTodo(task.Text, task.Due);
                if (noteBox?.IsChecked == true) Dictation.AppendToNotes(model, plan.Note);
                unsaved = null;
                review.Children.Clear();
                review.Visibility = Visibility.Collapsed;
                status.Text = L10n.T("Saved.");
            }, Brush("#3A3A3C"), 12);
            save.Padding = new(14, 6, 14, 6);
            save.HorizontalAlignment = HorizontalAlignment.Center;
            save.Margin = new(0, 10, 0, 0);
            AutomationProperties.SetName(save, L10n.T("Save"));
            review.Children.Add(save);
            review.Visibility = Visibility.Visible;
        }
        var clock =new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        void Paint()
        {
            orb.SetState(Dictation.IsRecording, true);
            AutomationProperties.SetName(record, L10n.T(Dictation.IsRecording ? "Stop dictation" : "Start dictation"));
            record.ToolTip = AutomationProperties.GetName(record);
            record.IsEnabled = !busy;
        }
        clock.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - Dictation.StartedAt;
            time.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
            if (elapsed.TotalMinutes >= Dictation.MaxMinutes) _ = Toggle(true);
        };
        async System.Threading.Tasks.Task Toggle(bool limit = false)
        {
            if (busy) return;
            busy = true;
            Paint();
            string? wav = null;
            try
            {
                if (!Dictation.IsRecording)
                {
                    if (!Dictation.Fixture && LocalMode() && !LocalVoice.Ready(LocalVoice.Selected(P.VoiceModel)))
                    {
                        status.Text = L10n.T("Download the built-in engine first.");
                        return;
                    }
                    if (!Dictation.Fixture && !LocalMode() && Dictation.HandyPath() is null)
                    {
                        status.Text = L10n.T("Handy was not found. Install Handy and its GigaAM v3 model to recognise speech.");
                        install.Visibility = Visibility.Visible;
                        return;
                    }
                    status.Text = L10n.T("Connecting the microphone…");
                    await Dictation.StartAsync();
                    if (closed) { Dictation.Discard(await Dictation.StopAsync()); return; }
                    time.Text = "00:00";
                    status.Text = L10n.T("Recording… press the button to stop.");
                    clock.Start();
                }
                else
                {
                    clock.Stop();
                    status.Text = L10n.T("Recognising speech…");
                    wav = await Dictation.StopAsync();
                    Paint();
                    string text = LocalMode() && !Dictation.Fixture
                        ? await LocalVoice.TranscribeAsync(LocalVoice.Selected(P.VoiceModel), wav, CancellationToken.None)
                        : await Dictation.TranscribeAsync(wav, CancellationToken.None,
                            (index, total) => status.Text = total > 1 ? L10n.F($"Recognising part {index} of {total}…") : L10n.T("Recognising speech…"));
                    if (text.Length == 0) status.Text = L10n.T("No speech was recognised.");
                    else
                    {
                        result.Text = text;
                        result.ToolTip = text;
                        string provider = P.VoiceProvider;
                        VoiceAssistant.Plan? plan = null;
                        if (provider != "none")
                        {
                            status.Text = L10n.F($"{VoiceAssistant.DisplayName(provider)} is analysing the dictation…");
                            try { plan = await VoiceAssistant.StructureAsync(provider, text, CancellationToken.None); }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                // помощник недоступен или ответил неверно: текст не теряется, он целиком уходит в заметки
                                Dictation.AppendToNotes(model, text);
                                string reason = ex is VoiceAssistant.AssistantException ? ex.Message : "The assistant could not analyse the dictation.";
                                status.Text = L10n.T(reason) + " " + L10n.T("Added to notes.");
                                return;
                            }
                        }
                        // без помощника, при пустом разборе и при закрытом окне продиктованное сразу уходит в заметки целиком
                        if (plan is null || plan.Tasks.Count == 0 && plan.Note.Length == 0 || closed)
                        {
                            Dictation.AppendToNotes(model, text);
                            status.Text = L10n.T(limit ? "Dictation stopped after 30 minutes. Added to notes." : "Added to notes.");
                        }
                        else
                        {
                            ShowReview(plan, text);
                            status.Text = L10n.T("Check the result and press Save.");
                        }
                    }
                }
            }
            catch (Dictation.DictationException ex) { clock.Stop(); status.Text = L10n.T(ex.Message); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                clock.Stop();
                status.Text = L10n.T("The recording could not be recognised.");
            }
            finally
            {
                Dictation.Discard(wav);
                busy = false;
                if (!closed) Paint();
            }
        }
        record.Click += (_, _) => _ = Toggle();
        var notes = Button(Text(L10n.T("Open notes"), 13, false, Paper), () => OpenWidget("Notes"), Brush("#3A3A3C"), 12);
        notes.Padding = new(14, 6, 14, 6);
        notes.HorizontalAlignment = HorizontalAlignment.Center;
        notes.Margin = new(0, 10, 0, 0);

        // Выбор помощника: без него текст уходит в заметки как есть, с ним разбирается на задачи со сроками и заметку
        var provider = ComboField(190, Accent(P), L10n.T("Assistant"));
        foreach (string name in VoiceAssistant.Providers) provider.Items.Add(L10n.T(VoiceAssistant.DisplayName(name)));
        provider.SelectedIndex = Math.Max(0, Array.IndexOf(VoiceAssistant.Providers, P.VoiceProvider));
        provider.SelectionChanged += (_, _) =>
        {
            if (provider.SelectedIndex < 0) return;
            P.VoiceProvider = VoiceAssistant.Providers[provider.SelectedIndex];
            model.Save();
        };
        // Подписи и списки лежат в одной сетке: подписи в левой колонке по одной линии, списки справа одной ширины
        static FrameworkElement Field(string label, FrameworkElement box)
        {
            // MaxWidth, а не Width: в узком окне сетка сжимается вместе с окном и ничего не обрезается
            var grid = new Grid { MaxWidth = 296, HorizontalAlignment = HorizontalAlignment.Stretch };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var caption = Text(label, 13, false, Muted);
            caption.VerticalAlignment = VerticalAlignment.Center;
            box.Width = double.NaN;
            box.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(box, 1);
            grid.Children.Add(caption);
            grid.Children.Add(box);
            return grid;
        }
        var providerRow = Field(L10n.T("Assistant"), provider);
        providerRow.Margin = new(0, 10, 0, 0);

        // Распознавание: Handy или встроенный движок (скачивается по кнопке, работает без Handy)
        var engine = ComboField(190, Accent(P), L10n.T("Recognition"));
        engine.Items.Add("Handy");
        engine.Items.Add(L10n.T("Built-in engine"));
        engine.SelectedIndex = LocalMode() ? 1 : 0;
        var engineRow = Field(L10n.T("Recognition"), engine);
        engineRow.Margin = new(0, 8, 0, 0);
        var models = ComboField(190, Accent(P), L10n.T("Model"));
        foreach (var item in LocalVoice.Models) models.Items.Add(L10n.T(item.Label));
        models.SelectedIndex = Array.IndexOf(LocalVoice.Models, LocalVoice.Selected(P.VoiceModel));
        var download = Button(Text(L10n.T("Download and enable"), 13, false, Paper), () => { }, Brush("#3A3A3C"), 12);
        download.Padding = new(14, 6, 14, 6);
        download.HorizontalAlignment = HorizontalAlignment.Center;
        download.Margin = new(0, 8, 0, 0);
        AutomationProperties.SetName(download, L10n.T("Download and enable"));
        var localPanel = Stack(Field(L10n.T("Model"), models), download);
        localPanel.Margin = new(0, 8, 0, 0);
        void PaintLocal()
        {
            localPanel.Visibility = LocalMode() ? Visibility.Visible : Visibility.Collapsed;
            download.Visibility = LocalVoice.Ready(LocalVoice.Selected(P.VoiceModel)) ? Visibility.Collapsed : Visibility.Visible;
        }
        engine.SelectionChanged += (_, _) =>
        {
            if (engine.SelectedIndex < 0) return;
            P.VoiceEngine = engine.SelectedIndex == 1 ? "local" : "handy";
            model.Save();
            PaintLocal();
        };
        models.SelectionChanged += (_, _) =>
        {
            if (models.SelectedIndex < 0) return;
            P.VoiceModel = LocalVoice.Models[models.SelectedIndex].Id;
            model.Save();
            PaintLocal();
        };
        download.Click += async (_, _) =>
        {
            if (!LocalVoice.Supported) { status.Text = L10n.T("The built-in engine needs 64-bit Windows on an Intel or AMD processor."); return; }
            var chosen = LocalVoice.Models[Math.Max(0, models.SelectedIndex)];
            download.IsEnabled = models.IsEnabled = engine.IsEnabled = false;
            try
            {
                await LocalVoice.PrepareAsync(chosen, text => status.Text = text, CancellationToken.None);
                P.VoiceModel = chosen.Id;
                model.Save();
                status.Text = L10n.T("The model is ready. You can start recording.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                status.Text = L10n.T("The engine could not be downloaded. Check the connection and try again.");
            }
            finally
            {
                if (!closed) { download.IsEnabled = models.IsEnabled = engine.IsEnabled = true; PaintLocal(); }
            }
        };
        PaintLocal();

        var panel = Stack(record, time, status, result, install, engineRow, localPanel, providerRow, review, notes);
        panel.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(Scroll(panel));
        window.Closed += async (_, _) =>
        {
            closed = true;
            KeepUnsaved();
            clock.Stop();
            if (Dictation.IsRecording) Dictation.Discard(await Dictation.StopAsync());
        };
        Paint();
        // открыто сочетанием клавиш: запись начинается сразу
        if (StartDictationOnOpen)
        {
            StartDictationOnOpen = false;
            window.Dispatcher.BeginInvoke(() => _ = Toggle());
        }
    }
}
