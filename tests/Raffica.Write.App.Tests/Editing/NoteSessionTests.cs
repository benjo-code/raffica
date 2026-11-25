using Avalonia.Headless.XUnit;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.WriteApp.Tests.Editing;

/// <summary>The session side of a note step: the layout shows the note after the insert, the first note makes styles (the layout starts over), and undo and redo take the note away and bring it back.</summary>
public sealed class NoteSessionTests
{
    [AvaloniaFact]
    public async Task InsertingAFootnoteShowsItAndUndoAndRedoMoveItAway()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = await window.Flow.NewDocumentAsync(PaperSize.A4, CancellationToken.None);
        WindowFixture.Pump();
        Assert.NotNull(tab);
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted);
        Assert.True(tab.Runner.FullModelReady);
        var session = tab.EnsureEditor();
        var model = tab.Docx.Model;
        var layout = tab.Runner.PageLayout;
        var restarts = layout.Stats.Restarts;
        var caret = session.Selection!.Value.Start;

        var result = session.Apply(e => e.InsertNote(caret, NoteType.Footnote), 0);

        Assert.NotNull(result?.Objects);
        Assert.NotNull(result.Style);
        Assert.True(layout.Stats.Restarts > restarts);
        Assert.True(tab.IsDirty);
        Assert.Contains(layout.Page(0).TextLines, l => l.Story.Kind == StoryKind.Note);
        Assert.Equal(StoryKind.Note, session.Selection!.Value.Start.Story.Kind);

        Assert.NotNull(session.Apply(e => e.History.Undo(), 0));
        Assert.DoesNotContain(layout.Page(0).TextLines, l => l.Story.Kind == StoryKind.Note);
        Assert.Null(model.Footnotes!.Find(1));
        Assert.Equal(StoryKind.Body, session.Selection!.Value.Start.Story.Kind);

        Assert.NotNull(session.Apply(e => e.History.Redo(), 0));
        Assert.Contains(layout.Page(0).TextLines, l => l.Story.Kind == StoryKind.Note);
        Assert.NotNull(model.Footnotes!.Find(1));

        // The second note makes no style: the layout is not started over.
        var second = session.Apply(e => e.InsertNote(new TextPosition(StoryRef.Body, (Paragraph)model.Body.Blocks[0], 0), NoteType.Footnote), 0);
        Assert.NotNull(second);
        Assert.Null(second.Style);
        Assert.Equal(2, layout.Page(0).TextLines.Count(l => l.Story.Kind == StoryKind.Note && l.Start == 0));
    }
}
