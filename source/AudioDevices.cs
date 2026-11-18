using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LumaDock;

internal static partial class WindowsShell
{
    internal record AudioDevice(string Id, string Name, bool Selected);

    internal static AudioDevice[] AudioDevices(bool capture)
    {
        object? manager = null;
        IEndpointCollection? endpoints = null;
        IMMDevice? defaultEndpoint = null;
        var devices = new List<AudioDevice>();
        try
        {
            manager = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!);
            var enumerator = (IMMDeviceEnumerator)manager!;
            string? active = null;
            try
            {
                enumerator.DefaultEndpoint(capture ? 1 : 0, capture ? 2 : 1, out defaultEndpoint);
                defaultEndpoint.GetId(out active);
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException) { }
            enumerator.EnumEndpoints(capture ? 1 : 0, 1, out endpoints);
            endpoints.GetCount(out var count);
            for (uint index = 0; index < count && index < 128; index++)
            {
                endpoints.Item(index, out var endpoint);
                try
                {
                    endpoint.GetId(out var id);
                    string label = DeviceName(endpoint) ?? $"Audio device {index + 1}";
                    devices.Add(new AudioDevice(id, label, string.Equals(id, active, StringComparison.OrdinalIgnoreCase)));
                }
                finally { Marshal.ReleaseComObject(endpoint); }
            }
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidCastException) { }
        finally
        {
            foreach (object? value in new[] { endpoints, defaultEndpoint, manager })
                if (value is not null) Marshal.ReleaseComObject(value);
        }
        return devices.ToArray();
    }

    private static string? DeviceName(IMMDevice endpoint)
    {
        IEndpointProperties? store = null;
        EndpointValue value = default;
        try
        {
            endpoint.OpenPropertyStore(0, out store);
            var key = new EndpointKey { Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
            store.GetValue(ref key, out value);
            return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException) { return null; }
        finally
        {
            PropVariantClear(ref value);
            if (store is not null) Marshal.ReleaseComObject(store);
        }
    }

    internal static bool SetDefaultAudioDevice(string id, bool capture)
    {
        object? service = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"));
            service = type is null ? null : Activator.CreateInstance(type);
            return service is IEndpointPolicy policy && policy.SetDefaultEndpoint(id, capture ? 2 : 1) >= 0;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException) { return false; }
        finally { if (service is not null && Marshal.IsComObject(service)) Marshal.ReleaseComObject(service); }
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEndpointCollection
    {
        void GetCount(out uint count);
        void Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEndpointProperties
    {
        void GetCount();
        void GetAt();
        void GetValue(ref EndpointKey key, out EndpointValue value);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct EndpointKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct EndpointValue
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint Pointer;
    }
    [DllImport("ole32")]
    private static extern int PropVariantClear(ref EndpointValue value);

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEndpointPolicy
    {
        void GetMixFormat();
        void GetDeviceFormat();
        void ResetDeviceFormat();
        void SetDeviceFormat();
        void GetProcessingPeriod();
        void SetProcessingPeriod();
        void GetShareMode();
        void SetShareMode();
        void GetPropertyValue();
        void SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }
}
