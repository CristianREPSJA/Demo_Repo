using System.Net;
using Rama.Core.Accounts;
using Rama.Core.Http;
using Rama.Core.Jira;

namespace Rama.Core.Tests.Jira;

public class JiraConnectorTests
{
    private const string Cloud = "00000000-0000-4000-8000-000000000001";
    private const string Me = """{"accountId":"u1","displayName":"Ana","active":true}""";
    private static readonly ApiClientOptions NoRetry = new() { MaxRetries = 0 };
    private static readonly JiraAccount Acc = new("acme", "a@x.cl", "V");

    private static HttpResponseMessage Status(HttpStatusCode c) =>
        new(c) { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") };

    // Sitio rechaza (token con alcance); el gateway acepta.
    private static HttpResponseMessage ScopedRoute(HttpRequestMessage r)
    {
        var u = r.RequestUri!.AbsoluteUri;
        if (u == "https://acme.atlassian.net/_edge/tenant_info") return JiraClientTests.Json($$"""{"cloudId":"{{Cloud}}"}""");
        if (u.StartsWith("https://api.atlassian.com/ex/jira/" + Cloud)) return JiraClientTests.Json(Me);
        return Status(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ClassicToken_UsesSiteWithoutResolvingCloudId()
    {
        var h = new JiraClientTests.FakeHandler((_, _) => JiraClientTests.Json(Me));
        var c = await JiraConnector.ConnectAsync(new HttpClient(h), Acc, "t", NoRetry);
        Assert.Equal(JiraAuthMode.Site, c.Account.Mode);
        Assert.Null(c.Account.CloudId);
        Assert.Single(h.Calls);
        Assert.Equal("https://acme.atlassian.net/rest/api/3/myself", h.Calls[0].Req.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task ScopedToken_Site401_FallsBackToGateway()
    {
        var h = new JiraClientTests.FakeHandler((r, _) => ScopedRoute(r));
        var c = await JiraConnector.ConnectAsync(new HttpClient(h), Acc, "t", NoRetry);
        Assert.Equal(JiraAuthMode.Gateway, c.Account.Mode);
        Assert.Equal(Cloud, c.Account.CloudId);
        Assert.Equal($"https://api.atlassian.com/ex/jira/{Cloud}", c.Account.ApiBaseUrl);
        Assert.Equal("https://acme.atlassian.net", c.Account.BaseUrl);
        Assert.Equal(["https://acme.atlassian.net/rest/api/3/myself", "https://acme.atlassian.net/_edge/tenant_info",
            $"https://api.atlassian.com/ex/jira/{Cloud}/rest/api/3/myself"],
            h.Calls.Select(x => x.Req.RequestUri!.AbsoluteUri));
        Assert.Null(h.Calls[1].Req.Headers.Authorization); // tenant_info sin credenciales
    }

    [Fact]
    public async Task PersistedGateway_GoesStraightToGateway()
    {
        var h = new JiraClientTests.FakeHandler((r, _) => ScopedRoute(r));
        var acc = Acc with { Mode = JiraAuthMode.Gateway, CloudId = Cloud };
        await JiraConnector.ConnectAsync(new HttpClient(h), acc, "t", NoRetry);
        Assert.Single(h.Calls);
        Assert.StartsWith("https://api.atlassian.com/ex/jira/", h.Calls[0].Req.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task ExplicitSite_DoesNotFallBack()
    {
        var h = new JiraClientTests.FakeHandler((r, _) => ScopedRoute(r));
        await Assert.ThrowsAsync<ApiAuthException>(() =>
            JiraConnector.ConnectAsync(new HttpClient(h), Acc with { Mode = JiraAuthMode.Site }, "t", NoRetry));
        Assert.Single(h.Calls);
    }

    [Fact]
    public async Task InvalidCloudId_IsRejected()
    {
        var h = new JiraClientTests.FakeHandler((r, _) =>
            r.RequestUri!.AbsolutePath.EndsWith("tenant_info")
                ? JiraClientTests.Json("""{"cloudId":"../evil"}""") : Status(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<ApiAuthException>(() =>
            JiraConnector.ConnectAsync(new HttpClient(h), Acc, "t", NoRetry));
        Assert.DoesNotContain(h.Calls, c => c.Req.RequestUri!.Host == "api.atlassian.com");
    }

    [Fact]
    public async Task TenantInfoFailure_IsApiException()
    {
        var h = new JiraClientTests.FakeHandler((r, _) =>
            r.RequestUri!.AbsolutePath.EndsWith("tenant_info") ? Status(HttpStatusCode.NotFound) : Status(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<ApiAuthException>(() => JiraConnector.ConnectAsync(new HttpClient(h), Acc, "t", NoRetry));
    }

    [Fact]
    public async Task TenantInfo_NonJsonBody_IsApiException()
    {
        var h = new JiraClientTests.FakeHandler((r, _) =>
            r.RequestUri!.AbsolutePath.EndsWith("tenant_info") ? JiraClientTests.Json("<html>") : Status(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<ApiAuthException>(() => JiraConnector.ConnectAsync(new HttpClient(h), Acc, "t", NoRetry));
    }

    [Fact]
    public void ApiBaseUrl_AutoAndSite_UseSite()
    {
        Assert.Equal("https://acme.atlassian.net", Acc.ApiBaseUrl);
        Assert.Equal("https://acme.atlassian.net", (Acc with { Mode = JiraAuthMode.Site }).ApiBaseUrl);
    }
}
