using ThumbDock.Core;
using ThumbDock.Layout;
using ThumbDock.Native;

namespace ThumbDock.Tests;

public class PlacementPlannerTests
{
    private const string Main = @"\\.\DISPLAY1", Side = @"\\.\DISPLAY2", Third = @"\\.\DISPLAY3";

    private static PlaceMode Want(bool max) => max ? PlaceMode.Maximize : PlaceMode.None;

    private static PlacementAction Single(bool wantMax, bool min, bool max, string current, string? home) =>
        Single(Want(wantMax), min, max, current, home);

    private static PlacementAction Single(PlaceMode want, bool min, bool max, string current, string? home) =>
        PlacementPlanner.Decide(new PlacementInput(want, min, max, current, home, ThumbMonitor: null, FocusMonitor: Main));

    // Dual mode: thumbnails on Main, focus on Side.
    private static PlacementAction Dual(bool wantMax, bool min, bool max, string current, string? home) =>
        Dual(Want(wantMax), min, max, current, home);

    private static PlacementAction Dual(PlaceMode want, bool min, bool max, string current, string? home) =>
        PlacementPlanner.Decide(new PlacementInput(want, min, max, current, home, ThumbMonitor: Main, FocusMonitor: Side));

    [Theory]
    [InlineData(true, false, true, (int)PlacementAction.None)]          // already maximized at home: just activate
    [InlineData(true, false, false, (int)PlacementAction.Maximize)]
    [InlineData(true, true, false, (int)PlacementAction.Maximize)]
    [InlineData(false, false, false, (int)PlacementAction.None)]        // normal window at home: untouched
    [InlineData(false, false, true, (int)PlacementAction.RestoreHome)]
    [InlineData(false, true, false, (int)PlacementAction.RestoreHome)]
    public void Single_AtHome(bool wantMax, bool min, bool max, int expected) =>
        Assert.Equal((PlacementAction)expected, Single(wantMax, min, max, Side, Side));

    [Fact]
    public void Single_AwayFromHome_GoesBack()
    {
        Assert.Equal(PlacementAction.MaximizeAtHome, Single(true, false, true, Main, Side));
        Assert.Equal(PlacementAction.RestoreHome, Single(false, false, false, Main, Side));
    }

    [Fact]
    public void Single_UnknownHome_UsesCurrentMonitor() =>
        Assert.Equal(PlacementAction.None, Single(false, false, false, Third, null));

    [Fact]
    public void Single_NeverMovesWindowOffItsHomeMonitor()
    {
        foreach (bool want in new[] { true, false })
        foreach (bool min in new[] { true, false })
        foreach (bool max in new[] { true, false })
        {
            var a = Single(want, min, max, Side, Side);
            Assert.DoesNotContain(a, new[] { PlacementAction.MaximizeAtHome, PlacementAction.MoveToFocus, PlacementAction.MaximizeOnFocus });
        }
    }

    [Fact]
    public void Dual_WindowOnFocusMonitor_StaysWhereItIs()
    {
        Assert.Equal(PlacementAction.None, Dual(true, false, true, Side, Side));   // e.g. maximized RDP window on the side monitor
        Assert.Equal(PlacementAction.None, Dual(false, false, false, Side, Side));
    }

    [Fact]
    public void Dual_WindowOnThirdMonitor_IsNotPulled() =>
        Assert.Equal(PlacementAction.None, Dual(false, false, false, Third, Third));

    [Fact]
    public void Dual_WindowHomedOnThumbMonitor_GoesToFocusMonitor()
    {
        Assert.Equal(PlacementAction.MoveToFocus, Dual(false, false, false, Main, Main));
        Assert.Equal(PlacementAction.MaximizeOnFocus, Dual(true, false, false, Main, Main));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Fit_AtHome_AlwaysRefits(bool min, bool max)
    {
        Assert.Equal(PlacementAction.Fit, Single(PlaceMode.Fit, min, max, Side, Side));
        Assert.Equal(PlacementAction.Fit, Single(PlaceMode.Fit, min, max, Main, Side)); // away from home: fitted on home
        Assert.Equal(PlacementAction.Fit, Dual(PlaceMode.Fit, min, max, Side, Side));   // focus-monitor window stays there
        Assert.Equal(PlacementAction.Fit, Dual(PlaceMode.Fit, min, max, Third, Third));
    }

    [Fact]
    public void Fit_HomedOnThumbMonitor_FitsOnFocus()
    {
        Assert.Equal(PlacementAction.FitOnFocus, Dual(PlaceMode.Fit, false, false, Main, Main));
        Assert.Equal(PlacementAction.FitOnFocus, Dual(PlaceMode.Fit, false, true, Side, Main));
    }

    [Fact]
    public void Dual_RedirectedWindow_AlreadyOnFocus_IsNotSentBack()
    {
        Assert.Equal(PlacementAction.None, Dual(false, false, false, Side, Main));
        Assert.Equal(PlacementAction.Restore, Dual(false, true, false, Side, Main));
        Assert.Equal(PlacementAction.Maximize, Dual(true, false, false, Side, Main));
        Assert.Equal(PlacementAction.None, Dual(true, false, true, Side, Main));
    }
}

public class LocationTextTests
{
    private static readonly Rectangle Bounds = new(100, 50, 1280, 720);

    [Fact]
    public void Maximized() =>
        Assert.Equal("副螢幕 · 最大化", WindowPreferences.Describe("副螢幕", new User32.WindowPlacement(Bounds, true, false, false), Bounds));

    [Fact]
    public void Normal() =>
        Assert.Equal("主螢幕 · 1280×720 @ (100,50)", WindowPreferences.Describe("主螢幕", new User32.WindowPlacement(Bounds, false, false, false), Bounds));

    [Fact]
    public void MinimizedFromMaximized() =>
        Assert.Equal("主螢幕 · 最小化（原為最大化）", WindowPreferences.Describe("主螢幕", new User32.WindowPlacement(Bounds, false, true, true), Bounds));

    [Fact]
    public void EffectivelyMaximized_CoversMinimizedRestoreToMax()
    {
        Assert.True(new User32.WindowPlacement(Bounds, false, true, true).IsEffectivelyMaximized);
        Assert.False(new User32.WindowPlacement(Bounds, false, true, false).IsEffectivelyMaximized);
    }
}

public class PlaceModeTests
{
    [Fact]
    public void Picker_CyclesThroughAllModes()
    {
        Assert.Equal(PlaceMode.Maximize, WindowPreferences.Next(PlaceMode.None));
        Assert.Equal(PlaceMode.Fit, WindowPreferences.Next(PlaceMode.Maximize));
        Assert.Equal(PlaceMode.None, WindowPreferences.Next(PlaceMode.Fit));
    }

    [Fact]
    public void Labels_AreChinese()
    {
        Assert.Equal("不變", WindowPreferences.Label(PlaceMode.None));
        Assert.Equal("最大化", WindowPreferences.Label(PlaceMode.Maximize));
        Assert.Equal("填滿操作區", WindowPreferences.Label(PlaceMode.Fit));
    }
}
