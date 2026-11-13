using ThumbDock.Core;

namespace ThumbDock.Tests;

public class DesktopNamesTests
{
    [Fact]
    public void ParseIds_ReadsConsecutiveGuidsInOrder()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        byte[] raw = [.. a.ToByteArray(), .. b.ToByteArray()];

        Assert.Equal([a, b], DesktopNames.ParseIds(raw));
    }

    [Fact]
    public void ParseIds_IgnoresTrailingPartialBlock()
    {
        var a = Guid.NewGuid();
        byte[] raw = [.. a.ToByteArray(), 1, 2, 3];

        Assert.Equal([a], DesktopNames.ParseIds(raw));
    }

    [Fact]
    public void ParseIds_Empty_ReturnsEmpty() => Assert.Empty(DesktopNames.ParseIds([]));
}
