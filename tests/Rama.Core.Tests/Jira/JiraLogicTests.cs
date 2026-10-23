using System.Net;
using System.Text.Json;
using Rama.Core.Http;
using Rama.Core.Jira;

namespace Rama.Core.Tests.Jira;

public class JqlBuilderTests
{
    [Fact]
    public void Build_ProjectOnly() =>
        Assert.Equal("project = \"VEN\" ORDER BY Rank ASC", JqlBuilder.Build("VEN"));

    [Fact]
    public void Build_MeSprintAndAccounts()
    {
        Assert.Equal("project = \"VEN\" AND assignee = currentUser() AND sprint in openSprints() ORDER BY Rank ASC",
            JqlBuilder.Build("VEN", includeMe: true, openSprintsOnly: true));
        Assert.Equal("project = \"VEN\" AND assignee = \"a1\" ORDER BY Rank ASC",
            JqlBuilder.Build("VEN", accountIds: ["a1"]));
        Assert.Equal("project = \"VEN\" AND (assignee = currentUser() OR assignee = \"a1\" OR assignee = \"b2\") ORDER BY Rank ASC",
            JqlBuilder.Build("VEN", true, ["a1", "b2"]));
    }

    [Fact]
    public void Quote_EscapesSpecials() =>
        Assert.Equal("\"a\\\"b\\\\c\\n\\r\\t\"", JqlBuilder.Quote("a\"b\\c\n\r\t"));

    [Fact]
    public void Build_InjectionStaysInsideLiteral() =>
        Assert.Equal("project = \"X\\\" OR project = \\\"Y\" ORDER BY Rank ASC",
            JqlBuilder.Build("X\" OR project = \"Y"));

    [Fact]
    public void Build_RejectsBlankProject() =>
        Assert.Throws<ArgumentException>(() => JqlBuilder.Build(" "));
}

