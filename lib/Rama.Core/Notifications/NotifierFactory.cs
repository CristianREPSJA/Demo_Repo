namespace Rama.Core.Notifications;

public static class NotifierFactory
{
    // Siempre incluye el notificador en-app; suma el nativo en macOS y Linux.
    public static INotifier Create(InAppNotifier inApp, IProcessLauncher? launcher = null)
    {
        launcher ??= new ProcessLauncher();
        if (OperatingSystem.IsMacOS()) return new CompositeNotifier(inApp, new MacNotifier(launcher));
        if (OperatingSystem.IsLinux()) return new CompositeNotifier(inApp, new LinuxNotifier(launcher));
        return inApp;
    }
}
