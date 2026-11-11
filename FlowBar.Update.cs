// Установка и обновление.
// Программа живёт в %LOCALAPPDATA%\Programs\FlowBar, личные данные — отдельно, в %APPDATA%\FlowBar (настройки, закрепления, группы,
// заметки, задачи, календари, секундомер, экранное время) — обновление их не меняет.
// Новый FlowBar.exe, запущенный откуда угодно (например, из «Загрузок»), сравнивает версию с установленной:
// установленная старше или её нет — резервная копия данных, бережное закрытие работающей панели, замена файлов программы, запуск новой версии;
// установленная новее — просто запускается она (старый exe не откатывает обновление).
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

[assembly: AssemblyVersion("1.6.2.0")]
[assembly: AssemblyFileVersion("1.6.2.0")]
[assembly: AssemblyTitle("FlowBar")]
[assembly: AssemblyProduct("FlowBar")]

namespace FlowBar
{
    static class Setup
    {
        public static readonly Version Current = Assembly.GetExecutingAssembly().GetName().Version;
        public static string VersionText { get { return V3(Current); } }
        public static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FlowBar");
        public static string InstalledExe { get { return Path.Combine(InstallDir, "FlowBar.exe"); } }
        static string Self { get { return Assembly.GetExecutingAssembly().Location; } }
        // по маркеру новая версия после запуска сообщает, что обновилась
        static string Marker { get { return Path.Combine(Bar.Dir, "updated.txt"); } }
        const string Config = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<configuration>\r\n  <startup>\r\n    <supportedRuntime version=\"v4.0\" sku=\".NETFramework,Version=v4.6.2\"/>\r\n  </startup>\r\n  <runtime>\r\n    <AppContextSwitchOverrides value=\"Switch.System.Windows.DoNotScaleForDpiChanges=false\"/>\r\n  </runtime>\r\n</configuration>\r\n";

        public static bool IsInstalledCopy { get { return string.Equals(Path.GetFullPath(Path.GetDirectoryName(Self)).TrimEnd('\\'), Path.GetFullPath(InstallDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); } }

        public static string V3(Version v) { return v.Major + "." + v.Minor + "." + Math.Max(0, v.Build); }
        static Version VersionOf(string exe)
        {
            try { if (!File.Exists(exe)) return null; var v = FileVersionInfo.GetVersionInfo(exe).FileVersion; return string.IsNullOrEmpty(v) ? new Version(0, 0) : new Version(v); }
            catch { return new Version(0, 0); } // старая сборка без номера версии — считаем самой старой
        }

        // true — работу передали установленной копии, этот процесс завершается
        public static bool HandOver()
        {
            if (IsInstalledCopy) { FixAutostart(); return false; }
            var inst = VersionOf(InstalledExe);
            try
            {
                if (inst != null && inst > Current) { Process.Start(InstalledExe); return true; } // установлена более новая — не откатываем
                bool same = inst != null && inst == Current && SameFile(Self, InstalledExe);
                if (!same)
                {
                    Backup(Bar.Dir, Path.Combine(Bar.Dir, "backups"), inst == null ? "new" : V3(inst));
                    StopRunning();
                    Install();
                    try { File.WriteAllText(Marker, (inst == null ? "" : V3(inst)) + "|" + VersionText); } catch { }
                }
                else StopRunning(); // та же версия запущена из другого места — оставляем одну, установленную
                Process.Start(new ProcessStartInfo(InstalledExe) { WorkingDirectory = InstallDir });
                return true;
            }
            catch (Exception ex)
            {
                // установить не удалось — работаем прямо отсюда, как раньше
                Bar.Log(new Exception("Установка в " + InstallDir + " не удалась, FlowBar запущен из своей папки", ex));
                return false;
            }
        }

        static bool SameFile(string a, string b)
        {
            try { var fa = new FileInfo(a); var fb = new FileInfo(b); return fb.Exists && fa.Length == fb.Length && File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b)); } catch { return false; }
        }

