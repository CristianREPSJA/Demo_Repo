using System;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    // Pagina "Cuentas": un formulario por plataforma (una cuenta por plataforma en esta UI).
    public class AccountsViewModel : ObservableObject
    {
        public AzureAccountSection Azure { get; } = new();
        public BitbucketAccountSection Bitbucket { get; } = new();
        public JiraAccountSection Jira { get; } = new();

        public AccountsViewModel()
        {
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            try
            {
                var svc = SourceGit.Rama.RamaServices.Accounts;
                await svc.LoadAsync();
                Azure.LoadFrom(svc.Current);
                Bitbucket.LoadFrom(svc.Current);
                Jira.LoadFrom(svc.Current);
            }
            catch (Exception ex)
            {
                Azure.SetStatus(ex.Message, true);
            }
        }
    }
}
