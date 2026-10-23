namespace Rama.Core.Notifications;

// Notificador dentro de la app (Windows: sin toast nativo para no registrar un AUMID en el sistema).
// La UI se suscribe a Notified para mostrar insignia/panel; Recent guarda las últimas.
public sealed class InAppNotifier(int capacity = 50) : INotifier
{
    private readonly object _lock = new();
    private readonly List<NotificationMessage> _recent = [];

    public event Action<NotificationMessage>? Notified;

    public IReadOnlyList<NotificationMessage> Recent
    {
        get { lock (_lock) return [.. _recent]; }
    }

    public Task NotifyAsync(NotificationMessage message, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _recent.Insert(0, message);
            if (_recent.Count > capacity) _recent.RemoveRange(capacity, _recent.Count - capacity);
        }
        Notified?.Invoke(message);
        return Task.CompletedTask;
    }
}

// Envía a varios notificadores; el fallo de uno no impide los demás.
public sealed class CompositeNotifier(params INotifier[] notifiers) : INotifier
{
    public async Task NotifyAsync(NotificationMessage message, CancellationToken ct = default)
    {
        foreach (var n in notifiers)
        {
            try { await n.NotifyAsync(message, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }
    }
}
