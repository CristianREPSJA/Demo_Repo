using System;
using System.Threading.Tasks;
using Rama.Core.Terminal;
using SourceGit.ViewModels;

namespace SourceGit.Rama.Terminal
{
    // Panel + vista + fuente de contexto de un repositorio. La vista se crea una vez y se muda entre alojadores.
    public sealed class TerminalEntry : IDisposable
    {
        public TerminalPanel Panel { get; }
        public TerminalPanelView View => _view ??= new TerminalPanelView { DataContext = Panel };

        public TerminalEntry(string path)
        {
            Panel = new TerminalPanel(path);
        }

        public void Bind(Repository repo)
        {
            if (_source != null)
                return;
            _source = new RepositoryContextSource(repo, Panel);
            Panel.ContextRefresher = _ => _source?.Refresh();
        }

        public void Dispose()
        {
            _source?.Dispose();
            _source = null;
            Panel.ContextRefresher = null;
            Panel.Dispose();
            _view = null;
        }

        private TerminalPanelView _view;
        private RepositoryContextSource _source;
    }

    // Un TerminalPanel por repositorio abierto; se libera al cerrar la pestaña del repositorio.
    public sealed class TerminalRegistry
    {
        public static TerminalRegistry Instance { get; } = new();

        public TerminalEntry Acquire(Repository repo)
        {
            ArgumentNullException.ThrowIfNull(repo);
            var entry = _cache.GetOrCreate(repo.FullPath);
            entry.Bind(repo);
            return entry;
        }

        public void Release(string repoPath)
        {
            if (!string.IsNullOrWhiteSpace(repoPath))
                _cache.Release(repoPath);
        }

        // "Pedir mensaje a Claude": abre el panel inferior e inserta el pedido de commit (sin Enter).
        public async Task AskCommitMessageAsync(Repository repo)
        {
            var entry = Acquire(repo);
            repo.IsTerminalVisible = true;
            entry.Panel.ContextRefresher?.Invoke(entry.Panel);
            try
            {
                await entry.Panel.CommitMessageAsync();
            }
            catch (Exception ex)
            {
                Native.OS.LogException(ex);
            }
        }

        private readonly RepoScopedCache<TerminalEntry> _cache = new(p => new TerminalEntry(p), OperatingSystem.IsWindows());
    }
}
