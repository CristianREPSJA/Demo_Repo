using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Rama.Core.Accounts;
using Rama.Core.Azure;
using Rama.Core.Http;
using Rama.Core.Reviews;

using SourceGit.Rama;

namespace SourceGit.ViewModels
{
    // Pantalla "Revisiones pendientes". Singleton (RamaServices.Reviews): el sondeo corre mientras la app está abierta.
    // SUPUESTO: se usa la primera cuenta de Azure guardada.
    public partial class PendingReviewsViewModel : ObservableObject
    {
        public AvaloniaList<ReviewItemViewModel> PullRequests { get; } = new();
        public AvaloniaList<ReviewItemViewModel> Deployments { get; } = new();
        public AvaloniaList<ReviewItemViewModel> MyPullRequests { get; } = new();

        [ObservableProperty]
        private bool _hasAccount;

        [ObservableProperty]
        private bool _hasSnapshot;

        [ObservableProperty]
        private int _pendingCount;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private string _scopeHint = string.Empty;

        [ObservableProperty]
        private string _updatedText = string.Empty;

        [ObservableProperty]
        private bool _isRefreshing;

        [ObservableProperty]
        private ReviewDetailViewModel _detail;

        public bool NoAccount => !HasAccount;
        public bool HasError => ErrorMessage.Length > 0;
        public bool HasScopeHint => ScopeHint.Length > 0;
        public bool IsLoading => HasAccount && !HasSnapshot && !HasError;
        public bool HasPullRequests => PullRequests.Count > 0;
        public bool HasDeployments => Deployments.Count > 0;
        public bool HasMine => MyPullRequests.Count > 0;
        public bool IsNothingPending => HasSnapshot && PendingCount == 0;
        public bool HasDetail => Detail != null;
        public bool NoDetail => Detail == null;

        public void EnsureStarted()
        {
            if (_started)
                return;

            _started = true;
            _stampTimer.Tick += (_, _) => UpdateStamp();
            _stampTimer.Start();
            RamaServices.Accounts.Changed += e => Dispatcher.UIThread.Post(() => _ = ReconnectAsync());
            _ = ReconnectAsync();
        }

        partial void OnHasAccountChanged(bool value)
        {
            OnPropertyChanged(nameof(NoAccount));
            OnPropertyChanged(nameof(IsLoading));
        }

        partial void OnHasSnapshotChanged(bool value)
        {
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(IsNothingPending));
        }

        partial void OnPendingCountChanged(int value) => OnPropertyChanged(nameof(IsNothingPending));

