using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Editing;

public sealed class EditRulesTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const char ObjectChar = ParagraphProjection.ObjectChar;

    private static Run R(params RunContent[] content)
    {
        var run = new Run();
        foreach (var c in content)
        {
            run.Content.Add(c);
        }

        return run;
    }

    private static Run R(string text) => R(new TextContent(text));

    private static Paragraph P(params Inline[] inlines)
    {
        var paragraph = new Paragraph();
        foreach (var inline in inlines)
        {
            paragraph.Inlines.Add(inline);
        }

        return paragraph;
    }

    private static WordDocument Doc(params Block[] blocks)
    {
        var story = new Story(new OoxmlRoot(W + "document", []));
        foreach (var block in blocks)
        {
            story.Blocks.Add(block);
        }

        return new WordDocument(story);
    }

    private static string Text(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static TextPosition At(Paragraph paragraph, int offset) => new(StoryRef.Body, paragraph, offset);

    private static TextRange Caret(Paragraph paragraph, int offset) => new(At(paragraph, offset), At(paragraph, offset));

    private static TextRange Sel(Paragraph paragraph, int start, int end) => new(At(paragraph, start), At(paragraph, end));

    private static Paragraph WithField(string before, string result, string after)
    {
        var p = new Paragraph();
        p.Inlines.Add(R(before));
        p.Inlines.Add(R(new FieldChar(FieldCharKind.Begin)));
        p.Inlines.Add(R(new InstructionText(" PAGE ")));
        p.Inlines.Add(R(new FieldChar(FieldCharKind.Separate)));
        p.Inlines.Add(R(result));
        p.Inlines.Add(R(new FieldChar(FieldCharKind.End)));
        p.Inlines.Add(R(after));
        return p;
    }

    private static int FieldCount(Paragraph p) => p.Inlines.OfType<Run>().SelectMany(r => r.Content).Count(c => c is FieldChar { Kind: FieldCharKind.Begin });

    private static IEnumerable<CommentReference> CommentRefs(Paragraph p) => p.Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<CommentReference>();

    [Fact]
    public void FirstBackspaceNextToAFieldSelectsItSecondRemovesIt()
    {
        var p = WithField("ab", "123", "cd");
        var document = Doc(p);
        var editor = Editor(document);
        var revision = document.Edits.Revision;

        var first = editor.Backspace(At(p, 5), DeleteUnit.Cluster);

        Assert.NotNull(first);
        Assert.Empty(first.Changes);
        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(At(p, 5), first.Caret);
        Assert.Equal(new TextRange(At(p, 2), At(p, 5)), first.Selected);
        Assert.Equal(1, FieldCount(p));

        var second = editor.Delete(first.Selected!.Value);

        Assert.Null(second.Selected);
        Assert.Equal("abcd", Text(p));
        Assert.Equal(0, FieldCount(p));
        Assert.Equal(At(p, 2), second.Caret);
    }

    [Fact]
    public void FirstDeleteNextToAFieldSelectsItSecondRemovesIt()
    {
        var p = WithField("ab", "123", "cd");
        var document = Doc(p);
        var editor = Editor(document);
        var revision = document.Edits.Revision;

        var first = editor.DeleteForward(At(p, 2), DeleteUnit.Cluster);

        Assert.NotNull(first);
        Assert.Empty(first.Changes);
        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(new TextRange(At(p, 2), At(p, 5)), first.Selected);
        Assert.Equal(1, FieldCount(p));

        var second = editor.Delete(first.Selected!.Value);

        Assert.Equal("abcd", Text(p));
        Assert.Equal(0, FieldCount(p));
        Assert.Equal(At(p, 2), second.Caret);
    }

    [Fact]
    public void BackspaceNextToATextCharacterDoesNotSelectAnything()
    {
        var p = WithField("ab", "123", "cd");
        var result = Editor(Doc(p)).Backspace(At(p, 6), DeleteUnit.Cluster);

        Assert.Null(result!.Selected);
        Assert.Equal("ab123d", Text(p));
    }

    // What the layout's host does: a comment reference takes no room in the text.
    private sealed class ZeroLengthReferenceHost : IProjectionHost
    {
        public string? FieldValue(string instruction) => null;

        public string? NoteMarkText(RunContent reference) => null;

        public bool IsHidden(Run run) => false;

        public bool HasText(RunContent content) => content is not CommentReference;
    }

    private static TextEditor LayoutEditor(WordDocument document) => new(document, (_, _) => new ZeroLengthReferenceHost());

    [Fact]
    public void ACommentReferenceWithoutLengthSurvivesSingleAndMultiParagraphDeletes()
    {
        var comments = new CommentCollection(new OoxmlRoot(W + "comments", []));
        var p = P(R("Hello"), new CommentRangeStart(1), R("big"), new CommentRangeEnd(1), R(new CommentReference(1)), R("World"));
        var document = Doc(p);
        document.Comments = comments;

        LayoutEditor(document).Delete(Sel(p, 2, 10));

        Assert.Single(CommentRefs(p));
        Assert.Single(p.Inlines.OfType<CommentRangeStart>());
        Assert.Single(p.Inlines.OfType<CommentRangeEnd>());
        Assert.Same(comments, document.Comments);

        var a = P(R("Hello"), new CommentRangeStart(2), R("big"));
        var b = P(new CommentRangeEnd(2), R(new CommentReference(2)), R("World"));
        var c = P(R("tail"));
        var across = Doc(a, b, c);
        across.Comments = comments;

        LayoutEditor(across).Delete(new TextRange(At(a, 2), At(c, 2)));

        var merged = Assert.IsType<Paragraph>(Assert.Single(across.Body.Blocks));
        Assert.Single(CommentRefs(merged));
        Assert.Single(merged.Inlines.OfType<CommentRangeStart>());
        Assert.Single(merged.Inlines.OfType<CommentRangeEnd>());
        Assert.Same(comments, across.Comments);
    }

    [Fact]
    public void FirstWordBackspaceAndWordDeleteNextToAFieldSelectIt()
    {
        var p = WithField("ab ", "123", " cd");
        var document = Doc(p);
        var editor = Editor(document);
        var revision = document.Edits.Revision;

        var back = editor.Backspace(At(p, 6), DeleteUnit.Word);
        Assert.Empty(back!.Changes);
        Assert.Equal(new TextRange(At(p, 3), At(p, 6)), back.Selected);

        var forward = editor.DeleteForward(At(p, 3), DeleteUnit.Word);
        Assert.Empty(forward!.Changes);
        Assert.Equal(new TextRange(At(p, 3), At(p, 6)), forward.Selected);

        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(1, FieldCount(p));
        Assert.Equal("ab 123 cd", Text(p));
    }

    [Fact]
    public void TypingInsideAFieldResultInsertsBesideIt()
    {
        var p = WithField("ab", "1234", "cd");
        Editor(Doc(p)).InsertText(Caret(p, 3), "x");
        Assert.Equal("abx1234cd", Text(p));

        var q = WithField("ab", "1234", "cd");
        Editor(Doc(q)).InsertText(Caret(q, 5), "y");
        Assert.Equal("ab1234ycd", Text(q));

        foreach (var paragraph in new[] { p, q })
        {
            Assert.Equal(1, FieldCount(paragraph));
            Assert.Single(paragraph.Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<TextContent>(), t => t.Text == "1234");
        }
    }

    [Fact]
    public void PictureAndNoteReferenceAreDeletedAsOneUnit()
    {
        var drawing = new XElement(W + "drawing", new XElement(XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing") + "inline"));
        var pictureRun = R(new Picture(drawing));
        var p = P(R("a"), pictureRun, R("b"));

        var pic = Editor(Doc(p)).Backspace(At(p, 2), DeleteUnit.Cluster);
        Assert.Null(pic!.Selected);
        Assert.Equal("ab", Text(p));
        Assert.DoesNotContain(p.Inlines, i => ReferenceEquals(i, pictureRun));

        var footnotes = new NoteCollection(new OoxmlRoot(W + "footnotes", []));
        var note = new Note(5, NoteKind.Normal, new Story(new OoxmlRoot(W + "footnote", [])));
        footnotes.Notes.Add(note);
        var endnotes = new NoteCollection(new OoxmlRoot(W + "endnotes", []));
        var endnote = new Note(6, NoteKind.Normal, new Story(new OoxmlRoot(W + "endnote", [])));
        endnotes.Notes.Add(endnote);

        var q = P(R("x"), R(new FootnoteReference(5)), R("y"), R(new EndnoteReference(6)), R("z"));
        var document = Doc(q);
        document.Footnotes = footnotes;
        document.Endnotes = endnotes;
        var editor = Editor(document);

        Assert.Equal("x" + ObjectChar + "y" + ObjectChar + "z", Text(q));
        var removed = editor.DeleteForward(At(q, 1), DeleteUnit.Cluster);
        Assert.Null(removed!.Selected);
        Assert.Equal("xy" + ObjectChar + "z", Text(q));
        removed = editor.Backspace(At(q, 3), DeleteUnit.Cluster);
        Assert.Null(removed!.Selected);
        Assert.Equal("xyz", Text(q));

        // The notes stay in their collections: a known, intended orphan (undo brings the reference back).
        Assert.Same(note, footnotes.Find(5));
        Assert.Same(endnote, endnotes.Find(6));
    }

    [Fact]
    public void CommentReferenceSurvivesASelectionDelete()
    {
        var p = P(R("Hello"), R(new CommentReference(1)), R("World"));

        // The plain host gives the reference one character; a delete over it must still keep it.
        Editor(Doc(p)).Delete(Sel(p, 2, 8));
        Assert.Equal("He" + ObjectChar + "rld", Text(p));
        Assert.Single(CommentRefs(p));

        var q = P(R("ab"), R(new CommentReference(2)), R("cd"));
        var result = Editor(Doc(q)).Delete(Sel(q, 2, 3));
        Assert.Empty(result.Changes);
        Assert.Single(CommentRefs(q));
        Assert.Null(Editor(Doc(q)).Backspace(At(q, 3), DeleteUnit.Cluster));
        Assert.Single(CommentRefs(q));

        // The partially deleted first and last paragraph of a range.
        var a = P(R("Hello"), R(new CommentReference(3)));
        var b = P(R(new CommentReference(4)), R("World"));
        var across = Doc(a, b);
        Editor(across).Delete(new TextRange(At(a, 2), At(b, 3)));
        var ids = across.Body.Blocks.OfType<Paragraph>().SelectMany(CommentRefs).Select(c => c.Id).Order().ToArray();
        Assert.Equal([3, 4], ids);
    }

    [Fact]
    public void CommentStartAndEndMarkersCollapseAtTheDeletionPoint()
    {
        var comments = new CommentCollection(new OoxmlRoot(W + "comments", []));
        var p = P(R("ab"), new CommentRangeStart(1), R("cdef"), new CommentRangeEnd(1), R(new CommentReference(1)), R("gh"));
        var document = Doc(p);
        document.Comments = comments;

        Editor(document).Delete(Sel(p, 1, 6));

        var kinds = p.Inlines.Select(i => i switch
        {
            CommentRangeStart => "CS",
            CommentRangeEnd => "CE",
            Run r when r.Content.OfType<CommentReference>().Any() => "CR",
            Run => "R",
            _ => "?",
        }).ToArray();
        Assert.Equal(["R", "CS", "CE", "CR", "R"], kinds);
        Assert.Same(comments, document.Comments);
    }

    [Fact]
    public void BookmarkMarkersMoveToTheNearestRemainingPosition()
    {
        var p = P(R("ab"), new BookmarkStart(1, "b"), R("cd"), new BookmarkEnd(1), R("ef"));

        Editor(Doc(p)).Delete(Sel(p, 1, 5));

        Assert.Equal("af", Text(p));
        var order = p.Inlines.Select(i => i switch { BookmarkStart => "BS", BookmarkEnd => "BE", _ => "R" }).ToArray();
        Assert.Equal(["R", "BS", "BE", "R"], order);

        var first = P(R("Hello"), new BookmarkStart(2, "c"), R(" there"));
        var last = P(R("again"), new BookmarkEnd(2), R(" end"));
        var across = Doc(first, last);

        Editor(across).Delete(new TextRange(At(first, 2), At(last, 3)));

        var merged = Assert.IsType<Paragraph>(Assert.Single(across.Body.Blocks));
        Assert.Equal("Hein end", Text(merged));
        var kinds = merged.Inlines.Where(i => i is BookmarkStart or BookmarkEnd).Select(i => i.GetType().Name).ToArray();
        Assert.Equal(["BookmarkStart", "BookmarkEnd"], kinds);
    }

    private static int RevisionCount(IList<Inline> inlines)
    {
        var count = 0;
        foreach (var inline in inlines)
        {
            if (inline is TrackedChange change)
            {
                count += 1 + RevisionCount(change.Inlines);
            }
        }

        return count;
    }

    [Fact]
    public void TypingInsideATrackedInsertionEditsItsRunAndIsNotTracked()
    {
        var change = new TrackedChange(TrackedChangeKind.Insert, new RevisionInfo(7, "A", null));
        var run = R("new");
        change.Inlines.Add(run);
        var p = P(R("old "), change);
        var editor = Editor(Doc(p));

        editor.InsertText(Caret(p, 5), "X");
        editor.InsertText(Caret(p, 7), "Y");

        Assert.Equal("old nXeYw", Text(p));
        Assert.Equal(1, RevisionCount(p.Inlines));
        Assert.Equal(2, p.Inlines.Count);
        Assert.Same(change, p.Inlines[1]);
        Assert.Equal(7, change.Info.Id);
        Assert.Same(run, Assert.Single(change.Inlines));
        Assert.Equal("nXeYw", ((TextContent)Assert.Single(run.Content)).Text);
    }

    [Fact]
    public void ContentControlKeepsItsElementWhenItsTextIsEdited()
    {
        var control = new ContentControlInline();
        control.Properties.Set(W + "alias", new XElement(W + "alias", new XAttribute(W + "val", "Name")));
        control.Inlines.Add(R("abcd"));
        var p = P(R("x"), control, R("y"));
        var editor = Editor(Doc(p));

        editor.InsertText(Caret(p, 3), "Z");
        Assert.Equal("xabZcdy", Text(p));
        Assert.Same(control, p.Inlines[1]);

        editor.Delete(Sel(p, 1, 6));
        Assert.Equal("xy", Text(p));
        Assert.Equal(3, p.Inlines.Count);
        Assert.Same(control, p.Inlines[1]);
        Assert.Empty(control.Inlines);
        Assert.NotNull(control.Properties.Get(W + "alias"));
    }

    [Fact]
    public void UnknownContentStaysInPlaceAndShared()
    {
        var inlineContent = new UnknownContent(new XElement(W + "smartTag"));
        var runContent = new UnknownContent(new XElement(W + "sym"));
        var p = P(R("abc"), new UnknownInline(inlineContent), R(new TextContent("de"), new UnknownRunContent(runContent), new TextContent("fg")));
        var document = Doc(p);
        var editor = Editor(document);

        editor.InsertText(Caret(p, 1), "X");
        editor.Delete(Sel(p, 0, 1));

        Assert.Same(inlineContent, p.Inlines.OfType<UnknownInline>().Single().Content);
        Assert.Same(runContent, p.Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<UnknownRunContent>().Single().Content);

        // A split of the paragraph that holds them: the content is shared, not copied.
        editor.SplitParagraph(Caret(p, 5));
        var all = document.Body.Blocks.OfType<Paragraph>().ToArray();
        Assert.Equal(2, all.Length);
        var inlines = all.SelectMany(x => x.Inlines).OfType<UnknownInline>().ToArray();
        var contents = all.SelectMany(x => x.Inlines.OfType<Run>()).SelectMany(r => r.Content).OfType<UnknownRunContent>().ToArray();
        Assert.Single(inlines);
        Assert.Single(contents);
        Assert.Same(inlineContent, inlines[0].Content);
        Assert.Same(runContent, contents[0].Content);
    }
}
