using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Documents;
using Raffica.Shell;
using Raffica.Shell.Frame;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Documents;

public sealed class WindowCloseTests
{
    private static PdfWindow Show(out Func<int> closedCount)
    {
        var options = PdfOptions.Parse(["--settings-file", Path.Combine(Path.GetTempPath(), "raffica-pdf-tests", "settings.json")]);
        var window = new PdfWindow(options, new ShellSettings());
        var closed = 0;
        window.Closed += (_, _) => closed++;
        closedCount = () => closed;
        window.Show();
        Pump();
        return window;
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task<PdfTab> AddAsync(PdfWindow window)
    {
        var session = new ViewerSession(new PdfEngine());
        await session.OpenAsync(TestData.Pdf("hello_world.pdf"));
        return window.Workspace.Add(session, TestData.Pdf("hello_world.pdf"));
    }

    private static void Click(PdfWindow window, string name)
    {
        var footer = Assert.IsType<StackPanel>(window.Host.ActiveDialog!.Footer);
        var button = footer.Children.OfType<Button>().Single(b => b.Name == name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
    }

    [AvaloniaFact]
    public void AWindowWithoutATabCloses()
    {
        var window = Show(out var closed);
        window.Close();
        Pump();
        Assert.Equal(1, closed());
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task AWindowWithACleanTabClosesOnceAndDisposesTheSession()
    {
        var window = Show(out var closed);
        var tab = await AddAsync(window);
        Assert.NotNull(tab.Session.Document);

        window.Close();
        Pump();

        Assert.Equal(1, closed());
        Assert.False(window.IsVisible);
        Assert.Null(window.Host.ActiveDialog);
        Assert.Null(tab.Session.Document);
    }

    [AvaloniaFact]
    public async Task CancelOnTheCloseQuestionKeepsTheWindowAndTheTab()
    {
        var window = Show(out var closed);
        var tab = await AddAsync(window);
        tab.MarkDirty();

        window.Close();
        Pump();
        Assert.NotNull(window.Host.ActiveDialog);

        Click(window, "PART_Cancel");
        Assert.Equal(0, closed());
        Assert.True(window.IsVisible);
        Assert.Null(window.Host.ActiveDialog);
        Assert.Contains(tab.Tab, window.Tabs.Items);
        Assert.NotNull(tab.Session.Document);

        // The window can still be closed afterwards (the cancelled attempt left no state behind).
        window.Close();
        Pump();
        Click(window, "PART_DontSave");
        Assert.Equal(1, closed());
    }

    [AvaloniaFact]
    public async Task DontSaveClosesTheWindowWithoutAskingAgain()
    {
        var window = Show(out var closed);
        var tab = await AddAsync(window);
        tab.MarkDirty();

        window.Close();
        Pump();
        Click(window, "PART_DontSave");

        Assert.Equal(1, closed());
        Assert.False(window.IsVisible);
        Assert.Null(window.Host.ActiveDialog);
        Assert.Null(tab.Session.Document);
    }
}
