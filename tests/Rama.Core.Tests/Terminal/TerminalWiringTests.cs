using Rama.Core.Terminal;

namespace Rama.Core.Tests.Terminal;

public class ContextPromptsTests
{
    private static TerminalContext Ctx(string[]? selected = null, string[]? staged = null, string? ticket = null, string? baseBranch = null) =>
        new(selected ?? [], staged ?? [], ticket, baseBranch);

    [Fact]
    public void Explain_RequiresSelectedFiles()
    {
        Assert.False(ContextPrompts.IsAvailable(ContextAction.ExplainChanges, Ctx()));
        Assert.Null(ContextPrompts.Build(ContextAction.ExplainChanges, Ctx(selected: [" "])));
        Assert.Equal("Explica los cambios seleccionados en: a.cs",
            ContextPrompts.Build(ContextAction.ExplainChanges, Ctx(selected: ["a.cs"])));
    }

    [Fact]
    public void CommitMessage_UsesStagedFilesAndTicket()
    {
        var prompt = ContextPrompts.Build(ContextAction.CommitMessage, Ctx(staged: ["a.cs", "b.cs"], ticket: "VEN-1"));
        Assert.Equal("Redacta un mensaje de commit en español para los cambios preparados (git diff --staged: a.cs, b.cs), formato: VEN-1 <resumen>", prompt);
    }

    [Fact]
    public void CommitMessage_AlwaysAvailable_GenericWithoutStaged()
    {
        Assert.True(ContextPrompts.IsAvailable(ContextAction.CommitMessage, TerminalContext.Empty));
        Assert.Contains("(git diff --staged)", ContextPrompts.Build(ContextAction.CommitMessage, TerminalContext.Empty));
    }

    [Fact]
    public void Review_NeedsBase()
    {
        Assert.Null(ContextPrompts.Build(ContextAction.ReviewBranch, Ctx()));
        Assert.Equal("Revisa la rama actual contra develop y señala riesgos",
            ContextPrompts.Build(ContextAction.ReviewBranch, Ctx(baseBranch: "develop")));
    }

    [Fact]
    public void Summarize_OnlyWithTicket()
    {
        Assert.False(ContextPrompts.IsAvailable(ContextAction.SummarizeTicket, Ctx()));
        Assert.Equal("Resume el ticket VEN-9 y su estado",
            ContextPrompts.Build(ContextAction.SummarizeTicket, Ctx(ticket: "VEN-9")));
    }

    [Fact]
    public void Prompts_NeverContainNewlines()
    {
        var ctx = Ctx(selected: ["x\ny"], staged: ["p\r\nq"], ticket: "VEN-1", baseBranch: "develop");
        foreach (var action in Enum.GetValues<ContextAction>())
        {
            var prompt = ContextPrompts.Build(action, ctx)!;
            Assert.DoesNotContain('\n', prompt);
            Assert.DoesNotContain('\r', prompt);
        }
    }
}

public class BaseBranchResolverTests
{
    [Fact]
    public void PrefersDevelopOverMain() =>
        Assert.Equal("develop", BaseBranchResolver.Resolve(["main", "develop", "feature/x"], [], "feature/x"));

    [Fact]
    public void FallsBackToDefaultBranch() =>
        Assert.Equal("main", BaseBranchResolver.Resolve(["main", "feature/x"], [], "feature/x"));

    [Fact]
    public void UsesRemoteWhenNoLocal() =>
        Assert.Equal("origin/develop", BaseBranchResolver.Resolve(["feature/x"], ["origin/develop", "origin/main"], "feature/x"));

    [Fact]
    public void SkipsCurrentBranch() =>
        Assert.Equal("main", BaseBranchResolver.Resolve(["develop", "main"], [], "develop"));

    [Fact]
    public void NullWhenNothingMatches() =>
        Assert.Null(BaseBranchResolver.Resolve(["feature/x"], ["origin/feature/x"], "feature/x"));
}

public class RepoScopedCacheTests
{
    private sealed class Item(string path) : IDisposable
    {
        public string Path { get; } = path;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void GetOrCreate_ReturnsSameInstanceForEquivalentPaths()
    {
        var created = 0;
        using var cache = new RepoScopedCache<Item>(p => { created++; return new Item(p); });
        var a = cache.GetOrCreate("C:\\repo\\a\\");
        var b = cache.GetOrCreate("C:/repo/a");
        Assert.Same(a, b);
        Assert.Equal(1, created);
        Assert.Equal("C:/repo/a", a.Path);
    }

    [Fact]
    public void Release_DisposesOnlyThatRepo()
    {
        using var cache = new RepoScopedCache<Item>(p => new Item(p));
        var a = cache.GetOrCreate("/a");
        var b = cache.GetOrCreate("/b");
        Assert.True(cache.Release("/a"));
        Assert.True(a.Disposed);
        Assert.False(b.Disposed);
        Assert.Equal(1, cache.Count);
        Assert.False(cache.Release("/a"));
        Assert.Null(cache.Peek("/a"));
    }

    [Fact]
    public void ReleaseThenGet_CreatesFreshInstance()
    {
        using var cache = new RepoScopedCache<Item>(p => new Item(p));
        var first = cache.GetOrCreate("/a");
        cache.Release("/a");
        Assert.NotSame(first, cache.GetOrCreate("/a"));
    }

    [Fact]
    public void Dispose_DisposesAll()
    {
        var cache = new RepoScopedCache<Item>(p => new Item(p));
        var a = cache.GetOrCreate("/a");
        var b = cache.GetOrCreate("/b");
        cache.Dispose();
        Assert.True(a.Disposed && b.Disposed);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void IgnoreCase_MergesPathsDifferingByCase()
    {
        using var cache = new RepoScopedCache<Item>(p => new Item(p), ignoreCase: true);
        Assert.Same(cache.GetOrCreate("C:/Repo"), cache.GetOrCreate("c:/repo"));
    }

    [Fact]
    public void RejectsBlankPath()
    {
        using var cache = new RepoScopedCache<Item>(p => new Item(p));
        Assert.Throws<ArgumentException>(() => cache.GetOrCreate(" "));
    }
}

public class ViewLeaseTests
{
    [Fact]
    public void Acquire_RevokesPreviousOwner()
    {
        var lease = new ViewLease();
        var a = new object();
        var b = new object();
        var revokedA = 0;
        lease.Acquire(a, () => revokedA++);
        lease.Acquire(b, () => { });
        Assert.Equal(1, revokedA);
        Assert.Same(b, lease.Owner);
    }

    [Fact]
    public void ReAcquireBySameOwner_DoesNotRevoke()
    {
        var lease = new ViewLease();
        var a = new object();
        var revoked = 0;
        lease.Acquire(a, () => revoked++);
        lease.Acquire(a, () => revoked++);
        Assert.Equal(0, revoked);
    }

    [Fact]
    public void Release_OnlyByOwner()
    {
        var lease = new ViewLease();
        var a = new object();
        lease.Acquire(a, () => { });
        Assert.False(lease.Release(new object()));
        Assert.Same(a, lease.Owner);
        Assert.True(lease.Release(a));
        Assert.Null(lease.Owner);
    }
}
