using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LumaDock;

// FlowBar.exe --check-network <папка>: настоящее чтение сетей Wi-Fi и сопряжённых устройств Bluetooth без подмен. Результат в result.txt
// (названия сетей и устройств — только в этом файле); успех — строка OK, если оба списка прочитаны без ошибок (пустой список не ошибка: адаптера или устройств может не быть).
internal static class NetworkCheck
{
    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var report = new StringBuilder();
        int code = 0;
        try
        {
            Task.Run(() => Check(report)).GetAwaiter().GetResult();
            report.AppendLine("OK");
        }
        catch (Exception ex) { report.AppendLine("СБОЙ: " + ex.GetType().Name + ": " + ex.Message); code = 1; }
        File.WriteAllText(Path.Combine(folder, "result.txt"), report.ToString(), Encoding.UTF8);
        return code;
    }

    private static async Task Check(StringBuilder report)
    {
        InternetConnectivity.Refresh();
        for (int wait = 0; wait < 50 && InternetConnectivity.Current == InternetConnectivity.State.Unknown; wait++) await Task.Delay(100);
        report.AppendLine("Доступ к интернету по данным Windows: " + InternetConnectivity.Current);
        if (InternetConnectivity.Current == InternetConnectivity.State.Unknown) throw new InvalidOperationException("Windows internet connectivity could not be read.");
        var networks = await WifiNetworks.ListAsync(true);
        report.AppendLine($"Сети Wi-Fi: {networks.Count}");
        foreach (var network in networks)
            report.AppendLine($"  {network.Ssid}: сигнал {network.Signal}%, {(network.Secured ? "с паролем" : "открытая")}{(network.Connected ? ", подключена" : "")}{(network.Profile is not null ? ", есть профиль" : "")}");
        var devices = await BluetoothDevices.ListAsync();
        report.AppendLine($"Устройства Bluetooth: {devices.Count}");
        foreach (var device in devices)
            report.AppendLine($"  {device.Name}: {(device.Connected ? "подключено" : "сопряжено")}{(device.Battery is { } level ? $", заряд {level}%" : "")}");
        // поиск новых устройств 8 секунд (сопряжение не выполняется)
        var nearby = new List<string>();
        using (var scan = new BluetoothScan())
        {
            scan.Found += item => { lock (nearby) nearby.Add(item.Name); };
            scan.Start();
            await Task.Delay(8000);
            scan.Stop();
        }
        lock (nearby) report.AppendLine($"Новые устройства поблизости за 8 с: {nearby.Count}" + (nearby.Count > 0 ? " (" + string.Join(", ", nearby) + ")" : ""));
    }
}
