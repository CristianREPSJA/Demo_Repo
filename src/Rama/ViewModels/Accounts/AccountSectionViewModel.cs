using System;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Rama.Core.Accounts;
using Rama.Core.Integrations;

namespace SourceGit.ViewModels
{
    // Base de cada formulario de cuenta: el secreto escrito nunca se relee ni se muestra.
    public abstract partial class AccountSectionViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _secret = string.Empty;

        [ObservableProperty]
        private bool _hasSavedSecret;

        [ObservableProperty]
        private string _status = string.Empty;

        [ObservableProperty]
        private bool _statusIsError;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private DateTime? _expiry;

        public bool HasStatus => !string.IsNullOrEmpty(Status);

        public string ExpiryWarning
        {
            get
            {
                if (Expiry is not { } d)
                    return string.Empty;

                var st = TokenExpiryTracker.Evaluate(DateOnly.FromDateTime(d), DateOnly.FromDateTime(DateTime.Now));
                return st.State switch
                {
                    TokenExpiryState.Expired => App.Text("Rama.Accounts.Expired"),
                    TokenExpiryState.ExpiringSoon => App.Text("Rama.Accounts.ExpiresIn", st.DaysLeft ?? 0),
                    _ => string.Empty,
                };
            }
        }

        public bool HasExpiryWarning => ExpiryWarning.Length > 0;

        partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));

        partial void OnExpiryChanged(DateTime? value)
        {
            OnPropertyChanged(nameof(ExpiryWarning));
            OnPropertyChanged(nameof(HasExpiryWarning));
        }

        public abstract void LoadFrom(AccountsData data);
        protected abstract Task<ConnectionTestResult> TestCoreAsync();
        protected abstract Task SaveCoreAsync();
        protected abstract Task RemoveCoreAsync();

        public void SetStatus(string message, bool isError)
        {
            StatusIsError = isError;
            Status = message;
        }

        [RelayCommand]
        private Task TestAsync() => RunAsync(async () =>
        {
            var r = await TestCoreAsync();
            SetStatus(r.Message, !r.Success);
        });

        [RelayCommand]
        private Task SaveAsync() => RunAsync(async () =>
        {
            await SaveCoreAsync();
            Secret = string.Empty;
            SetStatus(App.Text("Rama.Accounts.SavedOk"), false);
        });

        [RelayCommand]
        private Task RemoveAsync() => RunAsync(async () =>
        {
            await RemoveCoreAsync();
            Secret = string.Empty;
            SetStatus(App.Text("Rama.Accounts.RemovedOk"), false);
        });

        protected async Task RunAsync(Func<Task> action)
        {
            if (IsBusy)
                return;

            IsBusy = true;
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        protected static DateOnly? ToDateOnly(DateTime? d) => d is { } v ? DateOnly.FromDateTime(v) : null;
    }
}
