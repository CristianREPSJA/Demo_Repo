using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Rama.Core.Secrets;

// Linux Secret Service vía libsecret-1 (P/Invoke). Decisión: SourceGit no referencia Tmds.DBus, así que
// se evita una dependencia nueva y se enlaza libsecret en tiempo de ejecución.
// SIN VERIFICAR en Linux real: solo compila.
[SupportedOSPlatform("linux")]
[ExcludeFromCodeCoverage(Justification = "Nativo Linux, no ejecutable en Windows")]
public sealed class LinuxSecretServiceStore : ISecretStore
{
    private const int SchemaBytes = 1024;
    private const string DefaultCollection = "default";

    public Task<string?> GetAsync(string service, string account, CancellationToken ct = default)
    {
        SecretStoreFactory.Validate(service, account);
        using var schema = new Schema();
        var err = IntPtr.Zero;
        var ptr = Native.Lookup(schema.Ptr, IntPtr.Zero, ref err, "service", service, "account", account, IntPtr.Zero);
        ThrowIfError(err);
        if (ptr == IntPtr.Zero) return Task.FromResult<string?>(null);
        try { return Task.FromResult<string?>(Marshal.PtrToStringUTF8(ptr)); }
        finally { Native.Free(ptr); }
    }

    public Task SetAsync(string service, string account, string secret, CancellationToken ct = default)
    {
        SecretStoreFactory.Validate(service, account);
        ArgumentNullException.ThrowIfNull(secret);
        using var schema = new Schema();
        var err = IntPtr.Zero;
        Native.Store(schema.Ptr, DefaultCollection, $"Rama:{service}:{account}", secret, IntPtr.Zero, ref err,
            "service", service, "account", account, IntPtr.Zero);
        ThrowIfError(err);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string service, string account, CancellationToken ct = default)
    {
        SecretStoreFactory.Validate(service, account);
        using var schema = new Schema();
        var err = IntPtr.Zero;
        Native.Clear(schema.Ptr, IntPtr.Zero, ref err, "service", service, "account", account, IntPtr.Zero);
        ThrowIfError(err);
        return Task.CompletedTask;
    }

    private static void ThrowIfError(IntPtr gerror)
    {
        if (gerror == IntPtr.Zero) return;
        // GError: { guint32 domain; gint code; gchar* message } -> código en offset 4.
        var code = Marshal.ReadInt32(gerror, 4);
        throw new SecretStoreException($"libsecret falló (código {code}).", code);
    }

    // SecretSchema: name, flags, 32 pares (nombre, tipo), reservados. Todo cero salvo lo indicado.
    private sealed class Schema : IDisposable
    {
        public IntPtr Ptr { get; }
        private readonly IntPtr _name = Marshal.StringToCoTaskMemUTF8("org.rama.Password");
        private readonly IntPtr _a1 = Marshal.StringToCoTaskMemUTF8("service");
        private readonly IntPtr _a2 = Marshal.StringToCoTaskMemUTF8("account");

        public Schema()
        {
            Ptr = Marshal.AllocHGlobal(SchemaBytes);
            for (var i = 0; i < SchemaBytes; i += 8) Marshal.WriteInt64(Ptr, i, 0);
            Marshal.WriteIntPtr(Ptr, 0, _name);
            Marshal.WriteInt32(Ptr, 8, 2); // SECRET_SCHEMA_DONT_MATCH_NAME
            Marshal.WriteIntPtr(Ptr, 16, _a1);
            Marshal.WriteIntPtr(Ptr, 32, _a2);
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(Ptr);
            Marshal.FreeCoTaskMem(_name);
            Marshal.FreeCoTaskMem(_a1);
            Marshal.FreeCoTaskMem(_a2);
        }
    }

    private static class Native
    {
        private const string Lib = "libsecret-1.so.0";

        [DllImport(Lib, EntryPoint = "secret_password_store_sync")]
        internal static extern bool Store(IntPtr schema, string collection,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string label, [MarshalAs(UnmanagedType.LPUTF8Str)] string password,
            IntPtr cancellable, ref IntPtr error,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string n1, [MarshalAs(UnmanagedType.LPUTF8Str)] string v1,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string n2, [MarshalAs(UnmanagedType.LPUTF8Str)] string v2,
            IntPtr end);

        [DllImport(Lib, EntryPoint = "secret_password_lookup_sync")]
        internal static extern IntPtr Lookup(IntPtr schema, IntPtr cancellable, ref IntPtr error,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string n1, [MarshalAs(UnmanagedType.LPUTF8Str)] string v1,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string n2, [MarshalAs(UnmanagedType.LPUTF8Str)] string v2,
            IntPtr end);

        [DllImport(Lib, EntryPoint = "secret_password_clear_sync")]
        internal static extern bool Clear(IntPtr schema, IntPtr cancellable, ref IntPtr error,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string n1, [MarshalAs(UnmanagedType.LPUTF8Str)] string v1,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string n2, [MarshalAs(UnmanagedType.LPUTF8Str)] string v2,
            IntPtr end);

        [DllImport(Lib, EntryPoint = "secret_password_free")]
        internal static extern void Free(IntPtr password);
    }
}
