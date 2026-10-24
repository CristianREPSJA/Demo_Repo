using System.Net;
using Rama.Core.Http;

namespace Rama.Core.Azure.Entra;

// Fuente de tokens Bearer para una cuenta Entra: cachea en memoria y renueva en silencio (nunca abre el navegador).
public sealed class EntraTokenSource : IAccessTokenSource
{
    public const string SignInHint = "Inicia sesión con Microsoft otra vez (Cuentas > Azure DevOps).";

    // Margen antes del vencimiento en que el token se considera caducado.
    private static readonly TimeSpan RenewMargin = TimeSpan.FromMinutes(2);

    private readonly IEntraAuthenticator _auth;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private EntraToken? _cached;

    public string UserName { get; }

    public EntraTokenSource(IEntraAuthenticator auth, string userName, TimeProvider? time = null)
    {
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        UserName = userName;
        _time = time ?? TimeProvider.System;
    }

    // Token vigente sin tocar MSAL (para procesos síncronos como git); null si no hay uno fresco.
    public string? TryGetCachedToken()
    {
        var t = Volatile.Read(ref _cached);
        return IsFresh(t) ? t!.AccessToken : null;
    }

    public void Seed(EntraToken token) => Volatile.Write(ref _cached, token);

    public void Invalidate() => Volatile.Write(ref _cached, null);

    public async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && TryGetCachedToken() is { } fresh) return fresh;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && TryGetCachedToken() is { } again) return again;
            var token = await _auth.AcquireSilentAsync(UserName, forceRefresh, ct).ConfigureAwait(false);
            if (token is null || string.IsNullOrEmpty(token.AccessToken))
            {
                Invalidate();
                throw new ApiAuthException(HttpStatusCode.Unauthorized, null, "No hay sesión de Microsoft vigente. " + SignInHint);
            }
            Seed(token);
            return token.AccessToken;
        }
        finally { _gate.Release(); }
    }

    private bool IsFresh(EntraToken? t) =>
        t is not null && !string.IsNullOrEmpty(t.AccessToken) && t.ExpiresOn - _time.GetUtcNow() > RenewMargin;
}
