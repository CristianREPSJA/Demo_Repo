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

public sealed record JiraAccount(string Site, string Email, string ProjectKey, long? BoardId = null)
{
    public string BaseUrl => JiraClient.SiteBaseUrl(Site);
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
