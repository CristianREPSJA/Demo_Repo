using System.Diagnostics;

using Avalonia.Logging;

namespace Rama.VisualTests;

// Preparación hermética: datos de Rama en un directorio local de pruebas (sin tocar %APPDATA%\Rama),
// git en solo lectura y sumidero de logs de Avalonia hacia binding-errors.txt.
public static class VisualEnvironment
{
    public const string OutVariable = "RAMA_VISUAL_OUT";

    public static string OutDir => Environment.GetEnvironmentVariable(OutVariable);
    public static bool Enabled => !string.IsNullOrWhiteSpace(OutDir);
    public static string RepoRoot { get; } = FindRepoRoot();
    public static string DataDir { get; private set; }
    public static BindingLogSink Sink { get; private set; }

    public static void Prepare()
    {
        if (DataDir != null)
            return;

        if (Enabled)
            Directory.CreateDirectory(OutDir);

        // Modo portable de SourceGit: carpeta "data" junto al ejecutable => ConfigDir/CacheDir locales.
        DataDir = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "data");
        Directory.CreateDirectory(DataDir);
        Environment.SetEnvironmentVariable("GIT_OPTIONAL_LOCKS", "0");
        SourceGit.Native.OS.SetupBasicDirectories();
        SourceGit.Native.OS.GitExecutable = FindGit();
        // Avatares: solo el directorio de caché (sin iniciar el gestor, que haría peticiones de red).
        var avatars = Path.Combine(DataDir, "avatars");
        Directory.CreateDirectory(avatars);
        typeof(SourceGit.Models.AvatarManager).GetField("_storePath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(SourceGit.Models.AvatarManager.Instance, avatars);
    }

    public static void AfterSetup()
    {
        var logFile = Enabled ? Path.Combine(OutDir, "binding-errors.txt") : null;
        if (logFile != null)
            File.Delete(logFile);
        Sink = new BindingLogSink(logFile);
        Logger.Sink = Sink;
        SourceGit.ViewModels.Preferences.Instance.Locale = "es_CL";
        SourceGit.App.SetLocale("es_CL");
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
}

// Registra advertencias/errores de binding, propiedades y recursos; cuenta las líneas.
public sealed class BindingLogSink : ILogSink
{
    private static readonly string[] Areas = [LogArea.Binding, LogArea.Property, LogArea.Control, LogArea.Visual, LogArea.Layout];
    private readonly string _file;
    private readonly List<string> _lines = [];

    public BindingLogSink(string file) => _file = file;

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
                return _lines.ToList();
        }
    }

    public bool IsEnabled(LogEventLevel level, string area) =>
        level >= LogEventLevel.Warning && Areas.Contains(area);

    public void Log(LogEventLevel level, string area, object source, string messageTemplate)
        => Add(level, area, source, messageTemplate);

    public void Log(LogEventLevel level, string area, object source, string messageTemplate, params object[] propertyValues)
        => Add(level, area, source, FormatTemplate(messageTemplate, propertyValues));

    public void Mark(string text) => Add(LogEventLevel.Information, "Harness", null, text, always: true);

    private void Add(LogEventLevel level, string area, object source, string text, bool always = false)
    {
        var line = $"[{level}] {area} {source?.GetType().Name}: {text}";
        lock (_lines)
        {
            _lines.Add(line);
            if (_file != null)
                File.AppendAllText(_file, line + Environment.NewLine);
        }
    }

    // Plantillas estilo "{Prop}" -> valores posicionales.
    private static string FormatTemplate(string template, object[] values)
    {
        var i = 0;
        return System.Text.RegularExpressions.Regex.Replace(template, @"\{[^}]*\}",
            _ => i < values.Length ? Convert.ToString(values[i++]) : "?");
    }
}
