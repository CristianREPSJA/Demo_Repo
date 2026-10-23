using System.Linq;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

using Rama.Core.Accounts;

namespace SourceGit.ViewModels
{
    public partial class AzureAccountSection : AccountSectionViewModel
    {
        [ObservableProperty]
        private string _orgUrl = string.Empty;

        [ObservableProperty]
        private string _project = string.Empty;

        public override void LoadFrom(AccountsData data)
        {
            var a = data.Azure.FirstOrDefault();
            if (a == null)
                return;

            OrgUrl = a.OrgUrl;
            Project = a.Project;
            _ = RefreshSecretFlagAsync(a);
        }

        private async Task RefreshSecretFlagAsync(AzureAccount a) =>
            HasSavedSecret = await SourceGit.Rama.RamaServices.Accounts.HasSecretAsync(AccountPlatform.Azure, a.Organization);

        private AzureAccount Build() => new(OrgUrl.Trim(), Project.Trim());

        protected override Task<ConnectionTestResult> TestCoreAsync() =>
            SourceGit.Rama.RamaServices.Accounts.TestAzureAsync(Build(), Secret);

        protected override async Task SaveCoreAsync()
        {
            var svc = SourceGit.Rama.RamaServices.Accounts;
            var a = Build();
            await svc.SaveAzureAsync(a, Secret);
            OrgUrl = svc.Current.Azure.First(x => x.Project == a.Project).OrgUrl;
            HasSavedSecret = true;
        }

        protected override async Task RemoveCoreAsync()
        {
            await SourceGit.Rama.RamaServices.Accounts.RemoveAzureAsync(Build());
            HasSavedSecret = false;
        }
    }
}
