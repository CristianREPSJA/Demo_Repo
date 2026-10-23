using Rama.Core.Conflicts;

namespace Rama.Core.Tests.Conflicts;

public class ResolutionComposerTests
{
    private const string ThreeRegions =
        "head\n" +
        "<<<<<<< HEAD\no1\n=======\nt1\n>>>>>>> dev\n" +
        "mid\n" +
        "<<<<<<< HEAD\no2a\no2b\n||||||| base\nb2\n=======\nt2\n>>>>>>> dev\n" +
        "<<<<<<< HEAD\no3\n=======\n>>>>>>> dev\n" +
        "tail\n";

    [Theory]
    [InlineData(ThreeRegions)]
    [InlineData("a\r\n<<<<<<< HEAD\r\nx\r\n=======\r\ny\r\n>>>>>>> b\r\nz")]
    [InlineData("<<<<<<< HEAD\nx\n=======\ny\n>>>>>>> b")]
    [InlineData("sin conflictos")]
    [InlineData("")]
    public void Compose_NoChoices_IsByteIdentical(string text)
    {
        var doc = ConflictParser.Parse(text);
        var result = ResolutionComposer.Compose(doc);
        Assert.Equal(text, result.Text);
        Assert.Equal(doc.ConflictCount, result.UnresolvedCount);
    }

    [Fact]
    public void Compose_EachKind_ProducesExpectedText()
    {
        const string text = "<<<<<<< HEAD\nO\n=======\nT\n>>>>>>> b\n";
        var doc = ConflictParser.Parse(text);
        Assert.Equal("O\n", Compose(doc, RegionResolution.Ours));
        Assert.Equal("T\n", Compose(doc, RegionResolution.Theirs));
        Assert.Equal("O\nT\n", Compose(doc, RegionResolution.BothOursFirst));
        Assert.Equal("T\nO\n", Compose(doc, RegionResolution.BothTheirsFirst));
        Assert.Equal("M\n", Compose(doc, RegionResolution.Manual("M\n")));
        Assert.Equal(text, Compose(doc, RegionResolution.Unresolved));
    }

    [Fact]
    public void Compose_MixedChoices_ThreeRegions()
    {
        var doc = ConflictParser.Parse(ThreeRegions);
        var result = ResolutionComposer.Compose(doc,
            [RegionResolution.Theirs, RegionResolution.BothOursFirst, RegionResolution.Manual("m3\n")]);
        Assert.Equal("head\nt1\nmid\no2a\no2b\nt2\nm3\ntail\n", result.Text);
        Assert.Equal(0, result.UnresolvedCount);
        Assert.Empty(ConflictParser.Parse(result.Text).Hunks);
    }

    [Fact]
    public void Compose_PartialChoices_CountsUnresolved()
    {
        var doc = ConflictParser.Parse(ThreeRegions);
        var result = ResolutionComposer.Compose(doc, [RegionResolution.Ours]);
        Assert.Equal(2, result.UnresolvedCount);
        Assert.Equal(2, ConflictParser.Parse(result.Text).ConflictCount);
    }

    [Fact]
    public void Compose_ManualWithMarkers_CountsAsUnresolved()
    {
        var doc = ConflictParser.Parse("<<<<<<< HEAD\nO\n=======\nT\n>>>>>>> b\n");
        var manual = RegionResolution.Manual("<<<<<<< HEAD\nX\n=======\nY\n>>>>>>> b\n");
        Assert.Equal(1, ResolutionComposer.Compose(doc, [manual]).UnresolvedCount);
    }

    [Fact]
    public void Compose_CrlfPreserved()
    {
        var doc = ConflictParser.Parse("a\r\n<<<<<<< HEAD\r\nx\r\n=======\r\ny\r\n>>>>>>> b\r\nz\r\n");
        Assert.Equal("a\r\nx\r\ny\r\nz\r\n", Compose(doc, RegionResolution.BothOursFirst));
    }

    [Fact]
    public void Compose_EofWithoutNewline_TrimsTrailingTerminator()
    {
        var doc = ConflictParser.Parse("a\n<<<<<<< HEAD\nx\n=======\ny\n>>>>>>> b");
        Assert.Equal("a\nx", Compose(doc, RegionResolution.Ours));
        Assert.Equal("a\nx\ny", Compose(doc, RegionResolution.BothOursFirst));

        var crlf = ConflictParser.Parse("<<<<<<< HEAD\r\nx\r\n=======\r\ny\r\n>>>>>>> b");
        Assert.Equal("y", Compose(crlf, RegionResolution.Theirs));
    }

    [Fact]
    public void Compose_EofWithoutNewline_ManualKeptVerbatim()
    {
        var doc = ConflictParser.Parse("<<<<<<< HEAD\nx\n=======\ny\n>>>>>>> b");
        Assert.Equal("m\n", Compose(doc, RegionResolution.Manual("m\n")));
    }

    [Fact]
    public void Compose_EmptySides_Both_IsEmpty()
    {
        var doc = ConflictParser.Parse("a\n<<<<<<< HEAD\n=======\n>>>>>>> b\nz\n");
        Assert.Equal("a\nz\n", Compose(doc, RegionResolution.BothTheirsFirst));
    }

    [Fact]
    public void Compose_Spans_MapSegmentsToOutputOffsets()
    {
        var doc = ConflictParser.Parse(ThreeRegions);
        var result = ResolutionComposer.Compose(doc,
            [RegionResolution.Ours, RegionResolution.Unresolved, RegionResolution.Theirs]);
        Assert.Equal(doc.Segments.Count, result.Spans.Count);
        foreach (var span in result.Spans)
            Assert.True(span.Start + span.Length <= result.Text.Length);

        var first = result.Spans[1];
        Assert.Equal(0, first.ConflictIndex);
        Assert.True(first.IsResolved);
        Assert.Equal("o1\n", result.Text.Substring(first.Start, first.Length));
        Assert.False(result.Spans[3].IsResolved);
        Assert.Equal(-1, result.Spans[0].ConflictIndex);
        Assert.True(result.Spans[0].IsResolved);
        Assert.Equal(result.Text.Length, result.Spans[^1].Start + result.Spans[^1].Length);
    }

    [Fact]
    public void Compose_TooManyChoices_Throws()
    {
        var doc = ConflictParser.Parse("x\n");
        Assert.Throws<ArgumentException>(() => ResolutionComposer.Compose(doc, [RegionResolution.Ours]));
    }

    [Fact]
    public void Compose_NullArgs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ResolutionComposer.Compose(null!));
        Assert.Throws<ArgumentNullException>(() => ResolutionComposer.Compose(ConflictParser.Parse(""), null!));
    }

    [Fact]
    public void Manual_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RegionResolution.Manual(null!));
    }

    private static string Compose(ConflictDocument doc, RegionResolution r) =>
        ResolutionComposer.Compose(doc, [r]).Text;
}
