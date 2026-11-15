// Установка Handy по нажатию: последняя версия сама скачивается с официальной страницы выпусков проекта на GitHub (cjpais/Handy),
// проверяется по контрольной сумме SHA-256 и запускается её установщик. Сайт открывать не нужно.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;

namespace FlowBar
{
    partial class Bar
    {
        const string HandyLatestApi = "https://api.github.com/repos/cjpais/Handy/releases/latest";
        static bool handyDownloading;

        // Установщик из ответа GitHub: имя, адрес и SHA-256. Берётся только файл Handy_<версия>_x64-setup.exe (или arm64) с github.com из выпусков cjpais/Handy
        // и только с контрольной суммой, которую сообщает GitHub, иначе null.
        internal static Tuple<string, string, string> PickHandyInstaller(string json, bool arm)
        {
            var root = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.DeserializeObject(json) as Dictionary<string, object>;
            object list; if (root == null || !root.TryGetValue("assets", out list)) return null;
            var assets = list as object[]; if (assets == null) return null;
            var wanted = new Regex(@"^Handy_[0-9][0-9A-Za-z.\-]*_" + (arm ? "arm64" : "x64") + @"-setup\.exe$", RegexOptions.CultureInvariant);
            foreach (var item in assets)
            {
                var a = item as Dictionary<string, object>; if (a == null) continue;
                var name = a.ContainsKey("name") ? a["name"] as string : null;
                var url = a.ContainsKey("browser_download_url") ? a["browser_download_url"] as string : null;
                var digest = a.ContainsKey("digest") ? a["digest"] as string : null;
                if (name == null || url == null || !wanted.IsMatch(name)) continue;
                Uri uri; if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com" ||
                    !uri.AbsolutePath.StartsWith("/cjpais/Handy/releases/download/", StringComparison.Ordinal)) continue;
                if (digest == null || !Regex.IsMatch(digest, @"^sha256:[0-9a-fA-F]{64}$")) continue;
                return Tuple.Create(name, url, digest.Substring(7).ToLowerInvariant());
            }
            return null;
        }

        static bool IsArmWindows()
        {
            var arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
            return string.Equals(arch, "ARM64", StringComparison.OrdinalIgnoreCase);
        }

        static string HttpText(string url)
        {
            var rq = (HttpWebRequest)WebRequest.Create(url);
            rq.UserAgent = "FlowBar"; rq.Accept = "application/vnd.github+json"; rq.Timeout = 20000; rq.ReadWriteTimeout = 20000;
            using (var rs = rq.GetResponse()) using (var rd = new StreamReader(rs.GetResponseStream(), System.Text.Encoding.UTF8)) return rd.ReadToEnd();
        }

        // скачать установщик во временную папку и сверить SHA-256; progress — доля от 0 до 100
        static string FetchHandyInstaller(Tuple<string, string, string> pick, Action<int> progress)
        {
            var dir = Path.Combine(Path.GetTempPath(), "FlowBar-handy");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, pick.Item1);
            var rq = (HttpWebRequest)WebRequest.Create(pick.Item2);
            rq.UserAgent = "FlowBar"; rq.Timeout = 30000; rq.ReadWriteTimeout = 30000;
            using (var rs = rq.GetResponse())
            {
                long total = rs.ContentLength, got = 0; int last = -1; var buf = new byte[81920];
                using (var src = rs.GetResponseStream()) using (var dst = File.Create(file))
                {
                    int n; while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        dst.Write(buf, 0, n); got += n;
                        if (total > 0) { int pct = (int)(got * 100 / total); if (pct / 5 != last / 5) { last = pct; progress(pct); } }
                    }
                }
            }
            string sum; using (var sha = System.Security.Cryptography.SHA256.Create()) using (var fs = File.OpenRead(file))
                sum = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            if (sum != pick.Item3) { try { File.Delete(file); } catch { } throw new InvalidDataException("контрольная сумма Handy не совпала"); }
            return file;
        }

        // Handy 0.10.1 загружает настройки до регистрации клавиш и дополняет частичный файл значениями по умолчанию.
        internal static bool SeedHandyShortcut(string file)
        {
            if (File.Exists(file)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var shortcut = new Dictionary<string, object>
            {
                { "id", "transcribe" }, { "name", "Transcribe" },
                { "description", "Converts your speech into text." },
                { "default_binding", "ctrl+space" }, { "current_binding", "ctrl_right+space" }
            };
            var settings = new Dictionary<string, object>
            {
                { "keyboard_implementation", "handy_keys" },
                { "bindings", new Dictionary<string, object> { { "transcribe", shortcut } } }
            };
            var root = new Dictionary<string, object> { { "settings", settings } };
            string pending = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(pending, new JavaScriptSerializer().Serialize(root), new System.Text.UTF8Encoding(false));
                try { File.Move(pending, file); }
                catch (IOException) { if (File.Exists(file)) return false; throw; }
                return true;
            }
            finally { try { File.Delete(pending); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        // по нажатию: найти последнюю версию, скачать, проверить, запустить установщик. status — подпись для пользователя
        static async Task DownloadHandy(Action<string> status)
        {
            if (DemoMode) { status("В деморежиме скачивание отключено."); return; }
            if (handyDownloading) { status("Handy уже скачивается."); return; }
            handyDownloading = true;
            try
            {
                status("Ищем последнюю версию Handy…");
                var arm = IsArmWindows();
                var pick = await Task.Run(() => PickHandyInstaller(HttpText(HandyLatestApi), arm));
                if (pick == null) throw new InvalidDataException("в последнем выпуске нет подходящего установщика");
                var ui = Application.Current.Dispatcher;
                status("Скачиваем " + pick.Item1 + "…");
                var path = await Task.Run(() => FetchHandyInstaller(pick, pct => ui.BeginInvoke(new Action(() => status("Скачиваем " + pick.Item1 + ": " + pct + "%")))));
                string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "com.pais.handy", "settings_store.json");
                bool prepared = SeedHandyShortcut(profile);
                status("Handy скачан и проверен. Завершите установку в его окне." +
                    (prepared ? " Для Handy задан правый Ctrl+Пробел." : ""));
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log(ex);
                status("Не удалось скачать Handy автоматически. Проверьте интернет и попробуйте ещё раз или откройте «Выпуски Handy».");
            }
            finally { handyDownloading = false; }
        }
    }
}
