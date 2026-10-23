namespace Rama.Core.Secrets;

public static class SecretStoreFactory
{
    // Selecciona la implementación nativa según el SO en ejecución.
    public static ISecretStore Create()
    {
        if (OperatingSystem.IsWindows()) return new WindowsCredentialStore();
        if (OperatingSystem.IsMacOS()) return new MacKeychainStore();
        if (OperatingSystem.IsLinux()) return new LinuxSecretServiceStore();
        throw new PlatformNotSupportedException("No hay almacén de secretos para este SO.");
    }

    internal static void Validate(string service, string account)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
    }
}
