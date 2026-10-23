namespace Rama.Core.Notifications;

public sealed record NotificationMessage(string Id, string Title, string Body, string? ActionUrl = null);

public interface INotifier
{
    Task NotifyAsync(NotificationMessage message, CancellationToken ct = default);
}

public sealed class NullNotifier : INotifier
{
    public static readonly NullNotifier Instance = new();

    public Task NotifyAsync(NotificationMessage message, CancellationToken ct = default) => Task.CompletedTask;
}
