using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LumaDock;

internal static class CredentialStore
{
    public static string? Read(string provider)
    {
        if (!CredRead("LumaDock/" + provider, 1, 0, out nint pointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return Marshal.PtrToStringUni(credential.Blob, (int)credential.BlobSize / 2);
        }
        finally { CredFree(pointer); }
    }

    public static bool Save(string provider, string secret)
    {
        if (secret.Length is 0 or > 1200 || !System.Linq.Enumerable.All(secret, character => character is >= '!' and <= '~')) return false;
        nint blob = Marshal.StringToCoTaskMemUni(secret);
        try
        {
            var credential = new Credential { Type = 1, Target = "LumaDock/" + provider, Persist = 2,
                Blob = blob, BlobSize = (uint)Encoding.Unicode.GetByteCount(secret), User = provider };
            return CredWrite(ref credential, 0);
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
    }

    public static void Delete(string provider) => CredDelete("LumaDock/" + provider, 1, 0);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Credential
    {
        public uint Flags, Type;
        public string Target;
        public string? Comment;
        public long LastWritten;
        public uint BlobSize;
        public nint Blob;
        public uint Persist, AttributeCount;
        public nint Attributes;
        public string? Alias, User;
    }

    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out nint credential);
    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32")] private static extern void CredFree(nint buffer);
}
