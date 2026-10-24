namespace Rama.Core.Azure.Entra;

// Inyecta el Bearer de Entra en una invocación de git mediante GIT_CONFIG_COUNT/KEY_n/VALUE_n
// (http.https://dev.azure.com/.extraheader). El token va solo en el entorno del proceso hijo: nunca en argv,
// nunca en disco ni en logs.
public static class GitAuthEnvironment
{
    public const string ExtraHeaderKey = "http.https://dev.azure.com/.extraheader";
    private const string CountVar = "GIT_CONFIG_COUNT";

    // Subcomandos que hablan con el remoto; el resto no necesita credenciales.
    private static readonly HashSet<string> NetworkCommands = new(StringComparer.Ordinal)
    {
        "clone", "fetch", "pull", "push", "ls-remote", "submodule", "remote", "lfs",
    };

    public static void Apply(IDictionary<string, string?> env, string bearerToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        if (bearerToken.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException("Token con caracteres inválidos.", nameof(bearerToken));

        var count = env.TryGetValue(CountVar, out var raw) && int.TryParse(raw, out var n) && n >= 0 ? n : 0;
        env[$"GIT_CONFIG_KEY_{count}"] = ExtraHeaderKey;
        env[$"GIT_CONFIG_VALUE_{count}"] = "Authorization: Bearer " + bearerToken;
        env[CountVar] = (count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static bool NeedsNetwork(string? gitArgs)
    {
        if (string.IsNullOrWhiteSpace(gitArgs)) return false;
        var trimmed = gitArgs.AsSpan().TrimStart();
        var end = trimmed.IndexOf(' ');
        var first = end < 0 ? trimmed : trimmed[..end];
        return NetworkCommands.Contains(first.ToString());
    }
}
