namespace Rama.Core.Terminal;

// Localiza el binario oficial `claude` en el PATH del entorno dado.
public static class ClaudeLocator
{
    public const string CommandName = "claude";
    public const string NotFoundMessage = "No se encontró el comando claude. Instala Claude Code y vuelve a abrir la terminal.";

    public static string? Find(IReadOnlyDictionary<string, string> env, ExecutableLocator locator)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(locator);
        return locator.Find(CommandName, EnvLookup.Get(env, "PATH"), EnvLookup.Get(env, "PATHEXT"));
    }
}
