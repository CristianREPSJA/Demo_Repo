using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Rama.Core.Http;

namespace Rama.Core.Azure.Entra;

// Implementación real sobre MSAL (cliente público, navegador del sistema, auth code + PKCE).
// Caché de tokens cifrado por MSAL Extensions: DPAPI (Windows), Keychain (macOS), libsecret (Linux).
[ExcludeFromCodeCoverage(Justification = "Envuelve MSAL: requiere red, navegador y llavero del SO")]
public sealed class MsalEntraAuthenticator : IEntraAuthenticator
{
    private static readonly string[] Scopes = [EntraOptions.AzureDevOpsScope];

    private readonly EntraOptions _options;
    private readonly SemaphoreSlim _init = new(1, 1);
    private IPublicClientApplication? _app;

    public MsalEntraAuthenticator(EntraOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    public async Task<EntraToken> SignInInteractiveAsync(string? loginHint, CancellationToken ct)
    {
        var app = await GetAppAsync(ct).ConfigureAwait(false);
        var request = app.AcquireTokenInteractive(Scopes).WithUseEmbeddedWebView(false);
        request = string.IsNullOrWhiteSpace(loginHint) ? request.WithPrompt(Prompt.SelectAccount) : request.WithLoginHint(loginHint);
        try
        {
            return Map(await request.ExecuteAsync(ct).ConfigureAwait(false));
        }
        catch (MsalClientException ex) when (ex.ErrorCode == MsalError.AuthenticationCanceledError)
        {
            throw new ApiAuthException(HttpStatusCode.Unauthorized, null, "Inicio de sesión con Microsoft cancelado.");
        }
        catch (MsalException ex)
        {
            throw Translate(ex);
        }
    }

    public async Task<EntraToken?> AcquireSilentAsync(string userName, bool forceRefresh, CancellationToken ct)
    {
        var app = await GetAppAsync(ct).ConfigureAwait(false);
        var account = await FindAccountAsync(app, userName).ConfigureAwait(false);
        if (account is null) return null;
        try
        {
            var result = await app.AcquireTokenSilent(Scopes, account).WithForceRefresh(forceRefresh)
                .ExecuteAsync(ct).ConfigureAwait(false);
            return Map(result);
        }
        catch (MsalUiRequiredException)
        {
            return null; // la sesión expiró o requiere interacción: el llamador pide volver a iniciar sesión
        }
        catch (MsalException ex)
        {
            throw Translate(ex);
        }
    }

    public async Task SignOutAsync(string userName, CancellationToken ct)
    {
        var app = await GetAppAsync(ct).ConfigureAwait(false);
        var account = await FindAccountAsync(app, userName).ConfigureAwait(false);
        if (account is not null) await app.RemoveAsync(account).ConfigureAwait(false);
    }

    private static async Task<IAccount?> FindAccountAsync(IPublicClientApplication app, string userName)
    {
        var accounts = await app.GetAccountsAsync().ConfigureAwait(false);
        return accounts.FirstOrDefault(a => string.Equals(a.Username, userName, StringComparison.OrdinalIgnoreCase));
    }

    private static EntraToken Map(AuthenticationResult r) =>
        new(r.AccessToken, r.ExpiresOn, r.Account?.Username ?? string.Empty);

    // Nunca se incluye el token ni el mensaje completo de MSAL (puede traer URLs con parámetros).
    private static ApiException Translate(MsalException ex) =>
        new(0, $"Error de inicio de sesión con Microsoft ({ex.ErrorCode}).");

    private async Task<IPublicClientApplication> GetAppAsync(CancellationToken ct)
    {
        if (_app is not null) return _app;
        await _init.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_app is not null) return _app;
            var app = PublicClientApplicationBuilder.Create(_options.ClientId)
                .WithAuthority(EntraOptions.Authority)
                .WithRedirectUri(EntraOptions.RedirectUri)
                .WithClientName("Rama")
                .Build();
            if (_options.CacheDirectory is { Length: > 0 } dir)
                await RegisterPersistentCacheAsync(app, dir).ConfigureAwait(false);
            _app = app;
            return app;
        }
        finally { _init.Release(); }
    }

    private static async Task RegisterPersistentCacheAsync(IPublicClientApplication app, string dir)
    {
        Directory.CreateDirectory(dir);
        var props = new StorageCreationPropertiesBuilder(EntraOptions.CacheFileName, dir)
            .WithLinuxKeyring("cl.rama.entra", MsalCacheHelper.LinuxKeyRingDefaultCollection, "Rama: caché de tokens de Entra",
                new KeyValuePair<string, string>("Version", "1"), new KeyValuePair<string, string>("Product", "Rama"))
            .WithMacKeyChain("Rama.Entra", "MSALCache")
            .Build();
        var helper = await MsalCacheHelper.CreateAsync(props).ConfigureAwait(false);
        helper.RegisterCache(app.UserTokenCache);
    }
}
