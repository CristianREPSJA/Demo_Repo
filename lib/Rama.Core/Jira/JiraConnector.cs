using Rama.Core.Accounts;
using Rama.Core.Http;

namespace Rama.Core.Jira;

// Resultado de conectar: cliente listo, cuenta con el modo resuelto y usuario autenticado.
public sealed record JiraConnection(JiraClient Client, JiraAccount Account, JiraUser User);

// Soporta tokens clásicos (sitio) y con alcance (gateway). En Auto prueba el sitio y, ante 401,
// resuelve el cloudId y reintenta por el gateway; el modo resuelto se devuelve para persistirlo.
public static class JiraConnector
{
    public static async Task<JiraConnection> ConnectAsync(HttpClient http, JiraAccount account, string token,
        ApiClientOptions? options = null, CancellationToken ct = default)
    {
        if (account.Mode == JiraAuthMode.Gateway)
            return await ConnectGatewayAsync(http, account, token, options, ct).ConfigureAwait(false);

        var siteClient = new JiraClient(http, account.BaseUrl, account.Email, token, options);
        try
        {
            var user = await siteClient.GetMyselfAsync(ct).ConfigureAwait(false);
            return new JiraConnection(siteClient, account with { Mode = JiraAuthMode.Site, CloudId = null }, user);
        }
        catch (ApiAuthException auth) when (account.Mode == JiraAuthMode.Auto)
        {
            // Si no se puede resolver el cloudId, el 401 original es el error útil.
            string cloudId;
            try { cloudId = await JiraClient.ResolveCloudIdAsync(http, account.Site, ct).ConfigureAwait(false); }
            catch (ApiException) { throw auth; }
            return await ConnectGatewayAsync(http, account with { CloudId = cloudId }, token, options, ct).ConfigureAwait(false);
        }
    }

    private static async Task<JiraConnection> ConnectGatewayAsync(HttpClient http, JiraAccount account, string token,
        ApiClientOptions? options, CancellationToken ct)
    {
        var cloudId = !string.IsNullOrEmpty(account.CloudId)
            ? account.CloudId
            : await JiraClient.ResolveCloudIdAsync(http, account.Site, ct).ConfigureAwait(false);
        var resolved = account with { Mode = JiraAuthMode.Gateway, CloudId = cloudId };
        var client = new JiraClient(http, resolved.ApiBaseUrl, account.Email, token, options);
        var user = await client.GetMyselfAsync(ct).ConfigureAwait(false);
        return new JiraConnection(client, resolved, user);
    }
}