        partial void OnErrorMessageChanged(string value)
        {
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(IsLoading));
        }

        partial void OnScopeHintChanged(string value) => OnPropertyChanged(nameof(HasScopeHint));

        partial void OnDetailChanged(ReviewDetailViewModel value)
        {
            OnPropertyChanged(nameof(HasDetail));
            OnPropertyChanged(nameof(NoDetail));
        }

        [RelayCommand]
        private Task RefreshAsync()
        {
            if (_monitor == null)
                return ReconnectAsync();

            return RunRefreshAsync();
        }

        [RelayCommand]
        private void OpenAccounts() => App.GetLauncher()?.Rail.ShowCommand.Execute(RailDestination.Accounts);

        [RelayCommand]
        private void Select(ReviewItemViewModel item)
        {
            if (item == null || item.Key == _selectedKey)
                return;

            SelectKey(item.Key);
        }

        private async Task RunRefreshAsync()
        {
            IsRefreshing = true;
            try
            {
                await _monitor.RefreshAsync();
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        private async Task ReconnectAsync()
        {
            var old = _monitor;
            _monitor = null;
            if (old != null)
            {
                old.Updated -= OnMonitorUpdated;
                await old.StopAsync();
                await old.DisposeAsync();
            }

            try
            {
                var svc = RamaServices.Accounts;
                await svc.LoadAsync();
                var account = svc.Current.Azure.FirstOrDefault();
                var client = account == null ? null : await svc.CreateAzureClientAsync(account);
                if (client == null)
                {
                    ShowNoAccount();
                    return;
                }

                var service = new ReviewsService(client, new PendingWorkService(client));
                _service = service;
                _monitor = new ReviewsMonitor(ct => service.LoadAsync(ct), RamaServices.Notifier);
                _monitor.Updated += OnMonitorUpdated;
                HasAccount = true;
                HasSnapshot = false;
                ErrorMessage = string.Empty;
                _monitor.Start();
            }
            catch (Exception ex)
            {
                HasAccount = true;
                ShowError(ex);
            }
        }

        private void ShowNoAccount()
        {
            _service = null;
            HasAccount = false;
            HasSnapshot = false;
            ErrorMessage = string.Empty;
            ApplySnapshot(null);
        }

        private void OnMonitorUpdated() => Dispatcher.UIThread.Post(() =>
        {
            var monitor = _monitor;
            if (monitor == null)
                return;

            if (monitor.LastError != null)
                ShowError(monitor.LastError);
            else
                ErrorMessage = ScopeHint = string.Empty;

            if (monitor.Snapshot != null)
                ApplySnapshot(monitor.Snapshot);
        });

        private void ShowError(Exception ex)
        {
            ScopeHint = ex is ApiAuthException auth ? auth.MissingScopeHint ?? string.Empty : string.Empty;
            ErrorMessage = ex.Message;
        }

        private void ApplySnapshot(ReviewsSnapshot snap)
        {
            _snapshot = snap;
            var now = DateTimeOffset.UtcNow;
            Fill(PullRequests, snap?.PullRequests, now);
            Fill(Deployments, snap?.Deployments, now);
            Fill(MyPullRequests, snap?.MyPullRequests, now);
            PendingCount = snap?.PendingCount ?? 0;
            HasSnapshot = snap != null;
            OnPropertyChanged(nameof(HasPullRequests));
            OnPropertyChanged(nameof(HasDeployments));
            OnPropertyChanged(nameof(HasMine));
            UpdateStamp();
            ReselectAfterRefresh();
        }

        private void Fill(AvaloniaList<ReviewItemViewModel> target, IEnumerable<ReviewListItem> items, DateTimeOffset now)
        {
            target.Clear();
            if (items != null)
                target.AddRange(items.Select(i => new ReviewItemViewModel(i, now, Select)));
        }

        private IEnumerable<ReviewItemViewModel> AllItems => PullRequests.Concat(Deployments).Concat(MyPullRequests);

        // Conserva la selección entre sondeos (sin recargar el detalle); si desapareció, elige la primera fila.
        private void ReselectAfterRefresh()
        {
            var current = AllItems.FirstOrDefault(i => i.Key == _selectedKey);
            if (current != null)
            {
                current.IsSelected = true;
                return;
            }

            var first = AllItems.FirstOrDefault();
            if (first != null)
                SelectKey(first.Key);
            else
            {
                _selectedKey = null;
                Detail = null;
            }
        }

        private void SelectKey(string key)
        {
            _selectedKey = key;
            ReviewItemViewModel selected = null;
            foreach (var i in AllItems)
            {
                i.IsSelected = i.Key == key;
                if (i.IsSelected)
                    selected = i;
            }

            if (selected == null || _service == null || _snapshot == null)
                return;

            var item = selected.Item;
            var myId = _snapshot.Me.Id;
            Detail = item.Kind == ReviewKind.Deployment
                ? new DeploymentDetailViewModel(item, _service, myId, RunRefreshAsync)
                : new PullRequestDetailViewModel(item, _service, myId, RunRefreshAsync);
        }

        private void UpdateStamp()
        {
            if (_snapshot == null)
            {
                UpdatedText = string.Empty;
                return;
            }

            var now = DateTimeOffset.UtcNow;
            UpdatedText = App.Text("Rama.Reviews.Updated", RelativeTime.Ago(_snapshot.FetchedAt, now, true));
            foreach (var i in AllItems)
                i.Refresh(now);
        }

        private readonly DispatcherTimer _stampTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private ReviewsMonitor _monitor;
        private ReviewsService _service;
        private ReviewsSnapshot _snapshot;
        private string _selectedKey;
        private bool _started;
    }
}