public class BranchTicketParserTests
{
    [Theory]
    [InlineData("feature/VEN-412-x", "VEN-412")]
    [InlineData("VEN-1", "VEN-1")]
    [InlineData("bugfix/AB2-30_fix", "AB2-30")]
    [InlineData("feature/ven-412", null)]
    [InlineData("release-2024", null)]
    [InlineData("xVEN-4", null)]
    [InlineData("feature/A-1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractKey(string? branch, string? expected) =>
        Assert.Equal(expected, BranchTicketParser.ExtractKey(branch));

    private static JiraIssue Issue(string summary) =>
        new("VEN-412", summary, "1", "S", null, null, null, null, null);

    [Fact]
    public void BranchNameFor_FoldsAccentsAndHyphenates() =>
        Assert.Equal("feature/VEN-412-corregir-ano-nino-y-pinon",
            BranchTicketParser.BranchNameFor(Issue("Corregir año, niño y piñón!")));

    [Fact]
    public void BranchNameFor_TruncatesSlugTo40WithoutTrailingHyphen()
    {
        var name = BranchTicketParser.BranchNameFor(Issue("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa bbbbbbbb"));
        var slug = name["feature/VEN-412-".Length..];
        Assert.Equal(39, slug.Length);
        Assert.False(slug.EndsWith('-'));
        Assert.Equal(40, BranchTicketParser.Slugify(new string('a', 100)).Length);
    }

    [Fact]
    public void BranchNameFor_EmptySlugUsesKeyOnly() =>
        Assert.Equal("feature/VEN-412", BranchTicketParser.BranchNameFor(Issue("¡¿?!")));

    [Fact]
    public void Slugify_NullOrBlank() => Assert.Equal("", BranchTicketParser.Slugify(null));
}

public class BoardModelTests
{
    internal static JiraIssue I(string key, string status, string? who = null, string? name = null) =>
        new(key, key, status, "st" + status, who, name, null, null, null);

    internal static readonly JiraColumn[] Cols =
    [
        new("Por hacer", ["1"]), new("En curso", ["3", "4"]), new("Hecho", ["5"]),
    ];

    [Fact]
    public void GroupsPerPersonAndColumn_WithCounts()
    {
        var m = new BoardModel(Cols,
        [
            I("A-1", "1", "b", "Beto"), I("A-2", "4", "a", "Ana"), I("A-3", "3", "a", "Ana"),
            I("A-4", "5"), I("A-5", "1", "a", "Ana"),
        ]);
        Assert.Equal(["Ana", "Beto", BoardModel.UnassignedName], m.Lanes.Select(l => l.DisplayName));
        var ana = m.Lanes[0];
        Assert.Equal(3, ana.Total);
        Assert.Equal(["A-5"], ana.Cells[0].Select(x => x.Key));
        Assert.Equal(["A-2", "A-3"], ana.Cells[1].Select(x => x.Key));
        Assert.Equal([2, 2, 1], m.ColumnTotals);
        Assert.Null(m.Lanes[2].AccountId);
        Assert.Equal(3, m.Columns.Count);
    }

    [Fact]
    public void UnknownStatus_GoesToSinColumna()
    {
        var m = new BoardModel(Cols, [I("A-1", "99", "a", "Ana"), I("A-2", "1", "a", "Ana")]);
        Assert.Equal(4, m.Columns.Count);
        Assert.Equal(BoardModel.UnmappedColumnName, m.Columns[3].Name);
        Assert.Equal(["A-1"], m.Lanes[0].Cells[3].Select(x => x.Key));
        Assert.Equal(BoardModel.UnmappedColumnName, m.ColumnOf(m.Issues[0])!.Name);
        Assert.Equal("Por hacer", m.ColumnOf(m.Issues[1])!.Name);
    }

    [Fact]
    public void WithIssue_ReturnsNewModelLeavingOriginal()
    {
        var m = new BoardModel(Cols, [I("A-1", "1", "a", "Ana")]);
        var m2 = m.WithIssue(m.Issues[0] with { StatusId = "5" });
        Assert.Equal("1", m.Find("A-1")!.StatusId);
        Assert.Equal("5", m2.Find("A-1")!.StatusId);
        Assert.Null(m2.Find("nope"));
    }

    [Fact]
    public void AssigneeWithoutName_UsesAccountId() =>
        Assert.Equal("zz", new BoardModel(Cols, [I("A-1", "1", "zz")]).Lanes[0].DisplayName);

    [Fact]
    public void EmptyBoard_NoLanes()
    {
        var m = new BoardModel(Cols, []);
        Assert.Empty(m.Lanes);
        Assert.Equal([0, 0, 0], m.ColumnTotals);
    }
}

public class TransitionPlannerTests
{
    private static JiraTransition T(string id, string to, params TransitionField[] f) => new(id, id, to, "to" + to, f);

    private static readonly JiraColumn Target = new("Hecho", ["5"]);
    private static readonly JiraIssue Issue = BoardModelTests.I("A-1", "3");

    [Fact]
    public void NoTransition_NotAllowedWithSpanishReason()
    {
        var p = TransitionPlanner.Plan(Issue, Target, [T("1", "4")]);
        Assert.Equal(PlanKind.NotAllowed, p.Kind);
        Assert.Equal("Tu flujo no permite pasar de st3 a Hecho", p.Reason);
    }

    [Fact]
    public void Allowed_PrefersTransitionWithoutRequiredFields()
    {
        var req = new TransitionField("resolution", "Res", true, false, []);
        var p = TransitionPlanner.Plan(Issue, Target, [T("a", "5", req), T("b", "5")]);
        Assert.Equal(PlanKind.Allowed, p.Kind);
        Assert.Equal("b", p.Transition!.Id);
    }

    [Fact]
    public void RequiredFieldMissing_ThenSatisfied()
    {
        var req = new TransitionField("resolution", "Res", true, false, []);
        var def = new TransitionField("assignee", "Asig", true, true, []);
        var opt = new TransitionField("comment", "Com", false, false, []);
        var ts = new[] { T("a", "5", req, def, opt) };

        var p = TransitionPlanner.Plan(Issue, Target, ts);
        Assert.Equal(PlanKind.RequiresFields, p.Kind);
        Assert.Equal(["resolution"], p.MissingFields.Select(f => f.Key));

        var ok = TransitionPlanner.Plan(Issue, Target, ts, ["resolution"]);
        Assert.Equal(PlanKind.Allowed, ok.Kind);
    }
}

public class OptimisticMoveTests
{
    private sealed class FakeApi : IJiraTransitionApi
    {
        public List<JiraTransition> Transitions { get; set; } = [];
        public Exception? GetError { get; set; }
        public Exception? PostError { get; set; }
        public List<(string Key, string Id, int FieldCount)> Posted { get; } = [];
        public Action? OnGet { get; set; }

        public Task<IReadOnlyList<JiraTransition>> GetTransitionsAsync(string k, CancellationToken ct = default)
        {
            OnGet?.Invoke();
            if (GetError is not null) throw GetError;
            return Task.FromResult<IReadOnlyList<JiraTransition>>(Transitions);
        }

        public Task TransitionAsync(string k, string id, IReadOnlyDictionary<string, JsonElement>? f = null,
            CancellationToken ct = default)
        {
            if (PostError is not null) throw PostError;
            Posted.Add((k, id, f?.Count ?? 0));
            return Task.CompletedTask;
        }
    }

    private static readonly JiraColumn Done = BoardModelTests.Cols[2];

    private static (OptimisticMove Move, FakeApi Api, List<string> Seen) Make()
    {
        var board = new BoardModel(BoardModelTests.Cols, [BoardModelTests.I("A-1", "3", "a", "Ana")]);
        var api = new FakeApi { Transitions = [new("31", "Hecho", "5", "Done", [])] };
        var move = new OptimisticMove(api, board);
        var seen = new List<string>();
        move.BoardChanged += b => seen.Add(b.Find("A-1")!.StatusId);
        return (move, api, seen);
    }

    [Fact]
    public async Task Success_AppliesLocallyThenCommitsWithRealStatus()
    {
        var (m, api, seen) = Make();
        var states = new List<MoveState>();
        api.OnGet = () => states.Add(m.State);
        var r = await m.MoveAsync("A-1", Done);
        Assert.Equal(MoveOutcome.Committed, r.Outcome);
        Assert.Equal(["5", "5"], seen);
        Assert.Equal("Done", m.Board.Find("A-1")!.StatusName);
        Assert.Equal([MoveState.Pending], states);
        Assert.Equal(MoveState.Idle, m.State);
        Assert.Equal([("A-1", "31", 0)], api.Posted);
    }

    [Fact]
    public async Task ApiFailure_RollsBackWithSpanishMessage()
    {
        var (m, api, seen) = Make();
        api.PostError = new ApiException(HttpStatusCode.BadRequest, "x");
        var r = await m.MoveAsync("A-1", Done);
        Assert.Equal(MoveOutcome.RolledBack, r.Outcome);
        Assert.Equal("Jira rechazó mover A-1 a Hecho (400).", r.Message);
        Assert.Equal("3", m.Board.Find("A-1")!.StatusId);
        Assert.Equal(["5", "3"], seen);
        Assert.Equal(MoveState.Idle, m.State);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "credenciales")]
    [InlineData(HttpStatusCode.Forbidden, "credenciales")]
    [InlineData((HttpStatusCode)0, "conectar")]
    public async Task ErrorMessages(HttpStatusCode status, string fragment)
    {
        var (m, api, _) = Make();
        api.GetError = status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? new ApiAuthException(status, null)
            : new ApiException(status, "net");
        var r = await m.MoveAsync("A-1", Done);
        Assert.Contains(fragment, r.Message);
        Assert.Equal("3", m.Board.Find("A-1")!.StatusId);
    }

    [Fact]
    public async Task UnexpectedException_GenericMessage()
    {
        var (m, api, _) = Make();
        api.GetError = new InvalidOperationException("boom");
        var r = await m.MoveAsync("A-1", Done);
        Assert.Equal("No se pudo mover A-1 a Hecho.", r.Message);
    }

    [Fact]
    public async Task Cancellation_RollsBackAndRethrows()
    {
        var (m, api, _) = Make();
        api.GetError = new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(() => m.MoveAsync("A-1", Done));
        Assert.Equal("3", m.Board.Find("A-1")!.StatusId);
        Assert.Equal(MoveState.Idle, m.State);
    }

    [Fact]
    public async Task NotAllowed_RollsBackWithReason()
    {
        var (m, api, _) = Make();
        api.Transitions = [];
        var r = await m.MoveAsync("A-1", Done);
        Assert.Equal(MoveOutcome.NotAllowed, r.Outcome);
        Assert.Equal("Tu flujo no permite pasar de st3 a Hecho", r.Message);
        Assert.Equal("3", m.Board.Find("A-1")!.StatusId);
        Assert.Empty(api.Posted);
    }

    [Fact]
    public async Task RequiredFields_RollsBackThenSucceedsWithFields()
    {
        var (m, api, _) = Make();
        api.Transitions = [new("31", "Hecho", "5", "Done", [new("resolution", "Res", true, false, [])])];
        var r = await m.MoveAsync("A-1", Done);
        Assert.Equal(MoveOutcome.RequiresFields, r.Outcome);
        Assert.Equal("resolution", r.MissingFields![0].Key);
        Assert.Equal("3", m.Board.Find("A-1")!.StatusId);
        Assert.Empty(api.Posted);

        var fields = new Dictionary<string, JsonElement> { ["resolution"] = JsonDocument.Parse("{}").RootElement.Clone() };
        var r2 = await m.MoveAsync("A-1", Done, fields);
        Assert.Equal(MoveOutcome.Committed, r2.Outcome);
        Assert.Equal(1, api.Posted[0].FieldCount);
    }

    [Fact]
    public async Task EdgeCases_NoOpMissingEmptyColumnAndBusy()
    {
        var (m, api, _) = Make();
        Assert.Equal(MoveOutcome.NoOp, (await m.MoveAsync("A-1", BoardModelTests.Cols[1])).Outcome);
        Assert.Equal(MoveOutcome.RolledBack, (await m.MoveAsync("Z-9", Done)).Outcome);
        Assert.Equal(MoveOutcome.NotAllowed, (await m.MoveAsync("A-1", new JiraColumn("Vacía", []))).Outcome);

        MoveResult? inner = null;
        api.OnGet = () => inner = m.MoveAsync("A-1", Done).Result;
        await m.MoveAsync("A-1", Done);
        Assert.Equal(MoveOutcome.Busy, inner!.Outcome);
    }

    [Fact]
    public async Task Refresh_IgnoredWhilePending_AppliedWhenIdle()
    {
        var (m, api, _) = Make();
        var fresh = new BoardModel(BoardModelTests.Cols, [BoardModelTests.I("A-1", "1", "a", "Ana")]);
        api.OnGet = () => m.Refresh(fresh);
        await m.MoveAsync("A-1", Done);
        Assert.Equal("5", m.Board.Find("A-1")!.StatusId);
        m.Refresh(fresh);
        Assert.Equal("1", m.Board.Find("A-1")!.StatusId);
    }
}
