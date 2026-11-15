using System.Globalization;
using System.Text;
using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// The oracle over the kinds of edit and the kinds of document that the refill has to get right: inserts, deletes, splits and merges of
/// paragraphs, edits in table cells, in numbered lists and next to notes, in both views, in a document whose model was replaced, and the edits that
/// start over from the first page (a section break, a header).
/// </summary>
public sealed class IncrementalCoverageTests
{
    private static string Corpus(string id) => Path.Combine(TestData.Root, "testdata", "write-corpus", id, id + ".docx");

    private static async Task<DocumentLayout> LaidAsync(WordDocument model, LayoutMode mode = LayoutMode.Pages)
    {
        var layout = DocumentLayout.Create(model, PageTestKit.Fonts, new LayoutOptions(mode));
        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.True(layout.IsComplete);
        return layout;
    }

    private static async Task CheckAsync(DocumentLayout layout, WordDocument model, EditRefill refill, LayoutMode mode, string label)
    {
        Assert.True(refill.FirstPage >= 0, label);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, model, new LayoutOptions(mode), label);
    }

    private static int PageOf(DocumentLayout layout, WordDocument model, Paragraph paragraph) => layout.Locate(TestEdit.TopLevelIndex(model, paragraph))?.Page ?? 0;

    private static Paragraph Texty(WordDocument model, int index, bool bodyOnly = true)
    {
        var all = TestEdit.Paragraphs(model).Where(p => (!bodyOnly || model.Body.Blocks.Contains(p.Paragraph)) && p.Paragraph.SectionBreak is null && TestEdit.TextOf(p.Paragraph).Length > 12).Select(p => p.Paragraph).ToList();
        return all[index < 0 ? all.Count + index : index];
    }

    private static readonly string s_long = string.Concat(Enumerable.Repeat("a longer insert that wraps ", 16));

    [Theory]
    [InlineData(LayoutMode.Pages)]
    [InlineData(LayoutMode.Flow)]
    public async Task ThesisEditsOfEveryKindMatchAFullLayout(LayoutMode mode)
    {
        using var file = DocxFile.Open(Corpus("thesis"));
        var model = file.Model;
        var layout = await LaidAsync(model, mode);
        var step = 0;

        async Task Edit(Func<BlockChange> make, Paragraph? at, string what)
        {
            var through = at is null ? 1 : PageOf(layout, model, at) + 1;
            var refill = layout.ApplyEdit(make(), through);
            await CheckAsync(layout, model, refill, mode, $"{mode} {step++} {what}");
        }

        // The first and the last block, text in the middle, an insert that grows a paragraph by lines, a delete of a long stretch.
        var first = Texty(model, 0);
        await Edit(() => TestEdit.InsertText(model, first, 4, "Q"), first, "first paragraph");
        var last = Texty(model, -1);
        await Edit(() => TestEdit.InsertText(model, last, 2, s_long), last, "last paragraph, long insert");
        var middle = Texty(model, 300);
        await Edit(() => TestEdit.InsertText(model, middle, 8, s_long), middle, "middle, long insert");
        await Edit(() => TestEdit.DeleteText(model, middle, 3, 200), middle, "middle, delete");

        // Structure: a new paragraph, a removed block, a split, a merge.
        var anchor = Texty(model, 500);
        var (container, index) = TestEdit.Find(model, anchor);
        var added = (Paragraph)LayoutModel.Document("<w:p><w:r><w:t>An inserted paragraph that is long enough to take more than one line when it is laid out at the width of this page.</w:t></w:r></w:p>").Body.Blocks[0];
        await Edit(() => TestEdit.InsertBlock(container, index, added), anchor, "insert a paragraph");
        await Edit(() => TestEdit.RemoveBlock(container, index), anchor, "remove it again");
        var splitAt = Texty(model, 700);
        await Edit(() => TestEdit.SplitParagraph(model, splitAt, TestEdit.TextOf(splitAt).Length / 2), splitAt, "split");
        var mergeAt = TestEdit.Paragraphs(model).First(p => model.Body.Blocks.Contains(p.Paragraph) && p.Paragraph.SectionBreak is null && p.Index + 1 < p.Container.Count && p.Container[p.Index + 1] is Paragraph { SectionBreak: null } && TestEdit.TextOf(p.Paragraph).Length > 12 && p.Index > 900).Paragraph;
        await Edit(() => TestEdit.MergeWithNext(model, mergeAt), mergeAt, "merge");
        var gone = Texty(model, 100);
        var (c2, i2) = TestEdit.Find(model, gone);
        await Edit(() => TestEdit.RemoveBlock(c2, i2), gone, "remove a block");
    }

    [Fact]
    public async Task EditsAtPageEdgesMatchAFullLayout()
    {
        using var file = DocxFile.Open(Corpus("thesis"));
        var model = file.Model;
        var layout = await LaidAsync(model);
        var step = 0;
        foreach (var page in new[] { 30, 31, 120, 121 })
        {
            // The first and the last paragraph that has lines on the page.
            var lines = layout.Page(page).TextLines.Where(l => l.Story == StoryRef.Body && model.Body.Blocks.Contains(l.Paragraph) && TestEdit.TextOf(l.Paragraph).Length > 5).ToList();
            foreach (var paragraph in new[] { lines[0].Paragraph, lines[^1].Paragraph })
            {
                var refill = layout.ApplyEdit(TestEdit.InsertText(model, paragraph, 1, step % 2 == 0 ? "z" : s_long), page + 1);
                await CheckAsync(layout, model, refill, LayoutMode.Pages, "page edge " + step++);
            }
        }
    }

    [Fact]
    public async Task EditsInsideTableCellsMatchAFullLayout()
    {
        using var file = DocxFile.Open(Corpus("thesis"));
        var model = file.Model;
        var layout = await LaidAsync(model);
        var cells = TestEdit.Paragraphs(model).Where(p => !ReferenceEquals(p.Container, model.Body.Blocks) && TestEdit.TextOf(p.Paragraph).Length > 3).ToList();
        Assert.True(cells.Count > 20, "the thesis has tables");
        var step = 0;
        foreach (var cell in new[] { cells[0], cells[cells.Count / 2], cells[^1] })
        {
            var through = PageOf(layout, model, cell.Paragraph) + 1;
            var refill = layout.ApplyEdit(TestEdit.InsertText(model, cell.Paragraph, 1, step % 2 == 0 ? "c" : s_long), through);
            Assert.False(refill.Restarted);
            await CheckAsync(layout, model, refill, LayoutMode.Pages, "cell " + step++);
        }
    }

    private static string Item(string text) => $"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static string Plain(string text) => $"<w:p><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static WordDocument NumberedDocument(out string note)
    {
        note = "numbered";
        var body = new StringBuilder();
        for (var i = 0; i < 700; i++)
        {
            var text = $"item number {i} " + string.Concat(Enumerable.Repeat("with some words to fill the lines ", 1 + (i % 4)));
            body.Append(i % 7 == 3 ? Plain("Plain text between the items " + i) : Item(text));
        }

        var numbering = "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";
        return LayoutModel.Document(body.ToString(), LayoutModel.StylesPart("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"/>"), LayoutModel.NumberingPart(numbering));
    }

    [Fact]
    public async Task AddingAndRemovingANumberedParagraphChangesTheLabelsAfterIt()
    {
        var model = NumberedDocument(out _);
        var layout = await LaidAsync(model);
        var step = 0;

        // Insert a numbered item early: every later label moves by one, on every page after it.
        var itemAt = TestEdit.Paragraphs(model).First(p => p.Index == 20 && p.Paragraph.Properties.Children.Count > 0);
        var added = (Paragraph)LayoutModel.Document(Item("a new numbered item")).Body.Blocks[0];
        var refill = layout.ApplyEdit(TestEdit.InsertBlock(model.Body.Blocks, 20, added), PageOf(layout, model, itemAt.Paragraph) + 1);
        Assert.False(refill.Restarted);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "insert item " + step++);

        // Remove one in the middle, then one that is a plain paragraph between the items, then typing in a later item.
        refill = layout.ApplyEdit(TestEdit.RemoveBlock(model.Body.Blocks, 300), PageOf(layout, model, (Paragraph)model.Body.Blocks[299]) + 1);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "remove item " + step++);
        var plain = TestEdit.Paragraphs(model).First(p => p.Index > 400 && TestEdit.TextOf(p.Paragraph).StartsWith("Plain", StringComparison.Ordinal));
        var plainPage = PageOf(layout, model, plain.Paragraph);
        refill = layout.ApplyEdit(TestEdit.RemoveBlock(model.Body.Blocks, plain.Index), plainPage + 1);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "remove plain " + step++);
        var later = (Paragraph)model.Body.Blocks[500];
        refill = layout.ApplyEdit(TestEdit.InsertText(model, later, 3, "typed"), PageOf(layout, model, later) + 1);
        Assert.InRange(layout.PagesLaidOut, 1, 3);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "type in an item " + step++);
    }

    [Fact]
    public async Task ReplacingAnItemByPlainTextRelabelsTheRestWithoutMovingAnyPageBreak()
    {
        // An item and a plain paragraph with the same text take the same room, so every page starts where it did: only the state of the numbering tells that the pages after the edit differ.
        var model = NumberedDocument(out _);
        var layout = await LaidAsync(model);
        var oneLine = TestEdit.Paragraphs(model).First(p => p.Index > 15 && p.Index % 4 == 0 && p.Paragraph.Properties.Children.Count > 0 && TestEdit.TextOf(p.Paragraph).Length < 70);
        var text = TestEdit.TextOf(oneLine.Paragraph);
        var plain = (Paragraph)LayoutModel.Document(Plain(text)).Body.Blocks[0];
        var through = PageOf(layout, model, oneLine.Paragraph) + 1;
        var before = oneLine.Paragraph.Clone();
        model.Body.Blocks[oneLine.Index] = plain;
        var refill = layout.ApplyEdit(new BlockChange(StoryRef.Body, model.Body.Blocks, oneLine.Index, 1, 1, [before]), through);

        Assert.False(refill.Restarted);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "item to plain");
    }

    [Fact]
    public async Task EditsAfterAnEarlyInsertResumeFromRecordsMadeBeforeIt()
    {
        // After a block was inserted in front, the saved page starts of the later pages (a carried piece among them) hold plans whose body index moved.
        var model = NumberedDocument(out _);
        var layout = await LaidAsync(model);
        var added = (Paragraph)LayoutModel.Document(Plain("An early paragraph.")).Body.Blocks[0];
        var refill = layout.ApplyEdit(TestEdit.InsertBlock(model.Body.Blocks, 6, added), 1);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "early insert");
        foreach (var at in new[] { 100, 250, 400, 550, 650 })
        {
            var paragraph = (Paragraph)model.Body.Blocks[at];
            refill = layout.ApplyEdit(TestEdit.InsertText(model, paragraph, 2, "t"), PageOf(layout, model, paragraph) + 1);
            await CheckAsync(layout, model, refill, LayoutMode.Pages, "later edit at " + at);
        }
    }

    // Chapters that each start on a new page with a plain heading: a page start that no edit before it can move, with lists and notes running through them.
    private static WordDocument ChapterDocument(bool footnotes)
    {
        var body = new StringBuilder();
        var notes = new List<string>();
        var ends = new List<string>();
        var number = 0;
        for (var chapter = 0; chapter < 12; chapter++)
        {
            body.Append("<w:p><w:pPr><w:pageBreakBefore/></w:pPr><w:r><w:t>Chapter " + chapter.ToString(CultureInfo.InvariantCulture) + "</w:t></w:r></w:p>");
            for (var i = 0; i < 6; i++)
            {
                number++;
                if (footnotes)
                {
                    body.Append(Para(Text($"chapter {chapter} paragraph {i} with a few words") + FootnoteRef(number) + (i == 2 ? EndnoteRef(number) : string.Empty)));
                    notes.Add(Footnote(number, $"Footnote {number}."));
                    if (i == 2)
                    {
                        ends.Add(Endnote(number, $"Endnote {number}."));
                    }
                }
                else
                {
                    body.Append(Item($"chapter {chapter} item {i} with a few words"));
                }
            }
        }

        if (footnotes)
        {
            return Document(body.ToString(), footnotes: FootnotesPart([.. notes]), endnotes: EndnotesPart([.. ends]));
        }

        var numbering = "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";
        return LayoutModel.Document(body.ToString(), LayoutModel.StylesPart("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"/>"), LayoutModel.NumberingPart(numbering));
    }

    [Fact]
    public async Task ARelabelledListIsLaidOutAgainThroughPageStartsThatLookUnchanged()
    {
        var model = ChapterDocument(footnotes: false);
        var layout = await LaidAsync(model);

        // Chapter 2's items lose one number: every chapter heading is where it was, but the list after it must be numbered again, to the last page.
        var item = TestEdit.Paragraphs(model).First(p => p.Index == 15);
        var text = TestEdit.TextOf(item.Paragraph);
        var before = item.Paragraph.Clone();
        model.Body.Blocks[item.Index] = (Paragraph)LayoutModel.Document(Plain(text)).Body.Blocks[0];
        var refill = layout.ApplyEdit(new BlockChange(StoryRef.Body, model.Body.Blocks, item.Index, 1, 1, [before]), 1000);

        Assert.False(refill.Restarted);
        Assert.True(layout.IsComplete);
        Assert.True(layout.PagesLaidOut > 2, "the refill went on past the unchanged page starts: " + layout.PagesLaidOut);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "relabelled list");
    }

    [Fact]
    public async Task RenumberedNotesAreLaidOutAgainThroughPageStartsThatLookUnchanged()
    {
        var model = ChapterDocument(footnotes: true);
        var layout = await LaidAsync(model);

        var early = TestEdit.Paragraphs(model).First(p => p.Index == 9);
        var refill = layout.ApplyEdit(TestEdit.RemoveNoteReferences(model, early.Paragraph), 1000);

        Assert.False(refill.Restarted);
        Assert.True(layout.IsComplete);
        Assert.True(layout.PagesLaidOut > 2, "the refill went on past the unchanged page starts: " + layout.PagesLaidOut);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "renumbered notes");
    }

    private static WordDocument NotedDocument()
    {
        var body = new StringBuilder();
        var notes = new List<string> { Footnote(99, "A note that no paragraph cites yet, long enough to need a line or two of its own at the foot of a page.") };
        var ends = new List<string> { Endnote(99, "An endnote that no paragraph cites yet.") };
        for (var i = 1; i <= 360; i++)
        {
            var text = $"paragraph {i} " + string.Concat(Enumerable.Repeat("with text that fills a few lines of the page ", 1 + (i % 4)));
            body.Append(Para(Text(text) + FootnoteRef(i) + (i % 11 == 0 ? EndnoteRef(i) : string.Empty)));
            notes.Add(Footnote(i, $"Footnote {i} with some words so that it takes a line or two when the note area is laid out."));
            if (i % 11 == 0)
            {
                ends.Add(Endnote(i, $"Endnote {i}."));
            }
        }

        return Document(body.ToString(), footnotes: FootnotesPart([.. notes]), endnotes: EndnotesPart([.. ends]));
    }

    [Fact]
    public async Task AddingAndRemovingNoteReferencesChangesTheNumbersAfterThem()
    {
        var model = NotedDocument();
        var layout = await LaidAsync(model);
        var step = 0;

        // A new footnote reference early: every later footnote has a new number, and so do the notes at the foot of the pages.
        var early = (Paragraph)model.Body.Blocks[10];
        var refill = layout.ApplyEdit(TestEdit.AddNoteReference(model, early, new FootnoteReference(99)), PageOf(layout, model, early) + 1);
        Assert.False(refill.Restarted);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "add a footnote reference " + step++);

        // A footnote reference removed from a page with others.
        var mid = (Paragraph)model.Body.Blocks[200];
        refill = layout.ApplyEdit(TestEdit.RemoveNoteReferences(model, mid), PageOf(layout, model, mid) + 1);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "remove a footnote reference " + step++);

        // An endnote reference added and removed: the endnote pages at the end change with it.
        var other = (Paragraph)model.Body.Blocks[30];
        refill = layout.ApplyEdit(TestEdit.AddNoteReference(model, other, new EndnoteReference(99)), PageOf(layout, model, other) + 1);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "add an endnote reference " + step++);
        var withEnd = (Paragraph)model.Body.Blocks[43];
        refill = layout.ApplyEdit(TestEdit.RemoveNoteReferences(model, withEnd), PageOf(layout, model, withEnd) + 1);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "remove an endnote reference " + step++);

        // Typing next to a reference does not move any number: the refill stops at once.
        var typed = (Paragraph)model.Body.Blocks[300];
        refill = layout.ApplyEdit(TestEdit.InsertText(model, typed, 4, "typed"), PageOf(layout, model, typed) + 1);
        Assert.InRange(layout.PagesLaidOut, 1, 3);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "type " + step++);

        // A footnote paragraph that is cut off between two pages, then a block insert and remove before notes.
        var added = (Paragraph)LayoutModel.Document(Plain("An inserted paragraph")).Body.Blocks[0];
        refill = layout.ApplyEdit(TestEdit.InsertBlock(model.Body.Blocks, 5, added), 1);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "insert before notes " + step++);
        refill = layout.ApplyEdit(TestEdit.RemoveBlock(model.Body.Blocks, 5), 1);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "remove before notes " + step++);
    }

    private static WordDocument LongNoteDocument()
    {
        var body = new StringBuilder();
        var notes = new List<string> { Footnote(1, string.Join(' ', Enumerable.Repeat("a footnote that runs on and on over several pages", 300))) };
        for (var i = 0; i < 300; i++)
        {
            body.Append(Para(Text($"paragraph {i} " + string.Concat(Enumerable.Repeat("with text that fills a few lines of the page ", 1 + (i % 3)))) + (i == 20 ? FootnoteRef(1) : string.Empty)));
        }

        return Document(body.ToString(), footnotes: FootnotesPart([.. notes]));
    }

    [Fact]
    public async Task ANoteThatRunsOverSeveralPagesIsCarriedThroughEveryRefill()
    {
        var model = LongNoteDocument();
        var layout = await LaidAsync(model);
        var holder = (Paragraph)model.Body.Blocks[20];
        var holderPage = PageOf(layout, model, holder);
        Assert.True(layout.Page(holderPage + 2).TextLines.Any(l => l.Story.Kind == StoryKind.Note), "the note runs on to the page after next");

        var step = 0;
        // Edits on the pages the note runs over, so that a page begins with part of a note that began earlier ...
        foreach (var at in new[] { 60, 90, 120 })
        {
            var paragraph = (Paragraph)model.Body.Blocks[at];
            var refill = layout.ApplyEdit(TestEdit.InsertText(model, paragraph, 2, "t"), PageOf(layout, model, paragraph) + 1);
            await CheckAsync(layout, model, refill, LayoutMode.Pages, "inside the note " + step++);
        }

        // ... and edits that move the reference itself, so that the note starts further down its page and runs over differently.
        foreach (var text in new[] { s_long, "q", s_long + s_long })
        {
            var refill = layout.ApplyEdit(TestEdit.InsertText(model, holder, 3, text), PageOf(layout, model, holder) + 1);
            await CheckAsync(layout, model, refill, LayoutMode.Pages, "move the reference " + step++);
        }

        var before = (Paragraph)model.Body.Blocks[10];
        var moved = layout.ApplyEdit(TestEdit.InsertText(model, before, 1, string.Concat(Enumerable.Repeat(s_long, 3))), PageOf(layout, model, before) + 1);
        await CheckAsync(layout, model, moved, LayoutMode.Pages, "lines above the reference " + step++);
    }

    [Fact]
    public async Task BlockIndexesStayRightForPagesPutBackAfterAnInsertThatMovesNoPageStart()
    {
        // Every chapter starts a page, so a paragraph inserted in chapter 0 stops the refill at the second page and every later page is put back. Their saved
        // starts carry plans whose body index moved by one; an edit in a later chapter resumes from one of them.
        var model = ChapterDocument(footnotes: false);
        var layout = await LaidAsync(model);
        var added = (Paragraph)LayoutModel.Document(Plain("One more paragraph in the first chapter.")).Body.Blocks[0];
        var refill = layout.ApplyEdit(TestEdit.InsertBlock(model.Body.Blocks, 2, added), 1);
        Assert.InRange(layout.PagesLaidOut, 1, 2);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "insert in chapter 0");

        foreach (var at in new[] { 60, 75, 46 })
        {
            var paragraph = (Paragraph)model.Body.Blocks[at];
            refill = layout.ApplyEdit(TestEdit.InsertText(model, paragraph, 2, "t"), PageOf(layout, model, paragraph) + 1);
            Assert.False(refill.Restarted);
            await CheckAsync(layout, model, refill, LayoutMode.Pages, "edit in a later chapter at " + at);
        }
    }

    [Fact]
    public async Task ANoteThatRunsOverChapterStartsStaysCorrectWhenItsReferenceMoves()
    {
        // Chapters of a page and a bit, a long note that runs over the chapter starts, and edits that move its reference down the pages: what the pages
        // after the reference carry from the note changes, so a refill that stops at a chapter heading would keep the old carry.
        var body = new StringBuilder();
        for (var chapter = 0; chapter < 5; chapter++)
        {
            body.Append("<w:p><w:pPr><w:pageBreakBefore/></w:pPr><w:r><w:t>Chapter " + chapter.ToString(CultureInfo.InvariantCulture) + "</w:t></w:r></w:p>");
            for (var i = 0; i < 18; i++)
            {
                body.Append(Plain($"chapter {chapter} paragraph {i} " + string.Concat(Enumerable.Repeat("with some words that fill a couple of lines of the page ", 2))));
            }
        }

        var notes = Footnote(1, string.Join(' ', Enumerable.Repeat("a footnote that runs on and on over several pages", 300)));
        var model = Document(body.ToString(), footnotes: FootnotesPart(notes));
        var layout = await LaidAsync(model);
        var holder = (Paragraph)model.Body.Blocks[3];
        var refill = layout.ApplyEdit(TestEdit.AddNoteReference(model, holder, new FootnoteReference(1)), 1000);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "reference on page 0");
        Assert.True(layout.Page(3).TextLines.Any(l => l.Story.Kind == StoryKind.Note), "the note runs over the chapter starts");

        foreach (var repeats in new[] { 4, 30, 60, 2 })
        {
            var pusher = (Paragraph)LayoutModel.Document(Plain(string.Concat(Enumerable.Repeat("a paragraph that pushes the reference down the pages ", repeats)))).Body.Blocks[0];
            refill = layout.ApplyEdit(TestEdit.InsertBlock(model.Body.Blocks, 3, pusher), 1000);
            await CheckAsync(layout, model, refill, LayoutMode.Pages, "reference pushed by " + repeats);
            refill = layout.ApplyEdit(TestEdit.RemoveBlock(model.Body.Blocks, 3), 1000);
            await CheckAsync(layout, model, refill, LayoutMode.Pages, "pusher removed after " + repeats);
        }
    }

    [Fact]
    public async Task AHeadingThatKeepsWithTheNextBlockIsFoundWhenTheNextBlockChanges()
    {
        // Nine lines and a heading fill a page of ten lines; the next block has a page break before it, so the heading stays. Without the break the heading goes
        // with its block to the next page: the page before the edited block changes.
        var body = new StringBuilder();
        for (var i = 0; i < 9; i++)
        {
            body.Append(PageTestKit.P("filler " + i.ToString(CultureInfo.InvariantCulture)));
        }

        body.Append(PageTestKit.P("Heading", "<w:keepNext/>")).Append(PageTestKit.P("Chapter text", "<w:pageBreakBefore/>")).Append(Tall(10));
        var model = LayoutModel.Document(body.ToString());
        var layout = await LaidAsync(model);
        Assert.Equal(2, layout.LaidOutPages);
        Assert.Contains("Heading", layout.Page(0).Text);

        var before = model.Body.Blocks[10].Clone();
        model.Body.Blocks[10] = (Paragraph)LayoutModel.Document(PageTestKit.P("Chapter text")).Body.Blocks[0];
        var refill = layout.ApplyEdit(new BlockChange(StoryRef.Body, model.Body.Blocks, 10, 1, 1, [before]), 1);

        Assert.Equal(0, refill.FirstPage);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "heading with its block");
        Assert.DoesNotContain("Heading", layout.Page(0).Text);
    }

    [Fact]
    public async Task TheHostOfAParagraphShowsTheTextTheLayoutShowsAndNumbersNothingAgain()
    {
        var model = NotedDocument();
        var layout = await LaidAsync(model);
        var paragraph = (Paragraph)model.Body.Blocks[100];
        var line = layout.Page(PageOf(layout, model, paragraph)).TextLines.First(l => ReferenceEquals(l.Paragraph, paragraph));
        var host = layout.ProjectionHost(StoryRef.Body, paragraph);

        // The projection made with the host is the one the page was laid out from, mark text of the note reference included, however often it is made.
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(line.Projection.Text, ParagraphProjection.Build(paragraph, host).Text);
        }

        Assert.Contains("101", line.Projection.Text);

        // A paragraph that no page shows has the plain host, and nothing the host did disturbed the numbers: an edit elsewhere still equals a full layout.
        var stranger = (Paragraph)LayoutModel.Document(Plain("not in the model")).Body.Blocks[0];
        Assert.Same(ProjectionHost.Plain, layout.ProjectionHost(StoryRef.Body, stranger));
        var other = (Paragraph)model.Body.Blocks[250];
        var refill = layout.ApplyEdit(TestEdit.InsertText(model, other, 2, "t"), PageOf(layout, model, other) + 1);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "after the host was used");
    }

    [Fact]
    public async Task EditingTheParagraphThatEndsASectionStartsOverAndStaysCorrect()
    {
        using var file = DocxFile.Open(Corpus("thesis"));
        var model = file.Model;
        var layout = await LaidAsync(model);
        var ender = TestEdit.Paragraphs(model).First(p => p.Paragraph.SectionBreak is not null && model.Body.Blocks.Contains(p.Paragraph)).Paragraph;
        var refill = layout.ApplyEdit(TestEdit.InsertText(model, ender, 0, "Edited "), PageOf(layout, model, ender) + 1);

        Assert.True(refill.Restarted);
        Assert.Equal(0, refill.FirstPage);
        Assert.False(layout.IsComplete);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "section break paragraph");
    }

    [Fact]
    public async Task EditingAHeaderStartsOverAndStaysCorrect()
    {
        using var file = DocxFile.Open(Corpus("thesis"));
        var model = file.Model;
        var layout = await LaidAsync(model);
        var (id, part) = model.HeadersFooters.First(h => h.Value.Content.Blocks.OfType<Paragraph>().Any(p => TestEdit.TextOf(p).Length > 0));
        var paragraph = part.Content.Blocks.OfType<Paragraph>().First(p => TestEdit.TextOf(p).Length > 0);
        var before = paragraph.Clone();
        var run = paragraph.Inlines.OfType<Run>().First(r => r.Content.OfType<TextContent>().Any());
        run.Content.OfType<TextContent>().First().Text += " edited header";
        var change = new BlockChange(new StoryRef(StoryKind.HeaderFooter, id), part.Content.Blocks, part.Content.Blocks.IndexOf(paragraph), 1, 1, [before]);
        var refill = layout.ApplyEdit(change, 2);

        Assert.True(refill.Restarted);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "header");
    }

    [Fact]
    public async Task AnEditAfterReplaceModelIsRefilledLikeAnyOther()
    {
        var bytes = File.ReadAllBytes(Corpus("thesis"));
        var fonts = PageTestKit.Fonts;
        using var prefix = DocxFile.OpenPrefix(bytes, 64);
        var layout = DocumentLayout.Create(prefix.Model, fonts, new LayoutOptions());
        layout.LayOutFirstPage();
        Assert.True(layout.FirstPageIsFinal);
        using var full = DocxFile.Open(bytes);
        var model = full.Model;
        layout.ReplaceModel(model);
        var revision = layout.Revision;

        // Page 1 was republished without a new revision; an edit in it, and then one further on while the rest is still unlaid.
        var inFirst = Texty(model, 2);
        var refill = layout.ApplyEdit(TestEdit.InsertText(model, inFirst, 2, "first page"), 0);
        Assert.False(refill.Restarted);
        Assert.True(layout.Revision > revision, "an edit that changes pages raises the revision");
        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, model, label: "edit on page 1 after ReplaceModel");

        var later = Texty(model, 200);
        refill = layout.ApplyEdit(TestEdit.InsertText(model, later, 2, "later"), PageOf(layout, model, later) + 1);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "later edit");
    }

    [Fact]
    public async Task AnEditWhileTheLayoutIsIncompleteContinuesFromTheRecordedState()
    {
        using var file = DocxFile.Open(Corpus("thesis"));
        var model = file.Model;
        var layout = DocumentLayout.Create(model, PageTestKit.Fonts, new LayoutOptions());
        layout.LayOutThrough(80);
        Assert.False(layout.IsComplete);

        var paragraph = Texty(model, 60);
        var refill = layout.ApplyEdit(TestEdit.InsertText(model, paragraph, 2, "typing before the layout has finished"), PageOf(layout, model, paragraph) + 1);
        Assert.False(refill.Restarted);
        Assert.False(layout.IsComplete);
        Assert.True(layout.LaidOutPages >= 80, "the pages that were laid out stay: " + layout.LaidOutPages);
        await CheckAsync(layout, model, refill, LayoutMode.Pages, "incomplete layout");
    }

    [Theory]
    [InlineData("poi-table-columns")]
    [InlineData("poi-table-footnotes")]
    [InlineData("poi-endnotes")]
    [InlineData("poi-page-specific-head-foot")]
    [InlineData("poi-complex-numbered-lists")]
    [InlineData("poi-test-document")]
    [InlineData("poi-60329")]
    public async Task CorpusFilesMatchAFullLayoutAfterSeededEdits(string id)
    {
        foreach (var mode in new[] { LayoutMode.Pages, LayoutMode.Flow })
        {
            using var file = DocxFile.Open(Corpus(id));
            var model = file.Model;
            var layout = await LaidAsync(model, mode);
            var random = new Random(id.Length * 31);
            var step = 0;
            for (var i = 0; i < 6; i++)
            {
                var all = TestEdit.Paragraphs(model).Where(p => p.Paragraph.SectionBreak is null).ToList();
                if (all.Count == 0)
                {
                    break;
                }

                var pick = i switch { 0 => all[0], 1 => all[^1], _ => all[random.Next(all.Count)] };
                BlockChange change;
                switch (i % 4)
                {
                    case 0:
                        change = TestEdit.InsertText(model, pick.Paragraph, 0, i % 2 == 0 ? "x" : s_long);
                        break;
                    case 1 when pick.Container.Count > 1:
                        change = TestEdit.SplitParagraph(model, pick.Paragraph, TestEdit.TextOf(pick.Paragraph).Length / 2);
                        break;
                    case 2 when pick.Container.Count > 2 && ReferenceEquals(pick.Container, model.Body.Blocks):
                        change = TestEdit.RemoveBlock(pick.Container, pick.Index);
                        break;
                    default:
                        change = TestEdit.DeleteText(model, pick.Paragraph, 0, 5);
                        break;
                }

                var through = model.Body.Blocks.Contains(pick.Paragraph) ? (layout.Locate(Math.Min(TestEdit.TopLevelIndex(model, pick.Paragraph), model.Body.Blocks.Count - 1))?.Page ?? 0) + 1 : 1;
                var refill = layout.ApplyEdit(change, through);
                await CheckAsync(layout, model, refill, mode, $"{id} {mode} {step++}");
            }
        }
    }

    [Fact]
    public void TablePlansAreRebasedWithTheirCells()
    {
        // A table whose block moves because a paragraph was inserted before it: the plan and the plans inside its cells (a nested table too) take the new index.
        var cell = "<w:tc><w:tcPr><w:tcW w:w=\"2000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc>";
        var inner = $"<w:tbl><w:tblGrid><w:gridCol w:w=\"2000\"/></w:tblGrid><w:tr>{cell}</w:tr></w:tbl>";
        var outerCell = $"<w:tc><w:tcPr><w:tcW w:w=\"4000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>outer</w:t></w:r></w:p>{inner}<w:p/></w:tc>";
        var table = $"<w:tbl><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr>{outerCell}</w:tr></w:tbl>";
        var model = LayoutModel.Document(Plain("before") + table + Plain("after"));
        var styles = new StyleResolver(model.Styles, model.Settings, model.Numbering);
        var planner = new BlockPlanner(styles, new NumberingCounter(model.Numbering), new ParagraphLayouter(PageTestKit.Fonts), model.Settings);
        var flat = FlatBlocks.From(model.Body.Blocks);
        var plan = (TablePlan)planner.PlanFor(flat[1])!;
        Assert.Equal(1, plan.BodyIndex);
        var flow = plan.Layout(300f);
        Assert.Equal(1, flow.BodyIndex);

        model.Body.Blocks.Insert(0, LayoutModel.Document(Plain("new")).Body.Blocks[0]);
        var shifted = FlatBlocks.From(model.Body.Blocks);
        planner.RebaseAll(shifted);

        Assert.Equal(2, plan.BodyIndex);
        Assert.Equal(2, plan.Layout(300f).BodyIndex);
        var cellPlans = plan.Model.Rows.SelectMany(r => r.Cells).SelectMany(c => c.Content).ToList();
        Assert.NotEmpty(cellPlans);
        Assert.All(cellPlans, p => Assert.Equal(2, p.BodyIndex));
        var nested = cellPlans.OfType<TablePlan>().ToList();
        Assert.NotEmpty(nested);
        Assert.All(nested.SelectMany(t => t.Model.Rows).SelectMany(r => r.Cells).SelectMany(c => c.Content), p => Assert.Equal(2, p.BodyIndex));
    }

    [Fact]
    public async Task ThePlanCacheDoesNotGrowWithoutLimitOverManyStructuralEdits()
    {
        var model = NumberedDocument(out _);
        var layout = await LaidAsync(model);
        for (var i = 0; i < 300; i++)
        {
            var added = (Paragraph)LayoutModel.Document(Plain("inserted " + i.ToString(CultureInfo.InvariantCulture))).Body.Blocks[0];
            layout.ApplyEdit(TestEdit.InsertBlock(model.Body.Blocks, 50, added), 1);
            layout.ApplyEdit(TestEdit.RemoveBlock(model.Body.Blocks, 50), 1);
        }

        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, model, label: "after 600 structural edits");
    }
}
