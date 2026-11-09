using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Distribution;
using Raffica.Core.Settings;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Packaging;
using Raffica.Pdf.Tests.Text;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Packaging;

public sealed class DefaultAppFlowTests : IDisposable
{
    public void Dispose() => PdfStrings.Culture = CultureInfo.GetCultureInfo("en");

    // A flow over a fake service in a headless dialog host: the settings live in a variable, the writes are logged.
    private sealed class Rig : IDisposable
    {
        private readonly Window _window;

        public Rig(FakeDefaultAppService service, DefaultAppPromptSetting prompt = DefaultAppPromptSetting.Ask)
        {
            PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
            Service = service;
            Settings = AppSettings.Default with { DefaultAppPrompt = prompt };
            Host = new DialogHost { Content = new Border() };
            _window = new Window { Content = Host, Width = 700, Height = 400 };
            _window.Show();
            Flow = new DefaultAppFlow(service, () => Settings, p => Settings = Settings with { DefaultAppPrompt = p }, new DialogQueue(Host), Time,
                until => Settings = Settings with { DefaultAppPromptSnoozedUntil = until });
        }

        public FakeDefaultAppService Service { get; }

        public AppSettings Settings { get; private set; }

        public DialogHost Host { get; }

        public DefaultAppFlow Flow { get; }

        public ManualTime Time { get; } = new();

        public string AllText => Host.ActiveDialog!.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Message").Text!;

        public IReadOnlyList<string> ButtonNames =>
            Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Select(b => b.Name!).Where(n => n.StartsWith("PART_", StringComparison.Ordinal) && n != "PART_Close").ToList();

        public async Task WaitForCardAsync() => await ViewFixture.WaitAsync(() => Host.IsOpen);

        public void Click(string name)
        {
            Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
        }

        public void Dispose() => _window.Close();
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task AsksOnlyWhenNotDefaultAndSettingIsAsk()
    {
        // Already the default: no card.
        using (var isDefault = new Rig(new FakeDefaultAppService { Status = DefaultAppStatus.IsDefault }))
        {
            await isDefault.Flow.OfferAtStartAsync(TestContext.Current.CancellationToken);
            Assert.False(isDefault.Host.IsOpen);
            Assert.Equal(1, isDefault.Service.StatusCalls);
        }

        // "Don't ask again" was chosen once: not even the system is asked.
        using (var never = new Rig(new FakeDefaultAppService(), DefaultAppPromptSetting.Never))
        {
            await never.Flow.OfferAtStartAsync(TestContext.Current.CancellationToken);
            Assert.False(never.Host.IsOpen);
            Assert.Equal(0, never.Service.StatusCalls);
        }

        // Another app is the default and the setting asks: the card, with its three buttons; "Not now" changes nothing.
        using var asks = new Rig(new FakeDefaultAppService());
        var offer = asks.Flow.OfferAtStartAsync(TestContext.Current.CancellationToken);
        await asks.WaitForCardAsync();
        Assert.Equal(["PART_Set", "PART_DontAsk", "PART_NotNow"], asks.ButtonNames);
        Assert.Equal(PdfStrings.DefaultAppTitle, asks.Host.ActiveDialog!.Title);
        asks.Click("PART_NotNow");
        await offer;
        Assert.False(asks.Host.IsOpen);
        Assert.Equal(DefaultAppPromptSetting.Ask, asks.Settings.DefaultAppPrompt);
        Assert.Equal(0, asks.Service.MakeCalls);
    }

    // 08-UI-16 (scope change of M2.7): "Not now" is remembered for 30 days, it no longer returns at every start.
    [AvaloniaFact]
    public async Task NotNowSnoozesTheCardFor30DaysAndOnlyThenItReturns()
    {
        using var rig = new Rig(new FakeDefaultAppService());
        rig.Time.UtcNow = new DateTimeOffset(2026, 11, 9, 20, 0, 0, TimeSpan.Zero);
        var offer = rig.Flow.OfferAtStartAsync(TestContext.Current.CancellationToken);
        await rig.WaitForCardAsync();
        rig.Click("PART_NotNow");
        await offer;
        Assert.Equal(new DateOnly(2026, 12, 9), rig.Settings.DefaultAppPromptSnoozedUntil);
        Assert.Equal(DefaultAppPromptSetting.Ask, rig.Settings.DefaultAppPrompt);
        Assert.Equal(1, rig.Service.StatusCalls);

        // A later start inside the 30 days, and on the last day itself: no card, the system is not even asked.
        foreach (var day in new[] { new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 9, 23, 59, 0, TimeSpan.Zero) })
        {
            rig.Time.UtcNow = day;
            var snoozed = new DefaultAppFlow(rig.Service, () => rig.Settings, _ => { }, new DialogQueue(rig.Host), rig.Time);
            await snoozed.OfferAtStartAsync(TestContext.Current.CancellationToken);
            Assert.False(rig.Host.IsOpen);
            Assert.Equal(1, rig.Service.StatusCalls);
        }

