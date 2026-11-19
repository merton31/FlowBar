using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace LumaDock;

// Лента TikTok в небольшом окне: встроенный браузер WebView2 с мобильной раскладкой, лента листается колесом мыши и перетаскиванием.
// Окно можно тянуть за край, размер запоминается. Вход в аккаунт хранится в своей папке профиля браузера на этом устройстве.
internal sealed class TikTokFeed : Window
{
    private const string MobileAgent = "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36";
    private static TikTokFeed? current;
    private readonly WebView2 view = new();
    private readonly DockModel? model;
    private readonly string address, dataFolder;

    // Нажатие на виджет открывает ленту, повторное закрывает
    public static void Toggle(DockModel model)
    {
        if (current is not null) { current.Close(); return; }
        var area = SystemParameters.WorkArea;
        var feed = new TikTokFeed(model, "https://www.tiktok.com/foryou", model.Settings.TikTokWidth, model.Settings.TikTokHeight, Path.Combine(AppData.Root, "tiktok"));
        feed.Left = Math.Max(area.Left, area.Right - feed.Width - 16);
        feed.Top = Math.Max(area.Top, area.Bottom - feed.Height - 96);
        current = feed;
        feed.Show();
    }

    private TikTokFeed(DockModel? model, string address, double width, double height, string dataFolder)
    {
        this.model = model;
        this.address = address;
        this.dataFolder = dataFolder;
        Title = "TikTok";
        Width = Math.Clamp(width, 260, 900);
        Height = Math.Clamp(height, 380, 1200);
        MinWidth = 260;
        MinHeight = 380;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = new SolidColorBrush(Color.FromRgb(16, 16, 16));
        Content = view;
        Loaded += async (_, _) => await Start(); // один раз: повторный вызов вернёт тот же запуск
        Closing += (_, _) =>
        {
            if (model is not null && WindowState == WindowState.Normal)
            {
                model.Settings.TikTokWidth = Math.Round(ActualWidth);
                model.Settings.TikTokHeight = Math.Round(ActualHeight);
                model.Save(false);
            }
        };
        Closed += (_, _) =>
        {
            if (ReferenceEquals(current, this)) current = null;
            view.Dispose();
        };
    }

    public CoreWebView2? Browser => view.CoreWebView2;

    // Запускает браузер; без установленного WebView2 вместо ленты показывается объяснение
    private System.Threading.Tasks.Task? starting;
    public System.Threading.Tasks.Task Start() => starting ??= Begin();

    private async System.Threading.Tasks.Task Begin()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, dataFolder);
            await view.EnsureCoreWebView2Async(environment);
            var settings = view.CoreWebView2.Settings;
            settings.UserAgent = MobileAgent;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            // ссылки открываются в этом же окне, а не в отдельном
            view.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; view.CoreWebView2.Navigate(e.Uri); };
            view.Source = new Uri(address);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException or UriFormatException)
        {
            ErrorLog.Write(ex, "TikTok");
            Content = new TextBlock { Text = L10n.T("TikTok needs Microsoft Edge WebView2, which is not installed on this device."), Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18), VerticalAlignment = VerticalAlignment.Center };
        }
    }

    // FlowBar.exe --check-tiktok <папка>: настоящий WebView2 в окне за пределами экрана, страница из текста, прокрутка и мобильный агент. Результат в result.txt, успех — OK
    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var report = new StringBuilder();
        int code = 0;
        var frame = new DispatcherFrame();
        async void Go()
        {
            TikTokFeed? feed = null;
            try
            {
                feed = new TikTokFeed(null, "about:blank", 380, 640, Path.Combine(folder, "profile")) { Left = -4000, Top = 0 };
                feed.Show();
                await feed.Start();
                var browser = feed.Browser ?? throw new InvalidOperationException("WebView2 не запустился");
                var loaded = new System.Threading.Tasks.TaskCompletionSource();
                browser.NavigationCompleted += (_, _) => loaded.TrySetResult();
                browser.NavigateToString("<html><body style='margin:0'><div style='height:4000px'>лента</div></body></html>");
                await loaded.Task;
                string agent = await browser.ExecuteScriptAsync("navigator.userAgent");
                string scrolled = await browser.ExecuteScriptAsync("window.scrollTo(0, 500); String(Math.round(window.scrollY))");
                report.AppendLine("Агент: " + agent);
                report.AppendLine("Прокрутка: " + scrolled);
                if (!agent.Contains("Android") || scrolled.Trim('"') != "500") throw new InvalidOperationException("Мобильная страница не прокручивается");
                report.AppendLine("OK");
            }
            catch (Exception ex) { report.AppendLine("СБОЙ: " + ex.GetType().Name + ": " + ex.Message); code = 1; }
            finally
            {
                feed?.Close();
                frame.Continue = false;
            }
        }
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(Go)); // WebView2 запускается только внутри работающего цикла сообщений
        Dispatcher.PushFrame(frame);
        File.WriteAllText(Path.Combine(folder, "result.txt"), report.ToString(), Encoding.UTF8);
        return code;
    }
}
