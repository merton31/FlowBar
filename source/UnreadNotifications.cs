using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace LumaDock;

// Счётчики «непрочитанного» по уведомлениям Windows: сколько всплывающих уведомлений приложения лежит в центре уведомлений.
// Нужен доступ «Уведомления» в параметрах конфиденциальности Windows; без него виджет просит разрешение и открывает эту страницу.
internal static class UnreadNotifications
{
    internal enum Access { Unknown, Allowed, Denied }

    internal static bool Fixture { get; set; }
    public static Access State { get; private set; }

    private const string WhatsAppApp = "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App";
    private static IReadOnlyDictionary<string, int> counts = new Dictionary<string, int>();
    private static DateTime last = DateTime.MinValue;
    private static int busy;

    // Сколько уведомлений у приложений, в идентификаторе которых встречается app (например «WhatsApp»)
    public static int Count(string app) =>
        counts.Where(pair => pair.Key.Contains(app, StringComparison.OrdinalIgnoreCase)).Sum(pair => pair.Value);

    public static async Task Refresh()
    {
        if (Fixture)
        {
            State = Access.Allowed;
            counts = new Dictionary<string, int> { [WhatsAppApp] = 3 };
            return;
        }
        if (DateTime.UtcNow - last < TimeSpan.FromSeconds(5) || Interlocked.Exchange(ref busy, 1) == 1) return;
        try
        {
            last = DateTime.UtcNow;
            var listener = UserNotificationListener.Current;
            var status = listener.GetAccessStatus();
            if (status == UserNotificationListenerAccessStatus.Unspecified) status = await listener.RequestAccessAsync();
            if (status != UserNotificationListenerAccessStatus.Allowed) { State = Access.Denied; return; }
            var toasts = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            var next = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var toast in toasts)
            {
                string? app = null;
                try { app = toast.AppInfo?.AppUserModelId; }
                catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
                if (!string.IsNullOrEmpty(app)) next[app] = next.GetValueOrDefault(app) + 1;
            }
            counts = next;
            State = Access.Allowed;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or System.IO.FileNotFoundException or NotSupportedException)
        {
            State = Access.Denied;
        }
        finally { Volatile.Write(ref busy, 0); }
    }

    // Щелчок по виджету: без разрешения — страница параметров, иначе — сам WhatsApp
    public static void OpenWhatsApp() =>
        WindowsShell.Launch(State == Access.Denied ? "ms-settings:privacy-notifications" : "shell:AppsFolder\\" + WhatsAppApp);
}
