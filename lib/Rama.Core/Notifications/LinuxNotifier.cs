namespace Rama.Core.Notifications;

// Linux: notify-send (libnotify) si está instalado; "--" evita que el texto se lea como opción.
public sealed class LinuxNotifier(IProcessLauncher launcher) : INotifier
{
    internal const string Executable = "notify-send";

    public static IReadOnlyList<string> BuildArguments(NotificationMessage m) =>
        ["--app-name=Rama", "--", NotificationText.XmlEscape(NotificationText.Title(m.Title)),
         NotificationText.XmlEscape(NotificationText.Body(m.Body))];

    public Task NotifyAsync(NotificationMessage message, CancellationToken ct = default) =>
        launcher.IsOnPath(Executable)
            ? launcher.RunAsync(Executable, BuildArguments(message), ct)
            : Task.CompletedTask;
}
