namespace AiFortress;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        using var mutex = new Mutex(true, "ClaudeGuard-SingleInstance", out var created);
        bool cleanup = args is ["--remove-protection"];
        bool background = args is ["--background"];
        using var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, "AiFortress-Show");
        if (!created)
        {
            if (!cleanup && !background)
                showRequest.Set();
            return cleanup ? 2 : 0;
        }

        var profiles = ServiceCatalog.Supported;
        var enforcer = new Enforcer(profiles);
        if (cleanup)
            return enforcer.ReconcileAsync(new AppConfig(), false, (_, _) => { })
                .GetAwaiter().GetResult() ? 0 : 1;

        ApplicationConfiguration.Initialize();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        using var tray = new TrayApp(new ConfigStore(ConfigStore.DefaultPath, ConfigStore.LegacyPath),
            new Checker(http, WindowsTimeZone.CurrentId), enforcer.ReconcileAsync, profiles);
        RegisteredWaitHandle? wait = null;
        Application.Idle += Ready;
        try { Application.Run(tray); }
        finally
        {
            Application.Idle -= Ready;
            wait?.Unregister(null);
        }
        return 0;

        void Ready(object? sender, EventArgs e)
        {
            Application.Idle -= Ready;
            var ui = SynchronizationContext.Current!;
            wait = ThreadPool.RegisterWaitForSingleObject(showRequest,
                (_, _) => ui.Post(_ => tray.ShowFromLauncher(), null), null, Timeout.Infinite, false);
            if (!background)
                tray.ShowFromLauncher();
        }
    }
}
