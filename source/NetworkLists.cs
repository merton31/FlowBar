using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace LumaDock;

// Списки сетей Wi-Fi и устройств Bluetooth, как в быстрых настройках Windows. Сети — из системного интерфейса WLAN (wlanapi.dll),
// устройства — сопряжённые Bluetooth и Bluetooth LE из Windows.Devices.Enumeration.
internal sealed record WifiNetwork(string Ssid, int Signal, bool Secured, bool Connected, string? Profile);
internal sealed record BluetoothItem(string Name, string Id, bool Connected, int? Battery);

internal static class WifiNetworks
{
    // Для --smoke: вместо настоящих сетей показывается образец
    internal static bool Fixture { get; set; }

    private const uint Connected = 1, HasProfile = 2;

    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint clientVersion, nint reserved, out uint negotiated, out nint handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(nint handle, nint reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(nint handle, nint reserved, out nint list);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(nint memory);
    [DllImport("wlanapi.dll")] private static extern uint WlanGetAvailableNetworkList(nint handle, ref Guid iface, uint flags, nint reserved, out nint list);
    [DllImport("wlanapi.dll")] private static extern uint WlanScan(nint handle, ref Guid iface, nint ssid, nint ie, nint reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanConnect(nint handle, ref Guid iface, ref ConnectionParameters parameters, nint reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanDisconnect(nint handle, ref Guid iface, nint reserved);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ConnectionParameters
    {
        public uint Mode;
        public string Profile;
        public nint Ssid;
        public nint BssidList;
        public uint BssType;
        public uint Flags;
    }

    // Адаптеры Wi-Fi: (описание, GUID); пусто — адаптера нет
    private static List<Guid> Interfaces(nint handle)
    {
        var result = new List<Guid>();
        if (WlanEnumInterfaces(handle, 0, out nint list) != 0 || list == 0) return result;
        try
        {
            int count = Marshal.ReadInt32(list);
            for (int index = 0; index < count; index++)
            {
                var bytes = new byte[16];
                Marshal.Copy(list + 8 + index * 532, bytes, 0, 16); // заголовок списка 8 байт, запись 532 байта: GUID, описание (256 WCHAR), состояние
                result.Add(new Guid(bytes));
            }
        }
        finally { WlanFreeMemory(list); }
        return result;
    }

    private static T WithHandle<T>(Func<nint, T> action, T fallback)
    {
        if (WlanOpenHandle(2, 0, out _, out nint handle) != 0) return fallback;
        try { return action(handle); }
        finally { WlanCloseHandle(handle, 0); }
    }

    // Есть ли на компьютере адаптер Wi-Fi
    public static bool HasAdapter() => Fixture || WithHandle(handle => Interfaces(handle).Count > 0, false);

    // Для --smoke: притвориться, что адаптера нет (false) или есть (true)
    internal static bool? AdapterOverride { get; set; }
    private static DateTime adapterChecked;
    private static bool adapterPresent = true;

    // Наличие адаптера с запасом по времени (30 с): адаптер, например USB, могут вставить или вынуть; нет адаптера — кнопки Wi-Fi в доке не показываются
    public static bool HasAdapterCached
    {
        get
        {
            if (AdapterOverride is { } forced) return forced;
            if (Fixture) return true;
            if (DateTime.UtcNow - adapterChecked > TimeSpan.FromSeconds(30))
            {
                adapterPresent = HasAdapter();
                adapterChecked = DateTime.UtcNow;
            }
            return adapterPresent;
        }
    }

    // Доступные сети по убыванию сигнала; подключённая первой. wait — подождать результата сканирования эфира
    public static async Task<List<WifiNetwork>> ListAsync(bool scan)
    {
        if (Fixture)
            return [new("Home Wi-Fi", 82, true, true, "Home Wi-Fi"), new("Office", 64, true, false, "Office"), new("Guest", 41, false, false, null), new("Neighbour", 22, true, false, null)];
        if (scan)
        {
            WithHandle(handle => { foreach (var iface in Interfaces(handle)) { var id = iface; WlanScan(handle, ref id, 0, 0, 0); } return 0; }, 0);
            await Task.Delay(2500);
        }
        return await Task.Run(() => WithHandle(handle =>
        {
            var found = new Dictionary<string, WifiNetwork>(StringComparer.Ordinal);
            foreach (var iface in Interfaces(handle))
            {
                var id = iface;
                if (WlanGetAvailableNetworkList(handle, ref id, 0, 0, out nint list) != 0 || list == 0) continue;
                try
                {
                    int count = Marshal.ReadInt32(list);
                    for (int index = 0; index < count; index++)
                    {
                        nint item = list + 8 + index * 628; // запись WLAN_AVAILABLE_NETWORK: 628 байт
                        string profile = Marshal.PtrToStringUni(item, 256).Split('\0')[0];
                        int length = Math.Min(32, Marshal.ReadInt32(item + 512));
                        var ssidBytes = new byte[length];
                        Marshal.Copy(item + 516, ssidBytes, 0, length);
                        string ssid = Encoding.UTF8.GetString(ssidBytes);
                        if (ssid.Length == 0) continue; // скрытые сети без имени не показываем
                        int signal = Marshal.ReadInt32(item + 604);
                        bool secured = Marshal.ReadInt32(item + 608) != 0;
                        uint flags = (uint)Marshal.ReadInt32(item + 620);
                        var network = new WifiNetwork(ssid, signal, secured, (flags & Connected) != 0, (flags & HasProfile) != 0 ? profile : null);
                        if (!found.TryGetValue(ssid, out var known) || network.Connected || !known.Connected && network.Signal > known.Signal)
                            found[ssid] = network with { Profile = network.Profile ?? known?.Profile, Connected = network.Connected || known?.Connected == true };
                    }
                }
                finally { WlanFreeMemory(list); }
            }
            return found.Values.OrderByDescending(item => item.Connected).ThenByDescending(item => item.Signal).ToList();
        }, new List<WifiNetwork>()));
    }

    // Подключение к сети с сохранённым профилем; false — профиля нет или Windows отказала (тогда открывается список сетей Windows)
    public static Task<bool> ConnectAsync(WifiNetwork network) => Task.Run(() =>
    {
        if (Fixture) return network.Profile is not null;
        if (network.Profile is null) return false;
        return WithHandle(handle =>
        {
            foreach (var iface in Interfaces(handle))
            {
                var id = iface;
                var parameters = new ConnectionParameters { Mode = 0, Profile = network.Profile, BssType = 1 };
                if (WlanConnect(handle, ref id, ref parameters, 0) == 0) return true;
            }
            return false;
        }, false);
    });

    public static Task DisconnectAsync() => Task.Run(() =>
    {
        if (Fixture) return;
        WithHandle(handle => { foreach (var iface in Interfaces(handle)) { var id = iface; WlanDisconnect(handle, ref id, 0); } return 0; }, 0);
    });
}

// Поиск устройств Bluetooth поблизости, которые ещё не сопряжены (как «Добавить устройство» в Windows)
internal sealed class BluetoothScan : IDisposable
{
    private readonly List<DeviceWatcher> watchers = [];
    private readonly Dictionary<string, DeviceInformation> found = new(StringComparer.OrdinalIgnoreCase);
    public event Action<BluetoothItem>? Found;

    public void Start()
    {
        if (BluetoothDevices.Fixture)
        {
            Found?.Invoke(new("Mouse", "m1", false, null));
            Found?.Invoke(new("Gamepad", "g1", false, null));
            return;
        }
        foreach (string selector in new[] { BluetoothDevice.GetDeviceSelectorFromPairingState(false), BluetoothLEDevice.GetDeviceSelectorFromPairingState(false) })
        {
            try
            {
                var watcher = DeviceInformation.CreateWatcher(selector, [], DeviceInformationKind.AssociationEndpoint);
                watcher.Added += (_, device) =>
                {
                    if (string.IsNullOrWhiteSpace(device.Name) || device.Pairing.IsPaired) return;
                    lock (found) { if (!found.TryAdd(device.Id, device)) return; }
                    Found?.Invoke(new(device.Name.Trim(), device.Id, false, null));
                };
                watcher.Start();
                watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException) { }
        }
    }

    public void Stop()
    {
        foreach (var watcher in watchers)
        {
            try { if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop(); }
            catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        }
        watchers.Clear();
    }

    // Сопряжение: подтверждения и коды показывает confirm (true — пользователь согласен); возвращает успех и сообщение об ошибке для пользователя
    public async Task<(bool Ok, string Message)> PairAsync(string id, Func<string, string, bool> confirm)
    {
        if (BluetoothDevices.Fixture) return (true, "");
        DeviceInformation? device;
        lock (found) found.TryGetValue(id, out device);
        if (device is null) return (false, "The device is no longer in range.");
        try
        {
            var custom = device.Pairing.Custom;
            custom.PairingRequested += (_, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    switch (args.PairingKind)
                    {
                        case DevicePairingKinds.ConfirmPinMatch:
                            if (confirm("match", args.Pin)) args.Accept();
                            break;
                        case DevicePairingKinds.DisplayPin:
                            confirm("show", args.Pin);
                            args.Accept();
                            break;
                        case DevicePairingKinds.ProvidePin:
                            args.Accept("0000"); // типичный код по умолчанию для клавиатур и наушников без экрана
                            break;
                        default:
                            args.Accept();
                            break;
                    }
                }
                finally { deferral.Complete(); }
            };
            var result = await custom.PairAsync(DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin | DevicePairingKinds.ConfirmPinMatch | DevicePairingKinds.ProvidePin,
                DevicePairingProtectionLevel.Default);
            return result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired
                ? (true, "") : (false, "Could not pair the device. Try again.");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex, "Bluetooth pairing");
            return (false, "Could not pair the device. Try again.");
        }
    }

    public void Dispose() => Stop();
}

internal static class BluetoothDevices
{
    internal static bool Fixture { get; set; }

