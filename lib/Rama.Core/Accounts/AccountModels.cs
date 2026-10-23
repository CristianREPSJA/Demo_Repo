using Rama.Core.Azure;
using Rama.Core.Jira;

namespace Rama.Core.Accounts;

public enum AccountPlatform { Azure, Bitbucket, Jira }

// Configuración NO secreta de las cuentas; los tokens viven solo en ISecretStore.
public sealed record AzureAccount(string OrgUrl, string Project, string? DisplayName = null)
{
    public string Organization => AzureConnection.ParseOrganization(OrgUrl);
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
