namespace Rama.Core.Terminal;

// Busca ejecutables en PATH (con PATHEXT en Windows). El sistema de archivos es inyectable para pruebas.
public sealed class ExecutableLocator
{
    private const string DefaultPathExt = ".COM;.EXE;.BAT;.CMD";
    private readonly Func<string, bool> _fileExists;
    private readonly bool _isWindows;

    public ExecutableLocator(Func<string, bool> fileExists, bool isWindows)
    {
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
        _isWindows = isWindows;
    }

    public static ExecutableLocator ForCurrentOs() => new(File.Exists, OperatingSystem.IsWindows());

    public bool IsWindows => _isWindows;

    public string? Find(string name, string? pathVar, string? pathExt)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Nombre de ejecutable vacío.", nameof(name));
        if (string.IsNullOrEmpty(pathVar))
            return null;

        var separator = _isWindows ? ';' : ':';
        var extensions = Extensions(pathExt);
        foreach (var raw in pathVar.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = raw.Trim().Trim('"');
            if (dir.Length == 0)
                continue;
            foreach (var ext in extensions)
            {
                var candidate = Join(dir, name + ext);
                if (_fileExists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    private string[] Extensions(string? pathExt)
    {
        if (!_isWindows)
            return [string.Empty];
        var list = string.IsNullOrWhiteSpace(pathExt) ? DefaultPathExt : pathExt;
        return list.ToLowerInvariant().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private string Join(string dir, string file)
    {
        var sep = _isWindows ? '\\' : '/';
        return dir.EndsWith(sep) || dir.EndsWith('/') ? dir + file : dir + sep + file;
    }
}
