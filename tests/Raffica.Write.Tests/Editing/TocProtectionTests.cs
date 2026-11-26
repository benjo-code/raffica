using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>A table of contents spans many paragraphs and updates itself: nothing is typed, split, pasted or partly deleted in it, a range over the whole field removes it in one step.</summary>
public sealed class TocProtectionTests
{
    private const string Reason = "This table of contents updates itself. Change the headings, or remove it.";

    private static readonly string[] s_original = ["Intro", "One", "Two", "Three", "After"];

    private static Run Mark(FieldCharKind kind)
    {
        var run = new Run();
        run.Content.Add(new FieldChar(kind));
        return run;
    }

    private static Run Code(string text)
    {
        var run = new Run();
        run.Content.Add(new InstructionText(text));
        return run;
    }

    private static Run Text(string text)
    {
        var run = new Run();
        run.Content.Add(new TextContent(text));
        return run;
    }

    // Intro, three entries (the begin, the instruction and the separate in the first, the end in the last), After.
    private sealed record Fixture(WordDocument Document, TextEditor Editor, Paragraph Intro, Paragraph One, Paragraph Two, Paragraph Three, Paragraph After);

    private static Fixture Make()
    {
        var intro = new Paragraph();
        intro.Inlines.Add(Text("Intro"));
        var one = new Paragraph();
        one.Inlines.Add(Mark(FieldCharKind.Begin));
        one.Inlines.Add(Code(" TOC \\o \"1-3\" \\h \\z \\u "));
        one.Inlines.Add(Mark(FieldCharKind.Separate));
        one.Inlines.Add(Text("One"));
        var two = new Paragraph();
        two.Inlines.Add(Text("Two"));
        var three = new Paragraph();
        three.Inlines.Add(Text("Three"));
        three.Inlines.Add(Mark(FieldCharKind.End));
        var after = new Paragraph();
        after.Inlines.Add(Text("After"));
        var story = new Story(new OoxmlRoot(System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main") + "document", []));
        foreach (var block in new[] { intro, one, two, three, after })
        {
            story.Blocks.Add(block);
        }

        var document = new WordDocument(story);
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain) { FragmentCodec = new Raffica.Write.Docx.DocxFragmentCodec() };
        return new Fixture(document, editor, intro, one, two, three, after);
    }

    private static string Visible(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static string[] Texts(WordDocument document) => document.Body.Blocks.OfType<Paragraph>().Select(Visible).ToArray();

    private static int FieldChars(WordDocument document) =>
        document.Body.Blocks.OfType<Paragraph>().Sum(p => p.Inlines.OfType<Run>().Sum(r => r.Content.OfType<FieldChar>().Count()));

    private static TextPosition At(Paragraph paragraph, int offset) => new(StoryRef.Body, paragraph, offset);

    private static TextRange Caret(Paragraph paragraph, int offset) => new(At(paragraph, offset), At(paragraph, offset));

    private static TextRange Sel(Paragraph first, int start, Paragraph second, int end) => new(At(first, start), At(second, end));

    private static void AssertUntouched(Fixture f, int revision)
    {
        Assert.Equal(revision, f.Document.Edits.Revision);
        Assert.Equal(s_original, Texts(f.Document));
        Assert.Equal(3, FieldChars(f.Document));
    }

    [Fact]
    public void TypingInsideATocIsRefusedWithAReason()
    {
        var f = Make();
        var revision = f.Document.Edits.Revision;

        var inside = f.Editor.InsertText(Caret(f.Two, 1), "x");
        Assert.Empty(inside.Changes);
        Assert.Equal(FieldRefusalKind.InsideToc, f.Editor.LastFieldRefusal?.Kind);
        Assert.Equal(Reason, f.Editor.LastFieldRefusal?.Reason);

        // The edges of the field are inside as well: the first paragraph holds the begin, the last the end.
        Assert.Empty(f.Editor.InsertText(Caret(f.One, 0), "x").Changes);
        Assert.Empty(f.Editor.InsertText(Caret(f.Three, 5), "x").Changes);
        Assert.Empty(f.Editor.InsertText(Sel(f.Two, 0, f.Two, 2), "x").Changes);
        AssertUntouched(f, revision);

        // Outside the field typing is as before and clears the reason.
        Assert.NotEmpty(f.Editor.InsertText(Caret(f.After, 0), "x").Changes);
        Assert.Null(f.Editor.LastFieldRefusal);
    }

    [Fact]
    public void EnterPasteAndSplitInsideAreRefused()
    {
        var f = Make();
        var revision = f.Document.Edits.Revision;

        Assert.Empty(f.Editor.SplitParagraph(Caret(f.Two, 1)).Changes);
        Assert.Equal(FieldRefusalKind.InsideToc, f.Editor.LastFieldRefusal?.Kind);
        Assert.Empty(f.Editor.InsertText(Caret(f.Two, 1), "a\nb").Changes);

        var text = new ClipboardContent("pasted", null);
        Assert.Empty(f.Editor.Paste(Caret(f.Two, 1), text, _ => true).Changes);
        Assert.Equal(FieldRefusalKind.InsideToc, f.Editor.LastFieldRefusal?.Kind);

        var fragment = f.Editor.Copy(Sel(f.Intro, 0, f.Intro, 5));
        Assert.NotNull(fragment.Fragment);
        Assert.Empty(f.Editor.Paste(Caret(f.Two, 1), fragment, _ => true).Changes);
        Assert.Empty(f.Editor.Paste(Sel(f.Two, 0, f.Two, 3), fragment, _ => true).Changes);
        Assert.Equal(FieldRefusalKind.PartOfToc, f.Editor.LastFieldRefusal?.Kind);
        AssertUntouched(f, revision);
    }

    [Fact]
    public void APartialCutOrDeleteIsRefused()
    {
        var f = Make();
        var revision = f.Document.Edits.Revision;

        var delete = f.Editor.Delete(Sel(f.Two, 0, f.Two, 2));
        Assert.Empty(delete.Changes);
        Assert.Equal(FieldRefusalKind.PartOfToc, f.Editor.LastFieldRefusal?.Kind);
        Assert.Empty(f.Editor.Cut(Sel(f.Two, 0, f.Two, 2)).Result.Changes);
        Assert.Empty(f.Editor.Delete(Sel(f.Two, 1, f.After, 2)).Changes);
        Assert.Empty(f.Editor.Delete(Sel(f.Intro, 2, f.Two, 1)).Changes);
        Assert.Null(f.Editor.Backspace(At(f.Two, 2), DeleteUnit.Cluster));
        Assert.Equal(FieldRefusalKind.PartOfToc, f.Editor.LastFieldRefusal?.Kind);
        Assert.Null(f.Editor.DeleteForward(At(f.Two, 1), DeleteUnit.Cluster));
        Assert.Null(f.Editor.Backspace(At(f.One, 0), DeleteUnit.Cluster));
        AssertUntouched(f, revision);
    }

    [Fact]
    public void ARangeCoveringTheWholeTocDeletesItInOneStep()
    {
        var f = Make();
        var revision = f.Document.Edits.Revision;

        var result = f.Editor.Delete(Sel(f.One, 0, f.Three, 5));
        Assert.Single(result.Changes);
        Assert.Equal(revision + 1, f.Document.Edits.Revision);
        Assert.Equal(["Intro", string.Empty, "After"], Texts(f.Document));
        Assert.Equal(0, FieldChars(f.Document));
        Assert.Null(f.Editor.LastFieldRefusal);

        var undone = f.Editor.History.Undo();
        Assert.NotNull(undone);
        Assert.Equal(s_original, Texts(f.Document));
        Assert.Equal(3, FieldChars(f.Document));
        Assert.False(f.Editor.History.CanUndo);

        Assert.NotNull(f.Editor.History.Redo());
        Assert.Equal(0, FieldChars(f.Document));

        // A range that reaches past both ends takes the field with the rest.
        Assert.NotNull(f.Editor.History.Undo());
        var wide = f.Editor.Delete(Sel(f.Intro, 2, f.After, 2));
        Assert.Single(wide.Changes);
        Assert.Equal(["Inter"], Texts(f.Document));
        Assert.Equal(0, FieldChars(f.Document));
    }

    [Fact]
    public void BackspaceNextToATocSelectsItFirst()
    {
        var f = Make();
        var revision = f.Document.Edits.Revision;

        var fromAfter = f.Editor.Backspace(At(f.After, 0), DeleteUnit.Cluster);
        Assert.NotNull(fromAfter);
        Assert.Empty(fromAfter.Changes);
        Assert.Equal(new TextRange(At(f.One, 0), At(f.Three, 5)), fromAfter.Selected);
        AssertUntouched(f, revision);

        var fromEnd = f.Editor.Backspace(At(f.Three, 5), DeleteUnit.Word);
        Assert.Equal(fromAfter.Selected, fromEnd?.Selected);
        var forward = f.Editor.DeleteForward(At(f.Intro, 5), DeleteUnit.Cluster);
        Assert.Equal(fromAfter.Selected, forward?.Selected);
        var fromStart = f.Editor.DeleteForward(At(f.One, 0), DeleteUnit.Cluster);
        Assert.Equal(fromAfter.Selected, fromStart?.Selected);
        AssertUntouched(f, revision);

        // The caller deletes the selection next: the field goes in one step.
        var deleted = f.Editor.Delete(fromAfter.Selected!.Value);
        Assert.Single(deleted.Changes);
        Assert.Equal(0, FieldChars(f.Document));
    }

    [Fact]
    public void ACaretRestsOnlyAtTheEdgesOfTheField()
    {
        var f = Make();

        Assert.Equal(At(f.One, 0), f.Editor.SnapCaret(At(f.One, 0)));
        Assert.Equal(At(f.Three, 5), f.Editor.SnapCaret(At(f.Three, 5)));
        Assert.Equal(At(f.One, 0), f.Editor.SnapCaret(At(f.One, 2)));
        Assert.Equal(At(f.Three, 5), f.Editor.SnapCaret(At(f.Three, 1)));
        Assert.Equal(At(f.Three, 5), f.Editor.SnapCaret(At(f.Two, 1)));
        Assert.Equal(At(f.Intro, 2), f.Editor.SnapCaret(At(f.Intro, 2)));
        Assert.Equal(At(f.After, 0), f.Editor.SnapCaret(At(f.After, 0)));
    }
}
