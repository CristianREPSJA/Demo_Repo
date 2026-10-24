using System.Net;
using System.Text;
using Rama.Core.Azure;
using Rama.Core.Http;

namespace Rama.Core.Tests.Azure;

public class RunStagesTests
{
    private sealed class FixedHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Urls.Add(r.RequestUri!.AbsoluteUri);
            var (code, json) = respond(r);
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private static (AzureDevOpsClient, FixedHandler) Make(HttpStatusCode code, string json)
    {
        var h = new FixedHandler(_ => (code, json));
        return (new AzureDevOpsClient(new HttpClient(h), new AzureConnection("contoso", "My Proj"), "pat",
            new ApiClientOptions { MaxRetries = 0 }), h);
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Azure", "Fixtures", name));

    private static string Rec(string id, string type, string name, string state, string? result, string? order = null,
        string? parent = null) =>
        $$"""{"id":"{{id}}","parentId":{{(parent is null ? "null" : $"\"{parent}\"")}},"type":"{{type}}","name":"{{name}}","state":"{{state}}","result":{{(result is null ? "null" : $"\"{result}\"")}},"order":{{order ?? "null"}}}""";

    private static string Timeline(params string[] records) => "{\"records\":[" + string.Join(",", records) + "]}";

    [Fact]
    public async Task GetRunStages_RealSample_ReturnsSingleSucceededStage()
    {
        var (c, h) = Make(HttpStatusCode.OK, Fixture("timeline-sample.json"));

        var stages = await c.GetRunStagesAsync(2);

        var s = Assert.Single(stages);
        Assert.Equal("Aprobacion", s.Name);
        Assert.Equal(RunStageStatus.Succeeded, s.Status);
        Assert.Contains("/_apis/build/builds/2/timeline", h.Urls[0]);
        Assert.Contains("api-version=", h.Urls[0]);
    }

    // Forma real (build 3, 2026-10-23) esperando aprobación: Checkpoint.Approval -> Checkpoint -> Stage(pending).
    [Fact]
    public async Task GetRunStages_RealWaitingShape_ReturnsWaitingForApproval()
    {
        var json = Timeline(
            Rec("ap", "Checkpoint.Approval", "Checkpoint.Approval", "inProgress", null, parent: "cp"),
            Rec("st", "Stage", "Aprobacion", "pending", null),
            Rec("cp", "Checkpoint", "Checkpoint", "inProgress", null, parent: "st"));
        var (c, _) = Make(HttpStatusCode.OK, json);

        var s = Assert.Single(await c.GetRunStagesAsync(3));
        Assert.Equal(RunStageStatus.WaitingForApproval, s.Status);
    }

    [Fact]
    public async Task GetRunStages_OrdersByOrder_NullsLastStable()
    {
        var json = Timeline(
            Rec("a", "Stage", "SinOrden1", "pending", null),
            Rec("b", "Stage", "Segunda", "pending", null, "2"),
            Rec("c", "Stage", "SinOrden2", "pending", null),
            Rec("d", "Stage", "Primera", "completed", "succeeded", "1"));
        var (c, _) = Make(HttpStatusCode.OK, json);

        var names = (await c.GetRunStagesAsync(1)).Select(s => s.Name).ToList();

        Assert.Equal(["Primera", "Segunda", "SinOrden1", "SinOrden2"], names);
    }

    [Fact]
    public async Task GetRunStages_IgnoresNonStageRecords()
    {
        var json = Timeline(Rec("j", "Job", "J", "completed", "succeeded"), Rec("t", "Task", "T", "completed", "succeeded"));
        var (c, _) = Make(HttpStatusCode.OK, json);

        Assert.Empty(await c.GetRunStagesAsync(1));
    }

    [Theory]
    [InlineData("pending", null, RunStageStatus.Pending)]
    [InlineData("inProgress", null, RunStageStatus.Running)]
    [InlineData("completed", "succeeded", RunStageStatus.Succeeded)]
    [InlineData("completed", "succeededWithIssues", RunStageStatus.Succeeded)]
    [InlineData("completed", "failed", RunStageStatus.Failed)]
    [InlineData("completed", "canceled", RunStageStatus.Canceled)]
    [InlineData("completed", "abandoned", RunStageStatus.Canceled)]
    [InlineData("completed", "skipped", RunStageStatus.Skipped)]
    [InlineData("completed", null, RunStageStatus.Pending)]
    [InlineData(null, null, RunStageStatus.Pending)]
    public void MapStatus_CoversStatesAndResults(string? state, string? result, RunStageStatus expected) =>
        Assert.Equal(expected, RunStages.MapStatus(state, result, waitingForApproval: false));

    [Fact]
    public void MapStatus_WaitingForApprovalOverridesRunning() =>
        Assert.Equal(RunStageStatus.WaitingForApproval, RunStages.MapStatus("inProgress", null, true));

    [Fact]
    public void MapStatus_WaitingForApprovalDoesNotOverrideCompleted() =>
        Assert.Equal(RunStageStatus.Failed, RunStages.MapStatus("completed", "failed", true));

    [Fact]
    public async Task GetRunStages_InProgressApprovalCheckpoint_MarksStageWaiting()
    {
        var json = Timeline(
            Rec("s1", "Stage", "Build", "completed", "succeeded", "1"),
            Rec("s2", "Stage", "Prod", "inProgress", null, "2"),
            Rec("cp", "Checkpoint", "Checkpoint", "inProgress", null, parent: "s2"),
            Rec("ap", "Checkpoint.Approval", "Checkpoint.Approval", "inProgress", null, parent: "cp"));
        var (c, _) = Make(HttpStatusCode.OK, json);

        var stages = await c.GetRunStagesAsync(1);

        Assert.Equal([RunStageStatus.Succeeded, RunStageStatus.WaitingForApproval], stages.Select(s => s.Status));
    }

    [Fact]
    public async Task GetRunStages_ApprovalInOtherStage_DoesNotAffectSiblings()
    {
        var json = Timeline(
            Rec("s1", "Stage", "A", "inProgress", null, "1"),
            Rec("s2", "Stage", "B", "pending", null, "2"),
            Rec("cp", "Checkpoint", "Checkpoint", "inProgress", null, parent: "s2"),
            Rec("ap", "Checkpoint.Approval", "x", "inProgress", null, parent: "cp"));
        var (c, _) = Make(HttpStatusCode.OK, json);

        var stages = await c.GetRunStagesAsync(1);

        Assert.Equal(RunStageStatus.Running, stages[0].Status);
        Assert.Equal(RunStageStatus.WaitingForApproval, stages[1].Status);
    }

    [Fact]
    public async Task GetRunStages_ToleratesNullsAndMissingRecords()
    {
        var (c, _) = Make(HttpStatusCode.OK, """{"records":[{"type":"Stage","name":null,"state":null,"result":null}]}""");
        var s = Assert.Single(await c.GetRunStagesAsync(1));
        Assert.Equal("", s.Name);
        Assert.Equal(RunStageStatus.Pending, s.Status);

        var (c2, _) = Make(HttpStatusCode.OK, "{}");
        Assert.Empty(await c2.GetRunStagesAsync(1));
    }

    [Fact]
    public async Task GetRunStages_Forbidden_ThrowsWithBuildScopeHint()
    {
        var (c, _) = Make(HttpStatusCode.Forbidden, "{}");

        var ex = await Assert.ThrowsAsync<ApiAuthException>(() => c.GetRunStagesAsync(1));

        Assert.Equal(AzureDevOpsClient.BuildScopeHint, ex.MissingScopeHint);
    }

    [Fact]
    public async Task ListPendingApprovals_RealSample_ParsesRunId()
    {
        var (c, _) = Make(HttpStatusCode.OK, "{\"count\":1,\"value\":[" + Fixture("approval-sample.json") + "]}");

        var a = Assert.Single(await c.ListPendingApprovalsAsync());

        Assert.Equal(2, a.RunId);
        Assert.Equal("rama-aprobacion", a.PipelineName);
    }

    [Fact]
    public async Task ListPendingApprovals_WithoutOwner_RunIdIsNull()
    {
        var (c, _) = Make(HttpStatusCode.OK, """{"count":1,"value":[{"id":"x","status":"pending","pipeline":{"id":"1","name":"p"}}]}""");

        Assert.Null(Assert.Single(await c.ListPendingApprovalsAsync()).RunId);
    }
}
