namespace Rama.Core.Secrets;

// Almacén de secretos por servicio+cuenta. Nunca persiste en archivos ni registra valores.
public interface ISecretStore
{
    Task<string?> GetAsync(string service, string account, CancellationToken ct = default);
    Task SetAsync(string service, string account, string secret, CancellationToken ct = default);
    Task DeleteAsync(string service, string account, CancellationToken ct = default);
}

public class SecretStoreException(string message, int? code = null) : Exception(message)
{
    public int? Code { get; } = code;
}
