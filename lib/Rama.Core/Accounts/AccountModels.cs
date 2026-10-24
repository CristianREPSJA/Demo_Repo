using System.Text.Json.Serialization;
using Rama.Core.Azure;
using Rama.Core.Jira;

namespace Rama.Core.Accounts;

public enum AccountPlatform { Azure, Bitbucket, Jira }

// Cómo se autentica una cuenta de Azure DevOps: PAT (llavero) o sesión de Microsoft (Entra, caché de MSAL).
public enum AzureAuthKind { Pat, Entra }

// Configuración NO secreta de las cuentas; los tokens viven solo en ISecretStore (PAT) o en el caché de MSAL (Entra).
public sealed record AzureAccount(
    string OrgUrl,
    string Project,
    string? DisplayName = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AzureAuthKind>))] AzureAuthKind Auth = AzureAuthKind.Pat,
    string? UserName = null,
    string? EntraClientId = null)
{
    public string Organization => AzureConnection.ParseOrganization(OrgUrl);
    public bool IsEntra => Auth == AzureAuthKind.Entra;
    public AzureConnection ToConnection() => new(Organization, Project);
}

public sealed record BitbucketAccount(string Email, string? Workspace = null);

// Modo de API: Auto = aún sin detectar; Site = {site}.atlassian.net (token clásico);
// Gateway = api.atlassian.com/ex/jira/{CloudId} (token con alcance).
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<JiraAuthMode>))]
public enum JiraAuthMode { Auto, Site, Gateway }

public sealed record JiraAccount(string Site, string Email, string ProjectKey, long? BoardId = null,
    JiraAuthMode Mode = JiraAuthMode.Auto, string? CloudId = null)
{
    // URL del sitio (enlaces /browse); no cambia con el modo.
    public string BaseUrl => JiraClient.SiteBaseUrl(Site);

    // Base de la API REST según el modo detectado.
    [System.Text.Json.Serialization.JsonIgnore]
    public string ApiBaseUrl => Mode == JiraAuthMode.Gateway && !string.IsNullOrEmpty(CloudId)
        ? JiraClient.ScopedBaseUrl(CloudId)
        : BaseUrl;
}

public sealed record AccountsData(
    IReadOnlyList<AzureAccount> Azure,
    IReadOnlyList<BitbucketAccount> Bitbucket,
    IReadOnlyList<JiraAccount> Jira)
{
    public static AccountsData Empty { get; } = new([], [], []);
}

public sealed record AccountsChangedEventArgs(AccountPlatform Platform);

public sealed record ConnectionTestResult(bool Success, string Message, string? UserName = null);
