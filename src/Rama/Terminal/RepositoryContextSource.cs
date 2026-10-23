using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using Rama.Core.Jira;
using Rama.Core.Terminal;
using SourceGit.ViewModels;

namespace SourceGit.Rama.Terminal
{
    // Traduce el estado del repositorio (selección, rama, ticket) a TerminalContext y mantiene el panel al día.
    public sealed class RepositoryContextSource : IDisposable
    {
        private readonly Repository _repo;
        private readonly TerminalPanel _panel;
        private CommitDetail _watchedDetail;

        public RepositoryContextSource(Repository repo, TerminalPanel panel)
        {
            _repo = repo;
            _panel = panel;
            _repo.PropertyChanged += OnRepoChanged;
            if (_repo.WorkingCopy != null)
                _repo.WorkingCopy.PropertyChanged += OnWorkingCopyChanged;
            if (_repo.Histories != null)
                _repo.Histories.PropertyChanged += OnHistoriesChanged;
            WatchDetail(_repo.Histories?.DetailContext as CommitDetail);
            Refresh();
        }

        public void Refresh() => _panel.Context = Build();

        public TerminalContext Build()
        {
            var branch = _repo.CurrentBranch?.Name;
            var locals = _repo.Branches?.Where(b => b.IsLocal).Select(b => b.Name) ?? [];
            var remotes = _repo.Branches?.Where(b => !b.IsLocal).Select(b => b.FriendlyName) ?? [];
            var staged = _repo.WorkingCopy?.Staged?.Select(c => c.Path).ToList() ?? [];
            return new TerminalContext(SelectedFiles(), staged, BranchTicketParser.ExtractKey(branch),
                BaseBranchResolver.Resolve(locals, remotes, branch));
        }

        // Selección de la copia de trabajo; si no hay, la del detalle del commit abierto.
        private List<string> SelectedFiles()
        {
            var wc = _repo.WorkingCopy;
            var changes = wc?.SelectedUnstaged is { Count: > 0 } u ? u.Changes
                : wc?.SelectedStaged is { Count: > 0 } s ? s.Changes
                : (_repo.Histories?.DetailContext as CommitDetail)?.ChangeSelection?.Changes;
            return changes?.Select(c => c.Path).Distinct().ToList() ?? [];
        }

        private void OnRepoChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(Repository.CurrentBranch) or nameof(Repository.Branches))
                Post();
        }

        private void OnWorkingCopyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(WorkingCopy.SelectedUnstaged) or nameof(WorkingCopy.SelectedStaged) or nameof(WorkingCopy.Staged))
                Post();
        }

        private void OnHistoriesChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Histories.DetailContext))
                return;
            WatchDetail(_repo.Histories.DetailContext as CommitDetail);
            Post();
        }

        private void WatchDetail(CommitDetail detail)
        {
            if (ReferenceEquals(detail, _watchedDetail))
                return;
            if (_watchedDetail != null)
                _watchedDetail.PropertyChanged -= OnDetailChanged;
            _watchedDetail = detail;
            if (_watchedDetail != null)
                _watchedDetail.PropertyChanged += OnDetailChanged;
        }

        private void OnDetailChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CommitDetail.ChangeSelection))
                Post();
        }

        private void Post()
        {
            if (Dispatcher.UIThread.CheckAccess())
                Refresh();
            else
                Dispatcher.UIThread.Post(Refresh);
        }

        public void Dispose()
        {
            _repo.PropertyChanged -= OnRepoChanged;
            if (_repo.WorkingCopy != null)
                _repo.WorkingCopy.PropertyChanged -= OnWorkingCopyChanged;
            if (_repo.Histories != null)
                _repo.Histories.PropertyChanged -= OnHistoriesChanged;
            WatchDetail(null);
        }
    }
}