        // работающая панель: сначала просим выйти (сохраняет экранное время и возвращает панель Windows), через 4 с — принудительно
        static void StopRunning()
        {
            var me = Process.GetCurrentProcess();
            // у панели есть окна; у серверов MCP для агентов (--mcp) их нет, команду «выйти» они не получают — закрываем сразу, без 4 с ожидания.
            // Окна считаем до команды: выходящая панель закрывает их раньше, чем вернёт панель Windows
            var withWindows = new System.Collections.Generic.HashSet<uint>();
            N.EnumWindows((h, x) => { uint pid; N.GetWindowThreadProcessId(h, out pid); withWindows.Add(pid); return true; }, IntPtr.Zero);
            Program.PostToBar(N.RegisterWindowMessage("FlowBarOpen"), (IntPtr)Array.IndexOf(Bar.OpenNames, "quit"), IntPtr.Zero);
            foreach (var p in Process.GetProcessesByName("FlowBar").Where(p => p.Id != me.Id))
                try { if (!p.WaitForExit(withWindows.Contains((uint)p.Id) ? 4000 : 0)) { p.Kill(); p.WaitForExit(3000); } } catch { }
            // сторож уходит сам, когда панель закрылась; ждём, иначе его файл занят
            foreach (var p in Process.GetProcessesByName("flowbar-watch")) try { if (!p.WaitForExit(3000)) p.Kill(); } catch { }
        }

        static void Install()
        {
            Directory.CreateDirectory(InstallDir);
            string src = Path.GetDirectoryName(Self);
            Copy(Self, InstalledExe);
            var cfg = Path.Combine(src, "FlowBar.exe.config");
            if (File.Exists(cfg)) Copy(cfg, InstalledExe + ".config"); else File.WriteAllText(InstalledExe + ".config", Config); // без него неверный масштаб на мониторах с разным DPI
            var watch = Path.Combine(src, "flowbar-watch.exe");
            if (File.Exists(watch)) Copy(watch, Path.Combine(InstallDir, "flowbar-watch.exe"));
            var guide = Path.Combine(src, "Как пользоваться.txt");
            if (File.Exists(guide)) Copy(guide, Path.Combine(InstallDir, "Как пользоваться.txt"));
            Shortcut();
            FixAutostart(InstalledExe);
        }

        // файл может быть занят ещё полсекунды после выхода процесса
        static void Copy(string from, string to)
        {
            for (int i = 0; ; i++)
            {
                try { File.Copy(from, to, true); return; }
                catch (IOException) { if (i >= 15) throw; System.Threading.Thread.Sleep(300); }
                catch (UnauthorizedAccessException) { if (i >= 15) throw; System.Threading.Thread.Sleep(300); }
            }
        }

