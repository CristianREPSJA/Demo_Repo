using System;
using System.Linq;

namespace SourceGit.ViewModels
{
    // RAMA: datos de contexto del resolvedor de conflictos (ramas, banda de estado y acción tras el stage).
    public sealed record ConflictContext(
        string RepoPath,
        string FilePath,
        string OursName,
        string OursSha,
        string TheirsName,
        string TheirsSha,
        string Banner,
        Action AfterStaged)
    {
        public static ConflictContext FromRepository(Repository repo, Models.Commit head, string filePath)
        {
            var current = repo.CurrentBranch?.Name ?? ShortSha(head?.SHA);
            var conflicted = repo.WorkingCopy?.Unstaged?.Count(c => c.IsConflicted) ?? 0;

            var (ours, oursSha, theirs, theirsSha, operation) = repo.InProgressContext switch
            {
                MergeInProgress m => (current, head?.SHA, m.SourceName, m.Source?.SHA,
                    App.Text("Rama.Conflict.Banner.Merge", m.SourceName, current)),
                RebaseInProgress r => (r.BaseName, r.Onto?.SHA, r.HeadName, r.StoppedAt?.SHA,
                    App.Text("Rama.Conflict.Banner.Rebase", r.HeadName, r.BaseName)),
                CherryPickInProgress c => (current, head?.SHA, c.HeadName, c.Head?.SHA,
                    App.Text("Rama.Conflict.Banner.CherryPick", c.HeadName, current)),
                RevertInProgress v => (current, head?.SHA, ShortSha(v.Head?.SHA), v.Head?.SHA,
                    App.Text("Rama.Conflict.Banner.Revert", ShortSha(v.Head?.SHA), current)),
                _ => (current, head?.SHA, App.Text("Rama.Conflict.StashOrPatch"), null,
                    App.Text("Rama.Conflict.Banner.Other")),
            };

            return new ConflictContext(repo.FullPath, filePath, ours, ShortSha(oursSha), theirs, ShortSha(theirsSha),
                ComposeBanner(operation, conflicted), repo.MarkWorkingCopyDirtyManually);
        }

        private static string ComposeBanner(string operation, int conflicted)
        {
            if (conflicted <= 0)
                return operation;

            var files = conflicted == 1
                ? App.Text("Rama.Conflict.Banner.FilesOne")
                : App.Text("Rama.Conflict.Banner.FilesMany", conflicted);
            return $"{operation} {files}";
        }

        private static string ShortSha(string sha) =>
            string.IsNullOrEmpty(sha) ? string.Empty : sha.Length > 7 ? sha[..7] : sha;
    }
}

