using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static LumaDock.Visuals;

namespace LumaDock;

internal sealed partial class DockWindow
{
    private Window Surface(string key, double width, double height, bool popover, Action<Window, Border> draw,
        FrameworkElement? anchor = null, bool closeOnDeactivate = true)
    {
        CloseThumbnail();
        if (surfaces.TryGetValue(key, out var existing))
        {
            existing.Activate();
            return existing;
        }
        var parent = popover ? this : surfaces.Values.FirstOrDefault(surface => surface.IsActive && surface.Owner == this) ?? this;
        var window = new Window
        {
            Title = key.StartsWith("Connect · ", StringComparison.Ordinal) ? L10n.F($"Connect · {L10n.T(key[10..])}") : L10n.T(key),
            Language = System.Windows.Markup.XmlLanguage.GetLanguage(L10n.Culture.IetfLanguageTag),
            Width = Math.Min(width, SystemParameters.WorkArea.Width - 12),
            Height = Math.Min(height, SystemParameters.WorkArea.Height - 12), WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent,
            ShowInTaskbar = false, Owner = parent, Topmost = true, UseLayoutRounding = true
        };
        var shell = Card(null, 23, SurfaceInk, Line);
        var frame = Card(shell, 28, FrameGradient());
        frame.Padding = new(14);
        window.Content = frame;
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) window.Close(); };
        // Окно можно перетащить за любое место, которое не обработало нажатие само (кнопки, поля, ползунки его «съедают»)
        window.MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled || e.ButtonState != MouseButtonState.Pressed || OnControl(e.OriginalSource as DependencyObject)) return;
            try { window.DragMove(); } catch (InvalidOperationException) { }
        };
        var area = SystemParameters.WorkArea;
        double screenX = area.Left + area.Width / 2;
        if (anchor is { IsLoaded: true })
            screenX = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0)).X / VisualTreeHelper.GetDpi(this).DpiScaleX;
        window.Left = Math.Clamp(screenX - window.Width / 2, area.Left, Math.Max(area.Left, area.Right - window.Width));
        window.Top = popover ? Math.Clamp(BarTop - window.Height - 3, area.Top, Math.Max(area.Top, area.Bottom - window.Height))
            : area.Top + (area.Height - window.Height) / 2;
        surfaces.Add(key, window);
        ActivityLog.Record("окно открыто", key);
        window.Closed += (_, _) => { surfaces.Remove(key); ActivityLog.Record("окно закрыто", key); };
        long lastMove = 0; // при перетаскивании событий сотни: пишется только начало движения
        window.LocationChanged += (_, _) =>
        {
            if (Environment.TickCount64 - lastMove > 1000) ActivityLog.Record("окно перемещено", key);
            lastMove = Environment.TickCount64;
        };
        if (popover && closeOnDeactivate && !smoke)
        {
            void CloseByFocus()
            {
                if (window.IsVisible && !window.OwnedWindows.Cast<Window>().Any(child => child.IsVisible))
                {
                    if (IsMouseOver) closedByFocus = (key, Environment.TickCount64);
                    window.Close();
                }
            }
            window.Deactivated += (_, _) => Dispatcher.BeginInvoke(() => { if (!window.IsActive) CloseByFocus(); });
            // Дополнительно: щелчок по доку не снимает активность с карточки (док не берёт фокус), и Deactivated не приходит
            PopoverWatch.Attach(window, CloseByFocus);
        }
        draw(window, shell);
        window.Show();
        return window;
    }

    private static Button CloseButton(Window window)
    {
        var button = Button(Symbol("\uE711", 11, Muted), window.Close, Brush("#303030"), 16);
        System.Windows.Automation.AutomationProperties.SetName(button, L10n.T("Close"));
        button.Padding = new(0);
        button.Width = button.Height = 29;
        return button;
    }

    private static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };

    private static TextBox Input(string placeholder, string initial = "", string? label = null)
    {
        var textBox = new TextBox { Text = initial, Background = Row, Foreground = Paper, CaretBrush = Paper,
            BorderBrush = Line, BorderThickness = new(1), Padding = new(9, 6, 9, 6), FontFamily = Font, FontSize = 14 };
        System.Windows.Automation.AutomationProperties.SetName(textBox, L10n.T(label ?? placeholder));
        var border = new FrameworkElementFactory(typeof(Border), "InputFrame");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
        var grid = new FrameworkElementFactory(typeof(Grid));
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        var watermark = new FrameworkElementFactory(typeof(TextBlock), "Watermark");
        watermark.SetValue(TextBlock.TextProperty, L10n.T(placeholder));
        watermark.SetValue(TextBlock.ForegroundProperty, Muted);
        watermark.SetValue(UIElement.IsHitTestVisibleProperty, false);
        watermark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        watermark.SetBinding(FrameworkElement.MarginProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
        grid.AppendChild(host);
        grid.AppendChild(watermark);
        border.AppendChild(grid);
        var template = new ControlTemplate(typeof(TextBox)) { VisualTree = border };
        var empty = new Trigger { Property = TextBox.TextProperty, Value = "" };
        empty.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Watermark"));
        template.Triggers.Add(empty);
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#5A5A5E"), "InputFrame"));
        template.Triggers.Add(hover);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#6995CA"), "InputFrame"));
        template.Triggers.Add(focus);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
        template.Triggers.Add(disabled);
        textBox.Template = template;
        return textBox;
    }

    // Окно перетаскивается за пустое место; ползунки, поля ввода, полосы прокрутки и кнопки сами ловят мышь, их перехватывать нельзя
    private static bool OnControl(DependencyObject? source)
    {
        for (var node = source; node is not null; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is System.Windows.Controls.Primitives.RangeBase or System.Windows.Controls.Primitives.TextBoxBase or PasswordBox
                or System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.Thumb or Selector) return true;
        return false;
    }

    private static Grid Split(double leftWidth, UIElement sidebar, UIElement content)
    {
        var grid = new Grid { ClipToBounds = true };
        grid.ColumnDefinitions.Add(new() { Width = new(leftWidth) });
        grid.ColumnDefinitions.Add(new());
        Grid.SetColumn(content, 1);
        grid.Children.Add(sidebar);
        grid.Children.Add(content);
        return grid;
    }

    public Window OpenSettings(string startPage = "Appearance") => Surface("Settings", 720, 480, false, (window, shell) =>
    {
        var side = new StackPanel { Margin = new(11, 11, 11, 0) };
        var heading = Right(Text("Settings", 22, true), CloseButton(window));
        ((FrameworkElement)heading).Margin = new(7, 0, 4, 11);
        side.Children.Add(heading);
        var content = new StackPanel { Margin = new(24, 22, 24, 16) };
        var scroll = Scroll(content);
        shell.Child = Split(208, new Border { Child = side, CornerRadius = new(22, 0, 0, 22),
            Background = Brush("#202020"), BorderBrush = Brush("#303030"), BorderThickness = new(0, 0, 1, 0) }, scroll);
        var tabs = new[] { ("Appearance", "\uE706"), ("Dock", "\uE8B7"), ("Automations", "\uE735"),
            ("Sounds", "\uE7F5"), ("General", "\uE713") };
        // Запись нового сочетания клавиш (страница «Общие»): пока задан capture, нажатия идут в него, а не в окно
        Func<Key, ModifierKeys, bool>? capture = null;
        window.PreviewKeyDown += (_, e) =>
        {
            if (capture is null || e.Key == Key.Escape) return;
            if (capture(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers)) e.Handled = true;
        };
        var navigation = new List<Button>();
        // Меню-дерево: слева вертикальная линия с ответвлением к каждому пункту, у выбранного пункта жирный текст и ответвление цвета акцента
        var branches = new List<System.Windows.Shapes.Rectangle>();
        var labels = new List<TextBlock>();
        void Select(string name)
        {
            for (int index = 0; index < tabs.Length; index++)
            {
                bool chosen = tabs[index].Item1 == name;
                navigation[index].Background = chosen ? Brush("#2C2C2E") : Brushes.Transparent;
                labels[index].FontWeight = chosen ? FontWeights.Bold : FontWeights.Normal;
                branches[index].Fill = chosen ? Accent(P) : Brush("#4A4A4C");
            }
            content.Children.Clear();
            scroll.ScrollToTop();
            switch (name)
            {
                case "Appearance": Appearance(content, () => Select(name), () => branches[0].Fill = Accent(P)); break;
                case "Dock": DockSettings(content); break;
                case "Automations": AutomationSettings(content); break;
                case "Sounds": SoundSettings(content); break;
                case "General":
                    content.Children.Add(Text("FlowBar", 24, true));
                    AddRow(content, "Interface language", Segments(P.UiLanguage, SetLanguage,
                        ("Use system language", "system"), ("English", "en"), ("Русский", "ru")));
                    AddRow(content, "Version", Text(Updates.Display(Updates.Current), 13, false, Muted));
                    // Обновление с GitHub: проверка, затем скачивание, сверка суммы и запуск новой копии (она ставит себя поверх этой)
                    var updateStatus = Text("", 12, false, Muted);
                    var updateLabel = Text("Check for updates", 13, false, Paper);
                    var updateButton = Button(updateLabel, () => { }, Brush("#3A3A3C"), 12);
                    updateButton.Padding = new(12, 5, 12, 5);
                    Updates.Release? found = null;
                    updateButton.Click += async (_, _) =>
                    {
                        updateButton.IsEnabled = false;
                        try
                        {
                            if (found is null)
                            {
                                updateStatus.Text = L10n.T("Checking for updates…");
                                var latest = await Updates.LatestAsync();
                                if (latest.Version > Updates.Current)
                                {
                                    found = latest;
                                    updateStatus.Text = L10n.F($"Version {Updates.Display(latest.Version)} is available.");
                                    updateLabel.Text = L10n.T("Download and install");
                                }
                                else updateStatus.Text = L10n.T("You have the latest version.");
                            }
                            else
                            {
                                updateStatus.Text = L10n.T("Downloading the update…");
                                await Updates.InstallAsync(found);
                                Application.Current.Shutdown(); // новая копия сама заменит эту
                            }
                        }
                        catch (Exception ex) when (Updates.IsUpdateError(ex))
                        {
                            ErrorLog.Write(ex, "Update");
                            updateStatus.Text = L10n.T("The update could not be installed. Check the connection and try again.");
                        }
                        finally { updateButton.IsEnabled = true; }
                    };
                    AddRow(content, "Updates", updateButton);
                    updateStatus.Margin = new(4, 2, 0, 6);
                    content.Children.Add(updateStatus);
                    var reportButton = Button(Text("Report a problem…", 13, false, Paper), () => OpenBugReport(), Brush("#3A3A3C"), 12);
                    reportButton.Padding = new(12, 5, 12, 5);
                    AddRow(content, "Report a problem", reportButton);
                    // агенты (Claude Code, Codex) подключаются к LumaDock командой «LumaDock.exe --mcp» (см. AgentServer.cs)
                    // Глобальные сочетания клавиш: нажмите кнопку и сочетание с Alt, Ctrl, Shift или Win; Backspace очищает
                    var hotkeyStatus = Text("", 12, false, Muted);
                    for (int index = 0; index < Hotkeys.Items.Length; index++)
                    {
                        int slot = index;
                        var caption = RawText(L10n.T(Hotkeys.Label(Hotkeys.Get(P, slot))), 13, false, Paper);
                        var assign = Button(caption, () => { }, Brush("#3A3A3C"), 12);
                        assign.Padding = new(12, 5, 12, 5);
                        assign.MinWidth = 110;
                        System.Windows.Automation.AutomationProperties.SetName(assign, L10n.F($"Shortcut: {L10n.T(Hotkeys.Items[slot])}"));
                        assign.Click += (_, _) =>
                        {
                            caption.Text = L10n.T("Press the shortcut");
                            hotkeyStatus.Text = L10n.T("Press the new shortcut. Backspace clears it, Esc cancels.");
                            capture = (key, modifiers) =>
                            {
                                string? value = key == Key.Back ? "" : Hotkeys.FromKeys(modifiers, key);
                                if (value is null) return true; // нужна клавиша с модификатором, ждём дальше
                                capture = null;
                                string previous = Hotkeys.Get(P, slot);
                                if (value.Length > 0 && Hotkeys.Taken(P, slot, value)) hotkeyStatus.Text = L10n.T("This shortcut is already used by another action.");
                                else
                                {
                                    Hotkeys.Set(P, slot, value);
                                    if (ApplyHotkeys().Contains(slot))
                                    {
                                        Hotkeys.Set(P, slot, previous);
                                        ApplyHotkeys();
                                        hotkeyStatus.Text = L10n.T("Windows did not accept this shortcut. Another app may be using it.");
                                    }
                                    else { model.Save(false); hotkeyStatus.Text = ""; }
                                }
                                caption.Text = L10n.T(Hotkeys.Label(Hotkeys.Get(P, slot)));
                                return true;
                            };
                        };
                        AddRow(content, L10n.F($"Shortcut: {L10n.T(Hotkeys.Items[slot])}"), assign, false);
                    }
                    hotkeyStatus.Margin = new(4, 2, 0, 6);
                    content.Children.Add(hotkeyStatus);
                    AddToggle(content, "Agent access (MCP)", P.Agent, enabled => P.Agent = enabled);
                    AddToggle(content, "Let agents read tasks and notes", P.AgentRead, enabled => P.AgentRead = enabled);
                    AddRow(content, "MCP command", RawText("\"" + (Environment.ProcessPath ?? "FlowBar.exe") + "\" --mcp", 12, false, Muted));
                    // Диктовка: связь с помощниками (Codex, Claude Code, Gemini) и модель встроенного движка распознавания
                    var voiceTitle = Text("Dictation", 18, true);
                    voiceTitle.Margin = new(0, 18, 0, 4);
                    content.Children.Add(voiceTitle);
                    foreach (string provider in new[] { "codex", "claude", "agy" })
                    {
                        string who = provider, whoName = VoiceAssistant.DisplayName(provider);
                        var connection = Text("", 12, false, Muted);
                        connection.TextWrapping = TextWrapping.Wrap;
                        connection.Margin = new(4, 2, 0, 6);
                        var checkButton = Button(Text("Check connection", 13, false, Paper), () => { }, Brush("#3A3A3C"), 12);
                        checkButton.Padding = new(12, 5, 12, 5);
                        System.Windows.Automation.AutomationProperties.SetName(checkButton, L10n.F($"Check connection: {whoName}"));
                        checkButton.Click += async (_, _) =>
                        {
                            checkButton.IsEnabled = false;
                            connection.Foreground = Muted;
                            connection.Text = L10n.F($"{whoName}: checking the connection…");
                            try
                            {
                                var result = await VoiceAssistant.CheckAsync(who, System.Threading.CancellationToken.None);
                                connection.Foreground = result.Ok ? Brush("#7BD88F") : Brush("#FF9088");
                                connection.Text = result.Ok ? L10n.F($"{whoName}: connected, answered in {result.Elapsed.TotalSeconds:0.0} s.")
                                    : whoName + ": " + L10n.T(result.Message);
                            }
                            finally { checkButton.IsEnabled = true; }
                        };
                        AddRow(content, whoName, checkButton);
                        content.Children.Add(connection);
                    }
                    var selectedModel = LocalVoice.Selected(P.VoiceModel);
                    AddRow(content, "Recognition model", Text(selectedModel.Label, 13, false, Muted));
                    var modelStatus = Text("", 12, false, Muted);
                    modelStatus.TextWrapping = TextWrapping.Wrap;
                    modelStatus.Margin = new(4, 2, 0, 6);
                    var modelCheck = Button(Text("Check the model", 13, false, Paper), () => { }, Brush("#3A3A3C"), 12);
                    modelCheck.Padding = new(12, 5, 12, 5);
                    var modelActionLabel = Text("Download the model", 13, false, Paper);
                    var modelAction = Button(modelActionLabel, () => { }, Brush("#3A3A3C"), 12);
                    modelAction.Padding = new(12, 5, 12, 5);
                    modelAction.Margin = new(6, 0, 0, 0);
                    modelAction.Visibility = Visibility.Collapsed;
                    Func<System.Threading.Tasks.Task>? modelWork = null;
                    void ModelSay(string message, bool error = false)
                    {
                        modelStatus.Foreground = error ? Brush("#FF9088") : Muted;
                        modelStatus.Text = message;
                    }
                    async System.Threading.Tasks.Task CheckModel()
                    {
                        modelCheck.IsEnabled = modelAction.IsEnabled = false;
                        modelAction.Visibility = Visibility.Collapsed;
                        modelWork = null;
                        var lines = new List<string>();
                        bool trouble = false;
                        try
                        {
                            var current = LocalVoice.Selected(P.VoiceModel);
                            lines.Add(L10n.T(LocalVoice.EngineReady ? "Engine: ready" : "Engine: not downloaded"));
                            var state = await LocalVoice.VerifyAsync(current, System.Threading.CancellationToken.None);
                            lines.Add(L10n.T(state switch
                            {
                                LocalVoice.ModelState.Ok => "Model files are intact.",
                                LocalVoice.ModelState.Missing => "The model is not downloaded.",
                                _ => "The model files are damaged. Download the model again."
                            }));
                            if (state != LocalVoice.ModelState.Ok || !LocalVoice.EngineReady)
                            {
                                trouble = true;
                                modelActionLabel.Text = L10n.T("Download the model");
                                modelWork = async () =>
                                {
                                    if (state is LocalVoice.ModelState.Corrupt or LocalVoice.ModelState.WrongSize) File.Delete(LocalVoice.ModelPath(current));
                                    await LocalVoice.PrepareAsync(current, text => ModelSay(text), System.Threading.CancellationToken.None);
                                };
                            }
                            try
                            {
                                var latest = await LocalVoice.LatestAsync(current, System.Threading.CancellationToken.None);
                                if (latest.Digest != current.Digest)
                                {
                                    trouble = true;
                                    lines.Add(L10n.T("A newer version of the model is available."));
                                    modelActionLabel.Text = L10n.T("Update the model");
                                    modelWork = async () =>
                                    {
                                        await LocalVoice.InstallRemoteAsync(current, latest, text => ModelSay(text), System.Threading.CancellationToken.None);
                                        P.VoiceModelPins[current.Id] = new LocalVoice.ModelPin { Revision = latest.Revision, Digest = latest.Digest, Size = latest.Size };
                                        model.Save(false);
                                    };
                                }
                                else lines.Add(L10n.T("The model is up to date."));
                            }
                            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Threading.Tasks.TaskCanceledException or System.Text.Json.JsonException
                                or InvalidDataException or KeyNotFoundException or InvalidOperationException)
                            {
                                trouble = true;
                                lines.Add(L10n.T("Could not check for model updates. Check the connection and try again."));
                            }
                            ModelSay(string.Join(" ", lines), trouble);
                            modelAction.Visibility = modelWork is null ? Visibility.Collapsed : Visibility.Visible;
                        }
                        finally { modelCheck.IsEnabled = modelAction.IsEnabled = true; }
                    }
                    modelCheck.Click += (_, _) => _ = CheckModel();
                    modelAction.Click += async (_, _) =>
                    {
                        if (modelWork is null) return;
                        modelCheck.IsEnabled = modelAction.IsEnabled = false;
                        try
                        {
                            await modelWork();
                            await CheckModel();
                            if (modelWork is null) ModelSay(L10n.T("The model is ready. You can start recording."));
                        }
                        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Threading.Tasks.TaskCanceledException or InvalidDataException or IOException or UnauthorizedAccessException)
                        {
                            ModelSay(L10n.T("The engine could not be downloaded. Check the connection and try again."), true);
                        }
                        finally { modelCheck.IsEnabled = modelAction.IsEnabled = true; }
                    };
                    AddRow(content, "Model check", RowStack(modelCheck, modelAction));
                    content.Children.Add(modelStatus);
                    var handyNote = Text("Handy keeps its own models. Update them in Handy.", 12, false, Muted);
                    handyNote.Margin = new(4, 0, 0, 8);
                    content.Children.Add(handyNote);
                    AddRow(content, "Local profile", Button("Open folder", () =>
                    {
                        var directory = Path.GetDirectoryName(model.Profile)!;
                        Directory.CreateDirectory(directory);
                        WindowsShell.Launch(directory);
                    }));
                    break;
            }
        }
        for (int position = 0; position < tabs.Length; position++)
        {
            var (name, glyph) = tabs[position];
            var icon = Symbol(glyph, 18, Brush("#78B9FF"));
            icon.Width = 24;
            var text = Text(name, 14);
            text.Margin = new(9, 0, 0, 0);
            var button = Button(RowStack(icon, text), () => Select(name), Brushes.Transparent);
            button.Height = 36;
            button.Margin = new(0, 1, 0, 1);
            navigation.Add(button);
            labels.Add(text);
            // линия дерева не выходит за первый и последний пункт: начинается и кончается на уровне ответвления
            var trunk = new System.Windows.Shapes.Rectangle { Width = 1, Fill = Brush("#4A4A4C"), HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new(8, position == 0 ? 19 : 0, 0, position == tabs.Length - 1 ? 19 : 0) };
            var branch = new System.Windows.Shapes.Rectangle { Width = 9, Height = 2, Fill = Brush("#4A4A4C"), RadiusX = 1, RadiusY = 1,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) };
            branches.Add(branch);
            button.Margin = new(20, 1, 0, 1);
            var row = new Grid();
            row.Children.Add(trunk);
            row.Children.Add(branch);
            row.Children.Add(button);
            side.Children.Add(row);
        }
        Select(startPage);
    });

    private void SetLanguage(string value)
    {
        if (P.UiLanguage == value) return;
        P.UiLanguage = value;
        L10n.SetLanguage(value);
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(L10n.Culture.IetfLanguageTag);
        foreach (var surface in surfaces.Values.ToArray()) surface.Close();
        ContextMenu = CommonMenu();
        model.Save();
        OpenSettings("General");
    }

    private void Appearance(StackPanel content, Action reload, Action accentChanged)
    {
        var styleLabel = Text("Dock style", 13, false, Muted);
        styleLabel.Margin = new(4, 0, 0, 0);
        content.Children.Add(styleLabel);
        var styles = new UniformGrid { Rows = 1, Margin = new(-4, 7, -4, 17) };
        Border? selectedSwatch = null, selectedRing = null;
        foreach (string style in new[] { "Black", "White", "Liquid", "iOS", "Midnight", "Clear" })
        {
            var tiles = RowStack();
            tiles.HorizontalAlignment = HorizontalAlignment.Center;
            var ink = style == "White" ? Brush("#14000000") : style != "Clear" ? Brush("#24FFFFFF")
                : P.ClearWidgets == "White" ? Brush("#F5F5F7") : P.ClearWidgets == "Glass" ? Brush("#50FFFFFF") : Brush("#18181A");
            for (int i = 0; i < 3; i++)
            {
                var dot = Card(null, 5, ink);
                dot.Width = dot.Height = 15;
                dot.Margin = new(1.5, 0, 1.5, 0);
                tiles.Children.Add(dot);
            }
            var sample = Button(tiles, () => { P.Style = style; model.Save(); reload(); }, StylePreview(style), 11);
            sample.Height = 56;
            sample.Padding = new(0);
            System.Windows.Automation.AutomationProperties.SetName(sample, L10n.T(style));
            var caption = Text(style, 12, style == P.Style, style == P.Style ? Paper : Muted);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.Margin = new(0, 5, 0, 0);
            var swatch = Card(sample, 12, Brushes.Transparent, style == P.Style ? Accent(P) : Line);
            var ring = Accent(P).Clone();
            ring.Opacity = style == P.Style ? .30 : 0;
            var halo = Card(swatch, 14, Brushes.Transparent, ring);
            if (style == P.Style) { selectedSwatch = swatch; selectedRing = halo; }
            var column = Stack(halo, caption);
            column.Margin = new(4, 0, 4, 0);
            styles.Children.Add(column);
        }
        content.Children.Add(styles);
        if (P.Style == "Clear")
        {
            var clear = Segments(P.ClearWidgets, finish => { P.ClearWidgets = finish; model.Save(); reload(); },
                ("Black", "Black"), ("Glass", "Glass"), ("White", "White"));
            AddRow(content, "Widget style", clear);
            ((FrameworkElement)content.Children[content.Children.Count - 1]).Margin = new(0, 0, 0, 16);
        }
        // миксер стилей из FlowBar: стиль слева, материал справа, превью в центре, ниже прозрачность
        var mixerLabel = Text("Style mixer", 13, false, Muted);
        mixerLabel.Margin = new(4, 0, 0, 7);
        content.Children.Add(mixerLabel);
        Color AccentColor() => (Accent(P) as SolidColorBrush)?.Color ?? Color.FromRgb(10, 136, 255);
        var mixer = new StyleMixer(Looks.Styles, Looks.Styles.Select(L10n.T).ToArray(), () => P.Style,
            style => { P.Style = style; model.Save(); reload(); },
            Looks.Materials, Looks.Materials.Select(material => L10n.T(Looks.MaterialName(material))).ToArray(), () => Looks.Material(P),
            material => { P.Material = material; model.Save(); reload(); },
            () => P.Transparency, AccentColor);
        content.Children.Add(mixer);
        var clearness = FilledSlider(Accent(P), P.Transparency, 22, "Transparency");
        clearness.Width = 190;
        var percent = RawText($"{P.Transparency}%", 13, false, Muted);
        percent.Width = 44;
        percent.TextAlignment = TextAlignment.Right;
        var settle = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        settle.Tick += (_, _) => { settle.Stop(); model.Save(); };
        clearness.ValueChanged += (_, change) =>
        {
            P.Transparency = (int)Math.Round(change.NewValue);
            percent.Text = $"{P.Transparency}%";
            mixer.Repaint(false);
            settle.Stop();
            settle.Start();
        };
        AddRow(content, "Transparency", RowStack(clearness, percent));
        // Преломление живого стекла: насколько картинка под доком изгибается у его краёв
        var bend = FilledSlider(Accent(P), P.Refraction, 22, "Refraction");
        bend.Width = 190;
        var bendText = RawText($"{P.Refraction}%", 13, false, Muted);
        bendText.Width = 44;
        bendText.TextAlignment = TextAlignment.Right;
        var bendSettle = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        bendSettle.Tick += (_, _) => { bendSettle.Stop(); model.Save(); };
        bend.ValueChanged += (_, change) =>
        {
            P.Refraction = (int)Math.Round(change.NewValue);
            bendText.Text = $"{P.Refraction}%";
            bendSettle.Stop();
            bendSettle.Start();
        };
        AddRow(content, "Glass refraction", RowStack(bend, bendText));
        AddRow(content, "Tile borders", Segments(P.TileBorders, value => { P.TileBorders = value; model.Save(); }, ("On", true), ("Off", false)));
        // Код оформления: копируется в буфер обмена и вставляется оттуда; неверный код ничего не меняет
        var lookStatus = Text("", 12, false, Muted);
        lookStatus.Margin = new(10, 0, 0, 0);
        lookStatus.VerticalAlignment = VerticalAlignment.Center;
        AddRow(content, "Look code", RowStack(
            Button("Copy", () => { try { Clipboard.SetText(Looks.Export(P)); lookStatus.Text = L10n.T("Copied"); } catch (System.Runtime.InteropServices.ExternalException) { lookStatus.Text = L10n.T("The clipboard is busy."); } }),
            Button("Paste", () =>
            {
                string? code = null;
                try { code = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : null; } catch (System.Runtime.InteropServices.ExternalException) { }
                if (!Looks.TryImport(P, code)) { lookStatus.Text = L10n.T("The clipboard has no valid look code."); return; }
                model.Save();
                reload();
            }), lookStatus));
        ((FrameworkElement)content.Children[content.Children.Count - 1]).Margin = new(0, 0, 0, 16);
        var colourLabel = Text("Colour", 13, false, Muted);
        colourLabel.Margin = new(4, 0, 0, 7);
        content.Children.Add(colourLabel);
        AccentSettings(content, () =>
        {
            accentChanged();
            if (selectedSwatch is null || selectedRing is null) return;
            selectedSwatch.BorderBrush = Accent(P);
            var tint = Accent(P).Clone();
            tint.Opacity = .30;
            selectedRing.BorderBrush = tint;
        });
        var note = Text("Switches, sliders, progress and every selection wear it.", 12, false, Muted);
        note.Margin = new(4, 7, 0, 0);
        content.Children.Add(note);
    }

    private static Brush StylePreview(string style) => style switch
    {
        "Black" => Brush("#1C1C1E"),
        "White" => Brush("#F5F5F7"),
        "Clear" => ClearSample(),
        "iOS" => new LinearGradientBrush(Brush("#4A6FA5").Color, Brush("#23232B").Color, 45),
        "Midnight" => new LinearGradientBrush(Brush("#2E3440").Color, Brush("#0A0C10").Color, 45),
        _ => new LinearGradientBrush(Brush("#3A4A6B").Color, Brush("#2C2C38").Color, 45)
    };

    private static void AddRow(StackPanel holder, string title, FrameworkElement control, bool localizeTitle = true)
    {
        var row = Card(Right(RawText(localizeTitle ? L10n.T(title) : title, 14, true), control), 10, Row);
        row.Padding = new(12, 7, 12, 7);
        row.Margin = new(0, 2, 0, 1);
        row.MinHeight = 41;
        holder.Children.Add(row);
    }

    private void AddToggle(StackPanel holder, string title, bool value, Action<bool> set)
    {
        var ball = new System.Windows.Shapes.Ellipse { Width = 21, Height = 21, Fill = Brushes.White };
        var button = Button(ball, () => { }, Row, 13);
        button.Click += (_, _) =>
        {
            value = !value;
            set(value);
            model.Save();
            Paint();
        };
        button.Width = 44;
        button.Height = 26;
        button.Padding = new(2);
        void Paint()
        {
            button.Background = value ? Brush("#2BCC52") : Brush("#4B4B4B");
            ball.HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        Paint();
        AddRow(holder, title, button);
    }

    private void AddSlider(StackPanel holder, string title, double value, double min, double max, double step, Action<double> set)
    {
        var label = Text(value.ToString("0.##"), 13, false, Muted);
        var slider = FilledSlider(Accent(P), value, 18, title, min, max);
        slider.TickFrequency = slider.SmallChange = step;
        slider.IsSnapToTickEnabled = true;
        slider.Width = 145;
        slider.Margin = new(11, 0, 0, 0);
        slider.ValueChanged += (_, _) =>
        {
            set(slider.Value);
            label.Text = slider.Value.ToString("0.##");
            model.Save();
        };
        AddRow(holder, title, RowStack(label, slider));
    }

    private void DockSettings(StackPanel holder)
    {
        var dock = SettingsGroup(holder);
        var size = DockSlider(dock, "Size", P.Size, 60, 125, 1, "%", v => P.Size = v, "Scale the dock and its widgets.");
        DockSlider(dock, "Spacing", P.Spacing, 0, 24, .75, " px", v => P.Spacing = v, "Space between tiles.", size);
        DockSlider(dock, "Padding", P.Padding, 0, 24, .75, " px", v => P.Padding = v, "Space between tiles and the dock border.", size);
        DockSlider(dock, "Dock border", P.DockBorder, 0, 4, .5, " px", v => P.DockBorder = v, "Thickness of the line along the dock edges; 0 removes it.");
        DockSlider(dock, "Distance from edge", P.Edge, 0, 200, 1, " px", v => P.Edge = v, "Gap from the edge of the screen.");
        DockToggle(dock, "Full width", P.FullWidth, v => P.FullWidth = v, "Stretch the dock across the screen.");
        DockToggle(dock, "Names on hover", P.Names, v => P.Names = v, "Show the name of an app or widget under the pointer.");
        DockToggle(dock, "Window previews", P.WindowPreviews, v => P.WindowPreviews = v, "Preview open windows on hover.");
        DockToggle(dock, "App cards", P.AppCards, v => P.AppCards = v, "Show each app on a card like the widgets beside it.");
        DockToggle(dock, "App badges", P.AppBadges, v => P.AppBadges = v, "Show supported unread counts on application icons.");
        SettingsRow(dock, Text("App icons", 14, true), Segments(P.IconSize, value => { P.IconSize = value; model.Save(); },
            ("Small", 34), ("Medium", 40), ("Large", 46)), "Choose the icon size.");
        DockToggle(dock, "Magnification", P.Magnification, v => { P.Magnification = v; DrawDockSettings(); }, "Enlarge app icons under the pointer.");
        if (P.Magnification)
            DockSlider(dock, "Magnification scale", P.MagnificationScale, 1.2, 3, .1, "×", v => P.MagnificationScale = v, "Scale of the enlarged tile.");
        DockToggle(dock, "Auto-hide", P.AutoHide, v => { P.AutoHide = v; DrawDockSettings(); }, "Reveal the dock when the pointer reaches the edge.");
        if (P.AutoHide)
            DockToggle(dock, "Show on desktop", P.ShowOnDesktop, v => P.ShowOnDesktop = v, "Keep the dock visible while the desktop is active.");
        DockToggle(dock, "Keep system widgets", P.PinSystem, v => { P.PinSystem = v; model.EnsurePinned(); }, "Input language, sound, network switches and the tray icon always stay in the dock.");
        DockToggle(dock, "Pinned apps stay in place", P.StickyPinnedApps, v => P.StickyPinnedApps = v, "When the dock is full, only open apps scroll with the mouse wheel; if off, all apps scroll.");
        if (System.Windows.Forms.Screen.AllScreens.Length > 1)
            DockToggle(dock, "All monitors", P.AllMonitors, v => { P.AllMonitors = v; DrawDockSettings(); }, "Show the dock on every connected monitor.");
        var extra = System.Windows.Forms.Screen.AllScreens.Where(item => !item.Primary).ToList();
        if (P.AllMonitors && extra.Count > 0)
        {
            SettingsRow(dock, Text("Windows on docks", 14, true), Segments(P.WindowsOnDocks, value => { P.WindowsOnDocks = value; model.Save(); },
                ("Own monitor", "own"), ("Main only", "main"), ("All", "all")), "Which dock shows each open window; windows on other monitors get a ring.");
            foreach (var other in extra)
            {
                string device = other.DeviceName;
                string current = P.Monitors.TryGetValue(device, out var setup) ? setup.Widgets : "same";
                int number = Array.IndexOf(System.Windows.Forms.Screen.AllScreens, other) + 1;
                SettingsRow(dock, Text(L10n.T("Widgets on monitor") + " " + number, 14, true), Segments(current, value => model.SetMonitorWidgets(device, value),
                    ("Same as main", "same"), ("Own layout", "own"), ("Apps only", "none")), "Own layout has its own widgets, set by dragging and the gallery on that dock.");
            }
        }
        DockToggle(dock, "Lock dock", P.Locked, v => P.Locked = v, "Prevent dragging and rearranging items.");
        var behaviour = SettingsGroup(holder, "Behaviour");
        SettingsRow(behaviour, Text("Clock", 14, true), Segments(P.Hour24, value => { P.Hour24 = value; model.Save(); },
            ("24h", true), ("12h", false)), "Choose the clock format.");
        var startup = WindowsShell.StartupEnabled();
        if (startup.HasValue)
            DockToggle(behaviour, "Launch at startup", startup.Value, value =>
            {
                if (!WindowsShell.Startup(value)) { DrawDockSettings(); Report("Windows could not change startup settings."); }
            }, "Launch FlowBar when you sign in.");
        else
            SettingsRow(behaviour, Text("Launch at startup", 14, true), Text("Unavailable", 12, false, Muted),
                "Windows restricted access to startup settings.");
        var screen = SettingsGroup(holder, "Screen");
        int mode = P.ReplaceTaskbar ? P.AutoHideWindows ? 2 : 1 : 0;
        SettingsRow(screen, Text("Windows taskbar", 14, true), Segments(mode, value =>
        {
            P.ReplaceTaskbar = value != 0;
            P.AutoHideWindows = value == 2;
            model.Save();
        }, ("Visible", 0), ("Hidden", 1), ("Auto-hide", 2)), "Set the behaviour of the native Windows taskbar.");

        void DrawDockSettings()
        {
            holder.Children.Clear();
            DockSettings(holder);
        }
    }

    public Window OpenGallery(string startCategory = "Clocks") => Surface("Widgets", 1080, 620, false, (window, shell) =>
    {
        var search = Input("Search widgets");
        search.Height = 35;
        var side = new DockPanel { Margin = new(11) };
        var top = Stack(search);
        DockPanel.SetDock(top, Dock.Top);
        side.Children.Add(top);
        var go = Button(RowStack(Symbol("\uE713", 18, Brush("#9AC8F5")), Text("  Settings", 14, true)),
            () => { window.Close(); OpenSettings(); }, Brushes.Transparent);
        go.Height = 38;
        var footer = Stack(new Border { Height = 1, Background = Line, Margin = new(0, 4, 0, 5) }, go);
        DockPanel.SetDock(footer, Dock.Bottom);
        side.Children.Add(footer);
        var folder = new StackPanel { Margin = new(0, 10, 0, 5) };
        side.Children.Add(Scroll(folder));
        var main = new DockPanel { Margin = new(23, 17, 16, 12) };
        var count = Text("", 11, true, Muted);
        var badge = Card(count, 12, Row);
        badge.Padding = new(8, 2, 8, 2);
        badge.Margin = new(10, 0, 12, 0);
        badge.VerticalAlignment = VerticalAlignment.Center;
        var inBar = Text("", 11, false, Muted);
        var title = Text("All Widgets", 20, true);
        var heading = (FrameworkElement)Right(RowStack(title, badge, inBar), CloseButton(window));
        heading.Margin = new(0, 0, 0, 14);
        DockPanel.SetDock(heading, Dock.Top);
        main.Children.Add(heading);
        var content = Stack();
        var scroll = Scroll(content);
        main.Children.Add(scroll);
        shell.Background = new LinearGradientBrush(Color.FromRgb(33, 33, 34), Color.FromRgb(19, 19, 21), 90);
        shell.Child = Split(220, new Border { Child = side, CornerRadius = new(22, 0, 0, 22), BorderBrush = Line,
            BorderThickness = new(0, 0, 1, 0), Background = Brush("#66151517") }, main);
        var active = startCategory;
        var links = new Dictionary<string, Button>();
        var sections = new Dictionary<string, FrameworkElement>();
        var live = new List<(FrameworkElement Tile, Action Update)>();
        void UpdateVisible()
        {
            foreach (var (tile, update) in live)
            {
                if (!tile.IsLoaded) continue;
                var position = tile.TranslatePoint(new Point(0, 0), scroll);
                if (position.Y + tile.ActualHeight > 0 && position.Y < scroll.ActualHeight) update();
            }
        }
        void Jump(string name)
        {
            if (!sections.TryGetValue(name, out var section)) return;
            content.UpdateLayout();
            scroll.ScrollToVerticalOffset(section.TranslatePoint(new Point(0, 0), content).Y);
        }
        void Render(bool reset = true)
        {
            double offset = scroll.VerticalOffset;
            live.Clear();
            sections.Clear();
            content.Children.Clear();
            var term = search.Text.Trim();
            int shown = 0;
            foreach (var group in Widgets.Categories)
            {
                var ids = group.Catalog.Split('|').Where(id => Widgets.Available.TryGetValue(id, out var item)
                    && (L10n.Matches(item.Name, term) || L10n.Matches(group.Name, term))).ToArray();
                if (ids.Length == 0) continue;
                shown += ids.Length;
                var glyph = Symbol(group.Glyph, 15, Brush("#8CB1D1"));
                glyph.Margin = new(0, 0, 8, 0);
                var label = RowStack(glyph, Text(group.Name, 14, true, Muted));
                label.Margin = new(0, 2, 0, 13);
                content.Children.Add(label);
                sections.Add(group.Name, label);
                var wrap = new WrapPanel();
                foreach (string id in ids)
                {
                    var widget = Widgets.Available[id];
                    var face = Widgets.Face(widget, model, true, out Action update);
                    (face, update) = Widgets.ServicePreview(widget, model, face, update);
                    var card = Button(face, () => { model.ToggleWidget(id, Items); Render(false); }, Brush("#121213"), 17);
                    card.Padding = new(8, 2, 8, 2);
                    var added = Items.Contains(id);
                    string action = L10n.T(added ? "Remove" : "Add") + " · " + L10n.T(widget.Name);
                    System.Windows.Automation.AutomationProperties.SetName(card, L10n.Russian ? action : $"{(added ? "Remove" : "Add")} {widget.Name}");
                    var frame = Card(card, 18, Brushes.Transparent, Brush("#363638"));
                    frame.Height = 72;
                    frame.Width = id switch
                    {
                        "Clock" => 162, "Flip Clock" => 166, "World Clocks" => 193, "7 Days" => 344,
                        "World Time" => 228, "Today" => 234, "Date" => 230,
                        "Day Number" or "Analog Minimal" or "Clock & Weather" or "Now Playing Mini" => 73,
                        "Segment Clock" => 194, "WhatsApp" => 206, "Day Progress" => 181, "Month Progress" or "Year Progress" => 154,
                        "Tray" or "Language" or "Theme Toggle" or "Show Desktop" or "Notifications" or "Keyboard" or "Do Not Disturb"
                            or "CPU" or "RAM" or "Wallpaper Gallery" => 73,
                        "Quick Toggles" => 383, "Brightness" => 271, "Temperature" => 215, "Network" => 224,
                        _ => Math.Max(79, widget.Width * 1.12)
                    };
                    var plate = new Grid();
                    plate.Children.Add(frame);
                    if (added)
                    {
                        var marker = Card(Symbol("\uE73E", 11, Brushes.White), 10, Accent(P), Brush("#151518"));
                        marker.Width = marker.Height = 20;
                        marker.HorizontalAlignment = HorizontalAlignment.Right;
                        marker.VerticalAlignment = VerticalAlignment.Top;
                        marker.Margin = new(0, -7, -7, 0);
                        marker.IsHitTestVisible = false;
                        plate.Children.Add(marker);
                    }
                    var caption = RawText(widget.GalleryCaption, 12, false, Muted);
                    caption.HorizontalAlignment = HorizontalAlignment.Center;
                    caption.Margin = new(0, 5, 0, 0);
                    var tile = Stack(plate, caption);
                    tile.ToolTip = L10n.Russian ? action : added ? $"Remove {widget.Name}" : $"Add {widget.Name}";
                    tile.Margin = new(0, 0, 16, 27);
                    wrap.Children.Add(tile);
                    live.Add((tile, update));
                    update();
                }
                content.Children.Add(wrap);
            }
            if (content.Children.Count == 0) content.Children.Add(Text("No matching widgets", 15, true, Muted));
            count.Text = shown.ToString();
            inBar.Text = L10n.F($"{Items.Count(id => Widgets.Available.ContainsKey(id) && !Widgets.WindowsFolders.Contains(id))} in bar");
            foreach (var entry in links) entry.Value.Background = entry.Key == active && term.Length == 0 ? Accent(P) : Brushes.Transparent;
            if (reset) scroll.ScrollToTop(); else scroll.ScrollToVerticalOffset(offset);
        }
        foreach (var category in Widgets.Categories)
        {
            var icon = Symbol(category.Glyph, 17, Brush("#9AC8F5"));
            icon.Width = 24;
            var caption = Text(category.Name, 13, true);
            caption.Margin = new(8, 0, 0, 0);
            var button = Button(RowStack(icon, caption), () => { active = category.Name; search.Clear(); Render(); Jump(active); });
            button.Height = 35;
            button.Margin = new(0, 0, 0, 2);
            links.Add(category.Name, button);
            folder.Children.Add(button);
        }
        search.TextChanged += (_, _) => Render();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => UpdateVisible();
        window.Closed += (_, _) => timer.Stop();
        Render();
        window.Loaded += (_, _) => { if (active != "Clocks") Jump(active); };
        timer.Start();
    });

    internal static Action? NativeWidgetAction(string id) => id switch
        {
            "Show Desktop" => (Action)(() => WindowsShell.Key(0x5B, 0x44)),
            "Notifications" => () => WindowsShell.Key(0x5B, 0x4E),
            "Search" => () => WindowsShell.Key(0x5B, 0x53),
            "WhatsApp" => UnreadNotifications.OpenWhatsApp,
            "Keyboard" => () => WindowsShell.Key(0x5B, 0x11, 0x4F),
            "Theme Toggle" => WindowsShell.ToggleTheme,
            "Language" => () => WindowsShell.Key(0x5B, 0x20),
            "Do Not Disturb" => () => WindowsShell.Launch("ms-settings:notifications"),
            "Desktop" => () => WindowsShell.Launch(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            "Documents" => () => WindowsShell.Launch(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            "Pictures" => () => WindowsShell.Launch(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            "Music" => () => WindowsShell.Launch(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            "Downloads" => () => WindowsShell.Launch("shell:Downloads"),
            "Administrative Tools" => () => WindowsShell.Launch("shell:Administrative Tools"),
            _ => null
        };

    // Для самопроверки: щелчок по виджету сразу после того, как его окно закрылось от потери фокуса, окно не открывает; позже или по другому виджету открывает
    internal string? ReopenCheck()
    {
        closedByFocus = ("Notes", Environment.TickCount64);
        if (OpenWidget("Notes") is not null || surfaces.ContainsKey("Notes")) return "второе нажатие открыло окно заново";
        if (closedByFocus.Key is not null) return "отметка закрытия не сброшена после нажатия";
        closedByFocus = ("Notes", Environment.TickCount64 - 5000);
        var late = OpenWidget("Notes");
        late?.Close();
        if (late is null) return "нажатие через несколько секунд не открыло окно";
        closedByFocus = ("Weather", Environment.TickCount64);
        var other = OpenWidget("Notes");
        other?.Close();
        closedByFocus = default;
        return other is null ? "закрытие одного виджета помешало открыть другой" : "";
    }

    public Window? OpenWidget(string id, FrameworkElement? anchor = null)
    {
        if (id == "TikTok") { TikTokFeed.Toggle(model); return null; }
        var destination = NativeWidgetAction(id);
        if (destination is not null) { destination(); return null; }
        if (surfaces.TryGetValue(id, out var current) && !smoke) { current.Close(); return null; }
        // окно только что закрылось само от щелчка по этому же виджету: это и есть «второе нажатие», заново не открываем
        if (closedByFocus.Key == id && Environment.TickCount64 - closedByFocus.At < 1500) { closedByFocus = default; return null; }
        if (id is "Weather" or "Weather Details" or "Today & Forecast" or "Weather Status" or "Hourly" or "Sunrise & Sunset" or "Clock & Weather")
            return Surface(id, id == "Weather" ? 560 : 510, id == "Weather" ? 620 : 550, true,
                (window, shell) => Weather(shell, window, id), anchor);
        if (id is "Quick Toggles" or "Wi-Fi" or "Bluetooth")
            return Surface(id, 390, id == "Quick Toggles" ? 362 : 500, true,
                (window, shell) => QuickToggleSurface(window, shell, id, anchor), anchor);
        if (id == "Tray") { OpenWindowsTray(anchor); return null; } // своего трея нет: открываем системный
        if (id is "Calendar" or "Clock" or "Today" or "Date" or "7 Days")
            return Surface(id, 720, 560, true, CalendarSurface, anchor);
        bool large = id is "Todos" or "Notes" or "Apps" or "Screen Time";
        bool tall = id is "Calendar" or "Clock" or "Recently Opened" or "Storage" or "Audio Output" or "Day Timeline"
            or "AI Limits" or "Claude Usage" or "Total Revenue" or "Wallpaper Gallery" || ConnectedData.Supports(id);
        bool audio = id is "Audio Output" or "Microphone";
        bool player = id is "Now Playing" or "Now Playing Artwork" or "Now Playing Bar" or "Now Playing Mini";
        return Surface(id, player ? 440 : audio ? 420 : large ? 620 : tall ? 480 : 365,
            id == "Audio Output" ? 566 : id == "Dictation" ? 560 : player ? 476 : audio ? 402 : large ? 460 : tall ? 426 : 259,
            true, (window, shell) =>
            {
                var content = new DockPanel { Margin = new(11), LastChildFill = true };
                var glyph = Widgets.Available.TryGetValue(id, out var definition) ? definition.Glyph : "\uE735";
                var icon = Card(Symbol(glyph, 16), 9, Accent(P));
                icon.Width = icon.Height = 29;
                icon.Margin = new(0, 0, 9, 0);
                var title = Text(id == "Audio Output" ? "Sound" : id, 15, true);
                var subtitle = Text(id == "Todos" ? "Nothing to do" : "", 11, false, Muted);
                var header = (FrameworkElement)Right(RowStack(icon, Stack(title, subtitle)), CloseButton(window));
                header.Margin = new(3, 0, 3, 10);
                DockPanel.SetDock(header, Dock.Top);
                content.Children.Add(header);
                shell.Child = content;
                switch (id)
                {
                    case "Todos": TodoSurface(content, subtitle); break;
                    case "Dictation": DictationSurface(window, content, subtitle); break;
                    case "Notes":
                        var note = Input("Notes", P.Notes);
                        note.AcceptsReturn = true;
                        note.TextWrapping = TextWrapping.Wrap;
                        note.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                        note.TextChanged += (_, _) => { P.Notes = note.Text; model.Save(false); };
                        content.Children.Add(note);
                        break;
                    case "World Time":
                    case "World Clocks":
                        WorldClockSettings(content, window, id);
                        break;
                    case "Pomodoro":
                    case "Timer":
                    case "Stopwatch":
                        TimeSurface(id, window, content);
                        break;
                    case "Audio Output": AudioSurface(content); break;
                    case "Microphone": AudioSurface(content, true); break;
                    case "Now Playing":
                    case "Now Playing Artwork":
                    case "Now Playing Bar":
                    case "Now Playing Mini":
                        MediaSurface(content, window);
                        break;
                    case "Apps": AppsSurface(content); break;
                    default:
                        if (DrawExpanded(id, window, content)) break;
                        if (definition is null) break;
                        var face = Widgets.Face(definition, model, true, out var update);
                        face.HorizontalAlignment = HorizontalAlignment.Center;
                        face.VerticalAlignment = VerticalAlignment.Center;
                        content.Children.Add(face);
                        Live(window, update);
                        break;
                }
            }, anchor);
    }

    private void TodoSurface(DockPanel content, TextBlock subtitle)
    {
        var input = Input("New todo");
        var progress = FilledMeter(Accent(P), 3, "Task progress", Row);
        progress.Maximum = 1;
        progress.Margin = new(4, 0, 4, 9);
        DockPanel.SetDock(progress, Dock.Top);
        content.Children.Add(progress);
        var body = Card(null, 14, Brush("#222222"));
        void RefreshTodos()
        {
            subtitle.Text = P.Todos.Any(todo => !todo.Done) ? L10n.F($"{P.Todos.Count(todo => !todo.Done)} open") : L10n.T("Nothing to do");
            progress.Value = P.Todos.Count == 0 ? 0 : (double)P.Todos.Count(todo => todo.Done) / P.Todos.Count;
            if (P.Todos.Count == 0)
            {
                var text = Text("No todos yet – add one below.", 13, false, Muted);
                text.HorizontalAlignment = HorizontalAlignment.Center;
                var mark = Card(Symbol("\uE73E", 19, Muted), 20, Brush("#303030"));
                mark.Width = mark.Height = 39;
                var empty = Stack(mark, text);
                text.Margin = new(0, 12, 0, 0);
                empty.VerticalAlignment = VerticalAlignment.Center;
                body.Child = empty;
                return;
            }
            var list = Stack();
            list.Margin = new(8);
            foreach (Todo todo in P.Todos)
            {
                var label = CheckField(RawText(todo.Text, 14, false, todo.Done ? Muted : Paper), todo.Done, Accent(P), todo.Text);
                label.Click += (_, _) => { model.ToggleTodo(todo); RefreshTodos(); };
                var remove = Button(Symbol("\uE74D", 13, Muted), () => { P.Todos.Remove(todo); model.Save(); RefreshTodos(); }, SurfaceInk);
                System.Windows.Automation.AutomationProperties.SetName(remove, L10n.T("Remove task"));
                list.Children.Add(Right(label, remove));
            }
            body.Child = Scroll(list);
        }
        var reminder = DateField("Reminder date", 124, Accent(P));
        var feedback = Text("", 12, false, Brush("#FF9088"));
        feedback.TextWrapping = TextWrapping.Wrap;
        var reminderTime = Input("HH:mm", "09:00", "Reminder time");
        reminderTime.Width = 58;
        reminderTime.Margin = new(4, 0, 4, 0);
        var schedule = RowStack(reminder, reminderTime);
        schedule.Visibility = Visibility.Collapsed;
        bool invalidDate = false;
        reminder.DateValidationError += (_, e) =>
        {
            e.ThrowException = false;
            invalidDate = true;
            feedback.Text = L10n.T("Enter a valid date.");
        };
        reminder.SelectedDateChanged += (_, _) => { invalidDate = false; feedback.Text = ""; };
        void Add()
        {
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                feedback.Text = L10n.T("Enter a task name.");
                input.Focus();
                return;
            }
            DateTime? due = null;
            if (schedule.IsVisible)
            {
                if (invalidDate || reminder.SelectedDate is not { } date)
                {
                    feedback.Text = L10n.T("Enter a valid date.");
                    return;
                }
                if (!TryReadClock(reminderTime.Text, out var time))
                {
                    feedback.Text = L10n.T("Enter a valid time in HH:mm format.");
                    reminderTime.Focus();
                    return;
                }
                due = date.Date + time;
            }
            model.AddTodo(input.Text, due);
            input.Clear();
            reminder.SelectedDate = null;
            schedule.Visibility = Visibility.Collapsed;
            feedback.Text = "";
            RefreshTodos();
        }
        input.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Add(); e.Handled = true; } };
        var add = Button(Symbol("\uE710", 14), Add, Brush("#3A3A3C"), 15);
        System.Windows.Automation.AutomationProperties.SetName(add, L10n.T("Add task"));
        var toggle = Button(Symbol("\uE823", 15), () =>
        {
            schedule.Visibility = schedule.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (schedule.Visibility == Visibility.Visible && reminder.SelectedDate is null) reminder.SelectedDate = DateTime.Today;
            feedback.Text = "";
        }, Brushes.Transparent);
        toggle.ToolTip = L10n.T("Reminder date");
        System.Windows.Automation.AutomationProperties.SetName(toggle, L10n.T("Schedule reminder"));
        // Запись голосом в задачи: первое нажатие начинает запись, второе останавливает и распознаёт. Без помощника вся фраза становится одной задачей;
        // с выбранным помощником текст разбирается на несколько задач со сроками, а заметка из разбора дописывается в заметки (ничего не теряется).
        var micGlyph = Symbol("\uE720", 15);
        var mic = Button(micGlyph, () => { }, Brushes.Transparent, 15);
        bool micBusy = false, micMine = false;
        var micClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        void Say(string message, bool error = false)
        {
            feedback.Foreground = error ? Brush("#FF9088") : Muted;
            feedback.Text = message;
        }
        void PaintMic()
        {
            micGlyph.Text = micMine ? "\uE71A" : "\uE720";
            mic.Background = micMine ? Brush("#E5484D") : Brushes.Transparent;
            mic.IsEnabled = !micBusy;
            string name = L10n.T(micMine ? "Stop dictation" : "Dictate a task");
            System.Windows.Automation.AutomationProperties.SetName(mic, name);
            mic.ToolTip = name;
        }
        async System.Threading.Tasks.Task RecordTask(bool limit = false)
        {
            if (micBusy) return;
            micBusy = true;
            PaintMic();
            string? wav = null;
            try
            {
                if (!Dictation.IsRecording)
                {
                    bool local = P.VoiceEngine == "local";
                    if (!Dictation.Fixture && local && !LocalVoice.Ready(LocalVoice.Selected(P.VoiceModel)))
                    {
                        Say(L10n.T("Download the built-in engine first."), true);
                        return;
                    }
                    if (!Dictation.Fixture && !local && Dictation.HandyPath() is null)
                    {
                        Say(L10n.T("Handy was not found. Install Handy and its GigaAM v3 model to recognise speech."), true);
                        return;
                    }
                    Say(L10n.T("Connecting the microphone…"));
                    await Dictation.StartAsync();
                    micMine = true;
                    micClock.Start();
                    Say(L10n.T("Recording… press the button to stop."));
                }
                else if (micMine)
                {
                    micClock.Stop();
                    micMine = false;
                    Say(L10n.T("Recognising speech…"));
                    wav = await Dictation.StopAsync();
                    string text = P.VoiceEngine == "local" && !Dictation.Fixture
                        ? await LocalVoice.TranscribeAsync(LocalVoice.Selected(P.VoiceModel), wav, System.Threading.CancellationToken.None)
                        : await Dictation.TranscribeAsync(wav, System.Threading.CancellationToken.None,
                            (index, total) => Say(total > 1 ? L10n.F($"Recognising part {index} of {total}…") : L10n.T("Recognising speech…")));
                    text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                    if (text.Length == 0) { Say(L10n.T("No speech was recognised."), true); return; }
                    VoiceAssistant.Plan? plan = null;
                    if (P.VoiceProvider != "none")
                    {
                        Say(L10n.F($"{VoiceAssistant.DisplayName(P.VoiceProvider)} is analysing the dictation…"));
                        try { plan = await VoiceAssistant.StructureAsync(P.VoiceProvider, text, System.Threading.CancellationToken.None); }
                        catch (Exception ex) when (ex is not OperationCanceledException) { plan = null; } // помощник недоступен: фраза уйдёт одной задачей
                    }
                    int added = 0;
                    if (plan is not null && plan.Tasks.Count > 0)
                    {
                        foreach (var task in plan.Tasks) { model.AddTodo(task.Text, task.Due); added++; }
                        if (plan.Note.Length > 0) Dictation.AppendToNotes(model, plan.Note);
                    }
                    else { model.AddTodo(text); added = 1; }
                    RefreshTodos();
                    Say(added == 1 ? L10n.T("Task added.") : L10n.F($"Tasks added: {added}"));
                }
                else Say(L10n.T("Recording is already running in the dictation window."), true);
            }
            catch (Dictation.DictationException ex) { micClock.Stop(); micMine = false; Say(L10n.T(ex.Message), true); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                micClock.Stop();
                micMine = false;
                Say(L10n.T("The recording could not be recognised."), true);
            }
            finally
            {
                Dictation.Discard(wav);
                micBusy = false;
                PaintMic();
            }
        }
        micClock.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - Dictation.StartedAt;
            if (elapsed.TotalMinutes >= Dictation.MaxMinutes) _ = RecordTask(true);
        };
        mic.Click += (_, _) => _ = RecordTask();
        // окно закрыли во время записи: запись выбрасывается, микрофон освобождается
        content.Unloaded += async (_, _) =>
        {
            micClock.Stop();
            if (micMine && Dictation.IsRecording)
            {
                micMine = false;
                try { Dictation.Discard(await Dictation.StopAsync()); }
                catch (Exception ex) when (ex is Dictation.DictationException or InvalidOperationException) { }
            }
        };
        PaintMic();
        var footer = Stack(feedback, Right(input, RowStack(schedule, toggle, mic, add)));
        footer.Margin = new(0, 10, 0, 0);
        DockPanel.SetDock(footer, Dock.Bottom);
        content.Children.Add(footer);
        content.Children.Add(body);
        RefreshTodos();
    }

    private void TimeSurface(string id, Window window, DockPanel parent)
    {
        var time = Text("", 36, true);
        time.HorizontalAlignment = HorizontalAlignment.Center;
        var countdown = id == "Pomodoro" ? model.Focus : model.Timer;
        bool stopwatch = id == "Stopwatch";
        var minutes = Input("Minutes", Math.Ceiling(countdown.Duration.TotalMinutes).ToString(CultureInfo.InvariantCulture));
        var validation = Text("", 12, false, Brush("#FF9088"));
        validation.HorizontalAlignment = HorizontalAlignment.Center;
        bool ValidDuration(out int value)
        {
            bool valid = int.TryParse(minutes.Text, out value) && value is >= 1 and <= 1440;
            validation.Text = valid ? "" : L10n.T("Enter a duration from 1 to 1440 minutes.");
            return valid;
        }
        var play = Button("Start / pause", () =>
        {
            if (id == "Timer" && !countdown.Running && !ValidDuration(out _)) { minutes.Focus(); return; }
            if (!stopwatch) countdown.Toggle();
            else if (model.Stopwatch.IsRunning) model.Stopwatch.Stop();
            else model.Stopwatch.Start();
        }, Accent(P));
        var controls = RowStack(play, Button(id == "Pomodoro" ? "Skip" : "Reset", () =>
        {
            if (id == "Pomodoro") model.NextFocus();
            else if (stopwatch) model.Stopwatch.Reset();
            else if (ValidDuration(out var number))
                countdown.Reset(TimeSpan.FromMinutes(number));
        }));
        controls.HorizontalAlignment = HorizontalAlignment.Center;
        var body = Stack(time, controls);
        if (id == "Timer")
        {
            minutes.Width = 62;
            minutes.TextChanged += (_, _) =>
            {
                if (!countdown.Running && ValidDuration(out var value))
                    countdown.Reset(TimeSpan.FromMinutes(value));
            };
            var label = Text("Minutes");
            label.Margin = new(7, 0, 0, 0);
            var edit = RowStack(minutes, label);
            edit.HorizontalAlignment = HorizontalAlignment.Center;
            edit.Margin = new(0, 7, 0, 0);
            body.Children.Add(edit);
            body.Children.Add(validation);
        }
        parent.Children.Add(body);
        Live(window, () => time.Text = Widgets.Duration(stopwatch ? model.Stopwatch.Elapsed : countdown.Left));
    }

    private void AudioSurface(DockPanel parent, bool capture = false)
    {
        var panel = Stack();
        var deviceRows = Stack();
        var mixerRows = Stack();
        var endpoint = capture ? model.Microphone : model.Audio;
        var info = Text(endpoint is null ? "No audio device" : capture ? "Input level" : "Master volume", 13, true, Muted);
        var notice = Text("", 12, false, Muted);
        void Set(double? level, bool toggle)
        {
            endpoint = WindowsShell.Audio(level, toggle, capture);
            if (capture) model.Microphone = endpoint;
            else model.Audio = endpoint;
        }
        var slider = FilledSlider(Accent(P), 100 * (endpoint?.Volume ?? 0), 32, capture ? "Input level" : "Master volume");
        slider.IsEnabled = endpoint.HasValue;
        var mute = Text(endpoint?.Muted == true ? "Off" : $"{slider.Value:F0}%", 13, true);
        bool refreshing = false;
        slider.ValueChanged += (_, _) =>
        {
            if (refreshing) return;
            Set(slider.Value / 100, false);
            mute.Text = endpoint?.Muted == true ? L10n.T("Off") : $"{slider.Value:F0}%";
        };
        panel.Children.Add(info);
        var master = Right(slider, Button(mute, () =>
        {
            Set(null, true);
            mute.Text = endpoint?.Muted == true ? L10n.T("Off") : $"{slider.Value:F0}%";
        }, Brush("#38383C"), 11));
        master.Margin = new(0, 7, 0, 6);
        panel.Children.Add(master);
        if (!capture)
        {
            var apps = Text("APPS", 11, true, Muted);
            apps.Margin = new(4, 13, 0, 6);
            panel.Children.Add(apps);
            panel.Children.Add(mixerRows);
        }
        var devicesLabel = Text(capture ? "INPUT" : "OUTPUT", 11, true, Muted);
        devicesLabel.Margin = new(4, 13, 0, 6);
        panel.Children.Add(devicesLabel);
        panel.Children.Add(deviceRows);
        panel.Children.Add(notice);
        var footer = RowStack(Button("Refresh", RefreshAudio), Button("Sound Settings", () => WindowsShell.Launch("ms-settings:sound")));
        footer.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Margin = new(0, 9, 0, 0);
        panel.Children.Add(footer);
        parent.Children.Add(Scroll(panel));
        RefreshAudio();

        void RefreshAudio()
        {
            refreshing = true;
            Set(null, false);
            slider.IsEnabled = endpoint.HasValue;
            slider.Value = 100 * (endpoint?.Volume ?? 0);
            mute.Text = endpoint?.Muted == true ? L10n.T("Off") : $"{slider.Value:F0}%";
            refreshing = false;
            var devices = WindowsShell.AudioDevices(capture);
            info.Text = devices.FirstOrDefault(device => device.Selected)?.Name ?? L10n.T(capture ? "Input level" : "Master volume");
            deviceRows.Children.Clear();
            var outputs = SettingsGroup(deviceRows);
            foreach (var device in devices)
            {
                var mark = Symbol("\uE73E", 15, Accent(P));
                mark.Opacity = device.Selected ? 1 : 0;
                var option = Button(Right(RawText(device.Name, 13, true), mark), () =>
                {
                    if (device.Selected) return;
                    notice.Text = WindowsShell.SetDefaultAudioDevice(device.Id, capture)
                        ? "" : L10n.T("Windows could not select this device.");
                    RefreshAudio();
                }, Brushes.Transparent, 4);
                option.Height = 39;
                outputs.Children.Add(option);
            }
            if (devices.Length == 0)
                deviceRows.Children.Add(Text("No devices available in this session", 12, false, Muted));
            if (!capture)
            {
                mixerRows.Children.Clear();
                var mixer = SettingsGroup(mixerRows);
                var sessions = smoke
                    ? new[] { new WindowsShell.AudioSession("preview", "Example player", .64, false) }
                    : WindowsShell.SessionVolumes();
                foreach (var session in sessions)
                {
                    var knob = FilledSlider(Accent(P), session.Volume * 100, 14, L10n.F($"Volume · {session.Name}"));
                    knob.Width = 108;
                    var percentage = Text($"{knob.Value:F0}%", 11, true, Muted);
                    percentage.Width = 37;
                    percentage.TextAlignment = TextAlignment.Right;
                    percentage.Margin = new(4, 0, 6, 0);
                    knob.ValueChanged += (_, _) =>
                    {
                        if (!smoke) WindowsShell.SessionVolumes(session.Id, (float)(knob.Value / 100.0));
                        percentage.Text = $"{knob.Value:F0}%";
                    };
                    var muteGlyph = Symbol("", 14);
                    var muteButton = Button(muteGlyph, () => { }, Brush("#38383C"), 9);
                    muteButton.Width = 31;
                    muteButton.Height = 28;
                    muteButton.Padding = new(0);
                    bool isMuted = session.Muted;
                    void PaintMute()
                    {
                        muteGlyph.Text = isMuted ? "\uE74F" : "\uE767";
                        string action = L10n.T(isMuted ? "Unmute" : "Mute");
                        muteButton.ToolTip = action;
                        System.Windows.Automation.AutomationProperties.SetName(muteButton, action);
                        knob.Foreground = isMuted ? Muted : Accent(P);
                    }
                    muteButton.Click += (_, _) =>
                    {
                        if (smoke) isMuted = !isMuted;
                        else if (WindowsShell.SessionVolumes(session.Id, mute: true).FirstOrDefault() is { } after) isMuted = after.Muted;
                        PaintMute();
                    };
                    PaintMute();
                    var icon = Symbol("\uE7F5", 15, Muted);
                    icon.Margin = new(0, 0, 9, 0);
                    var appName = RawText(session.Name, 13, true);
                    appName.ToolTip = session.Name;
                    var app = new DockPanel();
                    DockPanel.SetDock(icon, Dock.Left);
                    app.Children.Add(icon);
                    app.Children.Add(appName);
                    SettingsRow(mixer, app, RowStack(knob, percentage, muteButton));
                }
                if (mixer.Children.Count == 0) mixer.Children.Add(Text("No application audio sessions", 12, false, Muted));
            }
        }
    }

    private void AppsSurface(DockPanel parent)
    {
        var input = Input("Search apps");
        DockPanel.SetDock(input, Dock.Top);
        parent.Children.Add(input);
        var list = Stack();
        parent.Children.Add(Scroll(list));
        var apps = WindowsShell.StartMenuShortcuts();
        void Filter()
        {
            list.Children.Clear();
            foreach (var app in apps.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Path.GetFileNameWithoutExtension))
            {
                string name = Path.GetFileNameWithoutExtension(app);
                if (!name.Contains(input.Text, StringComparison.CurrentCultureIgnoreCase)) continue;
                string id = "app:" + app;
                list.Children.Add(Right(Button(RawText(name, 14, true), () => WindowsShell.Launch(app)),
                    Button(Items.Contains(id) ? "Unpin" : "Pin", () => { model.ToggleWidget(id, Items); Filter(); })));
            }
        }
        input.TextChanged += (_, _) => Filter();
        Filter();
    }

    private static void Live(Window window, Action update)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        update();
        timer.Tick += (_, _) => update();
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
    }
}
