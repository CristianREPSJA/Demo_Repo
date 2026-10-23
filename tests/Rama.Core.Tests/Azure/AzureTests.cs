using System.Net;
using System.Text;
using Rama.Core.Azure;
using Rama.Core.Http;
using Rama.Core.Polling;
using Rama.Core.Secrets;

namespace Rama.Core.Tests.Azure;

public class AzureTests
{
    private const string Me = "me-id";

    private sealed class RouteHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body, string? Auth)> Calls { get; } = [];
        public Func<HttpRequestMessage, (HttpStatusCode, string)> Respond { get; set; } = _ => (HttpStatusCode.OK, "{}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
            Calls.Add((r.Method, r.RequestUri!.AbsoluteUri, body, r.Headers.Authorization?.ToString()));
            var (code, json) = Respond(r);
            return new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static (AzureDevOpsClient Client, RouteHandler Handler) Make(Func<HttpRequestMessage, (HttpStatusCode, string)> respond)
    {
        var h = new RouteHandler { Respond = respond };
        var c = new AzureDevOpsClient(new HttpClient(h), new AzureConnection("contoso", "My Proj"), "pat123",
            new ApiClientOptions { MaxRetries = 0 });
        return (c, h);
    }

    private static (HttpStatusCode, string) Ok(string json) => (HttpStatusCode.OK, json);

    private const string PrJson = """
    {"pullRequestId":42,"title":"Fix bug","status":"active","isDraft":false,"mergeStatus":"succeeded",
     "createdBy":{"id":"author","displayName":"Ana"},"creationDate":"2026-10-01T10:00:00Z",
     "sourceRefName":"refs/heads/f","targetRefName":"refs/heads/main",
     "repository":{"id":"r1","name":"repo one","project":{"id":"p1","name":"My Proj"}},
     "lastMergeSourceCommit":{"commitId":"abc"},
     "reviewers":[{"id":"me-id","displayName":"Yo","vote":0,"isRequired":true},
                  {"id":"g1","displayName":"Team","vote":-5,"isContainer":true}]}
    """;

    [Fact]
    public async Task GetCurrentUser_ParsesIdentityAndSendsPatBasicAuth()
    {
        var (c, h) = Make(_ => Ok("""{"authenticatedUser":{"id":"me-id","displayName":"Yo"}}"""));
        var u = await c.GetCurrentUserAsync();
        Assert.Equal(new AzureUser("me-id", "Yo"), u);
        Assert.Equal("https://dev.azure.com/contoso/_apis/connectionData?api-version=7.1-preview", h.Calls[0].Url);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(":pat123")), h.Calls[0].Auth);
    }

    [Fact]
    public async Task GetCurrentUser_Throws_WhenNoUser()
    {
        var (c, _) = Make(_ => Ok("{}"));
        await Assert.ThrowsAsync<ApiException>(() => c.GetCurrentUserAsync());
    }

    [Fact]
    public async Task ListRepos_MapsFieldsAndCloneUrl()
    {
        var (c, h) = Make(_ => Ok("""
        {"count":1,"value":[{"id":"r1","name":"repo","defaultBranch":"refs/heads/main",
        "remoteUrl":"https://contoso@dev.azure.com/contoso/My%20Proj/_git/repo","size":1234,"project":{"id":"p1"}}]}
        """));
        var repos = await c.ListReposAsync();
        var r = Assert.Single(repos);
        Assert.Equal("repo", r.Name);
        Assert.Equal(1234, r.Size);
        Assert.Equal("p1", r.ProjectId);
        Assert.Equal(r.RemoteUrl, AzureDevOpsClient.CloneUrlFor(r));
        Assert.Equal("https://dev.azure.com/contoso/My%20Proj/_apis/git/repositories?api-version=7.1", h.Calls[0].Url);
    }

    [Fact]
    public async Task ListPullRequests_AppliesFiltersAndMapsReviewers()
    {
        var (c, h) = Make(_ => Ok($$"""{"count":1,"value":[{{PrJson}}]}"""));
        var prs = await c.ListPullRequestsAsync("me-id", "author");
        Assert.Contains("searchCriteria.status=active", h.Calls[0].Url);
        Assert.Contains("searchCriteria.reviewerId=me-id", h.Calls[0].Url);
        Assert.Contains("searchCriteria.creatorId=author", h.Calls[0].Url);
        var pr = Assert.Single(prs);
        Assert.Equal("https://dev.azure.com/contoso/My%20Proj/_git/repo%20one/pullrequest/42", pr.WebUrl);
        Assert.Equal(Vote.WaitingForAuthor, pr.Reviewers[1].Vote);
        Assert.True(pr.Reviewers[1].IsGroup);
        Assert.Equal("Sin votar", pr.Reviewers[0].VoteLabel);
    }

    [Fact]
    public async Task GetPullRequest_ReturnsDetail_AndThrowsOnEmpty()
    {
        var (c, h) = Make(_ => Ok(PrJson));
        var pr = await c.GetPullRequestAsync("r1", 42);
        Assert.Equal("abc", pr.SourceCommitId);
        Assert.Equal("p1", pr.ProjectId);
        Assert.EndsWith("/_apis/git/repositories/r1/pullRequests/42?api-version=7.1", h.Calls[0].Url);

        var (c2, _) = Make(_ => Ok("null"));
        await Assert.ThrowsAsync<ApiException>(() => c2.GetPullRequestAsync("r1", 1));
    }

    [Fact]
    public async Task GetCommitCount_CountsValues()
    {
        var (c, _) = Make(_ => Ok("""{"value":[{"commitId":"1"},{"commitId":"2"}]}"""));
        Assert.Equal(2, await c.GetCommitCountAsync("r1", 42));
        var (c2, _) = Make(_ => Ok("{}"));
        Assert.Equal(0, await c2.GetCommitCountAsync("r1", 42));
    }

    [Theory]
    [InlineData(Vote.Approved, 10)]
    [InlineData(Vote.ApprovedWithSuggestions, 5)]
    [InlineData(Vote.NoVote, 0)]
    [InlineData(Vote.WaitingForAuthor, -5)]
    [InlineData(Vote.Rejected, -10)]
    public async Task Vote_PutsBody(Vote vote, int expected)
    {
        var (c, h) = Make(_ => Ok("""{"id":"me-id","vote":0}"""));
        await c.VoteAsync("r1", 42, "me-id", vote);
        var call = h.Calls[0];
        Assert.Equal(HttpMethod.Put, call.Method);
        Assert.EndsWith("/pullRequests/42/reviewers/me-id?api-version=7.1", call.Url);
        Assert.Contains($"\"vote\":{expected}", call.Body);
        Assert.Contains("\"id\":\"me-id\"", call.Body);
    }

    [Fact]
    public void VoteLabels_AreSpanish_AndUnknownIsNoVote()
    {
        Assert.Equal("Aprobó", Vote.Approved.Label());
        Assert.Equal("Aprobó con sugerencias", Vote.ApprovedWithSuggestions.Label());
        Assert.Equal("Esperando al autor", Vote.WaitingForAuthor.Label());
        Assert.Equal("Rechazó", Vote.Rejected.Label());
        Assert.Equal(Vote.NoVote, VoteExtensions.FromInt(7));
        Assert.Equal(Vote.Rejected, VoteExtensions.FromInt(-10));
    }

    [Fact]
    public async Task PolicySummary_ComputesMissingAndCanComplete()
    {
        var (c, h) = Make(_ => Ok("""
        {"count":5,"value":[
         {"status":"approved","configuration":{"isBlocking":true,"type":{"displayName":"Minimum reviewers"}}},
         {"status":"running","configuration":{"isBlocking":true,"type":{"displayName":"Build"}}},
         {"status":"rejected","configuration":{"isBlocking":true,"type":{"displayName":"Comments"}}},
         {"status":"queued","configuration":{"isBlocking":false,"type":{"displayName":"Optional"}}},
         {"status":"notApplicable","configuration":{"isBlocking":true}}]}
        """));
        var s = await c.GetPolicySummaryAsync("p1", 42);
        Assert.Contains("artifactId=vstfs%3A%2F%2F%2FCodeReview%2FCodeReviewId%2Fp1%2F42", h.Calls[0].Url);
        Assert.Contains("api-version=7.1-preview.1", h.Calls[0].Url);
        Assert.Equal(2, s.Missing);
        Assert.Equal(1, s.Failed);
        Assert.False(s.CanComplete);
        Assert.Equal("Faltan políticas (2)", s.Label);
        Assert.Equal("Build", s.Items[1].Name);
        Assert.Equal("Política", s.Items[4].Name);
    }

    [Fact]
    public void PolicySummary_CanComplete_WhenAllBlockingDone()
    {
        var s = PolicySummary.From([new("a", PolicyStatus.Approved, true), new("b", PolicyStatus.Broken, false)]);
        Assert.True(s.CanComplete);
        Assert.Equal("Listo para completar", s.Label);
        Assert.Equal(PolicyStatus.Unknown, AzureDevOpsClient.ParsePolicyStatus("zzz"));
        Assert.Equal(PolicyStatus.Unknown, AzureDevOpsClient.ParsePolicyStatus(null));
        Assert.Equal(PolicyStatus.Broken, AzureDevOpsClient.ParsePolicyStatus("Broken"));
    }

    private const string ApprovalsJson = """
    {"count":3,"value":[
     {"id":"a1","status":"pending","instructions":"Deploy prod","createdOn":"2026-10-02T00:00:00Z",
      "pipeline":{"id":"9","name":"Release"},
      "steps":[{"assignedApprover":{"id":"me-id","displayName":"Yo"},"status":"pending"}]},
     {"id":"a2","status":"pending","pipeline":{"name":"Other"},
      "steps":[{"assignedApprover":{"id":"someone","displayName":"X"},"status":"pending"}]},
     {"id":"a3","status":"pending","pipeline":{"name":"Group"},
      "steps":[{"assignedApprover":{"id":"grp","displayName":"G","isContainer":true},"status":"pending"}]}]}
    """;

    [Fact]
    public async Task Approvals_FilterByUserAndGroupMembership()
    {
        var (c, h) = Make(_ => Ok(ApprovalsJson));
        var mine = await c.ListApprovalsForUserAsync(Me);
        Assert.Equal(["a1"], mine.Select(a => a.Id));
        Assert.Contains("approvals?state=pending&$expand=steps&api-version=7.1", h.Calls[0].Url);

        var viaGroup = await c.ListApprovalsForUserAsync(Me, new HashSet<string> { "grp" });
        Assert.Equal(["a1", "a3"], viaGroup.Select(a => a.Id));
        Assert.True(viaGroup[1].Steps[0].IsGroup);
    }

    [Fact]
    public async Task Approvals_IgnoreCompletedSteps()
    {
        var (c, _) = Make(_ => Ok("""{"value":[{"id":"x","steps":[{"assignedApprover":{"id":"me-id"},"status":"approved"}]}]}"""));
        Assert.Empty(await c.ListApprovalsForUserAsync(Me));
    }

    [Theory]
    [InlineData(true, "approved")]
    [InlineData(false, "rejected")]
    public async Task UpdateApproval_PatchesArray(bool approve, string status)
    {
        var (c, h) = Make(_ => Ok("{}"));
        await c.UpdateApprovalAsync("a1", approve, "ok");
        var call = h.Calls[0];
        Assert.Equal(HttpMethod.Patch, call.Method);
        Assert.EndsWith("/_apis/pipelines/approvals?api-version=7.1", call.Url);
        Assert.StartsWith("[", call.Body);
        Assert.Contains("\"approvalId\":\"a1\"", call.Body);
        Assert.Contains($"\"status\":\"{status}\"", call.Body);
        Assert.Contains("\"comment\":\"ok\"", call.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AuthErrors_NameMissingScope(HttpStatusCode code)
    {
        var (c, _) = Make(_ => (code, ""));
        var e1 = await Assert.ThrowsAsync<ApiAuthException>(() => c.ListReposAsync());
        Assert.Contains("Code (Read & write)", e1.MissingScopeHint);
        var e2 = await Assert.ThrowsAsync<ApiAuthException>(() => c.ListPendingApprovalsAsync());
        Assert.Contains("Build (Read & execute)", e2.MissingScopeHint);
        Assert.Contains("Pipeline Resources (Use)", e2.MissingScopeHint);
        var e3 = await Assert.ThrowsAsync<ApiAuthException>(() => c.UpdateApprovalAsync("a", true));
        Assert.Contains("Pipeline Resources", e3.Message);
    }

    [Fact]
    public async Task Connection_ValidatesAndStoresPat()
    {
        Assert.Throws<ArgumentException>(() => new AzureConnection("bad/org", "p"));
        Assert.Throws<ArgumentException>(() => new AzureConnection("", "p"));
        Assert.Throws<ArgumentException>(() => new AzureConnection("org", " "));
        var conn = new AzureConnection("contoso", "p");
        var store = new InMemorySecretStore();
        Assert.Null(await conn.LoadPatAsync(store));
        await conn.SavePatAsync(store, "secret");
        Assert.Equal("secret", await store.GetAsync("azure-devops", "contoso"));
        Assert.Equal("secret", await conn.LoadPatAsync(store));
        await conn.DeletePatAsync(store);
        Assert.Null(await conn.LoadPatAsync(store));
    }

    private static (PendingWorkService Svc, RouteHandler H) MakePending()
    {
        var (c, h) = Make(r =>
        {
            var u = r.RequestUri!.ToString();
            if (u.Contains("connectionData")) return Ok("""{"authenticatedUser":{"id":"me-id","displayName":"Yo"}}""");
            if (u.Contains("pullrequests")) return Ok($$"""
              {"value":[{{PrJson}},
               {"pullRequestId":43,"title":"Mine","createdBy":{"id":"me-id"},"repository":{"id":"r1","name":"r"},
                "reviewers":[{"id":"me-id","vote":0}]},
               {"pullRequestId":44,"title":"Draft","isDraft":true,"createdBy":{"id":"a"},"repository":{"id":"r1","name":"r"},
                "reviewers":[{"id":"me-id","vote":0}]},
               {"pullRequestId":45,"title":"Voted","createdBy":{"id":"a"},"repository":{"id":"r1","name":"r"},
                "reviewers":[{"id":"me-id","vote":10}]}]}
              """);
            return Ok(ApprovalsJson);
        });
        return (new PendingWorkService(c), h);
    }

    [Fact]
    public async Task PendingWork_CombinesPrsAndApprovals_WithStableIds()
    {
        var (svc, h) = MakePending();
        var first = await svc.GetPendingAsync();
        Assert.Equal(["pr:contoso/My Proj/r1/42@abc", "approval:contoso/My Proj/a1"], first.Select(i => i.Id));
        Assert.Equal(PendingKind.PullRequestReview, first[0].Kind);
        Assert.Equal("#42 Fix bug", first[0].Title);
        Assert.Equal("Deploy prod", first[1].Subtitle);
        var second = await svc.GetPendingAsync();
        Assert.Equal(first.Select(i => i.Id), second.Select(i => i.Id));
        Assert.Single(h.Calls, c => c.Url.Contains("connectionData"));
    }

    [Fact]
    public async Task PendingWork_WorksWithDeduperForNewItems()
    {
        var (svc, _) = MakePending();
        var dedupe = new ItemDeduper<string>();
        var items = await svc.GetPendingAsync();
        Assert.Equal(2, dedupe.Filter(items.Select(i => i.Id)).Count);
        Assert.Empty(dedupe.Filter((await svc.GetPendingAsync()).Select(i => i.Id)));
    }

    [Fact]
    public async Task PendingWork_ApprovalWithoutInstructions_UsesDefaultSubtitle()
    {
        var (c, _) = Make(r => r.RequestUri!.ToString().Contains("connectionData")
            ? Ok("""{"authenticatedUser":{"id":"me-id"}}""")
            : r.RequestUri.ToString().Contains("pullrequests") ? Ok("{}")
            : Ok("""{"value":[{"id":"z","pipeline":{"name":"P"},"steps":[{"assignedApprover":{"id":"me-id"},"status":"pending"}]}]}"""));
        var item = Assert.Single(await new PendingWorkService(c).GetPendingAsync());
        Assert.Equal("Aprobación pendiente", item.Subtitle);
    }
}
