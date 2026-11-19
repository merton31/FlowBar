using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;

namespace LumaDock;

internal sealed class LinkTraffic
{
    private DateTimeOffset lastTime;
    private long lastDownload, lastUpload;
    public string Adapter { get; private set; } = "Offline";
    public double Download { get; private set; }
    public double Upload { get; private set; }
    public IReadOnlyCollection<(double Down, double Up)> History => samples;
    private readonly Queue<(double Down, double Up)> samples = new();

    public void Update()
    {
        try
        {
            var links = NetworkInterface.GetAllNetworkInterfaces().Where(link =>
                link.OperationalStatus == OperationalStatus.Up && link.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && link.NetworkInterfaceType != NetworkInterfaceType.Tunnel).ToArray();
            long inbound = 0, outbound = 0;
            foreach (var link in links)
            {
                var stats = link.GetIPStatistics();
                inbound += stats.BytesReceived;
                outbound += stats.BytesSent;
            }
            var now = DateTimeOffset.Now;
            double elapsed = (now - lastTime).TotalSeconds;
            Download = elapsed is > 0 and < 5 ? Math.Max(0, (inbound - lastDownload) / elapsed) : 0;
            Upload = elapsed is > 0 and < 5 ? Math.Max(0, (outbound - lastUpload) / elapsed) : 0;
            if (samples.Count >= 36) samples.Dequeue();
            samples.Enqueue((Download, Upload));
            Adapter = links.FirstOrDefault(link => link.GetIPProperties().GatewayAddresses.Count > 0)?.Name
                ?? links.FirstOrDefault()?.Name ?? "Offline";
            lastTime = now;
            lastDownload = inbound;
            lastUpload = outbound;
        }
        catch (NetworkInformationException) { }
    }
}

internal static partial class WindowsShell
{
    public static (long Items, long Bytes)? RecycleBin()
    {
        var info = new RecycleBinInfo { Size = Marshal.SizeOf<RecycleBinInfo>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? (info.Items, info.Bytes) : null;
    }

    public static bool EmptyRecycleBin(nint owner) => SHEmptyRecycleBin(owner, null, 0) == 0;

    public static (double Brightness, bool Supported) Brightness(nint window, double? set = null)
    {
        var monitor = MonitorFromWindow(window, 2);
        if (monitor == 0 || !GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count is 0 or > 16)
            return (0, false);
        var monitors = new PhysicalMonitor[count];
        if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, monitors)) return (0, false);
        try
        {
            foreach (var target in monitors)
            {
                if (!GetMonitorBrightness(target.Handle, out uint min, out uint current, out uint max) || max == min) continue;
                if (set.HasValue && SetMonitorBrightness(target.Handle, min + (uint)Math.Round((max - min) * Math.Clamp(set.Value, 0, 1))))
                    return (set.Value, true);
                return ((double)(current - min) / (max - min), true);
            }
            return (0, false);
        }
        finally { DestroyPhysicalMonitors(count, monitors); }
    }

    public static double? Temperature()
    {
        object? locator = null, service = null, results = null;
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (type is null) return null;
            locator = Activator.CreateInstance(type);
            service = ((dynamic)locator!).ConnectServer(".", "root\\WMI");
            results = ((dynamic)service!).ExecQuery("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            var temperatures = new List<double>();
            foreach (object zone in (dynamic)results!)
            {
                try
                {
                    var value = Convert.ToDouble(((dynamic)zone).CurrentTemperature, CultureInfo.InvariantCulture) / 10 - 273.15;
                    if (value > -20 && value < 150) temperatures.Add(value);
                }
                finally { if (zone is not null && Marshal.IsComObject(zone)) Marshal.ReleaseComObject(zone); }
            }
            return temperatures.Count == 0 ? null : temperatures.Max();
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or UnauthorizedAccessException) { return null; }
        finally
        {
            foreach (object? value in new[] { results, service, locator })
                if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct RecycleBinInfo { public int Size; public long Bytes, Items; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct PhysicalMonitor
    {
        public nint Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }
    [DllImport("shell32", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBin(string? root, ref RecycleBinInfo info);
    [DllImport("shell32", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBin(nint owner, string? root, uint flags);
    [DllImport("user32")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("dxva2")] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint monitor, out uint count);
    [DllImport("dxva2")] private static extern bool GetPhysicalMonitorsFromHMONITOR(nint monitor, uint count, [Out] PhysicalMonitor[] monitors);
    [DllImport("dxva2")] private static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] monitors);
    [DllImport("dxva2")] private static extern bool GetMonitorBrightness(nint monitor, out uint minimum, out uint current, out uint maximum);
    [DllImport("dxva2")] private static extern bool SetMonitorBrightness(nint monitor, uint brightness);
}
