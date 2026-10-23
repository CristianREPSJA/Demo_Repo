using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Rama.Core.Accounts;
using Rama.Core.Http;
using Rama.Core.Integrations;
using Rama.Core.Jira;
using Rama.Core.Polling;

namespace SourceGit.ViewModels
{
    public enum JiraBoardState
    {
        Loading,
        NoAccount,
        Error,
        Ready,
    }

    // Pagina "Tablero Jira": estado, filtro, persona seleccionada, arrastre de tarjetas y sondeo cada 60 s.
    // La logica de datos vive en Rama.Core (BoardController, BoardProjection, DropEvaluator).
    public partial class JiraBoardViewModel : ObservableObject
    {
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

        public ObservableCollection<JiraPersonViewModel> People { get; } = [];
        public ObservableCollection<JiraColumnViewModel> Columns { get; } = [];

        public JiraBoardState State
        {
            get => _state;
            private set
            {
                if (SetProperty(ref _state, value))
                {
                    OnPropertyChanged(nameof(IsLoading));
                    OnPropertyChanged(nameof(IsNoAccount));
                    OnPropertyChanged(nameof(IsError));
                    OnPropertyChanged(nameof(IsReady));
                }
            }
        }

        public bool IsLoading => _state == JiraBoardState.Loading;
        public bool IsNoAccount => _state == JiraBoardState.NoAccount;
        public bool IsError => _state == JiraBoardState.Error;
        public bool IsReady => _state == JiraBoardState.Ready;
        public bool HasTokenBanner => !string.IsNullOrEmpty(TokenBanner);
        public bool HasMoveError => !string.IsNullOrEmpty(MoveError);
        public bool HasFieldsDialog => FieldsDialog != null;

        [ObservableProperty]
        private string _title = App.Text("Rama.Jira.TitleEmpty");

        [ObservableProperty]
        private string _syncText = string.Empty;

