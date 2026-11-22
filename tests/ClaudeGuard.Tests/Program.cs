using System.Net;
using ClaudeGuard;

namespace ClaudeGuard.Tests;

internal static class Program
{
    private const string Zone = "Pacific Standard Time";

    [STAThread]
    private static int Main()
    {
        var directory = Directory.CreateTempSubdirectory("ClaudeGuard.Tests-");
        try
        {
            TestConfig(directory.FullName);
            TestCheckerAsync().GetAwaiter().GetResult();
            TestWindowsHelpers();
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            TestCheckNowAndSettings(directory.FullName);
            Console.WriteLine("All ClaudeGuard regression checks passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static void TestConfig(string directory)
    {
        var path = Path.Combine(directory, "legacy.json");
        var store = new ConfigStore(path);
        File.WriteAllText(path, """{"CountryCode":"ca","CheckIntervalSeconds":-2,"AllowedTimezones":[" Pacific Standard Time "]}""");
        var config = store.Load(out var error);
        Require(error == null && config.CountryCode == "CA" && config.Language == "en", "Legacy settings migrate");
        Require(config.AllowedTimezones.SequenceEqual([Zone]) && config.CheckIntervalSeconds == 5, "Legacy custom zones are preserved");
        File.WriteAllText(path, """{"CountryCode":null,"CheckIntervalSeconds":2147483647,"AllowedTimezones":null}""");
        config = store.Load(out error);
        Require(error == null && config.CountryCode == "US" && config.CheckIntervalSeconds == 3600 &&
            config.AllowedTimezones.Count > 0, "Invalid legacy values are normalized");
        config.Language = "fa";
        store.Save(config);
        Require(store.Load(out _).Language == "fa", "Language setting persists");
        File.WriteAllText(path, "{");
        Require(store.Load(out error).AllowedTimezones.Count > 0 && error != null, "Malformed JSON has recoverable defaults");
    }

    private static async Task TestCheckerAsync()
    {
        var config = new AppConfig { AllowedTimezones = [Zone] };
        using var http = new HttpClient(new FakeHttp(request => request.RequestUri!.Host == "api.country.is"
            ? Reply("""{"ip":17,"country":"?"}""") : Reply("""{"ip":"192.0.2.14","country":"us"}""")));
        var checker = new Checker(http, () => Zone);
        var result = await checker.RunAsync(config, CancellationToken.None);
        Require(result.Ip == "192.0.2.14" && result.IpState == CheckState.Ok && result.TimeZoneState == CheckState.Ok,
            "Check uses the fallback and validates the response");
        config.AllowedTimezones = ["Eastern Standard Time"];
        result = await checker.RunAsync(config, CancellationToken.None);
        Require(result.ShouldBlock(config), "Time zone mismatch activates protection");
        config.ProtectionEnabled = false;
        Require(!result.ShouldBlock(config), "Disabled protection has no blocking");

        using var offline = new HttpClient(new FakeHttp(_ => throw new HttpRequestException()));
        result = await new Checker(offline, () => Zone).RunAsync(new AppConfig { AllowedTimezones = [Zone] }, CancellationToken.None);
        config = new AppConfig { AllowedTimezones = [Zone] };
        Require(result.IpState == CheckState.Unknown && !result.ShouldBlock(config), "Offline check stays unknown");
        config.FailClosed = true;
        Require(result.ShouldBlock(config), "Fail closed is respected");
    }

    private static void TestWindowsHelpers()
    {
        Require(WindowsTimeZone.Available(["not-a-time-zone", Zone]).Single().Id == Zone, "Only installed Windows IDs are offered");
        Require(WindowsTimeZone.CurrentId() == WindowsCommand.RunAsync("tzutil.exe", ["/g"])
            .GetAwaiter().GetResult().Message, "Windows time zone reader matches tzutil");
        Require(Enforcer.IsClaudeProcess("claude.exe", "") &&
            Enforcer.IsClaudeProcess("node.exe", @"node C:\packages\@anthropic-ai\claude-code\cli.js") &&
            !Enforcer.IsClaudeProcess("pwsh.exe", @"cd C:\packages\@anthropic-ai\claude-code") &&
            !Enforcer.IsClaudeProcess("ClaudeGuard.exe", ""), "Process matching is scoped to Claude");
        var lines = new[] { "# custom", "# >>> ClaudeGuard", "0.0.0.0 claude.ai", "# <<< ClaudeGuard", "127.0.0.1 local.test" };
        Require(Enforcer.WithoutOwnedHostsEntries(lines).SequenceEqual([lines[0], lines[^1]]), "Cleanup preserves other hosts entries");
        Require(Enforcer.WithoutOwnedHostsEntries(["# >>> ClaudeGuard", "127.0.0.1 local.test"]).Count == 2,
            "An unmatched hosts marker does not erase trailing lines");
    }

    private static void TestCheckNowAndSettings(string directory)
    {
        var path = Path.Combine(directory, "tray.json");
        var store = new ConfigStore(path);
        store.Save(new AppConfig { AllowedTimezones = [Zone], CheckIntervalSeconds = 3600 });
        using var http = new HttpClient(new FakeHttp(_ => Reply("""{"ip":"192.0.2.14","country":"US"}""")));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checks = 0;
        using var tray = new TrayApp(store, new Checker(http, () => Zone), async (_, _, _) =>
        {
            var check = Interlocked.Increment(ref checks);
            if (check == 1)
            {
                started.SetResult();
                await finish.Task;
                throw new IOException("Simulated Windows failure");
            }
            return true;
        });

        Exception? failure = null;
        using var start = new System.Windows.Forms.Timer { Interval = 10 };
        start.Tick += async (_, _) =>
        {
            start.Stop();
            try
            {
                var form = tray.Open();
                var check = Find<Button>(form, "CheckNow");
                Require(Find<TextBox>(form, "AllowedTimeZones").Text == Zone &&
                    store.Load(out _).AllowedTimezones.SequenceEqual([Zone]), "Opening the form preserves custom time zones");
                check.PerformClick();
                var active = tray.CheckNowAsync();
                await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Require(!check.Enabled && !active.IsCompleted && ReferenceEquals(active, tray.CheckNowAsync()),
                    "Check now stays responsive and concurrent requests share one check");
                finish.SetResult();
                await active.WaitAsync(TimeSpan.FromSeconds(10));
                Require(check.Enabled && checks == 1 && HasActivity(form, "Simulated Windows failure"),
                    "A Windows operation failure appears in the UI and does not crash it");

                check.PerformClick();
                await tray.CheckNowAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Require(check.Enabled && checks == 2 && HasActivity(form, Zone), "Check now recovers on a subsequent click");
                Capture(form, "en");

                var language = Find<ComboBox>(form, "Language");
                language.SelectedIndex = Localization.Available.ToList().FindIndex(catalog => catalog.Code == "fa");
                Require(form.RightToLeft == RightToLeft.Yes && check.Text == Localization.ForLanguage("fa")["CheckNow"],
                    "Switching to Persian updates layout and labels");
                Require(store.Load(out _).Language == "fa", "UI language is saved");
                Capture(form, "fa");

                File.Delete(path);
                Directory.CreateDirectory(path);
                language.SelectedIndex = 0;
                Require(form.RightToLeft == RightToLeft.Yes && HasActivity(form, Localization.ForLanguage("fa")["SettingsSaveError"].Split('{')[0]),
                    "A settings write failure is reported and the current selection is kept");

                string? chosen = null;
                using var preview = new MainForm(new AppConfig { AllowedTimezones = [Zone] }, () => Task.CompletedTask,
                    _ => true, id => { chosen = id; return Task.FromResult(true); });
                preview.Show();
                Find<Button>(preview, "SetTimeZone").PerformClick();
                Require(chosen == Zone, "The set time zone button passes the selected Windows ID to its handler");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                finish.TrySetResult();
                tray.ExitThread();
            }
        };
        start.Start();
        Application.Run(tray);
        if (failure is not null)
            throw failure;
    }

    private static T Find<T>(Form form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();

    private static bool HasActivity(Form form, string value) => Descendants(form).OfType<ListBox>().Single().Items
        .Cast<string>().Any(item => item.Contains(value, StringComparison.Ordinal));

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(control => new[] { control }.Concat(Descendants(control)));

    private static void Capture(Form form, string language)
    {
        var directory = Environment.GetEnvironmentVariable("CLAUDEGUARD_PREVIEW_DIRECTORY");
        if (string.IsNullOrEmpty(directory))
            return;
        Directory.CreateDirectory(directory);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        bitmap.Save(Path.Combine(directory, $"claudeguard-{language}.png"));
    }

    private static HttpResponseMessage Reply(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
