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

    // The settings source is the one App.CreateWindow uses: the first window gets the settings of the start of the process, a later one reads the files again.
    [AvaloniaFact]
    public void ASecondWindowReadsTheSettingsTheFirstWrote()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-host-settings-").FullName;
        try
        {
            var file = Path.Combine(folder, "settings.json");
            var options = PdfOptions.Parse(["--settings-file", file]);
            var source = new WindowSettingsSource(options.SettingsFile, new ShellSettings(), Raffica.Core.Settings.AppSettings.Default);

            var (shell, app) = source.Next();
            Assert.Null(shell.WindowPlacement);
            var first = new PdfWindow(options, shell, app);
            first.Show();
            Pump();

            PdfWindow? second = null;
            var host = new WindowHost(_ =>
            {
                var (nextShell, nextApp) = source.Next();
                second = new PdfWindow(options, nextShell, nextApp);
                second.Show();
                return second;
            }, () => { }, Dispatcher.UIThread);
            host.Adopt(first);

            // The first window is moved and closes: its own close writes the placement.
            var placed = new WindowPlacement(60, 70, 1100, 700, false);
            first.NoteBounds(placed, Environment.TickCount64);
            first.Close();
            Pump();
            Assert.Equal(placed, new SettingsStore(file).Load().WindowPlacement);
            Assert.Null(host.Current);

            host.Reopen();
            Pump();

            Assert.NotNull(second);
            Assert.Same(second, host.Current);
            Assert.Equal(1100, second.Width);
            Assert.Equal(700, second.Height);
            Assert.Equal(new Avalonia.PixelPoint(60, 70), second.Position);
            second.Close();
            Pump();
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [AvaloniaFact]
    public async Task AFileQueuedBehindTheCloseQuestionOpensInTheNextWindow()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var fixture = WindowFixture.Create();
        var path = Raffica.TestSupport.TestData.Pdf("hello_world.pdf");
        var seen = new List<IReadOnlyList<string>>();
        var next = new List<PdfWindow>();
        var host = new WindowHost(paths =>
        {
            seen.Add(paths);
            var options = PdfOptions.Parse(["--settings-file", Path.Combine(fixture.Folder, "settings.json")]);
            var window = new PdfWindow(options, new ShellSettings());
            window.Show();
            next.Add(window);
            return window;
        }, () => { }, Dispatcher.UIThread);
        host.Adopt(fixture.Window);
        await DirtyTabAsync(fixture.Window, copy);

        var attempt = fixture.Window.RunCloseAsync(quit: false);
        var card = await WaitForCardAsync(fixture.Window);
        fixture.Window.OnHandover([path]);
        for (var i = 0; i < 100 && fixture.Window.PendingHandover.Count == 0; i++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
            Pump();
        }

        Assert.Single(fixture.Window.PendingHandover);
        Press(card, "PART_DontSave");
        await attempt;
        Pump();

        Assert.Equal(Path.GetFullPath(path), Path.GetFullPath(Assert.Single(Assert.Single(seen))));
        Assert.Same(next[0], host.Current);
        next[0].Close();
        Pump();
    }

    [AvaloniaFact]
    public async Task AFileThatArrivesOnceTheCloseIsConfirmedIsHandedToTheNextWindow()
    {
        using var fixture = WindowFixture.Create();
        var path = Raffica.TestSupport.TestData.Pdf("hello_world.pdf");

        // Both drop paths: the check on the pool thread sees the confirmed close, or the answer reaches the UI thread after it.
        await fixture.Window.RunCloseAsync(quit: false);
        Assert.True(fixture.Window.IsClosing);
        fixture.Window.OnHandover([path]);
        Assert.Equal([path], fixture.Window.TakeUndelivered());
        Assert.Empty(fixture.Window.TakeUndelivered());
        Pump();

        var second = WindowFixture.Create();
        try
        {
            // The hand-over was accepted before the close was confirmed and shown after it.
            second.Window.OnHandover([path]);
            await second.Window.RunCloseAsync(quit: false);
            Pump();
            Assert.Equal([path], second.Window.TakeUndelivered().Select(Path.GetFullPath));
        }
        finally
        {
            second.Dispose();
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
