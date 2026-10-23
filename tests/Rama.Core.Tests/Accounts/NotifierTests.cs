using Rama.Core.Notifications;

namespace Rama.Core.Tests.Accounts;

public class NotifierTests
{
    private sealed class FakeLauncher(bool onPath = true) : IProcessLauncher
    {
        public List<(string Exe, IReadOnlyList<string> Args)> Runs { get; } = [];
        public bool IsOnPath(string executable) => onPath;

        public Task RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken ct = default)
        {
            Runs.Add((executable, arguments));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingNotifier : INotifier
    {
        public Task NotifyAsync(NotificationMessage message, CancellationToken ct = default) =>
            throw new InvalidOperationException("fallo");
    }

    [Fact]
    public async Task Mac_PassesTextAsSeparateArgsNotInScript()
    {
        var l = new FakeLauncher();
        var evil = "\" & do shell script \"rm -rf ~\" & \"";
        await new MacNotifier(l).NotifyAsync(new("1", "T\"itle\\", evil));
        var (exe, args) = Assert.Single(l.Runs);
        Assert.Equal("osascript", exe);
        Assert.Equal(evil, args[^2]);
        Assert.Equal("T\"itle\\", args[^1]);
        var script = string.Join("\n", args.Take(args.Count - 2));
        Assert.DoesNotContain("rm -rf", script);
        Assert.Contains("item 1 of argv", script);
    }

    [Fact]
    public void Mac_NeutralizesLeadingDashAndControlChars()
    {
        var args = MacNotifier.BuildArguments(new("1", "-t\nx", "-e\tboom\u0007"));
        Assert.Equal(" -e boom", args[^2]);
        Assert.Equal(" -t x", args[^1]);
    }

    [Fact]
    public void LongTextIsTruncated()
    {
        var args = MacNotifier.BuildArguments(new("1", new string('a', 1000), new string('b', 1000)));
        Assert.Equal(400, args[^2].Length);
        Assert.Equal(120, args[^1].Length);
    }

    [Fact]
    public async Task Linux_UsesSeparatorAndEscapesMarkup()
    {
        var l = new FakeLauncher();
        await new LinuxNotifier(l).NotifyAsync(new("1", "PR <b>", "a & b --help"));
        var (exe, args) = Assert.Single(l.Runs);
        Assert.Equal("notify-send", exe);
        Assert.Equal(["--app-name=Rama", "--", "PR &lt;b&gt;", "a &amp; b --help"], args);
    }

    [Fact]
    public async Task Linux_SkipsWhenNotifySendMissing()
    {
        var l = new FakeLauncher(onPath: false);
        await new LinuxNotifier(l).NotifyAsync(new("1", "t", "b"));
        Assert.Empty(l.Runs);
    }

    [Fact]
    public async Task InApp_KeepsRecentBoundedAndRaisesEvent()
    {
        var n = new InAppNotifier(2);
        var seen = new List<string>();
        n.Notified += m => seen.Add(m.Id);
        foreach (var id in new[] { "a", "b", "c" }) await n.NotifyAsync(new(id, "t", "b"));
        Assert.Equal(["c", "b"], n.Recent.Select(m => m.Id));
        Assert.Equal(["a", "b", "c"], seen);
    }

    [Fact]
    public async Task Composite_ContinuesAfterFailure()
    {
        var inApp = new InAppNotifier();
        await new CompositeNotifier(new ThrowingNotifier(), inApp).NotifyAsync(new("x", "t", "b"));
        Assert.Single(inApp.Recent);
    }

    [Fact]
    public async Task Factory_DeliversToInAppOnAnyPlatform()
    {
        var inApp = new InAppNotifier();
        await NotifierFactory.Create(inApp, new FakeLauncher()).NotifyAsync(new("x", "t", "b"));
        Assert.Single(inApp.Recent);
    }

    [Fact]
    public void ProcessLauncher_MissingExecutableIsNotOnPath() =>
        Assert.False(new ProcessLauncher().IsOnPath("definitely-not-a-real-binary-xyz"));
}
