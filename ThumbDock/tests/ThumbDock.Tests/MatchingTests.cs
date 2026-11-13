using ThumbDock.Core;
using ThumbDock.Layout;

namespace ThumbDock.Tests;

public class WindowMatcherTests
{
    private static WindowInfo W(nint h, string title, string process = "chrome.exe", string cls = "Chrome_WidgetWin_1") =>
        new(h, title, cls, process, 1, null, true, "目前桌面");

    [Fact]
    public void Matches_ProcessCaseInsensitive_WithOrWithoutExe()
    {
        Assert.True(WindowMatcher.IsMatch(new MatchRule("CHROME"), W(1, "x")));
        Assert.True(WindowMatcher.IsMatch(new MatchRule("chrome.exe"), W(1, "x")));
        Assert.False(WindowMatcher.IsMatch(new MatchRule("msedge.exe"), W(1, "x")));
    }

    [Fact]
    public void Class_IsExact()
    {
        Assert.True(WindowMatcher.IsMatch(new MatchRule("", "Chrome_WidgetWin_1"), W(1, "x")));
        Assert.False(WindowMatcher.IsMatch(new MatchRule("", "chrome_widgetwin_1"), W(1, "x")));
    }

    [Fact]
    public void TitleRegex_IsUsed()
    {
        var rule = new MatchRule("chrome.exe", "", "Dash(board)?");
        Assert.True(WindowMatcher.IsMatch(rule, W(1, "Sales Dashboard - Chrome")));
        Assert.False(WindowMatcher.IsMatch(rule, W(1, "Inbox - Chrome")));
    }

    [Fact]
    public void InvalidRegex_NeverMatches_AndDoesNotThrow()
    {
        Assert.False(WindowMatcher.IsMatch(new MatchRule("", "", "(unclosed"), W(1, "(unclosed")));
        Assert.NotNull(WindowMatcher.ValidateRegex("(unclosed"));
        Assert.Null(WindowMatcher.ValidateRegex("ok.*"));
    }

    [Fact]
    public void EmptyRule_NeverMatches() => Assert.False(WindowMatcher.IsMatch(new MatchRule(), W(1, "x")));

    [Fact]
    public void FindMatch_SkipsTakenWindows()
    {
        var windows = new[] { W(1, "A"), W(2, "B") };
        Assert.Equal(2, WindowMatcher.FindMatch(new MatchRule("chrome"), windows, new HashSet<nint> { 1 }));
        Assert.Equal(0, WindowMatcher.FindMatch(new MatchRule("chrome"), windows, new HashSet<nint> { 1, 2 }));
    }

    [Fact]
    public void CreateRule_IsAsLooseAsPossible()
    {
        var notepad = W(1, "a.txt - 記事本", "notepad.exe", "Notepad");
        var chrome1 = W(2, "Sales (Q1) Dashboard");
        var chrome2 = W(3, "Inbox");
        var chromeAlert = W(4, "Alert", "chrome.exe", "Chrome_Alert");
        var all = new[] { notepad, chrome1, chrome2, chromeAlert };

        Assert.Equal(new MatchRule("notepad.exe"), WindowMatcher.CreateRule(notepad, all));                    // only window of its process
        Assert.Equal(new MatchRule("chrome.exe", "Chrome_Alert"), WindowMatcher.CreateRule(chromeAlert, all)); // class tells it apart
        var rule = WindowMatcher.CreateRule(chrome1, all);
        Assert.Equal(@"Sales\ \(Q1\)\ Dashboard", rule.TitleRegex);
        Assert.True(WindowMatcher.IsMatch(rule, chrome1));
        Assert.False(WindowMatcher.IsMatch(rule, chrome2));
    }
}

public class SlotBinderTests
{
    private static WindowInfo W(nint h, string process, string title = "t") => new(h, title, "C", process, 1, null, true, "");

    [Fact]
    public void Plan_BindsWaitingSlotsByRule_WithoutDuplicates()
    {
        var rules = new MatchRule?[] { new("chrome"), new("chrome"), null, new("notepad") };
        var bound = new nint[] { 0, 0, 0, 0 };
        var windows = new[] { W(10, "chrome.exe"), W(11, "chrome.exe"), W(12, "notepad.exe") };

        var plan = SlotBinder.Plan(rules, bound, windows);

        Assert.Equal([(0, (nint)10), (1, (nint)11), (3, (nint)12)], plan);
    }

    [Fact]
    public void Plan_SkipsBoundSlots_AndWindowsAlreadyUsed()
    {
        var rules = new MatchRule?[] { new("chrome"), new("chrome") };
        var bound = new nint[] { 10, 0 };
        var windows = new[] { W(10, "chrome.exe") };

        Assert.Empty(SlotBinder.Plan(rules, bound, windows));
    }

