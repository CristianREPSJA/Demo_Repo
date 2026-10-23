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
        using var attrs = new Attributes(service, account);
        var err = IntPtr.Zero;
        var ptr = Native.Lookup(schema.Ptr, attrs.Table, IntPtr.Zero, ref err);
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
        using var attrs = new Attributes(service, account);
        var err = IntPtr.Zero;
        Native.Store(schema.Ptr, attrs.Table, DefaultCollection, $"Rama:{service}:{account}", secret, IntPtr.Zero, ref err);
        ThrowIfError(err);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string service, string account, CancellationToken ct = default)
    {
        SecretStoreFactory.Validate(service, account);
        using var schema = new Schema();
        using var attrs = new Attributes(service, account);
        var err = IntPtr.Zero;
        Native.Clear(schema.Ptr, attrs.Table, IntPtr.Zero, ref err);
        ThrowIfError(err);
        return Task.CompletedTask;
    }

    private static void ThrowIfError(IntPtr gerror)
    {
        if (gerror == IntPtr.Zero) return;
        // GError: { guint32 domain; gint code; gchar* message } -> código en offset 4, mensaje en offset 8.
        var code = Marshal.ReadInt32(gerror, 4);
        var message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(gerror, 8)) ?? "sin mensaje";
        Native.ErrorFree(gerror);
        throw new SecretStoreException($"libsecret falló (código {code}): {message}", code);
    }

    // GHashTable<string,string> con los atributos; las cadenas nativas se liberan al terminar.
    private sealed class Attributes : IDisposable
    {
        private readonly List<IntPtr> _strings = [];
        public IntPtr Table { get; }

        public Attributes(string service, string account)
        {
            var hash = NativeLibrary.GetExport(NativeLibrary.Load(GLib), "g_str_hash");
            var equal = NativeLibrary.GetExport(NativeLibrary.Load(GLib), "g_str_equal");
            Table = Native.TableNew(hash, equal, IntPtr.Zero, IntPtr.Zero);
            Add("service", service);
            Add("account", account);
        }

        private void Add(string key, string value)
        {
            var (k, v) = (Marshal.StringToCoTaskMemUTF8(key), Marshal.StringToCoTaskMemUTF8(value));
            _strings.Add(k);
            _strings.Add(v);
            Native.TableInsert(Table, k, v);
        }

        public void Dispose()
        {
            Native.TableUnref(Table);
            foreach (var s in _strings) Marshal.FreeCoTaskMem(s);
        }
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

    private const string GLib = "libglib-2.0.so.0";

    private static class Native
    {
        private const string Lib = "libsecret-1.so.0";

        [DllImport(Lib, EntryPoint = "secret_password_storev_sync")]
        internal static extern bool Store(IntPtr schema, IntPtr attributes,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string collection,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string label, [MarshalAs(UnmanagedType.LPUTF8Str)] string password,
            IntPtr cancellable, ref IntPtr error);

        [DllImport(Lib, EntryPoint = "secret_password_lookupv_sync")]
        internal static extern IntPtr Lookup(IntPtr schema, IntPtr attributes, IntPtr cancellable, ref IntPtr error);

        [DllImport(Lib, EntryPoint = "secret_password_clearv_sync")]
        internal static extern bool Clear(IntPtr schema, IntPtr attributes, IntPtr cancellable, ref IntPtr error);

        [DllImport(Lib, EntryPoint = "secret_password_free")]
        internal static extern void Free(IntPtr password);

        [DllImport(GLib, EntryPoint = "g_hash_table_new_full")]
        internal static extern IntPtr TableNew(IntPtr hash, IntPtr equal, IntPtr keyDestroy, IntPtr valueDestroy);

        [DllImport(GLib, EntryPoint = "g_hash_table_insert")]
        internal static extern bool TableInsert(IntPtr table, IntPtr key, IntPtr value);

        [DllImport(GLib, EntryPoint = "g_hash_table_unref")]
        internal static extern void TableUnref(IntPtr table);

        [DllImport(GLib, EntryPoint = "g_error_free")]
        internal static extern void ErrorFree(IntPtr error);
    }
}
