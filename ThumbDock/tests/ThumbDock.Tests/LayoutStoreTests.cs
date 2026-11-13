using ThumbDock.Core;
using ThumbDock.Layout;

namespace ThumbDock.Tests;

public sealed class LayoutStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ThumbDockTests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // The example from spec section 7, verbatim.
    private const string SpecExample = """
        {
          "name": "監控台",
          "mode": "dual",
          "focusMonitor": "\\\\.\\DISPLAY1",
          "thumbMonitor": "\\\\.\\DISPLAY2",
          "thumbZone": { "type": "dock", "edge": "left", "sizePx": 320 },
          "placeOnActivate": "maximize",
          "crossDesktop": "switch",
          "showTitles": true,
          "slots": [
            {
              "id": 1,
              "rect": { "x": 0.05, "y": 0.02, "w": 0.90, "h": 0.18 },
              "match": { "process": "chrome.exe", "class": "", "titleRegex": "Dashboard" }
            }
          ]
        }
        """;

    [Fact]
    public void Deserialize_SpecExample()
    {
        var l = LayoutStore.Deserialize<LayoutModel>(SpecExample)!;

        Assert.Equal("監控台", l.Name);
        Assert.Equal(LayoutMode.Dual, l.Mode);
        Assert.Equal(@"\\.\DISPLAY1", l.FocusMonitor);
        Assert.Equal(@"\\.\DISPLAY2", l.ThumbMonitor);
        Assert.Equal(ZoneType.Dock, l.ThumbZone.Type);
        Assert.Equal(DockEdge.Left, l.ThumbZone.Edge);
        Assert.Equal(320, l.ThumbZone.SizePx);
        var slot = Assert.Single(l.Slots);
        Assert.Equal(new RelRect(0.05, 0.02, 0.90, 0.18), slot.Rect);
        Assert.Equal(new MatchRule("chrome.exe", "", "Dashboard"), slot.Match);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsEverything()
    {
        var store = new LayoutStore(_dir);
        var layout = DefaultLayouts.Create("工作");
        layout.Mode = LayoutMode.Dual;
        layout.FocusMonitor = @"\\.\DISPLAY2";
        layout.ThumbMonitor = @"\\.\DISPLAY1";
        layout.ThumbZone = new ThumbZone { Type = ZoneType.Fill, Edge = DockEdge.Top, SizePx = 400 };
        layout.Slots[0].Match = new MatchRule("mstsc.exe", "TscShellContainerClass", "伺服器");
        layout.Slots[0].Place = PlaceMode.Fit;
        layout.CrossDesktop = CrossDesktop.Pull;
        layout.AutoRestoreMinimized = true;
        layout.Slots[0].Home = new SavedPlacement(@"\\.\DISPLAY2", 1920, 0, 1280, 720, true);

        Assert.True(store.Save(layout));
        var loaded = store.Load("工作")!;

        Assert.Equal(LayoutStore.Serialize(layout), LayoutStore.Serialize(loaded));
        Assert.Contains("工作", store.ListNames());
    }

    [Fact]
    public void Json_UsesSpecNamesAndReadableChinese()
    {
        var json = LayoutStore.Serialize(DefaultLayouts.Create("default"));
        Assert.Contains("\"name\": \"default\"", json);
        Assert.Contains("\"mode\": \"single\"", json);
        Assert.Contains("\"thumbZone\"", json);
        Assert.Contains("\"sizePx\"", json);
        Assert.DoesNotContain("isEmpty", json);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsNull_AndDoesNotThrow()
    {
        var store = new LayoutStore(_dir);
        Directory.CreateDirectory(Path.Combine(_dir, "config", "layouts"));
        File.WriteAllText(Path.Combine(_dir, "config", "layouts", "壞掉.json"), "{ not json");

        Assert.Null(store.Load("壞掉"));
        Assert.DoesNotContain("壞掉", store.ListNames());
    }

    [Fact]
    public void Load_Missing_ReturnsNull() => Assert.Null(new LayoutStore(_dir).Load("沒有這個"));

    [Fact]
    public void Delete_RemovesFile()
    {
        var store = new LayoutStore(_dir);
        store.Save(DefaultLayouts.Create("暫時"));
        Assert.True(store.Delete("暫時"));
        Assert.False(store.Exists("暫時"));
    }

    [Fact]
    public void Settings_RoundTrip_AndDefaultWhenMissing()
    {
        var store = new LayoutStore(_dir);
        Assert.Equal("Ctrl+Alt+D", store.LoadSettings().Hotkeys.ToggleDock);

        store.SaveSettings(new AppSettings { LastLayout = "監控台", Hotkeys = new HotkeySettings { ToggleDock = "Ctrl+Shift+F12" } });
        var s = store.LoadSettings();
        Assert.Equal("監控台", s.LastLayout);
        Assert.Equal("Ctrl+Shift+F12", s.Hotkeys.ToggleDock);
    }

    [Fact]
    public void Normalize_RepairsBadValues()
    {
        var l = new LayoutModel
        {
            Name = " ",
            Mode = LayoutMode.Single,
            ThumbZone = new ThumbZone { Type = ZoneType.Fill, SizePx = 5 },
            Slots =
            [
                new SlotModel { Id = 3, Rect = new RelRect(2, 2, 0.5, 0.5), Match = new MatchRule() },
                new SlotModel { Id = 3, Rect = new RelRect(0, 0, 0.5, 0.5) },
            ],
        };

        LayoutStore.Normalize(l);

        // A blank name becomes the reserved "default", whose zone is then forced to the fixed left strip (safe mode).
        Assert.Equal("default", l.Name);
        Assert.Equal(ZoneType.Dock, l.ThumbZone.Type);
        Assert.Equal(ThumbZone.DefaultSizePx, l.ThumbZone.SizePx);
        Assert.Equal(DefaultLayouts.DefaultSlotCount, l.Slots.Count);
        Assert.Null(l.Slots[0].Match);                    // empty rule removed
        Assert.NotEqual(l.Slots[0].Id, l.Slots[1].Id);    // duplicate id fixed
        Assert.True(l.Slots[0].Rect.X + l.Slots[0].Rect.W <= 1.0001);
    }

    [Fact]
    public void Normalize_RepairsBadValues_OfOrdinaryLayout()
    {
        var l = new LayoutModel
        {
            Name = "其他",
            ThumbZone = new ThumbZone { Type = ZoneType.Fill, SizePx = 5 },
            Slots = [new SlotModel { Id = 3, Rect = new RelRect(2, 2, 0.5, 0.5) }],
        };
        LayoutStore.Normalize(l);
        Assert.Equal(ZoneType.Fill, l.ThumbZone.Type);   // single-monitor fill is the wall (0.4.0)
        Assert.Equal(80, l.ThumbZone.SizePx);
        Assert.Single(l.Slots);
    }

    [Fact]
    public void Reserved_Default_IsForcedToFixedShape_KeepingSlotOrderAndRules()
    {
        var l = new LayoutModel
        {
            Name = "default", Mode = LayoutMode.Dual,
            ThumbZone = new ThumbZone { Type = ZoneType.Fill, Edge = DockEdge.Top, SizePx = 3000 },
            Grid = new GridSpec { Cols = 4, Rows = 4 },
            Slots = Enumerable.Range(1, 12).Select(i => new SlotModel { Id = 13 - i, Match = i == 1 ? new MatchRule("a.exe") : null }).ToList(),
        };
        LayoutStore.Normalize(l);
        Assert.Equal(LayoutMode.Single, l.Mode);
        Assert.Equal((ZoneType.Dock, DockEdge.Left, 200), (l.ThumbZone.Type, l.ThumbZone.Edge, l.ThumbZone.SizePx));
        Assert.Null(l.Grid);
        Assert.Equal(8, l.Slots.Count);
        Assert.Equal(12, l.Slots[0].Id);                 // order kept
        Assert.NotNull(l.Slots[0].Match);
        Assert.All(l.Slots, s => Assert.Equal(0.01, s.Rect.X));
        Assert.True(l.Slots[1].Rect.Y > l.Slots[0].Rect.Y);
    }

    [Fact]
    public void Safe_flag_is_parsed_and_implies_no_launch()
    {
        var o = CliOptions.Parse(["--safe", "--layout", "x"]);
        Assert.True(o.Safe);
        Assert.True(o.NoLaunch);
        Assert.False(CliOptions.Parse([]).Safe);
    }

    [Theory]
    [InlineData("監控台", "監控台.json")]
    [InlineData("a/b:c", "a_b_c.json")]
    [InlineData("   ", "layout.json")]
    public void FileNameFor_IsSafe(string name, string expected) => Assert.Equal(expected, LayoutStore.FileNameFor(name));

    [Fact]
    public void Clone_IsDeepCopy()
    {
        var a = DefaultLayouts.Create();
        var b = LayoutStore.Clone(a);
        b.Slots[0].Place = PlaceMode.Maximize;
        Assert.Equal(PlaceMode.None, a.Slots[0].Place);
    }

    [Theory]
    [InlineData("true", (int)PlaceMode.Maximize)]
    [InlineData("false", (int)PlaceMode.None)]
    public void Stage2File_MaximizeFlag_BecomesPlace(string flag, int expected)
    {
        string json = $$"""{ "name": "舊", "slots": [ { "id": 1, "rect": { "x": 0, "y": 0, "w": 1, "h": 0.2 }, "maximize": {{flag}} } ] }""";
        var layout = LayoutStore.Normalize(LayoutStore.Deserialize<LayoutModel>(json)!);
        Assert.Equal((PlaceMode)expected, layout.Slots[0].Place);
        Assert.DoesNotContain("\"maximize\":", LayoutStore.Serialize(layout));
    }

    [Fact]
    public void Stage3Fields_UseReadableNames()
    {
        var layout = DefaultLayouts.Create();
        layout.Slots[0].Place = PlaceMode.Fit;
        layout.CrossDesktop = CrossDesktop.Pull;
        string json = LayoutStore.Serialize(layout);
        Assert.Contains("\"place\": \"fit\"", json);
        Assert.Contains("\"crossDesktop\": \"pull\"", json);
        Assert.Contains("\"autoRestoreMinimized\": false", json);
    }
}

public class DefaultLayoutsTests
{
    [Fact]
    public void Create_HasEightNonOverlappingColumnSlots()
    {
        var l = DefaultLayouts.Create();
        Assert.Equal(8, l.Slots.Count);
        Assert.Equal(Enumerable.Range(1, 8), l.Slots.Select(s => s.Id));
        for (int i = 1; i < l.Slots.Count; i++) Assert.True(l.Slots[i].Rect.Y >= l.Slots[i - 1].Rect.Y + l.Slots[i - 1].Rect.H);
    }

    [Theory]
    [InlineData((int)ZoneType.Dock, (int)DockEdge.Top)]
    [InlineData((int)ZoneType.Fill, (int)DockEdge.Left)]
    public void Arrange_KeepsSlotsInsideZone(int type, int edge)
    {
        var slots = DefaultLayouts.Arrange(Enumerable.Range(1, 6).Select(i => new SlotModel { Id = i }).ToList(), (ZoneType)type, (DockEdge)edge);
        Assert.All(slots, s => Assert.True(s.Rect.X >= 0 && s.Rect.Y >= 0 && s.Rect.X + s.Rect.W <= 1.0001 && s.Rect.Y + s.Rect.H <= 1.0001));
    }

    [Fact]
    public void Arrange_Fill_MakesGrid()
    {
        var slots = DefaultLayouts.Arrange(Enumerable.Range(1, 6).Select(i => new SlotModel { Id = i }).ToList(), ZoneType.Fill, DockEdge.Left);
        Assert.Equal(3, slots.Select(s => s.Rect.X).Distinct().Count());
        Assert.Equal(2, slots.Select(s => s.Rect.Y).Distinct().Count());
    }

    [Fact]
    public void LegacyDefaultLayoutIsMigratedToDefault()
    {
        string dir = Path.Combine(Path.GetTempPath(), "thumbdock-mig-" + Guid.NewGuid().ToString("N"));
        string layouts = Path.Combine(dir, "config", "layouts");
        Directory.CreateDirectory(layouts);
        File.WriteAllText(Path.Combine(layouts, "預設.json"), "{ \"name\": \"預設\", \"slots\": [] }");
        File.WriteAllText(Path.Combine(dir, "config", "settings.json"), "{ \"lastLayout\": \"預設\" }");
        try
        {
            var store = new LayoutStore(dir);
            Assert.True(File.Exists(Path.Combine(layouts, "default.json")));
            Assert.True(File.Exists(Path.Combine(layouts, "預設.json.migrated")));
            Assert.Equal("default", store.Load("default")?.Name);
            Assert.Equal("default", store.LoadSettings().LastLayout);
            Assert.True(DefaultLayouts.IsReserved("Default")); // file names ignore case
        }
        finally { Directory.Delete(dir, true); }
    }
}
