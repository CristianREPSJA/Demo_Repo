using System.Net;
using System.Text;
using Rama.Core.Accounts;
using Rama.Core.Azure.Entra;
using Rama.Core.Http;
using Rama.Core.Secrets;
using Rama.Core.Tests.Support;

namespace Rama.Core.Tests.Accounts;

// Cuenta Azure DevOps con "Iniciar sesión con Microsoft" (Entra): MSAL se reemplaza por un fake.
public class AccountsEntraTests : IDisposable
{
    private const string Cid = "11111111-2222-3333-4444-555555555555";
    private const string Upn = "ana@contoso.cl";
    private const string UserJson = """{"authenticatedUser":{"id":"i1","providerDisplayName":"Ana Az"}}""";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rama-entra-" + Guid.NewGuid().ToString("N"));
    private readonly InMemorySecretStore _secrets = new();
    private readonly FakeTimeProvider _time = new();
    private readonly List<EntraOptions> _factoryCalls = [];
    private readonly FakeAuthenticator _auth = new();
    private readonly List<string?> _authSeen = [];

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private sealed class FakeAuthenticator : IEntraAuthenticator
    {
        public Dictionary<string, EntraToken> Accounts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> SignedOut { get; } = [];
        public List<string?> InteractiveHints { get; } = [];
        public Exception? InteractiveError { get; set; }

        public Task<EntraToken> SignInInteractiveAsync(string? loginHint, CancellationToken ct)
        {
            InteractiveHints.Add(loginHint);
            if (InteractiveError is not null) throw InteractiveError;
            var token = new EntraToken("int-tok", DateTimeOffset.UtcNow.AddHours(1), Upn);
            Accounts[Upn] = token;
            return Task.FromResult(token);
        }

        public Task<EntraToken?> AcquireSilentAsync(string userName, bool forceRefresh, CancellationToken ct) =>
            Task.FromResult(Accounts.TryGetValue(userName, out var t) ? t : null);

        public Task SignOutAsync(string userName, CancellationToken ct)
        {
            SignedOut.Add(userName);
            Accounts.Remove(userName);
            return Task.CompletedTask;
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(route(r));
    }

    private static HttpResponseMessage Json(string j, HttpStatusCode c = HttpStatusCode.OK) =>
        new(c) { Content = new StringContent(j, Encoding.UTF8, "application/json") };

    private AccountsService Make(Func<HttpRequestMessage, HttpResponseMessage>? route = null, Func<string, string?>? env = null)
    {
        route ??= _ => Json(UserJson);
        var http = new HttpClient(new Handler(r => { _authSeen.Add(r.Headers.Authorization?.ToString()); return route(r); }));
        return new AccountsService(new AccountsStore(Path.Combine(_dir, "accounts.json")), _secrets, http, _time,
            o => { _factoryCalls.Add(o); return _auth; }, env ?? (_ => null));
    }

    private static AzureAccount Draft(string? clientId = Cid) =>
        new("https://dev.azure.com/acme", "Proj", Auth: AzureAuthKind.Entra, EntraClientId: clientId);

    [Fact]
    public async Task Model_DefaultsToPatAndSerializesAuthKindAsText()
    {
        var pat = new AzureAccount("https://dev.azure.com/acme", "P");
        Assert.Equal(AzureAuthKind.Pat, pat.Auth);
        Assert.False(pat.IsEntra);
        var entra = Draft() with { UserName = Upn };
        Assert.True(entra.IsEntra);
        var store = new AccountsStore(Path.Combine(_dir, "a.json"));
        await store.SaveAsync(new AccountsData([entra], [], []));
        var json = await File.ReadAllTextAsync(store.Path);
        Assert.Contains("\"auth\": \"Entra\"", json);
        var back = await store.LoadAsync();
        Assert.Equal(entra, back.Azure[0]);
    }

    [Fact]
    public async Task Store_LoadsLegacyAccountsWithoutAuthField()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "a.json");
        await File.WriteAllTextAsync(path, """{"azure":[{"orgUrl":"https://dev.azure.com/acme","project":"P"}],"bitbucket":[],"jira":[]}""");
        var data = await new AccountsStore(path).LoadAsync();
        Assert.Equal(AzureAuthKind.Pat, data.Azure[0].Auth);
    }

    [Fact]
    public async Task SignIn_UsesInteractiveFlow_ResolvesUser_AndSavesNoSecret()
    {
        var svc = Make();
        var (result, account) = await svc.SignInWithMicrosoftAsync(Draft());
        Assert.True(result.Success, result.Message);
        Assert.Equal("Ana Az", result.UserName);
        Assert.NotNull(account);
        Assert.Equal(Upn, account!.UserName);
        Assert.Equal("Ana Az", account.DisplayName);
        Assert.Equal(AzureAuthKind.Entra, account.Auth);
        Assert.Equal(["Bearer int-tok"], _authSeen);
        Assert.Single(_factoryCalls);
        Assert.Equal(Cid, _factoryCalls[0].ClientId);

        await svc.SaveAzureAsync(account, null);
        var file = await File.ReadAllTextAsync(Path.Combine(_dir, "accounts.json"));
        Assert.DoesNotContain("int-tok", file);
        Assert.Contains(Upn, file);
        Assert.Null(await _secrets.GetAsync("azure-devops", "acme"));
        Assert.True(await svc.HasSecretAsync(AccountPlatform.Azure, "acme") == false);
    }

    [Fact]
    public async Task SignIn_WithoutClientId_ExplainsRegistration()
    {
        var svc = Make();
        var (result, account) = await svc.SignInWithMicrosoftAsync(Draft(null));
        Assert.False(result.Success);
        Assert.Contains("Client ID", result.Message);
        Assert.Null(account);
        Assert.Empty(_auth.InteractiveHints);
    }

    [Fact]
    public async Task SignIn_EnvironmentClientIdOverridesAccount()
    {
        var svc = Make(env: name => name == EntraOptions.ClientIdEnvVar ? Cid : null);
        var (result, _) = await svc.SignInWithMicrosoftAsync(Draft(null));
        Assert.True(result.Success, result.Message);
        Assert.Equal(Cid, _factoryCalls[0].ClientId);
    }

    [Fact]
    public async Task SignIn_ReportsCancelledOrFailedLoginWithoutThrowing()
    {
        _auth.InteractiveError = new ApiAuthException(HttpStatusCode.Unauthorized, null, "Inicio de sesión cancelado.");
        var (r1, a1) = await Make().SignInWithMicrosoftAsync(Draft());
        Assert.False(r1.Success);
        Assert.Contains("cancelado", r1.Message);
        Assert.Null(a1);

        _auth.InteractiveError = null;
        var (r2, _) = await Make(_ => Json("{}", HttpStatusCode.Forbidden)).SignInWithMicrosoftAsync(Draft());
        Assert.False(r2.Success);
        Assert.Contains("403", r2.Message);

        var (r3, _) = await Make().SignInWithMicrosoftAsync(Draft() with { OrgUrl = "https://evil.com/x" });
        Assert.False(r3.Success);
    }

    [Fact]
    public async Task SignIn_PassesExistingUserAsLoginHint()
    {
        await Make().SignInWithMicrosoftAsync(Draft() with { UserName = "viejo@contoso.cl" });
        Assert.Equal(["viejo@contoso.cl"], _auth.InteractiveHints);
    }

    [Fact]
    public async Task Save_EntraWithoutSignIn_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Make().SaveAzureAsync(Draft(), null));
    }

    [Fact]
    public async Task Save_EntraIgnoresPatArgumentAndDropsStalePat()
    {
        await _secrets.SetAsync("azure-devops", "acme", "old-pat");
        var svc = Make();
        await svc.SaveAzureAsync(Draft() with { UserName = Upn }, "typed-pat");
        Assert.Null(await _secrets.GetAsync("azure-devops", "acme"));
        Assert.Equal(AzureAuthKind.Entra, svc.Current.Azure.Single().Auth);
    }

    [Fact]
    public async Task CreateClient_EntraSendsBearerFromSilentCache_AndCachesSource()
    {
        _auth.Accounts[Upn] = new EntraToken("silent-tok", DateTimeOffset.UtcNow.AddHours(1), Upn);
        var svc = Make();
        var account = Draft() with { UserName = Upn };
        var client = await svc.CreateAzureClientAsync(account);
        Assert.NotNull(client);
        await client!.GetCurrentUserAsync();
        await (await svc.CreateAzureClientAsync(account))!.GetCurrentUserAsync();
        Assert.Equal(["Bearer silent-tok", "Bearer silent-tok"], _authSeen);
        Assert.Single(_factoryCalls);
    }

    [Fact]
    public async Task CreateClient_EntraWithoutUser_ReturnsNull_AndListReposExplains()
    {
        var svc = Make();
        Assert.Null(await svc.CreateAzureClientAsync(Draft()));
        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.ListAzureReposAsync(Draft()));
        Assert.Contains("Microsoft", ex.Message);
        Assert.Null(await svc.CreateAzureClientAsync(Draft(null) with { UserName = Upn }));
    }

    [Fact]
    public async Task CreateClient_EntraWithoutCachedAccount_FailsOnFirstCallAskingToSignIn()
    {
        var client = await Make().CreateAzureClientAsync(Draft() with { UserName = Upn });
        var ex = await Assert.ThrowsAsync<ApiAuthException>(() => client!.GetCurrentUserAsync());
        Assert.Contains("Microsoft", ex.Message);
        Assert.Empty(_authSeen);
    }

    [Fact]
    public async Task Test_EntraUsesSilentTokenOnly()
    {
        var svc = Make();
        var r1 = await svc.TestAzureAsync(Draft() with { UserName = Upn }, null);
        Assert.False(r1.Success);
        Assert.Contains("Microsoft", r1.Message);
        Assert.Empty(_auth.InteractiveHints);

        _auth.Accounts[Upn] = new EntraToken("silent-tok", DateTimeOffset.UtcNow.AddHours(1), Upn);
        var r2 = await svc.TestAzureAsync(Draft() with { UserName = Upn }, null);
        Assert.True(r2.Success, r2.Message);
        Assert.Equal("Ana Az", r2.UserName);

        var r3 = await svc.TestAzureAsync(Draft(), null);
        Assert.False(r3.Success);
    }

    [Fact]
    public async Task Test_ExpiredCredentialsSignInPage_IsReportedAsAuthFailure()
    {
        var svc = Make(_ => new HttpResponseMessage(HttpStatusCode.NonAuthoritativeInformation)
        {
            Content = new StringContent("<html>sign in</html>", Encoding.UTF8, "text/html"),
        });
        var r = await svc.TestAzureAsync(new("https://dev.azure.com/acme", "P"), "expired-pat");
        Assert.False(r.Success);
        Assert.Contains("inicio de sesión", r.Message);
        Assert.DoesNotContain("expired-pat", r.Message);
    }

    [Fact]
    public async Task Remove_EntraSignsOutWhenNoOtherAccountUsesTheUser()
    {
        _auth.Accounts[Upn] = new EntraToken("t", DateTimeOffset.UtcNow.AddHours(1), Upn);
        var svc = Make();
        var a = Draft() with { UserName = Upn };
        var b = Draft() with { Project = "Otro", UserName = Upn };
        await svc.SaveAzureAsync(a, null);
        await svc.SaveAzureAsync(b, null);
        await svc.RemoveAzureAsync(a);
        Assert.Empty(_auth.SignedOut);
        await svc.RemoveAzureAsync(b);
        Assert.Equal([Upn], _auth.SignedOut);
        Assert.Null(await svc.TryGetAzureGitTokenAsync());
    }

    [Fact]
    public async Task Remove_InvalidatesTokensOfLiveClients()
    {
        _auth.Accounts[Upn] = new EntraToken("t", DateTimeOffset.UtcNow.AddHours(1), Upn);
        var svc = Make();
        var a = Draft() with { UserName = Upn };
        await svc.SaveAzureAsync(a, null);
        var client = (await svc.CreateAzureClientAsync(a))!;
        await client.GetCurrentUserAsync();
        Assert.Equal("t", svc.TryGetCachedAzureGitToken());

        await svc.RemoveAzureAsync(a);
        Assert.Equal([Upn], _auth.SignedOut);
        // El cliente vivo ya no tiene token en memoria y MSAL ya no tiene la cuenta: falla pidiendo iniciar sesión.
        await Assert.ThrowsAsync<ApiAuthException>(() => client.GetCurrentUserAsync());
        Assert.Equal(["Bearer t"], _authSeen);
    }

    [Fact]
    public async Task Save_SwitchingEntraToPat_SignsOutAndInvalidates()
    {
        _auth.Accounts[Upn] = new EntraToken("t", DateTimeOffset.UtcNow.AddHours(1), Upn);
        var svc = Make();
        var entra = Draft() with { UserName = Upn };
        await svc.SaveAzureAsync(entra, null);
        var client = (await svc.CreateAzureClientAsync(entra))!;
        await client.GetCurrentUserAsync();

        await svc.SaveAzureAsync(new("https://dev.azure.com/acme", "Proj"), "new-pat");
        Assert.Equal([Upn], _auth.SignedOut);
        Assert.Equal(AzureAuthKind.Pat, svc.Current.Azure.Single().Auth);
        Assert.Equal("new-pat", await _secrets.GetAsync("azure-devops", "acme"));
        await Assert.ThrowsAsync<ApiAuthException>(() => client.GetCurrentUserAsync());
        Assert.Null(svc.TryGetCachedAzureGitToken());
    }

    [Fact]
    public async Task Save_SwitchingEntraToPat_KeepsSessionWhileAnotherAccountUsesIt()
    {
        _auth.Accounts[Upn] = new EntraToken("t", DateTimeOffset.UtcNow.AddHours(1), Upn);
        var svc = Make();
        await svc.SaveAzureAsync(Draft() with { UserName = Upn }, null);
        await svc.SaveAzureAsync(Draft() with { Project = "Otro", UserName = Upn }, null);
        await svc.SaveAzureAsync(new("https://dev.azure.com/acme", "Proj"), "pat");
        Assert.Empty(_auth.SignedOut);
        Assert.Equal("t", await svc.TryGetAzureGitTokenAsync());
    }

    [Fact]
    public async Task GitToken_ComesFromFirstEntraAccount_AndCachedVariantIsSynchronous()
    {
        _auth.Accounts[Upn] = new EntraToken("git-tok", DateTimeOffset.UtcNow.AddHours(1), Upn);
        var svc = Make();
        Assert.Null(svc.TryGetCachedAzureGitToken());
        Assert.Null(await svc.TryGetAzureGitTokenAsync());

        await svc.SaveAzureAsync(new("https://dev.azure.com/acme", "PatProj"), "pat");
        Assert.Null(await svc.TryGetAzureGitTokenAsync());

        await svc.SaveAzureAsync(Draft() with { UserName = Upn }, null);
        Assert.Equal("git-tok", await svc.TryGetAzureGitTokenAsync());
        Assert.Equal("git-tok", svc.TryGetCachedAzureGitToken());

        _auth.Accounts.Clear();
        var other = Make();
        await other.LoadAsync();
        Assert.Null(await other.TryGetAzureGitTokenAsync());
    }

    [Fact]
    public async Task EnsureLoaded_ReadsOnce()
    {
        await Make().SaveAzureAsync(Draft() with { UserName = Upn }, null);
        var svc = Make();
        await svc.EnsureLoadedAsync();
        Assert.Single(svc.Current.Azure);
        await svc.SaveAzureAsync(new("https://dev.azure.com/acme", "Other"), "p");
        await svc.EnsureLoadedAsync();
        Assert.Equal(2, svc.Current.Azure.Count);
    }
}
