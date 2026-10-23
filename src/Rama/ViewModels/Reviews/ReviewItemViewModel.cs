using System;

using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Rama.Core.Reviews;

using SourceGit.Rama;

namespace SourceGit.ViewModels
{
    // Fila de la lista izquierda (PR, despliegue o "mis PRs").
    public partial class ReviewItemViewModel : ObservableObject
    {
        public ReviewListItem Item { get; }
        public string Key => Item.Key;
        public string Title => Item.Title;
        public string Number => Item.Number;
        public string Author => Item.Author;
        public string Initials => Item.Initials;
        public string Meta => Item.Meta;
        public bool IsDeployment => Item.Kind == ReviewKind.Deployment;
        public bool IsMine => Item.Kind == ReviewKind.MyPullRequest;
        public bool HasAuthor => Item.Author.Length > 0;
        public bool HasNumber => Item.Number.Length > 0;
        public bool HasPill => !string.IsNullOrEmpty(Item.Pill);
        public string Pill => Item.Pill ?? string.Empty;
        public IBrush PillBackground => ToneBrushes.Background(Item.PillTone);
        public IBrush PillInk => ToneBrushes.Ink(Item.PillTone);
        public IBrush AvatarBackground { get; }
        public IBrush AvatarInk { get; }

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private string _when = string.Empty;

        public ReviewItemViewModel(ReviewListItem item, DateTimeOffset now, Action<ReviewItemViewModel> onSelect)
        {
            Item = item;
            _onSelect = onSelect;
            (AvatarBackground, AvatarInk) = ToneBrushes.ForIdentity(item.Author);
            Refresh(now);
        }

        [RelayCommand]
        private void Select() => _onSelect(this);

        public void Refresh(DateTimeOffset now)
        {
            When = Item.CreatedOn is { } c ? RelativeTime.Ago(c, now) : string.Empty;
        }

        private readonly Action<ReviewItemViewModel> _onSelect;
    }

    public sealed class PolicyRowViewModel(PolicyRow row)
    {
        public string Name => row.Name;
        public string StatusText => row.StatusText;
        public IBrush Ink => ToneBrushes.Ink(row.Tone);
    }

    public sealed class ReviewerRowViewModel(ReviewerRow row)
    {
        public string Name => row.Name;
        public string Initials => row.Initials;
        public bool IsMe => row.IsMe;
        public string VoteLabel => row.VoteLabel;
        public IBrush PillBackground => ToneBrushes.Background(row.Tone);
        public IBrush PillInk => ToneBrushes.Ink(row.Tone);
        public IBrush AvatarBackground { get; } = ToneBrushes.ForIdentity(row.Name).Background;
        public IBrush AvatarInk { get; } = ToneBrushes.ForIdentity(row.Name).Ink;
    }
}
