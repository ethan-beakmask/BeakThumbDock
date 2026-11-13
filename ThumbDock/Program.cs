using System.Reflection;
using ThumbDock.Core;
using ThumbDock.Layout;
using ThumbDock.UI;

namespace ThumbDock;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Loc.Init(AppContext.BaseDirectory); // before anything shows text
        var options = CliOptions.Parse(args);
        AppLog.Info($"ThumbDock {Assembly.GetExecutingAssembly().GetName().Version} args=[{string.Join(" ", args)}]");
        if (options.ShowHelp || options.Error is not null)
        {
            ShowMessage(options.Error is null ? CliOptions.HelpText : options.Error + "\r\n\r\n" + CliOptions.HelpText, options.Error is null);
            return;
        }

        using var guard = new InstanceGuard();
        var store = new LayoutStore(AppContext.BaseDirectory);
        var (plan, error) = StartupResolver.Resolve(options, store, guard);
        if (plan is null)
        {
            ShowMessage(error ?? Loc.T("無法啟動。"), info: false);
            return;
        }

        AppLog.Tag = plan.Label;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        var app = new ThumbDockApp(plan, store, guard);
        InstallCleanupHandlers(app);
        Application.Run(app);
    }

    private static void ShowMessage(string text, bool info) =>
        MessageBox.Show(text, "ThumbDock", MessageBoxButtons.OK, info ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

    // Spec rule 12: always try to remove the AppBar, including on crashes.
    private static void InstallCleanupHandlers(ThumbDockApp app)
    {
        Application.ApplicationExit += (_, _) => app.EmergencyCleanup();
        Application.ThreadException += (_, e) => AppLog.Error("unhandled UI exception (continuing)", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            AppLog.Error("fatal exception", e.ExceptionObject as Exception);
            app.EmergencyCleanup();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => app.EmergencyCleanup();
    }
}
