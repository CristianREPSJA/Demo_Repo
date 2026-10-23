using System.Collections;

namespace Rama.Core.Terminal;

// Entorno para procesos hijos del PTY (shell y claude).
public static class ChildEnvironment
{
    public const string Term = "xterm-256color";
    public const string ColorTerm = "truecolor";
    public const string DefaultUtf8Locale = "en_US.UTF-8";

    private static readonly string[] BlockedPrefixes = ["CLAUDE_", "SOURCEGIT_", "RAMA_"];
    private static readonly string[] BlockedNames = ["NO_COLOR", "CLAUDECODE"];

    public static IReadOnlyDictionary<string, string> Sanitize(IDictionary<string, string> env) =>
        Sanitize(env, OperatingSystem.IsWindows());

    // Devuelve una copia: sin variables de sesiones Claude/SourceGit ni NO_COLOR, con capacidades de color y UTF-8.
    public static IReadOnlyDictionary<string, string> Sanitize(IDictionary<string, string> env, bool isWindows)
    {
        ArgumentNullException.ThrowIfNull(env);
        var comparer = isWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var result = new Dictionary<string, string>(comparer);
        foreach (var (key, value) in env)
        {
            if (!IsBlocked(key))
                result[key] = value;
        }

        result["TERM"] = Term;
        result["COLORTERM"] = ColorTerm;
        if (!isWindows)
        {
            var locale = result.TryGetValue("LANG", out var lang) && IsUtf8(lang) ? lang : DefaultUtf8Locale;
            result["LANG"] = locale;
            result["LC_ALL"] = locale;
        }
        return result;
    }

    public static bool IsBlocked(string key)
    {
        foreach (var name in BlockedNames)
        {
            if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        foreach (var prefix in BlockedPrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // Cambios para pasar de `current` a `desired`: valor null = eliminar la variable.
    public static IReadOnlyList<KeyValuePair<string, string?>> Diff(
        IReadOnlyDictionary<string, string> current, IReadOnlyDictionary<string, string> desired)
    {
        var changes = new List<KeyValuePair<string, string?>>();
        foreach (var (key, value) in desired)
        {
            if (!current.TryGetValue(key, out var old) || old != value)
                changes.Add(new(key, value));
        }
        foreach (var key in current.Keys)
        {
            if (!desired.ContainsKey(key))
                changes.Add(new(key, null));
        }
        return changes;
    }

    public static Dictionary<string, string> FromProcess()
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var result = new Dictionary<string, string>(comparer);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            result[(string)entry.Key] = entry.Value as string ?? string.Empty;
        return result;
    }

    private static bool IsUtf8(string locale) =>
        locale.Contains("UTF-8", StringComparison.OrdinalIgnoreCase) || locale.Contains("utf8", StringComparison.OrdinalIgnoreCase);
}
