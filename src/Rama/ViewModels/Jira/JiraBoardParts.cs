using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Rama.Core.Jira;

namespace SourceGit.ViewModels
{
    public partial class JiraPersonViewModel : ObservableObject
    {
        // Pares (fondo, tinta) palidos de la maqueta; el indice viene de BoardProjection.PaletteOf.
        private static readonly (string Bg, string Fg)[] Palette =
        [
            ("#3a2f1a", "#f0c27a"), ("#1c3326", "#8fdcae"), ("#36202a", "#ee9ab1"),
            ("#1b2c3b", "#8ec5ef"), ("#22293a", "#a9bff0"),
        ];

        public JiraPersonViewModel(PersonChip chip, bool isSelected, System.Action<string> select)
        {
            Id = chip.Id;
            Name = chip.Name;
            Initials = chip.Initials;
            Count = chip.Count;
            IsMe = chip.IsMe;
            IsSelected = isSelected;
            var (bg, fg) = Palette[chip.PaletteIndex % Palette.Length];
            AvatarBackground = new SolidColorBrush(Color.Parse(bg));
            AvatarForeground = new SolidColorBrush(Color.Parse(fg));
            _select = select;
        }

        public string Id { get; }
        public string Name { get; }
        public string Initials { get; }
        public int Count { get; }
        public bool IsMe { get; }
        public bool IsSelected { get; }
        public IBrush AvatarBackground { get; }
        public IBrush AvatarForeground { get; }

        [RelayCommand]
        private void Select() => _select(Id);

        private readonly System.Action<string> _select;
    }

    public partial class JiraCardViewModel : ObservableObject
    {
        public JiraCardViewModel(CardInfo info, JiraBoardViewModel owner, bool canCreateBranch, bool confirming, string error)
        {
            Info = info;
            _owner = owner;
            CanCreateBranch = canCreateBranch;
            _isConfirming = confirming;
            _error = error;
        }

        public CardInfo Info { get; }
        public string Key => Info.Key;
        public string Summary => Info.Summary;
        public string TypeLabel => Info.TypeLabel;
        public string Priority => Info.Priority ?? string.Empty;
        public bool IsHighPriority => Info.IsHighPriority;
        public string PointsLabel => Info.PointsLabel ?? string.Empty;
        public bool IsStory => Info.Kind == IssueKind.Story;
        public bool IsBug => Info.Kind == IssueKind.Bug;
        public bool IsTask => Info.Kind == IssueKind.Task;
        public string Branch => Info.Branch ?? string.Empty;
        public bool HasBranch => Info.Branch != null;
        public bool NoBranch => Info.Branch == null;
        public string NewBranchName => BranchTicketParser.BranchNameFor(Info.Issue);
        public bool CanCreateBranch { get; }
        public string CreateTooltip => CanCreateBranch ? NewBranchName : App.Text("Rama.Jira.NoRepo");
        public bool HasError => !string.IsNullOrEmpty(_error);

        public bool ShowCreateButton => NoBranch && !_isConfirming;
        public bool ShowConfirm => NoBranch && _isConfirming;

        public bool IsConfirming
        {
            get => _isConfirming;
            private set
            {
                if (SetProperty(ref _isConfirming, value))
                {
                    OnPropertyChanged(nameof(ShowCreateButton));
                    OnPropertyChanged(nameof(ShowConfirm));
                }
            }
        }

        public string Error
        {
            get => _error;
            set
            {
                if (SetProperty(ref _error, value))
                    OnPropertyChanged(nameof(HasError));
            }
        }

        [ObservableProperty]
        private bool _isDragSource;

        [RelayCommand(CanExecute = nameof(CanCreateBranch))]
        private void AskCreateBranch()
        {
            IsConfirming = true;
            _owner.SetConfirming(Key, true);
        }

        [RelayCommand]
        private void CancelCreateBranch()
        {
            IsConfirming = false;
            _owner.SetConfirming(Key, false);
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task ConfirmCreateBranchAsync()
        {
            IsConfirming = false;
            _owner.SetConfirming(Key, false);
            await _owner.CreateBranchAsync(this);
        }

        private readonly JiraBoardViewModel _owner;
        private bool _isConfirming;
        private string _error;
    }

