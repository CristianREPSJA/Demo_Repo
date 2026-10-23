using Rama.Core.Conflicts;

namespace Rama.Core.Tests.Conflicts;

public class ConflictParserTests
{
    private const string Simple =
        "a\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> feature\nz\n";

    [Fact]
    public void Parse_NoMarkers_SingleCommonSegment()
    {
        var doc = ConflictParser.Parse("hola\nmundo\n");
        Assert.Equal(0, doc.ConflictCount);
        var seg = Assert.Single(doc.Segments);
        Assert.IsType<CommonSegment>(seg);
        Assert.Equal("hola\nmundo\n", doc.OriginalText);
    }

    [Fact]
    public void Parse_EmptyText_NoSegments()
    {
        var doc = ConflictParser.Parse(string.Empty);
        Assert.Empty(doc.Segments);
        Assert.Equal(0, doc.ConflictCount);
        Assert.Equal(string.Empty, doc.OriginalText);
    }

    [Fact]
    public void Parse_SimpleConflict_SplitsSidesAndLabels()
    {
        var doc = ConflictParser.Parse(Simple);
        Assert.Equal(3, doc.Segments.Count);
        var h = Assert.Single(doc.Hunks);
        Assert.Equal(0, h.Index);
        Assert.Equal("ours\n", h.Ours);
        Assert.Equal("theirs\n", h.Theirs);
        Assert.Equal("HEAD", h.OursLabel);
        Assert.Equal("feature", h.TheirsLabel);
        Assert.Null(h.Base);
        Assert.False(h.HasBase);
        Assert.Equal("a\n", ((CommonSegment)doc.Segments[0]).Text);
        Assert.Equal("z\n", ((CommonSegment)doc.Segments[2]).Text);
        Assert.Equal(Simple, doc.OriginalText);
    }

    [Fact]
    public void Parse_Diff3Base_CapturesBaseSection()
    {
        const string text = "<<<<<<< ours\nA\n||||||| base\nB\n=======\nC\n>>>>>>> theirs\n";
        var doc = ConflictParser.Parse(text);
        var h = Assert.Single(doc.Hunks);
        Assert.True(h.HasBase);
        Assert.Equal("B\n", h.Base);
        Assert.Equal("||||||| base\n", h.BaseMarker);
        Assert.Equal("A\n", h.Ours);
        Assert.Equal("C\n", h.Theirs);
        Assert.Equal(text, doc.OriginalText);
    }

    [Fact]
    public void Parse_Crlf_PreservedInSides()
    {
        const string text = "x\r\n<<<<<<< HEAD\r\nO\r\n=======\r\nT\r\n>>>>>>> b\r\ny\r\n";
        var doc = ConflictParser.Parse(text);
        var h = Assert.Single(doc.Hunks);
        Assert.Equal("O\r\n", h.Ours);
        Assert.Equal("T\r\n", h.Theirs);
        Assert.Equal("HEAD", h.OursLabel);
        Assert.Equal("b", h.TheirsLabel);
        Assert.Equal(text, doc.OriginalText);
    }

    [Fact]
    public void Parse_EmptySides()
    {
        var doc = ConflictParser.Parse("<<<<<<< HEAD\n=======\n>>>>>>> b\n");
        var h = Assert.Single(doc.Hunks);
        Assert.Equal(string.Empty, h.Ours);
        Assert.Equal(string.Empty, h.Theirs);
    }

    [Fact]
    public void Parse_MultipleRegions_IndexedInOrder()
    {
        const string text =
            "<<<<<<< a\n1\n=======\n2\n>>>>>>> b\n" +
            "<<<<<<< a\n3\n=======\n4\n>>>>>>> b\n" +
            "mid\n" +
            "<<<<<<< a\n5\n=======\n6\n>>>>>>> b";
        var doc = ConflictParser.Parse(text);
        Assert.Equal(3, doc.ConflictCount);
        Assert.Equal([0, 1, 2], doc.Hunks.Select(h => h.Index));
        Assert.Equal(4, doc.Segments.Count);
        Assert.Equal(">>>>>>> b", doc.Hunks[2].EndMarker);
        Assert.Equal(text, doc.OriginalText);
    }

    [Fact]
    public void Parse_UnterminatedRegion_TreatedAsCommonText()
    {
        const string text = "a\n<<<<<<< HEAD\nb\n=======\nc\n";
        var doc = ConflictParser.Parse(text);
        Assert.Equal(0, doc.ConflictCount);
        Assert.Equal(text, doc.OriginalText);
    }

    [Fact]
    public void Parse_Diff3BaseWithoutSeparator_TreatedAsCommonText()
    {
        const string text = "<<<<<<< a\nA\n||||||| base\nB\n";
        var doc = ConflictParser.Parse(text);
        Assert.Equal(0, doc.ConflictCount);
        Assert.Equal(text, doc.OriginalText);
    }

    [Fact]
    public void Parse_MissingSeparator_TreatedAsCommonText()
    {
        const string text = "<<<<<<< HEAD\nb\n>>>>>>> x\n";
        var doc = ConflictParser.Parse(text);
        Assert.Equal(0, doc.ConflictCount);
        Assert.Equal(text, doc.OriginalText);
    }

    [Theory]
    [InlineData("=======\n")]
    [InlineData(">>>>>>> x\n")]
    [InlineData("||||||| x\n")]
    [InlineData("<<<<<<<<< long\n")]
    [InlineData("<<<<<<<x\n")]
    [InlineData("==========\n")]
    public void Parse_MarkerLikeTextOutsideConflict_IsCommon(string line)
    {
        var text = "a\n" + line + "b\n";
        var doc = ConflictParser.Parse(text);
        Assert.Equal(0, doc.ConflictCount);
        Assert.Equal(text, doc.OriginalText);
    }

    [Fact]
    public void Parse_MarkerLikeTextInsideSides_IsContent()
    {
        // '>>>>>>>' antes del separador y '=======' después del separador son contenido.
        const string text =
            "<<<<<<< HEAD\n>>>>>>> not end\n<<<<<<< nested\n=======\n=======\n<<<<<<< x\n>>>>>>> b\n";
        var doc = ConflictParser.Parse(text);
        var h = Assert.Single(doc.Hunks);
        Assert.Equal(">>>>>>> not end\n<<<<<<< nested\n", h.Ours);
        Assert.Equal("=======\n<<<<<<< x\n", h.Theirs);
        Assert.Equal(text, doc.OriginalText);
    }

    [Fact]
    public void Parse_LongerMarkers_NotRecognized()
    {
        const string text = "<<<<<<<< a\nx\n========\ny\n>>>>>>>> b\n";
        Assert.Equal(0, ConflictParser.Parse(text).ConflictCount);
    }

    [Fact]
    public void Parse_BareMarkersWithoutLabels()
    {
        var doc = ConflictParser.Parse("<<<<<<<\nA\n=======\nB\n>>>>>>>\n");
        var h = Assert.Single(doc.Hunks);
        Assert.Equal(string.Empty, h.OursLabel);
        Assert.Equal(string.Empty, h.TheirsLabel);
    }

    [Fact]
    public void Parse_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ConflictParser.Parse(null!));
    }

    [Fact]
    public void WithCommonText_ReplacesOnlyThatSegment()
    {
        var doc = ConflictParser.Parse(Simple);
        var updated = doc.WithCommonText(0, "A\n");
        Assert.Equal("A\n" + Simple[2..], updated.OriginalText);
        Assert.Equal(Simple, doc.OriginalText);
        Assert.Throws<ArgumentException>(() => doc.WithCommonText(1, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.WithCommonText(9, "x"));
    }
}
