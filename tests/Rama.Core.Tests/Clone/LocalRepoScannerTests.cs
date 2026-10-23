using Rama.Core.Clone;

namespace Rama.Core.Tests.Clone;

public sealed class LocalRepoScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rama-scan-" + Guid.NewGuid().ToString("N"));

    public LocalRepoScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private string MakeRepo(string name, string url)
    {
        var repo = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        File.WriteAllText(Path.Combine(repo, ".git", "config"), $"[remote \"origin\"]\n\turl = {url}\n");
        return repo;
    }

    [Fact]
    public void BuildIndex_MapsNormalizedUrlsToPaths_AndSkipsNonRepos()
    {
        var a = MakeRepo("a", "git@bitbucket.org:WS/Repo.git");
        var none = Path.Combine(_root, "vacio");
        Directory.CreateDirectory(none);

        var index = LocalRepoScanner.BuildIndex([a, none, Path.Combine(_root, "no-existe")]);

        Assert.Equal(a, index["bitbucket.org/ws/repo"]);
        Assert.Single(index);
    }

    [Fact]
    public void ReadRemoteUrls_FollowsGitdirFileAndCommondir()
    {
        var main = MakeRepo("main", "https://h/o/r.git");
        var wtGit = Path.Combine(main, ".git", "worktrees", "wt");
        Directory.CreateDirectory(wtGit);
        File.WriteAllText(Path.Combine(wtGit, "commondir"), "../..");
        var wt = Path.Combine(_root, "wt");
        Directory.CreateDirectory(wt);
        File.WriteAllText(Path.Combine(wt, ".git"), "gitdir: " + wtGit + "\n");

        Assert.Equal(["https://h/o/r.git"], LocalRepoScanner.ReadRemoteUrls(wt));
    }

    [Fact]
    public void ReadRemoteUrls_GitFileWithoutGitdir_ReturnsEmpty()
    {
        var odd = Path.Combine(_root, "odd");
        Directory.CreateDirectory(odd);
        File.WriteAllText(Path.Combine(odd, ".git"), "basura");
        Assert.Empty(LocalRepoScanner.ReadRemoteUrls(odd));
    }
}
