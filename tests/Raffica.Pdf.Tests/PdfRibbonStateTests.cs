using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Viewing;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Viewing;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests;

/// <summary>
/// M2.7 Task 10 step 4: the View tab shows what is on (the zoom fit mode, the page layout, the dark page and the pane that is open) as the
/// UIA Toggle state of its buttons; the state is read from the window and the active tab, not kept in the ribbon.
/// </summary>
public sealed class PdfRibbonStateTests
{
    private static ToggleState StateOf(PdfWindow window, string id)
    {
        var button = window.Ribbon.Band.GetVisualDescendants().OfType<RibbonButton>().First(b => b.Definition.Id == id);
        return Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(button)).ToggleState;
    }

    private static async Task<WindowFixture> OpenViewTabAsync()
    {
        var f = WindowFixture.Create(width: 1920, height: 700);
        var tab = await f.OpenAsync("outline.pdf");
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(tab)!.FirstSharpPageShown);
        f.Window.Ribbon.ActiveTab = f.Window.Ribbon.Definition!.Tabs.First(t => t.Letter == PdfRibbon.ViewLetter);
        PdfRibbonLayoutTests.Layout(f.Window);
        return f;
    }

    private static void Settle(PdfWindow window)
    {
        ViewFixture.Pump();
        PdfRibbonLayoutTests.Layout(window);
    }

    [AvaloniaFact]
    public async Task TheZoomFitModeThatIsOnIsPressedAndTheOthersAreNot()
    {
        using var f = await OpenViewTabAsync();
        var window = f.Window;
        var view = window.ActiveView!;

        foreach (var (mode, id) in new[] { (ZoomMode.FitWidth, PdfCommandIds.FitWidth), (ZoomMode.FitPage, PdfCommandIds.FitPage), (ZoomMode.ActualSize, PdfCommandIds.ActualSize) })
        {
            view.SetZoomMode(mode);
            Settle(window);
            foreach (var other in new[] { PdfCommandIds.FitWidth, PdfCommandIds.FitPage, PdfCommandIds.ActualSize })
            {
                Assert.Equal(other == id ? ToggleState.On : ToggleState.Off, StateOf(window, other));
            }
        }

        // A manual zoom ends the fit mode: none of the three is on.
        view.ZoomIn();
        Settle(window);
        Assert.Equal(ToggleState.Off, StateOf(window, PdfCommandIds.FitWidth));
        Assert.Equal(ToggleState.Off, StateOf(window, PdfCommandIds.FitPage));
        Assert.Equal(ToggleState.Off, StateOf(window, PdfCommandIds.ActualSize));
    }

    [AvaloniaFact]
    public async Task TheLayoutThatIsOnIsPressed()
    {
        using var f = await OpenViewTabAsync();
        var window = f.Window;
        var layouts = new[]
        {
            (PageLayoutMode.Continuous, PdfCommandIds.Continuous),
            (PageLayoutMode.SinglePage, PdfCommandIds.SinglePage),
            (PageLayoutMode.TwoPages, PdfCommandIds.TwoPages),
            (PageLayoutMode.TwoPagesWithCover, PdfCommandIds.TwoPagesCover),
        };

        foreach (var (mode, id) in layouts)
        {
            window.ActiveView!.SetLayout(mode);
            Settle(window);
            foreach (var (_, other) in layouts)
            {
                Assert.Equal(other == id ? ToggleState.On : ToggleState.Off, StateOf(window, other));
            }
        }
    }

    [AvaloniaFact]
    public async Task TheDarkPageAndThePaneThatIsOpenArePressed()
    {
        using var f = await OpenViewTabAsync();
        var window = f.Window;

        Assert.Equal(ToggleState.Off, StateOf(window, PdfCommandIds.DarkPage));
        window.ToggleDarkPage();
        Settle(window);
        Assert.Equal(ToggleState.On, StateOf(window, PdfCommandIds.DarkPage));
        window.ToggleDarkPage();
        Settle(window);
        Assert.Equal(ToggleState.Off, StateOf(window, PdfCommandIds.DarkPage));

        window.ShowPagesPane();
        Settle(window);
        Assert.Equal(ToggleState.On, StateOf(window, PdfCommandIds.PagesPane));
        Assert.Equal(ToggleState.Off, StateOf(window, PdfCommandIds.OutlinePane));
        window.ShowOutlinePane();
        Settle(window);
        Assert.Equal(ToggleState.Off, StateOf(window, PdfCommandIds.PagesPane));
        Assert.Equal(ToggleState.On, StateOf(window, PdfCommandIds.OutlinePane));
        window.CloseLeftPane();
        Settle(window);
        Assert.Equal(ToggleState.Off, StateOf(window, PdfCommandIds.PagesPane));
        Assert.Equal(ToggleState.Off, StateOf(window, PdfCommandIds.OutlinePane));
    }

    [AvaloniaFact]
    public async Task TheStateFollowsTheActiveTab()
    {
        using var f = await OpenViewTabAsync();
        var window = f.Window;
        window.ActiveView!.SetLayout(PageLayoutMode.SinglePage);
        window.ToggleDarkPage();
        Settle(window);
        var second = await f.OpenAsync("pages-marked.pdf");
        await ViewFixture.WaitAsync(() => window.ViewOf(second)!.FirstSharpPageShown);
        Settle(window);

        Assert.Equal(ToggleState.On, StateOf(window, PdfCommandIds.Continuous));
        Assert.Equal(ToggleState.Off, StateOf(window, PdfCommandIds.DarkPage));

        window.Workspace.SelectPrevious();
        Settle(window);
        Assert.Equal(ToggleState.On, StateOf(window, PdfCommandIds.SinglePage));
        Assert.Equal(ToggleState.On, StateOf(window, PdfCommandIds.DarkPage));
    }

    [AvaloniaFact]
    public void WithoutADocumentNothingIsOn()
    {
        using var f = WindowFixture.Create(width: 1920, height: 700);

        var toggles = f.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Where(c => c.IsToggle).ToList();
        Assert.Equal(10, toggles.Count);
        Assert.DoesNotContain(toggles, c => c.IsChecked);
    }
}
