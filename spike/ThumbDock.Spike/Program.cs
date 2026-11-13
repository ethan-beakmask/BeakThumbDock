namespace ThumbDock.Spike;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length >= 4 && args[0] == "--ticker")
        {
            Application.Run(new TickerForm(args[1], int.Parse(args[2]), int.Parse(args[3])));
            return;
        }
        InstallAppBarSafetyNet();
        if (args.Length > 0 && args[0] == "--selftest")
        {
            Log.FilePath = Path.Combine(AppContext.BaseDirectory, "selftest-log.txt");
            var test = new SelfTest();
            var host = new Form { ShowInTaskbar = false, Opacity = 0, Size = new Size(1, 1) };
            host.Load += async (_, _) => await test.RunAsync();
            Application.Run(host);
            return;
        }
        if (args.Length > 0 && args[0] == "--appbar-hold")
        {
            Log.FilePath = Path.Combine(AppContext.BaseDirectory, "hold-log.txt");
            var dock = new DockBar(args.Contains("tool"), args.Contains("noact"), 300);
            SelfTest.HoldDock = dock;
            var t = new System.Windows.Forms.Timer { Interval = 15000 };
            t.Tick += (_, _) => Application.Exit();
            t.Start();
            Application.Run(dock);
            return;
        }
        Application.Run(new ControlForm());
    }

    // Spec rule: remove the AppBar on exit and on unhandled exceptions.
    private static void InstallAppBarSafetyNet()
    {
        static void Remove()
        {
            SelfTest.CurrentDock?.RemoveAppBar();
            SelfTest.HoldDock?.RemoveAppBar();
            ControlForm.CurrentDock?.RemoveAppBar();
        }
        Application.ApplicationExit += (_, _) => Remove();
        Application.ThreadException += (_, e) => { Log.W($"未處理例外: {e.Exception}"); Remove(); };
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Remove();
    }
}
