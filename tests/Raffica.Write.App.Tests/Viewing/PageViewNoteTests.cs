using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Viewing;
using Raffica.WriteApp.Tests.Commands;
using Xunit;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>The note commands in the page view (milestone 4.5a, Task 3): the caret after an insert, Back to the text with the command and with Esc, the double click on a reference and on a note number, and the prefix document.</summary>
public sealed class PageViewNoteTests : IDisposable
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(60);
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-notes-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // The system cleans the temp folder.
        }
    }

    private async Task<ViewHarness> OpenAsync(params string[] paragraphs)
    {
        var path = StyledDocx.Write(_folder, Guid.NewGuid().ToString("N") + ".docx", StyledDocx.Styles, paragraphs);
        var harness = new ViewHarness(path, 900, 1000);
        harness.View.Runner.LeaseWaitTimeout = GateTimeout;
        harness.View.Runner.Start();
        await ViewHarness.WaitAsync(() => harness.View.Runner.FullModelReady || harness.View.Runner.IsFaulted, 30000);
        Assert.True(harness.View.Runner.FullModelReady);
        var layout = harness.View.Runner.PageLayout;
        await ViewHarness.WaitAsync(() => layout.IsComplete, 30000);
        return harness;
    }

    private static string Text(string text) => $"<w:r><w:t>{text}</w:t></w:r>";

    private static Paragraph P(ViewHarness h, int index) => (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[index];

    private static EditSession Place(ViewHarness h, int paragraph, int offset)
    {
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        var at = new TextPosition(StoryRef.Body, P(h, paragraph), offset);
        session.SetSelection(new TextRange(at, at));
        h.View.Focus();
        return session;
    }

    private static double Scale(PageView view) => view.Zoom * 96 / 72;

    private static Point ViewPoint(PageView view, int page, double x, double y)
    {
        var rect = view.PageBounds(page)!.Value;
        return new Point(rect.X + (x * Scale(view)), rect.Y + (y * Scale(view)));
    }

    private static void Stroke(ViewHarness h, PhysicalKey key)
    {
        h.Window.KeyPressQwerty(key, RawInputModifiers.None);
        h.Window.KeyReleaseQwerty(key, RawInputModifiers.None);
        ViewHarness.Pump();
    }

    // A double click on the character box at a position (a point just inside its left edge, in the middle of the line).
    private static void DoubleClickAt(ViewHarness h, TextPosition position)
    {
        var box = h.View.Runner.PageLayout.Caret(position, 0)!.Value;
        h.View.HandlePress(ViewPoint(h.View, box.Page, box.X + 1, box.Top + (box.Height / 2)), 2, KeyModifiers.None, rightButton: false);
        ViewHarness.Pump();
    }

    [AvaloniaFact]
    public async Task AfterInsertTheCaretIsInTheNote()
    {
        using var h = await OpenAsync(Text("alpha beta gamma"), Text("delta"));
        var session = Place(h, 0, 5);

        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteFootnote));

        var note = Assert.Single(h.Tab.Docx.Model.Footnotes!.Notes, n => n.Kind == NoteKind.Normal);
        var caret = session.Caret!.Value;
        Assert.Equal(StoryKind.Note, caret.Story.Kind);
        Assert.Equal(new StoryRef(StoryKind.Note, note.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)), caret.Story);
        Assert.Same(note.Content.Blocks[0], caret.Paragraph);
        Assert.Equal(caret, h.View.Caret);

        // The caret is drawn: the layout shows the note at the bottom of the page and the caret box exists.
        Assert.NotNull(h.View.Runner.PageLayout.Caret(caret, 0));
        Assert.NotNull(h.View.CaretBarBounds);
    }

    [AvaloniaFact]
    public async Task BackReturnsToTheReferenceAndEscapeToo()
    {
        using var h = await OpenAsync(Text("alpha beta gamma"), Text("delta"));
        var session = Place(h, 0, 5);
        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteFootnote));
        Assert.Equal(StoryKind.Note, session.Caret!.Value.Story.Kind);

        // The command: the caret is in the body, in the paragraph of the reference and behind the mark.
        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteBack));
        var back = session.Caret!.Value;
        Assert.Equal(StoryRef.Body, back.Story);
        Assert.Same(P(h, 0), back.Paragraph);
        Assert.True(back.Offset > 5, $"the caret is behind the mark ({back.Offset})");

        // Esc in the note does the same. After an undo and a redo the insert puts the caret in the body, so the way into the note is the jump.
        Assert.True(h.View.RunEditCommand(WriteCommandIds.Undo));
        Assert.True(h.View.RunEditCommand(WriteCommandIds.Redo));
        var reference = session.Caret!.Value;
        Assert.Equal(StoryRef.Body, reference.Story);
        Assert.True(h.View.GoToNote(reference));
        Assert.Equal(StoryKind.Note, session.Caret!.Value.Story.Kind);

        h.View.Focus();
        Stroke(h, PhysicalKey.Escape);
        Assert.Equal(StoryRef.Body, session.Caret!.Value.Story);
        Assert.Same(P(h, 0), session.Caret.Value.Paragraph);

        // Esc in the body does nothing to the caret.
        var before = session.Caret.Value;
        Stroke(h, PhysicalKey.Escape);
        Assert.Equal(before, session.Caret.Value);
    }

    [AvaloniaFact]
    public async Task BackFindsTheReferenceOfAnEndnoteAndOfASecondNote()
    {
        using var h = await OpenAsync(Text("alpha beta gamma"), Text("delta epsilon"));
        var session = Place(h, 1, 5);
        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteEndnote));
        var endnote = session.Caret!.Value;
        Assert.Equal("endnote:1", endnote.Story.Key);
        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteBack));
        Assert.Same(P(h, 1), session.Caret!.Value.Paragraph);

        Place(h, 0, 5);
        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteFootnote));
        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteBack));
        Assert.Same(P(h, 0), session.Caret!.Value.Paragraph);

        // Back outside a note says so and changes nothing.
        var notes = new List<string>();
        h.View.StructureNote += notes.Add;
        var before = session.Caret.Value;
        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteBack));
        Assert.Equal([WriteStrings.ReasonNoteNotInNote], notes);
        Assert.Equal(before, session.Caret.Value);
    }

    [AvaloniaFact]
    public async Task DoubleClickOnTheReferenceJumpsToTheNote()
    {
        using var h = await OpenAsync(Text("alpha beta gamma"), Text("delta"));
        var session = Place(h, 0, 5);
        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteFootnote));
        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteBack));
        var note = Assert.Single(h.Tab.Docx.Model.Footnotes!.Notes, n => n.Kind == NoteKind.Normal);

        // The reference stands at offset 5 (the caret was there when it went in): a double click on it goes to the note instead of selecting a word.
        DoubleClickAt(h, new TextPosition(StoryRef.Body, P(h, 0), 5));

        var caret = session.Caret!.Value;
        Assert.Equal(StoryKind.Note, caret.Story.Kind);
        Assert.Same(note.Content.Blocks[0], caret.Paragraph);
        Assert.True(session.Selection!.Value.IsEmpty);

        // A double click on the number of the note goes back to the text.
        DoubleClickAt(h, new TextPosition(caret.Story, caret.Paragraph, 0));
        Assert.Equal(StoryRef.Body, session.Caret!.Value.Story);
        Assert.Same(P(h, 0), session.Caret.Value.Paragraph);
        Assert.True(session.Selection!.Value.IsEmpty);
    }

    [AvaloniaFact]
    public async Task ADoubleClickOnAWordNextToTheReferenceStillSelectsTheWord()
    {
        using var h = await OpenAsync(Text("alpha beta gamma"), Text("delta"));
        var session = Place(h, 0, 5);
        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteFootnote));
        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteBack));

        // "gamma" starts behind the reference; its first letter is a word, not a note.
        var layout = h.View.Runner.PageLayout;
        var projection = ParagraphProjection.Build(P(h, 0), layout.ProjectionHost(StoryRef.Body, P(h, 0)));
        var at = projection.Text.IndexOf("gamma", StringComparison.Ordinal);
        Assert.True(at > 5);
        DoubleClickAt(h, new TextPosition(StoryRef.Body, P(h, 0), at + 1));

        var selection = session.Selection!.Value.Normalized();
        Assert.Equal(StoryRef.Body, selection.Start.Story);
        Assert.Equal("gamma", projection.Text[selection.Start.Offset..selection.End.Offset]);
    }

    [AvaloniaFact]
    public async Task AGoToNoteOnAPlaceThatIsNoReferenceDoesNothing()
    {
        using var h = await OpenAsync(Text("alpha beta gamma"));
        var session = Place(h, 0, 5);
        var before = session.Caret!.Value;
        Assert.False(h.View.GoToNote(before));
        Assert.Equal(before, session.Caret!.Value);
        Assert.False(h.View.BackToReference());
        Assert.Equal(before, session.Caret!.Value);
    }

    [AvaloniaFact]
    public async Task APrefixDocumentRefusesNotesWithItsReason()
    {
        var paragraphs = Enumerable.Range(0, 120).Select(i => Text("paragraph " + i)).ToArray();
        var path = StyledDocx.Write(_folder, "long.docx", StyledDocx.Styles, paragraphs);
        using var h = new ViewHarness(path, 900, 700);

        // Only the first blocks are open: the editor cannot see that the body was cut, so the command and the rule pass the flag.
        Assert.True(h.Tab.Docx.IsPrefix);
        var notes = new List<string>();
        h.View.StructureNote += notes.Add;
        var revision = h.Tab.Docx.Model.Edits.Revision;
        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteFootnote));
        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteEndnote));
        Assert.Equal([WriteStrings.ReasonSectionPrefix, WriteStrings.ReasonSectionPrefix], notes);
        Assert.Equal(revision, h.Tab.Docx.Model.Edits.Revision);
        Assert.Null(h.Tab.Docx.Model.Footnotes);
        Assert.Null(h.Tab.Docx.Model.Endnotes);

        var facts = new ObjectFactsSource().Read(h.Tab, WriteCommandIds.NoteFootnote, writingView: false);
        Assert.Equal(ObjectBlock.PrefixDocument, facts.Block);
    }

    [AvaloniaFact]
    public async Task DoubleClickOnAReferenceInTheWritingViewSelectsTheWordAndDoesNotEnterTheNote()
    {
        using var h = await OpenAsync(Text("alpha beta gamma"), Text("delta"));
        var session = Place(h, 0, 5);
        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteFootnote));
        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteBack));
        h.View.Mode = Raffica.Write.Settings.WriteView.Writing;
        await ViewHarness.WaitAsync(() => h.View.CaretBounds(new TextPosition(StoryRef.Body, P(h, 0), 5)) is not null, 30000);

        var bar = h.View.CaretBounds(new TextPosition(StoryRef.Body, P(h, 0), 5))!.Value;
        h.View.HandlePress(new Point(bar.X + 1, bar.Y + (bar.Height / 2)), 2, KeyModifiers.None, rightButton: false);
        ViewHarness.Pump();

        // The writing view draws no notes: the double click is a word, and the caret stays in the body.
        var selection = session.Selection!.Value;
        Assert.Equal(StoryRef.Body, selection.Start.Story);
        Assert.Equal(StoryRef.Body, selection.End.Story);
        Assert.False(selection.IsEmpty);
        Assert.False(h.View.GoToNote(new TextPosition(StoryRef.Body, P(h, 0), 5)));
        Assert.Equal(StoryRef.Body, session.Caret!.Value.Story);
    }

    [AvaloniaFact]
    public async Task SwitchingToTheWritingViewWhileTheCaretIsInANoteMovesItBackToTheReference()
    {
        using var h = await OpenAsync(Text("alpha beta gamma"), Text("delta"));
        var session = Place(h, 0, 5);
        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteFootnote));
        Assert.Equal(StoryKind.Note, session.Caret!.Value.Story.Kind);

        h.View.Mode = Raffica.Write.Settings.WriteView.Writing;

        Assert.Equal(StoryRef.Body, session.Caret!.Value.Story);
        Assert.Same(P(h, 0), session.Caret.Value.Paragraph);
        Assert.True(session.Caret.Value.Offset > 5);
    }

    [AvaloniaFact]
    public async Task TheEditorGetsThePrefixFlagFromTheRunPath()
    {
        // The view has the whole model, so the early "still opening" answer is out of the way: only the flag the command passes to the editor can refuse here.
        using var h = await OpenAsync(Text("alpha beta gamma"));
        var session = Place(h, 0, 5);
        var notes = new List<string>();
        h.View.StructureNote += notes.Add;
        h.View.PrefixProbe = () => true;
        var revision = h.Tab.Docx.Model.Edits.Revision;

        Assert.True(h.View.RunNoteCommand(WriteCommandIds.NoteFootnote));

        Assert.Equal([WriteStrings.ReasonSectionPrefix], notes);
        Assert.Equal(revision, h.Tab.Docx.Model.Edits.Revision);
        Assert.Null(h.Tab.Docx.Model.Footnotes);
        Assert.Equal(StoryRef.Body, session.Caret!.Value.Story);
    }
}
