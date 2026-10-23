using Rama.Core.Azure;
using Rama.Core.Http;

namespace Rama.Core.Tests.Azure;

public class CloneUrlValidationTests
{
    private static AzureRepo Repo(string url) => new("1", "r", null, url, null, null, null);

    [Theory]
    [InlineData("https://contoso@dev.azure.com/contoso/My%20Proj/_git/repo")]
    [InlineData("https://dev.azure.com/o/p/_git/r")]
    [InlineData("https://contoso.visualstudio.com/p/_git/r")]
    public void CloneUrlFor_AcceptsAzureHttpsHosts(string url) =>
        Assert.Equal(url, AzureDevOpsClient.CloneUrlFor(Repo(url)));

    [Theory]
    [InlineData("https://dev.azure.com/x\" --upload-pack=calc")]
    [InlineData("https://dev.azure.com/x\\y")]
    [InlineData("https://dev.azure.com/x'y")]
    [InlineData("-u")]
    [InlineData("--upload-pack=calc")]
    [InlineData("http://dev.azure.com/o/p/_git/r")]
    [InlineData("https://evil.example.com/o/p/_git/r")]
    [InlineData("https://dev.azure.com.evil.com/o/p/_git/r")]
    [InlineData("https://evilvisualstudio.com/o")]
    [InlineData("ssh://dev.azure.com/o")]
    [InlineData("https://dev.azure.com/x\ny")]
    [InlineData("")]
    public void CloneUrlFor_RejectsMaliciousOrForeignUrls(string url) =>
        Assert.Throws<ApiException>(() => AzureDevOpsClient.CloneUrlFor(Repo(url)));
}
