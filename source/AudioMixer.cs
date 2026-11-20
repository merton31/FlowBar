using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace LumaDock;

internal static partial class WindowsShell
{
    // Process — имя exe без расширения (для связи сеанса с плеером); setMute задаёт звук явно, mute переключает
    public sealed record AudioSession(string Id, string Name, double Volume, bool Muted, string Process = "");

    public static AudioSession[] SessionVolumes(string? selection = null, float? level = null, bool mute = false, bool? setMute = null)
    {
        object? enumerator = null;
        IMMDevice? device = null;
        object? manager = null;
        ISessionList? list = null;
        var sessions = new List<AudioSession>();
        try
        {
            enumerator = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!);
            ((IMMDeviceEnumerator)enumerator!).DefaultEndpoint(0, 1, out device);
            Guid iid = typeof(ISessionManager).GUID;
            device.Activate(ref iid, 23, 0, out manager);
            ((ISessionManager)manager).GetSessionEnumerator(out list);
            list.GetCount(out int count);
            for (int index = 0; index < count && index < 96; index++)
            {
                list.GetSession(index, out object entry);
                try
                {
                    if (entry is not ISessionControl session || entry is not ISimpleVolume volume) continue;
                    session.GetState(out int state);
                    if (state == 2) continue;
                    session.GetSessionInstanceIdentifier(out string id);
                    if (selection is not null && selection != id) continue;
                    if (level.HasValue) volume.SetVolume(Math.Clamp(level.Value, 0, 1), Guid.Empty);
                    volume.GetMute(out bool muted);
                    if (mute) volume.SetMute(muted = !muted, Guid.Empty);
                    else if (setMute.HasValue) volume.SetMute(muted = setMute.Value, Guid.Empty);
                    volume.GetVolume(out float current);
                    session.GetDisplayName(out string name);
                    session.GetProcessId(out uint pid);
                    string process = pid == 0 ? "" : Path.GetFileNameWithoutExtension(ProcessPath(pid));
                    if (string.IsNullOrWhiteSpace(name) || name.StartsWith('@'))
                        name = pid == 0 ? "System sounds" : process;
                    if (string.IsNullOrWhiteSpace(name)) name = "Audio session";
                    sessions.Add(new AudioSession(id, name, current, muted, process));
                }
                catch (Exception ex) when (ex is COMException or UnauthorizedAccessException) { }
                finally { Marshal.ReleaseComObject(entry); }
            }
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException) { }
        finally
        {
            foreach (object? value in new[] { list, manager, device, enumerator })
                if (value is not null) Marshal.ReleaseComObject(value);
        }
        return sessions.ToArray();
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionManager
    {
        void GetSessionControl();
        void GetSimpleVolume();
        void GetSessionEnumerator(out ISessionList list);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionList
    {
        void GetCount(out int count);
        void GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionControl
    {
        void GetState(out int state);
        void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetDisplayName();
        void GetIconPath();
        void SetIconPath();
        void GetGroupingParam();
        void SetGroupingParam();
        void RegisterAudioSessionNotification();
        void UnregisterAudioSessionNotification();
        void GetSessionIdentifier();
        void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetProcessId(out uint pid);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleVolume
    {
        void SetVolume(float value, in Guid context);
        void GetVolume(out float value);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, in Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
