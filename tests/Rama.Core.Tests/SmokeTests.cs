using Rama.Core;

namespace Rama.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void ProductName_IsRama()
    {
        Assert.Equal("Rama", RamaInfo.ProductName);
    }
}
