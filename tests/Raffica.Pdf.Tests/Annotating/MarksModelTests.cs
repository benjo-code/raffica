using Avalonia.Headless.XUnit;
using Raffica.Core.Annotations;
using Raffica.Core.Text;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Annotating;

public sealed class MarksModelTests
{
    private static readonly AnnotationColor Red = new(220, 38, 38);

    // A model without a page view reads at once; the test waits until everything that was started is read.
    private static async Task<MarksModel> LoadedAsync(ViewFixture f)
    {
        var model = new MarksModel(f.Tab);
        await SettleAsync(model);
        return model;
    }

    private static async Task SettleAsync(MarksModel model)
    {
        for (var i = 0; i < 3; i++)
        {
            ViewFixture.Pump();
            await model.Idle;
        }
    }

    private static List<MarkEntry> Entries(MarksModel model, MarkFilter filter = MarkFilter.All) =>
        model.Rows(filter).OfType<EntryRow>().Select(r => r.Entry).ToList();

    private static Task<PdfAnnotation?> AddInkAsync(ViewFixture f, int page, double x, double y, string? comment = null) =>
        f.Tab.Annotations.Editor.ApplyDrawingAsync(page, DrawingGeometry.Ink([[new DisplayPoint(x, y), new DisplayPoint(x + 20, y + 5)]], 2), Red, comment, null);

    private static Task<PdfAnnotation?> AddRectangleAsync(ViewFixture f, int page, double left, double top) =>
        f.Tab.Annotations.Editor.ApplyDrawingAsync(page, DrawingGeometry.Rectangle(new PageRect(left, top, left + 40, top + 20), 2), Red, null, null);

    [AvaloniaFact]
    public async Task PopupsAndRepliesAreNotListed()
    {
        using var f = await ViewFixture.OpenAsync("annotations-foreign.pdf", show: false);
        var model = await LoadedAsync(f);

        var entries = Entries(model);
        Assert.DoesNotContain(entries, e => e.Annotation.Subtype is "Link" or "Popup" or "Widget");
        Assert.DoesNotContain(entries, e => e.Annotation.Name == "reply-0001");

        // The highlight with a reply, the bare highlight, the ink, the free text and the sticky note on page 1, the underline and the strike-out on page 2.
        Assert.Equal(7, entries.Count);
        var note = Assert.Single(entries, e => e.Annotation.Subtype == "Text");
        Assert.Equal("Other: Text", note.KindText);
        Assert.NotNull(note.LockReason);
    }

    [AvaloniaFact]
    public async Task SortOrderIsPageTopLeftIndex()
    {
        using var f = await ViewFixture.OpenAsync("pages-marked.pdf", show: false);
        var late = await AddRectangleAsync(f, 2, 50, 50);
        var right = await AddRectangleAsync(f, 0, 200, 100);
        var left = await AddRectangleAsync(f, 0, 50, 100);
        var top = await AddRectangleAsync(f, 0, 120, 40);
        var model = await LoadedAsync(f);

        Assert.Equal(new[] { top!.Name, left!.Name, right!.Name, late!.Name }, Entries(model).Select(e => e.Annotation.Name).ToArray());
    }

    [AvaloniaFact]
    public async Task GroupsManyCommentlessStrokesAndNeverGroupsCommentedOnes()
    {
        using var f = await ViewFixture.OpenAsync("pages-marked.pdf", show: false);
        for (var i = 0; i < 5; i++)
        {
            await AddInkAsync(f, 0, 20 + (30 * i), 100);
        }

        await AddInkAsync(f, 0, 20, 200, "keep me");

        // Page 2 has four strokes without a comment and one with: not five without a comment, so no group.
        for (var i = 0; i < 4; i++)
        {
            await AddInkAsync(f, 1, 20 + (30 * i), 100);
        }

        await AddInkAsync(f, 1, 20, 200, "and me");
        var model = await LoadedAsync(f);

        var rows = model.Rows(MarkFilter.All);
        var group = Assert.Single(rows.OfType<GroupRow>());
        Assert.Equal(0, group.Page);
        Assert.Equal(5, group.Strokes.Count);
        Assert.Equal("Freehand, 5 strokes", group.Text);
        Assert.False(group.Expanded);

        // Group, the commented stroke, and the five strokes of page 2 as single entries.
        Assert.Equal(7, rows.Count);
        Assert.Equal("keep me", Assert.IsType<EntryRow>(rows[1]).Entry.Annotation.Contents);

        model.ToggleGroup(0);
        rows = model.Rows(MarkFilter.All);
        Assert.True(Assert.Single(rows.OfType<GroupRow>()).Expanded);
        Assert.Equal(12, rows.Count);
        Assert.Equal(11, rows.OfType<EntryRow>().Count());
    }

    [AvaloniaFact]
    public async Task OnlyInvalidatedPagesAreReRead()
    {
        using var f = await ViewFixture.OpenAsync("annotations-foreign.pdf", show: false);
        var model = await LoadedAsync(f);
        Assert.Equal(1, model.ReadsOf(0));
        Assert.Equal(1, model.ReadsOf(1));

        await model.RefreshAsync(new HashSet<int> { 1 });
        Assert.Equal(1, model.ReadsOf(0));
        Assert.Equal(2, model.ReadsOf(1));

        // A change reaches the model through the invalidated pages of the session, on another thread; only that page is read again.
        await AddRectangleAsync(f, 1, 30, 30);
        await f.Tab.Annotations.FlushAsync();
        await SettleAsync(model);
        Assert.Equal(1, model.ReadsOf(0));
        Assert.Equal(3, model.ReadsOf(1));
        Assert.Equal(8, Entries(model).Count);
    }

    [AvaloniaFact]
    public async Task NothingIsReadBeforeTheFirstSharpPage()
    {
        using var f = await ViewFixture.OpenAsync("annotations-foreign.pdf", show: false);
        var model = new MarksModel(f.Tab, f.View);
        await SettleAsync(model);
        await model.RefreshAsync(null);
        Assert.Equal(0, model.ReadCount);
        Assert.Empty(model.Rows(MarkFilter.All));

        f.Window.Content = f.View;
        f.Window.Show();
        await ViewFixture.WaitAsync(() => f.View.FirstSharpPageShown);
        await ViewFixture.WaitAsync(() => model.ReadCount > 0);
        await SettleAsync(model);
        Assert.Equal(2, model.ReadCount);
        Assert.Equal(7, Entries(model).Count);
    }

    [AvaloniaFact]
    public async Task FilterChangesTheSummary()
    {
        using var f = await ViewFixture.OpenAsync("annotations-foreign.pdf", show: false);
        var model = await LoadedAsync(f);

        Assert.Equal("7 marks on 2 pages", model.Summary(MarkFilter.All));
        Assert.Equal("2 marks, all on page 1", model.Summary(MarkFilter.Highlight));
        Assert.Equal("1 mark on page 2", model.Summary(MarkFilter.Underline));
        Assert.Equal("1 mark on page 2", model.Summary(MarkFilter.StrikeOut));
        Assert.Equal("2 marks, all on page 1", model.Summary(MarkFilter.Drawings));
        Assert.Equal("1 mark on page 1", model.Summary(MarkFilter.Other));
    }

    [AvaloniaFact]
    public async Task AFileWithoutMarksHasAnEmptyModel()
    {
        using var f = await ViewFixture.OpenAsync("text-basic.pdf", show: false);
        var model = await LoadedAsync(f);

        Assert.False(model.HasMarks);
        Assert.Empty(model.Rows(MarkFilter.All));
        Assert.Equal(string.Empty, model.Summary(MarkFilter.All));
    }
}
