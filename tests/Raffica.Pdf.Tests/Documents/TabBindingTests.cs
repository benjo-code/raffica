using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Documents;
using Raffica.Shell;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Documents;

public sealed class TabBindingTests
{
    private static PdfWindow Show()
    {
        var options = PdfOptions.Parse(["--settings-file", Path.Combine(Path.GetTempPath(), "raffica-pdf-tests", "settings.json")]);
        var window = new PdfWindow(options, new ShellSettings());
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static async Task<PdfTab> AddAsync(PdfWindow window, PdfEngine engine)
    {
        var session = new ViewerSession(engine);
        await session.OpenAsync(TestData.Pdf("hello_world.pdf"));
        return window.Workspace.Add(session, TestData.Pdf("hello_world.pdf"));
    }

    [AvaloniaFact]
    public async Task DirtyStateShowsOnTheTabAndInTheStatusBar()
    {
        var window = Show();
        var tab = await AddAsync(window, new PdfEngine());
        Assert.Contains(tab.Tab, window.Tabs.Items);
        Assert.Same(tab.Tab, window.Tabs.Selected);
        Assert.Null(window.Status.SaveState);

        tab.MarkDirty();
        Assert.True(tab.Tab.IsDirty);
        Assert.Equal("Unsaved changes", window.Status.SaveState);

        tab.MarkClean();
        Assert.False(tab.Tab.IsDirty);
        Assert.Null(window.Status.SaveState);
    }

    [AvaloniaFact]
    public async Task TheStatusBarFollowsTheGermanTextAndTheActiveTab()
    {
        ShellStrings.Culture = CultureInfo.GetCultureInfo("de");
        var window = Show();
        var engine = new PdfEngine();
        var first = await AddAsync(window, engine);
        var second = await AddAsync(window, engine);

        first.MarkDirty();
        Assert.Null(window.Status.SaveState);

        window.Tabs.Selected = first.Tab;
        Assert.Same(first, window.Workspace.Active);
        Assert.Equal(ShellStrings.TabUnsavedChanges, window.Status.SaveState);
        Assert.NotEqual("Unsaved changes", window.Status.SaveState);

        window.Tabs.Selected = second.Tab;
        Assert.Null(window.Status.SaveState);
    }

    [AvaloniaFact]
    public async Task ClosingTheTabRemovesItFromTheStripAndShowsTheStartView()
    {
        var window = Show();
        var tab = await AddAsync(window, new PdfEngine());
        Assert.Same(window.DocumentPane, window.Frame.Document);

        Assert.True(await window.Workspace.CloseAsync(tab));
        Assert.Empty(window.Tabs.Items);
        Assert.Same(window.Start, window.Frame.Document);
    }

    [AvaloniaFact]
    public async Task ADirtyTabAsksBeforeItClosesAndCancelKeepsIt()
    {
        var window = Show();
        var tab = await AddAsync(window, new PdfEngine());
        tab.MarkDirty();

        var closing = window.Workspace.CloseAsync(tab);
        Dispatcher.UIThread.RunJobs();
        var dialog = window.Host.ActiveDialog;
        Assert.NotNull(dialog);

        var footer = Assert.IsType<StackPanel>(dialog.Footer);
        Assert.Equal("PART_Save PART_DontSave PART_Cancel", string.Join(" ", footer.Children.Select(c => ((Button)c).Name)));
        ((Button)footer.Children[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.False(await closing);
        Assert.Null(window.Host.ActiveDialog);
        Assert.Contains(tab.Tab, window.Tabs.Items);
    }
}
