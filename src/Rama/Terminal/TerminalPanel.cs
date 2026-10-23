using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Rama.Core.Terminal;
using Rama.Terminal;

namespace SourceGit.Rama.Terminal
{
    // Panel de terminal integrada de un repositorio: pestañas claude y shell + botones de contexto.
    // Los botones solo insertan texto; el usuario lo revisa y lo envía con Enter.
    public sealed class TerminalPanel : ObservableObject, IDisposable
    {
        public const string Note = "Cada botón escribe el pedido en la terminal. Tú lo revisas y lo envías con Enter.";
        public const string Disclaimer = "Abre la CLI oficial sin modificarla. La sesión usa tu propia cuenta de Claude Code.";

        public string RepositoryPath { get; }
        public string DirectoryLabel => $"Directorio: {RepositoryPath}";
        public TerminalTab ClaudeTab { get; }
        public TerminalTab ShellTab { get; }
        public IReadOnlyList<TerminalTab> Tabs { get; }

        public TerminalTab SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (value == null || !SetProperty(ref _selectedTab, value))
                    return;
                foreach (var tab in Tabs)
                    tab.IsSelected = tab == value;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public IReadOnlyList<string> SelectedFiles
        {
            get => _selectedFiles;
            set
            {
                if (SetProperty(ref _selectedFiles, value ?? []))
                    OnPropertyChanged(nameof(CanExplain));
            }
        }

        public string TicketKey
        {
            get => _ticketKey;
            set
            {
                if (SetProperty(ref _ticketKey, value))
                {
                    OnPropertyChanged(nameof(CanSummarize));
                    OnPropertyChanged(nameof(SummarizeLabel));
                }
            }
        }

        public string BaseBranch
        {
            get => _baseBranch;
            set
            {
                if (SetProperty(ref _baseBranch, value))
                {
                    OnPropertyChanged(nameof(CanReview));
                    OnPropertyChanged(nameof(ReviewLabel));
                }
            }
        }

        public bool ClaudeAvailable => ClaudeTab.CanStart;
        public bool CanExplain => ClaudeAvailable && _selectedFiles.Count > 0;
        public bool CanCommit => ClaudeAvailable;
        public bool CanReview => ClaudeAvailable && !string.IsNullOrWhiteSpace(_baseBranch);
        public bool CanSummarize => ClaudeAvailable && !string.IsNullOrWhiteSpace(_ticketKey);
        public string ReviewLabel => string.IsNullOrWhiteSpace(_baseBranch) ? "Revisar rama" : $"Revisar rama contra {_baseBranch}";
        public string SummarizeLabel => string.IsNullOrWhiteSpace(_ticketKey) ? "Resumir ticket" : $"Resumir {_ticketKey}";

        public string StatusText
        {
            get
            {
                var pty = OperatingSystem.IsWindows() ? "ConPTY" : "PTY";
                var session = _selectedTab?.Session;
                var size = session is { Columns: > 0 } ? $"{session.Columns}×{session.Rows}" : "-";
                return $"PTY: {pty}   UTF-8   {size}";
            }
        }

        public TerminalPanel(string repositoryPath)
            : this(repositoryPath, ChildEnvironment.FromProcess(), ExecutableLocator.ForCurrentOs())
        {
        }

        public TerminalPanel(string repositoryPath, IReadOnlyDictionary<string, string> environment, ExecutableLocator locator)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
            ArgumentNullException.ThrowIfNull(environment);
            ArgumentNullException.ThrowIfNull(locator);

            RepositoryPath = repositoryPath;
            _environment = ChildEnvironment.Sanitize(new Dictionary<string, string>(environment), locator.IsWindows);

            var repoName = Path.GetFileName(repositoryPath.TrimEnd('/', '\\'));
            var claude = ClaudeLocator.Find(_environment, locator);
            ClaudeTab = claude == null
                ? new TerminalTab(TerminalTabKind.Claude, ClaudeLocator.CommandName, repoName, null, ClaudeLocator.NotFoundMessage)
                : new TerminalTab(TerminalTabKind.Claude, ClaudeLocator.CommandName, repoName, LaunchCommand.For(claude, locator.IsWindows), null);

            var shell = ShellResolver.Resolve(_environment, locator, locator.IsWindows);
            ShellTab = new TerminalTab(TerminalTabKind.Shell, Path.GetFileNameWithoutExtension(shell), string.Empty, LaunchCommand.For(shell, locator.IsWindows), null);

            Tabs = [ClaudeTab, ShellTab];
            SelectedTab = ClaudeTab;
        }

        // La vista entrega la sesión cuando el control está en pantalla; aquí se lanza el proceso.
        public async Task AttachAsync(TerminalTab tab, ITerminalSession session)
        {
            ArgumentNullException.ThrowIfNull(tab);
            ArgumentNullException.ThrowIfNull(session);
            if (_disposed || !tab.CanStart || tab.Session != null)
                return;

            tab.Session = session;
            await session.StartAsync(RepositoryPath, tab.Command, _environment);
            tab.IsRunning = session.IsRunning;
            OnPropertyChanged(nameof(StatusText));
        }

        public void RefreshStatus() => OnPropertyChanged(nameof(StatusText));

        public Task ExplainChangesAsync() => InsertPromptAsync(PromptBuilder.ExplainChanges(_selectedFiles));
        public Task CommitMessageAsync() => InsertPromptAsync(PromptBuilder.CommitMessage(_ticketKey));
        public Task ReviewBranchAsync() => InsertPromptAsync(PromptBuilder.ReviewBranch(_baseBranch));
        public Task SummarizeTicketAsync() => InsertPromptAsync(PromptBuilder.SummarizeTicket(_ticketKey));

        // Escribe en la pestaña claude sin Enter.
        public Task InsertPromptAsync(string prompt)
        {
            SelectedTab = ClaudeTab;
            var session = ClaudeTab.Session;
            return session == null ? Task.CompletedTask : session.WriteInputAsync(prompt, submit: false);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (var tab in Tabs)
            {
                tab.Session?.Kill();
                tab.Session = null;
                tab.IsRunning = false;
            }
        }

        private readonly IReadOnlyDictionary<string, string> _environment;
        private TerminalTab _selectedTab;
        private IReadOnlyList<string> _selectedFiles = [];
        private string _ticketKey;
        private string _baseBranch;
        private bool _disposed;
    }
}
