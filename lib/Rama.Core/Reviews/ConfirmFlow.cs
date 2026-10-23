namespace Rama.Core.Reviews;

public sealed record ConfirmRequest(string Prompt, Func<CancellationToken, Task> Action);

// Confirmación inline en dos pasos: Request deja la acción pendiente; solo Confirm la ejecuta.
public sealed class ConfirmFlow
{
    public ConfirmRequest? Pending { get; private set; }
    public bool IsPending => Pending is not null;

    public void Request(string prompt, Func<CancellationToken, Task> action) =>
        Pending = new ConfirmRequest(prompt, action ?? throw new ArgumentNullException(nameof(action)));

    public void Cancel() => Pending = null;

    // Devuelve false si no había nada pendiente. La solicitud se limpia aunque la acción falle.
    public async Task<bool> ConfirmAsync(CancellationToken ct = default)
    {
        var req = Pending;
        if (req is null) return false;
        Pending = null;
        await req.Action(ct).ConfigureAwait(false);
        return true;
    }
}
