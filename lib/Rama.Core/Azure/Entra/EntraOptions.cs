namespace Rama.Core.Azure.Entra;

// Parámetros del login con Microsoft (Entra ID) para Azure DevOps. El ClientId lo registra un administrador
// (docs/ENTRA.md); nunca se incluye uno de Microsoft hardcodeado.
public sealed record EntraOptions
{
    public const string Authority = "https://login.microsoftonline.com/organizations";
    public const string AzureDevOpsScope = "499b84ac-1321-427f-aa17-267ca6975798/.default";
    public const string RedirectUri = "http://localhost";
    public const string ClientIdEnvVar = "RAMA_ENTRA_CLIENT_ID";
    public const string CacheFileName = "rama-entra.msalcache";

    public string ClientId { get; }

    // Directorio del caché de tokens de MSAL; null = solo en memoria (pruebas).
    public string? CacheDirectory { get; init; }

    public EntraOptions(string clientId, string? cacheDirectory = null)
    {
        if (!IsValidClientId(clientId)) throw new ArgumentException("Client ID de Entra inválido (debe ser un GUID).", nameof(clientId));
        ClientId = clientId.Trim();
        CacheDirectory = cacheDirectory;
    }

    public static bool IsValidClientId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Guid.TryParseExact(value.Trim(), "D", out var g) && g != Guid.Empty;

    // Variable de entorno primero (equipos gestionados), luego el valor configurado. null si no hay uno válido.
    public static string? ResolveClientId(string? configured, Func<string, string?>? getEnv = null)
    {
        var env = (getEnv ?? Environment.GetEnvironmentVariable)(ClientIdEnvVar);
        if (!string.IsNullOrWhiteSpace(env))
            return IsValidClientId(env) ? env.Trim() : null;
        return IsValidClientId(configured) ? configured!.Trim() : null;
    }
}