        // ярлык в меню «Пуск», чтобы FlowBar находился поиском Windows
        static void Shortcut()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "FlowBar.lnk");
                var sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                var lnk = sh.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { path });
                Action<string, object> set = (n, v) => lnk.GetType().InvokeMember(n, BindingFlags.SetProperty, null, lnk, new[] { v });
                set("TargetPath", InstalledExe); set("WorkingDirectory", InstallDir); set("Description", "FlowBar — панель задач");
                lnk.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch (Exception ex) { Bar.Log(ex); }
        }

        // включённый автозапуск указывает на установленную копию, а не на старое место
        static void FixAutostart(string exe = null)
        {
            try
            {
                exe = exe ?? Self;
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    var v = k == null ? null : k.GetValue("FlowBar") as string;
                    if (v != null && !string.Equals(v.Trim('"'), exe, StringComparison.OrdinalIgnoreCase)) k.SetValue("FlowBar", "\"" + exe + "\"");
                }
            }
            catch (Exception ex) { Bar.Log(ex); }
        }

        // Права администратора — только по желанию (settings.txt: admin=1; по умолчанию без них: с правами Windows не пускает перетаскивание
        // из Проводника и автоматизацию в окно панели). Первый раз — запрос UAC; копия с правами создаёт задачу планировщика «FlowBar»
        // с наивысшими правами, дальше автозапуск и ярлык запускают панель через неё, без запроса.
        // true — запущена копия с правами, этот процесс завершается.
        public static bool Elevate(string[] args)
        {
            try
            {
                if (!File.ReadAllLines(Path.Combine(Bar.Dir, "settings.txt")).Contains("admin=1")) return false;
            }
            catch { return false; }
            if (Elevated) { if (IsInstalledCopy) System.Threading.Tasks.Task.Run(() => MakeTask()); return false; }
            if (ElevationType() != 3 || args.Contains("--fromtask")) return false; // не администратор (или задача не дала прав) — без прав, не зацикливаемся
            try
            {
                if (IsInstalledCopy && Schtasks("/Run /TN FlowBar") == 0) return true;
                Process.Start(new ProcessStartInfo(Self) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(Self) });
                return true;
            }
            catch (Exception ex) { Bar.Log(new Exception("Без прав администратора", ex)); return false; } // отказ в окне UAC — работаем без прав
        }

        public static readonly bool Elevated = IsAdmin();
        static bool IsAdmin()
        {
            using (var id = System.Security.Principal.WindowsIdentity.GetCurrent()) return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        [System.Runtime.InteropServices.DllImport("advapi32.dll")] static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int len, out int ret);
        // 3 — администратор с UAC, сейчас без прав
        static int ElevationType()
        {
            using (var id = System.Security.Principal.WindowsIdentity.GetCurrent()) { int t, n; return GetTokenInformation(id.Token, 18, out t, 4, out n) ? t : 0; }
        }

        static int Schtasks(string a)
        {
            using (var p = Process.Start(new ProcessStartInfo("schtasks.exe", a) { UseShellExecute = false, CreateNoWindow = true })) { p.WaitForExit(15000); return p.HasExited ? p.ExitCode : -1; }
        }

        // без триггеров (запускают автозапуск и ярлык), без ограничения времени и без остановки от батареи — иначе планировщик снимет панель через 3 дня
        static void MakeTask()
        {
            try
            {
                string sid; using (var id = System.Security.Principal.WindowsIdentity.GetCurrent()) sid = id.User.Value;
                Func<string, string> x = System.Security.SecurityElement.Escape;
                var xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n"
                    + "<RegistrationInfo><Description>FlowBar с правами администратора</Description></RegistrationInfo>\r\n"
                    + "<Principals><Principal id=\"Author\"><UserId>" + sid + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n"
                    + "<Settings><MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>"
                    + "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>4</Priority></Settings>\r\n"
                    + "<Actions Context=\"Author\"><Exec><Command>\"" + x(InstalledExe) + "\"</Command><Arguments>--fromtask</Arguments><WorkingDirectory>" + x(InstallDir) + "</WorkingDirectory></Exec></Actions>\r\n</Task>\r\n";
                var f = Path.Combine(Path.GetTempPath(), "flowbar-task.xml");
                File.WriteAllText(f, xml, System.Text.Encoding.Unicode);
                int rc = Schtasks("/Create /F /TN FlowBar /XML \"" + f + "\"");
                try { File.Delete(f); } catch { }
                if (rc != 0) Bar.Log(new Exception("schtasks /Create: код " + rc));
            }
            catch (Exception ex) { Bar.Log(ex); }
        }

        // Резервная копия личных данных перед обновлением: всё, кроме кэша, журнала и самих копий. Хранятся 5 последних.
        public static string Backup(string src, string root, string fromVer)
        {
            if (!Directory.Exists(src)) return null;
            var dst = Path.Combine(root, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_v" + fromVer);
            string full = Path.GetFullPath(src).TrimEnd('\\') + "\\", rootFull = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var abs = Path.GetFullPath(f);
                if (abs.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue;
                var rel = abs.Substring(full.Length);
                if (rel.StartsWith("cache\\", StringComparison.OrdinalIgnoreCase) || rel.Equals("error.log", StringComparison.OrdinalIgnoreCase)) continue;
                var to = Path.Combine(dst, rel); Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(f, to, true);
            }
            foreach (var old in new DirectoryInfo(root).GetDirectories().OrderByDescending(d => d.Name).Skip(5))
                try { old.Delete(true); } catch { }
            return dst;
        }

        // ---------- обновления с GitHub Releases ----------
        // Выпуск — архив FlowBar*.zip с папкой FlowBar (exe, config, сторож). Скачанный exe обновляет установленную копию тем же HandOver.
        public const string DefaultRepo = "merton31/FlowBar";
        public static string Repo { get { return Bar.C("update.repo", DefaultRepo); } }
        public static bool RepoKnown { get { return !Repo.StartsWith("OWNER/"); } }
        public class Release { public Version Ver; public string Tag, Asset, Url, Sha256, Page; }

        public static Release Latest()
        {
            if (!RepoKnown) throw new Exception("адрес выпусков ещё не задан");
            var json = Bar.GetSync("https://api.github.com/repos/" + Repo + "/releases/latest");
            var o = (System.Collections.Generic.Dictionary<string, object>)new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(json);
            var tag = o["tag_name"] as string ?? ""; Version v;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out v)) throw new Exception("непонятный номер выпуска: " + tag);
            var r = new Release { Ver = v, Tag = tag, Page = o.ContainsKey("html_url") ? o["html_url"] as string : null };
            foreach (var a0 in (object[])o["assets"])
            {
                var a = (System.Collections.Generic.Dictionary<string, object>)a0; var name = a["name"] as string ?? "";
                if (!name.StartsWith("FlowBar", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                r.Asset = name; r.Url = a["browser_download_url"] as string;
                var dg = a.ContainsKey("digest") ? a["digest"] as string : null; // GitHub сообщает SHA-256 файла выпуска
                if (dg != null && dg.StartsWith("sha256:")) r.Sha256 = dg.Substring(7).ToLowerInvariant();
                break;
            }
            if (r.Url == null) throw new Exception("в выпуске " + tag + " нет архива FlowBar*.zip");
            return r;
        }

        // скачать, сверить контрольную сумму, распаковать и запустить новый exe; он сам обновит установленную копию и перезапустит FlowBar
        public static void InstallRelease(Release r)
        {
            var dir = Path.Combine(Path.GetTempPath(), "FlowBar-update", V3(r.Ver));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var zip = Path.Combine(dir, r.Asset);
            using (var wc = new System.Net.WebClient()) { wc.Headers.Add("User-Agent", "FlowBar"); wc.DownloadFile(r.Url, zip); }
            if (r.Sha256 != null)
            {
                string got; using (var sha = System.Security.Cryptography.SHA256.Create()) using (var fs = File.OpenRead(zip)) got = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                if (got != r.Sha256) throw new Exception("контрольная сумма скачанного архива не совпала — файл повреждён или подменён, обновление не установлено");
            }
            var unpack = Path.Combine(dir, "files");
            System.IO.Compression.ZipFile.ExtractToDirectory(zip, unpack);
            var exe = Directory.GetFiles(unpack, "FlowBar.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe == null) throw new Exception("в архиве нет FlowBar.exe");
            var v = VersionOf(exe);
            if (v == null || v <= Current) throw new Exception("в архиве версия " + (v == null ? "?" : V3(v)) + " — не новее установленной " + VersionText);
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe) });
        }

        // после обновления: «было → стало» один раз, затем маркер удаляется
        public static string TakeUpdateNote()
        {
            try
            {
                if (!File.Exists(Marker)) return null;
                var t = File.ReadAllText(Marker).Split('|'); File.Delete(Marker);
                return t[0] == "" ? "FlowBar " + VersionText + " установлен. Ваши настройки и записи хранятся отдельно и при обновлениях не меняются." : "FlowBar обновлён: " + t[0] + " → " + VersionText + ". Настройки, заметки, задачи и календари сохранены; резервная копия — в папке backups.";
            }
            catch { return null; }
        }
    }
}
