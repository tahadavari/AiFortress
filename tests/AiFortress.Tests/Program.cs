using System.Net;
using AiFortress;

namespace AiFortress.Tests;

internal static class Program
{
    private const string Zone = "Pacific Standard Time";

    [STAThread]
    private static int Main()
    {
        var directory = Directory.CreateTempSubdirectory("AiFortress.Tests-");
        try
        {
            TestConfig(directory.FullName);
            TestCheckerAsync().GetAwaiter().GetResult();
            TestWindowsHelpers();
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            TestCheckNowAndSettings(directory.FullName);
            Console.WriteLine("All AiFortress regression checks passed.");
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
        var upgraded = new ConfigStore(Path.Combine(directory, "AiFortress.json"), path);
        Require(upgraded.Load(out _).CountryCode == "CA", "The renamed app prefers legacy settings to defaults on first run");
        upgraded.Save(new AppConfig { CountryCode = "FR" });
        Require(upgraded.Load(out _).CountryCode == "FR" && store.Load(out _).CountryCode == "CA",
            "Saving AiFortress settings keeps the original file intact");
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
        var enforcer = new Enforcer(ServiceCatalog.Supported);
        Require(enforcer.IsTargetProcess("claude.exe", "") &&
            enforcer.IsTargetProcess("node.exe", @"node C:\packages\@anthropic-ai\claude-code\cli.js") &&
            !enforcer.IsTargetProcess("pwsh.exe", @"cd C:\packages\@anthropic-ai\claude-code") &&
            !enforcer.IsTargetProcess("AiFortress.exe", ""), "Process matching is scoped to Claude");
        var website = new ServiceProfile("Site fixture", ["example.invalid"], ["example.invalid"]);
        var desktop = new ServiceProfile("Desktop fixture", [], [], (name, _) => name == "ExampleClient.exe");
        Require(new Enforcer([website, desktop]).IsTargetProcess("ExampleClient.exe", "") &&
            !new Enforcer([website]).IsTargetProcess("ExampleClient.exe", ""),
            "Adding a desktop or site-only profile does not require engine changes");
        var lines = new[] { "# custom", "# >>> ClaudeGuard", "0.0.0.0 claude.ai", "# <<< ClaudeGuard", "127.0.0.1 local.test" };
        Require(Enforcer.WithoutOwnedHostsEntries(lines).SequenceEqual([lines[0], lines[^1]]), "Cleanup preserves other hosts entries");
        Require(Enforcer.WithoutOwnedHostsEntries(["# >>> ClaudeGuard", "127.0.0.1 local.test"]).Count == 2,
            "An unmatched hosts marker does not erase trailing lines");
        var display = new Rectangle(-1920, 0, 1920, 1080);
        var work = Rectangle.FromLTRB(-1920, 0, 0, 1040);
        var desired = new Size(408, 620);
        Require(FlyoutPlacement.Calculate(display, work, desired, 12) == new Rectangle(-420, 408, 408, 620),
            "Flyout anchors above the taskbar even on a monitor with negative coordinates");
        var leftTaskbar = Rectangle.FromLTRB(-1872, 0, 0, 1080);
        Require(FlyoutPlacement.Calculate(display, leftTaskbar, desired, 12).Left == -1860,
            "Flyout opens beside left-docked taskbars");
    }

    private static void TestCheckNowAndSettings(string directory)
    {
        var path = Path.Combine(directory, "tray.json");
        var store = new ConfigStore(path);
        const string secondZone = "Eastern Standard Time";
        store.Save(new AppConfig { AllowedTimezones = [Zone, secondZone], CheckIntervalSeconds = 3600 });
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
        }, ServiceCatalog.Supported);

