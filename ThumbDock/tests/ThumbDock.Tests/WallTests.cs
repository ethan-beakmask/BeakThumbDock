using ThumbDock.Core;
using ThumbDock.Layout;

namespace ThumbDock.Tests;

public class WallPlannerTests
{
    private const string Wall = @"\\.\DISPLAY1", Other = @"\\.\DISPLAY2";
    private static readonly Rectangle Normal = new(100, 80, 800, 600);

    private static (string Kind, Rectangle Bounds) Decide(WindowBefore before, bool maximizedNow, string monitorNow = Wall)
    {
        var a = WallPlanner.Decide(before, new WindowNow(maximizedNow, monitorNow), Wall);
        return (a.Kind.ToString(), a.Bounds);
    }

    [Fact]
    public void Normal_window_the_wall_maximized_goes_back_to_its_old_rectangle()
    {
        var (kind, bounds) = Decide(new WindowBefore(false, false, Normal, Wall), maximizedNow: true);
        Assert.Equal("Normal", kind);
        Assert.Equal(Normal, bounds);
    }

    [Fact]
    public void Window_that_was_already_maximized_on_the_wall_monitor_is_left_alone() =>
        Assert.Equal("None", Decide(new WindowBefore(true, false, Normal, Wall), maximizedNow: true).Kind);

    [Fact]
    public void Window_that_was_maximized_on_another_monitor_goes_back_there_maximized()
    {
        var (kind, bounds) = Decide(new WindowBefore(true, false, Normal, Other), maximizedNow: true, monitorNow: Wall);
        Assert.Equal("Maximized", kind);
        Assert.Equal(Normal, bounds);
    }

    [Fact]
    public void Minimized_window_is_restored_to_normal_never_minimized_again()
    {
        var (kind, bounds) = Decide(new WindowBefore(false, true, Normal, Wall), maximizedNow: true);
        Assert.Equal("Normal", kind);
        Assert.Equal(Normal, bounds);
    }

    [Fact]
    public void Window_on_another_monitor_returns_to_that_monitor()
    {
        var elsewhere = new Rectangle(2000, 100, 700, 500);
        Assert.Equal(elsewhere, Decide(new WindowBefore(false, false, elsewhere, Other), maximizedNow: true).Bounds);
    }

    [Fact]
    public void Window_the_user_already_restored_is_not_touched() =>
        Assert.Equal("None", Decide(new WindowBefore(false, false, Normal, Wall), maximizedNow: false).Kind);

    [Theory]
    [InlineData(true, true, "ShowLastSource")]
    [InlineData(true, false, "RaiseWall")]
    [InlineData(false, true, "RaiseWall")]
    [InlineData(false, false, "RaiseWall")]
    public void Hotkey_raises_the_wall_unless_it_is_already_on_top(bool wallOnTop, bool lastUsable, string expected) =>
        Assert.Equal(expected, WallPlanner.Toggle(wallOnTop, lastUsable).ToString());
}

public class SingleFillTests
{
    private const string D1 = @"\\.\DISPLAY1", D2 = @"\\.\DISPLAY2";
    private static readonly MonitorDescriptor M1 = new(D1, new Rectangle(0, 0, 1920, 1080), true);
    private static readonly MonitorDescriptor M2 = new(D2, new Rectangle(1920, 0, 1920, 1080), false);

    [Fact]
    public void Single_layout_keeps_fill_and_is_not_dual()
    {
        var layout = new LayoutModel { Mode = LayoutMode.Single, ThumbMonitor = D1, FocusMonitor = D1, ThumbZone = new ThumbZone { Type = ZoneType.Fill } };
        var z = MonitorService.Resolve(layout, [M1, M2]);
        Assert.False(z.IsDual);
        Assert.Equal(ZoneType.Fill, z.Type);
        Assert.Equal(D1, z.ThumbDevice);
        Assert.Null(z.FallbackReason);
    }

    [Fact]
    public void Single_fill_on_a_missing_monitor_uses_the_primary_and_says_so()
    {
        var layout = new LayoutModel { Mode = LayoutMode.Single, ThumbMonitor = @"\\.\DISPLAY9", ThumbZone = new ThumbZone { Type = ZoneType.Fill } };
        var z = MonitorService.Resolve(layout, [M1]);
        Assert.Equal(D1, z.ThumbDevice);
        Assert.Equal(ZoneType.Fill, z.Type);
        Assert.NotNull(z.FallbackReason);
    }

    [Fact]
    public void Restore_on_return_is_on_by_default_and_survives_a_save()
    {
        Assert.True(new LayoutModel().RestoreOnReturn);
        string dir = Path.Combine(Path.GetTempPath(), "thumbdock-wall-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LayoutStore(dir);
            var layout = DefaultLayouts.Create("牆");
            layout.ThumbZone.Type = ZoneType.Fill;
            layout.RestoreOnReturn = false;
            store.Save(layout);
            var loaded = store.Load("牆")!;
            Assert.False(loaded.RestoreOnReturn);
            Assert.Equal(ZoneType.Fill, loaded.ThumbZone.Type);
            Assert.Equal(LayoutMode.Single, loaded.Mode);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}

public class DockFormConstructionTests
{
    // Regression: the Form base constructor reads CreateParams before DockForm's own constructor body runs,
    // so anything CreateParams uses must cope with a not-yet-assigned zone (0.4.0 crashed at startup because of it).
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    public void DockForm_can_be_constructed_and_reports_wall_correctly(int zoneType, bool dual, bool expectedWall)
    {
        Exception? error = null;
        bool? isWall = null;
        var thread = new Thread(() =>
        {
            try
            {
                var layout = ThumbDock.Layout.DefaultLayouts.Create("t", 4);
                var zone = new ZoneResolution(@"\.\DISPLAY1", @"\.\DISPLAY1", dual, (ThumbDock.Layout.ZoneType)zoneType, ThumbDock.Layout.DockEdge.Left, null);
                using var form = new ThumbDock.UI.DockForm(layout, zone);
                isWall = form.IsWall;
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error);
        Assert.Equal(expectedWall, isWall);
    }
}

public class WallRapidToggleTests
{
    private static readonly WindowBefore Old = new(false, false, new Rectangle(1, 2, 3, 4), "m");
    private static readonly WindowBefore Reread = new(true, false, new Rectangle(0, 0, 9, 9), "m");

    [Fact]
    public void Right_after_a_restore_the_remembered_state_is_kept_not_the_stale_reading()
    {
        Assert.Equal(Old, WallPlanner.PickBefore(Old, ageMs: 100, Reread));
        Assert.Equal(Reread, WallPlanner.PickBefore(Old, ageMs: WallPlanner.RecentRestoreMs, Reread));
        Assert.Equal(Reread, WallPlanner.PickBefore(null, ageMs: 0, Reread));
        Assert.Null(WallPlanner.PickBefore(null, ageMs: 0, null));
    }

    [Theory]
    [InlineData(true, "ShowSource", 100, false)]    // we just showed a window: the wall is not on top, whatever the measurement says
    [InlineData(false, "ReturnToWall", 100, true)]  // we just raised the wall: it counts as on top
    [InlineData(true, "ShowSource", 5000, true)]    // long ago: trust the measurement
    [InlineData(false, "None", 100, false)]
    public void Quick_hotkey_presses_follow_the_last_command(bool measured, string last, long ageMs, bool expected) =>
        Assert.Equal(expected, WallPlanner.EffectiveWallOnTop(measured, Enum.Parse<WallCommand>(last), ageMs));
}