    // Свойства из Windows: подключено ли устройство и заряд батареи (если устройство его сообщает)
    private const string IsConnected = "System.Devices.Aep.IsConnected";
    private const string Battery = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    // Сопряжённые устройства: сначала подключённые; один и тот же прибор из Bluetooth и Bluetooth LE показывается один раз
    public static async Task<List<BluetoothItem>> ListAsync()
    {
        if (Fixture) return [new("Headphones", "1", true, 80), new("Keyboard", "2", true, null), new("Speaker", "3", false, null)];
        var found = new Dictionary<string, BluetoothItem>(StringComparer.OrdinalIgnoreCase);
        foreach (string selector in new[] { BluetoothDevice.GetDeviceSelectorFromPairingState(true), BluetoothLEDevice.GetDeviceSelectorFromPairingState(true) })
        {
            DeviceInformationCollection devices;
            try { devices = await DeviceInformation.FindAllAsync(selector, [IsConnected, Battery], DeviceInformationKind.AssociationEndpoint); }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException) { continue; }
            foreach (var device in devices)
            {
                if (string.IsNullOrWhiteSpace(device.Name)) continue;
                bool connected = device.Properties.TryGetValue(IsConnected, out var state) && state is true;
                int? level = null;
                if (device.Properties.TryGetValue(Battery, out var value) && value is not null)
                {
                    try { level = Convert.ToInt32(value); }
                    catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { }
                }
                var item = new BluetoothItem(device.Name.Trim(), device.Id, connected, level is > 0 and <= 100 ? level : null);
                found[item.Name] = found.TryGetValue(item.Name, out var known)
                    ? known with { Connected = known.Connected || item.Connected, Battery = known.Battery ?? item.Battery } : item;
            }
        }
        return found.Values.OrderByDescending(item => item.Connected).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
