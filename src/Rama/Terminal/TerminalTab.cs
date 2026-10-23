using CommunityToolkit.Mvvm.ComponentModel;
using Rama.Core.Terminal;
using Rama.Terminal;

namespace SourceGit.Rama.Terminal
{
    public enum TerminalTabKind
    {
        Claude,
        Shell,
    }

    // Pestaña del panel: qué lanzar y, si no se puede, por qué.
    public sealed class TerminalTab : ObservableObject
    {
        public TerminalTabKind Kind { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public LaunchCommand Command { get; }
        public string Error { get; }
        public bool HasError => !string.IsNullOrEmpty(Error);
        public bool CanStart => Command != null;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            internal set => SetProperty(ref _isRunning, value);
        }

        internal ITerminalSession Session { get; set; }

        public TerminalTab(TerminalTabKind kind, string title, string subtitle, LaunchCommand command, string error)
        {
            Kind = kind;
            Title = title;
            Subtitle = subtitle;
            Command = command;
            Error = error;
        }

        private bool _isSelected;
        private bool _isRunning;
    }
}
