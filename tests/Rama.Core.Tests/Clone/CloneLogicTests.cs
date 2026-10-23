using Rama.Core.Azure;
using Rama.Core.Bitbucket;
using Rama.Core.Clone;

namespace Rama.Core.Tests.Clone;

public class CloneLogicTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.FromHours(-3));

    private static CloneRepoItem Item(string name, string project = "ERP", string? desc = null) =>
        new(CloneProvider.Azure, name, desc, project, "main", null, null, "https://x/" + name);

    [Fact]
    public void Filter_BySearchAndProject_IsCaseInsensitiveAndSorted()
    {
        var items = new[] { Item("zeta", "Portal"), Item("Erp-Ventas"), Item("alfa", "ERP", "Facturación SII") };
        Assert.Equal(["alfa", "Erp-Ventas"], CloneListLogic.Filter(items, "", "erp").Select(i => i.Name));
        Assert.Equal(["alfa"], CloneListLogic.Filter(items, "sii", null).Select(i => i.Name));
        Assert.Equal(3, CloneListLogic.Filter(items, "  ", "").Count);
        Assert.Empty(CloneListLogic.Filter(items, "nada", null));
    }

    [Fact]
    public void Projects_AreDistinctSortedAndSkipEmpty()
    {
        var items = new[] { Item("a", "Portal"), Item("b", "erp"), Item("c", "ERP"), Item("d", "") };
        Assert.Equal(["erp", "Portal"], CloneListLogic.Projects(items));
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData(0L, "—")]
    [InlineData(1048576L, "1,0 MB")]
    [InlineData(61445000L, "58,6 MB")]
    public void FormatSize_UsesCommaAndDash(long? bytes, string expected) =>
        Assert.Equal(expected, CloneListLogic.FormatSize(bytes));

    [Fact]
    public void FormatUpdated_CoversTodayYesterdayDateAndUnknown()
    {
        Assert.Equal("—", CloneListLogic.FormatUpdated(null, Now));
        Assert.Equal("Hoy 11:02", CloneListLogic.FormatUpdated(new DateTimeOffset(2026, 10, 22, 11, 2, 0, Now.Offset), Now));
        Assert.Equal("Ayer", CloneListLogic.FormatUpdated(new DateTimeOffset(2026, 10, 21, 23, 0, 0, Now.Offset), Now));
        Assert.Equal("3 oct", CloneListLogic.FormatUpdated(new DateTimeOffset(2026, 10, 3, 9, 0, 0, Now.Offset), Now));
        Assert.Equal("3 dic 2025", CloneListLogic.FormatUpdated(new DateTimeOffset(2025, 12, 3, 9, 0, 0, Now.Offset), Now));
        // 02:00 UTC del 22 es 23:00 del 21 en -03:00
        Assert.Equal("Ayer", CloneListLogic.FormatUpdated(new DateTimeOffset(2026, 10, 22, 2, 0, 0, TimeSpan.Zero), Now));
    }

    [Theory]
    [InlineData("https://dev.azure.com/Org/Proj/_git/Repo", "https://org@dev.azure.com/org/proj/_git/repo.git/")]
    [InlineData("https://bitbucket.org/ws/repo", "git@bitbucket.org:WS/Repo.git")]
    [InlineData("https://bitbucket.org/ws/repo", "ssh://git@bitbucket.org/ws/repo.git")]
    [InlineData("https://dev.azure.com/org/proj/_git/repo", "git@ssh.dev.azure.com:v3/org/proj/repo")]
    [InlineData("https://dev.azure.com/org/My%20Proj/_git/repo", "https://dev.azure.com/org/My Proj/_git/repo")]
    public void AreSame_NormalizesSchemesUsersSuffixAndCase(string a, string b) =>
        Assert.True(RemoteUrlMatcher.AreSame(a, b));

    [Fact]
    public void AreSame_DifferentReposOrEmpty_AreNotSame()
    {
        Assert.False(RemoteUrlMatcher.AreSame("https://bitbucket.org/ws/a", "https://bitbucket.org/ws/b"));
        Assert.False(RemoteUrlMatcher.AreSame("", ""));
        Assert.False(RemoteUrlMatcher.AreSame("no es url", "no es url"));
        Assert.Equal("", RemoteUrlMatcher.Normalize(null));
    }

    [Fact]
    public void ParseRemoteUrls_ReadsOnlyRemoteSections()
    {
        const string cfg = "[core]\n\turl = nope\n[remote \"origin\"]\n\turl = https://a/b.git\n\tfetch = +refs\n[branch \"m\"]\n\tremote = origin\n[remote \"up\"]\n\turl = git@h:o/r.git\n";
        Assert.Equal(["https://a/b.git", "git@h:o/r.git"], RemoteUrlMatcher.ParseRemoteUrls(cfg));
        Assert.Empty(RemoteUrlMatcher.ParseRemoteUrls(null));
        var set = RemoteUrlMatcher.NormalizedSet(["https://A/b.git", "basura"]);
        Assert.Contains("a/b", set);
        Assert.Single(set);
    }

    [Fact]
    public void Destination_DefaultSplitAndNames()
    {
        Assert.Equal(@"C:\dev\repo", CloneDestination.Default(@"C:\dev\", "repo"));
        Assert.Equal("/home/x/repo", CloneDestination.Default("/home/x", "repo"));
        Assert.Equal("repo", CloneDestination.Default(null, "repo"));
        Assert.Equal("a-b", CloneDestination.Sanitize("a/b"));
        Assert.Equal("repo", CloneDestination.Sanitize(" "));
        Assert.Equal("proj", CloneDestination.NameFromUrl("https://h/o/proj.git"));
        Assert.Equal("proj", CloneDestination.NameFromUrl("git@h:o/proj/"));
        Assert.Equal(("C:\\dev", "repo"), CloneDestination.Split(@"C:\dev\repo\"));
        Assert.Equal(("", "repo"), CloneDestination.Split("repo"));
        Assert.Equal(("/", "repo"), CloneDestination.Split("/repo"));
    }

    [Fact]
    public void GitProgress_ParsesPhasesAndIgnoresNoise()
    {
        var p = GitProgressParser.Parse("Receiving objects:  62% (18/29), 18.40 MiB | 5.00 MiB/s");
        Assert.Equal(new GitProgress("Recibiendo objetos", 62, "18.40 MiB"), p);
        Assert.Equal(new GitProgress("Resolviendo deltas", 100, null), GitProgressParser.Parse("Resolving deltas: 100% (5/5), done."));
        Assert.Equal("Contando objetos", GitProgressParser.Parse("remote: Counting objects:  10% (1/10)")!.Phase);
        Assert.Null(GitProgressParser.Parse("Cloning into 'x'..."));
        Assert.Null(GitProgressParser.Parse("Foo bar: 10%"));
        Assert.Null(GitProgressParser.Parse(""));
    }

    [Fact]
    public void Items_MapFromProviders()
    {
        var az = new AzureRepo("1", "r", "refs/heads/develop", "https://dev.azure.com/o/p/_git/r", 2097152, null, "pid");
        var a = CloneRepoItem.From(az, "P");
        Assert.Equal("develop", a.DefaultBranch);
        Assert.Equal("P", a.Project);
        Assert.Equal("https://dev.azure.com/o/p/_git/r", a.CloneUrl);

        var bb = new BitbucketRepo("R", "r", "ws/r", "d", null, "main", null, 0, true, null);
        var b = CloneRepoItem.From(bb);
        Assert.Null(b.SizeBytes);
        Assert.Equal("https://bitbucket.org/ws/r.git", b.CloneUrl);
        Assert.Equal("", b.Project);
    }
}
