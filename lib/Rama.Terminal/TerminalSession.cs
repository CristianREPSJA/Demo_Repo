using Iciclecreek.Terminal;
using Rama.Core.Terminal;

namespace Rama.Terminal;

// Sesión PTY alojada en un TerminalControl.
public interface ITerminalSession
{
    bool IsRunning { get; }
    int Columns { get; }
    int Rows { get; }
    Task StartAsync(string workingDirectory, LaunchCommand command, IReadOnlyDictionary<string, string> environment);
    Task WriteInputAsync(string text, bool submit = false);
    void Kill();
}

public sealed class TerminalSession : ITerminalSession
{
    // La librería no acepta entorno propio: el hijo hereda el del proceso al hacer spawn.
    // Se aplica el entorno saneado solo durante el lanzamiento y se restaura después.
    private static readonly SemaphoreSlim s_launchLock = new(1, 1);
    private readonly TerminalControl _control;

    public TerminalSession(TerminalControl control)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
    }

    public bool IsRunning => TerminalBridge.FindView(_control) is { } view && TerminalBridge.GetConnection(view) is not null;
    public int Columns => _control.Terminal?.Cols ?? 0;
    public int Rows => _control.Terminal?.Rows ?? 0;

    public async Task StartAsync(string workingDirectory, LaunchCommand command, IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(environment);
        if (TerminalBridge.BindingError is { } error)
            throw new InvalidOperationException(error);

        await s_launchLock.WaitAsync().ConfigureAwait(true);
        var original = ChildEnvironment.FromProcess();
        var changes = ChildEnvironment.Diff(original, environment);
        try
        {
            Apply(changes);
            await _control.LaunchProcess(workingDirectory, command.App, [.. command.Args]).ConfigureAwait(true);
        }
        finally
        {
            Restore(changes, original);
            s_launchLock.Release();
        }
        InstallUtf8Reader();
    }

    // Inserta texto en la entrada del PTY; Enter solo si submit = true.
    public Task WriteInputAsync(string text, bool submit = false)
    {
        var view = TerminalBridge.FindView(_control) ?? throw new InvalidOperationException("La terminal aún no está lista.");
        var payload = InputPayload.Build(text, _control.Terminal?.BracketedPasteMode ?? false, submit);
        return payload.Length == 0 ? Task.CompletedTask : TerminalBridge.SendAsync(view, payload);
    }

    public void Kill()
    {
        if (IsRunning)
            _control.Kill();
    }

    private void InstallUtf8Reader()
    {
        if (TerminalBridge.FindView(_control) is not { } view)
            return;
        var connection = TerminalBridge.GetConnection(view);
        if (connection is not null and not Utf8PtyConnection)
            TerminalBridge.SetConnection(view, new Utf8PtyConnection(connection));
    }

    private static void Apply(IEnumerable<KeyValuePair<string, string?>> changes)
    {
        foreach (var (key, value) in changes)
            Environment.SetEnvironmentVariable(key, value);
    }

    private static void Restore(IEnumerable<KeyValuePair<string, string?>> changes, IReadOnlyDictionary<string, string> original)
    {
        foreach (var (key, _) in changes)
            Environment.SetEnvironmentVariable(key, original.TryGetValue(key, out var value) ? value : null);
    }
}
