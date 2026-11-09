using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Raffica.Shell;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests;

public sealed class StartViewTests
{
    private static PdfWindow Show()
    {
        // A settings file of its own: a test never reads or writes the settings of the user.
        var options = PdfOptions.Parse(["--settings-file", Path.Combine(Path.GetTempPath(), "raffica-pdf-tests", "settings.json")]);
        var window = new PdfWindow(options, new ShellSettings());
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void StartViewIsShownWithoutTabsAndHidesWithOne()
    {
        var window = Show();
        var start = Assert.IsType<StartView>(window.Frame.Document);
        Assert.Equal(StartApp.Pdf, start.OwnApp);
        Assert.Null(window.Frame.StatusBar);
        Assert.Null(window.Frame.LeftPane);

        var tab = new DocumentTab("a.pdf");
        window.Tabs.Items.Add(tab);
        window.Tabs.Selected = tab;
        Assert.Same(window.DocumentPane, window.Frame.Document);
        Assert.Same(window.Status, window.Frame.StatusBar);
        Assert.Same(window.NavigationPane, window.Frame.LeftPane);

        window.Tabs.Items.Remove(tab);
        Assert.Same(window.Start, window.Frame.Document);
        Assert.Null(window.Frame.StatusBar);
    }

    private static Button[] ButtonsOf(Avalonia.Visual root, string cls) =>
        [.. Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<Button>().Where(b => b.Classes.Contains(cls))];

    private static void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void TheStartViewHasOneTileOpenPdfThatOpensThePicker()
    {
        var window = Show();
        var tile = Assert.Single(ButtonsOf(window.Start, "start-tile"));
        Assert.Equal(Raffica.Shell.Localization.ShellStrings.StartOpenPdf, Avalonia.Automation.AutomationProperties.GetName(tile));

        var calls = 0;
        var gate = new TaskCompletionSource<IReadOnlyList<string>>();
        window.FilePicker = () =>
        {
            calls++;
            return gate.Task;
        };
        Click(tile);
        Assert.Equal(1, calls);

        // A second click while the picker is open does not start another one.
        Click(tile);
        Assert.Equal(1, calls);
        gate.SetResult([]);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ThePlusOfTheTabStripIsNamedOpenFileAndOpensThePicker()
    {
        var window = Show();
        var plus = Assert.Single(ButtonsOf(window.Tabs, "document-tab-new"));
        Assert.Equal(PdfStrings.OpenFileButton, Avalonia.Automation.AutomationProperties.GetName(plus));
        Assert.Equal("Open file", PdfStrings.OpenFileButton);

        var calls = 0;
        var gate = new TaskCompletionSource<IReadOnlyList<string>>();
        window.FilePicker = () =>
        {
            calls++;
            return gate.Task;
        };
        Click(plus);
        Click(plus);
        Assert.Equal(1, calls);
        gate.SetResult([]);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ThePanesHaveTheirWidthsAndTheCommentsPaneIsReservedAndClosed()
    {
        var window = Show();
        Assert.Equal(204, window.NavigationPane.Width);
        Assert.Equal(256, window.CommentsPane.Width);
        Assert.False(window.NavigationPane.IsOpen);

        // With a document the pages pane opens; the comments pane stays closed (reserved for a later milestone).
        window.Tabs.Items.Add(new DocumentTab("a.pdf"));
        Assert.True(window.NavigationPane.IsOpen);
        Assert.False(window.CommentsPane.IsOpen);
    }

    [AvaloniaFact]
    public void TheWindowShowsTheRibbonOfTheApp()
    {
        var window = Show();
        Assert.Equal("Raffica PDF", window.Title);
        Assert.Equal("H", window.Ribbon.ActiveTab?.Letter);
        Assert.Equal(7, window.Ribbon.Definition!.Tabs.Count);
    }
}
