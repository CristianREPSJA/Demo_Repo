namespace Rama.Core.Notifications;

// macOS: osascript con el texto en argv (nunca interpolado en el script AppleScript => sin inyección).
public sealed class MacNotifier(IProcessLauncher launcher) : INotifier
{
    internal static readonly string[] ScriptArgs =
    [
        "-e", "on run argv",
        "-e", "display notification (item 1 of argv) with title (item 2 of argv)",
        "-e", "end run",
    ];

    public static IReadOnlyList<string> BuildArguments(NotificationMessage m) =>
        [.. ScriptArgs, NotificationText.Body(m.Body), NotificationText.Title(m.Title)];

    public Task NotifyAsync(NotificationMessage message, CancellationToken ct = default) =>
        launcher.RunAsync("osascript", BuildArguments(message), ct);
}
