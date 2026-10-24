using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;

namespace Rama.Core.Http;

public sealed record ApiClientOptions
{
    public int MaxRetries { get; init; } = 3;
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);
    public string? MissingScopeHint { get; init; }
    public Func<TimeSpan, CancellationToken, Task>? DelayAsync { get; init; }
}

// Base de clientes REST: reintentos acotados en 429/503, 401/403 -> ApiAuthException, sin loguear cabeceras.
// Con un IAuthProvider renovable (Entra), un 401 o una página de inicio de sesión renueva el token y reintenta una vez.
public abstract class ApiClientBase
{
    // Verificado contra Azure DevOps (2026-10-23): credenciales inválidas devuelven 203 + HTML, no 401.
    public const string SignInPageReason =
        "El servicio devolvió una página de inicio de sesión: las credenciales son inválidas o vencieron";

    private readonly HttpClient _http;
    private readonly ApiClientOptions _options;
    private readonly IAuthProvider _auth;

    protected ApiClientBase(HttpClient http, AuthenticationHeaderValue? auth = null, ApiClientOptions? options = null)
        : this(http, new StaticAuthProvider(auth), options)
    {
    }

    protected ApiClientBase(HttpClient http, IAuthProvider auth, ApiClientOptions? options = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _options = options ?? new ApiClientOptions();
    }

    protected Task<T?> GetJsonAsync<T>(string url, JsonTypeInfo<T> info, CancellationToken ct = default) =>
        SendJsonAsync(() => new HttpRequestMessage(HttpMethod.Get, url), info, ct);

    protected Task<TResp?> SendJsonAsync<TReq, TResp>(HttpMethod method, string url, TReq body,
        JsonTypeInfo<TReq> reqInfo, JsonTypeInfo<TResp> respInfo, CancellationToken ct = default) =>
        SendJsonAsync(() => new HttpRequestMessage(method, url) { Content = JsonContent.Create(body, reqInfo) },
            respInfo, ct);

    protected async Task<T?> SendJsonAsync<T>(Func<HttpRequestMessage> factory, JsonTypeInfo<T> info,
        CancellationToken ct = default)
    {
        using var response = await SendAsync(factory, ct).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength == 0) return default;
        return await response.Content.ReadFromJsonAsync(info, ct).ConfigureAwait(false);
    }

    // Devuelve respuesta exitosa; el llamador la desecha.
    protected async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> factory, CancellationToken ct)
    {
        var refreshed = false;
        var forceRefresh = false;
        for (var attempt = 0; ; attempt++)
        {
            var response = await SendOnceAsync(factory, forceRefresh, ct).ConfigureAwait(false);
            forceRefresh = false;

            if (response.IsSuccessStatusCode && !IsSignInPage(response)) return response;

            var status = response.StatusCode;
            if (IsAuthRejected(response) && _auth.CanRefresh && !refreshed)
            {
                response.Dispose();
                refreshed = true;
                forceRefresh = true;
                continue;
            }

            var retryable = status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;
            if (retryable && attempt < _options.MaxRetries)
            {
                var delay = ComputeDelay(response, attempt);
                response.Dispose();
                await (_options.DelayAsync?.Invoke(delay, ct) ?? Task.Delay(delay, ct)).ConfigureAwait(false);
                continue;
            }

            var signIn = IsSignInPage(response);
            response.Dispose();
            throw signIn || status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? new ApiAuthException(status, _options.MissingScopeHint, signIn ? $"{SignInPageReason} ({(int)status})." : null)
                : new ApiException(status, $"La API respondió {(int)status} {status}.");
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(Func<HttpRequestMessage> factory, bool forceRefresh, CancellationToken ct)
    {
        using var request = factory();
        if (request.Headers.Authorization is null)
            request.Headers.Authorization = await _auth.GetHeaderAsync(forceRefresh, ct).ConfigureAwait(false);
        try
        {
            return await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(0, $"Error de red: {ex.Message}");
        }
    }

    // 401 o página de inicio de sesión: candidatos a renovar el token. 403 no (es de permisos).
    private static bool IsAuthRejected(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.Unauthorized || IsSignInPage(response);

    private static bool IsSignInPage(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) return false;
        if (response.StatusCode == HttpStatusCode.NonAuthoritativeInformation) return true;
        var media = response.Content.Headers.ContentType?.MediaType;
        return string.Equals(media, "text/html", StringComparison.OrdinalIgnoreCase);
    }

    private TimeSpan ComputeDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        TimeSpan delay;
        if (retryAfter?.Delta is { } delta) delay = delta;
        else if (retryAfter?.Date is { } date) delay = date - DateTimeOffset.UtcNow;
        else delay = TimeSpan.FromTicks(_options.BaseDelay.Ticks * (1L << Math.Min(attempt, 20)));

        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        return delay > _options.MaxDelay ? _options.MaxDelay : delay;
    }
}
