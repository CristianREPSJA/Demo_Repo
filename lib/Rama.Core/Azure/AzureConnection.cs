using System.Text.RegularExpressions;
using Rama.Core.Secrets;

namespace Rama.Core.Azure;

// Conexión a una organización/proyecto de Azure DevOps; el PAT vive solo en ISecretStore.
public sealed partial record AzureConnection
{
    public const string SecretService = "azure-devops";
    public const string ApiVersion = "7.1";

    public string Organization { get; }
    public string Project { get; }
    public string OrgUrl => $"https://dev.azure.com/{Uri.EscapeDataString(Organization)}";
    public string ProjectUrl => $"{OrgUrl}/{Uri.EscapeDataString(Project)}";

    public AzureConnection(string organization, string project)
    {
        if (string.IsNullOrWhiteSpace(organization) || !OrgPattern().IsMatch(organization))
            throw new ArgumentException("Nombre de organización inválido.", nameof(organization));
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        Organization = organization;
        Project = project;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex OrgPattern();

    public Task<string?> LoadPatAsync(ISecretStore store, CancellationToken ct = default) =>
        store.GetAsync(SecretService, Organization, ct);

    public Task SavePatAsync(ISecretStore store, string pat, CancellationToken ct = default) =>
        store.SetAsync(SecretService, Organization, pat, ct);

    public Task DeletePatAsync(ISecretStore store, CancellationToken ct = default) =>
        store.DeleteAsync(SecretService, Organization, ct);
}