    public enum JiraDropState
    {
        None,
        Valid,
        Invalid,
    }

    public partial class JiraColumnViewModel : ObservableObject
    {
        public JiraColumnViewModel(ColumnInfo info, IEnumerable<JiraCardViewModel> cards)
        {
            Index = info.Index;
            Column = info.Column;
            Cards = new ObservableCollection<JiraCardViewModel>(cards);
        }

        public int Index { get; }
        public JiraColumn Column { get; }
        public string Name => Column.Name;
        public int Count => Cards.Count;
        public ObservableCollection<JiraCardViewModel> Cards { get; }
        public bool IsEmpty => Cards.Count == 0 && DropState != JiraDropState.Valid;
        public bool IsDropTarget => DropState == JiraDropState.Valid;
        public bool IsDimmed => DropState == JiraDropState.Invalid;
        public bool HasNote => !string.IsNullOrEmpty(Note);
        public string DropHint => App.Text("Rama.Jira.DropHint", Name);

        public JiraDropState DropState
        {
            get => _dropState;
            private set
            {
                if (SetProperty(ref _dropState, value))
                {
                    OnPropertyChanged(nameof(IsDropTarget));
                    OnPropertyChanged(nameof(IsDimmed));
                    OnPropertyChanged(nameof(IsEmpty));
                }
            }
        }

        public string Note
        {
            get => _note;
            private set
            {
                if (SetProperty(ref _note, value))
                    OnPropertyChanged(nameof(HasNote));
            }
        }

        // La columna actual de la tarjeta no se resalta ni se atenua.
        public void ApplyDrop(ColumnDrop drop)
        {
            Note = drop.Kind == DropKind.Invalid ? drop.Reason ?? string.Empty : string.Empty;
            DropState = drop.Kind switch
            {
                DropKind.Valid or DropKind.RequiresFields => JiraDropState.Valid,
                DropKind.Invalid => JiraDropState.Invalid,
                _ => JiraDropState.None,
            };
        }

        public void ClearDrop()
        {
            Note = string.Empty;
            DropState = JiraDropState.None;
        }

        private JiraDropState _dropState;
        private string _note = string.Empty;
    }

    public partial class JiraFieldInputViewModel : ObservableObject
    {
        public JiraFieldInputViewModel(TransitionField field)
        {
            Field = field;
            Kind = FieldValueBuilder.KindOf(field);
            Options = field.AllowedValues.ToList();
        }

        public TransitionField Field { get; }
        public FieldInputKind Kind { get; }
        public string Name => Field.Name;
        public IReadOnlyList<FieldOption> Options { get; }
        public bool IsSelect => Kind == FieldInputKind.Select;
        public bool IsText => Kind == FieldInputKind.Text;
        public bool IsUnsupported => Kind == FieldInputKind.Unsupported;

        [ObservableProperty]
        private string _text = string.Empty;

        [ObservableProperty]
        private FieldOption _selected;

        public string Value => Kind == FieldInputKind.Select ? Selected?.Id : Text;
    }

    // Dialogo (inline) de campos requeridos de una transicion.
    public partial class JiraFieldsDialogViewModel : ObservableObject
    {
        public JiraFieldsDialogViewModel(string issueKey, JiraColumnViewModel target,
            IEnumerable<TransitionField> missing, string issueUrl)
        {
            IssueKey = issueKey;
            Target = target;
            IssueUrl = issueUrl;
            Fields = missing.Select(f => new JiraFieldInputViewModel(f)).ToList();
        }

        public string IssueKey { get; }
        public JiraColumnViewModel Target { get; }
        public string IssueUrl { get; }
        public IReadOnlyList<JiraFieldInputViewModel> Fields { get; }
        public bool HasUnsupported => Fields.Any(f => f.IsUnsupported);
        public bool CanConfirm => !HasUnsupported;
        public string Title => App.Text("Rama.Jira.FieldsTitle", IssueKey, Target.Name);

        [ObservableProperty]
        private string _error = string.Empty;
    }
}
