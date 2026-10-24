using System.Linq;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Rama.Core.Accounts;
using Rama.Core.Azure.Entra;

namespace SourceGit.ViewModels
{
    // Cuenta de Azure DevOps: PAT (llavero) o "Iniciar sesión con Microsoft" (Entra, navegador del sistema).
    public partial class AzureAccountSection : AccountSectionViewModel
    {
        [ObservableProperty]
        private string _orgUrl = string.Empty;

        [ObservableProperty]
        private string _project = string.Empty;

        [ObservableProperty]
        private bool _useEntra;

        [ObservableProperty]
        private string _entraClientId = string.Empty;

        [ObservableProperty]
        private string _entraUser = string.Empty;

        public bool UsePat
        {
            get => !UseEntra;
            set => UseEntra = !value;
        }

        public bool HasEntraUser => EntraUser.Length > 0;
        public string EntraUserLabel => HasEntraUser ? App.Text("Rama.Accounts.Azure.EntraSignedInAs", EntraUser) : string.Empty;

        // Sin Client ID (ni configurado ni RAMA_ENTRA_CLIENT_ID) el login no puede iniciarse: hay que registrar la app.
        public bool IsEntraClientIdMissing => UseEntra && EntraOptions.ResolveClientId(EntraClientId) == null;

        partial void OnUseEntraChanged(bool value)
        {
            OnPropertyChanged(nameof(UsePat));
            OnPropertyChanged(nameof(IsEntraClientIdMissing));
        }

        partial void OnEntraClientIdChanged(string value) => OnPropertyChanged(nameof(IsEntraClientIdMissing));

        partial void OnEntraUserChanged(string value)
        {
            OnPropertyChanged(nameof(HasEntraUser));
            OnPropertyChanged(nameof(EntraUserLabel));
        }

        public override void LoadFrom(AccountsData data)
        {
            var a = data.Azure.FirstOrDefault();
            if (a == null)
                return;

            OrgUrl = a.OrgUrl;
            Project = a.Project;
            UseEntra = a.IsEntra;
            EntraClientId = a.EntraClientId ?? string.Empty;
            EntraUser = a.UserName ?? string.Empty;
            if (!a.IsEntra)
                _ = RefreshSecretFlagAsync(a);
        }

        private async Task RefreshSecretFlagAsync(AzureAccount a) =>
            HasSavedSecret = await SourceGit.Rama.RamaServices.Accounts.HasSecretAsync(AccountPlatform.Azure, a.Organization);

        private AzureAccount Build() => new(OrgUrl.Trim(), Project.Trim(),
            Auth: UseEntra ? AzureAuthKind.Entra : AzureAuthKind.Pat,
            UserName: UseEntra ? NullIfEmpty(EntraUser) : null,
            EntraClientId: NullIfEmpty(EntraClientId.Trim()));

        private static string NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

        // Abre el navegador, resuelve el usuario y guarda la cuenta (sin tokens en disco).
        [RelayCommand]
        private Task SignInAsync() => RunAsync(async () =>
        {
            var svc = SourceGit.Rama.RamaServices.Accounts;
            var (result, account) = await svc.SignInWithMicrosoftAsync(Build());
            if (!result.Success || account == null)
            {
                SetStatus(result.Message, true);
                return;
            }

            EntraUser = account.UserName ?? string.Empty;
            await svc.SaveAzureAsync(account, null);
            OrgUrl = account.OrgUrl;
            HasSavedSecret = false;
            SetStatus($"{result.Message} {App.Text("Rama.Accounts.SavedOk")}", false);
        });

        protected override Task<ConnectionTestResult> TestCoreAsync() =>
            SourceGit.Rama.RamaServices.Accounts.TestAzureAsync(Build(), UseEntra ? null : Secret);

        protected override async Task SaveCoreAsync()
        {
            var svc = SourceGit.Rama.RamaServices.Accounts;
            var a = Build();
            await svc.SaveAzureAsync(a, UseEntra ? null : Secret);
            OrgUrl = svc.Current.Azure.First(x => x.Project == a.Project).OrgUrl;
            HasSavedSecret = !UseEntra;
        }

        protected override async Task RemoveCoreAsync()
        {
            await SourceGit.Rama.RamaServices.Accounts.RemoveAzureAsync(Build());
            HasSavedSecret = false;
            EntraUser = string.Empty;
        }
    }
}
