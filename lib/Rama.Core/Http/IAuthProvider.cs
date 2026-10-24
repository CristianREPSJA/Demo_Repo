using System.Net.Http.Headers;

namespace Rama.Core.Http;

// Origen de la cabecera Authorization de cada petición.
public interface IAuthProvider
{
    // true si tras un rechazo (401 / página de inicio de sesión) vale la pena renovar y reintentar una vez.
    bool CanRefresh { get; }
    ValueTask<AuthenticationHeaderValue?> GetHeaderAsync(bool forceRefresh, CancellationToken ct);
}

// Cabecera fija (PAT / Basic): nunca se renueva.
public sealed class StaticAuthProvider(AuthenticationHeaderValue? header) : IAuthProvider
{
    public bool CanRefresh => false;

    public ValueTask<AuthenticationHeaderValue?> GetHeaderAsync(bool forceRefresh, CancellationToken ct) => new(header);
}

// Fuente asíncrona de tokens de acceso (Entra via MSAL u otro).
public interface IAccessTokenSource
{
    Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct);
}

// Bearer renovable: pide el token a la fuente en cada petición (la fuente cachea).
public sealed class BearerAuthProvider(IAccessTokenSource tokens) : IAuthProvider
{
    private readonly IAccessTokenSource _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));

    public bool CanRefresh => true;

    public async ValueTask<AuthenticationHeaderValue?> GetHeaderAsync(bool forceRefresh, CancellationToken ct) =>
        ApiAuth.Bearer(await _tokens.GetAccessTokenAsync(forceRefresh, ct).ConfigureAwait(false));
}
