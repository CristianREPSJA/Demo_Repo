using Rama.Core.Azure;
using Rama.Core.Bitbucket;
using Rama.Core.Clone;
using Rama.Core.Http;
using Rama.Core.Integrations;
using Rama.Core.Jira;
using Rama.Core.Secrets;

namespace Rama.Core.Accounts;

// API que consumen las fases de UI: cuentas guardadas, secretos, prueba de conexión y vencimiento de tokens.
// Los tokens se leen/escriben solo en ISecretStore con las mismas claves que las fábricas de clientes.
public sealed class AccountsService
{
    private readonly AccountsStore _store;
    private readonly ISecretStore _secrets;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AccountsData _data = AccountsData.Empty;

    public AccountsService(AccountsStore store, ISecretStore secrets, HttpClient http, TimeProvider? time = null)
    {
        _store = store;
        _secrets = secrets;
        _http = http;
        Expiry = new TokenExpiryTracker(secrets, time);
    }

    public TokenExpiryTracker Expiry { get; }
    public AccountsData Current => _data;
    public event Action<AccountsChangedEventArgs>? Changed;

    public async Task LoadAsync(CancellationToken ct = default) =>
        _data = await _store.LoadAsync(ct).ConfigureAwait(false);

    // Servicio de ISecretStore de cada plataforma (la cuenta es org / email).
    public static string SecretServiceOf(AccountPlatform p) => p switch
    {
        AccountPlatform.Azure => AzureConnection.SecretService,
        AccountPlatform.Bitbucket => BitbucketCredentials.Service,
        _ => JiraClientFactory.SecretService,
    };

    public async Task<bool> HasSecretAsync(AccountPlatform platform, string secretAccount, CancellationToken ct = default) =>
        !string.IsNullOrEmpty(await _secrets.GetAsync(SecretServiceOf(platform), secretAccount, ct).ConfigureAwait(false));

    // ---- Azure DevOps (token null/vacío = conservar el PAT guardado) ----
    public Task SaveAzureAsync(AzureAccount account, string? pat, CancellationToken ct = default)
    {
        var org = AzureConnection.ParseOrganization(account.OrgUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.Project);
        var normalized = account with { OrgUrl = $"https://dev.azure.com/{org}" };
        return MutateAsync(AccountPlatform.Azure,
            d => d with { Azure = Upsert(d.Azure, normalized, a => SameAzure(a, normalized)) },
            async () =>
            {
                if (!string.IsNullOrEmpty(pat))
                    await _secrets.SetAsync(AzureConnection.SecretService, org, pat, ct).ConfigureAwait(false);
            }, ct);
    }

    public Task RemoveAzureAsync(AzureAccount account, CancellationToken ct = default)
    {
        var org = account.Organization;
        AccountsData next = AccountsData.Empty;
        return MutateAsync(AccountPlatform.Azure,
            d => next = d with { Azure = d.Azure.Where(a => !SameAzure(a, account)).ToList() },
            async () =>
            {
                if (!next.Azure.Any(a => SameText(a.Organization, org)))
                    await _secrets.DeleteAsync(AzureConnection.SecretService, org, ct).ConfigureAwait(false);
            }, ct);
    }

    public Task<ConnectionTestResult> TestAzureAsync(AzureAccount account, string? pat, CancellationToken ct = default) =>
        TestAsync(AccountPlatform.Azure, () => account.Organization, pat, async secret =>
        {
            var client = new AzureDevOpsClient(_http, account.ToConnection(), secret);
            return (await client.GetCurrentUserAsync(ct).ConfigureAwait(false)).DisplayName;
        }, ct);

