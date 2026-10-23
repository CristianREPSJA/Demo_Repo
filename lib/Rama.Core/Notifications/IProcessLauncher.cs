using System.Diagnostics;

namespace Rama.Core.Notifications;

// Lanza procesos externos sin shell; los argumentos viajan como elementos separados.
public interface IProcessLauncher
{
    bool IsOnPath(string executable);
    Task RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken ct = default);
}

public sealed class ProcessLauncher : IProcessLauncher
{
    public bool IsOnPath(string executable)
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return dirs.Any(d => File.Exists(Path.Combine(d, executable)));
    }

    public async Task RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"No se pudo iniciar {executable}.");
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
    }
}
