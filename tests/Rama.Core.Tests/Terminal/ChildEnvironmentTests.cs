using Rama.Core.Terminal;

namespace Rama.Core.Tests.Terminal;

public class ChildEnvironmentTests
{
    private static Dictionary<string, string> Sample() => new()
    {
        ["PATH"] = "/usr/bin",
        ["HOME"] = "/home/u",
        ["NO_COLOR"] = "1",
        ["CLAUDECODE"] = "1",
        ["CLAUDE_CODE_ENTRYPOINT"] = "cli",
        ["claude_pid"] = "42",
        ["SOURCEGIT_LAUNCH_AS_ASKPASS"] = "TRUE",
        ["TERM"] = "dumb",
        ["LANG"] = "C",
    };

    [Fact]
    public void Sanitize_RemovesBlockedVariables()
    {
        var result = ChildEnvironment.Sanitize(Sample(), isWindows: false);
        Assert.DoesNotContain("NO_COLOR", result.Keys);
        Assert.DoesNotContain("CLAUDECODE", result.Keys);
        Assert.DoesNotContain("CLAUDE_CODE_ENTRYPOINT", result.Keys);
        Assert.DoesNotContain("claude_pid", result.Keys);
        Assert.DoesNotContain("SOURCEGIT_LAUNCH_AS_ASKPASS", result.Keys);
        Assert.Equal("/usr/bin", result["PATH"]);
        Assert.Equal("/home/u", result["HOME"]);
    }

    [Fact]
    public void Sanitize_SetsTerminalCapabilities()
    {
        var result = ChildEnvironment.Sanitize(Sample(), isWindows: true);
        Assert.Equal("xterm-256color", result["TERM"]);
        Assert.Equal("truecolor", result["COLORTERM"]);
    }

    [Fact]
    public void Sanitize_Unix_ForcesUtf8Locale()
    {
        var result = ChildEnvironment.Sanitize(Sample(), isWindows: false);
        Assert.Equal("en_US.UTF-8", result["LANG"]);
        Assert.Equal("en_US.UTF-8", result["LC_ALL"]);
    }

    [Fact]
    public void Sanitize_Unix_KeepsExistingUtf8Locale()
    {
        var env = new Dictionary<string, string> { ["LANG"] = "es_CL.UTF-8" };
        var result = ChildEnvironment.Sanitize(env, isWindows: false);
        Assert.Equal("es_CL.UTF-8", result["LANG"]);
        Assert.Equal("es_CL.UTF-8", result["LC_ALL"]);
    }

    [Fact]
    public void Sanitize_Windows_DoesNotTouchLocale()
    {
        var result = ChildEnvironment.Sanitize(Sample(), isWindows: true);
        Assert.Equal("C", result["LANG"]);
        Assert.DoesNotContain("LC_ALL", result.Keys);
    }

    [Fact]
    public void Sanitize_DoesNotMutateInput()
    {
        var input = Sample();
        _ = ChildEnvironment.Sanitize(input, isWindows: false);
        Assert.Equal("1", input["NO_COLOR"]);
        Assert.Equal("dumb", input["TERM"]);
    }

    [Fact]
    public void Sanitize_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ChildEnvironment.Sanitize(null!));
    }

    [Theory]
    [InlineData("NO_COLOR", true)]
    [InlineData("no_color", true)]
    [InlineData("CLAUDECODE", true)]
    [InlineData("CLAUDE_CONFIG_DIR", true)]
    [InlineData("SOURCEGIT_X", true)]
    [InlineData("CLAUDEX", false)]
    [InlineData("PATH", false)]
    public void IsBlocked_MatchesRules(string key, bool expected)
    {
        Assert.Equal(expected, ChildEnvironment.IsBlocked(key));
    }

    [Fact]
    public void Diff_ComputesSetsAndRemovals()
    {
        var current = new Dictionary<string, string> { ["A"] = "1", ["B"] = "2", ["C"] = "3" };
        var desired = new Dictionary<string, string> { ["A"] = "1", ["B"] = "9", ["D"] = "4" };
        var diff = ChildEnvironment.Diff(current, desired).ToDictionary(c => c.Key, c => c.Value);
        Assert.Equal(3, diff.Count);
        Assert.Equal("9", diff["B"]);
        Assert.Null(diff["C"]);
        Assert.Equal("4", diff["D"]);
    }

    [Fact]
    public void FromProcess_ReturnsCurrentVariables()
    {
        var env = ChildEnvironment.FromProcess();
        Assert.True(env.ContainsKey("PATH") || env.ContainsKey("Path"));
    }
}
