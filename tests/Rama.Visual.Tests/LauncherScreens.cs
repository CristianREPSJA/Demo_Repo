using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using SourceGit.ViewModels;

namespace Rama.VisualTests;

// Pantallas del Launcher: bienvenida, repositorio abierto (solo lectura) y página Terminal sin sesión.
public class LauncherScreens : IDisposable
{
    public void Dispose() { }

    private const string RamaRepo = @"C:\Users\Cris\Desktop\Personal\GitClient\rama";

    // Todas las pantallas alojadas en el Launcher comparten una única ventana y sesión de prueba:
    // una segunda ventana Launcher (o reutilizarla entre pruebas) bloquea/congela el render headless.
    [AvaloniaFact]
    [Trait("Category", "Visual")]
    public async Task LauncherHostedScreens()
    {
        if (!VisualEnvironment.Enabled) return;
        await Welcome();
        await RepositoryWithDiff();
        await TerminalEmpty();
        var rail = new RailPageScreens();
        await rail.Accounts();
        await rail.Clone();
        await rail.PendingReviews();
        await new JiraConflictLocaleScreens().JiraBoard();
    }

    public async Task Welcome()
    {
        if (!VisualEnvironment.Enabled) return;
        var window = Screens.NewLauncher(null, out _);
        await Shot.Save(window, "01-launcher-welcome-1440x900", 1440, 900);
        await Shot.Save(window, "01-launcher-welcome-1024x700", 1024, 700);
    }

    public async Task RepositoryWithDiff()
    {
        if (!VisualEnvironment.Enabled) return;
        var window = Screens.NewLauncher(RamaRepo, out var launcher);
        var repo = (Repository)launcher.ActivePage.Data;
        await Screens.WaitFor(() => repo.Histories.Commits.Count > 0, "historial");
        await Shot.Save(window, "02-repo-history-1440x900", 1440, 900);

        repo.Histories.SelectedCommits = [repo.Histories.Commits[0]];
        await Screens.WaitFor(() => repo.Histories.DetailContext is CommitDetail, "detalle de commit");
        var detail = (CommitDetail)repo.Histories.DetailContext;
        detail.ActiveTabIndex = 1;
        await Screens.WaitFor(() => detail.VisibleChanges is { Count: > 0 }, "cambios del commit");
        var pick = detail.VisibleChanges.FirstOrDefault(c => c.Path.EndsWith(".cs")) ?? detail.VisibleChanges[0];
        detail.ChangeSelection = new ChangeSelection(new List<SourceGit.Models.Change> { pick });
        await Screens.WaitFor(() => detail.DiffContext != null, "diff");
        await Shot.Settle(10, 60);
        await Shot.Save(window, "03-repo-commit-diff-1440x900", 1440, 900);
        await Shot.Save(window, "03-repo-commit-diff-1024x700", 1024, 700);
    }

    public async Task TerminalEmpty()
    {
        if (!VisualEnvironment.Enabled) return;
        var window = Screens.NewLauncher(null, out var launcher);
        launcher.Rail.ShowCommand.Execute(RailDestination.Terminal);
        await Shot.Settle();
        Screens.AssertOnlyVisible<SourceGit.Views.TerminalPage>(window);
        await Shot.Save(window, "12-terminal-empty-1440x900", 1440, 900);
    }
}

public static class Screens
{
    private static readonly List<Window> Open = [];

    public static void Track(Window w)
    {
        if (!Open.Contains(w))
            Open.Add(w);
    }

    public static void CloseAll()
    {
        foreach (var w in Open.ToList())
        {
            try { w.Hide(); } catch { /* ventana ya cerrada */ }
        }

        Open.Clear();
    }

    private static SourceGit.Views.Launcher _shared;

    // Una sola ventana Launcher por proceso de prueba (crear una segunda bloquea el hilo de UI headless);
    // cada pantalla le asigna un Launcher VM nuevo.
    public static Window NewLauncher(string repo, out Launcher launcher)
    {
        var pref = Preferences.Instance;
        pref.GetActiveWorkspace().Repositories.Clear();
        pref.RepositoryNodes.Clear();

        launcher = new Launcher(repo);
        Shot.SetField(SourceGit.App.Current, "_launcher", launcher);
        if (_shared == null)
        {
            _shared = new SourceGit.Views.Launcher { Width = 1440, Height = 900 };
            _shared.Show();
        }

        _shared.DataContext = launcher;
        return _shared;
    }

    // Prueba del arreglo de visibilidad: solo la página del destino activo es visible (sin forzarla a mano).
    public static void AssertOnlyVisible<TPage>(Window window) where TPage : Control
    {
        var pages = new Control[]
        {
            Shot.Find<SourceGit.Views.PendingReviewsPage>(window), Shot.Find<SourceGit.Views.JiraBoardPage>(window),
            Shot.Find<SourceGit.Views.TerminalPage>(window), Shot.Find<SourceGit.Views.AccountsPage>(window),
            Shot.Find<SourceGit.Views.ClonePage>(window),
        };
        var visible = pages.Where(p => p.IsEffectivelyVisible).Select(p => p.GetType().Name).ToList();
        Assert.Equal([typeof(TPage).Name], visible);
    }

    public static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 20000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException($"Tiempo agotado esperando: {what}");
            await Shot.Settle(1, 50);
        }
    }
}
