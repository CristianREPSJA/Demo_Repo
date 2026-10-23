using System.Diagnostics;

using Rama.Core.Conflicts;
using SourceGit.ViewModels;

namespace Rama.App.Tests;

// Repo git desechable bajo obj/tmp con 3 regiones en conflicto; se resuelve vía ConflictResolver sin ventana.
public sealed class ConflictResolverIntegrationTests : IDisposable
{
    private const string FileName = "app.txt";

    public ConflictResolverIntegrationTests()
    {
        _repo = Path.Combine(FindRepoRoot(), "obj", "tmp", "conflict-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_repo);
        SourceGit.Native.OS.GitExecutable = FindGit();
        CreateConflictingMerge();
    }

    public void Dispose()
    {
        if (!Directory.Exists(_repo))
            return;

        foreach (var f in Directory.EnumerateFiles(_repo, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_repo, true);
    }

    [Fact]
    public async Task Resolve_ThreeRegions_SavesWithoutMarkersAndStages()
    {
        var vm = new ConflictResolver(NewContext());
        Assert.Equal(3, vm.ConflictCount);
        Assert.Equal(3, vm.UnresolvedCount);

        vm.ToggleOurs(0);
        vm.ToggleTheirs(1);
        vm.UseBothOursFirst(); // aplica a la región actual (1) => ambas, actual primero
        vm.ToggleTheirs(2);
        Assert.Equal(0, vm.UnresolvedCount);

        Assert.True(await vm.SaveAndStageAsync(), vm.Error);

        var saved = await File.ReadAllTextAsync(Path.Combine(_repo, FileName));
        Assert.Equal(0, ConflictParser.Parse(saved).ConflictCount);
        Assert.DoesNotContain("<<<<<<<", saved);
        Assert.Contains("ours-1\n", saved);
        Assert.Contains("ours-2\ntheirs-2\n", saved);
        Assert.Contains("theirs-3\n", saved);
        Assert.Contains(FileName, Git("diff --cached --name-only"));
        Assert.Equal(string.Empty, Git("diff --name-only --diff-filter=U").Trim());
    }

    [Fact]
    public async Task Save_WithRemainingConflicts_RefusesAndKeepsFile()
    {
        var vm = new ConflictResolver(NewContext());
        vm.ToggleOurs(0);

        Assert.False(await vm.SaveAndStageAsync());
        Assert.Equal(2, vm.UnresolvedCount);
        Assert.Contains("Rama.Conflict.Remaining", vm.Error); // sin App, App.Text devuelve la clave
        Assert.Contains("<<<<<<<", await File.ReadAllTextAsync(Path.Combine(_repo, FileName)));
        Assert.Contains(FileName, Git("diff --name-only --diff-filter=U"));
    }

    [Fact]
    public async Task ManualEdit_RemovingMarkers_ResolvesAndStages()
    {
        var vm = new ConflictResolver(NewContext());
        vm.ToggleOurs(0);
        vm.ToggleOurs(1);

        // Edición manual en la región 2: reemplaza el bloque completo por texto propio.
        var span = vm.ResultSpans.First(s => s.ConflictIndex == 2);
        var text = vm.ResultText;
        var edited = text[..span.Start] + "manual-3\n" + text[(span.Start + span.Length)..];
        vm.ApplyResultEdit(span.Start, span.Length, edited);

        Assert.Equal(0, vm.UnresolvedCount);
        Assert.True(vm.Blocks.Single(b => b.ConflictIndex == 2).IsManual);
        Assert.True(await vm.SaveAndStageAsync(), vm.Error);
        Assert.Contains("manual-3\n", await File.ReadAllTextAsync(Path.Combine(_repo, FileName)));
        Assert.Contains(FileName, Git("diff --cached --name-only"));
    }

    [Fact]
    public void Navigation_MovesBetweenRegions()
    {
        var vm = new ConflictResolver(NewContext());
        Assert.Equal(0, vm.CurrentIndex);
        vm.GotoNext();
        vm.GotoNext();
        vm.GotoNext();
        Assert.Equal(2, vm.CurrentIndex);
        vm.GotoPrev();
        Assert.Equal(1, vm.CurrentIndex);
        Assert.True(vm.Blocks.Single(b => b.ConflictIndex == 1).IsCurrent);
    }

    private ConflictContext NewContext() =>
        new(_repo, FileName, "main", null, "feature", null, string.Empty, null);

    private void CreateConflictingMerge()
    {
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
        Assert.Contains("<<<<<<<", File.ReadAllText(Path.Combine(_repo, FileName)));
    }

    // Regiones separadas por 8 líneas comunes para que git no las fusione en una sola.
    private static string Content(string side)
    {
        const string gap = "c1\nc2\nc3\nc4\nc5\nc6\nc7\nc8\n";
        return $"a\n{side}-1\n{gap}{side}-2\n{gap}{side}-3\nz\n";
    }

    private void Write(string content) => File.WriteAllText(Path.Combine(_repo, FileName), content);

    private string Git(string args, bool allowFailure = false)
    {
        var psi = new ProcessStartInfo(SourceGit.Native.OS.GitExecutable, args)
        {
            WorkingDirectory = _repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(_repo, ".gitconfig-none");
        psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0 && !allowFailure)
            throw new InvalidOperationException($"git {args}: {stderr}");
        return stdout;
    }

    private static string FindGit()
    {
        var exe = OperatingSystem.IsWindows() ? "git.exe" : "git";
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim('"'), exe);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new InvalidOperationException("git no está en PATH");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SourceGit.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repo");
    }

    private readonly string _repo;
}
