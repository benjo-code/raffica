using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Hosting;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.Hosting;

public sealed class WindowHostWindowTests
{
    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task<Dialog> WaitForCardAsync(PdfWindow window)
    {
        for (var i = 0; i < 400 && window.Host.ActiveDialog is null; i++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
            Pump();
        }

        return window.Host.ActiveDialog ?? throw new InvalidOperationException("no card");
    }

    private static void Press(Dialog card, string name) =>
        Assert.IsType<StackPanel>(card.Footer).Children.OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task<PdfTab> DirtyTabAsync(PdfWindow window, TempCopy copy)
    {
        var tab = await window.Flow.OpenAsync(copy.Path) ?? throw new InvalidOperationException("did not open");
        Pump();
        Assert.NotNull(await AnnotatedFile.HighlightAsync(tab.Annotations));
        Assert.True(tab.IsDirty);
        return tab;
    }

    private static void DeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [AvaloniaFact]
    public async Task QuitRequestedIsClearedByACancelledAttempt()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var fixture = WindowFixture.Create();
        var shutdowns = 0;
        var host = new WindowHost(_ => throw new InvalidOperationException("no window is made"), () => shutdowns++, Dispatcher.UIThread);
        host.Adopt(fixture.Window);
        await DirtyTabAsync(fixture.Window, copy);

        var attempt = fixture.Window.RunCloseAsync(quit: true);
        var card = await WaitForCardAsync(fixture.Window);
        Assert.True(fixture.Window.QuitRequested);
        Press(card, "PART_Cancel");
        await attempt;
        Pump();

        Assert.False(fixture.Window.QuitRequested);
        Assert.Equal(0, shutdowns);

        // A later ordinary close of the same window must not end the app.
        fixture.Window.Close();
        card = await WaitForCardAsync(fixture.Window);
        Assert.False(fixture.Window.QuitRequested);
        Press(card, "PART_DontSave");
        for (var i = 0; i < 100 && fixture.Window.IsVisible; i++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
            Pump();
        }

        Assert.False(fixture.Window.IsVisible);
        Assert.Equal(0, shutdowns);
        Assert.Null(host.Current);
    }

    [AvaloniaFact]
    public async Task AConfirmedQuitCallsShutdownOnceTheWindowIsClosed()
    {
        using var fixture = WindowFixture.Create();
        var shutdowns = 0;
        var host = new WindowHost(_ => throw new InvalidOperationException("no window is made"), () => shutdowns++, Dispatcher.UIThread);
        host.Adopt(fixture.Window);

        await fixture.Window.RunCloseAsync(quit: true);
        Assert.True(fixture.Window.IsClosing);
        Assert.Equal(0, shutdowns);
        Pump();

        Assert.False(fixture.Window.IsVisible);
        Assert.Equal(1, shutdowns);
        Assert.Null(host.Current);
    }

    [AvaloniaFact]
    public void ASecondWindowReadsTheSettingsTheFirstWrote()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-host-settings-").FullName;
        try
        {
            var file = Path.Combine(folder, "settings.json");
            var options = PdfOptions.Parse(["--settings-file", file]);
            var first = new PdfWindow(options, new ShellSettings());
            first.Show();
            Pump();
            Assert.False(first.Frame.IsCompact);

            PdfWindow? second = null;
            var host = new WindowHost(_ =>
            {
                var (shell, app) = FreshSettings.Read(options.SettingsFile);
                second = new PdfWindow(options, shell, app);
                second.Show();
                return second;
            }, () => { }, Dispatcher.UIThread);
            host.Adopt(first);

            // The first window closes and has written its placement; the compact bar was chosen in it.
            first.Close();
            Pump();
            var store = new SettingsStore(file);
            store.Save(store.Load() with { CompactCommandBar = true });
            Assert.Null(host.Current);

            host.Reopen();
            Pump();

            Assert.NotNull(second);
            Assert.Same(second, host.Current);
            Assert.True(second.Frame.IsCompact);
            second.Close();
            Pump();
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [AvaloniaFact]
    public void ClosingTheLastWindowKeepsTheHostAndReopenMakesAWindow()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-host-reopen-").FullName;
        try
        {
            var options = PdfOptions.Parse(["--settings-file", Path.Combine(folder, "settings.json")]);
            var shutdowns = 0;
            var made = new List<PdfWindow>();
            var host = new WindowHost(_ =>
            {
                var window = new PdfWindow(options, new ShellSettings());
                window.Show();
                made.Add(window);
                return window;
            }, () => shutdowns++, Dispatcher.UIThread);
            var first = new PdfWindow(options, new ShellSettings());
            first.Show();
            Pump();
            host.Adopt(first);

            first.Close();
            Pump();
            Assert.Null(host.Current);
            Assert.Equal(0, shutdowns);
            Assert.Empty(made);

            host.Reopen();
            Pump();

            var window = Assert.Single(made);
            Assert.Same(window, host.Current);
            Assert.True(window.IsVisible);
            window.Close();
            Pump();
            Assert.Equal(0, shutdowns);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }
}
