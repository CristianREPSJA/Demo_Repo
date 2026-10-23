namespace Rama.Core.Terminal;

// Un panel de terminal solo puede estar en una vista a la vez: quien lo adquiere desaloja al anterior.
public sealed class ViewLease
{
    private object? _owner;
    private Action? _onRevoked;

    public object? Owner => _owner;

    public void Acquire(object owner, Action onRevoked)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(onRevoked);
        if (ReferenceEquals(_owner, owner))
        {
            _onRevoked = onRevoked;
            return;
        }
        var previous = _onRevoked;
        _owner = owner;
        _onRevoked = onRevoked;
        previous?.Invoke();
    }

    // Solo suelta si el llamador es el dueño actual; devuelve si soltó.
    public bool Release(object owner)
    {
        if (!ReferenceEquals(_owner, owner))
            return false;
        _owner = null;
        _onRevoked = null;
        return true;
    }
}
