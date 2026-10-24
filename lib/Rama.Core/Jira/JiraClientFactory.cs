using Rama.Core.Http;
using Rama.Core.Secrets;

namespace Rama.Core.Jira;

// Credenciales en ISecretStore: servicio "jira", cuenta = correo.
public static class JiraClientFactory
{
    public const string SecretService = "jira";

    public static Task SaveTokenAsync(ISecretStore store, string email, string apiToken, CancellationToken ct = default) =>
        store.SetAsync(SecretService, email, apiToken, ct);

    public static Task DeleteTokenAsync(ISecretStore store, string email, CancellationToken ct = default) =>
        store.DeleteAsync(SecretService, email, ct);

    // baseUrlOverride: gateway con alcance (verificado 2026-10-23) en vez de {site}.atlassian.net.
    public static async Task<JiraClient> CreateAsync(ISecretStore store, HttpClient http, string site, string email,
        string? baseUrlOverride = null, ApiClientOptions? options = null, CancellationToken ct = default)
    {
        var token = await store.GetAsync(SecretService, email, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("No hay API token de Jira guardado para esta cuenta.");
        var baseUrl = baseUrlOverride ?? JiraClient.SiteBaseUrl(site);
        return new JiraClient(http, baseUrl, email, token, options);
    }
}
