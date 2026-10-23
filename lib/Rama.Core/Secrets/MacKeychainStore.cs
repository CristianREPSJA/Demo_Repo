using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Rama.Core.Secrets;

// macOS Keychain vía Security.framework (SecKeychain*GenericPassword, P/Invoke; sin secreto en argv).
// SIN VERIFICAR en hardware real: solo compila.
[SupportedOSPlatform("macos")]
[ExcludeFromCodeCoverage(Justification = "Nativo macOS, no ejecutable en Windows")]
public sealed class MacKeychainStore : ISecretStore
{
    private const int ErrSecItemNotFound = -25300;

    public Task<string?> GetAsync(string service, string account, CancellationToken ct = default)
    {
        SecretStoreFactory.Validate(service, account);
        var (s, a) = (Encoding.UTF8.GetBytes(service), Encoding.UTF8.GetBytes(account));
        var status = Native.SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a,
            out var len, out var data, out var item);
        if (status == ErrSecItemNotFound) return Task.FromResult<string?>(null);
        Check(status, "buscar");
        try
        {
            var bytes = new byte[len];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            Native.SecKeychainItemFreeContent(IntPtr.Zero, data);
            if (item != IntPtr.Zero) Native.CFRelease(item);
        }
    }

    public async Task SetAsync(string service, string account, string secret, CancellationToken ct = default)
    {
        SecretStoreFactory.Validate(service, account);
        ArgumentNullException.ThrowIfNull(secret);
        await DeleteAsync(service, account, ct);
        var (s, a, p) = (Encoding.UTF8.GetBytes(service), Encoding.UTF8.GetBytes(account), Encoding.UTF8.GetBytes(secret));
        try
        {
            Check(Native.SecKeychainAddGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a,
                (uint)p.Length, p, IntPtr.Zero), "guardar");
        }
        finally
        {
            Array.Clear(p);
        }
    }

    public Task DeleteAsync(string service, string account, CancellationToken ct = default)
    {
        SecretStoreFactory.Validate(service, account);
        var (s, a) = (Encoding.UTF8.GetBytes(service), Encoding.UTF8.GetBytes(account));
        var status = Native.SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a,
            out _, out var data, out var item);
        if (status == ErrSecItemNotFound) return Task.CompletedTask;
        Check(status, "buscar");
        try
        {
            if (data != IntPtr.Zero) Native.SecKeychainItemFreeContent(IntPtr.Zero, data);
            Check(Native.SecKeychainItemDelete(item), "borrar");
        }
        finally
        {
            if (item != IntPtr.Zero) Native.CFRelease(item);
        }
        return Task.CompletedTask;
    }

    private static void Check(int status, string op)
    {
        if (status != 0) throw new SecretStoreException($"Keychain: fallo al {op} (OSStatus {status}).", status);
    }

    private static class Native
    {
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        [DllImport(Security)]
        internal static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLen, byte[] service,
            uint accountLen, byte[] account, out uint passwordLen, out IntPtr passwordData, out IntPtr item);

        [DllImport(Security)]
        internal static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLen, byte[] service,
            uint accountLen, byte[] account, uint passwordLen, byte[] passwordData, IntPtr item);

        [DllImport(Security)]
        internal static extern int SecKeychainItemDelete(IntPtr item);

        [DllImport(Security)]
        internal static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

        [DllImport(CoreFoundation)]
        internal static extern void CFRelease(IntPtr cf);
    }
}
