using Rama.Core.Http;
using Rama.Core.Jira;

namespace Rama.Core.Tests.Jira;

public class BoardConfigTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Jira", "Fixtures", name));

    private static JiraClient Client(string body) => new(new HttpClient(new JiraClientTests.FakeHandler((_, _) => JiraClientTests.Json(body))),
        "https://acme.atlassian.net", "a@x.cl", "t", new ApiClientOptions { MaxRetries = 0 });

    [Fact]
    public async Task RealConfig_ReadsEstimationFieldAndColumns()
    {
        var cfg = await Client(Fixture("jira-config-sanitized.json")).GetBoardConfigurationAsync(18);
        Assert.Equal("customfield_10024", cfg.EstimationField);
        Assert.Equal("Por Desarrollar", cfg.Columns[0].Name);
        Assert.Equal(["10109"], cfg.Columns[0].StatusIds);
        Assert.True(cfg.Columns.Count >= 8);
    }

    [Theory]
    [InlineData("""{"columnConfig":{"columns":[]}}""")]
    [InlineData("""{"estimation":{"type":"issueCount"}}""")]
    [InlineData("""{"estimation":{"type":"field","field":{}}}""")]
    [InlineData("""{"estimation":{"type":"none","field":{"fieldId":"customfield_1"}}}""")]
    public async Task OtherEstimation_YieldsNull(string json) =>
        Assert.Null((await Client(json).GetBoardConfigurationAsync(1)).EstimationField);

    [Fact]
    public async Task RealSearch_MapsIssuesFromFixture()
    {
        var h = new JiraClientTests.FakeHandler((_, _) => JiraClientTests.Json(Fixture("jira-search-sanitized.json")),
            (_, _) => JiraClientTests.Json("""{"issues":[]}"""));
        var c = new JiraClient(new HttpClient(h), "https://acme.atlassian.net", "a@x.cl", "t", new ApiClientOptions { MaxRetries = 0 });
        var issues = await c.SearchIssuesAsync("project = DFE", "customfield_10016");
        Assert.Equal(5, issues.Count);
        Assert.Equal("DFE-3434", issues[0].Key);
        Assert.All(issues, i => Assert.False(string.IsNullOrEmpty(i.StatusId)));
        Assert.All(issues, i => Assert.Null(i.StoryPoints));
    }

    [Fact]
    public async Task GetBoardColumns_StillWorksViaConfiguration()
    {
        var cols = await Client(Fixture("jira-config-sanitized.json")).GetBoardColumnsAsync(18);
        Assert.NotEmpty(cols);
    }
}
