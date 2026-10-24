using System.Net;
using System.Text;
using System.Text.Json;
using Rama.Core.Http;
using Rama.Core.Jira;
using Rama.Core.Secrets;

namespace Rama.Core.Tests.Jira;

public class JiraClientTests
{
    internal sealed class FakeHandler(params Func<HttpRequestMessage, string?, HttpResponseMessage>[] steps) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Req, string? Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request, body));
            return steps[Math.Min(Calls.Count, steps.Length) - 1](request, body);
        }
    }

    internal static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string Base = "https://acme.atlassian.net";

    private static (JiraClient Client, FakeHandler Handler) Make(params Func<HttpRequestMessage, string?, HttpResponseMessage>[] steps)
    {
        var h = new FakeHandler(steps);
        return (new JiraClient(new HttpClient(h), Base, "ana@acme.cl", "tok", new ApiClientOptions { MaxRetries = 0 }), h);
    }

    [Fact]
    public async Task Boards_SendsBasicAuthAndParses()
    {
        var (c, h) = Make((_, _) => Json("""{"values":[{"id":7,"name":"VEN board","type":"scrum"},{"id":8}]}"""));
        var boards = await c.GetBoardsAsync("VEN & co");
        Assert.Equal(2, boards.Count);
        Assert.Equal(new JiraBoardInfo(7, "VEN board", "scrum"), boards[0]);
        Assert.Equal("Board 8", boards[1].Name);
        var req = h.Calls[0].Req;
        Assert.Equal($"{Base}/rest/agile/1.0/board?projectKeyOrId=VEN%20%26%20co", req.RequestUri!.AbsoluteUri);
        Assert.Equal("Basic", req.Headers.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("ana@acme.cl:tok")), req.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task BoardColumns_KeepsOrderAndStatusIds()
    {
        var (c, h) = Make((_, _) => Json("""
            {"id":7,"columnConfig":{"columns":[
              {"name":"Por hacer","statuses":[{"id":"10000"}]},
              {"name":"En curso","statuses":[{"id":"3"},{"id":"4"}]},
              {"name":"Hecho"}]}}
            """));
        var cols = await c.GetBoardColumnsAsync(7);
        Assert.Equal(["Por hacer", "En curso", "Hecho"], cols.Select(x => x.Name));
        Assert.Equal(["3", "4"], cols[1].StatusIds);
        Assert.Empty(cols[2].StatusIds);
        Assert.EndsWith("/rest/agile/1.0/board/7/configuration", h.Calls[0].Req.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Search_PaginatesWithNextPageToken_AndMapsFields()
    {
        var (c, h) = Make(
            (_, _) => Json("""
                {"issues":[{"key":"VEN-1","fields":{"summary":"Uno","status":{"id":"3","name":"En curso"},
                 "assignee":{"accountId":"a1","displayName":"Ana"},"issuetype":{"name":"Task"},
                 "priority":{"name":"High"},"customfield_10016":5.5}}],"nextPageToken":"tok2"}
                """),
            (_, _) => Json("""{"issues":[{"key":"VEN-2","fields":{"summary":"Dos","status":{"id":"4","name":"QA"},"assignee":null}}]}"""));
        var issues = await c.SearchIssuesAsync("project = \"VEN\"", "customfield_10016");
        Assert.Equal(2, issues.Count);
        Assert.Equal(5.5, issues[0].StoryPoints);
        Assert.Equal("Ana", issues[0].AssigneeName);
        Assert.Equal("High", issues[0].Priority);
        Assert.Null(issues[1].AssigneeId);
        Assert.Null(issues[1].StoryPoints);

        Assert.Equal(2, h.Calls.Count);
        Assert.EndsWith("/rest/api/3/search/jql", h.Calls[0].Req.RequestUri!.AbsolutePath);
        using var first = JsonDocument.Parse(h.Calls[0].Body!);
        Assert.False(first.RootElement.TryGetProperty("nextPageToken", out _));
        Assert.Contains("customfield_10016", first.RootElement.GetProperty("fields").EnumerateArray().Select(x => x.GetString()));
        using var second = JsonDocument.Parse(h.Calls[1].Body!);
        Assert.Equal("tok2", second.RootElement.GetProperty("nextPageToken").GetString());
    }

    [Fact]
    public async Task Search_StopsAtMaxPages()
    {
        var (c, h) = Make((_, _) => Json("""{"issues":[],"nextPageToken":"again"}"""));
        await c.SearchIssuesAsync("x");
        Assert.Equal(JiraClient.MaxPages, h.Calls.Count);
    }

    [Fact]
    public async Task Search_EmptyBody_ReturnsEmpty()
    {
        var (c, _) = Make((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        Assert.Empty(await c.SearchIssuesAsync("x"));
    }

    [Fact]
    public async Task AssignableUsers_ParsesArray()
    {
        var (c, h) = Make((_, _) => Json("""[{"accountId":"a1","displayName":"Ana","active":true},{"displayName":"x"}]"""));
        var users = await c.GetAssignableUsersAsync("VEN");
        Assert.Single(users);
        Assert.Equal(new JiraUser("a1", "Ana", true), users[0]);
        Assert.Contains("user/assignable/search?project=VEN", h.Calls[0].Req.RequestUri!.ToString());
    }

    [Fact]
    public async Task Transitions_MapsToAndRequiredFields()
    {
        var (c, h) = Make((_, _) => Json("""
            {"transitions":[
              {"id":"31","name":"Hecho","to":{"id":"5","name":"Done"},"fields":{
                "resolution":{"required":true,"name":"Resolution","hasDefaultValue":false,
                  "allowedValues":[{"id":"1","name":"Fixed"},{"id":"2","value":"Won't"},"junk",{"x":1}]},
                "comment":{"required":false},
                "assignee":{"required":true,"name":"Asig","hasDefaultValue":true},
                "other":{"required":true,"defaultValue":{"id":"9"}}}},
              {"id":"bad"}]}
            """));
        var ts = await c.GetTransitionsAsync("VEN-1");
        Assert.Single(ts);
        var t = ts[0];
        Assert.Equal("5", t.ToStatusId);
        Assert.Equal(["Fixed", "Won't"], t.Fields.First(f => f.Key == "resolution").AllowedValues.Select(o => o.Label));
        Assert.Equal(["resolution"], TransitionPlanner.RequiredFields(t).Select(f => f.Key));
        Assert.Contains("expand=transitions.fields", h.Calls[0].Req.RequestUri!.Query);
    }

    [Fact]
    public async Task Transition_PostsBodyWithAndWithoutFields()
    {
        var (c, h) = Make((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        await c.TransitionAsync("VEN-1", "31");
        var fields = new Dictionary<string, JsonElement>
        {
            ["resolution"] = JsonDocument.Parse("""{"id":"1"}""").RootElement.Clone(),
        };
        await c.TransitionAsync("VEN-1", "31", fields);

        using var a = JsonDocument.Parse(h.Calls[0].Body!);
        Assert.Equal("31", a.RootElement.GetProperty("transition").GetProperty("id").GetString());
        Assert.False(a.RootElement.TryGetProperty("fields", out _));
        using var b = JsonDocument.Parse(h.Calls[1].Body!);
        Assert.Equal("1", b.RootElement.GetProperty("fields").GetProperty("resolution").GetProperty("id").GetString());
        Assert.Equal(HttpMethod.Post, h.Calls[0].Req.Method);
    }

    [Fact]
    public async Task Unauthorized_ThrowsAuthExceptionWithHint()
    {
        var (c, _) = Make((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var ex = await Assert.ThrowsAsync<ApiAuthException>(() => c.GetBoardsAsync("VEN"));
        Assert.Contains("API token", ex.Message);
    }

    // Forma real (2026-10-23): la transición exitosa responde 204 vacío con Content-Type text/html; no es error.
    [Fact]
    public async Task Transition_NoContentWithHtmlHeader_IsSuccess()
    {
        var (c, _) = Make((_, _) =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.NoContent) { Content = new ByteArrayContent([]) };
            resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
            return resp;
        });
        await c.TransitionAsync("VEN-1", "31");
    }

    // Forma real (2026-10-23): token con alcance de solo lectura al hacer la transición.
    [Fact]
    public async Task Transition_ScopeMismatch_ExplainsMissingWritePermission()
    {
        var (c, _) = Make((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"code":401,"message":"Unauthorized; scope does not match"}""", Encoding.UTF8, "application/json"),
        });
        var ex = await Assert.ThrowsAsync<ApiAuthException>(() => c.TransitionAsync("VEN-1", "31"));
        Assert.Contains("write:jira-work", ex.Message);
        Assert.DoesNotContain("Revisa el correo", ex.Message);
    }

    [Fact]
    public void BaseUrls_AreValidated()
    {
        Assert.Equal("https://acme.atlassian.net", JiraClient.SiteBaseUrl(" Acme.atlassian.net "));
        Assert.Equal("https://acme.atlassian.net", JiraClient.SiteBaseUrl("acme"));
        Assert.Throws<ArgumentException>(() => JiraClient.SiteBaseUrl("evil.com/x"));
        Assert.Throws<ArgumentException>(() => JiraClient.SiteBaseUrl(""));
        Assert.Equal("https://api.atlassian.com/ex/jira/abc-123", JiraClient.ScopedBaseUrl("abc-123"));
        Assert.Throws<ArgumentException>(() => JiraClient.ScopedBaseUrl("a/b"));
        Assert.Throws<ArgumentException>(() => JiraClient.ScopedBaseUrl(" "));
    }

    [Fact]
    public void Constructor_RejectsHttpAndEmptyEmail()
    {
        var http = new HttpClient(new FakeHandler());
        Assert.Throws<ArgumentException>(() => new JiraClient(http, "http://acme.atlassian.net", "a@b.c", "t"));
        Assert.Throws<ArgumentException>(() => new JiraClient(http, Base, " ", "t"));
    }

    [Fact]
    public async Task Factory_UsesSecretStore_AndOverride()
    {
        var store = new InMemorySecretStore();
        var h = new FakeHandler((_, _) => Json("""{"values":[]}"""));
        var http = new HttpClient(h);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JiraClientFactory.CreateAsync(store, http, "acme", "ana@acme.cl"));

        await JiraClientFactory.SaveTokenAsync(store, "ana@acme.cl", "secret");
        Assert.Equal("secret", await store.GetAsync("jira", "ana@acme.cl"));
        var c = await JiraClientFactory.CreateAsync(store, http, "acme", "ana@acme.cl",
            JiraClient.ScopedBaseUrl("cloud-1"));
        await c.GetBoardsAsync("VEN");
        Assert.StartsWith("https://api.atlassian.com/ex/jira/cloud-1/rest/agile", h.Calls[0].Req.RequestUri!.ToString());

        var c2 = await JiraClientFactory.CreateAsync(store, http, "acme", "ana@acme.cl");
        await c2.GetBoardsAsync("VEN");
        Assert.StartsWith(Base, h.Calls[1].Req.RequestUri!.ToString());

        await JiraClientFactory.DeleteTokenAsync(store, "ana@acme.cl");
        Assert.Null(await store.GetAsync("jira", "ana@acme.cl"));
    }
}
