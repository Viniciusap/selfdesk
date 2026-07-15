using SelfDesk.Viewer.Settings;
using Xunit;

namespace SelfDesk.Viewer.Tests;

public class UpdateCheckerTests
{
    [Fact]
    public void ReturnsTagWhenLatestIsNewer()
    {
        Assert.Equal("v0.9.0", UpdateChecker.PickUpdateTag("v0.9.0", "0.8.0"));
    }

    [Fact]
    public void ReturnsNullWhenCurrentIsUpToDate()
    {
        Assert.Null(UpdateChecker.PickUpdateTag("v0.8.0", "0.8.0"));
    }

    [Fact]
    public void ReturnsNullWhenCurrentIsNewer()
    {
        Assert.Null(UpdateChecker.PickUpdateTag("v0.7.0", "0.8.0"));
    }

    [Fact]
    public void ReturnsNullWhenTagMissing()
    {
        Assert.Null(UpdateChecker.PickUpdateTag(null, "0.8.0"));
        Assert.Null(UpdateChecker.PickUpdateTag(string.Empty, "0.8.0"));
    }

    [Fact]
    public void ReturnsNullWhenTagIsUnparseable()
    {
        Assert.Null(UpdateChecker.PickUpdateTag("not-a-version", "0.8.0"));
    }
}
