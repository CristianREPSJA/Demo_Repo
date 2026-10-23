using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Rama.Core.Secrets;

// Windows Credential Manager (advapi32), CRED_TYPE_GENERIC, objetivo "Rama:<service>:<account>".
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ISecretStore
{
    private const uint CredTypeGeneric = 1;
    private const uint PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaxBlobBytes = 5 * 512;

    internal static string Target(string service, string account) => $"Rama:{service}:{account}";

    public Task<string?> GetAsync(string service, string account, CancellationToken ct = default)
    {
        SecretStoreFactory.Validate(service, account);
        if (!NativeMethods.CredReadW(Target(service, account), CredTypeGeneric, 0, out var ptr))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == ErrorNotFound) return Task.FromResult<string?>(null);
            throw new SecretStoreException($"CredReadW falló ({err}).", err);
        }

        try
        {
            var cred = Marshal.PtrToStructure<NativeMethods.Credential>(ptr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                return Task.FromResult<string?>(string.Empty);
            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            NativeMethods.CredFree(ptr);
        }
    }

    public Task SetAsync(string service, string account, string secret, CancellationToken ct = default)
    {
        SecretStoreFactory.Validate(service, account);
        ArgumentNullException.ThrowIfNull(secret);
        var bytes = Encoding.UTF8.GetBytes(secret);
        if (bytes.Length > MaxBlobBytes) throw new SecretStoreException("Secreto demasiado largo para Credential Manager.");

        var blob = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1));
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var cred = new NativeMethods.Credential
            {
                Type = CredTypeGeneric,
                TargetName = Target(service, account),
                UserName = account,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
            };
            if (!NativeMethods.CredWriteW(ref cred, 0))
            {
                var err = Marshal.GetLastWin32Error();
                throw new SecretStoreException($"CredWriteW falló ({err}).", err);
            }
        }
        finally
        {
            Array.Clear(bytes);
            Marshal.FreeHGlobal(blob);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string service, string account, CancellationToken ct = default)
    {
        SecretStoreFactory.Validate(service, account);
        if (!NativeMethods.CredDeleteW(Target(service, account), CredTypeGeneric, 0))
        {
            var err = Marshal.GetLastWin32Error();
            if (err != ErrorNotFound) throw new SecretStoreException($"CredDeleteW falló ({err}).", err);
        }
        return Task.CompletedTask;
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct Credential
        {
            public uint Flags;
            public uint Type;
            public string? TargetName;
            public string? Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string? UserName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredWriteW(ref Credential credential, uint flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredDeleteW(string target, uint type, uint flags);

        [DllImport("advapi32.dll")]
        internal static extern void CredFree(IntPtr buffer);
    }
}
