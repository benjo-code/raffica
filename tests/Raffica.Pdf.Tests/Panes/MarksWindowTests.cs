using Avalonia.Headless.XUnit;
using Raffica.Core.Annotations;
using Raffica.Core.Pdf;
using Raffica.Core.Text;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Panes;

public sealed class MarksWindowTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [AvaloniaFact]
    public async Task PaneIsPerTabAndSurvivesASave()
    {
        using var copy = new TempCopy("pages-marked.pdf");
        using var windows = WindowFixture.Create();
        var window = windows.Window;
        var first = (await window.Flow.OpenAsync(copy.Path))!;
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => window.ViewOf(first)!.FirstSharpPageShown);

        // Closed until asked for; 256 px wide.
        Assert.False(window.CommentsPane.IsOpen);
        Assert.Equal(256, window.CommentsPane.Width);
        window.ToggleMarksPane();
        ViewFixture.Pump();
        Assert.True(window.CommentsPane.IsOpen);
        var pane = window.MarksOf(first);
        Assert.NotNull(pane);
        Assert.Same(pane, window.CommentsPane.Content);

        // Another tab has its own pane, and it is closed.
        var second = await windows.OpenAsync("text-basic.pdf");
        await ViewFixture.WaitAsync(() => window.ViewOf(second)!.FirstSharpPageShown);
        Assert.False(window.CommentsPane.IsOpen);
        Assert.Null(window.MarksOf(second));
        window.ToggleMarksPane();
        ViewFixture.Pump();
        Assert.True(window.CommentsPane.IsOpen);
        Assert.NotSame(pane, window.MarksOf(second));
        window.ToggleMarksPane();
        ViewFixture.Pump();
        Assert.False(window.CommentsPane.IsOpen);

        // Back to the first tab: the same pane, still open.
        window.Workspace.Select(first);
        ViewFixture.Pump();
        Assert.True(window.CommentsPane.IsOpen);
        Assert.Same(pane, window.CommentsPane.Content);

        // A mark is added and saved: the pane is the same, still open, and reads the saved document.
        var made = await first.Annotations.Editor.ApplyDrawingAsync(1, DrawingGeometry.Rectangle(new PageRect(40, 40, 120, 90), 2), new AnnotationColor(220, 38, 38), null, null);
        var saved = await first.Annotations.Editor.SaveAsync(new PdfEngine(), copy.Path, false, null, Cancel);
        Assert.NotNull(saved);
        ViewFixture.Pump();
        await pane!.Model.RefreshAsync(new HashSet<int> { 1 });
        await pane.Model.Idle;
        ViewFixture.Pump();
        Assert.Same(pane, window.MarksOf(first));
        Assert.True(window.CommentsPane.IsOpen);
        Assert.Contains(pane.Model.Rows(MarkFilter.All), r => r is EntryRow e && e.Entry.Annotation.Name == made!.Name);
    }

    [AvaloniaFact]
    public async Task TheMarksCommandShowsAndHidesThePane()
    {
        using var windows = WindowFixture.Create();
        var window = windows.Window;
        var tab = await windows.OpenAsync("text-basic.pdf");
        await ViewFixture.WaitAsync(() => window.ViewOf(tab)!.FirstSharpPageShown);

        var command = window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == "annotate.marks");
        Assert.True(command.IsEnabled);
        command.Run!();
        ViewFixture.Pump();
        Assert.True(window.CommentsPane.IsOpen);
        command.Run!();
        ViewFixture.Pump();
        Assert.False(window.CommentsPane.IsOpen);
    }
}
