using System;
using System.Linq;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

using Rama.Core.Accounts;

namespace SourceGit.ViewModels
{
    public partial class JiraAccountSection : AccountSectionViewModel
    {
        [ObservableProperty]
        private string _site = string.Empty;

        [ObservableProperty]
        private string _email = string.Empty;

        [ObservableProperty]
        private string _projectKey = string.Empty;

        public override void LoadFrom(AccountsData data)
        {
            var a = data.Jira.FirstOrDefault();
            if (a == null)
                return;

            Site = a.Site;
            Email = a.Email;
            ProjectKey = a.ProjectKey;
            _boardId = a.BoardId;
            _ = RefreshAsync(a);
        }

        private async Task RefreshAsync(JiraAccount a)
        {
            var svc = SourceGit.Rama.RamaServices.Accounts;
            HasSavedSecret = await svc.HasSecretAsync(AccountPlatform.Jira, a.Email);
            Expiry = (await svc.GetExpiryDateAsync(AccountPlatform.Jira, a.Email))?.ToDateTime(TimeOnly.MinValue);
        }

        private JiraAccount Build() => new(Site.Trim(), Email.Trim(), ProjectKey.Trim().ToUpperInvariant(), _boardId);

        protected override Task<ConnectionTestResult> TestCoreAsync() =>
            SourceGit.Rama.RamaServices.Accounts.TestJiraAsync(Build(), Secret);

        protected override async Task SaveCoreAsync()
        {
            await SourceGit.Rama.RamaServices.Accounts.SaveJiraAsync(Build(), Secret, ToDateOnly(Expiry));
            HasSavedSecret = true;
        }

        protected override async Task RemoveCoreAsync()
        {
            await SourceGit.Rama.RamaServices.Accounts.RemoveJiraAsync(Build());
            HasSavedSecret = false;
            Expiry = null;
        }

        private long? _boardId;
    }
}
