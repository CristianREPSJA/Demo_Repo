namespace Rama.Core.Terminal;

// Rutas absolutas a herramientas de Windows (evita que un cmd.exe/powershell.exe del cwd o PATH las reemplace).
public static class SystemTools
{
    public static string CmdExe => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    public static string PowerShellExe => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
}
