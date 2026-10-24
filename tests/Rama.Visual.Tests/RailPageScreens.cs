using Avalonia.Headless.XUnit;

using Rama.Core.Clone;
using Rama.Core.Reviews;

using SourceGit.ViewModels;
using SourceGit.Views;

namespace Rama.VisualTests;

// Páginas del riel con datos simulados: Cuentas, Clonar, Revisiones pendientes (PR y despliegue).
public class RailPageScreens : IDisposable
{
    public void Dispose() { }

    public async Task Accounts()
    {
        if (!VisualEnvironment.Enabled) return;
        var window = Screens.NewLauncher(null, out var launcher);
        launcher.Rail.ShowCommand.Execute(RailDestination.Accounts);
        await Shot.Settle();
        Screens.AssertOnlyVisible<AccountsPage>(window);
        await Shot.Settle();

        var vm = (AccountsViewModel)Shot.Find<AccountsPage>(window).DataContext;
        vm.Azure.OrgUrl = "https://dev.azure.com/contoso";
        vm.Azure.Project = "Ventas";
        vm.Azure.UseEntra = false;
        vm.Azure.HasSavedSecret = true;
        vm.Azure.Expiry = DateTime.Now.AddDays(12);
        vm.Azure.SetStatus("Conexión correcta como Diego Muñoz.", false);
        vm.Bitbucket.Email = "diego@example.com";
        vm.Bitbucket.Workspace = "contoso-ws";
        vm.Bitbucket.HasSavedSecret = true;
        vm.Jira.Site = "contoso.atlassian.net";
        vm.Jira.Email = "diego@example.com";
        vm.Jira.ProjectKey = "DFE";
        vm.Jira.Expiry = DateTime.Now.AddDays(-2);
        vm.Jira.SetStatus("Jira rechazó tus credenciales. Revisa tu API token.", true);
        await Shot.Save(window, "04-accounts-azure-pat-1440x900", 1440, 900);

        vm.Azure.UseEntra = true;
        vm.Azure.EntraUser = "diego@contoso.com";
        vm.Azure.EntraClientId = "3f2a9c1e-7b4d-4e58-9a61-c0d5e8b72f14";
        await Shot.Save(window, "05-accounts-azure-entra-1440x900", 1440, 900);

        vm.Azure.EntraUser = string.Empty;
        vm.Azure.EntraClientId = string.Empty;
        await Shot.Save(window, "05b-accounts-entra-sin-clientid-1440x900", 1440, 900);
    }

    public async Task Clone()
    {
        if (!VisualEnvironment.Enabled) return;
        var window = Screens.NewLauncher(null, out var launcher);
        launcher.Rail.ShowCommand.Execute(RailDestination.Clone);
        await Shot.Settle();
        Screens.AssertOnlyVisible<ClonePage>(window);
        await Shot.Settle();

        var vm = (ClonePageViewModel)Shot.Find<ClonePage>(window).DataContext;
        vm.Message = string.Empty;
        vm.Accounts.Clear();
        vm.Accounts.Add(new CloneAccountCard("Azure DevOps", "contoso / Ventas", "vence en 12 días", true));
        vm.Accounts.Add(new CloneAccountCard("Bitbucket Cloud", "contoso-ws", string.Empty, true));
        var now = DateTimeOffset.Now;
        var items = new List<CloneRepoItem>
        {
            Repo("erp-ventas", "Ventas", "develop", 2, 48_000_000),
            Repo("erp-bodega", "Ventas", "main", 5, 12_500_000),
            Repo("portal-clientes", "Portal", "develop", 1, 220_000_000),
            Repo("app-despacho", "Logística", "master", 30, 5_000_000),
            Repo("libs-comunes", "Plataforma", "main", 90, 900_000),
        };
        Shot.SetField(vm, "_azureItems", items);
        Shot.SetField(vm, "_localIndex", new Dictionary<string, string>
        {
            [Rama.Core.Clone.RemoteUrlMatcher.Normalize(items[1].CloneUrl)] = @"D:\src\erp-bodega",
        });
        Shot.Invoke(vm, "ApplyFilter");
        vm.SelectedRow = vm.Rows[0];
        await Shot.Save(window, "06-clone-azure-list-1440x900", 1440, 900);
        _ = now;
    }

    public async Task PendingReviews()
    {
        if (!VisualEnvironment.Enabled) return;
        var window = Screens.NewLauncher(null, out var launcher);
        launcher.Rail.ShowCommand.Execute(RailDestination.PendingReviews);
        await Shot.Settle();
        Screens.AssertOnlyVisible<PendingReviewsPage>(window);
        await Shot.Settle();

        var page = Shot.Find<PendingReviewsPage>(window);
        var vm = new PendingReviewsViewModel { HasAccount = true };
        var service = FakeAzure.CreateService();
        var snapshot = await service.LoadAsync();
        Shot.SetField(vm, "_service", service);
        page.DataContext = vm;
        Shot.Invoke(vm, "ApplySnapshot", snapshot);
        await Shot.Settle(10, 60);
        await Shot.Save(window, "07-pending-reviews-pr-detail-1440x900", 1440, 900);
        await Shot.Save(window, "07-pending-reviews-pr-detail-1024x700", 1024, 700);

        var deploy = vm.Deployments[0];
        deploy.SelectCommand.Execute(null);
        await Shot.Settle(10, 60);
        await Shot.Save(window, "08-pending-reviews-deployment-1440x900", 1440, 900);

        // Confirmación inline de aprobar (estado de interés para revisión visual).
        ((DeploymentDetailViewModel)vm.Detail).ApproveCommand.Execute(null);
        await Shot.Save(window, "08b-pending-reviews-deployment-confirm-1440x900", 1440, 900);

        var mine = vm.MyPullRequests[0];
        mine.SelectCommand.Execute(null);
        await Shot.Settle(10, 60);
        await Shot.Save(window, "09-pending-reviews-my-pr-1440x900", 1440, 900);
    }

    private static CloneRepoItem Repo(string name, string project, string branch, int daysAgo, long size) =>
        new(CloneProvider.Azure, name, null, project, branch, DateTimeOffset.Now.AddDays(-daysAgo), size,
            $"https://contoso@dev.azure.com/contoso/{project}/_git/{name}");
}
