using Avalonia.Headless.XUnit;
using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Documents;

/// <summary>
/// <see cref="PdfWindow.RebuildTab"/>: after Core's rewrite made the session adopt a new document, the per-tab pieces are made anew over it.
/// The rewrite is made through <see cref="PageEditApplier"/> with a hand-made plan on a temp copy; no organize UI exists yet.
/// </summary>
public sealed class TabRebuildTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<PdfTab> OpenAsync(WindowFixture windows, TempCopy copy)
    {
        var tab = await windows.Window.Flow.OpenAsync(copy.Path) ?? throw new InvalidOperationException("did not open");
        ViewFixture.Pump();
        return tab;
    }

    // Writes the plan over the file of the tab (the checked rewrite of Core); the session adopts the written file.
    private static async Task ApplyAsync(PdfTab tab, Func<PageEditPlan, PageEditPlan> change)
    {
        var plan = change(PageEditPlan.Identity(tab.Session.Document!.PageCount));
        var request = new PageApplyRequest(plan, tab.Path!, new Dictionary<PageSourceId, PageSourceFile>());
        var result = await Task.Run(() => PageEditApplier.ApplyAsync(tab.Session, new PdfEngine(), request, null, Ct), Ct);
        Assert.True(result.Adopted);
    }

    private static PagesPane ShowPanes(WindowFixture windows)
    {
        windows.Window.ShowPagesPane();
        ViewFixture.Pump();
        return Assert.IsType<PagesPane>(windows.Window.NavigationPane.Content);
    }

    [AvaloniaFact]
    public async Task RebuildShowsTheNewPageCountInViewAndPane()
    {
        using var copy = new TempCopy("pages-marked.pdf");
        using var windows = WindowFixture.Create(realSave: true);
        var tab = await OpenAsync(windows, copy);
        var before = tab.Session.Document!.PageCount;
        Assert.True(before >= 3);
        ShowPanes(windows);

        await ApplyAsync(tab, plan => plan.Delete(new HashSet<int> { 0 }));
        windows.Window.RebuildTab(tab, 0);
        ViewFixture.Pump();

        Assert.Equal(before - 1, tab.Session.Document!.PageCount);
        Assert.Equal(before - 1, windows.Window.ViewOf(tab)!.PageCount);
        var pane = Assert.IsType<PagesPane>(windows.Window.NavigationPane.Content);
        Assert.Equal(before - 1, pane.List.ItemCount);
        Assert.Same(windows.Window.ViewOf(tab), windows.Window.ActiveView);
    }

    [AvaloniaFact]
    public async Task RotatedPagesGetNewSizesAfterApply()
    {
        using var copy = new TempCopy("pages-marked.pdf");
        using var windows = WindowFixture.Create(realSave: true);
        var tab = await OpenAsync(windows, copy);
        var view = windows.Window.ViewOf(tab)!;
        await ViewFixture.WaitAsync(() => view.CurrentPageSize is not null);
        var size = view.CurrentPageSize!.Value;
        var count = view.PageCount;

        await ApplyAsync(tab, plan => plan.Rotate(Enumerable.Range(0, count).ToHashSet(), 90));
        windows.Window.RebuildTab(tab, 0);
        ViewFixture.Pump();

        var rebuilt = windows.Window.ViewOf(tab)!;
        Assert.NotSame(view, rebuilt);
        Assert.Equal(count, rebuilt.PageCount);
        await ViewFixture.WaitAsync(() => rebuilt.CurrentPageSize is not null);
        var turned = rebuilt.CurrentPageSize!.Value;
        Assert.Equal(size.Height, turned.Width, 1);
        Assert.Equal(size.Width, turned.Height, 1);
    }

    [AvaloniaFact]
    public async Task RebuildKeepsZoomLayoutAndFollowsThePage()
    {
        using var copy = new TempCopy("pages-marked.pdf");
        using var windows = WindowFixture.Create(realSave: true);
        var tab = await OpenAsync(windows, copy);
        var view = windows.Window.ViewOf(tab)!;
        view.SetLayout(PageLayoutMode.SinglePage);
        view.SetZoom(1.5);
        ViewFixture.Pump();
        var zoom = view.Zoom;
        var mode = view.ZoomMode;

        await ApplyAsync(tab, plan => plan.Delete(new HashSet<int> { 0 }));
        windows.Window.RebuildTab(tab, 1);
        ViewFixture.Pump();

        var rebuilt = windows.Window.ViewOf(tab)!;
        Assert.Equal(zoom, rebuilt.Zoom);
        Assert.Equal(mode, rebuilt.ZoomMode);
        Assert.Equal(PageLayoutMode.SinglePage, rebuilt.Layout);
        Assert.Equal(1, rebuilt.CurrentPage);

        // A page behind the new end is limited to the last page.
        windows.Window.RebuildTab(tab, 999);
        ViewFixture.Pump();
        Assert.Equal(tab.Session.Document!.PageCount - 1, windows.Window.ViewOf(tab)!.CurrentPage);
    }

    [AvaloniaFact]
    public async Task OldPiecesAreDisposedAndForgotten()
    {
        using var copy = new TempCopy("pages-marked.pdf");
        using var windows = WindowFixture.Create(realSave: true);
        var tab = await OpenAsync(windows, copy);
        var pane = ShowPanes(windows);
        windows.Window.ToggleMarksPane();
        ViewFixture.Pump();
        var view = windows.Window.ViewOf(tab)!;
        var marks = windows.Window.MarksOf(tab)!;
        var search = windows.Window.SearchOf(tab)!;
        var text = windows.Window.TextOf(tab)!;
        var annotate = windows.Window.AnnotateOf(tab)!;

        await ApplyAsync(tab, plan => plan.Delete(new HashSet<int> { 0 }));
        windows.Window.RebuildTab(tab, 0);
        ViewFixture.Pump();

        Assert.Null(view.Tab);
        Assert.True(pane.IsDisposed);
        Assert.NotSame(view, windows.Window.ViewOf(tab));
        Assert.NotSame(pane, windows.Window.NavigationPane.Content);
        Assert.NotSame(search, windows.Window.SearchOf(tab));
        Assert.NotSame(text, windows.Window.TextOf(tab));
        Assert.NotSame(annotate, windows.Window.AnnotateOf(tab));
        Assert.NotSame(marks, windows.Window.MarksOf(tab));
        Assert.NotNull(windows.Window.MarksOf(tab));

        // The old pane takes no part any more: the new one is the only one that follows the session.
        Assert.False(((PagesPane)windows.Window.NavigationPane.Content!).IsDisposed);
    }

    [AvaloniaFact]
    public async Task AnnotationAndFormSessionsStartCleanAfterTheRebuild()
    {
        using var copy = new TempCopy("forms-basic.pdf");
        using var windows = WindowFixture.Create(realSave: true);
        var tab = await OpenAsync(windows, copy);
        var annotations = tab.Annotations;
        var forms = tab.Forms;
        tab.MarkDirty();

        await ApplyAsync(tab, plan => plan.Rotate(new HashSet<int> { 0 }, 90));
        windows.Window.RebuildTab(tab, 0);
        ViewFixture.Pump();

        Assert.NotSame(annotations, tab.Annotations);
        Assert.NotSame(forms, tab.Forms);
        Assert.False(tab.IsDirty);
        Assert.False(tab.Annotations.IsDirty);
        Assert.False(tab.Annotations.CanUndo);
        Assert.NotNull(windows.Window.FormsOf(tab));
    }

    [AvaloniaFact]
    public async Task RebuildOfABackgroundTabShowsNothingUntilItIsSelected()
    {
        using var first = new TempCopy("pages-marked.pdf");
        using var second = new TempCopy("text-rich.pdf");
        using var windows = WindowFixture.Create(realSave: true);
        var background = await OpenAsync(windows, first);
        var active = await OpenAsync(windows, second);
        Assert.Same(active, windows.Window.Workspace.Active);
        var shown = windows.Window.ActiveView;
        var count = background.Session.Document!.PageCount;

        await ApplyAsync(background, plan => plan.Delete(new HashSet<int> { 0 }));
        windows.Window.RebuildTab(background, 0);
        ViewFixture.Pump();

        Assert.Null(windows.Window.ViewOf(background));
        Assert.Same(shown, windows.Window.ActiveView);

        windows.Window.Tabs.Selected = background.Tab;
        ViewFixture.Pump();

        Assert.Same(background, windows.Window.Workspace.Active);
        Assert.Equal(count - 1, windows.Window.ViewOf(background)!.PageCount);
    }

    [AvaloniaFact]
    public async Task NothingIsRebuiltForATabThatWasNotAsked()
    {
        using var first = new TempCopy("pages-marked.pdf");
        using var second = new TempCopy("text-rich.pdf");
        using var windows = WindowFixture.Create(realSave: true);
        var other = await OpenAsync(windows, first);
        var asked = await OpenAsync(windows, second);
        windows.Window.Tabs.Selected = other.Tab;
        ViewFixture.Pump();
        var view = windows.Window.ViewOf(other)!;
        var search = windows.Window.SearchOf(other);
        var annotations = other.Annotations;

        await ApplyAsync(asked, plan => plan.Rotate(new HashSet<int> { 0 }, 90));
        windows.Window.RebuildTab(asked, 0);
        ViewFixture.Pump();

        Assert.Same(view, windows.Window.ViewOf(other));
        Assert.Same(search, windows.Window.SearchOf(other));
        Assert.Same(annotations, other.Annotations);
        Assert.Same(view, windows.Window.ActiveView);
        Assert.NotNull(view.Tab);
    }
}