        // The first start after that day asks again.
        rig.Time.UtcNow = new DateTimeOffset(2026, 12, 10, 0, 1, 0, TimeSpan.Zero);
        var later = new DefaultAppFlow(rig.Service, () => rig.Settings, _ => { }, new DialogQueue(rig.Host), rig.Time);
        var again = later.OfferAtStartAsync(TestContext.Current.CancellationToken);
        await rig.WaitForCardAsync();
        Assert.Equal(2, rig.Service.StatusCalls);
        rig.Click("PART_NotNow");
        await again;
    }

    [AvaloniaFact]
    public async Task DontAskAgainStaysPermanentWhateverTheDate()
    {
        using var rig = new Rig(new FakeDefaultAppService());
        rig.Time.UtcNow = new DateTimeOffset(2026, 11, 9, 20, 0, 0, TimeSpan.Zero);
        var offer = rig.Flow.OfferAtStartAsync(TestContext.Current.CancellationToken);
        await rig.WaitForCardAsync();
        rig.Time.Advance(ArmingLock.DelayMilliseconds);
        rig.Click("PART_DontAsk");
        await offer;
        Assert.Equal(DefaultAppPromptSetting.Never, rig.Settings.DefaultAppPrompt);
        Assert.Null(rig.Settings.DefaultAppPromptSnoozedUntil);

        rig.Time.UtcNow = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var next = new DefaultAppFlow(rig.Service, () => rig.Settings, _ => { }, new DialogQueue(rig.Host), rig.Time);
        await next.OfferAtStartAsync(TestContext.Current.CancellationToken);
        Assert.False(rig.Host.IsOpen);
        Assert.Equal(1, rig.Service.StatusCalls);
    }

    [AvaloniaFact]
    public async Task NeverAsksWhenTheStatusIsUnknown()
    {
        using var rig = new Rig(new FakeDefaultAppService { Status = DefaultAppStatus.Unknown });
        await rig.Flow.OfferAtStartAsync(TestContext.Current.CancellationToken);
        Assert.False(rig.Host.IsOpen);
        Assert.Equal(1, rig.Service.StatusCalls);
        Assert.Equal(DefaultAppPromptSetting.Ask, rig.Settings.DefaultAppPrompt);

        // A system that has no notion of it (the stub of macOS) is not even asked.
        using var unsupported = new Rig(new FakeDefaultAppService { IsSupported = false });
        await unsupported.Flow.OfferAtStartAsync(TestContext.Current.CancellationToken);
        Assert.False(unsupported.Host.IsOpen);
        Assert.Equal(0, unsupported.Service.StatusCalls);
    }

    [AvaloniaFact]
    public async Task DontAskAgainIsStoredAndNeverShownAgain()
    {
        using var rig = new Rig(new FakeDefaultAppService());
        var offer = rig.Flow.OfferAtStartAsync(TestContext.Current.CancellationToken);
        await rig.WaitForCardAsync();

        // The choice is locked for the first 400 ms (a key held down from before cannot confirm it).
        rig.Click("PART_DontAsk");
        Assert.True(rig.Host.IsOpen);
        Assert.Equal(DefaultAppPromptSetting.Ask, rig.Settings.DefaultAppPrompt);

        rig.Time.Advance(ArmingLock.DelayMilliseconds);
        rig.Click("PART_DontAsk");
        await offer;
        Assert.False(rig.Host.IsOpen);
        Assert.Equal(DefaultAppPromptSetting.Never, rig.Settings.DefaultAppPrompt);
        Assert.Equal(0, rig.Service.MakeCalls);

        // Not in this run ...
        await rig.Flow.OfferAtStartAsync(TestContext.Current.CancellationToken);
        Assert.False(rig.Host.IsOpen);
        Assert.Equal(1, rig.Service.StatusCalls);

        // ... and not in the next one, which reads the stored setting.
        var next = new DefaultAppFlow(rig.Service, () => rig.Settings, _ => { }, new DialogQueue(rig.Host), rig.Time);
        await next.OfferAtStartAsync(TestContext.Current.CancellationToken);
        Assert.False(rig.Host.IsOpen);
        Assert.Equal(1, rig.Service.StatusCalls);
    }

