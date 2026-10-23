using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Threading;

using Rama.Core.Clone;

namespace SourceGit.ViewModels
{
    // Ejecucion del clonado: siempre via Commands.Clone (el credential helper resuelve la autenticacion).
    public partial class ClonePageViewModel
    {
        private async Task RunCloneAsync(string url, string destination, string name)
        {
            var launcher = App.GetLauncher();
            if (launcher == null)
                return;

            var (parent, folder) = CloneDestination.Split(destination);
            if (parent.Length == 0 || folder.Length == 0)
            {
                CloneError = App.Text("Rama.Clone.Error.Destination");
                return;
            }

            var target = Path.GetFullPath(Path.Combine(parent, folder));
            if (!PrepareTarget(parent, target))
                return;

            var page = launcher.ActivePage;
            var log = new CloneProgressLog(p => Dispatcher.UIThread.Post(() => ApplyProgress(p)));
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            CloneError = string.Empty;
            CloningName = name;
            ApplyProgress(new GitProgress(App.Text("Rama.Clone.Starting"), null, null));
            IsCloning = true;

            try
            {
                var ok = await new Commands.Clone(page.Node.Id, parent, url, folder, string.Empty, string.Empty) { RaiseError = false }
                    .WithCancellation(token)
                    .Use(log)
                    .ExecAsync();

                if (token.IsCancellationRequested)
                    return;

                if (!ok || !Directory.Exists(target))
                {
                    var detail = log.LastLines();
                    CloneError = detail.Length > 0 ? detail : App.Text("Rama.Clone.Error.Failed");
                    return;
                }

                await FinishCloneAsync(launcher, page, target, token);
            }
            finally
            {
                IsCloning = false;
                _cts = null;
            }
        }

        private bool PrepareTarget(string parent, string target)
        {
            try
            {
                Directory.CreateDirectory(parent);
                if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                {
                    CloneError = App.Text("Rama.Clone.Error.NotEmpty");
                    return false;
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CloneError = ex.Message;
                return false;
            }
        }

        private async Task FinishCloneAsync(Launcher launcher, LauncherPage page, string target, CancellationToken token)
        {
            if (InitSubmodules && !token.IsCancellationRequested)
            {
                var submodules = await new Commands.QueryUpdatableSubmodules(target, true).GetResultAsync();
                if (submodules.Count > 0)
                    await new Commands.Submodule(target)
                        .WithCancellation(token)
                        .UpdateAsync(submodules, true, true, false);
            }

            var node = Preferences.Instance.FindOrAddNodeByRepositoryPath(target, null, true);
            await node.UpdateStatusAsync(false, null);
            Welcome.Instance.Refresh();

            await RefreshLocalIndexAsync();
            ApplyFilter();

            launcher.OpenRepositoryInTab(node, page);
            launcher.Rail.ShowCommand.Execute(RailDestination.Repository);
        }
    }
}
