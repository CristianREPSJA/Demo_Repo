using Rama.Core.Terminal;

namespace Rama.Core.Tests.Terminal;

public class ExecutableLocatorTests
{
    private static Func<string, bool> Files(params string[] paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    [Fact]
    public void Find_Windows_PrefersPathOrderAndPathExt()
    {
        var locator = new ExecutableLocator(Files(@"C:\b\claude.cmd", @"C:\c\claude.exe"), isWindows: true);
        var found = locator.Find("claude", @"C:\a;C:\b;C:\c", ".COM;.EXE;.BAT;.CMD");
        Assert.Equal(@"C:\b\claude.cmd", found);
    }

    [Fact]
    public void Find_Windows_ExeBeforeCmdInSameDirectory()
    {
        var locator = new ExecutableLocator(Files(@"C:\b\claude.cmd", @"C:\b\claude.exe"), isWindows: true);
        Assert.Equal(@"C:\b\claude.exe", locator.Find("claude", @"C:\b", ".EXE;.CMD"));
    }

    [Fact]
    public void Find_Windows_DefaultPathExtWhenMissing()
    {
        var locator = new ExecutableLocator(Files(@"C:\b\claude.exe"), isWindows: true);
        Assert.Equal(@"C:\b\claude.exe", locator.Find("claude", @"C:\b", null));
    }

    [Fact]
    public void Find_Windows_IgnoresBlankAndQuotedEntries()
    {
        var locator = new ExecutableLocator(Files(@"C:\Program Files\x\claude.exe"), isWindows: true);
        Assert.Equal(@"C:\Program Files\x\claude.exe", locator.Find("claude", @";;""C:\Program Files\x"";", ".EXE"));
    }

    [Fact]
    public void Find_Unix_UsesBareName()
    {
        var locator = new ExecutableLocator(Files("/usr/local/bin/claude"), isWindows: false);
        Assert.Equal("/usr/local/bin/claude", locator.Find("claude", "/usr/bin:/usr/local/bin", null));
    }

    [Fact]
    public void Find_NotFound_ReturnsNull()
    {
        var locator = new ExecutableLocator(Files(), isWindows: false);
        Assert.Null(locator.Find("claude", "/usr/bin", null));
        Assert.Null(locator.Find("claude", null, null));
    }

    [Fact]
    public void Find_InvalidName_Throws()
    {
        var locator = new ExecutableLocator(Files(), isWindows: false);
        Assert.Throws<ArgumentException>(() => locator.Find(" ", "/usr/bin", null));
    }

    [Fact]
    public void ClaudeLocator_FindsOnPath()
    {
        var locator = new ExecutableLocator(Files(@"C:\u\.local\bin\claude.exe"), isWindows: true);
        var env = new Dictionary<string, string> { ["Path"] = @"C:\u\.local\bin", ["PATHEXT"] = ".EXE" };
        Assert.Equal(@"C:\u\.local\bin\claude.exe", ClaudeLocator.Find(env, locator));
    }

    [Fact]
    public void ClaudeLocator_MissingReturnsNull()
    {
        var locator = new ExecutableLocator(Files(), isWindows: true);
        Assert.Null(ClaudeLocator.Find(new Dictionary<string, string>(), locator));
    }

    [Fact]
    public void ClaudeLocator_NotFoundMessageIsSpanish()
    {
        Assert.Equal("No se encontró el comando claude. Instala Claude Code y vuelve a abrir la terminal.", ClaudeLocator.NotFoundMessage);
    }

    [Fact]
    public void ShellResolver_Windows_PrefersPwsh()
    {
        var locator = new ExecutableLocator(Files(@"C:\p\pwsh.exe", @"C:\w\powershell.exe"), isWindows: true);
        var env = new Dictionary<string, string> { ["PATH"] = @"C:\p;C:\w", ["PATHEXT"] = ".EXE" };
        Assert.Equal(@"C:\p\pwsh.exe", ShellResolver.Resolve(env, locator, isWindows: true));
    }

    [Fact]
    public void ShellResolver_Windows_FallsBackToPowershell()
    {
        var locator = new ExecutableLocator(Files(), isWindows: true);
        Assert.Equal(SystemTools.PowerShellExe, ShellResolver.Resolve(new Dictionary<string, string>(), locator, isWindows: true));
    }

    [Fact]
    public void ShellResolver_Unix_UsesShellVariable()
    {
        var locator = new ExecutableLocator(Files(), isWindows: false);
        var env = new Dictionary<string, string> { ["SHELL"] = "/bin/zsh" };
        Assert.Equal("/bin/zsh", ShellResolver.Resolve(env, locator, isWindows: false));
        Assert.Equal("/bin/bash", ShellResolver.Resolve(new Dictionary<string, string>(), locator, isWindows: false));
    }

    [Theory]
    [InlineData(@"C:\x\claude.cmd", true, "<cmd>", "/d|/c|\"C:\\x\\claude.cmd\"")]
    [InlineData(@"C:\x y\claude.BAT", true, "<cmd>", "/d|/c|\"C:\\x y\\claude.BAT\"")]
    [InlineData(@"C:\x\claude.exe", true, @"C:\x\claude.exe", "")]
    [InlineData("/usr/bin/claude", false, "/usr/bin/claude", "")]
    public void LaunchCommand_WrapsBatchFilesOnWindows(string path, bool isWindows, string app, string args)
    {
        var cmd = LaunchCommand.For(path, isWindows);
        Assert.Equal(app == "<cmd>" ? SystemTools.CmdExe : app, cmd.App);
        Assert.Equal(args, string.Join("|", cmd.Args));
    }
}