    [AvaloniaFact]
    public async Task NotAvailableAndFailedEndInAWordedNoteWithoutLooping()
    {
        // The command: no question, one note per result, the setting stays as it was.
        using var rig = new Rig(new FakeDefaultAppService { Result = DefaultAppResult.Failed });
        var failed = rig.Flow.MakeDefaultFromCommandAsync(TestContext.Current.CancellationToken);
        await rig.WaitForCardAsync();
        Assert.Equal(PdfStrings.DefaultAppFailed, rig.AllText);
        Assert.Equal(["PART_Ok"], rig.ButtonNames);
        rig.Click("PART_Ok");
        await failed;
        Assert.False(rig.Host.IsOpen);

        rig.Service.Result = DefaultAppResult.NotAvailable;
        var notAvailable = rig.Flow.MakeDefaultFromCommandAsync(TestContext.Current.CancellationToken);
        await rig.WaitForCardAsync();
        Assert.Equal(PdfStrings.DefaultAppNotAvailable, rig.AllText);
        rig.Click("PART_Ok");
        await notAvailable;
        Assert.False(rig.Host.IsOpen);
        Assert.Equal(2, rig.Service.MakeCalls);
        Assert.Equal(DefaultAppPromptSetting.Ask, rig.Settings.DefaultAppPrompt);

        // A service that throws ends the same way, in the note of a failure.
        rig.Service.MakeThrows = new UnauthorizedAccessException("locked");
        var threw = rig.Flow.MakeDefaultFromCommandAsync(TestContext.Current.CancellationToken);
        await rig.WaitForCardAsync();
        Assert.Equal(PdfStrings.DefaultAppFailed, rig.AllText);
        rig.Click("PART_Ok");
        await threw;
        Assert.False(rig.Flow.IsBusy);
    }

    [AvaloniaFact]
    public async Task AFailedSetAtStartIsOneNoteAndTheCardNeverReturns()
    {
        using var rig = new Rig(new FakeDefaultAppService { Result = DefaultAppResult.Failed });
        var offer = rig.Flow.OfferAtStartAsync(TestContext.Current.CancellationToken);
        await rig.WaitForCardAsync();
        rig.Time.Advance(ArmingLock.DelayMilliseconds);
        rig.Click("PART_Set");
        await rig.WaitForCardAsync();
        Assert.Equal(PdfStrings.DefaultAppFailed, rig.AllText);
        rig.Click("PART_Ok");
        await offer;
        Assert.False(rig.Host.IsOpen);
        Assert.Equal(DefaultAppPromptSetting.Ask, rig.Settings.DefaultAppPrompt);

        // The run does not ask again, whatever the system says now.
        rig.Service.Result = DefaultAppResult.NotAvailable;
        await rig.Flow.OfferAtStartAsync(TestContext.Current.CancellationToken);
        Assert.False(rig.Host.IsOpen);
        Assert.Equal(1, rig.Service.MakeCalls);
        Assert.Equal(1, rig.Service.StatusCalls);
    }

    [AvaloniaFact]
    public async Task OpenedSystemSettingsNamesTheSettingsPage()
    {
        foreach (var language in new[] { "en", "de" })
        {
            using var rig = new Rig(new FakeDefaultAppService { Result = DefaultAppResult.OpenedSystemSettings });
            PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
            var run = rig.Flow.MakeDefaultFromCommandAsync(TestContext.Current.CancellationToken);
            await rig.WaitForCardAsync();
            Assert.Equal(PdfStrings.DefaultAppOpenedSettings, rig.AllText);
            Assert.Contains(language == "en" ? "settings" : "Systemeinstellungen", rig.AllText, StringComparison.Ordinal);
            Assert.Contains(".pdf", rig.AllText, StringComparison.Ordinal);

            // It never claims the change was made: that stays with the user.
            Assert.DoesNotContain(PdfStrings.DefaultAppDone, rig.AllText, StringComparison.Ordinal);
            rig.Click("PART_Ok");
            await run;
        }
    }

    [AvaloniaFact]
    public async Task DoneIsWorded()
    {
        using var rig = new Rig(new FakeDefaultAppService { Result = DefaultAppResult.Done });
        var run = rig.Flow.MakeDefaultFromCommandAsync(TestContext.Current.CancellationToken);
        await rig.WaitForCardAsync();
        Assert.Equal(PdfStrings.DefaultAppDone, rig.AllText);
        rig.Click("PART_Ok");
        await run;
    }

    [AvaloniaFact]
    public async Task ACanceledTokenEndsAHangingServiceWithoutACard()
    {
        using var rig = new Rig(new FakeDefaultAppService { MakeHangs = true });
        using var cancel = new CancellationTokenSource();
        var run = rig.Flow.MakeDefaultFromCommandAsync(cancel.Token);
        await ViewFixture.WaitAsync(() => rig.Service.MakeCalls == 1);
        await cancel.CancelAsync();
        await run;
        Assert.False(rig.Host.IsOpen);
        Assert.False(rig.Flow.IsBusy);
    }

    [AvaloniaFact]
    public void EveryResultHasWordingInBothLanguages()
    {
        foreach (var result in Enum.GetValues<DefaultAppResult>())
        {
            foreach (var language in new[] { "en", "de" })
            {
                PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
                Assert.False(string.IsNullOrWhiteSpace(DefaultAppFlow.NoteFor(result)));
            }
        }

        // The wording never promises more than was checked.
        foreach (var language in new[] { "en", "de" })
        {
            PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
            foreach (var result in Enum.GetValues<DefaultAppResult>())
            {
                var text = DefaultAppFlow.NoteFor(result);
                Assert.DoesNotContain("guarantee", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("garantier", text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
