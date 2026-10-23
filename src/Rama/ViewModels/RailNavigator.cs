using System;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SourceGit.ViewModels
{
    public enum RailDestination
    {
        Repository,
        PendingReviews,
        JiraBoard,
        Terminal,
        Accounts,
        Clone,
    }

    // Navegacion del riel: destinos con pagina propia y acciones (Clonar, Ajustes) que abren dialogos.
    public partial class RailNavigator : ObservableObject
    {
        public RailDestination Current
        {
            get => _current;
            private set
            {
                if (SetProperty(ref _current, value))
                {
                    OnPropertyChanged(nameof(IsRepository));
                    OnPropertyChanged(nameof(IsPendingReviews));
                    OnPropertyChanged(nameof(IsJiraBoard));
                    OnPropertyChanged(nameof(IsTerminal));
                    OnPropertyChanged(nameof(IsAccounts));
                    OnPropertyChanged(nameof(IsClone));
                }
            }
        }

        public RailNavigator()
        {
            // El sondeo de revisiones corre con la app abierta; el riel solo refleja el conteo.
            var reviews = SourceGit.Rama.RamaServices.Reviews;
            _pendingReviewsCount = reviews.PendingCount;
            reviews.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PendingReviewsViewModel.PendingCount))
                    PendingReviewsCount = reviews.PendingCount;
            };
            reviews.EnsureStarted();
        }

        public bool IsRepository => _current == RailDestination.Repository;
        public bool IsPendingReviews => _current == RailDestination.PendingReviews;
        public bool IsJiraBoard => _current == RailDestination.JiraBoard;
        public bool IsTerminal => _current == RailDestination.Terminal;
        public bool IsAccounts => _current == RailDestination.Accounts;
        public bool IsClone => _current == RailDestination.Clone;

        public int PendingReviewsCount
        {
            get => _pendingReviewsCount;
            set
            {
                if (SetProperty(ref _pendingReviewsCount, Math.Max(0, value)))
                    OnPropertyChanged(nameof(HasPendingReviews));
            }
        }

        public bool HasPendingReviews => _pendingReviewsCount > 0;

        [RelayCommand]
        private void Show(RailDestination destination)
        {
            Current = destination;
        }

        [RelayCommand]
        private void Clone()
        {
            Current = RailDestination.Clone;
        }

        [RelayCommand]
        private void OpenSettings()
        {
            App.OpenPreferencesCommand.Execute(null);
        }

        private RailDestination _current = RailDestination.Repository;
        private int _pendingReviewsCount;
    }
}
