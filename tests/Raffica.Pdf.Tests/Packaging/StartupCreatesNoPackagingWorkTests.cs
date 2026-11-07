using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Settings;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell;
using Xunit;

namespace Raffica.Pdf.Tests.Packaging;

public sealed class StartupCreatesNoPackagingWorkTests
{
    private static Action<string> Prompt(DefaultAppPromptSetting prompt) => folder =>
        Assert.True(new SettingsStore(Path.Combine(folder, "pdf-settings.json")).TrySave(AppSettings.Default with { DefaultAppPrompt = prompt }));

    private static void RestoreTheme() => ShellTheme.Apply(Avalonia.Application.Current!, ThemeMode.System, AppInk.Pdf, null);

    // The service is the only thing that could start a process or read the registry: if it is never made, nothing of the packaging runs.
    [AvaloniaFact]
    public async Task StartupCreatesNoPackagingWork()
    {
        // The window of a test has no service factory at all: building it and opening the first page makes no flow.
        using (var bare = WindowFixture.Create())
        {
            var tab = await bare.OpenAsync("pages-marked.pdf");
            await ViewFixture.WaitAsync(() => tab.Session.State == Raffica.Core.Viewing.ViewerState.Ready);
            await ViewFixture.WaitAsync(() => bare.Window.ViewOf(tab)!.FirstSharpPageShown);
            await Task.Delay(300, TestContext.Current.CancellationToken);
            ViewFixture.Pump();
            Assert.False(bare.Window.DefaultAppFlowCreated);
        }

        // With a factory but a window that has no page yet (and after the window went idle): the factory is not called.
        var made = 0;
        var fake = new FakeDefaultAppService { Status = Raffica.Core.Distribution.DefaultAppStatus.IsDefault };
        using (var idle = WindowFixture.Create())
        {
            idle.Window.DefaultAppServices = () =>
            {
                made++;
                return fake;
            };
            await idle.LoadRecentAsync();
            await Task.Delay(300, TestContext.Current.CancellationToken);
            ViewFixture.Pump();
            Assert.Equal(0, made);
            Assert.False(idle.Window.DefaultAppFlowCreated);

            // The first sharp page makes the flow, once, and a machine where Raffica is the default asks nothing more.
            var tab = await idle.OpenAsync("pages-marked.pdf");
            await ViewFixture.WaitAsync(() => idle.Window.ViewOf(tab)!.FirstSharpPageShown);
            await ViewFixture.WaitAsync(() => fake.StatusCalls == 1);
            Assert.Equal(1, made);
            Assert.True(idle.Window.DefaultAppFlowCreated);
            Assert.False(idle.Window.Host.IsOpen);
            Assert.Equal(0, fake.MakeCalls);

            // A second tab does not offer again.
            await idle.OpenAsync("hello_world.pdf");
            await Task.Delay(300, TestContext.Current.CancellationToken);
            Assert.Equal(1, made);
            Assert.Equal(1, fake.StatusCalls);
        }

        // "Don't ask again" stored: not even the factory is called.
        made = 0;
        using (var never = WindowFixture.Create(Prompt(DefaultAppPromptSetting.Never)))
        {
            never.Window.DefaultAppServices = () =>
            {
                made++;
                return fake;
            };
            var tab = await never.OpenAsync("pages-marked.pdf");
            await ViewFixture.WaitAsync(() => never.Window.ViewOf(tab)!.FirstSharpPageShown);
            await Task.Delay(300, TestContext.Current.CancellationToken);
            ViewFixture.Pump();
            Assert.Equal(0, made);
            Assert.False(never.Window.DefaultAppFlowCreated);
        }
    }

    // The notices (about 400 KB of text) are read from the assembly when the About card opens, not at startup, not when the palette
    // is searched and not when the help card opens.
    [AvaloniaFact]
    public async Task TheAboutCardReadsTheNoticesOnlyWhenItOpens()
    {
        var reads = 0;
        using var f = WindowFixture.Create();
        f.Window.NoticesSource = () =>
        {
            reads++;
            return "NOTICES";
        };
        var tab = await f.OpenAsync("pages-marked.pdf");
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(tab)!.FirstSharpPageShown);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Assert.Equal(0, reads);

