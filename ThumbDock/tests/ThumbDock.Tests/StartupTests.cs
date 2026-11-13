using ThumbDock.Core;
using ThumbDock.Layout;

namespace ThumbDock.Tests;

public class CliOptionsTests
{
    [Fact]
    public void No_arguments_is_a_normal_start()
    {
        var o = CliOptions.Parse([]);
        Assert.Null(o.Layout);
        Assert.Null(o.Monitor);
        Assert.False(o.ShowHelp);
        Assert.Null(o.Error);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("/?")]
    public void Help_flags(string flag) => Assert.True(CliOptions.Parse([flag]).ShowHelp);

    [Fact]
    public void Layout_and_monitor_with_separate_values()
    {
        var o = CliOptions.Parse(["--layout", "監控台", "--monitor", "2"]);
        Assert.Equal("監控台", o.Layout);
        Assert.Equal("2", o.Monitor);
    }

    [Fact]
    public void Equals_form_is_accepted()
    {
        var o = CliOptions.Parse(["--layout=左側", "--monitor=1"]);
        Assert.Equal("左側", o.Layout);
        Assert.Equal("1", o.Monitor);
    }

    [Theory]
    [InlineData("--layout")]
    [InlineData("--monitor", "--layout", "x")]
    public void Missing_value_is_an_error(params string[] args) => Assert.NotNull(CliOptions.Parse(args).Error);

    [Fact]
    public void Unknown_argument_is_an_error() => Assert.Contains("--bogus", CliOptions.Parse(["--bogus"]).Error);

    [Fact]
    public void Help_text_ends_with_a_blank_line() => Assert.EndsWith("\r\n\r\n", CliOptions.HelpText);
}

public class MonitorKeyTests
{
    private static readonly List<MonitorDescriptor> Monitors =
    [
        new(@"\\.\DISPLAY1", new Rectangle(0, 0, 1920, 1080), true),
        new(@"\\.\DISPLAY3", new Rectangle(1920, 0, 1920, 1080), false),
    ];

    [Fact]
    public void DeviceKey_strips_the_prefix() => Assert.Equal("DISPLAY2", MonitorService.DeviceKey(@"\\.\DISPLAY2"));

    [Theory]
    [InlineData("1", @"\\.\DISPLAY1")]
    [InlineData("3", @"\\.\DISPLAY3")]
    [InlineData("display3", @"\\.\DISPLAY3")]
    [InlineData(@"\\.\DISPLAY1", @"\\.\DISPLAY1")]
    public void ParseDevice_finds_connected_monitors(string text, string expected) =>
        Assert.Equal(expected, MonitorService.ParseDevice(text, Monitors));

    [Theory]
    [InlineData("2")]
    [InlineData("abc")]
    public void ParseDevice_returns_null_for_unknown(string text) => Assert.Null(MonitorService.ParseDevice(text, Monitors));
}

public class SettingsKeyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thumbdock-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Each_settings_key_has_its_own_file()
    {
        var store = new LayoutStore(_dir);
        store.SaveSettings(new AppSettings { LastLayout = "main" });
        store.SettingsKey = "DISPLAY2";
        Assert.Null(store.LoadSettings().LastLayout);
        store.SaveSettings(new AppSettings { LastLayout = "second" });

        Assert.True(File.Exists(Path.Combine(_dir, "config", "settings.DISPLAY2.json")));
        store.SettingsKey = null;
        Assert.Equal("main", store.LoadSettings().LastLayout);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}

public class InstanceGuardTests
{
    [Fact]
    public void Layout_key_ignores_case_and_invalid_characters() =>
        Assert.Equal(InstanceGuard.LayoutKey("My:Layout"), InstanceGuard.LayoutKey("my:layout"));

    [Fact]
    public void Same_process_can_reclaim_its_own_monitor_and_layout()
    {
        using var guard = new InstanceGuard();
        string device = @"\\.\DISPLAY" + Environment.ProcessId; // unique, cannot clash with a real instance
        Assert.True(guard.TryClaimMonitors([device], out _));
        Assert.True(guard.TryClaimMonitors([device], out _));
        Assert.True(guard.TryClaimLayout("test-layout-" + Environment.ProcessId));
        Assert.True(guard.IsLayoutAvailable("test-layout-" + Environment.ProcessId));
    }
}
