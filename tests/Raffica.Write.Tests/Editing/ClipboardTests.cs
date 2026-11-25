using System.Diagnostics;
using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Editing;

public sealed class ClipboardTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

    private static Run R(string text, bool bold = false)
    {
        var run = new Run();
        if (bold)
        {
            run.Properties.SetFlag(W + "b", true);
        }

        run.Content.Add(new TextContent(text));
        return run;
    }

    private static Paragraph P(params string[] runs)
    {
        var paragraph = new Paragraph();
        foreach (var text in runs)
        {
            paragraph.Inlines.Add(R(text));
        }

        return paragraph;
    }

    private static void Set(Paragraph paragraph, string name, string value) =>
        paragraph.Properties.Set(W + name, new XElement(W + name, new XAttribute(W + "val", value)));

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

    private static string[] Texts(WordDocument document) => document.Body.Blocks.OfType<Paragraph>().Select(Text).ToArray();

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain) { FragmentCodec = new DocxFragmentCodec() };

    private static TextPosition At(Paragraph paragraph, int offset) => new(StoryRef.Body, paragraph, offset);

    private static TextRange Caret(Paragraph paragraph, int offset) => new(At(paragraph, offset), At(paragraph, offset));

    private static TextRange Sel(Paragraph paragraph, int start, int end) => new(At(paragraph, start), At(paragraph, end));

    private static TextRange Sel(Paragraph first, int start, Paragraph second, int end) => new(At(first, start), At(second, end));

    private static bool Any<T>(Paragraph paragraph)
        where T : RunContent => paragraph.Inlines.OfType<Run>().Any(r => r.Content.OfType<T>().Any());

    private static Table Table(params string[][] rows)
    {
        var table = new Table();
        foreach (var cells in rows)
        {
            var row = new TableRow();
            foreach (var text in cells)
            {
                var cell = new TableCell();
                cell.Blocks.Add(P(text));
                row.Items.Add(cell);
            }

            table.Rows.Add(row);
        }

        return table;
    }

    private static Picture MakePicture() => new(new XElement(
        W + "drawing",
        new XElement(Wp + "inline", new XElement(Wp + "extent", new XAttribute("cx", "100"), new XAttribute("cy", "100")))));

    [Fact]
    public void CopyPasteKeepsRunAndParagraphFormatting()
    {
        var source = new Paragraph();
        Set(source, "jc", "center");
        source.Properties.Set(W + "numPr", new XElement(W + "numPr", new XElement(W + "ilvl", new XAttribute(W + "val", "0")), new XElement(W + "numId", new XAttribute(W + "val", "3"))));
        source.UnknownAttributes.Add(new XAttribute(W14 + "paraId", "11111111"));
        source.Inlines.Add(R("Hello ", bold: true));
        source.Inlines.Add(R("world"));
        var second = P("Second");
        Set(second, "keepNext", "1");
        var target = P("Target");
        var document = Doc(source, second, target);
        var editor = Editor(document);

        var content = editor.Copy(Sel(source, 0, second, 6));
        Assert.NotNull(content.Fragment);
        Assert.Equal("Hello world\nSecond", content.PlainText);
        var revision = document.Edits.Revision;
        var result = editor.Paste(Caret(target, 6), content, _ => true);

        Assert.Equal(document.Edits.Revision, revision + 1);
        Assert.Equal(["Hello world", "Second", "TargetHello world", "Second"], Texts(document));
        var blocks = document.Body.Blocks.OfType<Paragraph>().ToList();
        var pasted = blocks[3];
        Assert.Equal("keepNext", pasted.Properties.Children.Single().Name.LocalName);
        Assert.Equal("Second", Text(pasted));
        // The first pasted paragraph joins the target paragraph and keeps its run formatting.
        var joined = blocks[2];
        Assert.Equal(3, joined.Inlines.Count);
        Assert.True(Assert.IsType<Run>(joined.Inlines[1]).Properties.Bold);
        Assert.False(Assert.IsType<Run>(joined.Inlines[2]).Properties.Bold);
        Assert.Null(joined.Properties.Get(W + "jc"));
        Assert.Equal(At(pasted, 6).Offset, result.Caret.Offset);
        Assert.Same(pasted, result.Caret.Paragraph);
        Assert.Single(result.Changes);
        Assert.Equal(1, editor.History.UndoCount);
        editor.History.Undo();
        Assert.Equal(["Hello world", "Second", "Target"], Texts(document));
        editor.History.Redo();
        Assert.Equal(["Hello world", "Second", "TargetHello world", "Second"], Texts(document));
    }

    [Fact]
    public void WholeParagraphsPasteWithTheirParagraphPropertiesAndNumberingInTheSameDocument()
    {
        var source = new Paragraph();
        Set(source, "jc", "center");
        source.Properties.Set(W + "numPr", new XElement(W + "numPr", new XElement(W + "numId", new XAttribute(W + "val", "3"))));
        source.UnknownAttributes.Add(new XAttribute(W14 + "paraId", "11111111"));
        source.UnknownAttributes.Add(new XAttribute(W14 + "textId", "22222222"));
        source.Inlines.Add(R("Centered"));
        var tail = P("tail");
        var empty = P();
        var document = Doc(source, tail, empty);
        var editor = Editor(document);

        var content = editor.Copy(Sel(source, 0, tail, 4));
        editor.Paste(Caret(empty, 0), content, _ => true);

        var blocks = document.Body.Blocks.OfType<Paragraph>().ToList();
        Assert.Equal(["Centered", "tail", "Centered", "tail"], Texts(document));
        // The empty target takes the properties of the first pasted paragraph (nothing before the caret).
        var adopted = blocks[2];
        Assert.Equal("center", (string?)adopted.Properties.Get(W + "jc")?.Attribute(W + "val"));
        Assert.Equal(3, adopted.Properties.NumberingId);
        Assert.DoesNotContain(adopted.UnknownAttributes, a => a.Name.LocalName is "paraId" or "textId");
        Assert.DoesNotContain(blocks[3].UnknownAttributes, a => a.Name.LocalName is "paraId" or "textId");
    }

    [Fact]
    public void PasteIntoAnotherDocumentMapsOnlyExistingStyles()
    {
        var source = new Paragraph();
        Set(source, "pStyle", "Heading1");
        source.Properties.Set(W + "numPr", new XElement(W + "numPr", new XElement(W + "numId", new XAttribute(W + "val", "9"))));
        var styled = R("Styled", bold: true);
        styled.Properties.StyleId = "Emphasis";
        var known = R("Known");
        known.Properties.StyleId = "Strong";
        source.Inlines.Add(styled);
        source.Inlines.Add(known);
        var second = P("Plain");
        Set(second, "pStyle", "NoSuchStyleThere");
        var sourceDocument = Doc(source, second);
        var content = Editor(sourceDocument).Copy(Sel(source, 0, second, 5));

        var target = P("T");
        var targetDocument = Doc(target);
        var editor = Editor(targetDocument);
        editor.Paste(Caret(target, 1), content, id => id is "Heading1" or "Strong");

        var blocks = targetDocument.Body.Blocks.OfType<Paragraph>().ToList();
        Assert.Equal(["TStyledKnown", "Plain"], Texts(targetDocument));
        // The first pasted paragraph merges into the target; nothing followed the caret, so the last keeps its own properties (minus the style the target lacks).
        var runs = blocks[0].Inlines.OfType<Run>().ToList();
        Assert.True(runs[1].Properties.Bold);
        Assert.Null(runs[1].Properties.StyleId);
        Assert.Equal("Strong", runs[2].Properties.StyleId);
        Assert.Null(blocks[1].Properties.StyleId);
    }

    [Fact]
    public void AMiddleParagraphOfAnotherDocumentKeepsAMappedStyleAndLosesNumbering()
    {
        var a = P("a");
        var middle = P("middle");
        Set(middle, "pStyle", "Heading1");
        middle.Properties.Set(W + "numPr", new XElement(W + "numPr", new XElement(W + "numId", new XAttribute(W + "val", "9"))));
        Set(middle, "jc", "right");
        var z = P("z");
        var content = Editor(Doc(a, middle, z)).Copy(Sel(a, 0, z, 1));

        var target = P("xy");
        var document = Doc(target);
        Editor(document).Paste(Caret(target, 1), content, id => id == "Heading1");

        var blocks = document.Body.Blocks.OfType<Paragraph>().ToList();
        Assert.Equal(["xa", "middle", "zy"], Texts(document));
        Assert.Equal("Heading1", blocks[1].Properties.StyleId);
        Assert.Equal("right", (string?)blocks[1].Properties.Get(W + "jc")?.Attribute(W + "val"));
        Assert.Null(blocks[1].Properties.NumberingId);
    }

    [Fact]
    public void FragmentWithTableAndPictureAndFieldPastesAsText()
    {
        var first = P("A");
        var table = Table(["x", "y"], ["z"]);
        var rich = new Paragraph();
        rich.Inlines.Add(R("see "));
        var field = new SimpleField(" PAGE ");
        field.Inlines.Add(R("7"));
        rich.Inlines.Add(field);
        var pictureRun = new Run();
        pictureRun.Content.Add(MakePicture());
        pictureRun.Content.Add(new FootnoteReference(2));
        pictureRun.Content.Add(new CommentReference(5));
        rich.Inlines.Add(pictureRun);
        var complex = new Run();
        complex.Content.Add(new FieldChar(FieldCharKind.Begin));
        complex.Content.Add(new InstructionText(" NUMPAGES "));
        complex.Content.Add(new FieldChar(FieldCharKind.Separate));
        complex.Content.Add(new TextContent("42"));
        complex.Content.Add(new FieldChar(FieldCharKind.End));
        rich.Inlines.Add(complex);
        rich.Inlines.Add(R("!"));
        var sourceDocument = Doc(first, table, rich);
        var content = Editor(sourceDocument).Copy(Sel(first, 0, rich, 100));
        Assert.Equal("A\nx\ty\nz\nsee 7￼￼42!".Replace("￼", string.Empty), content.PlainText.Replace("￼", string.Empty));
        Assert.DoesNotContain('￼', content.PlainText);

        var target = P(">");
        var targetDocument = Doc(target);
        Editor(targetDocument).Paste(Caret(target, 1), content, _ => true);

        Assert.Equal([">A", "x\ty", "z", "see 742!"], Texts(targetDocument));
        foreach (var pasted in targetDocument.Body.Blocks.OfType<Paragraph>())
        {
            Assert.False(Any<Picture>(pasted));
            Assert.False(Any<FootnoteReference>(pasted));
            Assert.False(Any<CommentReference>(pasted));
            Assert.False(Any<FieldChar>(pasted));
            Assert.False(Any<InstructionText>(pasted));
            Assert.DoesNotContain(pasted.Inlines, i => i is SimpleField);
        }

        Assert.DoesNotContain(targetDocument.Body.Blocks, b => b is Table);
    }

    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static Hyperlink Link(string text)
    {
        var link = new Hyperlink { RelationshipId = "rId9" };
        link.Inlines.Add(R(text));
        return link;
    }

    private static (WordDocument Document, Paragraph InNote, Paragraph InBody, StoryRef Note) NoteAndBody(Inline? inNote)
    {
        var inBody = P("body ");
        var noteParagraph = P("note ");
        if (inNote is not null)
        {
            noteParagraph.Inlines.Add(inNote);
        }

        var noteStory = new Story(new OoxmlRoot(W + "footnote", []));
        noteStory.Blocks.Add(noteParagraph);
        var document = Doc(inBody, P("body two"));
        document.Footnotes = new NoteCollection(new OoxmlRoot(W + "footnotes", []));
        document.Footnotes.Notes.Add(new Note(5, NoteKind.Normal, noteStory));
        return (document, noteParagraph, inBody, new StoryRef(StoryKind.Note, "5"));
    }

    [Fact]
    public void ALinkCopiedFromANoteIsPlainTextInTheBody()
    {
        var (document, inNote, inBody, note) = NoteAndBody(Link("click"));
        var editor = Editor(document);
        var content = editor.Copy(new TextRange(new TextPosition(note, inNote, 0), new TextPosition(note, inNote, 10)));

        editor.Paste(Caret(inBody, 5), content, _ => true);

        Assert.Equal("body note click", Text(inBody));
        Assert.DoesNotContain(inBody.Inlines, i => i is Hyperlink);
    }

    [Fact]
    public void ALinkCopiedFromTheBodyIsPlainTextInANote()
    {
        var (document, inNote, inBody, note) = NoteAndBody(null);
        inBody.Inlines.Add(Link("click"));
        var editor = Editor(document);
        var content = editor.Copy(Sel(inBody, 0, 10));

        editor.Paste(new TextRange(new TextPosition(note, inNote, 5), new TextPosition(note, inNote, 5)), content, _ => true);

        Assert.Equal("note body click", Text(inNote));
        Assert.DoesNotContain(inNote.Inlines, i => i is Hyperlink);
    }

    [Fact]
    public void ALinkPastedInTheSameStoryStaysALink()
    {
        var (document, _, inBody, _) = NoteAndBody(null);
        inBody.Inlines.Add(Link("click"));
        var editor = Editor(document);
        var content = editor.Copy(Sel(inBody, 5, 10));

        editor.Paste(Caret(inBody, 0), content, _ => true);

        Assert.Equal("clickbody click", Text(inBody));
        Assert.Equal("rId9", Assert.IsType<Hyperlink>(inBody.Inlines[0]).RelationshipId);
    }

    [Fact]
    public void ARelationshipBearingUnknownInlineIsDroppedAcrossStoriesAndKeptWithinOne()
    {
        var unknown = new UnknownInline(new UnknownContent(new XElement(W + "subDoc", new XAttribute(Rel + "id", "rId7"))));
        var (document, inNote, inBody, note) = NoteAndBody(unknown);
        var editor = Editor(document);
        var range = new TextRange(new TextPosition(note, inNote, 0), new TextPosition(note, inNote, 5));
        var content = editor.Copy(range);

        editor.Paste(Caret(inBody, 5), content, _ => true);
        Assert.DoesNotContain(inBody.Inlines, i => i is UnknownInline);

        editor.Paste(new TextRange(new TextPosition(note, inNote, 0), new TextPosition(note, inNote, 0)), content, _ => true);
        Assert.Contains(inNote.Inlines, i => i is UnknownInline);
    }

    [Fact]
    public void ARelationshipBearingUnknownRunContentIsDroppedFromAnotherDocument()
    {
        var run = new Run();
        run.Content.Add(new TextContent("v"));
        run.Content.Add(new UnknownRunContent(new UnknownContent(new XElement(W + "ruby", new XElement(W + "inner", new XAttribute(Rel + "id", "rId3"))))));
        run.Content.Add(new UnknownRunContent(new UnknownContent(new XElement(W + "sym", new XAttribute(W + "char", "F028")))));
        var source = new Paragraph();
        source.Inlines.Add(run);
        var content = Editor(Doc(source)).Copy(Sel(source, 0, 1));
        var target = P("[]");
        var document = Doc(target);

        Editor(document).Paste(Caret(target, 1), content, _ => true);

        var contents = target.Inlines.OfType<Run>().SelectMany(r => r.Content).ToList();
        Assert.DoesNotContain(contents, c => c is UnknownRunContent { Content.Element.Name.LocalName: "ruby" });
        Assert.Contains(contents, c => c is UnknownRunContent { Content.Element.Name.LocalName: "sym" });
    }


    [Fact]
    public void APasteCountsTheDrawingsItLeftOutSoTheCallerCanSaySo()
    {
        var pictureRun = new Run();
        pictureRun.Content.Add(MakePicture());
        var source = new Paragraph();
        source.Inlines.Add(R("with "));
        source.Inlines.Add(pictureRun);
        var plain = P("plain");
        var target = P("target");
        var document = Doc(source, plain, target);
        var editor = Editor(document);
        var withPictures = editor.Copy(Sel(source, 0, 6));
        var withoutPictures = editor.Copy(Sel(plain, 0, 5));
        Assert.Equal(0, editor.LastPasteDroppedPictures);

        // Text and a picture: the text goes in and the picture is counted.
        editor.Paste(Caret(target, 6), withPictures, _ => true);
        Assert.Equal(1, editor.LastPasteDroppedPictures);
        Assert.Equal("targetwith ", Text(target));

        // Only pictures: nothing changes and they are counted all the same.
        var pictureOnly = new Paragraph();
        pictureOnly.Inlines.Add(pictureRun);
        var lone = Doc(pictureOnly, P("x"));
        var loneEditor = Editor(lone);
        var content = loneEditor.Copy(Sel(pictureOnly, 0, 1));
        var revision = lone.Edits.Revision;
        var result = loneEditor.Paste(Caret((Paragraph)lone.Body.Blocks[1], 0), content, _ => true);
        Assert.Empty(result.Changes);
        Assert.Equal(revision, lone.Edits.Revision);
        Assert.Equal(1, loneEditor.LastPasteDroppedPictures);

        // The count belongs to the last paste: one without pictures, or one of plain text, resets it.
        editor.Paste(Caret(target, 0), withoutPictures, _ => true);
        Assert.Equal(0, editor.LastPasteDroppedPictures);
    }

    [Fact]
    public void TheCleaningOfAPasteCountsPicturesAndOldStyleDrawingsAndNothingElse()
    {
        var run = new Run();
        run.Content.Add(new TextContent("text"));
        run.Content.Add(MakePicture());
        run.Content.Add(new UnknownRunContent(new UnknownContent(new XElement(W + "pict"))));
        run.Content.Add(new UnknownRunContent(new UnknownContent(new XElement(W + "object"))));
        run.Content.Add(new FootnoteReference(3));
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(run);

        Assert.Equal(3, FragmentCleaner.ForPaste(paragraph, sameDocument: true, sameRelationships: true, _ => true));
        Assert.Equal("text", Text(paragraph));
        Assert.Equal(0, FragmentCleaner.ForPaste(P("plain"), sameDocument: true, sameRelationships: true, _ => true));
    }
    [Fact]
    public void AFragmentThatFlattensToNothingLeavesTheSelectionAlone()
    {
        var pictureRun = new Run();
        pictureRun.Content.Add(MakePicture());
        var source = new Paragraph();
        source.Inlines.Add(pictureRun);
        var target = P("keep me");
        var document = Doc(source, target);
        var editor = Editor(document);
        var content = editor.Copy(Sel(source, 0, 1));
        Assert.NotNull(content.Fragment);
        var revision = document.Edits.Revision;

        var result = editor.Paste(Sel(target, 0, 4), content, _ => true);

        Assert.Empty(result.Changes);
        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(0, editor.History.UndoCount);
        Assert.Equal("keep me", Text(target));
    }

    [Fact]
    public void CopyLeavesNoCommentBookmarkOrTrackedDeletionInTheFragment()
    {
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new BookmarkStart(1, "mark"));
        paragraph.Inlines.Add(new CommentRangeStart(4));
        paragraph.Inlines.Add(R("kept "));
        var deleted = new TrackedChange(TrackedChangeKind.Delete, new RevisionInfo(7, "A", null));
        var deletedRun = new Run();
        deletedRun.Content.Add(new DeletedText("gone "));
        deleted.Inlines.Add(deletedRun);
        paragraph.Inlines.Add(deleted);
        var inserted = new TrackedChange(TrackedChangeKind.Insert, new RevisionInfo(8, "A", null));
        inserted.Inlines.Add(R("added"));
        paragraph.Inlines.Add(inserted);
        paragraph.Inlines.Add(new CommentRangeEnd(4));
        var reference = new Run();
        reference.Content.Add(new CommentReference(4));
        paragraph.Inlines.Add(reference);
        paragraph.Inlines.Add(new BookmarkEnd(1));
        var document = Doc(paragraph);
        var editor = Editor(document);

        var content = editor.Copy(Sel(paragraph, 0, 100));
        var read = new DocxFragmentCodec().Read(content.Fragment!);

        var copy = Assert.Single(read);
        Assert.Equal("kept added", Text(copy));
        Assert.DoesNotContain(copy.Inlines, i => i is BookmarkStart or BookmarkEnd or CommentRangeStart or CommentRangeEnd or TrackedChange);
        Assert.False(Any<CommentReference>(copy));
        Assert.DoesNotContain(copy.Inlines.OfType<Run>(), r => r.Content.OfType<DeletedText>().Any());
        // The document itself is untouched.
        Assert.Equal(8, paragraph.Inlines.Count);
    }

    [Fact]
    public void CopyChangesNothingAndRaisesNothing()
    {
        var paragraph = P("abc");
        var document = Doc(paragraph);
        var editor = Editor(document);
        var revision = document.Edits.Revision;

        var content = editor.Copy(Sel(paragraph, 0, 3));

        Assert.Equal("abc", content.PlainText);
        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(0, editor.History.UndoCount);
        Assert.Equal("", editor.Copy(Caret(paragraph, 1)).PlainText);
        Assert.Null(editor.Copy(Caret(paragraph, 1)).Fragment);
    }

    [Fact]
    public void CopyOfPartOfAParagraphCutsRunsAtTheEnds()
    {
        var paragraph = P("abc", "def", "ghi");
        var document = Doc(paragraph, P("x"));
        var editor = Editor(document);

        var content = editor.Copy(Sel(paragraph, 2, 7));

        Assert.Equal("cdefg", content.PlainText);
        var copy = Assert.Single(new DocxFragmentCodec().Read(content.Fragment!));
        Assert.Equal("cdefg", Text(copy));
        Assert.Equal("abcdefghi", Text(paragraph));
    }

    [Fact]
    public void CutIsOneHistoryStep()
    {
        var first = P("one ");
        var second = P("two");
        var document = Doc(first, second);
        var editor = Editor(document);
        var revision = document.Edits.Revision;

        var cut = editor.Cut(Sel(first, 2, second, 1));

        Assert.Equal("e \nt", cut.Content.PlainText);
        Assert.NotNull(cut.Content.Fragment);
        Assert.Equal(["onwo"], Texts(document));
        Assert.Equal(revision + 1, document.Edits.Revision);
        Assert.Equal(1, editor.History.UndoCount);
        Assert.True(editor.History.Undo() is not null);
        Assert.Equal(["one ", "two"], Texts(document));
        Assert.Equal(0, editor.History.UndoCount);
    }

    [Fact]
    public void CutOfNothingChangesNothing()
    {
        var paragraph = P("abc");
        var document = Doc(paragraph);
        var editor = Editor(document);

        var cut = editor.Cut(Caret(paragraph, 1));

        Assert.Empty(cut.Result.Changes);
        Assert.Equal(0, editor.History.UndoCount);
        Assert.Equal(["abc"], Texts(document));
    }

    [Fact]
    public void PasteReplacesTheSelectionInTheSameStep()
    {
        var paragraph = P("hello world");
        var other = P("XY");
        var document = Doc(paragraph, other);
        var editor = Editor(document);
        var content = editor.Copy(Sel(other, 0, 2));
        var revision = document.Edits.Revision;

        editor.Paste(Sel(paragraph, 6, 11), content, _ => true);

        Assert.Equal(["hello XY", "XY"], Texts(document));
        Assert.Equal(revision + 1, document.Edits.Revision);
        Assert.Equal(1, editor.History.UndoCount);
        editor.History.Undo();
        Assert.Equal(["hello world", "XY"], Texts(document));
    }

    [Fact]
    public void PlainTextOutsideWriteBecomesParagraphsAndTabs()
    {
        var target = P("[]");
        var document = Doc(target);
        var editor = Editor(document);

        var result = editor.Paste(Caret(target, 1), new ClipboardContent("a\r\nb\tc\nd\u0001", null), _ => true);

        Assert.Equal(["[a", "b\tc", "d]"], Texts(document));
        var middle = document.Body.Blocks.OfType<Paragraph>().ElementAt(1);
        Assert.Contains(middle.Inlines.OfType<Run>().SelectMany(r => r.Content), c => c is TabContent);
        Assert.Equal(1, result.Caret.Offset);
        Assert.Equal(1, editor.History.UndoCount);
    }

    [Fact]
    public void AnUnreadableFragmentFallsBackToThePlainText()
    {
        var target = P("[]");
        var document = Doc(target);
        var editor = Editor(document);
        var garbage = new byte[] { 1, 2, 3, 4, 5 };

        editor.Paste(Caret(target, 1), new ClipboardContent("plain", garbage), _ => true);

        Assert.Equal(["[plain]"], Texts(document));
    }

    [Fact]
    public void AHostileFragmentIsTreatedAsAbsent()
    {
        var target = P("[]");
        var document = Doc(target);
        var editor = Editor(document);
        var hostile = System.Text.Encoding.UTF8.GetBytes($"<?xml version=\"1.0\"?><!DOCTYPE d [<!ENTITY e \"boom\">]><w:document xmlns:w=\"{W.NamespaceName}\"><w:body><w:p><w:r><w:t>&e;</w:t></w:r></w:p></w:body></w:document>");

        editor.Paste(Caret(target, 1), new ClipboardContent("fallback", hostile), _ => true);

        Assert.Equal(["[fallback]"], Texts(document));
    }

    [Fact]
    public void WithoutACodecThePlainTextIsPasted()
    {
        var source = P("src");
        var target = P("[]");
        var sourceEditor = Editor(Doc(source));
        var content = sourceEditor.Copy(Sel(source, 0, 3));
        var document = Doc(target);
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain);

        editor.Paste(Caret(target, 1), content, _ => true);

        Assert.Equal(["[src]"], Texts(document));
        Assert.Null(new TextEditor(Doc(source), (_, _) => ProjectionHost.Plain).Copy(Sel(source, 0, 3)).Fragment);
    }

    [Fact]
    public void ARefusedPasteRecordsNothing()
    {
        var first = P("one");
        first.SectionBreak = new SectionProperties();
        var second = P("two");
        var third = P("three");
        var document = Doc(first, second, third);
        var editor = Editor(document);
        var content = new ClipboardContent("zzz", null);
        var revision = document.Edits.Revision;

        var result = editor.Paste(Sel(first, 1, second, 1), content, _ => true);

        Assert.Empty(result.Changes);
        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(0, editor.History.UndoCount);
        Assert.Equal(["one", "two", "three"], Texts(document));
    }

    [Fact]
    public void PasteIntoATableCellStaysInTheCell()
    {
        var inCell = P("cell");
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Blocks.Add(inCell);
        row.Items.Add(cell);
        table.Rows.Add(row);
        var body = P("body one");
        var body2 = P("body two");
        var document = Doc(body, body2, table);
        var editor = Editor(document);
        var content = editor.Copy(Sel(body, 0, body2, 8));

        var result = editor.Paste(new TextRange(new TextPosition(StoryRef.Body, inCell, 4), new TextPosition(StoryRef.Body, inCell, 4)), content, _ => true);

        Assert.Equal(2, cell.Blocks.Count);
        Assert.Equal("cellbody one", Text((Paragraph)cell.Blocks[0]));
        Assert.Equal("body two", Text((Paragraph)cell.Blocks[1]));
        Assert.Same(cell.Blocks[1], result.Caret.Paragraph);
    }

    [Fact]
    public void PastedContentIsNeverTheSameObjectsAsTheFragmentBytesAndTheClipboardCanBePastedTwice()
    {
        var source = P("ab");
        var target = P("");
        var document = Doc(source, target);
        var editor = Editor(document);
        var content = editor.Copy(Sel(source, 0, 2));

        editor.Paste(Caret(target, 0), content, _ => true);
        editor.Paste(Caret(target, 2), content, _ => true);

        Assert.Equal(["ab", "abab"], Texts(document));
        Assert.Equal(2, editor.History.UndoCount);
    }

    [Fact]
    public void FiveMegabyteFragmentlessPasteIsOneStepAndBounded()
    {
        var target = P("start end");
        var document = Doc(target);
        var editor = Editor(document);
        var line = new string('x', 99);
        var text = string.Join("\n", Enumerable.Repeat(line, 50_000));
        Assert.True(text.Length > 4_999_000);
        var revision = document.Edits.Revision;

        var timer = Stopwatch.StartNew();
        var result = editor.Paste(Caret(target, 6), new ClipboardContent(text, null), _ => true);
        timer.Stop();

        // A bound that still catches quadratic behaviour (it would take minutes) and survives a loaded CI runner (1 s flaked on macOS at 1.2 s).
        Assert.True(timer.ElapsedMilliseconds < 5000, $"took {timer.ElapsedMilliseconds} ms");
        Assert.Equal(revision + 1, document.Edits.Revision);
        Assert.Equal(1, editor.History.UndoCount);
        Assert.Equal(50_000, document.Body.Blocks.Count);
        Assert.Equal(50_000, result.Changes[0].Added);
        // The history accounts for what was inserted (two bytes a character) and stays within a small factor of it.
        Assert.InRange(editor.History.UndoBytes, text.Length, 4L * text.Length * 2);
        editor.History.Undo();
        Assert.Equal(["start end"], Texts(document));
    }
}
