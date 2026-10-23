using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Rama.Core.Accounts;
using Rama.Core.Clone;
using Rama.Core.Integrations;

namespace SourceGit.ViewModels
{
    public enum CloneTab
    {
        Azure,
        Bitbucket,
        Url,
    }

    // RAMA: pagina "Clonar repositorio" (reemplaza el popup de SourceGit para el riel). Clona con Commands.Clone.
    public partial class ClonePageViewModel : ObservableObject
    {
        public ObservableCollection<CloneAccountCard> Accounts { get; } = [];
        public ObservableCollection<CloneRowViewModel> Rows { get; } = [];
        public ObservableCollection<string> Projects { get; } = [];

        [ObservableProperty]
        private CloneTab _tab = CloneTab.Azure;

        [ObservableProperty]
        private string _search = string.Empty;

        [ObservableProperty]
        private int _selectedProjectIndex;

        [ObservableProperty]
        private CloneRowViewModel _selectedRow;

        [ObservableProperty]
        private string _destination = string.Empty;

        [ObservableProperty]
        private bool _initSubmodules = true;

        [ObservableProperty]
        private string _urlText = string.Empty;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _message = string.Empty;

        [ObservableProperty]
        private string _cloneError = string.Empty;

        [ObservableProperty]
        private bool _isCloning;

        [ObservableProperty]
        private string _cloningName = string.Empty;

        [ObservableProperty]
        private string _progressText = string.Empty;

        [ObservableProperty]
        private string _progressTransferred = string.Empty;

        [ObservableProperty]
        private double _progressPercent;

        [ObservableProperty]
        private bool _progressIndeterminate = true;

        public bool IsAzureTab => Tab == CloneTab.Azure;
        public bool IsBitbucketTab => Tab == CloneTab.Bitbucket;
        public bool IsUrlTab => Tab == CloneTab.Url;
        public bool IsProviderTab => Tab != CloneTab.Url;
        public bool HasMessage => Message.Length > 0;
        public bool HasCloneError => CloneError.Length > 0;
        public bool HasSelection => SelectedRow != null;
        public bool ShowFooter => IsUrlTab || HasSelection;
        public bool ShowHttpsNote => IsProviderTab;

        public string CountText => IsProviderTab
            ? App.Text("Rama.Clone.Count", Rows.Count)
            : string.Empty;

        public string ActionText
        {
            get
            {
                var name = CurrentName;
                return name.Length == 0 ? App.Text("Rama.Clone.ActionPlain") : App.Text("Rama.Clone.Action", name);
            }
        }

        public bool CanClone => !IsCloning && Destination.Trim().Length > 0 &&
            (IsUrlTab ? Models.Remote.IsValidURL(UrlText.Trim()) : SelectedRow is { IsCloned: false });

        private string CurrentName => IsUrlTab ? CloneDestination.NameFromUrl(UrlText) : SelectedRow?.Name ?? string.Empty;

        public ClonePageViewModel()
        {
            _ = ReloadAsync();
        }

        // Recarga cuentas, indice de repos locales y la lista de la pestana actual.
        public async Task ReloadAsync()
        {
            try
            {
                var svc = SourceGit.Rama.RamaServices.Accounts;
                await svc.LoadAsync();
                _azureItems = null;
                _bitbucketItems = null;
                await BuildAccountCardsAsync(svc);
                await RefreshLocalIndexAsync();
                await LoadTabAsync();
            }
            catch (Exception ex)
            {
                Message = ex.Message;
            }
        }

        [RelayCommand]
        private void GoToAccounts()
        {
            App.GetLauncher()?.Rail.ShowCommand.Execute(RailDestination.Accounts);
        }

        [RelayCommand]
        private void SelectTab(CloneTab tab) => Tab = tab;

        [RelayCommand]
        private void OpenCloned(CloneRowViewModel row)
        {
            if (row is { IsCloned: true })
                OpenRepository(row.LocalPath);
        }

        [RelayCommand]
        private void CancelClone() => _cts?.Cancel();