        // No ribbon button: the command is not in the catalog; searching the palette finds it without reading anything.
        Assert.DoesNotContain(PdfCommandCatalog.Commands, c => c.Id == PdfCommandIds.About);
        Assert.Equal("app.about", PdfCommandIds.About);
        var entry = Assert.Single(f.Window.TitleSearch.ExtraResults!("about"), e => e.Group == PdfStrings.PaletteGroupApp);
        Assert.Equal(PdfStrings.CmdAbout, entry.Name);
        Assert.Equal(0, reads);

        // Opening the help card reads nothing either; its About button opens the About card, which reads the text once.
        _ = f.Window.ShowHelpAsync();
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen);
        Assert.Equal(PdfStrings.HelpTitle, f.Window.Host.ActiveDialog!.Title);
        Assert.Equal(0, reads);
        f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_About")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.AboutTitle);
        Assert.Equal(1, reads);
        f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Ok")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await ViewFixture.WaitAsync(() => !f.Window.Host.IsOpen);

        // The palette entry opens it as well.
        entry.Run();
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen);
        Assert.Equal(PdfStrings.AboutTitle, f.Window.Host.ActiveDialog!.Title);
        Assert.Equal(2, reads);
    }

    [AvaloniaFact]
    public async Task AnOfferAtStartShowsTheCardOnlyAfterTheFirstSharpPage()
    {
        var fake = new FakeDefaultAppService();
        using var f = WindowFixture.Create();
        f.Window.DefaultAppServices = () => fake;
        Assert.False(f.Window.Host.IsOpen);

        var tab = await f.OpenAsync("pages-marked.pdf");
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(tab)!.FirstSharpPageShown);
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen);
        Assert.Equal(Raffica.Pdf.PdfStrings.DefaultAppTitle, f.Window.Host.ActiveDialog!.Title);
        Assert.Equal(1, fake.StatusCalls);
        Assert.Equal(0, fake.MakeCalls);
    }

    [AvaloniaFact]
    public async Task ThePaletteOffersTheCommandAndNothingSitsInTheRibbon()
    {
        var fake = new FakeDefaultAppService { Result = Raffica.Core.Distribution.DefaultAppResult.OpenedSystemSettings };
        using var f = WindowFixture.Create();
        f.Window.DefaultAppServices = () => fake;

        // No ribbon button: the command is not in the catalog.
        Assert.DoesNotContain(PdfCommandCatalog.Commands, c => c.Id == PdfCommandIds.MakeDefaultApp);
        Assert.Equal("app.default", PdfCommandIds.MakeDefaultApp);

        var entry = Assert.Single(f.Window.TitleSearch.ExtraResults!("default app"), e => e.Group == PdfStrings.PaletteGroupApp);
        Assert.Equal(PdfStrings.CmdMakeDefaultApp, entry.Name);
        Assert.Equal(0, fake.MakeCalls);
        entry.Run();
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen);
        Assert.Equal(1, fake.MakeCalls);
        Assert.Equal(PdfStrings.DefaultAppResultTitle, f.Window.Host.ActiveDialog!.Title);
    }

    [AvaloniaFact]
    public void TheSettingsRowStoresTheOfferAndRunsTheCommandAfterOk()
    {
        try
        {
            var fake = new FakeDefaultAppService { Result = Raffica.Core.Distribution.DefaultAppResult.Failed };
            using var f = WindowFixture.Create();
            f.Window.DefaultAppServices = () => fake;
            f.Window.Settings.Open();
            ViewFixture.Pump();

            T Find<T>(string name) where T : Control => f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

            var ask = Find<CheckBox>("PART_DefaultAppAsk");
            Assert.True(ask.IsChecked);
            Assert.False(Find<CheckBox>("PART_DefaultAppNow").IsChecked);
            ask.IsChecked = false;
            Find<Button>("PART_Ok").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ViewFixture.Pump();

            Assert.Equal(DefaultAppPromptSetting.Never, f.Window.Settings.App.DefaultAppPrompt);
            Assert.Equal(0, fake.MakeCalls);

            // The second row runs the command once the dialog is closed.
            f.Window.Settings.Open();
            ViewFixture.Pump();
            Assert.False(Find<CheckBox>("PART_DefaultAppAsk").IsChecked);
            Find<CheckBox>("PART_DefaultAppNow").IsChecked = true;
            Find<Button>("PART_Ok").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ViewFixture.Pump();
            Assert.Equal(1, fake.MakeCalls);
        }
        finally
        {
            RestoreTheme();
        }
    }
}
