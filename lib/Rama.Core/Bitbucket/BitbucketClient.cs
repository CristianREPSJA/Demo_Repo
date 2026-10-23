using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using Rama.Core.Http;

namespace Rama.Core.Bitbucket;

// Cliente de Bitbucket Cloud API 2.0. Auth REST: Basic email:token de API (los app passwords están obsoletos).
public sealed class BitbucketClient : ApiClientBase
{
    public const string ApiHost = "api.bitbucket.org";
    public const int PageLen = 50;
    private const int MaxPages = 200;
    private const string WebHost = "bitbucket.org";

    // SUPUESTO: GET /2.0/workspaces (scope read:workspace:bitbucket); alternativa /2.0/user/permissions/workspaces.
    private static readonly string WorkspacesUrl = $"https://{ApiHost}/2.0/workspaces?pagelen={PageLen}";

    private const string ScopeHint =
        "Revisa el email y que el token tenga los scopes read:workspace:bitbucket y read:repository:bitbucket.";

    public BitbucketClient(HttpClient http, string email, string apiToken, ApiClientOptions? options = null)
        : base(http, ApiAuth.Basic(email, apiToken), WithHint(options))
    {
    }

    private static ApiClientOptions WithHint(ApiClientOptions? o) =>
        (o ?? new ApiClientOptions()) with { MissingScopeHint = o?.MissingScopeHint ?? ScopeHint };

    // Prueba de conexión: GET /2.0/user (scope read:user:bitbucket).
    public async Task<BitbucketUser> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var dto = await GetJsonAsync($"https://{ApiHost}/2.0/user", BitbucketJsonContext.Default.BbUserDto, ct)
            .ConfigureAwait(false);
        var name = dto?.DisplayName ?? dto?.Nickname;
        if (dto is null || string.IsNullOrEmpty(name)) throw new ApiException(0, "/2.0/user no devolvió el usuario autenticado.");
        return new BitbucketUser(name, dto.AccountId);
    }

    public async Task<IReadOnlyList<BitbucketWorkspace>> ListWorkspacesAsync(CancellationToken ct = default)
    {
        var result = new List<BitbucketWorkspace>();
        await foreach (var w in PagesAsync(WorkspacesUrl, BitbucketJsonContext.Default.BbPageBbWorkspaceDto, ct))
            if (!string.IsNullOrEmpty(w.Slug)) result.Add(new(w.Slug, w.Name ?? w.Slug));
        return result;
    }

    public async Task<IReadOnlyList<BitbucketRepo>> ListRepositoriesAsync(string workspace, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        var url = $"https://{ApiHost}/2.0/repositories/{Uri.EscapeDataString(workspace)}?pagelen={PageLen}";
        var result = new List<BitbucketRepo>();
        await foreach (var r in PagesAsync(url, BitbucketJsonContext.Default.BbPageBbRepoDto, ct))
            if (!string.IsNullOrEmpty(r.Slug)) result.Add(Map(r, workspace));
        return result;
    }

    // URL HTTPS sin credenciales: la autenticación de git la resuelve Git Credential Manager.
    public static string CloneUrlFor(BitbucketRepo repo)
    {
        ArgumentNullException.ThrowIfNull(repo);
        return repo.HttpsCloneUrl ?? $"https://{WebHost}/{repo.FullName}.git";
    }

    private async IAsyncEnumerable<T> PagesAsync<T>(string firstUrl, JsonTypeInfo<BbPage<T>> info,
        [EnumeratorCancellation] CancellationToken ct)
    {
        string? url = firstUrl;
        for (var page = 0; url is not null && page < MaxPages; page++)
        {
            var data = await GetJsonAsync(url, info, ct).ConfigureAwait(false);
            if (data?.Values is { } values)
                foreach (var v in values) yield return v;
            url = SafeNext(data?.Next);
        }
    }

    // Solo sigue enlaces `next` hacia api.bitbucket.org por HTTPS: evita filtrar la cabecera Authorization.
    private static string? SafeNext(string? next) =>
        Uri.TryCreate(next, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && u.Host.Equals(ApiHost, StringComparison.OrdinalIgnoreCase) ? u.AbsoluteUri : null;

    private static BitbucketRepo Map(BbRepoDto r, string workspace)
    {
        var slug = r.Slug!;
        return new BitbucketRepo(
            r.Name ?? slug, slug, r.FullName ?? $"{workspace}/{slug}",
            string.IsNullOrEmpty(r.Description) ? null : r.Description,
            r.Project?.Name, r.Mainbranch?.Name, r.UpdatedOn, r.Size ?? 0, r.IsPrivate ?? false,
            HttpsClone(r.Links));
    }

    private static string? HttpsClone(BbRepoLinks? links)
    {
        var href = links?.CloneLinks?.FirstOrDefault(l => string.Equals(l.Name, "https", StringComparison.OrdinalIgnoreCase))?.Href;
        if (!Uri.TryCreate(href, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return null;
        // Bitbucket incluye "usuario@" en el href: se elimina.
        return new UriBuilder(u) { UserName = "", Password = "" }.Uri.AbsoluteUri;
    }
}
