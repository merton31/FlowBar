using System;
using System.IO;

namespace LumaDock;

// FlowBar.exe --check-install <папка>: проверка установки и разбора выпусков GitHub. Установка идёт в чужую папку,
// настоящие процессы не останавливаются и ничего не запускается, поэтому проверку можно гонять на рабочем компьютере.
internal static class InstallCheck
{
    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        string result = Path.Combine(folder, "result.txt");
        try
        {
            string dir = Path.Combine(folder, "Programs", "FlowBar");
            Environment.SetEnvironmentVariable("FLOWBAR_INSTALL_DIR", dir);
            Directory.CreateDirectory(dir);
            string config = Path.Combine(dir, "FlowBar.exe.config"), watcher = Path.Combine(dir, "flowbar-watch.exe"), keep = Path.Combine(dir, "Как пользоваться.txt");
            File.WriteAllText(Path.Combine(dir, "FlowBar.exe"), "прежняя панель 1.x");
            File.WriteAllText(config, "old");
            File.WriteAllText(watcher, "old");
            File.WriteAllText(keep, "keep");
            string self = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
            string installed = Installer.Install(self, false);
            if (installed != Installer.InstalledExe || new FileInfo(installed).Length != new FileInfo(self).Length)
                throw new InvalidOperationException("The installed copy differs from the source.");
            if (File.Exists(config) || File.Exists(watcher)) throw new InvalidOperationException("Files of the old panel stayed.");
            if (!File.Exists(keep)) throw new InvalidOperationException("An unrelated file was removed.");

            if (!Updates.TryParseTag("v2.0.0", out var two) || two != new Version(2, 0, 0, 0)) throw new InvalidOperationException("Tag v2.0.0 was not read.");
            if (!Updates.TryParseTag("v1.6.13f", out var old) || old != new Version(1, 6, 13, 6) || two <= old)
                throw new InvalidOperationException("Tag v1.6.13f was not read or is not older.");
            if (Updates.TryParseTag("v2.0", out _)) throw new InvalidOperationException("Tag v2.0 must be rejected.");
            var release = Updates.ParseLatest("""
                {"tag_name":"v2.0.0","html_url":"https://github.com/merton31/FlowBar/releases/tag/v2.0.0","assets":[
                {"name":"notes.txt","browser_download_url":"https://github.com/merton31/FlowBar/releases/download/v2.0.0/notes.txt"},
                {"name":"FlowBar.zip","browser_download_url":"https://github.com/merton31/FlowBar/releases/download/v2.0.0/FlowBar.zip","digest":"sha256:ABCDEF0123"}]}
                """);
            if (release.Version != two || release.Asset != "FlowBar.zip" || release.Sha256 != "abcdef0123")
                throw new InvalidOperationException("The release was parsed wrongly.");
            bool refused = false;
            try
            {
                Updates.ParseLatest("""{"tag_name":"v2.0.0","assets":[{"name":"FlowBar.zip","browser_download_url":"https://example.com/FlowBar.zip"}]}""");
            }
            catch (InvalidOperationException) { refused = true; }
            if (!refused) throw new InvalidOperationException("A download address outside the repository was accepted.");
            File.WriteAllText(result, "OK");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(result, ex.ToString());
            return 1;
        }
    }
}
