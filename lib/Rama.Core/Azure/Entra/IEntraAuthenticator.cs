namespace Rama.Core.Azure.Entra;

// Token de acceso de Entra para Azure DevOps; UserName es el UPN de la cuenta que inició sesión.
public sealed record EntraToken(string AccessToken, DateTimeOffset ExpiresOn, string UserName)
{
    // El ToString() generado imprimiría el token (logs, interpolaciones, depurador): se redacta siempre.
    public override string ToString() => $"EntraToken {{ UserName = {UserName}, ExpiresOn = {ExpiresOn:O}, AccessToken = *** }}";
}

// Fachada sobre MSAL para que la lógica sea verificable con fakes. Las implementaciones traducen sus errores a
// ApiAuthException (cancelado / requiere interacción) o ApiException (servicio), nunca exponen tipos de MSAL.
public interface IEntraAuthenticator
{
    // Abre el navegador del sistema (auth code + PKCE). loginHint preselecciona la cuenta.
    Task<EntraToken> SignInInteractiveAsync(string? loginHint, CancellationToken ct);

    // Token desde el caché (renovado con refresh token si hace falta). null si la cuenta no está en el caché.
    Task<EntraToken?> AcquireSilentAsync(string userName, bool forceRefresh, CancellationToken ct);

    // Quita la cuenta y sus tokens del caché local.
    Task SignOutAsync(string userName, CancellationToken ct);
}
