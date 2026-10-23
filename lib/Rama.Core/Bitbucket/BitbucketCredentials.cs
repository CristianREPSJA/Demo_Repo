using Rama.Core.Secrets;

namespace Rama.Core.Bitbucket;

// Token de API de Atlassian guardado por email. Las credenciales de git las gestiona Git Credential Manager.
public sealed class BitbucketCredentials(ISecretStore store)
{
    public const string Service = "bitbucket";

    public Task SaveAsync(string email, string apiToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrEmpty(apiToken);
        return store.SetAsync(Service, email, apiToken, ct);
    }

    public Task RemoveAsync(string email, CancellationToken ct = default) => store.DeleteAsync(Service, email, ct);

    public async Task<BitbucketClient?> CreateClientAsync(HttpClient http, string email, CancellationToken ct = default)
    {
        var token = await store.GetAsync(Service, email, ct).ConfigureAwait(false);
        return string.IsNullOrEmpty(token) ? null : new BitbucketClient(http, email, token);
    }
}
