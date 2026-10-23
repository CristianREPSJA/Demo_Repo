using System.Collections.Concurrent;

namespace Rama.Core.Secrets;

// Implementación en memoria para pruebas y modo sin almacén nativo.
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<(string, string), string> _items = new();

    public Task<string?> GetAsync(string service, string account, CancellationToken ct = default)
    {
        SecretStoreFactory.Validate(service, account);
        return Task.FromResult(_items.TryGetValue((service, account), out var v) ? v : null);
    }

    public Task SetAsync(string service, string account, string secret, CancellationToken ct = default)
    {
        SecretStoreFactory.Validate(service, account);
        ArgumentNullException.ThrowIfNull(secret);
        _items[(service, account)] = secret;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string service, string account, CancellationToken ct = default)
    {
        SecretStoreFactory.Validate(service, account);
        _items.TryRemove((service, account), out _);
        return Task.CompletedTask;
    }
}
