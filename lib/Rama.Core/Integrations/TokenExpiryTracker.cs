using System.Globalization;
using Rama.Core.Secrets;

namespace Rama.Core.Integrations;

public enum TokenExpiryState { Unknown, Ok, ExpiringSoon, Expired }

public sealed record TokenExpiryStatus(TokenExpiryState State, int? DaysLeft);

// Las APIs no exponen el vencimiento del token: se guarda la fecha que ingresa el usuario.
// Compartido por Bitbucket y Jira. La fecha no es secreta, pero se guarda en el mismo almacén para no tocar preferencias.
public sealed class TokenExpiryTracker(ISecretStore store, TimeProvider? time = null)
{
    public const int WarningDays = 30;
    private const string ServicePrefix = "token-expiry:";
    private const string DateFormat = "yyyy-MM-dd";
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public static TokenExpiryStatus Evaluate(DateOnly? expiry, DateOnly today)
    {
        if (expiry is not { } date) return new(TokenExpiryState.Unknown, null);
        var days = date.DayNumber - today.DayNumber;
        var state = days < 0 ? TokenExpiryState.Expired
            : days < WarningDays ? TokenExpiryState.ExpiringSoon
            : TokenExpiryState.Ok;
        return new(state, days);
    }

    public Task SetExpiryAsync(string service, string account, DateOnly expiry, CancellationToken ct = default) =>
        store.SetAsync(ServicePrefix + service, account, expiry.ToString(DateFormat, CultureInfo.InvariantCulture), ct);

    public async Task<DateOnly?> GetExpiryAsync(string service, string account, CancellationToken ct = default)
    {
        var raw = await store.GetAsync(ServicePrefix + service, account, ct).ConfigureAwait(false);
        return DateOnly.TryParseExact(raw, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    public Task ClearAsync(string service, string account, CancellationToken ct = default) =>
        store.DeleteAsync(ServicePrefix + service, account, ct);

    public async Task<TokenExpiryStatus> GetStatusAsync(string service, string account, CancellationToken ct = default)
    {
        var expiry = await GetExpiryAsync(service, account, ct).ConfigureAwait(false);
        return Evaluate(expiry, DateOnly.FromDateTime(_time.GetLocalNow().DateTime));
    }
}
