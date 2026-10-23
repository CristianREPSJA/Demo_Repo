using System.Net;
using System.Text;
using Rama.Core.Azure;
using Rama.Core.Http;
using Rama.Core.Notifications;
using Rama.Core.Reviews;
using Rama.Core.Tests.Support;

namespace Rama.Core.Tests.Reviews;

public class ReviewsTests
{
    private const string Me = "me-id";

    private sealed class RouteHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body)> Calls { get; } = [];
        public Func<HttpRequestMessage, (HttpStatusCode, string)> Respond { get; set; } = _ => (HttpStatusCode.OK, "{}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
            Calls.Add((r.Method, r.RequestUri!.AbsoluteUri, body));
            var (code, json) = Respond(r);
            return new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class RecordingNotifier : INotifier
    {
        public List<NotificationMessage> Sent { get; } = [];
        public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task NotifyAsync(NotificationMessage m, CancellationToken ct = default)
        {
            Sent.Add(m);
            First.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private static AzureDevOpsClient Client(RouteHandler h) => new(new HttpClient(h),
        new AzureConnection("contoso", "Proj"), "pat", new ApiClientOptions { MaxRetries = 0 });
    private static string Pr(int id, string creator, string creatorName, int myVote) => PrTemplate
        .Replace("__ID__", id.ToString()).Replace("__CREATOR__", creator).Replace("__NAME__", creatorName)
        .Replace("__VOTE__", myVote.ToString());

    private const string PrTemplate = """
        {"pullRequestId":__ID__,"title":"T","status":"active","isDraft":false,"mergeStatus":"succeeded",
         "createdBy":{"id":"__CREATOR__","displayName":"__NAME__"},"creationDate":"2026-10-01T10:00:00Z",
         "sourceRefName":"refs/heads/feat","targetRefName":"refs/heads/develop","description":"desc",
         "repository":{"id":"r1","name":"repo","project":{"id":"p1"}},"lastMergeSourceCommit":{"commitId":"c__ID__"},
         "reviewers":[{"id":"me-id","displayName":"Yo","vote":__VOTE__,"isRequired":true},
                      {"id":"o","displayName":"Otra Persona","vote":10}]}
        """;

    private static (HttpStatusCode, string) Route(HttpRequestMessage r)
    {
        var u = r.RequestUri!.AbsoluteUri;
        if (u.Contains("connectionData")) return (HttpStatusCode.OK, """{"authenticatedUser":{"id":"me-id","displayName":"Yo"}}""");
        if (u.Contains("searchCriteria.reviewerId"))
            return (HttpStatusCode.OK, $$"""{"value":[{{Pr(65, "cami", "Camila Rojas", 0)}},{{Pr(66, "x", "Voté", 10)}},{{Pr(67, Me, "Yo", 0)}}]}""");
        if (u.Contains("searchCriteria.creatorId"))
            return (HttpStatusCode.OK, $$"""{"value":[{{Pr(64, Me, "Diego Muñoz", 0)}}]}""");
        if (u.Contains("pipelines/approvals"))
            return (HttpStatusCode.OK, """{"value":[{"id":"a1","status":"pending","pipeline":{"name":"erp-ventas"},"steps":[{"assignedApprover":{"id":"me-id","displayName":"Yo"},"status":"pending"}]}]}""");
        if (u.Contains("policy/evaluations"))
            return (HttpStatusCode.OK, """{"value":[{"status":"running","configuration":{"isBlocking":true,"type":{"displayName":"Build"}}}]}""");
        if (u.EndsWith("/commits?api-version=7.1")) return (HttpStatusCode.OK, """{"value":[{"commitId":"1"},{"commitId":"2"}]}""");
        if (u.Contains("/pullRequests/65?")) return (HttpStatusCode.OK, Pr(65, "cami", "Camila Rojas", 0));
        return (HttpStatusCode.OK, "{}");
    }

    private static ReviewsService Service(RouteHandler h)
    {
        var c = Client(h);
        return new ReviewsService(c, new PendingWorkService(c));
    }

    [Fact]
    public async Task Load_GroupsPendingPrsDeploymentsAndMine()
    {
        var snap = await Service(new RouteHandler { Respond = Route }).LoadAsync();

        Assert.Equal(["#65"], snap.PullRequests.Select(i => i.Number));
        Assert.Equal(["erp-ventas"], snap.Deployments.Select(i => i.Title));
        Assert.Equal(["#64"], snap.MyPullRequests.Select(i => i.Number));
        Assert.Equal(2, snap.PendingCount);
        Assert.Equal("feat → develop", snap.PullRequests[0].Meta);
    }

    [Fact]
    public async Task LoadDetail_MapsPoliciesReviewersAndBlocksComplete()
    {
        var svc = Service(new RouteHandler { Respond = Route });
        var snap = await svc.LoadAsync();
        var d = await svc.LoadDetailAsync(snap.PullRequests[0].Pr!, Me);

        Assert.Equal(2, d.CommitCount);
        Assert.Equal("2 commits", d.CommitsText);
        Assert.Equal("feat", d.Source);
        Assert.Equal("desc", d.Pr.Description);
        Assert.Equal("En ejecución", d.Policies[0].StatusText);
        Assert.Equal(Tone.Warn, d.Policies[0].Tone);
        Assert.True(d.Reviewers[0].IsMe);
        Assert.Equal("Sin votar", d.Reviewers[0].VoteLabel);
        Assert.Equal("Aprobó", d.Reviewers[1].VoteLabel);
        Assert.False(d.CanComplete);
        Assert.Equal("Faltan políticas (1)", d.CompleteBlockReason);
    }

    [Fact]
    public async Task LoadDetail_PolicyServerError_StillReturnsDetailAndAllowsComplete()
    {
        var svc = Service(new RouteHandler
        {
            Respond = r => r.RequestUri!.AbsoluteUri.Contains("policy/") ? (HttpStatusCode.InternalServerError, "") : Route(r),
        });
        var snap = await svc.LoadAsync();
        var d = await svc.LoadDetailAsync(snap.PullRequests[0].Pr!, Me);
        Assert.Empty(d.Policies);
        Assert.True(d.CanComplete);
    }

    [Fact]
    public async Task LoadDetail_PolicyAuthError_Propagates()
    {
        var svc = Service(new RouteHandler
        {
            Respond = r => r.RequestUri!.AbsoluteUri.Contains("policy/") ? (HttpStatusCode.Forbidden, "") : Route(r),
        });
        var snap = await svc.LoadAsync();
        var ex = await Assert.ThrowsAsync<ApiAuthException>(() => svc.LoadDetailAsync(snap.PullRequests[0].Pr!, Me));
        Assert.Contains("Code", ex.MissingScopeHint);
    }

    [Fact]
    public async Task Vote_PutsVoteForMe()
    {
        var h = new RouteHandler { Respond = Route };
        var svc = Service(h);
        var snap = await svc.LoadAsync();
        await svc.VoteAsync(snap.PullRequests[0].Pr!, Me, Vote.Rejected);
        var call = h.Calls.Last();
        Assert.Equal(HttpMethod.Put, call.Method);
        Assert.Contains("/pullRequests/65/reviewers/me-id", call.Url);
        Assert.Contains("\"vote\":-10", call.Body);
    }

    [Fact]
    public async Task DecideDeployment_SendsApprovalWithTrimmedComment()
    {
        var h = new RouteHandler { Respond = Route };
        var svc = Service(h);
        var snap = await svc.LoadAsync();
        await svc.DecideDeploymentAsync(snap.Deployments[0].Approval!, true, "  ok  ");
        Assert.Equal(HttpMethod.Patch, h.Calls.Last().Method);
        Assert.Contains("\"comment\":\"ok\"", h.Calls.Last().Body);
        await svc.DecideDeploymentAsync(snap.Deployments[0].Approval!, false, "  ");
        Assert.DoesNotContain("comment", h.Calls.Last().Body);
    }

    [Fact]
    public void VoteOptions_OnlyRejectNeedsConfirm_AndMapToVotes()
    {
        Assert.Equal([Vote.Approved, Vote.ApprovedWithSuggestions, Vote.WaitingForAuthor, Vote.Rejected],
            ReviewMapper.VoteOptions.Select(o => o.Vote));
        Assert.Equal([Vote.Rejected], ReviewMapper.VoteOptions.Where(o => o.NeedsConfirm).Select(o => o.Vote));
    }

    [Theory]
    [InlineData(Vote.Approved, "Aprobó", Tone.Good)]
    [InlineData(Vote.ApprovedWithSuggestions, "Aprobó con sugerencias", Tone.Good)]
    [InlineData(Vote.WaitingForAuthor, "Esperando al autor", Tone.Warn)]
    [InlineData(Vote.Rejected, "Rechazó", Tone.Bad)]
    [InlineData(Vote.NoVote, "Sin votar", Tone.Neutral)]
    public void VoteBadge_MapsLabelAndTone(Vote v, string label, Tone tone) =>
        Assert.Equal((label, tone), ReviewMapper.VoteBadge(v));

    [Theory]
    [InlineData(PolicyStatus.Approved, Tone.Good)]
    [InlineData(PolicyStatus.Rejected, Tone.Bad)]
    [InlineData(PolicyStatus.Broken, Tone.Bad)]
    [InlineData(PolicyStatus.Queued, Tone.Warn)]
    [InlineData(PolicyStatus.NotApplicable, Tone.Neutral)]
    [InlineData(PolicyStatus.Unknown, Tone.Warn)]
    public void PolicyBadge_MapsTone(PolicyStatus s, Tone tone) => Assert.Equal(tone, ReviewMapper.PolicyBadge(s).Tone);

    [Theory]
    [InlineData("Camila Rojas", "CR")]
    [InlineData("diego", "D")]
    [InlineData("Ana María de la Cruz", "AC")]
    [InlineData("", "?")]
    public void Initials_Works(string name, string expected) => Assert.Equal(expected, ReviewMapper.Initials(name));

    [Fact]
    public void BranchName_And_CommitsText()
    {
        Assert.Equal("develop", ReviewMapper.BranchName("refs/heads/develop"));
        Assert.Equal("x", ReviewMapper.BranchName("x"));
        Assert.Equal("1 commit", ReviewMapper.CommitsText(1));
        Assert.Equal("4 commits", ReviewMapper.CommitsText(4));
    }

    [Fact]
    public void CompleteBlockReason_CoversDraftConflictsAndReady()
    {
        PullRequestInfo Make(bool draft, string? merge) => new(1, "t", "active", draft, "a", "A", null, "", "", "r", "r",
            null, null, merge, [], "");
        var ok = PolicySummary.From([]);
        Assert.Equal("Es un borrador", ReviewMapper.CompleteBlockReason(Make(true, null), ok));
        Assert.Equal("Hay conflictos de fusión", ReviewMapper.CompleteBlockReason(Make(false, "conflicts"), ok));
        Assert.Null(ReviewMapper.CompleteBlockReason(Make(false, "succeeded"), ok));
    }

    [Fact]
    public void MyPrPill_ReflectsVotes()
    {
        PullRequestInfo Make(params Vote[] votes) => new(1, "t", "active", false, "a", "A", null, "", "", "r", "r", null,
            null, null, votes.Select(v => new PullRequestReviewer("i", "n", v, false, false)).ToList(), "");
        Assert.Equal("Esperando revisores", ReviewMapper.MyPrPill(Make()).Text);
        Assert.Equal("Esperando revisores", ReviewMapper.MyPrPill(Make(Vote.Approved, Vote.NoVote)).Text);
        Assert.Equal("Aprobado", ReviewMapper.MyPrPill(Make(Vote.Approved, Vote.ApprovedWithSuggestions)).Text);
        Assert.Equal("Esperando al autor", ReviewMapper.MyPrPill(Make(Vote.Approved, Vote.WaitingForAuthor)).Text);
        Assert.Equal("Rechazado", ReviewMapper.MyPrPill(Make(Vote.WaitingForAuthor, Vote.Rejected)).Text);
    }

    [Fact]
    public void RelativeTime_FormatsUnits()
    {
        var now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("hace un momento", RelativeTime.Ago(now.AddSeconds(-10), now));
        Assert.Equal("hace 10 s", RelativeTime.Ago(now.AddSeconds(-10), now, true));
        Assert.Equal("hace 25 min", RelativeTime.Ago(now.AddMinutes(-25), now));
        Assert.Equal("hace 3 h", RelativeTime.Ago(now.AddHours(-3), now));
        Assert.Equal("hace 2 d", RelativeTime.Ago(now.AddDays(-2), now));
        Assert.Equal("hace un momento", RelativeTime.Ago(now.AddMinutes(5), now));
    }

    [Fact]
    public async Task ConfirmFlow_RunsOnlyAfterConfirm_AndCancelDiscards()
    {
        var flow = new ConfirmFlow();
        var runs = 0;
        Assert.False(await flow.ConfirmAsync());

        flow.Request("¿Aprobar el despliegue a Producción?", _ => { runs++; return Task.CompletedTask; });
        Assert.True(flow.IsPending);
        Assert.Equal(0, runs);
        Assert.True(await flow.ConfirmAsync());
        Assert.Equal(1, runs);
        Assert.False(flow.IsPending);

        flow.Request("x", _ => { runs++; return Task.CompletedTask; });
        flow.Cancel();
        Assert.False(await flow.ConfirmAsync());
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task ConfirmFlow_ClearsPending_WhenActionFails()
    {
        var flow = new ConfirmFlow();
        flow.Request("x", _ => throw new InvalidOperationException("boom"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.ConfirmAsync());
        Assert.False(flow.IsPending);
    }

    [Fact]
    public void Notifications_UseExpectedTexts()
    {
        var pr = ReviewMapper.ToPendingPr(new PullRequestInfo(65, "t", "active", false, "c", "Camila Rojas", null, "", "",
            "r", "repo", null, null, null, [], "https://x/pr"), "k1");
        var m = ReviewNotifications.For(pr);
        Assert.Equal("Camila Rojas te pidió revisar el PR #65", m.Body);
        Assert.Equal("https://x/pr", m.ActionUrl);
        Assert.Equal("k1", m.Id);

        var dep = ReviewMapper.ToDeployment(new PipelineApproval("a", "pending", "erp-ventas", null, null, []), "k2");
        Assert.Equal("erp-ventas espera tu aprobación", ReviewNotifications.For(dep).Body);
    }

    [Fact]
    public async Task Monitor_NotifiesOnlyNewItems_AndSeedsFirstLoadSilently()
    {
        var h = new RouteHandler { Respond = Route };
        var svc = Service(h);
        var notifier = new RecordingNotifier();
        var extra = false;
        await using var mon = new ReviewsMonitor(async ct =>
        {
            var s = await svc.LoadAsync(ct);
            if (!extra) return s;
            var added = ReviewMapper.ToPendingPr(s.PullRequests[0].Pr! with { Id = 99, Title = "nuevo" }, "pr:new");
            return s with { PullRequests = [.. s.PullRequests, added] };
        }, notifier);

        await mon.RefreshAsync();
        Assert.Empty(notifier.Sent);
        Assert.Equal(2, mon.Snapshot!.PendingCount);

        await mon.RefreshAsync();
        Assert.Empty(notifier.Sent);

        extra = true;
        await mon.RefreshAsync();
        Assert.Single(notifier.Sent);
        Assert.Contains("#99", notifier.Sent[0].Body);

        await mon.RefreshAsync();
        Assert.Single(notifier.Sent);
    }

    [Fact]
    public async Task Monitor_NotifyInitial_SendsEverythingOnce()
    {
        var svc = Service(new RouteHandler { Respond = Route });
        var notifier = new RecordingNotifier();
        await using var mon = new ReviewsMonitor(ct => svc.LoadAsync(ct), notifier, notifyInitial: true);
        await mon.RefreshAsync();
        await mon.RefreshAsync();
        Assert.Equal(2, notifier.Sent.Count);
    }

    [Fact]
    public async Task Monitor_Failure_KeepsLastSnapshotAndRecordsError_ThenRecovers()
    {
        var fail = false;
        var svc = Service(new RouteHandler { Respond = Route });
        var updates = 0;
        await using var mon = new ReviewsMonitor(ct => fail
            ? Task.FromException<ReviewsSnapshot>(new ApiAuthException(HttpStatusCode.Forbidden, "hint"))
            : svc.LoadAsync(ct), NullNotifier.Instance);
        mon.Updated += () => updates++;

        await mon.RefreshAsync();
        fail = true;
        await mon.RefreshAsync();
        Assert.IsType<ApiAuthException>(mon.LastError);
        Assert.NotNull(mon.Snapshot);

        fail = false;
        await mon.RefreshAsync();
        Assert.Null(mon.LastError);
        Assert.Equal(3, updates);
    }

    [Fact]
    public async Task Monitor_PollerTick_LoadsAndNotifies()
    {
        var calls = 0;
        var notifier = new RecordingNotifier();
        var svc = Service(new RouteHandler { Respond = Route });
        var time = new FakeTimeProvider();
        var seeded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var mon = new ReviewsMonitor(async ct =>
        {
            var s = await svc.LoadAsync(ct);
            if (Interlocked.Increment(ref calls) < 2) return s;
            return s with { Deployments = [.. s.Deployments, ReviewMapper.ToDeployment(new PipelineApproval("z", "pending", "api", null, null, []), "approval:z")] };
        }, notifier, new Rama.Core.Polling.PollerOptions(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120)), time);
        mon.Updated += () => seeded.TrySetResult();

        mon.Start();
        await seeded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromSeconds(60));
        await notifier.First.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await mon.StopAsync();
        Assert.Contains(notifier.Sent, m => m.Body == "api espera tu aprobación");
    }
}