    [Fact]
    public void Plan_NoWindow_LeavesSlotWaiting() =>
        Assert.Empty(SlotBinder.Plan([new MatchRule("mstsc")], [0], [W(1, "notepad.exe")]));
}

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+Alt+D", HotkeyParser.Ctrl | HotkeyParser.Alt, Keys.D)]
    [InlineData("ctrl + shift + 3", HotkeyParser.Ctrl | HotkeyParser.Shift, Keys.D3)]
    [InlineData("Win+Alt+F5", HotkeyParser.Win | HotkeyParser.Alt, Keys.F5)]
    [InlineData("F9", 0u, Keys.F9)]
    public void Parse_Valid(string text, uint mods, Keys key) =>
        Assert.Equal(new HotkeyDef(mods, key), HotkeyParser.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("D")]            // no modifier
    [InlineData("Ctrl+Alt")]     // no key
    [InlineData("Hyper+D")]      // unknown modifier
    [InlineData("Ctrl+Nope")]    // unknown key
    public void Parse_Invalid(string text) => Assert.Null(HotkeyParser.Parse(text));

    [Fact]
    public void Format_RoundTrips()
    {
        var def = new HotkeyDef(HotkeyParser.Ctrl | HotkeyParser.Alt, Keys.D7);
        Assert.Equal("Ctrl+Alt+7", HotkeyParser.Format(def));
        Assert.Equal(def, HotkeyParser.Parse(HotkeyParser.Format(def)));
    }

    [Fact]
    public void ParseModifiers_RequiresAtLeastOne()
    {
        Assert.Equal(HotkeyParser.Ctrl | HotkeyParser.Alt, HotkeyParser.ParseModifiers("Ctrl+Alt"));
        Assert.Null(HotkeyParser.ParseModifiers(""));
        Assert.Null(HotkeyParser.ParseModifiers("Ctrl+X"));
    }

    [Fact]
    public void FromKeyData_IgnoresLoneModifier() =>
        Assert.Null(HotkeyParser.FromKeyData(Keys.Control | Keys.ControlKey));

    [Fact]
    public void FromKeyData_ReadsCombination() =>
        Assert.Equal(new HotkeyDef(HotkeyParser.Ctrl | HotkeyParser.Shift, Keys.K), HotkeyParser.FromKeyData(Keys.Control | Keys.Shift | Keys.K));
}

public class MonitorResolveTests
{
    private const string D1 = @"\\.\DISPLAY1", D2 = @"\\.\DISPLAY2";
    private static readonly MonitorDescriptor M1 = new(D1, new Rectangle(0, 0, 1920, 1080), true);
    private static readonly MonitorDescriptor M2 = new(D2, new Rectangle(1920, 0, 2560, 1440), false);

    private static LayoutModel Dual(string? thumb = D1, string? focus = D2, ZoneType type = ZoneType.Fill) => new()
    {
        Mode = LayoutMode.Dual, ThumbMonitor = thumb, FocusMonitor = focus,
        ThumbZone = new ThumbZone { Type = type, Edge = DockEdge.Right },
    };

    [Fact]
    public void Dual_BothPresent_IsDual()
    {
        var z = MonitorService.Resolve(Dual(), [M1, M2]);
        Assert.True(z.IsDual);
        Assert.Equal(D1, z.ThumbDevice);
        Assert.Equal(D2, z.FocusDevice);
        Assert.Equal(ZoneType.Fill, z.Type);
        Assert.Null(z.FallbackReason);
    }

    [Fact]
    public void Dual_SecondMonitorUnplugged_FallsBackToSingleDockOnPrimary()
    {
        var z = MonitorService.Resolve(Dual(thumb: D2, focus: D1), [M1]);
        Assert.False(z.IsDual);
        Assert.Equal(D1, z.ThumbDevice);
        Assert.Equal(D1, z.FocusDevice);
        Assert.Equal(ZoneType.Dock, z.Type);   // fill would cover the only monitor
        Assert.Equal(DockEdge.Right, z.Edge);
        Assert.NotNull(z.FallbackReason);
    }

    [Fact]
    public void Dual_MissingDevice_FallsBack() =>
        Assert.NotNull(MonitorService.Resolve(Dual(focus: @"\\.\DISPLAY9"), [M1, M2]).FallbackReason);

    [Fact]
    public void Dual_SameMonitorTwice_FallsBack() =>
        Assert.NotNull(MonitorService.Resolve(Dual(thumb: D1, focus: D1), [M1, M2]).FallbackReason);

    [Fact]
    public void Single_DefaultsToPrimary()
    {
        var z = MonitorService.Resolve(new LayoutModel(), [M2, M1]);
        Assert.False(z.IsDual);
        Assert.Equal(D1, z.ThumbDevice);
        Assert.Null(z.FallbackReason);
    }

    [Fact]
    public void Single_ChosenMonitor_IsUsed() =>
        Assert.Equal(D2, MonitorService.Resolve(new LayoutModel { ThumbMonitor = D2 }, [M1, M2]).ThumbDevice);
}
