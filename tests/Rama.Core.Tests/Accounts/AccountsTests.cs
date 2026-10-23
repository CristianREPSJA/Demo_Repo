using System.Net;
using System.Text;
using Rama.Core.Accounts;
using Rama.Core.Integrations;
using Rama.Core.Secrets;
using Rama.Core.Tests.Support;

namespace Rama.Core.Tests.Accounts;

public class AccountsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rama-acc-" + Guid.NewGuid().ToString("N"));
    private readonly InMemorySecretStore _secrets = new();

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(route(r));
    }

    private static HttpResponseMessage Json(string j, HttpStatusCode c = HttpStatusCode.OK) =>
        new(c) { Content = new StringContent(j, Encoding.UTF8, "application/json") };

    private AccountsService Make(Func<HttpRequestMessage, HttpResponseMessage>? route = null) =>
        new(new AccountsStore(Path.Combine(_dir, "accounts.json")), _secrets,
            new HttpClient(new Handler(route ?? (_ => Json("{}")))));

    [Fact]
    public async Task Store_RoundTripsAndMissingFileIsEmpty()
    {
        var store = new AccountsStore(Path.Combine(_dir, "sub", "a.json"));
        Assert.Empty((await store.LoadAsync()).Azure);
        var data = new AccountsData([new("https://dev.azure.com/acme", "P", "Acme")], [new("a@x.cl", "ws")],
            [new("acme", "a@x.cl", "VEN", 7)]);
        await store.SaveAsync(data);
        var back = await store.LoadAsync();
        Assert.Equal(data.Azure, back.Azure);
        Assert.Equal(data.Bitbucket, back.Bitbucket);
        Assert.Equal(data.Jira, back.Jira);
        Assert.False(File.Exists(store.Path + ".tmp"));
    }

    [Fact]
    public async Task Store_CorruptFileIsMovedAside()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "a.json");
        await File.WriteAllTextAsync(path, "{ no json");
        var data = await new AccountsStore(path).LoadAsync();
        Assert.Empty(data.Jira);
        Assert.True(File.Exists(path + ".bad"));
    }

    [Fact]
    public void Store_RejectsEmptyPath() => Assert.Throws<ArgumentException>(() => new AccountsStore(" "));

    [Fact]
    public async Task Azure_SaveStoresPatOnlyInSecretStoreAndNotInJson()
    {
        var svc = Make();
        var raised = new List<AccountPlatform>();
        svc.Changed += e => raised.Add(e.Platform);
        await svc.SaveAzureAsync(new("acme", "Proj"), "PAT-SECRET");
        Assert.Equal("PAT-SECRET", await _secrets.GetAsync("azure-devops", "acme"));
        Assert.Equal("https://dev.azure.com/acme", svc.Current.Azure[0].OrgUrl);
        Assert.DoesNotContain("PAT-SECRET", await File.ReadAllTextAsync(Path.Combine(_dir, "accounts.json")));
        Assert.Equal([AccountPlatform.Azure], raised);
        Assert.True(await svc.HasSecretAsync(AccountPlatform.Azure, "acme"));
    }

    [Fact]
    public async Task Azure_UpdateWithoutTokenKeepsSecret_RemoveDeletesWhenLastOfOrg()
    {
        var svc = Make();
        await svc.SaveAzureAsync(new("https://dev.azure.com/acme", "A"), "tok");
        await svc.SaveAzureAsync(new("https://dev.azure.com/acme", "B"), null);
        await svc.SaveAzureAsync(new("https://dev.azure.com/acme", "A", "Nombre"), "");
        Assert.Equal(2, svc.Current.Azure.Count);
        Assert.Equal("Nombre", svc.Current.Azure[0].DisplayName);
        await svc.RemoveAzureAsync(new("https://dev.azure.com/acme", "A"));
        Assert.Equal("tok", await _secrets.GetAsync("azure-devops", "acme"));
        await svc.RemoveAzureAsync(new("https://dev.azure.com/acme", "B"));
        Assert.Null(await _secrets.GetAsync("azure-devops", "acme"));
        Assert.Empty(svc.Current.Azure);
    }

    [Theory]
    [InlineData("https://evil.com/acme")]
    [InlineData("http://dev.azure.com/acme")]
    [InlineData("a b")]
    public async Task Azure_RejectsInvalidOrg(string url) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Make().SaveAzureAsync(new(url, "P"), "t"));

    [Fact]
    public async Task Bitbucket_SaveWithExpiryAndRemove()
    {
        var svc = Make();
        await svc.SaveBitbucketAsync(new("a@x.cl", "ws"), "bbtok", new DateOnly(2027, 1, 5));
        Assert.Equal("bbtok", await _secrets.GetAsync("bitbucket", "a@x.cl"));
        Assert.Equal(new DateOnly(2027, 1, 5), await svc.GetExpiryDateAsync(AccountPlatform.Bitbucket, "a@x.cl"));
        await svc.SaveBitbucketAsync(new("a@x.cl", "ws2"), null, null);
        Assert.Equal("ws2", svc.Current.Bitbucket.Single().Workspace);
        Assert.Null(await svc.GetExpiryDateAsync(AccountPlatform.Bitbucket, "a@x.cl"));
        await svc.RemoveBitbucketAsync(new("a@x.cl"));
        Assert.Null(await _secrets.GetAsync("bitbucket", "a@x.cl"));
        Assert.Empty(svc.Current.Bitbucket);
    }

    [Fact]
    public async Task Jira_NormalizesSite_KeepsTokenWhileEmailStillUsed()
    {
        var svc = Make();
        await svc.SaveJiraAsync(new("Acme.atlassian.net", "a@x.cl", "VEN"), "jt", new DateOnly(2026, 11, 1));
        await svc.SaveJiraAsync(new("otro", "a@x.cl", "OPS"), null, null);
        Assert.Equal("acme", svc.Current.Jira[0].Site);
        await svc.RemoveJiraAsync(new("acme.atlassian.net", "a@x.cl", "VEN"));
        Assert.Equal("jt", await _secrets.GetAsync("jira", "a@x.cl"));
        await svc.RemoveJiraAsync(new("otro", "a@x.cl", "OPS"));
        Assert.Null(await _secrets.GetAsync("jira", "a@x.cl"));
    }

    [Fact]
    public async Task Expiry_StatusUsesTracker()
    {
        var time = new FakeTimeProvider();
        var svc = new AccountsService(new AccountsStore(Path.Combine(_dir, "a.json")), _secrets, new HttpClient(), time);
        await svc.SaveJiraAsync(new("acme", "a@x.cl", "V"), "t", new DateOnly(2026, 1, 20));
        var st = await svc.GetExpiryStatusAsync(AccountPlatform.Jira, "a@x.cl");
        Assert.Equal(TokenExpiryState.ExpiringSoon, st.State);
    }

    [Fact]
    public async Task Load_ReadsPreviouslySavedAccounts()
    {
        await Make().SaveBitbucketAsync(new("a@x.cl"), "t", null);
        var other = Make();
        await other.LoadAsync();
        Assert.Single(other.Current.Bitbucket);
    }

    [Fact]
    public async Task TestJira_SuccessUsesMyselfEndpoint()
    {
        string? url = null;
        var svc = Make(r =>
        {
            url = r.RequestUri!.AbsoluteUri;
            return Json("""{"accountId":"u1","displayName":"Ana","active":true}""");
        });
        var res = await svc.TestJiraAsync(new("acme", "a@x.cl", "V"), "tok");
        Assert.True(res.Success);
        Assert.Equal("Ana", res.UserName);
        Assert.Equal("https://acme.atlassian.net/rest/api/3/myself", url);
    }

    [Fact]
    public async Task TestBitbucket_UsesStoredTokenWhenNoneTyped()
    {
        await _secrets.SetAsync("bitbucket", "a@x.cl", "stored");
        string? auth = null;
        var svc = Make(r =>
        {
            auth = r.Headers.Authorization?.Parameter;
            return Json("""{"display_name":"Ana B"}""");
        });
        var res = await svc.TestBitbucketAsync(new("a@x.cl"), null);
        Assert.True(res.Success);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("a@x.cl:stored")), auth);
    }

    [Fact]
    public async Task TestAzure_ParsesConnectionData()
    {
        var svc = Make(r => r.RequestUri!.AbsoluteUri.StartsWith("https://dev.azure.com/acme/_apis/connectionData")
            ? Json("""{"authenticatedUser":{"id":"i1","displayName":"Ana Az"}}""")
            : Json("{}", HttpStatusCode.NotFound));
        var res = await svc.TestAzureAsync(new("acme", "P"), "pat");
        Assert.True(res.Success, res.Message);
        Assert.Equal("Ana Az", res.UserName);
    }

    [Fact]
    public async Task Test_ReportsFailuresWithoutThrowing()
    {
        var r1 = await Make(_ => Json("{}", HttpStatusCode.Unauthorized))
            .TestJiraAsync(new("acme", "a@x.cl", "V"), "bad");
        Assert.False(r1.Success);
        Assert.Contains("401", r1.Message);

        Assert.False((await Make().TestBitbucketAsync(new("a@x.cl"), null)).Success);
        Assert.False((await Make().TestJiraAsync(new("bad site!", "a@x.cl", "V"), "t")).Success);
        Assert.False((await Make(_ => Json("{}")).TestJiraAsync(new("acme", "a@x.cl", "V"), "t")).Success);
    }

    [Fact]
    public void SecretServiceKeysMatchClientFactories()
    {
        Assert.Equal("azure-devops", AccountsService.SecretServiceOf(AccountPlatform.Azure));
        Assert.Equal("bitbucket", AccountsService.SecretServiceOf(AccountPlatform.Bitbucket));
        Assert.Equal("jira", AccountsService.SecretServiceOf(AccountPlatform.Jira));
    }
}
