using System.Net;
using System.Text;
using Rama.Core.Bitbucket;
using Rama.Core.Http;
using Rama.Core.Secrets;

namespace Rama.Core.Tests.Bitbucket;

public class BitbucketClientTests
{
    private sealed class RouteHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        public List<(string Url, string? Auth)> Seen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add((r.RequestUri!.OriginalString, r.Headers.Authorization?.ToString()));
            return Task.FromResult(route(r));
        }
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static BitbucketClient Client(RouteHandler h) =>
        new(new HttpClient(h), "ana@x.cl", "tok123", new ApiClientOptions { DelayAsync = (_, _) => Task.CompletedTask });

    private const string Repo1 = """
        {"name":"Mi Repo","slug":"mi-repo","full_name":"acme/mi-repo","description":"desc","is_private":true,
         "size":2048,"updated_on":"2026-09-01T12:30:00.000000+00:00",
         "project":{"name":"Proj","key":"P"},"mainbranch":{"name":"main","type":"branch"},
         "links":{"clone":[{"name":"https","href":"https://ana@bitbucket.org/acme/mi-repo.git"},
                           {"name":"ssh","href":"git@bitbucket.org:acme/mi-repo.git"}]}}
        """;

    [Fact]
    public async Task ListWorkspaces_PaginatesAndSendsBasicAuth()
    {
        var h = new RouteHandler(r => r.RequestUri!.Query.Contains("page=2")
            ? Json("""{"values":[{"slug":"b","name":"B"}]}""")
            : Json("""{"values":[{"slug":"a","name":"A"},{"name":"sinslug"}],"next":"https://api.bitbucket.org/2.0/workspaces?pagelen=50&page=2"}"""));

        var ws = await Client(h).ListWorkspacesAsync();

        Assert.Equal(["a", "b"], ws.Select(w => w.Slug));
        Assert.Equal(2, h.Seen.Count);
        Assert.StartsWith("https://api.bitbucket.org/2.0/workspaces?pagelen=50", h.Seen[0].Url);
        Assert.Equal(ApiAuth.Basic("ana@x.cl", "tok123").ToString(), h.Seen[0].Auth);
    }

    [Fact]
    public async Task ListWorkspaces_NameFallsBackToSlug_AndEmptyBodyIsEmpty()
    {
        var h = new RouteHandler(_ => Json("""{"values":[{"slug":"a"}]}"""));
        Assert.Equal("a", (await Client(h).ListWorkspacesAsync())[0].Name);

        var h2 = new RouteHandler(_ => Json("{}"));
        Assert.Empty(await Client(h2).ListWorkspacesAsync());
    }

    [Fact]
    public async Task ListRepositories_MapsFieldsAndPaginates()
    {
        var h = new RouteHandler(r => r.RequestUri!.Query.Contains("page=2")
            ? Json("""{"values":[{"slug":"minimo"}]}""")
            : Json($$"""{"values":[{{Repo1}}],"next":"https://api.bitbucket.org/2.0/repositories/acme?pagelen=50&page=2"}"""));

        var repos = await Client(h).ListRepositoriesAsync("acme");

        Assert.Equal("https://api.bitbucket.org/2.0/repositories/acme?pagelen=50", h.Seen[0].Url);
        var r1 = repos[0];
        Assert.Equal(("Mi Repo", "mi-repo", "acme/mi-repo"), (r1.Name, r1.Slug, r1.FullName));
        Assert.Equal(("desc", "Proj", "main"), (r1.Description, r1.ProjectName, r1.MainBranch));
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 12, 30, 0, TimeSpan.Zero), r1.UpdatedOn);
        Assert.Equal(2048, r1.Size);
        Assert.True(r1.IsPrivate);
        var min = repos[1];
        Assert.Equal("minimo", min.Name);
        Assert.Equal("acme/minimo", min.FullName);
        Assert.Null(min.Description);
        Assert.Null(min.ProjectName);
        Assert.Null(min.MainBranch);
        Assert.False(min.IsPrivate);
        Assert.Null(min.HttpsCloneUrl);
    }

    [Fact]
    public async Task Pagination_IgnoresNextLinkToOtherHostOrHttp()
    {
        foreach (var next in new[] { "https://evil.example/x", "http://api.bitbucket.org/2.0/x", "no-es-url" })
        {
            var h = new RouteHandler(_ => Json($$"""{"values":[{"slug":"a"}],"next":"{{next}}"}"""));
            await Client(h).ListWorkspacesAsync();
            Assert.Single(h.Seen);
        }
    }

    [Fact]
    public async Task ListRepositories_EscapesWorkspace_AndValidatesInput()
    {
        var h = new RouteHandler(_ => Json("{}"));
        await Client(h).ListRepositoriesAsync("a b/c");
        Assert.Contains("/2.0/repositories/a%20b%2Fc?", h.Seen[0].Url);
        await Assert.ThrowsAsync<ArgumentException>(() => Client(h).ListRepositoriesAsync(" "));
    }

    [Fact]
    public async Task Unauthorized_ThrowsAuthExceptionWithScopeHint()
    {
        var h = new RouteHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var ex = await Assert.ThrowsAsync<ApiAuthException>(() => Client(h).ListWorkspacesAsync());
        Assert.Contains("read:workspace:bitbucket", ex.MissingScopeHint);
    }

    [Fact]
    public async Task CloneUrlFor_UsesHttpsLinkWithoutUserInfoOrToken()
    {
        var h = new RouteHandler(_ => Json($$"""{"values":[{{Repo1}}]}"""));
        var repo = (await Client(h).ListRepositoriesAsync("acme"))[0];

        var url = BitbucketClient.CloneUrlFor(repo);

        Assert.Equal("https://bitbucket.org/acme/mi-repo.git", url);
        Assert.DoesNotContain("@", url);
        Assert.DoesNotContain("tok123", url);
    }

    [Fact]
    public void CloneUrlFor_FallsBackToConstructedUrl_AndRejectsNull()
    {
        var repo = new BitbucketRepo("n", "s", "acme/s", null, null, null, null, 0, false, null);
        Assert.Equal("https://bitbucket.org/acme/s.git", BitbucketClient.CloneUrlFor(repo));
        Assert.Throws<ArgumentNullException>(() => BitbucketClient.CloneUrlFor(null!));
    }

    [Fact]
    public async Task Clone_IgnoresNonHttpsHref()
    {
        var h = new RouteHandler(_ => Json("""{"values":[{"slug":"s","links":{"clone":[{"name":"https","href":"ssh://x"}]}}]}"""));
        Assert.Null((await Client(h).ListRepositoriesAsync("acme"))[0].HttpsCloneUrl);
    }

    [Fact]
    public async Task Credentials_RoundTripAndClientCreation()
    {
        var store = new InMemorySecretStore();
        var creds = new BitbucketCredentials(store);
        Assert.Null(await creds.CreateClientAsync(new HttpClient(), "ana@x.cl"));

        await creds.SaveAsync("ana@x.cl", "tok123");
        Assert.Equal("tok123", await store.GetAsync("bitbucket", "ana@x.cl"));
        Assert.NotNull(await creds.CreateClientAsync(new HttpClient(), "ana@x.cl"));

        await creds.RemoveAsync("ana@x.cl");
        Assert.Null(await store.GetAsync("bitbucket", "ana@x.cl"));
        await Assert.ThrowsAsync<ArgumentException>(() => creds.SaveAsync("", "t"));
        await Assert.ThrowsAsync<ArgumentException>(() => creds.SaveAsync("a@x.cl", ""));
    }
}
