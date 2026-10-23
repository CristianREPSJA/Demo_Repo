using Rama.Core.Conflicts;

namespace Rama.Core.Tests.Conflicts;

public class ResultEditTests
{
    private const string Text =
        "a\n<<<<<<< HEAD\nO\n=======\nT\n>>>>>>> b\nz\n<<<<<<< HEAD\nP\n=======\nQ\n>>>>>>> b\n";

    private static (ConflictDocument Doc, RegionResolution[] Choices, ComposeResult Before) Setup(
        params RegionResolution[] choices)
    {
        var doc = ConflictParser.Parse(Text);
        return (doc, choices, ResolutionComposer.Compose(doc, choices));
    }

    private static string Splice(string s, int offset, int removed, string inserted) =>
        s[..offset] + inserted + s[(offset + removed)..];

    [Fact]
    public void EditInsideResolvedRegion_BecomesManual()
    {
        var (doc, choices, before) = Setup(RegionResolution.Ours, RegionResolution.Theirs);
        // "a\nO\nz\nQ\n": insertar "X" tras "O"
        var after = Splice(before.Text, 3, 0, "X");
        var outcome = ResultEdit.Apply(doc, choices, before, 3, 0, after);
        Assert.False(outcome.Reparsed);
        Assert.Equal(ResolutionKind.Manual, outcome.Choices[0].Kind);
        Assert.Equal("OX\n", outcome.Choices[0].ManualText);
        Assert.Equal(ResolutionKind.Theirs, outcome.Choices[1].Kind);
        Assert.Equal(after, ResolutionComposer.Compose(outcome.Document, outcome.Choices).Text);
    }

    [Fact]
    public void EditInsideUnresolvedRegion_BecomesManualWithMarkers()
    {
        var (doc, choices, before) = Setup();
        var start = before.Spans[1].Start;
        var after = Splice(before.Text, start, 0, "!");
        var outcome = ResultEdit.Apply(doc, choices, before, start, 0, after);
        Assert.Equal(ResolutionKind.Manual, outcome.Choices[0].Kind);
        Assert.Equal(ResolutionKind.Unresolved, outcome.Choices[1].Kind);
        var composed = ResolutionComposer.Compose(outcome.Document, outcome.Choices);
        Assert.Equal(after, composed.Text);
    }

    [Fact]
    public void DeletingMarkersByHand_ResolvesRegion()
    {
        var (doc, choices, before) = Setup(RegionResolution.Unresolved, RegionResolution.Ours);
        var span = before.Spans[1];
        var after = Splice(before.Text, span.Start, span.Length, "manual\n");
        var outcome = ResultEdit.Apply(doc, choices, before, span.Start, span.Length, after);
        var composed = ResolutionComposer.Compose(outcome.Document, outcome.Choices);
        Assert.Equal(0, composed.UnresolvedCount);
        Assert.Equal("a\nmanual\nz\nP\n", composed.Text);
    }

    [Fact]
    public void EditInCommonText_UpdatesDocumentKeepsChoices()
    {
        var (doc, choices, before) = Setup(RegionResolution.Ours, RegionResolution.Unresolved);
        var after = Splice(before.Text, 0, 1, "A");
        var outcome = ResultEdit.Apply(doc, choices, before, 0, 1, after);
        Assert.False(outcome.Reparsed);
        Assert.Equal(ResolutionKind.Ours, outcome.Choices[0].Kind);
        Assert.Equal(ResolutionKind.Unresolved, outcome.Choices[1].Kind);
        Assert.Equal(after, ResolutionComposer.Compose(outcome.Document, outcome.Choices).Text);
    }

    [Fact]
    public void EditAcrossSegments_ReparsesWholeText()
    {
        var (doc, choices, before) = Setup(RegionResolution.Ours, RegionResolution.Unresolved);
        // borra desde "a" hasta dentro de la región 2 (cruza límites)
        var after = Splice(before.Text, 0, 6, string.Empty);
        var outcome = ResultEdit.Apply(doc, choices, before, 0, 6, after);
        Assert.True(outcome.Reparsed);
        Assert.Equal(after, ResolutionComposer.Compose(outcome.Document, outcome.Choices).Text);
    }

    [Fact]
    public void InsertAtBoundary_PrefersConflictRegion()
    {
        var (doc, choices, before) = Setup(RegionResolution.Ours, RegionResolution.Ours);
        var boundary = before.Spans[1].Start;
        var after = Splice(before.Text, boundary, 0, "Y");
        var outcome = ResultEdit.Apply(doc, choices, before, boundary, 0, after);
        Assert.Equal(ResolutionKind.Manual, outcome.Choices[0].Kind);
        Assert.Equal("YO\n", outcome.Choices[0].ManualText);
    }

    [Fact]
    public void InvalidRange_Throws()
    {
        var (doc, choices, before) = Setup();
        Assert.Throws<ArgumentOutOfRangeException>(() => ResultEdit.Apply(doc, choices, before, -1, 0, before.Text));
        Assert.Throws<ArgumentOutOfRangeException>(() => ResultEdit.Apply(doc, choices, before, 0, 999, before.Text));
        Assert.Throws<ArgumentNullException>(() => ResultEdit.Apply(doc, choices, before, 0, 0, null!));
    }
}

public class RegionSelectionTests
{
    [Fact]
    public void Empty_IsUnresolved()
    {
        Assert.Equal(ResolutionKind.Unresolved, RegionSelection.None.ToResolution().Kind);
    }

    [Fact]
    public void SingleSides()
    {
        Assert.Equal(ResolutionKind.Ours, RegionSelection.None.ToggleOurs().ToResolution().Kind);
        Assert.Equal(ResolutionKind.Theirs, RegionSelection.None.ToggleTheirs().ToResolution().Kind);
    }

    [Fact]
    public void Both_OrderFollowsFirstChecked()
    {
        Assert.Equal(ResolutionKind.BothOursFirst,
            RegionSelection.None.ToggleOurs().ToggleTheirs().ToResolution().Kind);
        Assert.Equal(ResolutionKind.BothTheirsFirst,
            RegionSelection.None.ToggleTheirs().ToggleOurs().ToResolution().Kind);
    }

    [Fact]
    public void Uncheck_ReturnsToSingleOrNone()
    {
        var both = RegionSelection.None.ToggleOurs().ToggleTheirs();
        Assert.Equal(ResolutionKind.Theirs, both.ToggleOurs().ToResolution().Kind);
        Assert.Equal(ResolutionKind.Ours, both.ToggleTheirs().ToResolution().Kind);
        Assert.Equal(ResolutionKind.Unresolved, both.ToggleOurs().ToggleTheirs().ToResolution().Kind);
    }

    [Theory]
    [InlineData(ResolutionKind.Unresolved, false, false)]
    [InlineData(ResolutionKind.Manual, false, false)]
    [InlineData(ResolutionKind.Ours, true, false)]
    [InlineData(ResolutionKind.Theirs, false, true)]
    [InlineData(ResolutionKind.BothOursFirst, true, true)]
    [InlineData(ResolutionKind.BothTheirsFirst, true, true)]
    public void FromResolution_RoundTrips(ResolutionKind kind, bool ours, bool theirs)
    {
        var r = kind == ResolutionKind.Manual ? RegionResolution.Manual("x") : new RegionResolution(kind);
        var sel = RegionSelection.From(r);
        Assert.Equal(ours, sel.Ours);
        Assert.Equal(theirs, sel.Theirs);
        if (kind != ResolutionKind.Manual)
            Assert.Equal(kind, sel.ToResolution().Kind);
    }
}
