using System;
using System.Linq;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

using Rama.Core.Accounts;

namespace SourceGit.ViewModels
{
    public partial class BitbucketAccountSection : AccountSectionViewModel
    {
        [ObservableProperty]
        private string _email = string.Empty;

        [ObservableProperty]
        private string _workspace = string.Empty;

        public override void LoadFrom(AccountsData data)
        {
            var a = data.Bitbucket.FirstOrDefault();
            if (a == null)
                return;

            Email = a.Email;
            Workspace = a.Workspace ?? string.Empty;
            _ = RefreshAsync(a);
        }

        private async Task RefreshAsync(BitbucketAccount a)
        {
            var svc = SourceGit.Rama.RamaServices.Accounts;
            HasSavedSecret = await svc.HasSecretAsync(AccountPlatform.Bitbucket, a.Email);
            Expiry = (await svc.GetExpiryDateAsync(AccountPlatform.Bitbucket, a.Email))?.ToDateTime(TimeOnly.MinValue);
        }

        private BitbucketAccount Build() =>
            new(Email.Trim(), string.IsNullOrWhiteSpace(Workspace) ? null : Workspace.Trim());

        protected override Task<ConnectionTestResult> TestCoreAsync() =>
            SourceGit.Rama.RamaServices.Accounts.TestBitbucketAsync(Build(), Secret);

        protected override async Task SaveCoreAsync()
        {
            await SourceGit.Rama.RamaServices.Accounts.SaveBitbucketAsync(Build(), Secret, ToDateOnly(Expiry));
            HasSavedSecret = true;
        }

        protected override async Task RemoveCoreAsync()
        {
            await SourceGit.Rama.RamaServices.Accounts.RemoveBitbucketAsync(Build());
            HasSavedSecret = false;
            Expiry = null;
        }
    }
}