    // ---- Bitbucket ----
    public Task SaveBitbucketAsync(BitbucketAccount account, string? token, DateOnly? expiry, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account.Email);
        return MutateAsync(AccountPlatform.Bitbucket,
            d => d with { Bitbucket = Upsert(d.Bitbucket, account, a => SameText(a.Email, account.Email)) },
            async () =>
            {
                if (!string.IsNullOrEmpty(token))
                    await _secrets.SetAsync(BitbucketCredentials.Service, account.Email, token, ct).ConfigureAwait(false);
                await SaveExpiryAsync(BitbucketCredentials.Service, account.Email, expiry, ct).ConfigureAwait(false);
            }, ct);
    }

    public Task RemoveBitbucketAsync(BitbucketAccount account, CancellationToken ct = default) =>
        MutateAsync(AccountPlatform.Bitbucket,
            d => d with { Bitbucket = d.Bitbucket.Where(a => !SameText(a.Email, account.Email)).ToList() },
            async () =>
            {
                await _secrets.DeleteAsync(BitbucketCredentials.Service, account.Email, ct).ConfigureAwait(false);
                await Expiry.ClearAsync(BitbucketCredentials.Service, account.Email, ct).ConfigureAwait(false);
            }, ct);

    public Task<ConnectionTestResult> TestBitbucketAsync(BitbucketAccount account, string? token, CancellationToken ct = default) =>
        TestAsync(AccountPlatform.Bitbucket, () => account.Email, token, async secret =>
            (await new BitbucketClient(_http, account.Email, secret).GetCurrentUserAsync(ct).ConfigureAwait(false)).DisplayName, ct);

    // ---- Jira ----
    public Task SaveJiraAsync(JiraAccount account, string? token, DateOnly? expiry, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account.Email);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.ProjectKey);
        var normalized = account with { Site = NormalizeSite(account.Site) };
        return MutateAsync(AccountPlatform.Jira,
            d => d with { Jira = Upsert(d.Jira, normalized, a => SameJira(a, normalized)) },
            async () =>
            {
                if (!string.IsNullOrEmpty(token))
                    await _secrets.SetAsync(JiraClientFactory.SecretService, normalized.Email, token, ct).ConfigureAwait(false);
                await SaveExpiryAsync(JiraClientFactory.SecretService, normalized.Email, expiry, ct).ConfigureAwait(false);
            }, ct);
    }

    public Task RemoveJiraAsync(JiraAccount account, CancellationToken ct = default)
    {
        var target = account with { Site = NormalizeSite(account.Site) };
        AccountsData next = AccountsData.Empty;
        return MutateAsync(AccountPlatform.Jira,
            d => next = d with { Jira = d.Jira.Where(a => !SameJira(a, target)).ToList() },
            async () =>
            {
                if (next.Jira.Any(a => SameText(a.Email, target.Email))) return;
                await _secrets.DeleteAsync(JiraClientFactory.SecretService, target.Email, ct).ConfigureAwait(false);
                await Expiry.ClearAsync(JiraClientFactory.SecretService, target.Email, ct).ConfigureAwait(false);
            }, ct);
    }

    public Task<ConnectionTestResult> TestJiraAsync(JiraAccount account, string? token, CancellationToken ct = default) =>
        TestAsync(AccountPlatform.Jira, () => account.Email, token, async secret =>
        {
            var client = new JiraClient(_http, JiraClient.SiteBaseUrl(account.Site), account.Email, secret);
            return (await client.GetMyselfAsync(ct).ConfigureAwait(false)).DisplayName;
        }, ct);

    // ---- Catálogo de repos para clonar (el token se lee del llavero y nunca sale de aquí) ----
    public async Task<IReadOnlyList<CloneRepoItem>> ListAzureReposAsync(AzureAccount account, CancellationToken ct = default)
    {
        var pat = await _secrets.GetAsync(AzureConnection.SecretService, account.Organization, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(pat)) throw new ApiException(0, "No hay PAT guardado para " + account.Organization + ".");
        var repos = await new AzureDevOpsClient(_http, account.ToConnection(), pat).ListReposAsync(ct).ConfigureAwait(false);
        return repos.Select(r => CloneRepoItem.From(r, account.Project)).ToList();
    }

    public async Task<IReadOnlyList<CloneRepoItem>> ListBitbucketReposAsync(BitbucketAccount account, CancellationToken ct = default)
    {
        var client = await new BitbucketCredentials(_secrets).CreateClientAsync(_http, account.Email, ct).ConfigureAwait(false)
            ?? throw new ApiException(0, "No hay token guardado para " + account.Email + ".");
        var spaces = string.IsNullOrWhiteSpace(account.Workspace)
            ? (await client.ListWorkspacesAsync(ct).ConfigureAwait(false)).Select(w => w.Slug).ToList()
            : [account.Workspace];
        var items = new List<CloneRepoItem>();
        foreach (var space in spaces)
            items.AddRange((await client.ListRepositoriesAsync(space, ct).ConfigureAwait(false)).Select(CloneRepoItem.From));
        return items;
    }

    // ---- Vencimiento (Bitbucket y Jira; la cuenta es el email) ----
    public Task<TokenExpiryStatus> GetExpiryStatusAsync(AccountPlatform platform, string email, CancellationToken ct = default) =>
        Expiry.GetStatusAsync(SecretServiceOf(platform), email, ct);

    public Task<DateOnly?> GetExpiryDateAsync(AccountPlatform platform, string email, CancellationToken ct = default) =>
        Expiry.GetExpiryAsync(SecretServiceOf(platform), email, ct);

    private async Task SaveExpiryAsync(string service, string email, DateOnly? expiry, CancellationToken ct)
    {
        if (expiry is { } date) await Expiry.SetExpiryAsync(service, email, date, ct).ConfigureAwait(false);
        else await Expiry.ClearAsync(service, email, ct).ConfigureAwait(false);
    }

    private async Task<ConnectionTestResult> TestAsync(AccountPlatform platform, Func<string> secretAccount, string? typed,
        Func<string, Task<string>> call, CancellationToken ct)
    {
        try
        {
            var secret = string.IsNullOrEmpty(typed)
                ? await _secrets.GetAsync(SecretServiceOf(platform), secretAccount(), ct).ConfigureAwait(false)
                : typed;
            if (string.IsNullOrEmpty(secret)) return new(false, "Ingresa el token para probar la conexión.");
            var user = await call(secret).ConfigureAwait(false);
            return new(true, $"Conectado como {user}.", user);
        }
        catch (Exception ex) when (ex is ApiException or ArgumentException)
        {
            return new(false, ex.Message);
        }
    }

    private async Task MutateAsync(AccountPlatform platform, Func<AccountsData, AccountsData> change,
        Func<Task> secretStep, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var next = change(_data);
            await secretStep().ConfigureAwait(false);
            await _store.SaveAsync(next, ct).ConfigureAwait(false);
            _data = next;
        }
        finally { _gate.Release(); }
        Changed?.Invoke(new AccountsChangedEventArgs(platform));
    }

    private static string NormalizeSite(string site) =>
        JiraClient.SiteBaseUrl(site)["https://".Length..^".atlassian.net".Length];

    private static List<T> Upsert<T>(IReadOnlyList<T> list, T item, Func<T, bool> same)
    {
        var result = list.ToList();
        var i = result.FindIndex(x => same(x));
        if (i >= 0) result[i] = item; else result.Add(item);
        return result;
    }

    private static bool SameText(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static bool SameAzure(AzureAccount a, AzureAccount b) =>
        SameText(a.Organization, b.Organization) && SameText(a.Project, b.Project);

    private static bool SameJira(JiraAccount a, JiraAccount b) => SameText(a.Site, b.Site) && SameText(a.Email, b.Email);
}