        [RelayCommand]
        private async Task CloneAsync()
        {
            if (!CanClone)
                return;

            var url = IsUrlTab ? UrlText.Trim() : SelectedRow.Item.CloneUrl;
            if (url.StartsWith('-') || !Models.Remote.IsValidURL(url))
            {
                CloneError = App.Text("Rama.Clone.Error.Url");
                return;
            }

            await RunCloneAsync(url, Destination.Trim(), CurrentName);
        }

        public void SetDestinationFromFolder(string folder)
        {
            if (!string.IsNullOrWhiteSpace(folder))
                Destination = CloneDestination.Default(folder, CurrentName);
        }

        public string DestinationParent => CloneDestination.Split(Destination).Parent;

        partial void OnTabChanged(CloneTab value)
        {
            OnPropertyChanged(nameof(IsAzureTab));
            OnPropertyChanged(nameof(IsBitbucketTab));
            OnPropertyChanged(nameof(IsUrlTab));
            OnPropertyChanged(nameof(IsProviderTab));
            OnPropertyChanged(nameof(ShowHttpsNote));
            SelectedRow = null;
            Search = string.Empty;
            SelectedProjectIndex = 0;
            ResetDestination();
            NotifyFooter();
            _ = LoadTabAsync();
        }

        partial void OnSearchChanged(string value) => ApplyFilter();

        partial void OnSelectedProjectIndexChanged(int value)
        {
            if (!_updatingProjects)
                ApplyFilter();
        }

        partial void OnSelectedRowChanged(CloneRowViewModel value)
        {
            if (value is { IsCloned: true })
            {
                SelectedRow = null;
                return;
            }

            ResetDestination();
            NotifyFooter();
        }

        partial void OnUrlTextChanged(string value)
        {
            ResetDestination();
            NotifyFooter();
        }

