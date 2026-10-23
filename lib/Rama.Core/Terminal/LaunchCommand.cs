namespace Rama.Core.Terminal;

// Programa y argumentos para lanzar en el PTY. En Windows los .cmd/.bat necesitan cmd.exe /c.
public sealed record LaunchCommand(string App, IReadOnlyList<string> Args)
{
    public static LaunchCommand For(string path, bool isWindows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var ext = Path.GetExtension(path);
        var isBatch = ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase);
        return isWindows && isBatch ? new("cmd.exe", ["/c", path]) : new(path, []);
    }
}