        Exception? failure = null;
        using var start = new System.Windows.Forms.Timer { Interval = 10 };
        start.Tick += async (_, _) =>
        {
            start.Stop();
            try
            {
                var screen = Screen.PrimaryScreen ?? Screen.FromPoint(Cursor.Position);
                var form = tray.Open(screen);
                Require(!form.ShowInTaskbar && form.FormBorderStyle == FormBorderStyle.None && screen.WorkingArea.Contains(form.Bounds),
                    "The real flyout opens inside the monitor work area");
                var check = Find<Button>(form, "CheckNow");
                var editor = Find<TextBox>(form, "AllowedTimeZones");
                Require(editor.Lines.SequenceEqual([Zone, secondZone]) &&
                    store.Load(out _).AllowedTimezones.SequenceEqual([Zone, secondZone]), "Opening the form preserves custom time zones");
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
                Capture(form, "en-overview");

                Find<Button>(form, "LocationTab").PerformClick();
                var zone = Find<FlyoutPicker>(form, "TimeZone");
                Require(zone.Visible && !check.Visible, "Location tab shows the time zone controls");
                Capture(form, "en-location");
                Choose(zone, secondZone);
                editor.AppendText(Environment.NewLine + "UTC");
                Find<Button>(form, "Settings").PerformClick();
                var language = Find<FlyoutPicker>(form, "Language");
                Require(language.Visible, "Settings tab shows the language selector");
                Capture(form, "en-settings");
                foreach (var code in new[] { "fa", "en", "fa", "en", "fa" })
                {
                    Choose(language, code, capture: true);
                    await Task.Yield();
                    var catalog = Localization.ForLanguage(code);
                    Require(form.Visible && language.Visible && !language.DroppedDown &&
                        form.RightToLeftLayout == catalog.RightToLeft &&
                        form.RightToLeft == (catalog.RightToLeft ? RightToLeft.Yes : RightToLeft.No) &&
                        check.Text == catalog["CheckNow"] && store.Load(out _).Language == code,
                        $"Switching to {code} retains the visible Settings tab, applies direction and saves language");
                }
                var translatedLocation = Find<Button>(form, "LocationTab");
                Require(translatedLocation.Visible && translatedLocation.Text == "موقعیت" && translatedLocation.Width > 60,
                    "The middle navigation tab remains visible after mirroring");
                Capture(form, "fa-settings");
                translatedLocation.PerformClick();
                Require(zone.Selection?.Id == secondZone && editor.Lines.SequenceEqual([Zone, secondZone, "UTC"]) &&
                    store.Load(out _).AllowedTimezones.SequenceEqual([Zone, secondZone]),
                    "Switching languages preserves the chosen time zone and unsaved text without writing the draft");
                Require(Find<FlyoutPicker>(form, "Country").Text == Localization.ForLanguage("fa")["CountryUS"],
                    "Country labels follow the active language while their stored codes stay stable");
                editor.Lines = [Zone, secondZone];
                Capture(form, "fa-location");
                Find<Button>(form, "Overview").PerformClick();
                Capture(form, "fa-overview");
                var protection = Find<Button>(form, "ProtectionToggle");
                protection.PerformClick();
                await tray.CheckNowAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Require(!store.Load(out _).ProtectionEnabled, "The fortress control pauses protection");
                protection.PerformClick();
                await tray.CheckNowAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Require(store.Load(out _).ProtectionEnabled, "The fortress control resumes protection");

                Find<Button>(form, "Settings").PerformClick();
                File.Delete(path);
                Directory.CreateDirectory(path);
                Choose(language, "en");
                Require(form.RightToLeft == RightToLeft.Yes && language.Selection?.Id == "fa" &&
                    HasActivity(form, Localization.ForLanguage("fa")["SettingsSaveError"].Split('{')[0]),
                    "A settings write failure is reported and the current selection is kept");

                string? chosen = null;
                using var preview = new MainForm(new AppConfig { AllowedTimezones = [Zone] }, () => Task.CompletedTask,
                    _ => true, id => { chosen = id; return Task.FromResult(true); }, ["Claude"]);
                preview.ShowFlyout(screen);
                Find<Button>(preview, "LocationTab").PerformClick();
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

    private static void Choose(FlyoutPicker picker, string id, bool capture = false)
    {
        picker.PerformClick();
        Require(picker.DroppedDown, $"{picker.Name} opens its choices");
        if (capture)
            Capture(picker.Menu, $"picker-{picker.Selection?.Id}");
        var item = picker.Menu.Items.Find(id, false).Single();
        item.PerformClick();
        Require(!picker.DroppedDown, $"{picker.Name} closes after choosing an item");
    }

    private static bool HasActivity(Form form, string value) => Descendants(form).OfType<ListBox>().Single().Items
        .Cast<string>().Any(item => item.Contains(value, StringComparison.Ordinal));

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(control => new[] { control }.Concat(Descendants(control)));

    private static void Capture(Control control, string language)
    {
        var directory = Environment.GetEnvironmentVariable("AIFORTRESS_PREVIEW_DIRECTORY");
        if (string.IsNullOrEmpty(directory))
            return;
        Directory.CreateDirectory(directory);
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        bitmap.Save(Path.Combine(directory, $"aifortress-{language}.png"));
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
