namespace Rama.Core.Terminal;

// Shell por defecto: pwsh > powershell en Windows; $SHELL > /bin/bash en unix.
public static class ShellResolver
{
    public static string Resolve(IReadOnlyDictionary<string, string> env, ExecutableLocator locator, bool isWindows)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(locator);
        if (isWindows)
            return locator.Find("pwsh", EnvLookup.Get(env, "PATH"), EnvLookup.Get(env, "PATHEXT")) ?? SystemTools.PowerShellExe;

        var shell = EnvLookup.Get(env, "SHELL");
        return string.IsNullOrWhiteSpace(shell) ? "/bin/bash" : shell;
    }
}
