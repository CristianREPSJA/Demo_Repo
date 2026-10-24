using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Rama.Core.Http;

namespace Rama.Core.Tests;

public class ApiClientBaseTests
{
    private sealed class FakeHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] steps) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> AuthSeen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            AuthSeen.Add(request.Headers.Authorization?.ToString());
            Requests.Add(request);
            var step = steps[Math.Min(Requests.Count, steps.Length) - 1];
            return Task.FromResult(step(request));
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            throw new HttpRequestException("boom Bearer abc123token");
    }

    private sealed class TestClient(HttpClient http, AuthenticationHeaderValue? auth = null, ApiClientOptions? o = null)
        : ApiClientBase(http, auth, o)
    {
        public Task<Dictionary<string, string>?> Get(string url) =>
            GetJsonAsync(url, RamaJsonContext.Default.DictionaryStringString);

        public Task<Dictionary<string, string>?> Post(Dictionary<string, string> b) =>
            SendJsonAsync(HttpMethod.Post, "http://x/p", b, RamaJsonContext.Default.DictionaryStringString,
                RamaJsonContext.Default.DictionaryStringString);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Status(HttpStatusCode c, Action<HttpResponseMessage>? cfg = null)
    {
        var r = new HttpResponseMessage(c);
        cfg?.Invoke(r);
        return r;
    }

    private static (TestClient, FakeHandler, List<TimeSpan>) Make(AuthenticationHeaderValue? auth,
        ApiClientOptions? opts, params Func<HttpRequestMessage, HttpResponseMessage>[] steps)
    {
        var delays = new List<TimeSpan>();
        var o = (opts ?? new ApiClientOptions()) with
        {
            DelayAsync = (d, _) => { delays.Add(d); return Task.CompletedTask; },
        };
        var h = new FakeHandler(steps);
        return (new TestClient(new HttpClient(h), auth, o), h, delays);
    }

    [Fact]
    public async Task Get_DeserializesJsonAndSendsAuth()
    {
        var (c, h, _) = Make(ApiAuth.Bearer("tok"), null, _ => Json("{\"a\":\"b\"}"));
        var r = await c.Get("http://x/y");
        Assert.Equal("b", r!["a"]);
        Assert.Equal("Bearer tok", h.AuthSeen[0]);
    }

    [Fact]
    public async Task Post_SerializesBody()
    {
        string? body = null;
        var (c, _, _) = Make(null, null, req => { body = req.Content!.ReadAsStringAsync().Result; return Json("{}"); });
        await c.Post(new() { ["k"] = "v" });
        Assert.Equal("{\"k\":\"v\"}", body);
    }

    [Fact]
    public async Task EmptyBody_ReturnsDefault()
    {
        var (c, _, _) = Make(null, null, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
        Assert.Null(await c.Get("http://x"));
    }

    [Fact]
    public async Task Retries429HonoringRetryAfterThenSucceeds()
    {
        var (c, h, d) = Make(null, null,
            _ => Status((HttpStatusCode)429, r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7))),
            _ => Status(HttpStatusCode.ServiceUnavailable),
            _ => Json("{}"));
        await c.Get("http://x");
        Assert.Equal(3, h.Requests.Count);
        Assert.Equal(TimeSpan.FromSeconds(7), d[0]);
        Assert.Equal(TimeSpan.FromSeconds(2), d[1]); // backoff base*2^1
    }

    [Fact]
    public async Task RetryAfterIsCappedAndDateSupported()
    {
        var (c, _, d) = Make(null, new ApiClientOptions { MaxDelay = TimeSpan.FromSeconds(5) },
            _ => Status((HttpStatusCode)429, r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(10))),
            _ => Status((HttpStatusCode)429, r => r.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddHours(-1))),
            _ => Json("{}"));
        await c.Get("http://x");
        Assert.Equal(TimeSpan.FromSeconds(5), d[0]);
        Assert.Equal(TimeSpan.Zero, d[1]);
    }

    [Fact]
    public async Task RetriesAreBounded()
    {
        var (c, h, _) = Make(null, new ApiClientOptions { MaxRetries = 2 }, _ => Status((HttpStatusCode)429));
        var ex = await Assert.ThrowsAsync<ApiException>(() => c.Get("http://x"));
        Assert.Equal((HttpStatusCode)429, ex.StatusCode);
        Assert.Equal(3, h.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AuthErrorsMapToApiAuthExceptionWithHint(HttpStatusCode code)
    {
        var (c, h, _) = Make(ApiAuth.Pat("SECRETPAT"), new ApiClientOptions { MissingScopeHint = "Falta scope Code (Read)" },
            _ => Status(code));
        var ex = await Assert.ThrowsAsync<ApiAuthException>(() => c.Get("http://x"));
        Assert.Equal("Falta scope Code (Read)", ex.MissingScopeHint);
        Assert.Equal(code, ex.StatusCode);
        Assert.Single(h.Requests);
        Assert.DoesNotContain("SECRETPAT", ex.ToString());
    }

    [Fact]
    public async Task AuthExceptionWithoutHint_HasPlainMessage()
    {
        var (c, _, _) = Make(null, null, _ => Status(HttpStatusCode.Unauthorized));
        var ex = await Assert.ThrowsAsync<ApiAuthException>(() => c.Get("http://x"));
        Assert.Null(ex.MissingScopeHint);
        Assert.EndsWith("(401).", ex.Message);
    }

    [Fact]
    public async Task OtherErrors_ThrowApiException()
    {
        var (c, _, _) = Make(null, null, _ => Status(HttpStatusCode.NotFound));
        var ex = await Assert.ThrowsAsync<ApiException>(() => c.Get("http://x"));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task NetworkError_IsWrappedAndRedacted()
    {
        var c = new TestClient(new HttpClient(new ThrowingHandler()));
        var ex = await Assert.ThrowsAsync<ApiException>(() => c.Get("http://x"));
        Assert.DoesNotContain("abc123token", ex.Message);
        Assert.Contains("Bearer ***", ex.Message);
    }

    [Fact]
    public void AuthHelpers_BuildHeaders()
    {
        var basic = ApiAuth.Basic("me@x.com", "tok");
        Assert.Equal("Basic", basic.Scheme);
        Assert.Equal("me@x.com:tok", Encoding.UTF8.GetString(Convert.FromBase64String(basic.Parameter!)));
        Assert.Equal(":pat", Encoding.UTF8.GetString(Convert.FromBase64String(ApiAuth.Pat("pat").Parameter!)));
        Assert.Equal("Bearer t", ApiAuth.Bearer("t").ToString());
        Assert.ThrowsAny<ArgumentException>(() => ApiAuth.Bearer(""));
        Assert.ThrowsAny<ArgumentException>(() => ApiAuth.Basic("u", ""));
    }

    [Fact]
    public void Redactor_HidesBasicAndBearer()
    {
        Assert.Equal("h: Basic *** and bearer ***", SecretRedactor.Redact("h: Basic dXNlcjpwYXNz and bearer abc.def"));
        Assert.Equal("", SecretRedactor.Redact(null));
        Assert.Equal("plain", SecretRedactor.Redact("plain"));
    }

    [Fact]
    public void Ctor_RejectsNullHttpClient() =>
        Assert.Throws<ArgumentNullException>(() => new TestClient(null!));

    // ---- Bug real (Azure DevOps 2026-10-23): credenciales inválidas => 203 + página HTML de inicio de sesión ----

    private static HttpResponseMessage Html(HttpStatusCode code) =>
        new(code) { Content = new StringContent("<html><body>Sign in</body></html>", Encoding.UTF8, "text/html") };

    [Fact]
    public async Task Status203_IsTreatedAsAuthFailure()
    {
        var (c, h, _) = Make(ApiAuth.Pat("SECRETPAT"), null, _ => Html(HttpStatusCode.NonAuthoritativeInformation));
        var ex = await Assert.ThrowsAsync<ApiAuthException>(() => c.Get("http://x"));
        Assert.Equal(HttpStatusCode.NonAuthoritativeInformation, ex.StatusCode);
        Assert.Contains("inicio de sesión", ex.Message);
        Assert.Single(h.Requests);
        Assert.DoesNotContain("SECRETPAT", ex.ToString());
    }

    [Fact]
    public async Task Status200WithHtmlBody_IsTreatedAsAuthFailure()
    {
        var (c, _, _) = Make(null, null, _ => Html(HttpStatusCode.OK));
        var ex = await Assert.ThrowsAsync<ApiAuthException>(() => c.Get("http://x"));
        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
    }

    [Fact]
    public async Task Status203_KeepsReasonWhenHintIsAdded()
    {
        var (c, _, _) = Make(null, new ApiClientOptions { MissingScopeHint = "Falta scope" },
            _ => Html(HttpStatusCode.NonAuthoritativeInformation));
        var ex = await Assert.ThrowsAsync<ApiAuthException>(() => c.Get("http://x"));
        Assert.Contains("Falta scope", ex.Message);
        Assert.NotNull(ex.Reason);
        var rethrown = new ApiAuthException(ex.StatusCode, "Otro hint", ex.Reason);
        Assert.Contains("inicio de sesión", rethrown.Message);
        Assert.Contains("Otro hint", rethrown.Message);
    }

    // ---- Proveedor de autenticación renovable (Entra) ----

    private sealed class FakeTokens : IAccessTokenSource
    {
        public List<bool> Calls { get; } = [];
        public int Counter;

        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct)
        {
            Calls.Add(forceRefresh);
            return Task.FromResult("tok" + (++Counter));
        }
    }

    private sealed class ProviderClient(HttpClient http, IAuthProvider auth, ApiClientOptions? o = null)
        : ApiClientBase(http, auth, o)
    {
        public Task<Dictionary<string, string>?> Get(string url) =>
            GetJsonAsync(url, RamaJsonContext.Default.DictionaryStringString);
    }

    [Fact]
    public async Task BearerProvider_RefreshesOnceAndRetriesOn401()
    {
        var tokens = new FakeTokens();
        var h = new FakeHandler(_ => Status(HttpStatusCode.Unauthorized), _ => Json("{\"ok\":\"1\"}"));
        var c = new ProviderClient(new HttpClient(h), new BearerAuthProvider(tokens));
        var r = await c.Get("http://x");
        Assert.Equal("1", r!["ok"]);
        Assert.Equal(["Bearer tok1", "Bearer tok2"], h.AuthSeen);
        Assert.Equal([false, true], tokens.Calls);
    }

    [Fact]
    public async Task BearerProvider_RefreshesOn203SignInPage()
    {
        var tokens = new FakeTokens();
        var h = new FakeHandler(_ => Html(HttpStatusCode.NonAuthoritativeInformation), _ => Json("{}"));
        var c = new ProviderClient(new HttpClient(h), new BearerAuthProvider(tokens));
        await c.Get("http://x");
        Assert.Equal(2, h.Requests.Count);
        Assert.Equal([false, true], tokens.Calls);
    }

    [Fact]
    public async Task BearerProvider_DoesNotLoopWhen401Persists()
    {
        var tokens = new FakeTokens();
        var h = new FakeHandler(_ => Status(HttpStatusCode.Unauthorized));
        var c = new ProviderClient(new HttpClient(h), new BearerAuthProvider(tokens));
        var ex = await Assert.ThrowsAsync<ApiAuthException>(() => c.Get("http://x"));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task BearerProvider_DoesNotRefreshOn403()
    {
        var tokens = new FakeTokens();
        var h = new FakeHandler(_ => Status(HttpStatusCode.Forbidden));
        var c = new ProviderClient(new HttpClient(h), new BearerAuthProvider(tokens));
        await Assert.ThrowsAsync<ApiAuthException>(() => c.Get("http://x"));
        Assert.Single(h.Requests);
        Assert.Equal([false], tokens.Calls);
    }

    [Fact]
    public async Task StaticProvider_NeverRetriesOn401()
    {
        var h = new FakeHandler(_ => Status(HttpStatusCode.Unauthorized));
        var provider = new StaticAuthProvider(ApiAuth.Pat("p"));
        Assert.False(provider.CanRefresh);
        var c = new ProviderClient(new HttpClient(h), provider);
        await Assert.ThrowsAsync<ApiAuthException>(() => c.Get("http://x"));
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task Provider_IsSkippedWhenRequestAlreadyHasAuthorization()
    {
        var tokens = new FakeTokens();
        var h = new FakeHandler(_ => Json("{}"));
        var c = new ProviderClient(new HttpClient(h), new BearerAuthProvider(tokens));
        await c.Get("http://x");
        Assert.Equal(["Bearer tok1"], h.AuthSeen);
        Assert.Throws<ArgumentNullException>(() => new ProviderClient(new HttpClient(h), null!));
    }
}
