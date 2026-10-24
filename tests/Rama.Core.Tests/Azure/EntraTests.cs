using System.Net;
using Rama.Core.Azure.Entra;
using Rama.Core.Http;
using Rama.Core.Tests.Support;

namespace Rama.Core.Tests.Azure;

public class EntraTests
{
    private const string Cid = "11111111-2222-3333-4444-555555555555";

    // ---- EntraOptions ----

    [Fact]
    public void ResolveClientId_PrefersEnvironmentOverride()
    {
        Assert.Equal(Cid, EntraOptions.ResolveClientId("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", _ => Cid));
        Assert.Equal(Cid, EntraOptions.ResolveClientId(null, _ => " " + Cid + " "));
        Assert.Equal(Cid, EntraOptions.ResolveClientId(Cid, _ => null));
        Assert.Equal(Cid, EntraOptions.ResolveClientId(Cid, _ => ""));
    }

    [Fact]
    public void ResolveClientId_RejectsNonGuidValues()
    {
        Assert.Null(EntraOptions.ResolveClientId(null, _ => null));
        Assert.Null(EntraOptions.ResolveClientId("not-a-guid", _ => null));
        Assert.Null(EntraOptions.ResolveClientId(Cid, _ => "garbage; rm -rf"));
        Assert.False(EntraOptions.IsValidClientId(""));
        Assert.True(EntraOptions.IsValidClientId(Cid));
    }

    [Fact]
    public void Options_ValidateClientIdAndExposeConstants()
    {
        Assert.Throws<ArgumentException>(() => new EntraOptions("nope"));
        var o = new EntraOptions(Cid, "C:/cache");
        Assert.Equal(Cid, o.ClientId);
        Assert.Equal("https://login.microsoftonline.com/organizations", EntraOptions.Authority);
        Assert.Equal("499b84ac-1321-427f-aa17-267ca6975798/.default", EntraOptions.AzureDevOpsScope);
        Assert.Equal("http://localhost", EntraOptions.RedirectUri);
        Assert.Equal("RAMA_ENTRA_CLIENT_ID", EntraOptions.ClientIdEnvVar);
    }

    // ---- EntraTokenSource ----

    private sealed class FakeAuthenticator : IEntraAuthenticator
    {
        public List<(string User, bool Force)> Silent { get; } = [];
        public Func<string, bool, EntraToken?> OnSilent { get; set; } = (_, _) => null;
        public List<string> SignedOut { get; } = [];
        public Func<string?, EntraToken> OnInteractive { get; set; } = hint =>
            new("int-tok", DateTimeOffset.UtcNow.AddHours(1), hint ?? "ana@contoso.cl");

        public Task<EntraToken> SignInInteractiveAsync(string? loginHint, CancellationToken ct) =>
            Task.FromResult(OnInteractive(loginHint));

        public Task<EntraToken?> AcquireSilentAsync(string userName, bool forceRefresh, CancellationToken ct)
        {
            Silent.Add((userName, forceRefresh));
            return Task.FromResult(OnSilent(userName, forceRefresh));
        }

        public Task SignOutAsync(string userName, CancellationToken ct)
        {
            SignedOut.Add(userName);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task TokenSource_CachesUntilNearExpiry_ThenRenewsSilently()
    {
        var time = new FakeTimeProvider();
        var auth = new FakeAuthenticator();
        var n = 0;
        auth.OnSilent = (_, _) => new("tok" + (++n), time.GetUtcNow().AddMinutes(10), "ana@contoso.cl");
        var src = new EntraTokenSource(auth, "ana@contoso.cl", time);

        Assert.Null(src.TryGetCachedToken());
        Assert.Equal("tok1", await src.GetAccessTokenAsync(false, CancellationToken.None));
        Assert.Equal("tok1", await src.GetAccessTokenAsync(false, CancellationToken.None));
        Assert.Equal("tok1", src.TryGetCachedToken());
        Assert.Single(auth.Silent);

        time.Advance(TimeSpan.FromMinutes(9)); // queda 1 min: dentro del margen de renovación
        Assert.Null(src.TryGetCachedToken());
        Assert.Equal("tok2", await src.GetAccessTokenAsync(false, CancellationToken.None));
        Assert.Equal(2, auth.Silent.Count);
        Assert.False(auth.Silent[1].Force);
    }

    [Fact]
    public async Task TokenSource_ForceRefreshBypassesCacheAndAsksMsalToRefresh()
    {
        var time = new FakeTimeProvider();
        var auth = new FakeAuthenticator();
        var n = 0;
        auth.OnSilent = (_, _) => new("tok" + (++n), time.GetUtcNow().AddHours(1), "u");
        var src = new EntraTokenSource(auth, "u", time);
        await src.GetAccessTokenAsync(false, CancellationToken.None);
        Assert.Equal("tok2", await src.GetAccessTokenAsync(true, CancellationToken.None));
        Assert.True(auth.Silent[1].Force);
        Assert.Equal("u", auth.Silent[1].User);
    }

    [Fact]
    public async Task TokenSource_NoCachedAccount_ThrowsAuthExceptionAskingToSignIn()
    {
        var src = new EntraTokenSource(new FakeAuthenticator(), "u", new FakeTimeProvider());
        var ex = await Assert.ThrowsAsync<ApiAuthException>(() => src.GetAccessTokenAsync(false, CancellationToken.None));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Contains("Microsoft", ex.Message);
        Assert.Null(src.TryGetCachedToken());
    }

    [Fact]
    public async Task TokenSource_SeedAndInvalidate()
    {
        var time = new FakeTimeProvider();
        var auth = new FakeAuthenticator();
        var src = new EntraTokenSource(auth, "u", time);
        src.Seed(new EntraToken("seeded", time.GetUtcNow().AddHours(1), "u"));
        Assert.Equal("seeded", await src.GetAccessTokenAsync(false, CancellationToken.None));
        Assert.Empty(auth.Silent);
        src.Invalidate();
        Assert.Null(src.TryGetCachedToken());
        Assert.Throws<ArgumentException>(() => new EntraTokenSource(auth, " ", time));
        Assert.Throws<ArgumentNullException>(() => new EntraTokenSource(null!, "u", time));
    }

    [Fact]
    public async Task TokenSource_SerializesConcurrentRenewals()
    {
        var time = new FakeTimeProvider();
        var auth = new FakeAuthenticator();
        var calls = 0;
        auth.OnSilent = (_, _) => { Interlocked.Increment(ref calls); return new("t", time.GetUtcNow().AddHours(1), "u"); };
        var src = new EntraTokenSource(auth, "u", time);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => src.GetAccessTokenAsync(false, CancellationToken.None)));
        Assert.All(results, r => Assert.Equal("t", r));
        Assert.Equal(1, calls);
    }

