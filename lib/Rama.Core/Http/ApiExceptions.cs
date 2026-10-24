using System.Net;
using System.Text.RegularExpressions;

namespace Rama.Core.Http;

public static partial class SecretRedactor
{
    [GeneratedRegex(@"\b(Basic|Bearer)\s+[A-Za-z0-9\-._~+/=]+", RegexOptions.IgnoreCase)]
    private static partial Regex AuthPattern();

    // Oculta credenciales de Authorization en cualquier texto.
    public static string Redact(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : AuthPattern().Replace(text, "$1 ***");
}

public class ApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public ApiException(HttpStatusCode statusCode, string message)
        : base(SecretRedactor.Redact(message)) => StatusCode = statusCode;

    public override string ToString() => SecretRedactor.Redact(base.ToString());
}

public sealed class ApiAuthException : ApiException
{
    public string? MissingScopeHint { get; }

    // Motivo específico del rechazo (p. ej. página de inicio de sesión); se conserva al reetiquetar el hint.
    public string? Reason { get; }

    public ApiAuthException(HttpStatusCode statusCode, string? missingScopeHint, string? reason = null)
        : base(statusCode, (string.IsNullOrWhiteSpace(reason) ? $"Autenticación rechazada ({(int)statusCode})." : reason)
                           + (string.IsNullOrWhiteSpace(missingScopeHint) ? "" : $" {missingScopeHint}"))
    {
        MissingScopeHint = missingScopeHint;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason;
    }
}
