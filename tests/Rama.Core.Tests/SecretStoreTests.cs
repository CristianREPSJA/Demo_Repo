using Rama.Core.Secrets;

namespace Rama.Core.Tests;

public class SecretStoreTests
{
    [Fact]
    public async Task InMemory_RoundTripAndDelete()
    {
        var s = new InMemorySecretStore();
        Assert.Null(await s.GetAsync("svc", "acc"));
        await s.SetAsync("svc", "acc", "v1");
        await s.SetAsync("svc", "acc", "v2");
        Assert.Equal("v2", await s.GetAsync("svc", "acc"));
        await s.DeleteAsync("svc", "acc");
        Assert.Null(await s.GetAsync("svc", "acc"));
    }

    [Theory]
    [InlineData("", "a")]
    [InlineData("s", " ")]
    public async Task InMemory_RejectsEmptyKeys(string svc, string acc)
    {
        var s = new InMemorySecretStore();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => s.GetAsync(svc, acc));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => s.SetAsync(svc, acc, "x"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => s.DeleteAsync(svc, acc));
    }

    [Fact]
    public void Factory_PicksPlatformStore()
    {
        var s = SecretStoreFactory.Create();
        if (OperatingSystem.IsWindows()) Assert.IsType<WindowsCredentialStore>(s);
        else if (OperatingSystem.IsMacOS()) Assert.IsType<MacKeychainStore>(s);
        else if (OperatingSystem.IsLinux()) Assert.IsType<LinuxSecretServiceStore>(s);
    }

    [Fact]
    public async Task Windows_RealCredentialManagerRoundTrip()
    {
        // Toca el llavero real: solo con RAMA_TEST_REAL_KEYCHAIN=1 (evita alertas del antivirus en cada build).
        if (!OperatingSystem.IsWindows() || !RealKeychainEnabled) return;
        ISecretStore store = new WindowsCredentialStore();
        var svc = "rama-test-" + Guid.NewGuid().ToString("N");
        const string acc = "user@example.com";
        try
        {
            Assert.Null(await store.GetAsync(svc, acc));
            await store.SetAsync(svc, acc, "pät-ñ-secret-1");
            Assert.Equal("pät-ñ-secret-1", await store.GetAsync(svc, acc));
            await store.SetAsync(svc, acc, "");
            Assert.Equal("", await store.GetAsync(svc, acc));
            await store.SetAsync(svc, acc, "second");
            Assert.Equal("second", await store.GetAsync(svc, acc));
        }
        finally
        {
            await store.DeleteAsync(svc, acc);
        }
        Assert.Null(await store.GetAsync(svc, acc));
        await store.DeleteAsync(svc, acc); // borrar inexistente no falla
    }

    [Fact]
    public async Task Windows_RejectsOversizedSecret()
    {
        if (!OperatingSystem.IsWindows() || !RealKeychainEnabled) return;
        ISecretStore store = new WindowsCredentialStore();
        await Assert.ThrowsAsync<SecretStoreException>(() => store.SetAsync("x", "y", new string('a', 4000)));
    }

    private static bool RealKeychainEnabled =>
        Environment.GetEnvironmentVariable("RAMA_TEST_REAL_KEYCHAIN") == "1";
}
