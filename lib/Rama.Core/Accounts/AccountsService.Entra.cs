using Rama.Core.Azure;
using Rama.Core.Azure.Entra;
using Rama.Core.Http;

namespace Rama.Core.Accounts;

// Login con Microsoft (Entra ID) para Azure DevOps. MSAL queda detrás de IEntraAuthenticator; los tokens solo viven
// en el caché de MSAL y en memoria (EntraTokenSource), nunca en rama-accounts.json ni en el llavero de PAT.
public sealed partial class AccountsService
{
    public const string EntraClientIdMissing =
        "Falta el Client ID de la aplicación Entra: un administrador debe registrar Rama en Entra ID (docs/ENTRA.md) "
        + "o definir la variable RAMA_ENTRA_CLIENT_ID.";

    public const string EntraNotSignedIn = "Inicia sesión con Microsoft antes de guardar la cuenta.";

    private readonly TimeProvider _time;
    private readonly Func<EntraOptions, IEntraAuthenticator> _entraFactory;
    private readonly Func<string, string?> _getEnv;
    private readonly object _entraLock = new();
    private readonly Dictionary<string, IEntraAuthenticator> _authenticators = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string ClientId, string User), EntraTokenSource> _tokenSources = [];

    // Abre el navegador, resuelve el usuario en Azure DevOps y devuelve la cuenta lista para guardar (sin tokens).
    public async Task<(ConnectionTestResult Result, AzureAccount? Account)> SignInWithMicrosoftAsync(AzureAccount draft,
        CancellationToken ct = default)
    {
        try
        {
            var org = AzureConnection.ParseOrganization(draft.OrgUrl);
            ArgumentException.ThrowIfNullOrWhiteSpace(draft.Project);
            var clientId = EntraOptions.ResolveClientId(draft.EntraClientId, _getEnv);
            if (clientId is null) return (new(false, EntraClientIdMissing), null);

            var hint = string.IsNullOrWhiteSpace(draft.UserName) ? null : draft.UserName;
            var token = await GetAuthenticator(clientId).SignInInteractiveAsync(hint, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token.UserName))
                return (new(false, "Microsoft no devolvió la cuenta que inició sesión."), null);

            var account = draft with { OrgUrl = $"https://dev.azure.com/{org}", Auth = AzureAuthKind.Entra, UserName = token.UserName };
            var source = GetOrCreateSource(clientId, token.UserName);
            source.Seed(token);
            var user = await NewEntraClient(account, source).GetCurrentUserAsync(ct).ConfigureAwait(false);
            return (new(true, $"Conectado como {user.DisplayName}.", user.DisplayName),
                account with { DisplayName = draft.DisplayName ?? user.DisplayName });
        }
        catch (Exception ex) when (ex is ApiException or ArgumentException)
        {
            return (new(false, ex.Message), null);
        }
    }

    // Token Bearer para git (clone/fetch/push a dev.azure.com): renovación silenciosa, nunca interactiva.
    public async Task<string?> TryGetAzureGitTokenAsync(CancellationToken ct = default)
    {
        foreach (var account in _data.Azure.Where(a => a.IsEntra))
        {
            if (TryGetEntraSource(account) is not { } source) continue;
            try { return await source.GetAccessTokenAsync(false, ct).ConfigureAwait(false); }
            catch (ApiException) { /* esta cuenta no tiene sesión vigente; se prueba la siguiente */ }
        }
        return null;
    }

    // Variante síncrona para procesos que no pueden esperar: solo tokens ya en memoria.
    public string? TryGetCachedAzureGitToken()
    {
        foreach (var account in _data.Azure.Where(a => a.IsEntra))
        {
            if (TryGetEntraSource(account)?.TryGetCachedToken() is { } token) return token;
        }
        return null;
    }

    private AzureDevOpsClient? CreateEntraClient(AzureAccount account) =>
        TryGetEntraSource(account) is { } source ? NewEntraClient(account, source) : null;

    private AzureDevOpsClient NewEntraClient(AzureAccount account, EntraTokenSource source) =>
        new(_http, account.ToConnection(), new BearerAuthProvider(source));

    private async Task<ConnectionTestResult> TestEntraAsync(AzureAccount account, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(account.UserName)) return new(false, EntraNotSignedIn);
            var client = CreateEntraClient(account);
            if (client is null) return new(false, EntraClientIdMissing);
            var user = await client.GetCurrentUserAsync(ct).ConfigureAwait(false);
            return new(true, $"Conectado como {user.DisplayName}.", user.DisplayName);
        }
        catch (Exception ex) when (ex is ApiException or ArgumentException)
        {
            return new(false, ex.Message);
        }
    }

    // Cierra la sesión de MSAL cuando ninguna cuenta restante usa ese usuario con ese Client ID (al quitar la cuenta
    // o al pasarla de Entra a PAT). Invalida la fuente para que los clientes vivos dejen de usar el token en memoria.
    private async Task SignOutEntraIfUnusedAsync(AccountsData next, AzureAccount? removed, CancellationToken ct)
    {
        if (removed is null || !removed.IsEntra || string.IsNullOrWhiteSpace(removed.UserName)) return;
        var clientId = EntraOptions.ResolveClientId(removed.EntraClientId, _getEnv);
        if (clientId is null) return;
        var stillUsed = next.Azure.Any(a => a.IsEntra && a.UserName is not null && SameText(a.UserName, removed.UserName)
            && string.Equals(EntraOptions.ResolveClientId(a.EntraClientId, _getEnv), clientId, StringComparison.OrdinalIgnoreCase));
        if (stillUsed) return;

        lock (_entraLock)
        {
            if (_tokenSources.Remove((clientId, removed.UserName.ToLowerInvariant()), out var source))
                source.Invalidate();
        }
        await GetAuthenticator(clientId).SignOutAsync(removed.UserName, ct).ConfigureAwait(false);
    }

    private EntraTokenSource? TryGetEntraSource(AzureAccount account)
    {
        if (!account.IsEntra || string.IsNullOrWhiteSpace(account.UserName)) return null;
        var clientId = EntraOptions.ResolveClientId(account.EntraClientId, _getEnv);
        return clientId is null ? null : GetOrCreateSource(clientId, account.UserName);
    }

    private EntraTokenSource GetOrCreateSource(string clientId, string userName)
    {
        var key = (clientId, userName.ToLowerInvariant());
        lock (_entraLock)
        {
            if (_tokenSources.TryGetValue(key, out var existing)) return existing;
            var source = new EntraTokenSource(GetAuthenticatorCore(clientId), userName, _time);
            _tokenSources[key] = source;
            return source;
        }
    }

    private IEntraAuthenticator GetAuthenticator(string clientId)
    {
        lock (_entraLock) return GetAuthenticatorCore(clientId);
    }

    // Requiere _entraLock tomado.
    private IEntraAuthenticator GetAuthenticatorCore(string clientId)
    {
        if (_authenticators.TryGetValue(clientId, out var auth)) return auth;
        auth = _entraFactory(new EntraOptions(clientId, EntraCacheDirectory));
        _authenticators[clientId] = auth;
        return auth;
    }

    // Directorio del caché persistente de MSAL (lo fija la app; null = solo memoria, como en pruebas).
    public string? EntraCacheDirectory { get; init; }
}
