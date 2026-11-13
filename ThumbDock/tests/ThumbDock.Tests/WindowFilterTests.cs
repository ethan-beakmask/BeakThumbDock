using ThumbDock.Core;
using ThumbDock.Native;

namespace ThumbDock.Tests;

public class WindowFilterTests
{
    private static readonly Guid Desk = Guid.NewGuid();

    private static WindowTraits Normal() => new(
        Visible: true, Title: "Notepad", ExStyle: 0, HasOwner: false, IsOwnProcess: false,
        Cloaked: 0, DesktopId: Desk, IsOnCurrentDesktop: true);

    [Fact]
    public void NormalVisibleWindow_IsCandidate() => Assert.True(WindowFilter.IsCandidate(Normal()));

    [Fact]
    public void Invisible_IsRejected() => Assert.False(WindowFilter.IsCandidate(Normal() with { Visible = false }));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NoTitle_IsRejected(string title) => Assert.False(WindowFilter.IsCandidate(Normal() with { Title = title }));

    [Fact]
    public void ToolWindow_IsRejected() =>
        Assert.False(WindowFilter.IsCandidate(Normal() with { ExStyle = User32.WsExToolWindow }));

    [Fact]
    public void OwnProcess_IsRejected() => Assert.False(WindowFilter.IsCandidate(Normal() with { IsOwnProcess = true }));

    [Fact]
    public void OwnedWindow_IsRejected_UnlessAppWindow()
    {
        Assert.False(WindowFilter.IsCandidate(Normal() with { HasOwner = true }));
        Assert.True(WindowFilter.IsCandidate(Normal() with { HasOwner = true, ExStyle = User32.WsExAppWindow }));
    }

    [Fact]
    public void ShellCloaked_OnOtherDesktop_IsKept() =>
        Assert.True(WindowFilter.IsCandidate(Normal() with { Cloaked = Dwm.CloakedShell, IsOnCurrentDesktop = false }));

    [Fact]
    public void ShellCloaked_WithoutDesktop_IsRejected() =>
        Assert.False(WindowFilter.IsCandidate(Normal() with { Cloaked = Dwm.CloakedShell, DesktopId = null, IsOnCurrentDesktop = false }));

    [Fact]
    public void ShellCloaked_OnCurrentDesktop_IsRejected() => // e.g. suspended UWP frame
        Assert.False(WindowFilter.IsCandidate(Normal() with { Cloaked = Dwm.CloakedShell, IsOnCurrentDesktop = true }));

    [Fact]
    public void AppCloaked_IsRejected() => // DWM_CLOAKED_APP (1) is the app hiding itself
        Assert.False(WindowFilter.IsCandidate(Normal() with { Cloaked = 1, IsOnCurrentDesktop = false }));

    [Fact]
    public void ShellCloaked_UnknownDesktopState_IsRejected() =>
        Assert.False(WindowFilter.IsCandidate(Normal() with { Cloaked = Dwm.CloakedShell, IsOnCurrentDesktop = null }));
}
