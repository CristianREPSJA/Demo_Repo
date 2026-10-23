using System.Text.Json;

namespace Rama.Core.Accounts;

// Persiste la configuración no secreta de las cuentas como JSON (ruta inyectada).
public sealed class AccountsStore(string path)
{
    public string Path { get; } = string.IsNullOrWhiteSpace(path)
        ? throw new ArgumentException("Ruta vacía.", nameof(path)) : path;

    // Archivo ausente o corrupto => datos vacíos (el archivo corrupto se conserva como .bad).
    public async Task<AccountsData> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(Path)) return AccountsData.Empty;
        try
        {
            await using var stream = File.OpenRead(Path);
            var data = await JsonSerializer.DeserializeAsync(stream, AccountsJsonContext.Default.AccountsData, ct)
                .ConfigureAwait(false);
            return Normalize(data);
        }
        catch (JsonException)
        {
            File.Move(Path, Path + ".bad", overwrite: true);
            return AccountsData.Empty;
        }
    }

    // Escritura atómica: archivo temporal + reemplazo.
    public async Task SaveAsync(AccountsData data, CancellationToken ct = default)
    {
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = Path + ".tmp";
        await using (var stream = File.Create(tmp))
            await JsonSerializer.SerializeAsync(stream, data, AccountsJsonContext.Default.AccountsData, ct)
                .ConfigureAwait(false);
        File.Move(tmp, Path, overwrite: true);
    }

    private static AccountsData Normalize(AccountsData? d) =>
        new(d?.Azure ?? [], d?.Bitbucket ?? [], d?.Jira ?? []);
}
