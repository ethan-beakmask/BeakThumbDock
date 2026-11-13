using ThumbDock.Core;
using ThumbDock.Layout;

namespace ThumbDock.Tests;

public class LaunchConfigTests
{
    private static LaunchEntry E(int slot, string path = "a.exe") => new() { Slot = slot, Path = path };

    [Fact]
    public void Valid_entries_are_kept_with_defaults()
    {
        var c = new LaunchConfig { Launch = [E(1), E(2)] };
        Assert.Empty(c.Normalize());
        Assert.Equal(2, c.Launch.Count);
        Assert.Equal(LaunchConfig.DefaultWaitSec, c.Launch[0].WaitSec);
    }

    [Fact]
    public void Entries_without_path_or_with_bad_slot_are_dropped_with_a_message()
    {
        var c = new LaunchConfig { Launch = [E(1, " "), E(0), E(3)] };
        var warnings = c.Normalize();
        Assert.Equal(2, warnings.Count);
        Assert.Equal([3], c.Launch.Select(e => e.Slot).ToArray());
    }

    [Fact]
    public void Second_entry_for_the_same_slot_is_dropped()
    {
        var c = new LaunchConfig { Launch = [E(1, "first.exe"), E(1, "second.exe")] };
        Assert.Single(c.Normalize());
        Assert.Equal("first.exe", c.Launch.Single().Path);
    }

    [Fact]
    public void Numbers_are_clamped_and_empty_match_is_removed()
    {
        var e = E(1);
        e.DelayMs = -5;
        e.WaitSec = 99999;
        e.Match = new MatchRule();
        var c = new LaunchConfig { Launch = [e] };
        c.Normalize();
        Assert.Equal(0, e.DelayMs);
        Assert.Equal(LaunchConfig.MaxWaitSec, e.WaitSec);
        Assert.Null(e.Match);
    }

    [Fact]
    public void Invalid_regex_gives_a_warning_but_keeps_the_entry()
    {
        var e = E(1);
        e.Match = new MatchRule("chrome.exe", "", "(unclosed");
        var c = new LaunchConfig { Launch = [e] };
        Assert.Single(c.Normalize());
        Assert.Single(c.Launch);
    }
}

public class LaunchWindowPickerTests
{
    private static WindowInfo W(nint h, string process) => new(h, "t", "c", process, 1, null, true, "目前桌面");

    [Fact]
    public void Prefers_window_of_the_started_program()
    {
        var entry = new LaunchEntry { Path = @"C:\Program Files\Google\Chrome\Application\chrome.exe" };
        var picked = LaunchWindowPicker.Pick([W(1, "explorer.exe"), W(2, "chrome.exe")], entry);
        Assert.Equal(2, picked!.Hwnd);
    }

    [Fact]
    public void Falls_back_to_first_new_window_for_launchers_like_cmd()
    {
        var entry = new LaunchEntry { Path = "cmd.exe" };
        Assert.Equal(7, LaunchWindowPicker.Pick([W(7, "WindowsTerminal.exe"), W(8, "x.exe")], entry)!.Hwnd);
    }

    [Fact]
    public void No_candidates_gives_null() => Assert.Null(LaunchWindowPicker.Pick([], new LaunchEntry { Path = "a.exe" }));
}

