using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;

namespace StockCount.Server.Hosting;

/// <summary>Compatible with Rust keyring's Windows username.service target convention.</summary>
public sealed class WindowsCredentialVault
{
    private const string Service = "com.dreamer.stockcount.server";
    private const uint GenericType = 1;
    private const uint PersistLocalMachine = 2;
    private const int NotFound = 1168;

    // Existing Tauri code calls keyring::Entry::new(Service, username).
    private static string Target(string name) => name + "." + Service;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr credential);

    public string? Read(string name)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows Credential Manager only.");
        if (!CredRead(Target(name), GenericType, 0, out var ptr))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound) return null;
            throw new Win32Exception(error, "无法读取 Windows Credential Manager 凭据。");
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(ptr);
            if (credential.CredentialBlobSize == 0) return "";
            var bytes = new byte[checked((int)credential.CredentialBlobSize)];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try { return Encoding.Unicode.GetString(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { CredFree(ptr); }
    }

    public void Write(string name, string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows Credential Manager only.");
        var bytes = Encoding.Unicode.GetBytes(value);
        if (bytes.Length > 5120) throw new ArgumentOutOfRangeException(nameof(value), "凭据超过 Windows 限制。");
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var credential = new NativeCredential
            {
                Type = GenericType, TargetName = Target(name), UserName = name,
                CredentialBlobSize = (uint)bytes.Length, CredentialBlob = pinned.AddrOfPinnedObject(),
                Persist = PersistLocalMachine
            };
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法保存 Windows 凭据。");
        }
        finally { pinned.Free(); CryptographicOperations.ZeroMemory(bytes); }
    }

    public string GetOrCreateDeviceKey()
    {
        var existing = Read("device-key");
        if (!string.IsNullOrEmpty(existing)) return existing;
        var value = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        Write("device-key", value);
        return value;
    }
}
