using System.Text.Json;
using Rama.Core.Accounts;
using Rama.Core.Http;
using Rama.Core.Jira;

namespace Rama.Core.Tests.Jira;

internal sealed class FakeBoardApi : IJiraBoardApi
{
    public List<JiraIssue> Issues { get; set; } = [];
    public List<JiraTransition> Transitions { get; set; } = [];
    public Exception? SearchError { get; set; }
    public int SearchCalls { get; private set; }
    public int ColumnCalls { get; private set; }
    public string? LastJql { get; private set; }
    public string? LastField { get; private set; }
    public string? EstimationField { get; set; }
    public string BoardType { get; set; } = "kanban";
    public Exception? TransitionError { get; set; }

    public Task<JiraUser> GetMyselfAsync(CancellationToken ct = default) =>
        Task.FromResult(new JiraUser("me", "Diego Muñoz", true));

    public Task<IReadOnlyList<JiraBoardInfo>> GetBoardsAsync(string p, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<JiraBoardInfo>>([new JiraBoardInfo(7, "VEN", BoardType)]);

    public async Task<JiraBoardConfig> GetBoardConfigurationAsync(long id, CancellationToken ct = default) =>
        new(await GetBoardColumnsAsync(id, ct), EstimationField);

    public Task<IReadOnlyList<JiraColumn>> GetBoardColumnsAsync(long id, CancellationToken ct = default)
    {
        ColumnCalls++;
        return Task.FromResult<IReadOnlyList<JiraColumn>>(
            [new JiraColumn("Por hacer", ["1"]), new JiraColumn("En curso", ["2"]), new JiraColumn("Listo", ["3"])]);
    }

    public Task<IReadOnlyList<JiraIssue>> SearchIssuesAsync(string jql, string? sp = null, CancellationToken ct = default)
    {
        SearchCalls++;
        LastJql = jql;
        LastField = sp;
        if (SearchError is not null) throw SearchError;
        return Task.FromResult<IReadOnlyList<JiraIssue>>(Issues.ToList());
    }

    public Task<IReadOnlyList<JiraUser>> GetAssignableUsersAsync(string p, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<JiraUser>>(
            [new JiraUser("me", "Diego Muñoz", true), new JiraUser("c", "Camila Rojas", true),
             new JiraUser("x", "Inactivo", false)]);

    public Task<IReadOnlyList<JiraTransition>> GetTransitionsAsync(string key, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<JiraTransition>>(Transitions);

    public Task TransitionAsync(string key, string id, IReadOnlyDictionary<string, JsonElement>? f = null,
        CancellationToken ct = default) => TransitionError is null ? Task.CompletedTask : Task.FromException(TransitionError);
}

public class BoardControllerTests
{
    [Fact]
    public async Task Sync_UsesEstimationFieldFromBoardConfig()
    {
        var (c, api) = Make(a => a.EstimationField = "customfield_10024");
        await c.SyncAsync();
        Assert.Equal("customfield_10024", api.LastField);
        Assert.Equal("customfield_10024", c.EffectiveStoryPointsField);
    }

    [Fact]
    public async Task Sync_FallsBackToDefaultFieldWhenBoardHasNone()
    {
        var (c, api) = Make();
        await c.SyncAsync();
        Assert.Equal(BoardController.DefaultStoryPointsField, api.LastField);
    }

    [Fact]
    public async Task Sync_ExplicitFieldOverridesBoardField()
    {
        var api = new FakeBoardApi { EstimationField = "customfield_10024" };
        var c = new BoardController(api, Acc) { StoryPointsField = "customfield_99" };
        await c.SyncAsync();
        Assert.Equal("customfield_99", api.LastField);
    }
    private static JiraIssue Issue(string key, string status, string? who = "me", string? type = "Historia",
        string? pri = "Alta", double? pts = 5) =>
        new(key, $"Resumen {key}", status, "S" + status, who, who is null ? null : who == "me" ? "Diego Muñoz" : "Camila Rojas",
            type, pri, pts);

    private static readonly JiraAccount Acc = new("acme", "d@x.cl", "VEN");

    private static (BoardController, FakeBoardApi) Make(Action<FakeBoardApi>? setup = null)
    {
        var api = new FakeBoardApi { Issues = [Issue("VEN-1", "1"), Issue("VEN-2", "2", "c")] };
        setup?.Invoke(api);
        return (new BoardController(api, Acc), api);
    }

    [Fact]
    public async Task Sync_LoadsOnceThenOnlyIssues()
    {
        var (c, api) = Make();
        await c.SyncAsync();
        await c.SyncAsync();
        Assert.Equal(1, api.ColumnCalls);
        Assert.Equal(2, api.SearchCalls);
        Assert.Equal("me", c.MyAccountId);
        Assert.Single(c.Users, u => u.AccountId == "c");
        Assert.DoesNotContain(c.Users, u => !u.Active);
        Assert.NotNull(c.LastSync);
        Assert.Equal(2, c.Board!.Issues.Count);
    }

    [Fact]
    public async Task Sync_ScrumBoardRestrictsToOpenSprints()
    {
        var (c, api) = Make(a => a.BoardType = "scrum");
        await c.SyncAsync();
        Assert.Contains("openSprints()", api.LastJql);
    }

    [Fact]
    public async Task Sync_UsesConfiguredBoardId()
    {
        var api = new FakeBoardApi();
        var c = new BoardController(api, Acc with { BoardId = 9 });
        await c.SyncAsync();
        Assert.DoesNotContain("openSprints", api.LastJql);
    }

    [Fact]
    public async Task Sync_ErrorLeavesPreviousBoardAndRethrows()
    {
        var (c, api) = Make();
        await c.SyncAsync();
        api.SearchError = new ApiException((System.Net.HttpStatusCode)500, "x");
        await Assert.ThrowsAsync<ApiException>(() => c.SyncAsync());
        Assert.Equal(2, c.Board!.Issues.Count);
    }

    [Fact]
    public async Task Sync_RaisesBoardChanged()
    {
        var (c, _) = Make();
        var n = 0;
        c.BoardChanged += _ => n++;
        await c.SyncAsync();
        await c.SyncAsync();
        Assert.True(n >= 2);
    }

    [Fact]
    public async Task EvaluateDrops_MarksValidInvalidAndCurrent()
    {
        var (c, api) = Make(a => a.Transitions = [new JiraTransition("t", "Ir", "2", "En curso", [])]);
        await c.SyncAsync();
        var drops = await c.EvaluateDropsAsync("VEN-1");
        Assert.Equal(DropKind.Current, drops[0].Kind);
        Assert.Equal(DropKind.Valid, drops[1].Kind);
        Assert.Equal(DropKind.Invalid, drops[2].Kind);
        Assert.Contains("Listo", drops[2].Reason);
    }

    [Fact]
    public async Task EvaluateDrops_FailsBeforeLoad()
    {
        var (c, _) = Make();
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.EvaluateDropsAsync("VEN-1"));
    }

    [Fact]
    public async Task Move_CommitsAndRollsBack()
    {
        var (c, api) = Make(a => a.Transitions = [new JiraTransition("t", "Ir", "2", "En curso", [])]);
        await c.SyncAsync();
        var ok = await c.MoveAsync("VEN-1", c.Board!.Columns[1]);
        Assert.Equal(MoveOutcome.Committed, ok.Outcome);
        Assert.Equal("2", c.Board.Find("VEN-1")!.StatusId);

        api.TransitionError = new ApiException((System.Net.HttpStatusCode)400, "no");
        var bad = await c.MoveAsync("VEN-2", c.Board.Columns[2]);
        Assert.Equal(MoveOutcome.NotAllowed, bad.Outcome);
        Assert.Equal("2", c.Board.Find("VEN-2")!.StatusId);
    }

    [Fact]
    public async Task Move_BeforeLoadIsRolledBack()
    {
        var (c, _) = Make();
        var r = await c.MoveAsync("VEN-1", new JiraColumn("X", ["1"]));
        Assert.Equal(MoveOutcome.RolledBack, r.Outcome);
    }

    [Fact]
    public async Task Move_RequiresFields()
    {
        var f = new TransitionField("resolution", "Resolución", true, false, [new FieldOption("1", "Hecho")]);
        var (c, _) = Make(a => a.Transitions = [new JiraTransition("t", "Cerrar", "3", "Listo", [f])]);
        await c.SyncAsync();
        var r = await c.MoveAsync("VEN-1", c.Board!.Columns[2]);
        Assert.Equal(MoveOutcome.RequiresFields, r.Outcome);
        Assert.Single(r.MissingFields!);
        Assert.Equal("1", c.Board.Find("VEN-1")!.StatusId);
    }

    [Fact]
    public void IssueUrl_EscapesKey() =>
        Assert.Equal("https://acme.atlassian.net/browse/VEN-1", new BoardController(new FakeBoardApi(), Acc).IssueUrl("VEN-1"));

    [Fact]
    public void DescribeError_MapsKnownExceptions()
    {
        Assert.Contains("credenciales", BoardController.DescribeError(new ApiAuthException((System.Net.HttpStatusCode)401, "x")));
        Assert.Contains("conectar", BoardController.DescribeError(new ApiException((System.Net.HttpStatusCode)0, "x")));
        Assert.Contains("500", BoardController.DescribeError(new ApiException((System.Net.HttpStatusCode)500, "x")));
        Assert.Equal("hola", BoardController.DescribeError(new InvalidOperationException("hola")));
        Assert.Contains("tablero", BoardController.DescribeError(new Exception("z")));
    }
}

public class BoardProjectionTests
{
    private static JiraIssue I(string key, string status, string? who, string type = "Tarea", string? pri = "Baja",
        double? pts = 2) =>
        new(key, "Resumen " + key, status, "s", who, who, type, pri, pts);

    private static BoardModel Model(params JiraIssue[] issues) =>
        new([new JiraColumn("A", ["1"]), new JiraColumn("B", ["2"])], issues);

    private static readonly JiraUser[] Users =
        [new("me", "Diego Muñoz", true), new("c", "Camila Rojas", true), new("z", "Sin tickets", true)];

    [Fact]
    public void People_MeFirstWithCounts_AndHidesEmptyOthers()
    {
        var v = BoardProjection.Build(Model(I("V-1", "1", "c"), I("V-2", "2", "c"), I("V-3", "1", "me"), I("V-4", "1", null)),
            Users, "me", null, null);
        Assert.Equal(["me", "c", BoardProjection.UnassignedId], v.People.Select(p => p.Id));
        Assert.Equal([1, 2, 1], v.People.Select(p => p.Count));
        Assert.True(v.People[0].IsMe);
        Assert.Equal("me", v.SelectedId);
    }

    [Fact]
    public void People_MeShownEvenWithoutTickets()
    {
        var v = BoardProjection.Build(Model(I("V-1", "1", "c")), Users, "me", null, null);
        Assert.Equal(0, v.People[0].Count);
    }

    [Fact]
    public void Selection_FiltersColumns_AndFallsBack()
    {
        var m = Model(I("V-1", "1", "c"), I("V-2", "2", "c"), I("V-3", "1", "me"));
        var v = BoardProjection.Build(m, Users, "me", "c", null);
        Assert.Equal([1, 1], v.Columns.Select(c => c.Cards.Count));
        Assert.Equal("me", BoardProjection.Build(m, Users, "me", "nope", null).SelectedId);
        Assert.Equal("c", BoardProjection.Build(m, Users, null, null, null).SelectedId);
    }

    [Fact]
    public void Query_MatchesKeyAndSummary_CaseInsensitive()
    {
        var m = Model(I("V-1", "1", "me"), I("V-22", "1", "me"));
        Assert.Single(BoardProjection.Build(m, Users, "me", "me", "v-22").Columns[0].Cards);
        Assert.Equal(2, BoardProjection.Build(m, Users, "me", "me", "RESUMEN").Columns[0].Cards.Count);
    }

    [Fact]
    public void Cards_CarryBranchPointsPriorityAndKind()
    {
        var m = Model(I("V-1", "1", "me", "Error", "Alta", 3), I("V-2", "1", "me", "Historia", "Baja", null));
        var v = BoardProjection.Build(m, Users, "me", "me", null, k => k == "V-1" ? "feature/V-1-x" : null);
        var a = v.Columns[0].Cards[0];
        Assert.Equal("feature/V-1-x", a.Branch);
        Assert.Equal("3 pts", a.PointsLabel);
        Assert.True(a.IsHighPriority);
        Assert.Equal(IssueKind.Bug, a.Kind);
        var b = v.Columns[0].Cards[1];
        Assert.Null(b.PointsLabel);
        Assert.False(b.IsHighPriority);
        Assert.Equal(IssueKind.Story, b.Kind);
    }

    [Fact]
    public void UnmappedStatus_GoesToExtraColumn()
    {
        var v = BoardProjection.Build(Model(I("V-1", "99", "me")), Users, "me", "me", null);
        Assert.Equal(BoardModel.UnmappedColumnName, v.Columns[2].Column.Name);
        Assert.Single(v.Columns[2].Cards);
    }

    [Theory]
    [InlineData("Diego Muñoz", "DM")]
    [InlineData("ana", "A")]
    [InlineData("Juan Pablo Soto", "JS")]
    [InlineData("", "?")]
    public void Initials(string name, string expected) => Assert.Equal(expected, BoardProjection.Initials(name));

    [Theory]
    [InlineData("Historia", IssueKind.Story)]
    [InlineData("Bug", IssueKind.Bug)]
    [InlineData("Subtarea", IssueKind.Task)]
    [InlineData(null, IssueKind.Task)]
    public void KindOf(string? t, IssueKind k) => Assert.Equal(k, BoardProjection.KindOf(t));

    [Fact]
    public void PaletteOf_IsStableAndInRange()
    {
        Assert.Equal(BoardProjection.PaletteOf("abc"), BoardProjection.PaletteOf("abc"));
        Assert.InRange(BoardProjection.PaletteOf("zzz-123"), 0, BoardProjection.PaletteSize - 1);
    }

    [Fact]
    public void ToCard_FractionalPoints() =>
        Assert.Equal("0.5 pts", BoardProjection.ToCard(I("V-1", "1", "me", pts: 0.5), null).PointsLabel);
}

public class BoardTextTests
{
    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(40, "40 s")]
    [InlineData(150, "2 min")]
    [InlineData(7300, "2 h")]
    [InlineData(-5, "0 s")]
    public void SyncAge(int seconds, string expected) =>
        Assert.Equal(expected, BoardText.SyncAge(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void FindBranch_ReturnsFirstMatchByKey()
    {
        var names = new[] { "main", "feature/VEN-4120-otro", "feature/VEN-412-desc", "hotfix/VEN-412" };
        Assert.Equal("feature/VEN-412-desc", BoardText.FindBranch("VEN-412", names));
        Assert.Null(BoardText.FindBranch("VEN-1", names));
    }
}

public class FieldValueBuilderTests
{
    private static TransitionField F(string key, string? schema = null, params FieldOption[] opts) =>
        new(key, key, true, false, opts, schema);

    [Fact]
    public void KindOf_ClassifiesFields()
    {
        Assert.Equal(FieldInputKind.Select, FieldValueBuilder.KindOf(F("a", null, new FieldOption("1", "x"))));
        Assert.Equal(FieldInputKind.Text, FieldValueBuilder.KindOf(F("a")));
        Assert.Equal(FieldInputKind.Text, FieldValueBuilder.KindOf(F("a", "string")));
        Assert.Equal(FieldInputKind.Unsupported, FieldValueBuilder.KindOf(F("a", "user")));
    }

    [Fact]
    public void Build_ProducesTextSelectAndArrayJson()
    {
        var r = FieldValueBuilder.Build([
            (F("t"), " hola "),
            (F("s", null, new FieldOption("5", "Cinco")), "5"),
            (F("arr", "array", new FieldOption("7", "Siete")), "7"),
        ])!;
        Assert.Equal("hola", r["t"].GetString());
        Assert.Equal("5", r["s"].GetProperty("id").GetString());
        Assert.Equal("7", r["arr"][0].GetProperty("id").GetString());
    }

    [Fact]
    public void Build_NullWhenMissingOrUnsupported()
    {
        Assert.Null(FieldValueBuilder.Build([(F("t"), " ")]));
        Assert.Null(FieldValueBuilder.Build([(F("u", "user"), "x")]));
    }
}
