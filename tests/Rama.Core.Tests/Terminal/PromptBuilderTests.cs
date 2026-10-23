using Rama.Core.Terminal;

namespace Rama.Core.Tests.Terminal;

public class PromptBuilderTests
{
    [Fact]
    public void ExplainChanges_ListsFiles()
    {
        var prompt = PromptBuilder.ExplainChanges(["src/A.cs", "src/B.cs"]);
        Assert.Equal("Explica los cambios seleccionados en: src/A.cs, src/B.cs", prompt);
    }

    [Fact]
    public void ExplainChanges_CapsLongLists()
    {
        var files = Enumerable.Range(1, PromptBuilder.MaxFiles + 3).Select(i => $"f{i}.cs").ToList();
        var prompt = PromptBuilder.ExplainChanges(files);
        Assert.EndsWith("y 3 más", prompt);
        Assert.DoesNotContain($"f{PromptBuilder.MaxFiles + 1}.cs", prompt);
    }

    [Fact]
    public void ExplainChanges_SkipsBlankAndRequiresFiles()
    {
        Assert.Equal("Explica los cambios seleccionados en: a", PromptBuilder.ExplainChanges(["", "a", "  "]));
        Assert.Throws<ArgumentException>(() => PromptBuilder.ExplainChanges([]));
        Assert.Throws<ArgumentNullException>(() => PromptBuilder.ExplainChanges(null!));
    }

    [Fact]
    public void CommitMessage_UsesTicket()
    {
        Assert.Equal(
            "Redacta un mensaje de commit en español para los cambios preparados (git diff --staged), formato: VEN-412 <resumen>",
            PromptBuilder.CommitMessage("VEN-412"));
    }

    [Fact]
    public void CommitMessage_WithoutTicket_OmitsIt()
    {
        Assert.Equal(
            "Redacta un mensaje de commit en español para los cambios preparados (git diff --staged), formato: <resumen>",
            PromptBuilder.CommitMessage(null));
    }

    [Fact]
    public void ReviewBranch_UsesBase()
    {
        Assert.Equal("Revisa la rama actual contra develop y señala riesgos", PromptBuilder.ReviewBranch("develop"));
        Assert.Throws<ArgumentException>(() => PromptBuilder.ReviewBranch(" "));
    }

    [Fact]
    public void SummarizeTicket_UsesKey()
    {
        Assert.Equal("Resume el ticket VEN-412 y su estado", PromptBuilder.SummarizeTicket("VEN-412"));
        Assert.Throws<ArgumentException>(() => PromptBuilder.SummarizeTicket(""));
    }

    [Theory]
    [InlineData("a\rb", "a b")]
    [InlineData("a\nb", "a b")]
    [InlineData("a\r\nb", "a b")]
    [InlineData("a\u001b[31mb", "a[31mb")]
    [InlineData("a\u0007\u0000b", "ab")]
    [InlineData("a‮b", "ab")]
    [InlineData("a\u009bb", "ab")]
    [InlineData("a\tb", "a b")]
    [InlineData("  ñandú ─│┌┐  ", "ñandú ─│┌┐")]
    public void Sanitize_StripsControlCharacters(string input, string expected)
    {
        Assert.Equal(expected, PromptBuilder.Sanitize(input));
    }

    [Fact]
    public void Prompts_NeverContainNewlinesOrEscape()
    {
        var evil = "x\r\n\u001b[201~rm -rf /\r";
        var prompts = new[]
        {
            PromptBuilder.ExplainChanges([evil]),
            PromptBuilder.CommitMessage(evil),
            PromptBuilder.ReviewBranch(evil),
            PromptBuilder.SummarizeTicket(evil),
        };
        foreach (var p in prompts)
        {
            Assert.DoesNotContain('\r', p);
            Assert.DoesNotContain('\n', p);
            Assert.DoesNotContain('\u001b', p);
        }
    }

    [Fact]
    public void Sanitize_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, PromptBuilder.Sanitize(null));
    }
}
