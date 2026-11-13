using ThumbDock.Native;
using Xunit;

namespace ThumbDock.Tests;

public class ProcessPathTests
{
    [Fact]
    public void OwnProcessPathMatchesTheRuntime()
    {
        string? path = ProcessPath.Of((uint)Environment.ProcessId);
        Assert.Equal(Environment.ProcessPath, path, ignoreCase: true);
    }

    [Fact]
    public void MissingProcessGivesNull()
    {
        Assert.Null(ProcessPath.Of(0));
        Assert.Null(ProcessPath.Of(0xFFFFFFF0)); // not a valid process id
    }
}
