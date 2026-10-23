using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        private const int StartTimeoutMs = 15000;
        private const int PollMs = 100;
        private const int ClaudeBootDelayMs = 1500;
        private static readonly string[] ContextDependent =
        [
            nameof(HasTicket), nameof(CanExplain), nameof(CanReview), nameof(CanSummarize), nameof(ReviewLabel), nameof(SummarizeLabel),
        ];

        // Lo asigna el alojador para recalcular el contexto bajo demanda.
        public Action<TerminalPanel> ContextRefresher { get; set; }

        // Vista única: solo un alojador a la vez muestra este panel.
        public ViewLease Lease { get; } = new();

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

        // Contexto del repositorio (archivos, rama, ticket); lo actualiza quien aloja el panel.
        public TerminalContext Context
        {
            get => _context;
            set
            {
                value ??= TerminalContext.Empty;
                if (!SetProperty(ref _context, value))
                    return;
                foreach (var name in ContextDependent)
                    OnPropertyChanged(name);
            }
        }

        public bool ClaudeAvailable => ClaudeTab.CanStart;
        public bool HasTicket => !string.IsNullOrWhiteSpace(_context.TicketKey);
        public bool CanExplain => ClaudeAvailable && ContextPrompts.IsAvailable(ContextAction.ExplainChanges, _context);
        public bool CanCommit => ClaudeAvailable;
        public bool CanReview => ClaudeAvailable && ContextPrompts.IsAvailable(ContextAction.ReviewBranch, _context);
        public bool CanSummarize => ClaudeAvailable && HasTicket;
        public string ReviewLabel => string.IsNullOrWhiteSpace(_context.BaseBranch) ? "Revisar rama" : $"Revisar rama contra {_context.BaseBranch}";
        public string SummarizeLabel => HasTicket ? $"Resumir {_context.TicketKey}" : "Resumir ticket";

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

        public Task ExplainChangesAsync() => RunAsync(ContextAction.ExplainChanges);
        public Task CommitMessageAsync() => RunAsync(ContextAction.CommitMessage);
        public Task ReviewBranchAsync() => RunAsync(ContextAction.ReviewBranch);
        public Task SummarizeTicketAsync() => RunAsync(ContextAction.SummarizeTicket);

        // Refresca el contexto justo antes de armar el pedido (la selección pudo cambiar).
        private Task RunAsync(ContextAction action)
        {
            ContextRefresher?.Invoke(this);
            var prompt = ContextPrompts.Build(action, _context);
            return prompt == null ? Task.CompletedTask : InsertPromptAsync(prompt);
        }

        // Escribe en la pestaña claude sin Enter; la inicia si aún no corre. Nunca envía.
        public async Task InsertPromptAsync(string prompt)
        {
            if (_disposed || !ClaudeTab.CanStart)
                return;
            SelectedTab = ClaudeTab;

            var wasRunning = ClaudeTab.Session is { IsRunning: true };
            var sw = Stopwatch.StartNew();
            while (ClaudeTab.Session is not { IsRunning: true } && !_disposed && sw.ElapsedMilliseconds < StartTimeoutMs)
                await Task.Delay(PollMs);
            if (_disposed || ClaudeTab.Session is not { IsRunning: true } session)
                return;

            if (!wasRunning)
                await Task.Delay(ClaudeBootDelayMs); // la CLI necesita pintar su caja de entrada
            await session.WriteInputAsync(prompt, submit: false);
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
        private TerminalContext _context = TerminalContext.Empty;
        private bool _disposed;
    }
}
