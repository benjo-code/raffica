using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>Cross-references (milestone 4.5b): the targets, what each offers, the field written for each choice, the hidden bookmark and its reuse, the result at insert time against the independent count, a deleted target and the history.</summary>
public sealed class CrossReferenceEditingTests
{
    private const string Section = "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>";

    private static string Run(string text) => $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    private static string P(string inner, string? style = null) =>
        $"<w:p><w:pPr>{(style is null ? string.Empty : $"<w:pStyle w:val=\"{style}\"/>")}</w:pPr>{inner}</w:p>";

    private static string Text(string text, string? style = null) => P(Run(text), style);

    private static WordDocument Doc(string body, bool numberedHeadings = false)
    {
        var styles = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style><w:style w:type=\"paragraph\" w:styleId=\"Caption\"><w:name w:val=\"caption\"/></w:style>";
        for (var i = 1; i <= 3; i++)
        {
            var number = numberedHeadings ? "<w:numPr><w:numId w:val=\"1\"/></w:numPr>" : string.Empty;
            styles += $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:pPr>{number}<w:outlineLvl w:val=\"{i - 1}\"/></w:pPr></w:style>";
        }

        var numbering = "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";
        return LayoutModel.Document(body + Section, LayoutModel.StylesPart(styles), LayoutModel.NumberingPart(numbering));
    }

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static Paragraph Para(WordDocument document, int block) => (Paragraph)document.Body.Blocks[block];

    private static TextPosition At(WordDocument document, int block, int offset = 0) => new(StoryRef.Body, Para(document, block), offset);

    private static string TextOf(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static string Bytes(WordDocument document) => ModelSnapshot.Of(document).ToString(SaveOptions.DisableFormatting);

    private static IReadOnlyList<FieldSpan> Fields(Paragraph paragraph) => [.. FieldView.InParagraph(paragraph).Where(f => f.Parent is null)];

    private static IReadOnlyList<string> RefNames(WordDocument document) => [.. BookmarkOperations.AllNames(document).Where(n => n.StartsWith("_Ref", StringComparison.Ordinal))];

    private static CrossRefTarget Target(WordDocument document, IFieldHost host, CrossRefTargetKind kind, int index = 0) => CrossReferences.Targets(document, host, kind)[index];

    // Three figure captions (stored numbers right), a numbered heading before them and the text to refer from at the end.
    private static (WordDocument Document, TextEditor Editor, FakeFieldHost Host) ThreeFigures()
    {
        var document = Doc(Text("Introduction", "Heading1") + Text("pic one") + Text("pic two") + Text("pic three") + Text("see here"), numberedHeadings: true);
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        Assert.NotNull(editor.InsertCaption(At(document, 3), "Figure", ": three", CaptionPlace.Below, false, "Figure", host));
        Assert.NotNull(editor.InsertCaption(At(document, 2), "Figure", ": two", CaptionPlace.Below, false, "Figure", host));
        Assert.NotNull(editor.InsertCaption(At(document, 1), "Figure", ": one", CaptionPlace.Below, false, "Figure", host));
        editor.RefreshFields(host, RefreshScope.OrderBased);
        return (document, editor, host);
    }

    [Fact]
    public void LabelAndNumberOfACaptionGivesFigure3AndFollowsAMove()
    {
        var (document, editor, host) = ThreeFigures();
        var last = document.Body.Blocks.Count - 1;
        var captions = CrossReferences.Targets(document, host, CrossRefTargetKind.Caption);
        Assert.Equal(["Figure 1: one", "Figure 2: two", "Figure 3: three"], captions.Select(c => c.Display).ToArray());

        var result = editor.InsertCrossReference(At(document, last, 4), captions[2], CrossRefKind.LabelAndNumber, host);

        Assert.NotNull(result);
        var reference = Para(document, last);
        var field = Assert.Single(Fields(reference));
        Assert.Equal(FieldKind.Ref, field.Support.Kind);
        Assert.StartsWith("_Ref", field.Instruction.Argument, StringComparison.Ordinal);
        Assert.True(field.Instruction.Has("h"));
        Assert.Equal("see Figure 3here", TextOf(reference));
        Assert.Equal(TextOf(reference).IndexOf("here", StringComparison.Ordinal), result.Caret.Offset);

        // The caption (and with it the number the reference shows) moves; the refresh follows.
        var captionBlock = document.Body.Blocks.ToList().FindIndex(b => b is Paragraph p && TextOf(p).StartsWith("Figure 3", StringComparison.Ordinal));
        Assert.NotNull(editor.MoveBlocks(captionBlock, 1, 1));
        editor.RefreshFields(host, RefreshScope.OrderBased);
        ReferenceFields.AssertStoredEqualsExpected(document, host, "after the move");
        Assert.Equal("see Figure 1here", TextOf(Para(document, document.Body.Blocks.Count - 1)));
    }

    [Fact]
    public void ANumberOnlyReferenceToAHeadingGivesItsLabel()
    {
        var document = Doc(Text("Introduction", "Heading1") + Text("Background", "Heading1") + Text("see "), numberedHeadings: true);
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        var target = Target(document, host, CrossRefTargetKind.Heading, 1);
        Assert.Equal("2 Background", target.Display);

        Assert.NotNull(editor.InsertCrossReference(At(document, 2, 4), target, CrossRefKind.NumberOnly, host));

        var field = Assert.Single(Fields(Para(document, 2)));
        Assert.Equal(FieldKind.Ref, field.Support.Kind);
        Assert.True(field.Instruction.Has("n"));
        Assert.True(field.Instruction.Has("h"));
        Assert.Equal("see 2", TextOf(Para(document, 2)));
        ReferenceFields.AssertStoredEqualsExpected(document, host, "at insert time");
    }

    [Fact]
    public void TextGivesTheHeadingText()
    {
        var document = Doc(Text("Introduction", "Heading1") + Text("see "));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);

        Assert.NotNull(editor.InsertCrossReference(At(document, 1, 4), Target(document, host, CrossRefTargetKind.Heading), CrossRefKind.Text, host));

        var field = Assert.Single(Fields(Para(document, 1)));
        Assert.Equal(FieldKind.Ref, field.Support.Kind);
        Assert.False(field.Instruction.Has("n"));
        Assert.Equal("see Introduction", TextOf(Para(document, 1)));
        ReferenceFields.AssertStoredEqualsExpected(document, host, "at insert time");
    }

    [Fact]
    public void PageGivesThePrintedPageNumberWithTheSectionFormat()
    {
        var document = Doc(Text("Introduction", "Heading1") + Text("see page "));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        host.Pages[Para(document, 0)] = (3, "iv");

        Assert.NotNull(editor.InsertCrossReference(At(document, 1, 9), Target(document, host, CrossRefTargetKind.Heading), CrossRefKind.Page, host));

        var field = Assert.Single(Fields(Para(document, 1)));
        Assert.Equal(FieldKind.PageRef, field.Support.Kind);
        Assert.True(field.Instruction.Has("h"));
        Assert.Equal("see page iv", TextOf(Para(document, 1)));

        // A page the layout does not know yet shows a question mark until the page results are refreshed.
        var unknown = Doc(Text("Introduction", "Heading1") + Text("see page "));
        var unknownEditor = Editor(unknown);
        var unknownHost = new FakeFieldHost(unknown);
        Assert.NotNull(unknownEditor.InsertCrossReference(At(unknown, 1, 9), Target(unknown, unknownHost, CrossRefTargetKind.Heading), CrossRefKind.Page, unknownHost));
        Assert.Equal("see page ?", TextOf(Para(unknown, 1)));
    }

    [Fact]
    public void AFootnoteReferenceUsesNoterefAndTheMark()
    {
        var document = Doc(Text("first sentence") + Text("second sentence") + Text("see note "));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        Assert.NotNull(editor.InsertNote(At(document, 0, 5), NoteType.Footnote));
        Assert.NotNull(editor.InsertNote(At(document, 1, 6), NoteType.Footnote));
        var notes = CrossReferences.Targets(document, host, CrossRefTargetKind.Footnote);
        Assert.Equal(2, notes.Count);
        Assert.StartsWith("2", notes[1].Display, StringComparison.Ordinal);

        Assert.NotNull(editor.InsertCrossReference(At(document, 2, 9), notes[1], CrossRefKind.NumberOnly, host));

        var field = Assert.Single(Fields(Para(document, 2)));
        Assert.Equal(FieldKind.NoteRef, field.Support.Kind);
        Assert.True(field.Instruction.Has("h"));
        Assert.Equal("see note 2", TextOf(Para(document, 2)));
        ReferenceFields.AssertStoredEqualsExpected(document, host, "at insert time");

        // The bookmark stands around the reference alone.
        var mark = Assert.Single(RefNames(document));
        var range = editor.FindBookmark(mark)!.Value;
        Assert.Equal(1, range.End.Offset - range.Start.Offset);
        Assert.Same(Para(document, 1), range.Start.Paragraph);
    }

    [Fact]
    public void TheHiddenBookmarkIsReusedForTheSameTarget()
    {
        var document = Doc(Text("Introduction", "Heading1") + Text("one ") + Text("two "), numberedHeadings: true);
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        var target = Target(document, host, CrossRefTargetKind.Heading);

        Assert.NotNull(editor.InsertCrossReference(At(document, 1, 4), target, CrossRefKind.Text, host));
        Assert.NotNull(editor.InsertCrossReference(At(document, 2, 4), target, CrossRefKind.NumberOnly, host));
        Assert.NotNull(editor.InsertCrossReference(At(document, 2, 0), target, CrossRefKind.Page, host));

        // One bookmark serves the three references (text, number and page of a heading read the same range), made once.
        var name = Assert.Single(RefNames(document));
        Assert.Matches("^_Ref[0-9]{9,}$", name);
        var instructions = document.Body.Blocks.OfType<Paragraph>().SelectMany(Fields).Select(f => f.Instruction.Argument).ToList();
        Assert.Equal([name, name, name], instructions);
        Assert.Single(BookmarkOperations.AllNames(document), n => n == name);

        // A caption's label and number and its whole text are two ranges, so two bookmarks; the same choice again reuses its own.
        var (figures, figureEditor, figureHost) = ThreeFigures();
        var caption = CrossReferences.Targets(figures, figureHost, CrossRefTargetKind.Caption)[0];
        var last = figures.Body.Blocks.Count - 1;
        Assert.NotNull(figureEditor.InsertCrossReference(At(figures, last), caption, CrossRefKind.LabelAndNumber, figureHost));
        Assert.NotNull(figureEditor.InsertCrossReference(At(figures, last), caption, CrossRefKind.Page, figureHost));
        Assert.Single(RefNames(figures));
        Assert.NotNull(figureEditor.InsertCrossReference(At(figures, last), caption, CrossRefKind.Text, figureHost));
        Assert.Equal(2, RefNames(figures).Count);
        Assert.NotNull(figureEditor.InsertCrossReference(At(figures, last), caption, CrossRefKind.NumberOnly, figureHost));
        Assert.Equal(3, RefNames(figures).Count);
        Assert.Equal(RefNames(figures).Count, RefNames(figures).Distinct().Count());
    }

    [Fact]
    public void DeletingTheTargetLeavesTheStoredResultAndMarksTheField()
    {
        var document = Doc(Text("Introduction", "Heading1") + Text("see "));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        Assert.NotNull(editor.InsertCrossReference(At(document, 1, 4), Target(document, host, CrossRefTargetKind.Heading), CrossRefKind.Text, host));
        Assert.Single(RefNames(document));

        // The whole range of the hidden bookmark goes with the text; the reference keeps what it showed, and the refresh says it could not update it.
        Assert.NotNull(editor.Delete(new TextRange(At(document, 0, 0), At(document, 0, "Introduction".Length))));
        Assert.Empty(RefNames(document));
        var refresh = editor.RefreshFields(host, RefreshScope.OrderBased);

        Assert.Equal(0, refresh.Written);
        Assert.Equal(1, refresh.NotUpdated);
        Assert.Equal("see Introduction", TextOf(Para(document, 1)));
    }

    [Fact]
    public void EveryReferenceIsALinkWithH()
    {
        var (document, editor, host) = ThreeFigures();
        Assert.NotNull(editor.InsertNote(At(document, 1, 2), NoteType.Footnote));
        var last = document.Body.Blocks.Count - 1;
        CrossRefTarget[] targets =
        [
            Target(document, host, CrossRefTargetKind.Heading),
            CrossReferences.Targets(document, host, CrossRefTargetKind.Caption)[0],
            Target(document, host, CrossRefTargetKind.Footnote),
        ];
        var made = 0;
        foreach (var target in targets)
        {
            foreach (var kind in CrossReferences.Offered(target.Kind))
            {
                Assert.NotNull(editor.InsertCrossReference(At(document, last), target, kind, host));
                made++;
            }
        }

        var fields = Fields(Para(document, last)).ToList();
        Assert.Equal(made, fields.Count);
        Assert.All(fields, f => Assert.True(f.Instruction.Has("h"), f.Instruction.Name));
        Assert.Contains(fields, f => f.Support.Kind == FieldKind.Ref && f.Instruction.Has("n"));
        Assert.Contains(fields, f => f.Support.Kind == FieldKind.PageRef);
        Assert.Contains(fields, f => f.Support.Kind == FieldKind.NoteRef);
        Assert.All(fields, f => Assert.True(f.Support.IsSupported));
    }

    [Fact]
    public void ABookmarkTargetIsListedByItsNameOnly()
    {
        var document = Doc(P(Run("the quick ") + "<w:bookmarkStart w:id=\"50\" w:name=\"_Hidden1\"/>" + Run("brown") + "<w:bookmarkEnd w:id=\"50\"/>" + Run(" fox")) + Text("see "));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        Assert.NotNull(editor.AddBookmark(new TextRange(At(document, 0, 4), At(document, 0, 9)), "Quick"));

        var targets = CrossReferences.Targets(document, host, CrossRefTargetKind.Bookmark);

        // The line is the name (never the text it covers) and a name that starts with an underscore is not listed.
        var only = Assert.Single(targets);
        Assert.Equal("Quick", only.Display);
        Assert.Equal(4, only.Position.Offset);

        // Its text and its page are what a reference to it offers; its range gets a hidden bookmark of its own, the user's name never enters the instruction.
        Assert.Equal([CrossRefKind.Text, CrossRefKind.Page], CrossReferences.Offered(CrossRefTargetKind.Bookmark).ToArray());
        Assert.NotNull(editor.InsertCrossReference(At(document, 1, 4), only, CrossRefKind.Text, host));
        Assert.Equal("see quick", TextOf(Para(document, 1)));
        var field = Assert.Single(Fields(Para(document, 1)));
        Assert.NotEqual("Quick", field.Instruction.Argument);
        Assert.Matches("^_Ref[0-9]+$", field.Instruction.Argument!);
        Assert.Equal(["Quick"], editor.VisibleBookmarks().Select(b => b.Name).ToArray());
    }

    [Fact]
    public void UndoRemovesFieldAndNewBookmarkTogether()
    {
        var document = Doc(Text("Introduction", "Heading1") + Text("one ") + Text("two "));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        var target = Target(document, host, CrossRefTargetKind.Heading);
        var start = Bytes(document);

        Assert.NotNull(editor.InsertCrossReference(At(document, 2, 4), target, CrossRefKind.Text, host));
        var afterFirst = Bytes(document);
        Assert.Single(RefNames(document));
        Assert.NotNull(editor.InsertCrossReference(At(document, 1, 4), target, CrossRefKind.Page, host));
        var afterSecond = Bytes(document);

        // The second reference reused the bookmark: its undo leaves the bookmark (the first reference still needs it).
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(afterFirst, Bytes(document));
        Assert.Single(RefNames(document));

        // The first made it: its undo takes field and bookmark away in the one step.
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(start, Bytes(document));
        Assert.Empty(RefNames(document));
        Assert.False(editor.History.CanUndo);

        Assert.NotNull(editor.History.Redo());
        Assert.Equal(afterFirst, Bytes(document));
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(afterSecond, Bytes(document));
    }

    [Fact]
    public void AReferenceAcrossATableCellIsOneStepToo()
    {
        const string TableXml = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc><w:tcPr/><w:p><w:r><w:t>cell </w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        var document = Doc(Text("Introduction", "Heading1") + Text("between") + TableXml + Text("after"));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        var cell = ((Table)document.Body.Blocks[2]).Rows.OfType<TableRow>().Single().Items.OfType<TableCell>().Single();
        var inCell = new TextPosition(StoryRef.Body, (Paragraph)cell.Blocks[0], 5);
        var start = Bytes(document);

        // The target is a top-level paragraph and the caret is in a cell: the step covers the blocks from one to the other and undo restores them all.
        var result = editor.InsertCrossReference(inCell, Target(document, host, CrossRefTargetKind.Heading), CrossRefKind.Text, host);

        Assert.NotNull(result);
        Assert.Equal("cell Introduction", TextOf((Paragraph)cell.Blocks[0]));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(start, Bytes(document));
    }

    [Fact]
    public void WhatATargetOffersIsTheTableOfTheSpec()
    {
        Assert.Equal([CrossRefKind.NumberOnly, CrossRefKind.Text, CrossRefKind.Page], CrossReferences.Offered(CrossRefTargetKind.Heading).ToArray());
        Assert.Equal([CrossRefKind.LabelAndNumber, CrossRefKind.NumberOnly, CrossRefKind.Text, CrossRefKind.Page], CrossReferences.Offered(CrossRefTargetKind.Caption).ToArray());
        Assert.Equal([CrossRefKind.NumberOnly, CrossRefKind.Page], CrossReferences.Offered(CrossRefTargetKind.Footnote).ToArray());
        Assert.Equal([CrossRefKind.NumberOnly, CrossRefKind.Page], CrossReferences.Offered(CrossRefTargetKind.Endnote).ToArray());

        var anywhere = new TextPosition(StoryRef.Body, new Paragraph(), 0);
        foreach (var targetKind in Enum.GetValues<CrossRefTargetKind>())
        {
            var target = new CrossRefTarget(targetKind, anywhere, "x");
            foreach (var kind in Enum.GetValues<CrossRefKind>())
            {
                var refusal = CrossReferences.Refusal(kind, target);
                Assert.Equal(CrossReferences.Offered(targetKind).Contains(kind), refusal is null);
                Assert.True(refusal is null || refusal == CrossRefRefusal.KindNotOffered);
            }
        }

        Assert.Equal(CrossRefRefusal.KindNotOffered, CrossReferences.Refusal(CrossRefKind.Text, new CrossRefTarget(CrossRefTargetKind.Footnote, anywhere, "1")));
        Assert.Equal(CrossRefRefusal.KindNotOffered, CrossReferences.Refusal((CrossRefKind)9, new CrossRefTarget(CrossRefTargetKind.Heading, anywhere, "x")));
    }

    [Fact]
    public void ARefusalChangesNothingAndNamesItsReason()
    {
        var document = Doc(Text("Introduction", "Heading1") + Text("plain") + Text("see "));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        var heading = Target(document, host, CrossRefTargetKind.Heading);
        var start = Bytes(document);
        var revision = document.Edits.Revision;

        void Refused(EditResult? result, CrossRefRefusal expected, string label)
        {
            Assert.Null(result);
            Assert.Equal(expected, editor.LastCrossRefRefusal);
            Assert.Equal(start, Bytes(document));
            Assert.Equal(revision, document.Edits.Revision);
            Assert.False(editor.History.CanUndo, label);
        }

        // A choice the target does not offer, a prefix document, a target that is not there, a heading with no number, and a reference inside its own text.
        Refused(editor.InsertCrossReference(At(document, 2), new CrossRefTarget(CrossRefTargetKind.Footnote, heading.Position, "1"), CrossRefKind.Text, host), CrossRefRefusal.KindNotOffered, "kind");
        Refused(editor.InsertCrossReference(At(document, 2), heading, CrossRefKind.Text, host, prefixDocument: true), CrossRefRefusal.Prefix, "prefix");
        Refused(editor.InsertCrossReference(At(document, 2), new CrossRefTarget(CrossRefTargetKind.Heading, At(document, 1), "x"), CrossRefKind.Text, host), CrossRefRefusal.TargetGone, "not a heading");
        Refused(editor.InsertCrossReference(At(document, 2), new CrossRefTarget(CrossRefTargetKind.Bookmark, At(document, 1), "Nobody"), CrossRefKind.Text, host), CrossRefRefusal.TargetGone, "no bookmark");
        Refused(editor.InsertCrossReference(At(document, 2), new CrossRefTarget(CrossRefTargetKind.Footnote, At(document, 1), "1"), CrossRefKind.Page, host), CrossRefRefusal.TargetGone, "no note");
        Refused(editor.InsertCrossReference(At(document, 2), heading, CrossRefKind.NumberOnly, host), CrossRefRefusal.NoNumber, "heading without a number");
        Refused(editor.InsertCrossReference(At(document, 0, 5), heading, CrossRefKind.Text, host), CrossRefRefusal.InsideTarget, "inside its own text");
        Assert.Equal(CrossRefRefusal.Prefix, editor.RefusalForCrossReference(At(document, 2), prefixDocument: true));
        Assert.Null(editor.RefusalForCrossReference(At(document, 2)));
        Assert.Throws<ArgumentNullException>(() => editor.InsertCrossReference(At(document, 2), null!, CrossRefKind.Text, host));
    }


    [Fact]
    public void AReferenceIsRefusedInsideATableOfContentsAndInAPlainTextControl()
    {
        // A table of contents updates itself: the refusal names it and the document is as it was.
        var entries = TocDocument.Toc(nested: true, "One", "Two");
        var heading = TocDocument.Plain("Heading");
        var toc = TocDocument.Document([heading, .. entries]);
        var tocEditor = Editor(toc);
        var tocHost = new FakeFieldHost(toc);
        var dump = TocDocument.Dump(toc);
        var revision = toc.Edits.Revision;
        var elsewhere = new CrossRefTarget(CrossRefTargetKind.Heading, new TextPosition(StoryRef.Body, heading, 0), "Heading");

        Assert.Null(tocEditor.InsertCrossReference(new TextPosition(StoryRef.Body, entries[1], 1), elsewhere, CrossRefKind.Text, tocHost));

        Assert.Equal(CrossRefRefusal.InsideToc, tocEditor.LastCrossRefRefusal);
        Assert.Equal(FieldRefusalKind.InsideToc, tocEditor.LastFieldRefusal?.Kind);
        Assert.Equal(CrossRefRefusal.InsideToc, tocEditor.RefusalForCrossReference(new TextPosition(StoryRef.Body, entries[0], 0)));
        Assert.Equal(dump, TocDocument.Dump(toc));
        Assert.Equal(revision, toc.Edits.Revision);
        Assert.False(tocEditor.History.CanUndo);

        // A plain-text content control holds one run of text: no field goes in.
        var control = Doc(Text("Introduction", "Heading1") + "<w:sdt><w:sdtPr><w:text/></w:sdtPr><w:sdtContent>" + Text("typed") + "</w:sdtContent></w:sdt>");
        var controlEditor = Editor(control);
        var controlHost = new FakeFieldHost(control);
        var inControl = ((ContentControlBlock)control.Body.Blocks[1]).Content.OfType<Paragraph>().Single();
        var before = Bytes(control);

        Assert.Null(controlEditor.InsertCrossReference(new TextPosition(StoryRef.Body, inControl, 2), Target(control, controlHost, CrossRefTargetKind.Heading), CrossRefKind.Text, controlHost));

        Assert.Equal(CrossRefRefusal.RestrictedControl, controlEditor.LastCrossRefRefusal);
        Assert.Equal(before, Bytes(control));
    }

    private static string Field(string instruction, string result) =>
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
        + $"<w:r><w:instrText xml:space=\"preserve\"> {instruction} </w:instrText></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
        + (result.Length == 0 ? string.Empty : Run(result))
        + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    [Fact]
    public void AFarTargetChangesOnlyTheTwoParagraphsAndTheHistoryKeepsOnlyThose()
    {
        var body = Text("Introduction", "Heading1");
        for (var i = 0; i < 300; i++)
        {
            body += Text("filler paragraph number " + i);
        }

        body += Text("see ");
        var document = Doc(body);
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        var blocks = document.Body.Blocks.ToList();
        var last = blocks.Count - 1;
        var before = Bytes(document);

        var result = editor.InsertCrossReference(At(document, last, 4), Target(document, host, CrossRefTargetKind.Heading), CrossRefKind.Text, host);

        // The step is the paragraph with the caret; the heading that got the markers is a derived change of it; nothing between them was replaced or cloned into the history.
        Assert.NotNull(result);
        var change = Assert.Single(result.Changes);
        Assert.Equal((last, 1, 1), (change.First, change.Removed, change.Added));
        var derived = Assert.Single(result.DerivedChanges!);
        Assert.Equal((0, 1, 1), (derived.First, derived.Removed, derived.Added));
        for (var i = 1; i < last; i++)
        {
            Assert.Same(blocks[i], document.Body.Blocks[i]);
        }

        Assert.True(editor.History.UndoBytes < 40_000, "the history holds " + editor.History.UndoBytes + " bytes");

        // Undo and redo restore the bytes of both paragraphs and leave everything between them alone.
        var after = Bytes(document);
        var undone = editor.History.Undo();
        Assert.NotNull(undone);
        Assert.Equal(before, Bytes(document));
        Assert.Single(undone.DerivedChanges!);
        for (var i = 1; i < last; i++)
        {
            Assert.Same(blocks[i], document.Body.Blocks[i]);
        }

        Assert.NotNull(editor.History.Redo());
        Assert.Equal(after, Bytes(document));
        for (var i = 1; i < last; i++)
        {
            Assert.Same(blocks[i], document.Body.Blocks[i]);
        }
    }

    [Fact]
    public void ABookmarkTargetSpanningParagraphsGetsItsMarkersAsDerivedChangesToo()
    {
        var document = Doc(Text("first part") + Text("middle") + Text("second part") + Text("see "));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        host.Pages[Para(document, 0)] = (0, "7");
        Assert.NotNull(editor.AddBookmark(new TextRange(At(document, 0, 6), At(document, 2, 6)), "Span"));
        var middle = Para(document, 1);
        var before = Bytes(document);

        var result = editor.InsertCrossReference(At(document, 3, 4), Target(document, host, CrossRefTargetKind.Bookmark), CrossRefKind.Page, host);

        Assert.NotNull(result);
        Assert.Equal(2, result.DerivedChanges!.Count);
        Assert.Same(middle, document.Body.Blocks[1]);
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, Bytes(document));
        Assert.Same(middle, document.Body.Blocks[1]);
    }


    // A host that has a mark for every note id, as the layout does for a reference the text does not show (the fake host counts only the references it can see).
    private sealed class MarksEverythingHost(FakeFieldHost inner) : IFieldHost
    {
        public bool IsComplete => inner.IsComplete;

        public string? NumberLabel(Paragraph paragraph) => inner.NumberLabel(paragraph);

        public string? ChapterNumber(Paragraph paragraph) => inner.ChapterNumber(paragraph);

        public bool LabelEndsInTab(Paragraph paragraph) => inner.LabelEndsInTab(paragraph);

        public LineGeometry MeasureLine(Paragraph paragraph, string text) => inner.MeasureLine(paragraph, text);

        public (int PageIndex, string PageText)? PageOf(TextPosition position) => inner.PageOf(position);

        public string? NoteMarkText(int noteId, NoteType type) => "1";
    }

    // A projection host that hides the runs it was told to hide (what the layout does for a tracked deletion): the piece of a hidden note reference stays in the projection with length 0.
    private sealed class HidingHost(HashSet<Run> hidden) : IProjectionHost
    {
        public string? FieldValue(string instruction) => null;

        public string? NoteMarkText(RunContent reference) => null;

        public bool IsHidden(Run run) => hidden.Contains(run);
    }

    [Fact]
    public void AHiddenNoteReferenceIsNeverATargetAndAStaleOneIsRefusedWithoutAChange()
    {
        var document = Doc(Text("first sentence") + Text("see "));
        var hidden = new HashSet<Run>(ReferenceEqualityComparer.Instance);
        var editor = new TextEditor(document, (_, _) => new HidingHost(hidden));
        var host = new MarksEverythingHost(new FakeFieldHost(document));
        Assert.NotNull(editor.InsertNote(At(document, 0, 5), NoteType.Footnote));
        var listed = Target(document, host, CrossRefTargetKind.Footnote);

        // The layout now hides the reference (a tracked deletion): the plain list still holds it, but the editor sees no text for it and refuses before anything changes, whatever the host says of its mark.
        hidden.Add(Para(document, 0).Inlines.OfType<Run>().Single(r => r.Content.Any(c => c is FootnoteReference)));
        document.Edits.Raise();
        var start = Bytes(document);
        var revision = document.Edits.Revision;
        editor.History.Clear();

        foreach (var kind in new[] { CrossRefKind.NumberOnly, CrossRefKind.Page })
        {
            Assert.Null(editor.InsertCrossReference(At(document, 1, 4), listed, kind, host));
            Assert.Equal(CrossRefRefusal.TargetGone, editor.LastCrossRefRefusal);
            Assert.Equal(start, Bytes(document));
            Assert.Equal(revision, document.Edits.Revision);
            Assert.False(editor.History.CanUndo);
        }
    }

    [Fact]
    public void ATrackedDeletedNoteReferenceIsNotListed()
    {
        var document = Doc(Text("first sentence") + Text("see "));
        var editor = Editor(document);
        var host = new MarksEverythingHost(new FakeFieldHost(document));
        Assert.NotNull(editor.InsertNote(At(document, 0, 5), NoteType.Footnote));
        var paragraph = Para(document, 0);
        var run = paragraph.Inlines.OfType<Run>().Single(r => r.Content.Any(c => c is FootnoteReference));
        var deletion = new TrackedChange(TrackedChangeKind.Delete, new RevisionInfo(7, "A", null));
        deletion.Inlines.Add(run);
        paragraph.Inlines[paragraph.Inlines.IndexOf(run)] = deletion;
        document.Edits.Raise();

        Assert.Empty(CrossReferences.Targets(document, host, CrossRefTargetKind.Footnote));
    }

    [Fact]
    public void TheBookmarkOfACaptionReferenceEnclosesTheFieldsWholeAndNeverSplitsOne()
    {
        var document = Doc(Text("Chapter one", "Heading1") + Text("pic") + Text("see "), numberedHeadings: true);
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        Assert.NotNull(editor.InsertCaption(At(document, 1), "Figure", ": a", CaptionPlace.Below, chapterNumber: true, "Figure", host));
        var caption = Target(document, host, CrossRefTargetKind.Caption);
        var captionParagraph = Para(document, 2);

        Assert.NotNull(editor.InsertCrossReference(At(document, 3, 4), caption, CrossRefKind.LabelAndNumber, host));
        Assert.NotNull(editor.InsertCrossReference(At(document, 3, 4), caption, CrossRefKind.NumberOnly, host));

        // A marker is never between a field begin and its end; the start stands directly before the first run of the range and the end directly after the last field end run.
        AssertMarkersOutsideFields(captionParagraph);
        var inlines = captionParagraph.Inlines.ToList();
        var starts = inlines.Select((inline, index) => (inline, index)).Where(x => x.inline is BookmarkStart).Select(x => x.index).ToList();
        var ends = inlines.Select((inline, index) => (inline, index)).Where(x => x.inline is BookmarkEnd).Select(x => x.index).ToList();
        Assert.Equal(2, starts.Count);
        Assert.Equal(2, ends.Count);

        // The label and number: from the first run of the paragraph (the word) to the end of the SEQ field.
        var labelStart = starts.Min();
        Assert.Equal(0, labelStart);
        Assert.Equal("Figure ", TextOfRun(inlines[labelStart + 1]));
        Assert.Equal(1, ends.Max() - ends.Min());
        Assert.True(IsFieldEnd(inlines[ends.Min() - 1]), "both ranges end directly after the end run of the sequence field");

        // The number alone (chapter number included): from the begin run of the STYLEREF field to the same end.
        var numberStart = starts.Max();
        Assert.True(IsFieldBegin(inlines[numberStart + 1]), "the number starts at the begin run of the chapter field");
        Assert.True(IsFieldEnd(inlines[ends.Min() - 1]));
    }

    [Fact]
    public void ACaptionWhoseFieldsCannotBePairedStillOffersItsTextAndPageButRefusesItsNumber()
    {
        var odd = P(Run("Figure ") + Field("SEQ Figure", "1") + Run(" and ") + Field("DATE", string.Empty));
        var document = Doc(odd + Text("see "));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        var caption = Target(document, host, CrossRefTargetKind.Caption);
        var start = Bytes(document);

        Assert.Null(editor.InsertCrossReference(At(document, 1, 4), caption, CrossRefKind.LabelAndNumber, host));
        Assert.Equal(CrossRefRefusal.CaptionNumberNotRead, editor.LastCrossRefRefusal);
        Assert.Null(editor.InsertCrossReference(At(document, 1, 4), caption, CrossRefKind.NumberOnly, host));
        Assert.Equal(CrossRefRefusal.CaptionNumberNotRead, editor.LastCrossRefRefusal);
        Assert.Equal(start, Bytes(document));

        Assert.NotNull(editor.InsertCrossReference(At(document, 1, 4), caption, CrossRefKind.Text, host));
        Assert.NotNull(editor.InsertCrossReference(At(document, 1, 0), caption, CrossRefKind.Page, host));
        Assert.Single(RefNames(document));
    }

    [Fact]
    public void AHeadingOrCaptionWithNothingToShowIsNotListed()
    {
        var document = Doc(Text(string.Empty, "Heading1") + Text("Real", "Heading1") + P(Field("SEQ Figure", string.Empty)) + P(Run("Figure ") + Field("SEQ Figure", "1")));
        var host = new FakeFieldHost(document);

        Assert.Equal(["Real"], CrossReferences.Targets(document, host, CrossRefTargetKind.Heading).Select(t => t.Display).ToArray());
        Assert.Equal(["Figure 1"], CrossReferences.Targets(document, host, CrossRefTargetKind.Caption).Select(t => t.Display).ToArray());
    }

    private static bool IsFieldBegin(Inline inline) => inline is Run { Content: [FieldChar { Kind: FieldCharKind.Begin }] };

    private static bool IsFieldEnd(Inline inline) => inline is Run { Content: [FieldChar { Kind: FieldCharKind.End }] };

    private static string TextOfRun(Inline inline) => inline is Run run ? string.Concat(run.Content.OfType<TextContent>().Select(t => t.Text)) : string.Empty;

    private static void AssertMarkersOutsideFields(Paragraph paragraph)
    {
        var depth = 0;
        foreach (var inline in paragraph.Inlines)
        {
            if (IsFieldBegin(inline))
            {
                depth++;
            }
            else if (IsFieldEnd(inline))
            {
                depth--;
            }
            else if (inline is BookmarkStart or BookmarkEnd)
            {
                Assert.Equal(0, depth);
            }
        }

        Assert.Equal(0, depth);
    }
    [Fact]
    public void ASpanningBookmarkOffersItsPageButNotItsText()
    {
        var document = Doc(Text("first part") + Text("second part") + Text("see "));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        host.Pages[Para(document, 0)] = (0, "7");
        Assert.NotNull(editor.AddBookmark(new TextRange(At(document, 0, 6), At(document, 1, 6)), "Span"));
        var target = Target(document, host, CrossRefTargetKind.Bookmark);

        Assert.Null(editor.InsertCrossReference(At(document, 2, 4), target, CrossRefKind.Text, host));
        Assert.Equal(CrossRefRefusal.SeveralParagraphs, editor.LastCrossRefRefusal);

        Assert.NotNull(editor.InsertCrossReference(At(document, 2, 4), target, CrossRefKind.Page, host));
        Assert.Equal("see 7", TextOf(Para(document, 2)));
        Assert.NotNull(editor.History.Undo());
        Assert.Empty(RefNames(document));
    }

    [Fact]
    public void ACaptionNumberOnlyReferenceIncludesTheChapterNumber()
    {
        var document = Doc(Text("Chapter one", "Heading1") + Text("pic") + Text("see "), numberedHeadings: true);
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        Assert.NotNull(editor.InsertCaption(At(document, 1), "Figure", ": a", CaptionPlace.Below, chapterNumber: true, "Figure", host));
        var caption = CrossReferences.Targets(document, host, CrossRefTargetKind.Caption).Single();

        Assert.NotNull(editor.InsertCrossReference(At(document, 3, 4), caption, CrossRefKind.NumberOnly, host));

        Assert.Equal("see 1-1", TextOf(Para(document, 3)));
        ReferenceFields.AssertStoredEqualsExpected(document, host, "at insert time");
    }

    [Fact]
    public void ANewNameIsTheNextAfterTheHighestHiddenOne()
    {
        Assert.Equal("_Ref100000001", CrossReferences.NextRefName([]));
        Assert.Equal("_Ref523985435", CrossReferences.NextRefName(["_Ref523985434", "Mine", "_Toc99", "_Reference", "_Ref"]));
        Assert.Null(CrossReferences.NextRefName(["_Ref99999999999999999"]));
        Assert.True(CrossReferences.IsRefName("_Ref123"));
        Assert.False(CrossReferences.IsRefName("_Ref"));
        Assert.False(CrossReferences.IsRefName("_Ref12x"));
        Assert.False(CrossReferences.IsRefName("Ref123"));
    }
}