    // ---- GitAuthEnvironment ----

    [Fact]
    public void GitEnv_AddsExtraHeaderViaGitConfigEnv_WithoutTokenInKey()
    {
        var env = new Dictionary<string, string?>();
        GitAuthEnvironment.Apply(env, "T0K3N");
        Assert.Equal("1", env["GIT_CONFIG_COUNT"]);
        Assert.Equal("http.https://dev.azure.com/.extraheader", env["GIT_CONFIG_KEY_0"]);
        Assert.Equal("Authorization: Bearer T0K3N", env["GIT_CONFIG_VALUE_0"]);
        Assert.Equal(3, env.Count);
    }

    [Fact]
    public void GitEnv_AppendsAfterExistingGitConfigEntries()
    {
        var env = new Dictionary<string, string?>
        {
            ["GIT_CONFIG_COUNT"] = "2",
            ["GIT_CONFIG_KEY_0"] = "a.b",
            ["GIT_CONFIG_VALUE_0"] = "1",
            ["GIT_CONFIG_KEY_1"] = "c.d",
            ["GIT_CONFIG_VALUE_1"] = "2",
        };
        GitAuthEnvironment.Apply(env, "tok");
        Assert.Equal("3", env["GIT_CONFIG_COUNT"]);
        Assert.Equal(GitAuthEnvironment.ExtraHeaderKey, env["GIT_CONFIG_KEY_2"]);
        Assert.Equal("Authorization: Bearer tok", env["GIT_CONFIG_VALUE_2"]);
        Assert.Equal("1", env["GIT_CONFIG_VALUE_0"]);
    }

    [Fact]
    public void GitEnv_InvalidCountIsReset_AndBadTokensRejected()
    {
        var env = new Dictionary<string, string?> { ["GIT_CONFIG_COUNT"] = "abc" };
        GitAuthEnvironment.Apply(env, "tok");
        Assert.Equal("1", env["GIT_CONFIG_COUNT"]);
        Assert.Throws<ArgumentException>(() => GitAuthEnvironment.Apply(env, ""));
        Assert.Throws<ArgumentException>(() => GitAuthEnvironment.Apply(env, "a b"));
        Assert.Throws<ArgumentException>(() => GitAuthEnvironment.Apply(env, "a\nb"));
        Assert.Throws<ArgumentNullException>(() => GitAuthEnvironment.Apply(null!, "tok"));
    }

    [Fact]
    public void EntraToken_ToStringNeverPrintsAccessToken()
    {
        var token = new EntraToken("SECRET-ACCESS", new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero), "ana@contoso.cl");
        var text = token.ToString();
        Assert.DoesNotContain("SECRET-ACCESS", text);
        Assert.Contains("ana@contoso.cl", text);
        Assert.DoesNotContain("SECRET-ACCESS", $"{token}");
        Assert.Equal("SECRET-ACCESS", token.AccessToken);
    }

    [Theory]
    [InlineData("-c protocol.file.allow=always submodule add -- https://x y", true)]
    [InlineData("-c core.quotepath=off -c credential.helper=manager fetch origin", true)]
    [InlineData("--no-optional-locks fetch origin", true)]
    [InlineData("-C /repo push origin HEAD", true)]
    [InlineData("-c a=b status --porcelain", false)]
    [InlineData("-c", false)]
    [InlineData("-c a=b", false)]
    [InlineData("--version", false)]
    public void GitEnv_NeedsNetworkSkipsLeadingGlobalOptions(string args, bool expected) =>
        Assert.Equal(expected, GitAuthEnvironment.NeedsNetwork(args));

    [Theory]
    [InlineData("clone --progress -- https://dev.azure.com/o/p/_git/r", true)]
    [InlineData("fetch --progress --verbose origin", true)]
    [InlineData("pull origin main", true)]
    [InlineData("push origin HEAD", true)]
    [InlineData("ls-remote origin", true)]
    [InlineData("submodule update --init", true)]
    [InlineData("  remote show origin", true)]
    [InlineData("lfs pull", true)]
    [InlineData("status --porcelain", false)]
    [InlineData("log --oneline", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void GitEnv_NeedsNetworkOnlyForRemoteCommands(string? args, bool expected) =>
        Assert.Equal(expected, GitAuthEnvironment.NeedsNetwork(args));
}
