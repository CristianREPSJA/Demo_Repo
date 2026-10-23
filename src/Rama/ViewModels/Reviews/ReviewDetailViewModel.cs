using System;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Rama.Core.Http;
using Rama.Core.Reviews;

namespace SourceGit.ViewModels
{
    // Base del panel de detalle: error propio (con pista de permiso 401/403) y confirmación inline.
    public abstract partial class ReviewDetailViewModel : ObservableObject
    {
        protected ReviewsService Service { get; }
        protected string MyId { get; }
        protected Func<Task> RefreshList { get; }
        public ReviewListItem Item { get; }

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private string _scopeHint = string.Empty;

        [ObservableProperty]
        private string _confirmPrompt = string.Empty;

        public string Title => Item.Title;
        public string Number => Item.Number;
        public bool HasError => ErrorMessage.Length > 0;
        public bool HasScopeHint => ScopeHint.Length > 0;
        public bool HasConfirm => ConfirmPrompt.Length > 0;
        public bool IsIdle => !IsBusy;

        protected ReviewDetailViewModel(ReviewListItem item, ReviewsService service, string myId, Func<Task> refreshList)
        {
            Item = item;
            Service = service;
            MyId = myId;
            RefreshList = refreshList;
        }

        partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));
        partial void OnScopeHintChanged(string value) => OnPropertyChanged(nameof(HasScopeHint));
        partial void OnConfirmPromptChanged(string value) => OnPropertyChanged(nameof(HasConfirm));
        partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsIdle));

        protected void AskConfirm(string prompt, Func<CancellationToken, Task> action)
        {
            _confirm.Request(prompt, action);
            ConfirmPrompt = prompt;
        }

        [RelayCommand]
        private void CancelConfirm()
        {
            _confirm.Cancel();
            ConfirmPrompt = string.Empty;
        }

        [RelayCommand]
        private async Task ConfirmAsync()
        {
            ConfirmPrompt = string.Empty;
            await RunAsync(() => _confirm.ConfirmAsync());
        }

        protected async Task RunAsync(Func<Task> work)
        {
            if (IsBusy)
                return;

            IsBusy = true;
            ClearError();
            try
            {
                await work();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SetError(ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        protected void ClearError()
        {
            ErrorMessage = string.Empty;
            ScopeHint = string.Empty;
        }

        protected void SetError(Exception ex)
        {
            ScopeHint = ex is ApiAuthException auth ? auth.MissingScopeHint ?? string.Empty : string.Empty;
            ErrorMessage = ex.Message;
        }

        private readonly ConfirmFlow _confirm = new();
    }
}