public class LaunchFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thumbdock-launch-" + Guid.NewGuid().ToString("N"));
    private LayoutStore Store => new(_dir);

    private void Write(string layout, string json)
    {
        var store = Store;
        Directory.CreateDirectory(Path.GetDirectoryName(store.LaunchPath(layout))!);
        File.WriteAllText(store.LaunchPath(layout), json);
    }

    [Fact]
    public void Missing_file_is_not_an_error()
    {
        var (config, warnings, error) = Store.LoadLaunch("none");
        Assert.Null(config);
        Assert.Empty(warnings);
        Assert.Null(error);
    }

    [Fact]
    public void Reads_entries_with_comments_trailing_commas_and_any_key_case()
    {
        Write("監控台", """
            {
              // Chrome profile 3
              "Launch": [
                { "slot": 1, "path": "C:\\Program Files\\x\\chrome.exe", "args": "--profile-directory=\"Profile 3\"",
                  "match": { "process": "chrome.exe", "titleRegex": "Dash" }, },
                { "slot": 2, "path": "cmd.exe", "args": "/k dir" },
              ],
            }
            """);
        var (config, warnings, error) = Store.LoadLaunch("監控台");
        Assert.Null(error);
        Assert.Empty(warnings);
        Assert.Equal(2, config!.Launch.Count);
        Assert.Equal(@"C:\Program Files\x\chrome.exe", config.Launch[0].Path);
        Assert.Equal("--profile-directory=\"Profile 3\"", config.Launch[0].Args);
        Assert.Equal("Dash", config.Launch[0].Match!.TitleRegex);
    }

    [Fact]
    public void Bad_json_reports_the_line_and_the_backslash_hint()
    {
        Write("L", "{\n  \"launch\": [\n    { \"slot\": 1, \"path\": \"C:\\Program Files\\a.exe\" }\n  ]\n}");
        var (config, _, error) = Store.LoadLaunch("L");
        Assert.Null(config);
        Assert.Contains("第 3 行", error);
        Assert.Contains("反斜線", error);
    }

    [Fact]
    public void Template_is_valid_and_launches_nothing()
    {
        string? path = Store.EnsureLaunchTemplate("新佈局");
        Assert.True(File.Exists(path));
        var (config, warnings, error) = Store.LoadLaunch("新佈局");
        Assert.Null(error);
        Assert.Empty(warnings);
        Assert.Empty(config!.Launch);
    }

    [Fact]
    public void Template_is_not_overwritten()
    {
        Write("L", "{ \"launch\": [] }");
        Store.EnsureLaunchTemplate("L");
        Assert.Equal("{ \"launch\": [] }", File.ReadAllText(Store.LaunchPath("L")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}

public class NoLaunchOptionTests
{
    [Fact]
    public void Flag_is_parsed_alone_or_with_other_options()
    {
        Assert.True(CliOptions.Parse(["--no-launch"]).NoLaunch);
        var o = CliOptions.Parse(["--layout", "A", "--no-launch"]);
        Assert.True(o.NoLaunch);
        Assert.Equal("A", o.Layout);
    }

    [Fact]
    public void Default_is_launch_allowed() => Assert.False(CliOptions.Parse([]).NoLaunch);

    [Fact]
    public void Launch_is_off_by_default_in_settings() => Assert.False(new AppSettings().AllowLaunch);

    [Fact]
    public void Launch_on_switch_is_off_by_default_and_survives_a_save()
    {
        Assert.False(new AppSettings().LaunchOnSwitch);
        string dir = Path.Combine(Path.GetTempPath(), "thumbdock-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LayoutStore(dir);
            store.SaveSettings(new AppSettings { AllowLaunch = true, LaunchOnSwitch = true });
            Assert.True(store.LoadSettings().LaunchOnSwitch);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}

public class LaunchArgsTests
{
    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "", "Chrome")]
    [InlineData(@"C:\x\msedge.exe", "", "Edge")]
    [InlineData("cmd.exe", "/k dir", "Cmd")]
    [InlineData("cmd.exe", "", "Cmd")]
    [InlineData("cmd.exe", "/d /k dir", "General")]
    [InlineData("calc.exe", "", "General")]
    [InlineData(@"C:\x\brave.exe", "", "General")]
    public void Kind_is_detected_from_program_and_args(string path, string args, string expected) =>
        Assert.Equal(expected, LaunchArgs.DetectKind(path, args).ToString());

    [Fact]
    public void Browser_args_from_the_docs_example_round_trip()
    {
        string args = "--disable-features=CalculateNativeWinOcclusion --profile-directory=\"Profile 10\" https://www.youtube.com/";
        var parsed = LaunchArgs.ParseBrowser(args);
        Assert.Equal(new BrowserArgs("Profile 10", true, false, "", "https://www.youtube.com/"), parsed);
        Assert.Equal(args, LaunchArgs.BuildBrowser(parsed));
    }

    [Fact]
    public void Unknown_flags_are_kept_and_second_url_is_not_lost()
    {
        var parsed = LaunchArgs.ParseBrowser("--incognito --window-size=800,600 file:///a.html https://b.example");
        Assert.Equal("file:///a.html", parsed.Url);
        Assert.Contains("--incognito", parsed.ExtraFlags);
        Assert.Contains("--window-size=800,600", parsed.ExtraFlags);
        Assert.Contains("https://b.example", parsed.ExtraFlags);
    }

    [Fact]
    public void Other_disable_features_values_stay_untouched()
    {
        var parsed = LaunchArgs.ParseBrowser("--disable-features=Foo,CalculateNativeWinOcclusion");
        Assert.False(parsed.NoOcclusion);
        Assert.Equal("--disable-features=Foo,CalculateNativeWinOcclusion", parsed.ExtraFlags);
    }

    [Fact]
    public void Profile_in_whole_token_quotes_is_recognised()
    {
        Assert.Equal("Profile 3", LaunchArgs.ParseBrowser("\"--profile-directory=Profile 3\"").Profile);
    }

    [Fact]
    public void Url_with_spaces_is_quoted_and_empty_fields_give_empty_args()
    {
        Assert.Equal("\"file:///D:/a b.html\"", LaunchArgs.BuildBrowser(new BrowserArgs(Url: "file:///D:/a b.html")));
        Assert.Equal("", LaunchArgs.BuildBrowser(new BrowserArgs()));
    }

    [Fact]
    public void Cmd_args_round_trip()
    {
        Assert.Equal(new CmdArgs(true, @"cd /d D:\work && python run.py"), LaunchArgs.ParseCmd(@"/k cd /d D:\work && python run.py"));
        Assert.Equal(new CmdArgs(false, "dir"), LaunchArgs.ParseCmd("/C dir"));
        Assert.Null(LaunchArgs.ParseCmd("/kdir"));
        Assert.Equal("/k dir", LaunchArgs.BuildCmd(new CmdArgs(true, "dir")));
        Assert.Equal("/c dir", LaunchArgs.BuildCmd(new CmdArgs(false, "dir")));
        Assert.Equal("", LaunchArgs.BuildCmd(new CmdArgs(true, "")));
    }
}

public class BrowserProfilesTests
{
    [Fact]
    public void Profiles_are_read_from_info_cache_in_folder_order()
    {
        var list = BrowserProfiles.Parse("""
            { "profile": { "info_cache": {
                "Profile 10": { "name": "監控", "user_name": "a@example.com" },
                "Default": { "name": "個人" } } } }
            """);
        Assert.Equal(["Default", "Profile 10"], list.Select(p => p.Folder).ToArray());
        Assert.Equal("Profile 10 — 監控（a@example.com）", list[1].Display);
        Assert.Equal("Default — 個人", list[0].Display);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"profile\":{\"info_cache\":5}}")]
    public void Bad_or_missing_data_gives_an_empty_list(string json) => Assert.Empty(BrowserProfiles.Parse(json));
}

public class LaunchSaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thumbdock-launchsave-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Saved_config_loads_back_identically_with_note_and_match()
    {
        var store = new LayoutStore(_dir);
        var config = new LaunchConfig
        {
            Launch =
            [
                new LaunchEntry { Slot = 1, Path = @"C:\a\chrome.exe", Args = "--profile-directory=\"Profile 3\"", DelayMs = 2500, WaitSec = 20,
                    Match = new MatchRule("chrome.exe", "", "監控"), Note = "監控看板" },
                new LaunchEntry { Slot = 2, Path = "cmd.exe", Args = "/k dir", WorkDir = @"D:\work" },
            ],
        };
        Assert.True(store.SaveLaunch("L", config));
        var (loaded, warnings, error) = store.LoadLaunch("L");
        Assert.Null(error);
        Assert.Empty(warnings);
        Assert.Equal(2, loaded!.Launch.Count);
        Assert.Equal("監控看板", loaded.Launch[0].Note);
        Assert.Equal(new MatchRule("chrome.exe", "", "監控"), loaded.Launch[0].Match);
        Assert.Equal(@"D:\work", loaded.Launch[1].WorkDir);
        Assert.Equal(2500, loaded.Launch[0].DelayMs);
    }

    [Fact]
    public void Empty_optional_fields_are_left_out_of_the_file()
    {
        var store = new LayoutStore(_dir);
        store.SaveLaunch("L", new LaunchConfig { Launch = [new LaunchEntry { Slot = 1, Path = "calc.exe" }] });
        string text = File.ReadAllText(store.LaunchPath("L"));
        Assert.DoesNotContain("args", text);
        Assert.DoesNotContain("workDir", text);
        Assert.DoesNotContain("match", text);
        Assert.DoesNotContain("note", text);
    }

    [Fact]
    public void Previous_file_is_kept_as_bak_before_overwriting()
    {
        var store = new LayoutStore(_dir);
        Directory.CreateDirectory(Path.GetDirectoryName(store.LaunchPath("L"))!);
        File.WriteAllText(store.LaunchPath("L"), "// my comment\n{ \"launch\": [] }");
        store.SaveLaunch("L", new LaunchConfig { Launch = [new LaunchEntry { Slot = 1, Path = "calc.exe" }] });
        Assert.Contains("my comment", File.ReadAllText(store.LaunchPath("L") + ".bak"));
        Assert.DoesNotContain("my comment", File.ReadAllText(store.LaunchPath("L")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
