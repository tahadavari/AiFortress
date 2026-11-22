namespace ClaudeGuard;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        using var mutex = new Mutex(true, "ClaudeGuard-SingleInstance", out var created);
        bool cleanup = args is ["--remove-protection"];
        if (!created)
            return cleanup ? 2 : 0;

        var enforcer = new Enforcer();
        if (cleanup)
            return enforcer.ReconcileAsync(new AppConfig(), false, (_, _) => { })
                .GetAwaiter().GetResult() ? 0 : 1;

        ApplicationConfiguration.Initialize();
        using var tray = new TrayApp(new ConfigStore(ConfigStore.DefaultPath), Checker.Create(), enforcer.ReconcileAsync);
        Application.Run(tray);
        return 0;
    }
}
