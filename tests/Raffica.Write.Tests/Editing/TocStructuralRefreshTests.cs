using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// The refresh of a table of contents (milestone 4.5b): an entry that changes its text is rewritten in place and never recorded; an entry that appears or goes changes the number of paragraphs, which is a structural change the
/// newest history step carries (undo reverts it before the step, redo replays it after), left undone while a redo is pending, and not recorded at all when there is no step to protect.
/// </summary>
public sealed class TocStructuralRefreshTests
{
    private const string Section = "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>";

    private static readonly TocOptions s_default = new(1, 3, true, true, TocLeader.Dots, null);

    private static string Run(string text) => $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    private static string P(string inner, string? style = null) =>
        $"<w:p><w:pPr>{(style is null ? string.Empty : $"<w:pStyle w:val=\"{style}\"/>")}</w:pPr>{inner}</w:p>";

    private static string Text(string text, string? style = null) => P(Run(text), style);

    private static string Field(string instruction, string result) =>
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
        + $"<w:r><w:instrText xml:space=\"preserve\"> {instruction} </w:instrText></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
        + Run(result)
        + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    // TOC1 is defined: the hand-written lists name it, as every list of Word names a style the file has.
    private static string Styles()
    {
        var styles = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style><w:style w:type=\"paragraph\" w:styleId=\"TOC1\"><w:name w:val=\"toc 1\"/><w:basedOn w:val=\"Normal\"/></w:style>";
        for (var i = 1; i <= 9; i++)
        {
            styles += $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:pPr><w:outlineLvl w:val=\"{i - 1}\"/></w:pPr></w:style>";
        }

        return LayoutModel.StylesPart(styles);
    }

    private static WordDocument Doc(string body) => LayoutModel.Document(body + Section, Styles(), LayoutModel.NumberingPart(string.Empty));

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(document));

    private static Paragraph Para(WordDocument document, int block) => (Paragraph)document.Body.Blocks[block];

    private static TextPosition At(WordDocument document, int block, int offset = 0) => new(StoryRef.Body, Para(document, block), offset);

    private static TextRange Whole(WordDocument document, int block) => new(At(document, block), At(document, block, TextOf(Para(document, block)).Length));

    private static string TextOf(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    // The body alone: the styles a step made in place stay after an undo, by design.
    private static string BodyBytes(WordDocument document) =>
        string.Concat(ModelSnapshot.Of(document).Elements().Where(p => (string?)p.Attribute("name") == "document").Select(p => p.ToString(SaveOptions.DisableFormatting)));

    // The entry paragraphs of the first table of contents: a paragraph that holds only the end mark is not one of them.
    private static List<Paragraph> TocParagraphs(WordDocument document)
    {
        if (TocRefresh.Find(document.Body.Blocks) is not [var site, ..])
        {
            return [];
        }

        var last = TocRefresh.Analyze(site)?.TailParagraph == true ? site.Last - 1 : site.Last;
        return [.. Enumerable.Range(site.First, last - site.First + 1).Select(i => (Paragraph)site.Container[i])];
    }

    // The stored text of every entry paragraph, tabs as '|' (the instruction left out).
    private static List<string> Lines(WordDocument document) =>
        [.. TocParagraphs(document).Select(p => string.Concat(ModelWalk.Inlines(p.Inlines).OfType<Run>().SelectMany(r => r.Content).Select(c => c switch { TextContent t => t.Text, TabContent => "|", _ => string.Empty })))];

    private static EditResult Step(EditResult? result)
    {
        Assert.NotNull(result);
        return result;
    }

    // What the session does after an edit: the refresh over the touched blocks, the new paragraphs told to the layout (not here), the structural record attached to the history.
    private static (FieldRefreshResult Result, AttachOutcome? Outcome) Refresh(TextEditor editor, FakeFieldHost host, EditResult? after = null)
    {
        var result = editor.RefreshFields(host, RefreshScope.OrderBased, after is null ? null : [.. after.ChangesInApplyOrder()]);
        AttachOutcome? outcome = result.Structural is { } record ? editor.History.AttachDerived(record) : null;
        return (result, outcome);
    }

    // Headings A, B (with their hidden bookmarks) and three paragraphs; the table of contents is made by Insert.
    private static (WordDocument Document, TextEditor Editor, FakeFieldHost Host) WithToc(string body, int at)
    {
        var document = Doc(body);
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        Assert.NotNull(editor.InsertToc(At(document, at), s_default, host));
        return (document, editor, host);
    }

    [Fact]
    public void AddingAHeadingAddsAnEntryAndUndoRemovesBoth()
    {
        var (document, editor, host) = WithToc(Text("Alpha", "Heading1") + Text("x") + Text("Beta", "Heading1") + Text("y") + Text(string.Empty), 4);
        var stable = BodyBytes(document);
        Assert.Equal(["Alpha|?", "Beta|?"], Lines(document));

        var step = Step(editor.ApplyParagraphStyle(Whole(document, 1), "Heading1"));
        var afterStep = BodyBytes(document);
        var (result, outcome) = Refresh(editor, host, step);

        Assert.NotNull(result.Structural);
        Assert.Equal(AttachOutcome.Attached, outcome);
        Assert.Equal(["Alpha|?", "x|?", "Beta|?"], Lines(document));
        ReferenceToc.AssertEqualToExpected(document, host, "after the refresh");
        var structural = Assert.Single(result.Structural.Changes);
        Assert.Equal((2, 3), (structural.Removed, structural.Added));

        // Undo reverts the record first and then the step: the document is what it was before both, bytes and indices.
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(stable, BodyBytes(document));
        Assert.Equal(["Alpha|?", "Beta|?"], Lines(document));
        Assert.NotEqual(afterStep, BodyBytes(document));
    }

    [Fact]
    public void TypingARenamedHeadingChangesTheEntryInPlace()
    {
        var (document, editor, host) = WithToc(Text("Alpha", "Heading1") + Text("Beta", "Heading1") + Text(string.Empty), 2);
        var steps = editor.History.UndoCount;
        var step = Step(editor.InsertText(new TextRange(At(document, 0, 5), At(document, 0, 5)), "bet"));

        var (result, outcome) = Refresh(editor, host, step);

        Assert.Null(result.Structural);
        Assert.Null(outcome);
        Assert.False(result.StructuralDeferred);
        Assert.Equal(["Alphabet|?", "Beta|?"], Lines(document));
        Assert.Equal(steps + 1, editor.History.UndoCount);
        var change = Assert.Single(result.Content);
        Assert.Equal((1, 1), (change.Removed, change.Added));
        ReferenceToc.AssertEqualToExpected(document, host, "renamed");

        // The undo of the typing leaves the entry as it was; the next refresh (over the undone blocks) puts it right again.
        var undone = Assert.IsType<EditResult>(editor.History.Undo());
        Assert.Equal(["Alphabet|?", "Beta|?"], Lines(document));
        var (again, againOutcome) = Refresh(editor, host, undone);
        Assert.Null(again.Structural);
        Assert.Null(againOutcome);
        Assert.Equal(["Alpha|?", "Beta|?"], Lines(document));
    }

    [Fact]
    public void ADeletedHeadingRemovesItsEntry()
    {
        var (document, editor, host) = WithToc(Text("Alpha", "Heading1") + Text("x") + Text("Beta", "Heading1") + Text("y") + Text("Gamma", "Heading1") + Text(string.Empty), 5);
        var stable = BodyBytes(document);
        Assert.Equal(3, TocParagraphs(document).Count);

        var step = Step(editor.Delete(new TextRange(At(document, 1, 1), At(document, 2, 4))));
        var (result, outcome) = Refresh(editor, host, step);

        Assert.NotNull(result.Structural);
        Assert.Equal(AttachOutcome.Attached, outcome);
        Assert.Equal(["Alpha|?", "Gamma|?"], Lines(document));
        ReferenceToc.AssertEqualToExpected(document, host, "after the delete");

        Assert.NotNull(editor.History.Undo());
        Assert.Equal(stable, BodyBytes(document));
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(["Alpha|?", "Gamma|?"], Lines(document));

        // A list that shrinks to one line keeps two paragraphs: the end mark has one of its own, so the field stays a field that spans paragraphs.
        var rest = Step(editor.Delete(new TextRange(At(document, 1, 1), At(document, 3, 5))));
        var (last, _) = Refresh(editor, host, rest);
        Assert.Equal(["Alpha|?"], Lines(document));
        var site = Assert.Single(TocRefresh.Find(document.Body.Blocks));
        Assert.Equal(2, site.Last - site.First + 1);
        Assert.Null(last.Structural);
    }

    // A table of contents as Word writes it: the field starts in the first entry, ends in a paragraph of its own, and its entries are links with page numbers; the headings carry their hidden bookmarks.
    private static string StaleToc(string instruction, params (string Name, string Text, int Level)[] entries)
    {
        var xml = string.Empty;
        for (var i = 0; i < entries.Length; i++)
        {
            var (name, text, level) = entries[i];
            var link = $"<w:hyperlink w:anchor=\"{name}\" w:history=\"1\">{Run(text)}<w:r><w:tab/></w:r>{Field("PAGEREF " + name + " \\h", (i + 7).ToString(System.Globalization.CultureInfo.InvariantCulture))}</w:hyperlink>";
            var head = i == 0 ? "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" + $"<w:r><w:instrText xml:space=\"preserve\"> {instruction} </w:instrText></w:r>" + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" : string.Empty;
            xml += P(head + link, "TOC" + level.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return xml + "<w:p><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";
    }

    private static string Marked(string text, int id, string style = "Heading1") =>
        P($"<w:bookmarkStart w:id=\"{id}\" w:name=\"_Toc10000000{id}\"/>{Run(text)}<w:bookmarkEnd w:id=\"{id}\"/>", style);

    private const string TocInstruction = "TOC \\o \"1-3\" \\h \\z \\u";

    private static WordDocument StaleDocument() => Doc(
        Marked("Alpha", 1) + Marked("Beta", 2) + Marked("Gamma", 3) + Text("after")
        + StaleToc(TocInstruction, ("_Toc100000001", "Alpha", 1), ("_Toc100000003", "Gamma", 1)) + Text("end"));

    [Fact]
    public void AStaleWordTocIsRefreshedWithoutDirtyingAndWithNothingAttached()
    {
        var document = StaleDocument();
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        var revision = document.Edits.Revision;
        var derived = document.Edits.DerivedVersion;
        Assert.Equal(["Alpha|7", "Gamma|8"], Lines(document));

        var result = editor.RefreshFields(host, RefreshScope.OrderBased);

        // The count changes (two entries become three): a structural result; with no history step to protect nothing is attached and the refresh still applies.
        Assert.NotNull(result.Structural);
        Assert.Equal(AttachOutcome.NothingToProtect, editor.History.AttachDerived(result.Structural));
        Assert.False(editor.History.CanUndo);
        Assert.False(document.Edits.IsDirty);
        Assert.Equal(revision, document.Edits.Revision);
        Assert.True(document.Edits.DerivedVersion > derived);
        Assert.Equal(["Alpha|7", "Beta|?", "Gamma|8"], Lines(document));

        // The instruction run and the end mark are the ones the file had.
        var first = TocParagraphs(document)[0];
        Assert.Equal(" " + TocInstruction + " ", first.Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<InstructionText>().First().Text);
        Assert.Equal(["TOC1", "TOC1", "TOC1"], TocParagraphs(document).Select(p => p.Properties.StyleId));
        var site = Assert.Single(TocRefresh.Find(document.Body.Blocks));
        Assert.Contains(((Paragraph)site.Container[site.Last]).Inlines.OfType<Run>().SelectMany(r => r.Content), c => c is FieldChar { Kind: FieldCharKind.End });
        ReferenceToc.AssertEqualToExpected(document, host, "stale word toc");
        Assert.Empty(editor.RefreshFields(host, RefreshScope.OrderBased).Content);
    }

    [Fact]
    public void AnUnsupportedTocKeepsItsBytesAndIsCounted()
    {
        var document = Doc(Marked("Alpha", 1) + Text("after") + StaleToc("TOC \\t \"Chapterish,1\"", ("_Toc100000001", "Old", 1)) + Text("end"));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        var before = BodyBytes(document);

        var result = editor.RefreshFields(host, RefreshScope.OrderBased);

        Assert.Equal(before, BodyBytes(document));
        Assert.Empty(result.Content);
        Assert.Null(result.Structural);
        Assert.True(result.NotUpdated >= 1);
    }

    [Fact]
    public void AStructuralRefreshWaitsWhileARedoIsPending()
    {
        var (document, editor, host) = WithToc(Text("Alpha", "Heading1") + Text("x") + Text("y") + Text(string.Empty), 3);
        Step(editor.ApplyParagraphStyle(Whole(document, 1), "Heading1"));
        Step(editor.InsertText(new TextRange(At(document, 2, 1), At(document, 2, 1)), "q"));
        Assert.NotNull(editor.History.Undo());
        Assert.True(editor.History.CanRedo);
        var before = BodyBytes(document);

        // The table of contents needs one more entry (the restyled paragraph), the redo stack is not empty: nothing is changed, and the result says so.
        var waiting = editor.RefreshFields(host, RefreshScope.OrderBased);

        Assert.True(waiting.StructuralDeferred);
        Assert.Null(waiting.Structural);
        Assert.Empty(waiting.Content);
        Assert.Equal(before, BodyBytes(document));
        Assert.Equal(["Alpha|?"], Lines(document));

        // Once the redo is done the same refresh makes the change and the newest step carries it.
        Assert.NotNull(editor.History.Redo());
        var (done, outcome) = Refresh(editor, host);
        Assert.False(done.StructuralDeferred);
        Assert.NotNull(done.Structural);
        Assert.Equal(AttachOutcome.Attached, outcome);
        Assert.Equal(["Alpha|?", "x|?"], Lines(document));

        // Undo of the newest step takes its record with it: the table of contents is as stale as it was after the restyle.
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(["Alpha|?"], Lines(document));
    }

    [Fact]
    public void RedoAfterAStructuralRefreshGivesTheSameBytes()
    {
        var (document, editor, host) = WithToc(Text("Alpha", "Heading1") + Text("x") + Text(string.Empty), 2);
        var step = Step(editor.ApplyParagraphStyle(Whole(document, 1), "Heading1"));
        var (_, outcome) = Refresh(editor, host, step);
        Assert.Equal(AttachOutcome.Attached, outcome);
        var settled = BodyBytes(document);

        var undone = Assert.IsType<EditResult>(editor.History.Undo());
        var redone = Assert.IsType<EditResult>(editor.History.Redo());
        Assert.NotNull(undone.DerivedChanges);
        Assert.NotNull(redone.DerivedChanges);

        // The record is replayed with the step: the paragraphs are the ones the refresh made. The hidden bookmark of the restyled heading is a content change the refresh makes again; nothing structural follows.
        Assert.Equal(["Alpha|?", "x|?"], Lines(document));
        var (again, againOutcome) = Refresh(editor, host, redone);
        Assert.Null(again.Structural);
        Assert.Null(againOutcome);
        Assert.Equal(settled, BodyBytes(document));
        var (third, _) = Refresh(editor, host);
        Assert.Empty(third.Content);
    }

    [Fact]
    public void UndoAfterAStructuralRefreshKeepsEveryOlderStepsIndices()
    {
        // Three paragraphs above the table of contents and three below, one typing step in each; then a step that makes a new heading and a structural refresh that adds a paragraph to the list.
        var (document, editor, host) = WithToc(
            Text("Alpha", "Heading1") + Text("t1") + Text("t2") + Text("t3") + Text(string.Empty) + Text("u1") + Text("u2") + Text("u3"),
            4);
        var snapshots = new List<string> { BodyBytes(document) };
        foreach (var block in new[] { 1, 6, 2, 7, 3, 8 })
        {
            var at = At(document, block, 1);
            Assert.NotNull(editor.InsertText(new TextRange(at, at), "n" + block));
            snapshots.Add(BodyBytes(document));
        }

        var step = Step(editor.ApplyParagraphStyle(Whole(document, 2), "Heading1"));
        var (result, outcome) = Refresh(editor, host, step);
        Assert.NotNull(result.Structural);
        Assert.Equal(AttachOutcome.Attached, outcome);
        var settled = BodyBytes(document);
        Assert.Equal(["Alpha|?", "tn22|?"], Lines(document).Take(2));

        // Back through all seven steps: each undo gives the bytes of the document before that step.
        for (var i = snapshots.Count - 1; i >= 0; i--)
        {
            Assert.NotNull(editor.History.Undo());
            Assert.Equal(snapshots[i], BodyBytes(document));
        }

        Assert.Equal(1, editor.History.UndoCount);

        // And forward through all of them again; the last redo replays the record, and the refresh over it only puts the hidden bookmark back.
        EditResult? last = null;
        while (editor.History.CanRedo)
        {
            last = editor.History.Redo();
            Assert.NotNull(last);
        }

        var (again, againOutcome) = Refresh(editor, host, last);
        Assert.Null(again.Structural);
        Assert.Null(againOutcome);
        Assert.Equal(settled, BodyBytes(document));
    }

    private static readonly System.Xml.Linq.XName s_tabs = System.Xml.Linq.XName.Get("tabs", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");

    [Fact]
    public void AListMadeWithoutRightAlignmentStaysSoThroughRefreshesAndNewEntries()
    {
        var document = Doc(Text("Alpha", "Heading1") + Text("x") + Text("Beta", "Heading1") + Text(string.Empty));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        Assert.NotNull(editor.InsertToc(At(document, 3), new TocOptions(1, 3, true, false, TocLeader.Dots, null), host));
        var before = BodyBytes(document);
        var own = TocParagraphs(document)[0].Properties.Get(s_tabs)?.ToString(SaveOptions.DisableFormatting);
        Assert.NotNull(own);

        // The next refresh finds nothing to write.
        var again = editor.RefreshFields(host, RefreshScope.OrderBased);
        Assert.Empty(again.Content);
        Assert.Equal(before, BodyBytes(document));

        // A heading added later gets an entry like its neighbours: the page number after a space, the same stops.
        var step = Step(editor.ApplyParagraphStyle(Whole(document, 1), "Heading1"));
        var (result, _) = Refresh(editor, host, step);
        Assert.NotNull(result.Structural);
        Assert.Equal(["Alpha ?", "x ?", "Beta ?"], Lines(document));
        Assert.All(TocParagraphs(document), p => Assert.Equal(own, p.Properties.Get(s_tabs)?.ToString(SaveOptions.DisableFormatting)));
        ReferenceToc.AssertEqualToExpected(document, host, "left aligned");
    }

    [Fact]
    public void ARightAlignedListGivesANewEntryTheTabStopOfItsNeighbours()
    {
        var (document, editor, host) = WithToc(Text("Alpha", "Heading1") + Text("x") + Text("Beta", "Heading1") + Text(string.Empty), 3);
        var own = TocParagraphs(document)[0].Properties.Get(s_tabs);
        Assert.NotNull(own);

        var step = Step(editor.ApplyParagraphStyle(Whole(document, 1), "Heading1"));
        var (result, _) = Refresh(editor, host, step);

        Assert.NotNull(result.Structural);
        var paragraphs = TocParagraphs(document);
        Assert.Equal(3, paragraphs.Count);
        Assert.All(paragraphs, p => Assert.Equal(own.ToString(SaveOptions.DisableFormatting), p.Properties.Get(s_tabs)?.ToString(SaveOptions.DisableFormatting)));
        Assert.Empty(editor.RefreshFields(host, RefreshScope.OrderBased).Content);
    }

    [Fact]
    public void AForeignListWithoutTabStopsKeepsNone()
    {
        var document = StaleDocument();
        var editor = Editor(document);
        var host = new FakeFieldHost(document);

        editor.RefreshFields(host, RefreshScope.OrderBased);

        Assert.Equal(["Alpha|7", "Beta|?", "Gamma|8"], Lines(document));
        Assert.All(TocParagraphs(document), p => Assert.Null(p.Properties.Get(s_tabs)));
    }

    [Fact]
    public void TwoListsInOneContainerThatBothChangeTheirParagraphCountUndoAndRedoToTheSameBytes()
    {
        var document = Doc(
            Marked("Alpha", 1) + Marked("Beta", 2, "Heading2") + Marked("Gamma", 3) + Text("after")
            + StaleToc("TOC \\o \"1-1\" \\h \\z \\u", ("_Toc100000001", "Alpha", 1)) + Text("mid")
            + StaleToc("TOC \\o \"1-2\" \\h \\z \\u", ("_Toc100000001", "Alpha", 1)) + Text("end"));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        var original = BodyBytes(document);
        var typing = Step(editor.InsertText(new TextRange(At(document, 3, 1), At(document, 3, 1)), "q"));
        var beforeRefresh = BodyBytes(document);

        var (result, outcome) = Refresh(editor, host);

        // Both lists grow (one entry became two, one became three) in one refresh: one record with two changes, each in the coordinates after the one before it.
        Assert.NotNull(typing);
        Assert.Equal(AttachOutcome.Attached, outcome);
        Assert.Equal(2, result.Structural!.Changes.Count);
        Assert.Equal([1, 2], result.Structural.Changes.Select(c => c.Added - c.Removed).ToArray());
        ReferenceToc.AssertEqualToExpected(document, host, "two lists");
        var afterRefresh = BodyBytes(document);
        Assert.NotEqual(beforeRefresh, afterRefresh);

        Assert.NotNull(editor.History.Undo());
        Assert.Equal(original, BodyBytes(document));
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(afterRefresh, BodyBytes(document));
        Assert.Empty(editor.RefreshFields(host, RefreshScope.OrderBased).Content);
    }

    [Fact]
    public void AListWhoseEntriesCarryASectionBreakIsNotUpdatedButOneWithTheBreakOnItsEndParagraphIs()
    {
        const string Break = "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>";
        var entries = (("_Toc100000001", "Alpha", 1), ("_Toc100000003", "Gamma", 1));
        var toc = StaleToc(TocInstruction, entries.Item1, entries.Item2);
        var marked = Marked("Alpha", 1) + Marked("Beta", 2) + Marked("Gamma", 3) + Text("after");

        // A break on an entry paragraph: replacing the paragraphs would lose it, so the list keeps its bytes and is counted.
        var inside = Doc(marked + toc.Replace("<w:pStyle w:val=\"TOC1\"/></w:pPr>", "<w:pStyle w:val=\"TOC1\"/>" + Break + "</w:pPr>", StringComparison.Ordinal) + Text("end"));
        var insideEditor = Editor(inside);
        var before = BodyBytes(inside);
        var skipped = insideEditor.RefreshFields(new FakeFieldHost(inside), RefreshScope.OrderBased);
        Assert.Equal(before, BodyBytes(inside));
        Assert.Null(skipped.Structural);
        Assert.True(skipped.NotUpdated >= 1);

        // A break on the paragraph that holds the end mark alone is not rewritten: the list is updated and the break stays.
        var tail = Doc(marked + toc.Replace("<w:p><w:r><w:fldChar w:fldCharType=\"end\"/>", "<w:p><w:pPr>" + Break + "</w:pPr><w:r><w:fldChar w:fldCharType=\"end\"/>", StringComparison.Ordinal) + Text("end"));
        var tailEditor = Editor(tail);
        var done = tailEditor.RefreshFields(new FakeFieldHost(tail), RefreshScope.OrderBased);
        Assert.NotNull(done.Structural);
        Assert.Equal(["Alpha|7", "Beta|?", "Gamma|8"], Lines(tail));
        var site = Assert.Single(TocRefresh.Find(tail.Body.Blocks));
        Assert.NotNull(((Paragraph)site.Container[site.Last]).SectionBreak);
    }

    // One row per step kind the editor records; the table of contents sits after the paragraphs the steps work on and misses the entry of Beta, so the refresh after the step is structural.
    public static TheoryData<string> Kinds() => ["typing", "format", "list", "move", "section", "object", "bookmark"];

    private static string Styles2() => LayoutModel.StylesPart(
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>"
        + string.Concat(Enumerable.Range(1, 9).Select(i => $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:pPr><w:outlineLvl w:val=\"{i - 1}\"/></w:pPr></w:style>")));

    [Theory]
    [MemberData(nameof(Kinds))]
    public void TheHistoryMatrixWithARealToc(string kind)
    {
        var document = LayoutModel.Document(
            Marked("Alpha", 1) + Text("a") + Marked("Beta", 2, "Heading2") + Text("b") + Marked("Gamma", 3) + Text("c") + Text("d")
            + StaleToc(TocInstruction, ("_Toc100000001", "Alpha", 1), ("_Toc100000003", "Gamma", 1)) + Text("end") + Section,
            Styles2(),
            LayoutModel.NumberingPart(string.Empty));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        Assert.NotNull(editor.InsertText(new TextRange(At(document, 5, 1), At(document, 5, 1)), "q"));
        var beforeStep = BodyBytes(document);

        Step(kind switch
        {
            "typing" => editor.InsertText(new TextRange(At(document, 3, 1), At(document, 3, 1)), "ab"),
            "format" => editor.ToggleCharacter(Whole(document, 1), CharacterToggle.Bold),
            "list" => editor.ToggleList(new TextRange(At(document, 1), At(document, 3, 1)), ListKind.Bullet),
            "move" => editor.MoveBlocks(0, 2, 7),
            "section" => editor.InsertSectionBreak(At(document, 3, 1), SectionBreakKind.NextPage),
            "object" => editor.InsertNote(At(document, 3, 1), NoteType.Footnote),
            "bookmark" => editor.AddBookmark(new TextRange(At(document, 1), At(document, 3, 1)), "Mark"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        });
        var afterStep = BodyBytes(document);
        Assert.NotEqual(beforeStep, afterStep);

        var (refreshed, outcome) = Refresh(editor, host);
        Assert.NotNull(refreshed.Structural);
        Assert.Equal(AttachOutcome.Attached, outcome);
        var afterBoth = BodyBytes(document);
        Assert.NotEqual(afterStep, afterBoth);

        // Undo reverts the record before the step and gives the state before the step; redo gives the state after both.
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(beforeStep, BodyBytes(document));
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(afterBoth, BodyBytes(document));

        // And once more, with the older step too.
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Assert.False(editor.History.CanUndo);
        Assert.Equal(2, TocParagraphs(document).Count);
    }
}