        [ObservableProperty]
        private string _errorText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasTokenBanner))]
        private string _tokenBanner = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasMoveError))]
        private string _moveError = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasFieldsDialog))]
        private JiraFieldsDialogViewModel _fieldsDialog;

        public string Query
        {
            get => _query;
            set
            {
                if (SetProperty(ref _query, value ?? string.Empty))
                    Rebuild();
            }
        }

        public void SetVisible(bool visible)
        {
            if (visible == _visible)
                return;

            _visible = visible;
            // Serializado: activar/desactivar nunca se solapan y solo importa el último estado pedido.
            _ = _activation.RunAsync(visible ? ActivateAsync : DeactivateSafeAsync);
        }

        private async Task DeactivateSafeAsync()
        {
            try
            {
                await DeactivateAsync();
            }
            catch (Exception ex)
            {
                Native.OS.LogException(ex);
            }
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            if (_poller is { IsPaused: true })
            {
                State = JiraBoardState.Loading;
                _poller.Resume();
                return;
            }

            if (_controller != null)
                await TickAsync(CancellationToken.None, force: true);
        }

        [RelayCommand]
        private void OpenAccounts() => App.GetLauncher()?.Rail.ShowCommand.Execute(RailDestination.Accounts);

        [RelayCommand]
        private void DismissMoveError() => MoveError = string.Empty;

        [RelayCommand]
        private void DismissTokenBanner() => TokenBanner = string.Empty;

        internal void SetConfirming(string key, bool value)
        {
            if (value)
                _confirming.Add(key);
            else
                _confirming.Remove(key);
        }

        private void SelectPerson(string id)
        {
            _selectedId = id;
            Rebuild();
        }

        private async Task ActivateAsync()
        {
            _ageTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateSyncText());
            _ageTimer.Start();

            try
            {
                var svc = SourceGit.Rama.RamaServices.Accounts;
                await svc.LoadAsync();
                var account = svc.Current.Jira.FirstOrDefault();
                if (account == null || !await svc.HasSecretAsync(AccountPlatform.Jira, account.Email))
                {
                    await StopPollerAsync();
                    _controller = null;
                    _account = null;
                    State = JiraBoardState.NoAccount;
                    return;
                }

                if (_controller == null || !account.Equals(_account))
                {
                    await StopPollerAsync();
                    var client = await svc.CreateJiraClientAsync(account);
                    _account = account;
                    _controller = new BoardController(client, account);
                    _controller.BoardChanged += _ => Dispatcher.UIThread.Post(Rebuild);
                    Title = App.Text("Rama.Jira.Title", account.ProjectKey);
                    State = JiraBoardState.Loading;
                }

                await LoadTokenBannerAsync(svc, account);
                if (!_visible)
                    return;

                Rebuild();
                _poller ??= new Poller(c => TickAsync(c), new PollerOptions(PollInterval, MaxBackoff));
                _poller.Start();
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private async Task DeactivateAsync()
        {
            _ageTimer?.Stop();
            await StopPollerAsync();
        }

        private async Task StopPollerAsync()
        {
            if (_poller == null)
                return;

            var p = _poller;
            _poller = null;
            await p.StopAsync();
        }

        private async Task LoadTokenBannerAsync(AccountsService svc, JiraAccount account)
        {
            var status = await svc.GetExpiryStatusAsync(AccountPlatform.Jira, account.Email);
            TokenBanner = status.State switch
            {
                TokenExpiryState.Expired => App.Text("Rama.Jira.TokenExpired"),
                TokenExpiryState.ExpiringSoon => App.Text("Rama.Jira.TokenExpiring", status.DaysLeft ?? 0),
                _ => string.Empty,
            };
        }

        // Ciclo del Poller (y del boton Actualizar): lanza ante fallos para que el Poller aplique backoff.
        // El error se informa solo aqui (UI); el Poller unicamente lo usa para el backoff y no lo muestra.
        private async Task TickAsync(CancellationToken ct, bool force = false)
        {
            var controller = _controller;
            if (controller == null || (!force && (_isDragging || controller.IsMovePending)))
                return;

            try
            {
                await controller.SyncAsync(ct);
                Dispatcher.UIThread.Post(() =>
                {
                    ErrorText = string.Empty;
                    State = JiraBoardState.Ready;
                    Rebuild();
                    UpdateSyncText();
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => ShowError(ex));
                if (force)
                    return;
                throw;
            }
        }

        private void ShowError(Exception ex)
        {
            ErrorText = BoardController.DescribeError(ex);
            if (_controller?.Board == null)
                State = JiraBoardState.Error;
            else
                MoveError = ErrorText;
        }

        private void UpdateSyncText()
        {
            var last = _controller?.LastSync;
            SyncText = last is { } t ? App.Text("Rama.Jira.Synced", BoardText.SyncAge(DateTimeOffset.UtcNow - t)) : string.Empty;
        }

        private static Repository ActiveRepository() => App.GetLauncher()?.ActivePage?.Data as Repository;

        private void Rebuild()
        {
            var board = _controller?.Board;
            if (board == null)
                return;

            var repo = ActiveRepository();
            var names = repo == null
                ? new List<string>()
                : repo.Branches.OrderBy(b => !b.IsLocal).Select(b => b.FriendlyName).ToList();
            var canCreate = repo != null;

            var view = BoardProjection.Build(board, _controller.Users, _controller.MyAccountId, _selectedId, _query,
                key => _createdBranches.GetValueOrDefault(key) ?? BoardText.FindBranch(key, names));
            _selectedId = view.SelectedId;

            People.Clear();
            foreach (var chip in view.People)
                People.Add(new JiraPersonViewModel(chip, chip.Id == view.SelectedId, SelectPerson));

            Columns.Clear();
            foreach (var col in view.Columns)
            {
                var cards = col.Cards.Select(c => new JiraCardViewModel(c, this, canCreate,
                    _confirming.Contains(c.Key), _cardErrors.GetValueOrDefault(c.Key) ?? string.Empty));
                Columns.Add(new JiraColumnViewModel(col, cards));
            }
        }

        // ---- Arrastrar y soltar ----

        public async Task BeginDragAsync(JiraCardViewModel card)
        {
            _isDragging = true;
            _dragKey = card.Key;
            card.IsDragSource = true;
            MoveError = string.Empty;
            _cardErrors.Remove(card.Key);
            card.Error = string.Empty;

            try
            {
                var drops = await _controller.EvaluateDropsAsync(card.Key);
                if (!_isDragging || _dragKey != card.Key)
                    return;

                foreach (var col in Columns)
                    if (col.Index < drops.Count)
                        col.ApplyDrop(drops[col.Index]);
            }
            catch (ApiException)
            {
                // Sin transiciones conocidas no se resalta nada; Jira decide al soltar.
            }
            catch (Exception ex)
            {
                Native.OS.LogException(ex);
            }
        }

        public void EndDrag(JiraCardViewModel card)
        {
            _isDragging = false;
            _dragKey = null;
            card.IsDragSource = false;
            foreach (var col in Columns)
                col.ClearDrop();
        }

        public async Task DropAsync(string issueKey, JiraColumnViewModel target)
        {
            if (_controller == null)
                return;

            if (target.DropState == JiraDropState.Invalid)
            {
                SetMoveFailure(issueKey, target.Note);
                return;
            }

            await MoveAsync(issueKey, target, null);
        }

        private async Task MoveAsync(string issueKey, JiraColumnViewModel target,
            IReadOnlyDictionary<string, System.Text.Json.JsonElement> fields)
        {
            var result = await _controller.MoveAsync(issueKey, target.Column, fields);
            switch (result.Outcome)
            {
                case MoveOutcome.Committed:
                case MoveOutcome.NoOp:
                    MoveError = string.Empty;
                    break;
                case MoveOutcome.RequiresFields:
                    FieldsDialog = new JiraFieldsDialogViewModel(issueKey, target, result.MissingFields,
                        _controller.IssueUrl(issueKey));
                    break;
                default:
                    SetMoveFailure(issueKey, result.Message);
                    break;
            }
        }

        private void SetMoveFailure(string key, string reason)
        {
            _cardErrors[key] = reason ?? string.Empty;
            MoveError = App.Text("Rama.Jira.MoveError", key, reason);
            Rebuild();
        }

        [RelayCommand]
        private async Task ConfirmFieldsAsync()
        {
            var dlg = FieldsDialog;
            if (dlg == null)
                return;

            var fields = FieldValueBuilder.Build(dlg.Fields.Select(f => (f.Field, f.Value)));
            if (fields == null)
            {
                dlg.Error = App.Text("Rama.Jira.FieldsMissing");
                return;
            }

            FieldsDialog = null;
            await MoveAsync(dlg.IssueKey, dlg.Target, fields);
        }

        [RelayCommand]
        private void CancelFields() => FieldsDialog = null;

        [RelayCommand]
        private void OpenInJira()
        {
            if (FieldsDialog != null)
                Native.OS.OpenBrowser(FieldsDialog.IssueUrl);
        }

        // ---- Crear rama ----

        public async Task CreateBranchAsync(JiraCardViewModel card)
        {
            var repo = ActiveRepository();
            if (repo == null)
                return;

            var name = card.NewBranchName;
            var ok = Models.RefName.IsValidBranchName(name) &&
                await new Commands.Branch(repo.FullPath, name).CreateAsync("HEAD", false);
            if (ok)
            {
                _createdBranches[card.Key] = name;
                _cardErrors.Remove(card.Key);
                repo.RefreshBranches();
            }
            else
            {
                _cardErrors[card.Key] = App.Text("Rama.Jira.BranchError", name);
            }

            Rebuild();
        }

        private JiraBoardState _state = JiraBoardState.Loading;
        private string _query = string.Empty;
        private string _selectedId;
        private bool _visible;
        private bool _isDragging;
        private string _dragKey;
        private JiraAccount _account;
        private BoardController _controller;
        private readonly LatestWinsGate _activation = new();
        private Poller _poller;
        private DispatcherTimer _ageTimer;
        private readonly HashSet<string> _confirming = [];
        private readonly Dictionary<string, string> _cardErrors = [];
        private readonly Dictionary<string, string> _createdBranches = [];
    }
}
