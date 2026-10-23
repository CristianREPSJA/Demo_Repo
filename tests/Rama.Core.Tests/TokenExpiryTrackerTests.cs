using Rama.Core.Integrations;
using Rama.Core.Secrets;
using Rama.Core.Tests.Support;

namespace Rama.Core.Tests;

public class TokenExpiryTrackerTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    [Theory]
    [InlineData(60, TokenExpiryState.Ok)]
    [InlineData(30, TokenExpiryState.Ok)]
    [InlineData(29, TokenExpiryState.ExpiringSoon)]
    [InlineData(0, TokenExpiryState.ExpiringSoon)]
    [InlineData(-1, TokenExpiryState.Expired)]
    public void Evaluate_ClassifiesByDaysLeft(int days, TokenExpiryState expected)
    {
        var s = TokenExpiryTracker.Evaluate(Today.AddDays(days), Today);
        Assert.Equal(expected, s.State);
        Assert.Equal(days, s.DaysLeft);
    }

    [Fact]
    public void Evaluate_NoDate_IsUnknown() =>
        Assert.Equal(new TokenExpiryStatus(TokenExpiryState.Unknown, null), TokenExpiryTracker.Evaluate(null, Today));

    [Fact]
    public async Task StoresAndReportsStatus_PerServiceAndAccount()
    {
        var time = new FakeTimeProvider();
        var tracker = new TokenExpiryTracker(new InMemorySecretStore(), time);
        Assert.Equal(TokenExpiryState.Unknown, (await tracker.GetStatusAsync("jira", "a")).State);

        var soon = DateOnly.FromDateTime(time.GetLocalNow().DateTime).AddDays(20);
        await tracker.SetExpiryAsync("jira", "a", soon);
        Assert.Equal(soon, await tracker.GetExpiryAsync("jira", "a"));
        Assert.Null(await tracker.GetExpiryAsync("bitbucket", "a"));
        Assert.Equal(TokenExpiryState.ExpiringSoon, (await tracker.GetStatusAsync("jira", "a")).State);

        await tracker.ClearAsync("jira", "a");
        Assert.Null(await tracker.GetExpiryAsync("jira", "a"));
    }

    [Fact]
    public async Task InvalidStoredValue_IsTreatedAsUnknown()
    {
        var store = new InMemorySecretStore();
        await store.SetAsync("token-expiry:jira", "a", "basura");
        Assert.Null(await new TokenExpiryTracker(store).GetExpiryAsync("jira", "a"));
    }
}
