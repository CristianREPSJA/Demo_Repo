using System.Diagnostics;
using System.Text.RegularExpressions;

using Avalonia.Headless.XUnit;

using Rama.Core.Accounts;
using Rama.Core.Jira;

using SourceGit.ViewModels;
using SourceGit.Views;

namespace Rama.VisualTests;

// Tablero Jira simulado, resolvedor de conflictos con repo temporal y verificación estática de claves de locale.
public class JiraConflictLocaleScreens : IDisposable
{
    public void Dispose() { }

    public async Task JiraBoard()
    {
        if (!VisualEnvironment.Enabled) return;
        var window = Screens.NewLauncher(null, out var launcher);
        launcher.Rail.ShowCommand.Execute(RailDestination.JiraBoard);
        Screens.ShowOnly<JiraBoardPage>(window);
        await Shot.Settle();

        var account = new JiraAccount("contoso", "diego@example.com", "DFE", 7);
        var controller = new BoardController(new FakeJiraApi(), account);
        await controller.SyncAsync();

        var vm = new JiraBoardViewModel();
        Shot.SetField(vm, "_controller", controller);
        Shot.SetField(vm, "_account", account);
        Shot.SetField(vm, "_visible", true); // evita que la página reactive el VM contra las cuentas reales
        vm.Title = "Tablero DFE";
        vm.SyncText = "Sincronizado hace 5 s";
        Shot.SetProperty(vm, "State", JiraBoardState.Ready);
        Shot.Invoke(vm, "Rebuild");
        Shot.Find<JiraBoardPage>(window).DataContext = vm;
        await Shot.Settle(8, 50);
        await Shot.Save(window, "10-jira-board-1440x900", 1440, 900);
        await Shot.Save(window, "10-jira-board-1024x700", 1024, 700);

        // Arrastre de DFE-3445 (En desarrollo): destinos válidos, inválidos y la columna actual.
        var drops = new List<ColumnDrop>
        {
            new(DropKind.Invalid, "No hay transición hacia Por hacer"),
            new(DropKind.Valid),
            new(DropKind.Current),
            new(DropKind.Valid),
            new(DropKind.RequiresFields),
            new(DropKind.Invalid, "Requiere aprobación del PO"),
            new(DropKind.Invalid, "No hay transición hacia UAT"),
            new(DropKind.Invalid, "Solo QA puede cerrar"),
        };
        for (var i = 0; i < vm.Columns.Count; i++)
            vm.Columns[i].ApplyDrop(drops[i]);
        vm.Columns[2].Cards[0].IsDragSource = true;
        vm.MoveError = "No se pudo mover DFE-3445: Jira rechazó la transición.";
        await Shot.Save(window, "11-jira-board-drop-invalid-1440x900", 1440, 900);
    }

    [AvaloniaFact]
    [Trait("Category", "Visual")]
    public async Task ConflictResolverWindow()
    {
        if (!VisualEnvironment.Enabled) return;
        using var repo = new TempConflictRepo();
        var ctx = new ConflictContext(repo.Path, TempConflictRepo.FileName, "main", "a1b2c3d", "feature/stock", "d4e5f6a",
            "Fusionando feature/stock en main. 1 archivo en conflicto.", null);
        var vm = new SourceGit.ViewModels.ConflictResolver(ctx);
        var window = new SourceGit.Views.ConflictResolver { DataContext = vm };
        await Shot.Save(window, "13-conflict-resolver-3-regiones-1440x900", 1440, 900);

        vm.ToggleOurs(0);
        vm.ToggleTheirs(1);
        await Shot.Save(window, "14-conflict-resolver-parcial-1440x900", 1440, 900);
    }

    // Claves Text.* usadas por XAML/App.Text que no existen en la cadena es_CL -> es_ES -> en_US.
    [Fact]
    [Trait("Category", "Visual")]
    public void MissingLocaleKeys()
    {
        if (!VisualEnvironment.Enabled) return;
        var src = Path.Combine(VisualEnvironment.RepoRoot, "src");
        var defined = Directory.EnumerateFiles(Path.Combine(src, "Resources", "Locales"), "*.axaml")
            .Where(f => Path.GetFileName(f) is "en_US.axaml" or "es_ES.axaml")
            .Concat(Directory.EnumerateFiles(Path.Combine(src, "Rama", "Resources", "Locales"), "es_CL*.axaml"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), "x:Key=\"(Text\\.[^\"]+)\"").Select(m => m.Groups[1].Value))
            .ToHashSet();

        var missing = new SortedSet<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".axaml") || f.EndsWith(".cs")) && !f.Contains(@"\Locales\") &&
                                 !f.Contains(@"\obj\") && !f.Contains(@"\bin\")))
        {
            var text = File.ReadAllText(file);
            var rel = Path.GetRelativePath(src, file);
            var used = file.EndsWith(".axaml")
                ? Regex.Matches(text, @"(?:DynamicResource|StaticResource)\s+(Text\.[A-Za-z0-9_.]+)").Select(m => m.Groups[1].Value)
                : Regex.Matches(text, "App\\.Text\\(\"([A-Za-z0-9_.]+)\"").Select(m => "Text." + m.Groups[1].Value);
            foreach (var key in used.Where(k => !defined.Contains(k)))
                missing.Add($"{key}  <- {rel}");
        }

        File.WriteAllLines(Path.Combine(VisualEnvironment.OutDir, "missing-locale-keys.txt"), missing);
        File.AppendAllText(Path.Combine(VisualEnvironment.OutDir, "binding-errors.txt"), $"[Information] Harness : claves de locale faltantes (estático): {missing.Count}{Environment.NewLine}");
    }
}

// Repo git desechable con 3 regiones en conflicto (merge fallido), creado bajo %TEMP%.
internal sealed class TempConflictRepo : IDisposable
{
    public const string FileName = "app.txt";
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rama-visual-" + Guid.NewGuid().ToString("N")[..8]);

    public TempConflictRepo()
    {
        Directory.CreateDirectory(Path);
        Git("init -q -b main");
        Git("config user.name Test");
        Git("config user.email test@example.invalid");
        Git("config core.autocrlf false");
        Write(Content("base"));
        Git("add .");
        Git("commit -q -m base");
        Git("checkout -q -b feature");
        Write(Content("theirs"));
        Git("commit -q -am feature");
        Git("checkout -q main");
        Write(Content("ours"));
        Git("commit -q -am ours");
        Git("merge feature", allowFailure: true);
    }

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(Path, true);
    }

    private static string Content(string side)
    {
        const string gap = "c1\nc2\nc3\nc4\nc5\nc6\nc7\nc8\n";
        return $"a\n{side}-1\n{gap}{side}-2\n{gap}{side}-3\nz\n";
    }

    private void Write(string content) => File.WriteAllText(System.IO.Path.Combine(Path, FileName), content);

    private void Git(string args, bool allowFailure = false)
    {
        var psi = new ProcessStartInfo(SourceGit.Native.OS.GitExecutable, args)
        {
            WorkingDirectory = Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["GIT_CONFIG_GLOBAL"] = System.IO.Path.Combine(Path, ".gitconfig-none");
        psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        using var proc = Process.Start(psi)!;
        proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0 && !allowFailure)
            throw new InvalidOperationException($"git {args}: {stderr}");
    }
}