        partial void OnDestinationChanged(string value) => OnPropertyChanged(nameof(CanClone));
        partial void OnIsCloningChanged(bool value) => OnPropertyChanged(nameof(CanClone));
        partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));
        partial void OnCloneErrorChanged(string value) => OnPropertyChanged(nameof(HasCloneError));

        private void NotifyFooter()
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(ShowFooter));
            OnPropertyChanged(nameof(ActionText));
            OnPropertyChanged(nameof(CanClone));
        }

        private void ResetDestination()
        {
            var name = CurrentName;
            Destination = name.Length == 0 ? string.Empty : CloneDestination.Default(DefaultCloneDir(), name);
        }

        private static string DefaultCloneDir()
        {
            var dir = Preferences.Instance.GetActiveWorkspace()?.DefaultCloneDir;
            return string.IsNullOrEmpty(dir) ? Preferences.Instance.GitDefaultCloneDir : dir;
        }

        private async Task BuildAccountCardsAsync(AccountsService svc)
        {
            var cards = new List<CloneAccountCard>();
            foreach (var a in svc.Current.Azure)
            {
                var expiry = await ExpiryTextAsync(svc, AccountPlatform.Azure, a.Organization);
                cards.Add(new CloneAccountCard("Azure DevOps", $"{a.Organization} / {a.Project}", expiry, true));
            }

            foreach (var b in svc.Current.Bitbucket)
            {
                var expiry = await ExpiryTextAsync(svc, AccountPlatform.Bitbucket, b.Email);
                var detail = string.IsNullOrWhiteSpace(b.Workspace) ? b.Email : b.Workspace;
                cards.Add(new CloneAccountCard("Bitbucket Cloud", detail, expiry, true));
            }

            Accounts.Clear();
            foreach (var c in cards)
                Accounts.Add(c);
        }

        private static async Task<string> ExpiryTextAsync(AccountsService svc, AccountPlatform platform, string account)
        {
            var st = await svc.GetExpiryStatusAsync(platform, account);
            return st.State switch
            {
                TokenExpiryState.Expired => App.Text("Rama.Clone.Account.Expired"),
                TokenExpiryState.ExpiringSoon => App.Text("Rama.Clone.Account.ExpiresIn", st.DaysLeft ?? 0),
                _ => string.Empty,
            };
        }

        private async Task RefreshLocalIndexAsync()
        {
            var paths = new List<string>();
            CollectRepositoryPaths(Preferences.Instance.RepositoryNodes, paths);
            _localIndex = await Task.Run(() => LocalRepoScanner.BuildIndex(paths));
        }

        private static void CollectRepositoryPaths(IEnumerable<RepositoryNode> nodes, List<string> paths)
        {
            foreach (var node in nodes)
            {
                if (node.IsRepository)
                    paths.Add(node.Id);
                else
                    CollectRepositoryPaths(node.SubNodes, paths);
            }
        }

        private async Task LoadTabAsync()
        {
            if (IsUrlTab)
            {
                Message = string.Empty;
                return;
            }

            var tab = Tab;
            if ((tab == CloneTab.Azure ? _azureItems : _bitbucketItems) == null)
            {
                IsLoading = true;
                Message = string.Empty;
                try
                {
                    var items = await FetchAsync(tab);
                    if (tab == CloneTab.Azure) _azureItems = items; else _bitbucketItems = items;
                }
                catch (Exception ex)
                {
                    Message = ex.Message;
                }
                finally
                {
                    IsLoading = false;
                }
            }

            if (tab == Tab)
                ApplyFilter();
        }

        private async Task<List<CloneRepoItem>> FetchAsync(CloneTab tab)
        {
            var svc = SourceGit.Rama.RamaServices.Accounts;
            var items = new List<CloneRepoItem>();
            if (tab == CloneTab.Azure)
            {
                foreach (var a in svc.Current.Azure)
                    items.AddRange(await svc.ListAzureReposAsync(a));
            }
            else
            {
                foreach (var b in svc.Current.Bitbucket)
                    items.AddRange(await svc.ListBitbucketReposAsync(b));
            }

            if (items.Count == 0 && string.IsNullOrEmpty(Message))
                Message = App.Text("Rama.Clone.NoAccount");
            return items;
        }

        private void ApplyFilter()
        {
            var source = IsAzureTab ? _azureItems : IsBitbucketTab ? _bitbucketItems : null;
            if (source == null)
            {
                Rows.Clear();
                OnPropertyChanged(nameof(CountText));
                return;
            }

            UpdateProjects(source);
            var project = SelectedProjectIndex > 0 && SelectedProjectIndex < Projects.Count ? Projects[SelectedProjectIndex] : null;
            var now = DateTimeOffset.Now;
            var previous = SelectedRow?.Item;

            Rows.Clear();
            foreach (var item in CloneListLogic.Filter(source, Search, project))
                Rows.Add(new CloneRowViewModel(item, LocalPathOf(item), now));

            SelectedRow = Rows.FirstOrDefault(r => r.Item == previous);
            OnPropertyChanged(nameof(CountText));
        }

        private void UpdateProjects(List<CloneRepoItem> source)
        {
            var wanted = new List<string> { App.Text("Rama.Clone.AllProjects") };
            wanted.AddRange(CloneListLogic.Projects(source));
            if (wanted.SequenceEqual(Projects))
                return;

            var current = SelectedProjectIndex > 0 && SelectedProjectIndex < Projects.Count ? Projects[SelectedProjectIndex] : null;
            _updatingProjects = true;
            Projects.Clear();
            foreach (var p in wanted)
                Projects.Add(p);
            SelectedProjectIndex = current == null ? 0 : Math.Max(0, wanted.IndexOf(current));
            _updatingProjects = false;
        }

        private string LocalPathOf(CloneRepoItem item) =>
            _localIndex.TryGetValue(RemoteUrlMatcher.Normalize(item.CloneUrl), out var path) ? path : null;

        private void ApplyProgress(GitProgress p)
        {
            ProgressIndeterminate = p.Percent == null;
            ProgressPercent = p.Percent ?? 0;
            ProgressText = p.Percent == null ? p.Phase : $"{p.Phase} {p.Percent}%";
            ProgressTransferred = p.Transferred?.Replace('.', ',') ?? string.Empty;
        }

        private static void OpenRepository(string path)
        {
            var launcher = App.GetLauncher();
            if (launcher == null)
                return;

            launcher.OpenRepositoryInTab(path, null);
            launcher.Rail.ShowCommand.Execute(RailDestination.Repository);
        }

        private List<CloneRepoItem> _azureItems;
        private List<CloneRepoItem> _bitbucketItems;
        private Dictionary<string, string> _localIndex = new();
        private CancellationTokenSource _cts;
        private bool _updatingProjects;
    }
}
