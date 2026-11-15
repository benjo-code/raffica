using System.Diagnostics;
using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Editing;

public sealed class TextEditorTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

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

    private static Paragraph Styled(string style, params string[] runs)
    {
        var paragraph = P(runs);
        paragraph.Properties.Set(W + "pStyle", new XElement(W + "pStyle", new XAttribute(W + "val", style)));
        return paragraph;
    }

    private static string StyleOf(Paragraph paragraph) => paragraph.Properties.StyleId ?? string.Empty;

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

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static TextPosition At(Paragraph paragraph, int offset) => new(StoryRef.Body, paragraph, offset);

    private static TextRange Caret(Paragraph paragraph, int offset) => new(At(paragraph, offset), At(paragraph, offset));

    private static TextRange Sel(Paragraph paragraph, int start, int end) => new(At(paragraph, start), At(paragraph, end));

    private static TextRange Sel(Paragraph first, int start, Paragraph second, int end) => new(At(first, start), At(second, end));

    private static Table CellTable(out TableCell cell, params Paragraph[] cellBlocks)
    {
        cell = new TableCell();
        foreach (var block in cellBlocks)
        {
            cell.Blocks.Add(block);
        }

        var row = new TableRow();
        row.Items.Add(cell);
        var table = new Table();
        table.Rows.Add(row);
        return table;
    }

    private static Run[] Runs(Paragraph paragraph) => paragraph.Inlines.OfType<Run>().ToArray();

    [Fact]
    public void InsertAtStartMiddleEnd()
    {
        var p = P("Hello");
        var editor = Editor(Doc(p));

        var result = editor.InsertText(Caret(p, 0), ">");
        Assert.Equal(">Hello", Text(p));
        Assert.Equal(At(p, 1), result.Caret);

        result = editor.InsertText(Caret(p, 4), "-");
        Assert.Equal(">Hel-lo", Text(p));
        Assert.Equal(At(p, 5), result.Caret);

        result = editor.InsertText(Caret(p, 7), "!");
        Assert.Equal(">Hel-lo!", Text(p));
        Assert.Equal(At(p, 8), result.Caret);
    }

    [Fact]
    public void InsertReplacesSelection()
    {
        var p = P("Hello world");
        var editor = Editor(Doc(p));

        var result = editor.InsertText(Sel(p, 6, 11), "there");
        Assert.Equal("Hello there", Text(p));
        Assert.Equal(At(p, 11), result.Caret);

        result = editor.InsertText(Sel(p, 11, 6), "x");
        Assert.Equal("Hello x", Text(p));
        Assert.Equal(At(p, 7), result.Caret);
    }

    [Fact]
    public void InsertReplacesASelectionAcrossParagraphs()
    {
        var p1 = P("Hello");
        var p2 = P("World");
        var p3 = P("Again");
        var document = Doc(p1, p2, p3);

        var result = Editor(document).InsertText(Sel(p1, 2, p3, 2), "-");
        Assert.Equal(["He-ain"], Texts(document));
        Assert.Equal(At(p1, 3), result.Caret);
    }

    [Fact]
    public void InsertJoinsThePreviousRunAndKeepsItsProperties()
    {
        var bold = R("Bold", bold: true);
        var plain = R(" plain");
        var p = new Paragraph();
        p.Inlines.Add(bold);
        p.Inlines.Add(plain);
        var editor = Editor(Doc(p));

        editor.InsertText(Caret(p, 4), "er");
        Assert.Equal("Bolder plain", Text(p));
        Assert.Equal("Bolder", ((TextContent)bold.Content[0]).Text);
        Assert.True(bold.Properties.Bold);
        Assert.Equal(" plain", ((TextContent)plain.Content[0]).Text);

        // At a paragraph start the run after the caret supplies the properties.
        editor.InsertText(Caret(p, 0), ">");
        Assert.Equal(">Bolder", ((TextContent)bold.Content[0]).Text);
        Assert.Equal(2, Runs(p).Length);
    }

    [Fact]
    public void InsertAfterANonTextPieceAddsTextInTheSameRun()
    {
        var run = new Run();
        run.Properties.SetFlag(W + "i", true);
        run.Content.Add(new TabContent());
        var p = new Paragraph();
        p.Inlines.Add(run);

        Editor(Doc(p)).InsertText(Caret(p, 1), "x");
        Assert.Equal("\tx", Text(p));
        Assert.Single(Runs(p));
        Assert.IsType<TabContent>(run.Content[0]);
        Assert.Equal("x", ((TextContent)run.Content[1]).Text);
    }

    [Fact]
    public void InsertIntoAnEmptyParagraphUsesTheParagraphMarkFormatting()
    {
        var p = new Paragraph();
        p.Properties.Set(W + "rPr", new XElement(W + "rPr", new XElement(W + "b")));
        var editor = Editor(Doc(p));

        editor.InsertText(Caret(p, 0), "Hi");
        Assert.Equal("Hi", Text(p));
        Assert.True(Assert.Single(Runs(p)).Properties.Bold);
    }

    [Fact]
    public void EnterSplitsAndKeepsParagraphProperties()
    {
        var p = Styled("Heading1", "Hello world");
        var document = Doc(p);
        var editor = Editor(document);

        var result = editor.SplitParagraph(Caret(p, 5));

        Assert.Equal(["Hello", " world"], Texts(document));
        var second = (Paragraph)document.Body.Blocks[1];
        Assert.Equal("Heading1", StyleOf(p));
        Assert.Equal("Heading1", StyleOf(second));
        Assert.Equal(At(second, 0), result.Caret);
        Assert.Same(p, document.Body.Blocks[0]);
    }

    [Fact]
    public void EnterSplitsInsideARunAndKeepsRunProperties()
    {
        var p = new Paragraph();
        p.Inlines.Add(R("Boldtext", bold: true));
        var document = Doc(p);

        Editor(document).SplitParagraph(Caret(p, 4));

        var second = (Paragraph)document.Body.Blocks[1];
        Assert.Equal("Bold", Text(p));
        Assert.Equal("text", Text(second));
        Assert.True(Runs(p)[0].Properties.Bold);
        Assert.True(Runs(second)[0].Properties.Bold);
    }

    [Fact]
    public void EnterInsideAHyperlinkSplitsTheLinkInTwo()
    {
        var link = new Hyperlink { RelationshipId = "rId9" };
        link.Inlines.Add(R("abcdef"));
        var p = new Paragraph();
        p.Inlines.Add(R("go "));
        p.Inlines.Add(link);
        var document = Doc(p);

        Editor(document).SplitParagraph(Caret(p, 6));

        var second = (Paragraph)document.Body.Blocks[1];
        Assert.Equal("go abc", Text(p));
        Assert.Equal("def", Text(second));
        Assert.Equal("rId9", Assert.IsType<Hyperlink>(second.Inlines[0]).RelationshipId);
        Assert.Equal("rId9", Assert.IsType<Hyperlink>(p.Inlines[1]).RelationshipId);
    }

    [Fact]
    public void EnterMovesTheSectionBreakToTheSecondParagraph()
    {
        var p = P("Hello");
        p.SectionBreak = new SectionProperties();
        var document = Doc(p);

        Editor(document).SplitParagraph(Caret(p, 2));

        var second = (Paragraph)document.Body.Blocks[1];
        Assert.Null(p.SectionBreak);
        Assert.NotNull(second.SectionBreak);
    }

    [Fact]
    public void BackspaceAtParagraphStartMergesWithTheFirstParagraphProperties()
    {
        var p1 = Styled("Heading1", "One");
        var p2 = Styled("Normal", "Two");
        var document = Doc(p1, p2);

        var result = Editor(document).Backspace(At(p2, 0), DeleteUnit.Cluster);

        Assert.NotNull(result);
        Assert.Equal(["OneTwo"], Texts(document));
        Assert.Same(p1, document.Body.Blocks[0]);
        Assert.Equal("Heading1", StyleOf(p1));
        Assert.Equal(At(p1, 3), result.Caret);
    }

    [Fact]
    public void BackspaceInAnEmptyFirstParagraphTakesTheSecondProperties()
    {
        var p1 = Styled("Heading1");
        var p2 = Styled("Normal", "Two");
        var document = Doc(p1, p2);

        var result = Editor(document).Backspace(At(p2, 0), DeleteUnit.Cluster);

        Assert.NotNull(result);
        Assert.Single(document.Body.Blocks);
        Assert.Same(p2, document.Body.Blocks[0]);
        Assert.Equal("Normal", StyleOf(p2));
        Assert.Equal("Two", Text(p2));
        Assert.Equal(At(p2, 0), result.Caret);
    }

    [Fact]
    public void BackspaceRemovesWholeEmojiAndCombiningCluster()
    {
        var thumbs = P("a👍🏽");
        var accent = P("aé");
        var document = Doc(thumbs, accent);
        var editor = Editor(document);

        var first = editor.Backspace(At(thumbs, Text(thumbs).Length), DeleteUnit.Cluster);
        Assert.Equal("a", Text(thumbs));
        Assert.Equal(At(thumbs, 1), first!.Caret);

        var second = editor.Backspace(At(accent, Text(accent).Length), DeleteUnit.Cluster);
        Assert.Equal("a", Text(accent));
        Assert.Equal(At(accent, 1), second!.Caret);

        var precomposed = P("é");
        var other = Doc(precomposed);
        Editor(other).Backspace(At(precomposed, 1), DeleteUnit.Cluster);
        Assert.Equal(string.Empty, Text(precomposed));
    }

    [Fact]
    public void DeleteForwardRemovesWholeClusters()
    {
        var p = P("👍🏽éx");
        var editor = Editor(Doc(p));

        editor.DeleteForward(At(p, 0), DeleteUnit.Cluster);
        Assert.Equal("éx", Text(p));
        editor.DeleteForward(At(p, 0), DeleteUnit.Cluster);
        Assert.Equal("x", Text(p));
    }

    [Fact]
    public void ClusterAcrossTwoRunsIsOneUnit()
    {
        var p = P("ae", "́b");
        var editor = Editor(Doc(p));

        editor.Backspace(At(p, 3), DeleteUnit.Cluster);
        Assert.Equal("ab", Text(p));
        editor.Backspace(At(p, 2), DeleteUnit.Cluster);
        Assert.Equal("a", Text(p));
    }

    [Fact]
    public void WordDeletionStopsAtWordBoundaries()
    {
        var p = P("alpha beta, gamma_1  delta");
        var editor = Editor(Doc(p));
        var length = Text(p).Length;

        // Backward: trailing blanks go with the word before them.
        editor.Backspace(At(p, length), DeleteUnit.Word);
        Assert.Equal("alpha beta, gamma_1  ", Text(p));
        editor.Backspace(At(p, Text(p).Length), DeleteUnit.Word);
        Assert.Equal("alpha beta, ", Text(p));
        editor.Backspace(At(p, Text(p).Length), DeleteUnit.Word);
        Assert.Equal("alpha beta", Text(p));
        editor.Backspace(At(p, Text(p).Length), DeleteUnit.Word);
        Assert.Equal("alpha ", Text(p));

        // Forward: the word and the blanks after it.
        var q = P("alpha beta, gamma");
        var forward = Editor(Doc(q));
        forward.DeleteForward(At(q, 0), DeleteUnit.Word);
        Assert.Equal("beta, gamma", Text(q));
        forward.DeleteForward(At(q, 0), DeleteUnit.Word);
        Assert.Equal(", gamma", Text(q));
        forward.DeleteForward(At(q, 0), DeleteUnit.Word);
        Assert.Equal("gamma", Text(q));
    }

    [Fact]
    public void WordDeletionAtAParagraphEdgeMergesLikeACluster()
    {
        var p1 = P("One");
        var p2 = P("Two");
        var document = Doc(p1, p2);

        Editor(document).Backspace(At(p2, 0), DeleteUnit.Word);
        Assert.Equal(["OneTwo"], Texts(document));
    }

    [Fact]
    public void DeleteForwardAtParagraphEndMergesTheNext()
    {
        var p1 = Styled("A", "One");
        var p2 = Styled("B", "Two");
        var document = Doc(p1, p2);

        var result = Editor(document).DeleteForward(At(p1, 3), DeleteUnit.Cluster);

        Assert.Equal(["OneTwo"], Texts(document));
        Assert.Equal("A", StyleOf(p1));
        Assert.Equal(At(p1, 3), result!.Caret);
    }

    [Fact]
    public void BackspaceAtTheStartOfTheStoryDoesNothing()
    {
        var p = P("One");
        var document = Doc(p);
        var before = document.Edits.Revision;

        Assert.Null(Editor(document).Backspace(At(p, 0), DeleteUnit.Cluster));
        Assert.Null(Editor(document).DeleteForward(At(p, 3), DeleteUnit.Cluster));
        Assert.Equal(before, document.Edits.Revision);
        Assert.Equal("One", Text(p));
    }

    [Fact]
    public void BackspaceAtStartOfCellDoesNothing()
    {
        var before = P("Before");
        var inCell = P("Cell");
        var table = CellTable(out _, inCell);
        var document = Doc(before, table);
        var revision = document.Edits.Revision;

        Assert.Null(Editor(document).Backspace(At(inCell, 0), DeleteUnit.Cluster));
        Assert.Equal("Cell", Text(inCell));
        Assert.Equal("Before", Text(before));
        Assert.Equal(revision, document.Edits.Revision);
    }

    [Fact]
    public void BackspaceInTheSecondParagraphOfACellMergesWithinTheCell()
    {
        var first = P("A");
        var second = P("B");
        var table = CellTable(out var cell, first, second);
        var document = Doc(table);

        var result = Editor(document).Backspace(At(second, 0), DeleteUnit.Cluster);

        Assert.NotNull(result);
        Assert.Single(cell.Blocks);
        Assert.Equal("AB", Text(first));
    }

    [Fact]
    public void BackspaceAfterATableKeepsTheTable()
    {
        var table = CellTable(out _, P("Cell"));
        var after = P("After");
        var document = Doc(table, after);

        Assert.Null(Editor(document).Backspace(At(after, 0), DeleteUnit.Cluster));
        Assert.Equal(2, document.Body.Blocks.Count);
        Assert.Same(table, document.Body.Blocks[0]);
        Assert.Equal("After", Text(after));
    }

    [Fact]
    public void DeleteForwardBeforeATableKeepsTheTable()
    {
        var before = P("Before");
        var table = CellTable(out _, P("Cell"));
        var document = Doc(before, table);

        Assert.Null(Editor(document).DeleteForward(At(before, 6), DeleteUnit.Cluster));
        Assert.Equal(2, document.Body.Blocks.Count);
    }

    [Fact]
    public void BackspaceInEmptyCellAndEmptyHeaderAndSingleEmptyParagraphDocumentDoNotThrow()
    {
        var emptyCell = new Paragraph();
        var table = CellTable(out _, emptyCell);
        var document = Doc(table, new Paragraph());
        var editor = Editor(document);

        Assert.Null(editor.Backspace(At(emptyCell, 0), DeleteUnit.Cluster));
        Assert.Null(editor.DeleteForward(At(emptyCell, 0), DeleteUnit.Word));

        var headerParagraph = new Paragraph();
        var header = new Story(new OoxmlRoot(W + "hdr", []));
        header.Blocks.Add(headerParagraph);
        document.HeadersFooters["rId5"] = new HeaderFooterPart(header);
        var inHeader = new TextPosition(new StoryRef(StoryKind.HeaderFooter, "rId5"), headerParagraph, 0);
        Assert.Null(editor.Backspace(inHeader, DeleteUnit.Cluster));
        Assert.Null(editor.DeleteForward(inHeader, DeleteUnit.Cluster));

        var lone = new Paragraph();
        var single = Doc(lone);
        Assert.Null(Editor(single).Backspace(At(lone, 0), DeleteUnit.Word));
        Assert.Null(Editor(single).DeleteForward(At(lone, 0), DeleteUnit.Cluster));
        Assert.Equal(string.Empty, Text(lone));
    }

    [Fact]
    public void TextInHeadersCanBeEdited()
    {
        var headerParagraph = P("Head");
        var header = new Story(new OoxmlRoot(W + "hdr", []));
        header.Blocks.Add(headerParagraph);
        var document = Doc(P("Body"));
        document.HeadersFooters["rId5"] = new HeaderFooterPart(header);
        var story = new StoryRef(StoryKind.HeaderFooter, "rId5");

        var result = Editor(document).InsertText(new TextRange(new TextPosition(story, headerParagraph, 4), new TextPosition(story, headerParagraph, 4)), "er");

        Assert.Equal("Header", Text(headerParagraph));
        Assert.Same(header.Blocks, result.Changes[0].Container);
        Assert.Equal(story, result.Changes[0].Story);
    }

    [Fact]
    public void FiveMegabytePlainTextInsertsWithinOneSecond()
    {
        var p = new Paragraph();
        var document = Doc(p);
        var editor = Editor(document);
        var line = new string('x', 79) + "\n";
        var text = string.Concat(Enumerable.Repeat(line, 65_536));
        Assert.True(text.Length > 5_000_000);

        var stopwatch = Stopwatch.StartNew();
        var result = editor.InsertText(Caret(p, 0), text);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"took {stopwatch.ElapsedMilliseconds} ms");
        Assert.Equal(65_537, document.Body.Blocks.Count);
        Assert.Single(result.Changes);
        Assert.Equal(65_537, result.Changes[0].Added);
        Assert.Equal(1, result.Changes[0].Removed);
        var last = (Paragraph)document.Body.Blocks[^1];
        Assert.Equal(At(last, 0), result.Caret);
        Assert.Equal(new string('x', 79), Text((Paragraph)document.Body.Blocks[1234]));
    }

    [Theory]
    [InlineData("a\r\nb", new[] { "a", "b" })]
    [InlineData("a\rb", new[] { "a", "b" })]
    [InlineData("a\nb", new[] { "a", "b" })]
    [InlineData("a\r\n\r\nb", new[] { "a", "", "b" })]
    [InlineData("a\n\rb", new[] { "a", "", "b" })]
    [InlineData("\r\n", new[] { "", "" })]
    public void CrLfCrAndLfSplitOnce(string text, string[] expected)
    {
        var p = new Paragraph();
        var document = Doc(p);

        Editor(document).InsertText(Caret(p, 0), text);

        Assert.Equal(expected, Texts(document));
    }

    [Fact]
    public void ControlCharactersAreDropped()
    {
        var p = P("ab");
        var editor = Editor(Doc(p));

        var result = editor.InsertText(Caret(p, 1), "x\u0000y\u0007z\u001bw\u007f\u0085");

        Assert.Equal("axyzwb", Text(p));
        Assert.Equal(At(p, 5), result.Caret);
    }

    [Fact]
    public void TabBecomesTabContent()
    {
        var p = P("ab");
        var editor = Editor(Doc(p));

        var result = editor.InsertText(Caret(p, 1), "x\ty");

        Assert.Equal("ax\tyb", Text(p));
        Assert.Equal(At(p, 4), result.Caret);
        var run = Runs(p).Single();
        Assert.Collection(
            run.Content,
            c => Assert.Equal("ax", ((TextContent)c).Text),
            c => Assert.IsType<TabContent>(c),
            c => Assert.Equal("yb", ((TextContent)c).Text));
    }

    [Fact]
    public void InsertingOnlyATabAtTheEndAddsATabContent()
    {
        var p = P("ab");
        Editor(Doc(p)).InsertText(Caret(p, 2), "\t");

        Assert.Equal("ab\t", Text(p));
        Assert.IsType<TabContent>(Runs(p).Single().Content[^1]);
    }

    [Fact]
    public void EveryOperationRaisesTheRevisionOnce()
    {
        var p1 = P("One two");
        var p2 = P("Three");
        var document = Doc(p1, p2);
        var editor = Editor(document);
        var revision = document.Edits.Revision;

        editor.InsertText(Caret(p1, 3), "x\ny");
        Assert.Equal(++revision, document.Edits.Revision);

        editor.InsertText(Sel(p1, 0, 1), "Z");
        Assert.Equal(++revision, document.Edits.Revision);

        editor.Delete(Sel(p1, 0, 1));
        Assert.Equal(++revision, document.Edits.Revision);

        editor.Backspace(At(p1, 1), DeleteUnit.Cluster);
        Assert.Equal(++revision, document.Edits.Revision);

        editor.DeleteForward(At(p1, 0), DeleteUnit.Word);
        Assert.Equal(++revision, document.Edits.Revision);

        editor.SplitParagraph(Caret(p2, 2));
        Assert.Equal(++revision, document.Edits.Revision);

        var second = (Paragraph)document.Body.Blocks.Last();
        editor.Backspace(At(second, 0), DeleteUnit.Cluster);
        Assert.Equal(++revision, document.Edits.Revision);

        // Nothing to do, nothing raised.
        editor.Delete(Caret(p2, 1));
        editor.InsertText(Caret(p2, 1), string.Empty);
        editor.InsertText(Caret(p2, 1), "\u0000");
        Assert.Equal(revision, document.Edits.Revision);
    }

    [Fact]
    public void ChangesDescribeTheReplacedBlocks()
    {
        var p1 = P("Hello");
        var p2 = P("World");
        var p3 = P("Tail");
        var document = Doc(p1, p2, p3);
        var editor = Editor(document);

        // One paragraph edited in place.
        var edit = editor.InsertText(Caret(p1, 5), "!");
        var change = Assert.Single(edit.Changes);
        Assert.Equal((0, 1, 1), (change.First, change.Removed, change.Added));
        Assert.Same(document.Body.Blocks, change.Container);
        Assert.Equal(StoryRef.Body, change.Story);
        var old = Assert.IsType<Paragraph>(Assert.Single(change.OldBlocks));
        Assert.NotSame(p1, old);
        Assert.Equal("Hello", Text(old));
        Assert.Equal("Hello!", Text(p1));

        // A split replaces one by two.
        var split = editor.SplitParagraph(Caret(p2, 2));
        change = Assert.Single(split.Changes);
        Assert.Equal((1, 1, 2), (change.First, change.Removed, change.Added));
        Assert.Equal("World", Text((Paragraph)Assert.Single(change.OldBlocks)));

        // A merge replaces two by one.
        var tail = (Paragraph)document.Body.Blocks[2];
        var merge = editor.Backspace(At(tail, 0), DeleteUnit.Cluster)!;
        change = Assert.Single(merge.Changes);
        Assert.Equal((1, 2, 1), (change.First, change.Removed, change.Added));
        Assert.Equal(["Wo", "rld"], change.OldBlocks.Select(b => Text((Paragraph)b)).ToArray());
        Assert.All(change.OldBlocks, b => Assert.DoesNotContain(b, document.Body.Blocks));
    }

    [Fact]
    public void ChangesInACellNameTheCellsBlocks()
    {
        var inCell = P("Cell");
        var table = CellTable(out var cell, inCell);
        var document = Doc(table);

        var result = Editor(document).InsertText(Caret(inCell, 4), "s");

        var change = Assert.Single(result.Changes);
        Assert.Same(cell.Blocks, change.Container);
        Assert.Equal((0, 1, 1), (change.First, change.Removed, change.Added));
    }

    [Fact]
    public void DeleteAcrossParagraphsMergesAndRemovesTheBlocksBetween()
    {
        var p1 = Styled("A", "Hello");
        var p2 = P("middle");
        var p3 = Styled("C", "World");
        var document = Doc(p1, p2, p3);

        var result = Editor(document).Delete(Sel(p1, 2, p3, 3));

        Assert.Equal(["Held"], Texts(document));
        Assert.Equal("A", StyleOf(p1));
        Assert.Equal(At(p1, 2), result.Caret);
        var change = Assert.Single(result.Changes);
        Assert.Equal((0, 3, 1), (change.First, change.Removed, change.Added));
    }

    [Fact]
    public void DeletingFromTheStartOfAParagraphIntoTheNextTakesTheSecondProperties()
    {
        var p1 = Styled("A", "Hello");
        var p2 = Styled("B", "World");
        var document = Doc(p1, p2);

        var result = Editor(document).Delete(Sel(p1, 0, p2, 2));

        Assert.Single(document.Body.Blocks);
        Assert.Same(p2, document.Body.Blocks[0]);
        Assert.Equal("B", StyleOf(p2));
        Assert.Equal("rld", Text(p2));
        Assert.Equal(At(p2, 0), result.Caret);
    }

    [Fact]
    public void ADeletedRangeAcrossATableInTheSameListRemovesTheTable()
    {
        var p1 = P("Hello");
        var table = CellTable(out _, P("Cell"));
        var p2 = P("World");
        var document = Doc(p1, table, p2);

        Editor(document).Delete(Sel(p1, 3, p2, 2));

        Assert.Single(document.Body.Blocks);
        Assert.Equal("Helrld", Text(p1));
    }

    [Fact]
    public void ASelectionThatLeavesItsCellOrStoryChangesNothing()
    {
        var before = P("Before");
        var inCell = P("Cell");
        var table = CellTable(out _, inCell);
        var document = Doc(before, table);
        var editor = Editor(document);
        var revision = document.Edits.Revision;

        var delete = editor.Delete(Sel(before, 2, inCell, 2));
        Assert.Empty(delete.Changes);
        var insert = editor.InsertText(Sel(before, 2, inCell, 2), "x");
        Assert.Empty(insert.Changes);
        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal("Before", Text(before));
        Assert.Equal("Cell", Text(inCell));

        var header = P("Head");
        var story = new Story(new OoxmlRoot(W + "hdr", []));
        story.Blocks.Add(header);
        document.HeadersFooters["rId1"] = new HeaderFooterPart(story);
        var other = new TextPosition(new StoryRef(StoryKind.HeaderFooter, "rId1"), header, 1);
        Assert.Empty(editor.Delete(new TextRange(At(before, 1), other)).Changes);
    }

    [Fact]
    public void InsertAtAComplexFieldBoundaryGoesBeforeOrAfterTheField()
    {
        var p = new Paragraph();
        p.Inlines.Add(R("a"));
        AddComplexField(p, "5");
        p.Inlines.Add(R("b"));
        var editor = Editor(Doc(p));

        // "a5b": offsets 1 and 2 are the field's boundaries; 1.x is not a caret position.
        editor.InsertText(Caret(p, 1), "<");
        Assert.Equal("a<5b", Text(p));
        editor.InsertText(Caret(p, 3), ">");
        Assert.Equal("a<5>b", Text(p));

        // A position inside the field (here a field result with two characters) snaps to its nearer edge.
        var q = new Paragraph();
        AddComplexField(q, "12");
        var inner = Editor(Doc(q));
        inner.InsertText(Caret(q, 1), "x");
        Assert.Equal("12x", Text(q));
    }

    [Fact]
    public void InsertBeforeAFieldAtTheParagraphStartKeepsTheFieldIntact()
    {
        var q = new Paragraph();
        AddComplexField(q, "12");
        Editor(Doc(q)).InsertText(Caret(q, 0), "x");

        Assert.Equal("x12", Text(q));
        Assert.Equal(6, q.Inlines.OfType<Run>().SelectMany(r => r.Content).Count(c => c is FieldChar or InstructionText or TextContent));
    }

    [Fact]
    public void DeletingAnyPartOfAFieldRemovesTheWholeField()
    {
        var p = new Paragraph();
        p.Inlines.Add(R("ab"));
        AddComplexField(p, "123");
        p.Inlines.Add(R("cd"));
        var document = Doc(p);

        var result = Editor(document).Delete(Sel(p, 3, 4));

        Assert.Equal("abcd", Text(p));
        Assert.Equal(At(p, 2), result.Caret);
        Assert.DoesNotContain(p.Inlines.OfType<Run>().SelectMany(r => r.Content), c => c is FieldChar or InstructionText);
    }

    [Fact]
    public void BackspaceAfterAFieldRemovesTheWholeField()
    {
        var p = new Paragraph();
        p.Inlines.Add(R("ab"));
        AddComplexField(p, "123");
        var editor = Editor(Doc(p));

        var result = editor.Backspace(At(p, 5), DeleteUnit.Cluster);

        Assert.Equal("ab", Text(p));
        Assert.Equal(At(p, 2), result!.Caret);
        Assert.DoesNotContain(p.Inlines.OfType<Run>().SelectMany(r => r.Content), c => c is FieldChar or InstructionText);
    }

    [Fact]
    public void DeletingPartOfASimpleFieldRemovesTheWholeFieldInline()
    {
        var field = new SimpleField(" PAGE ");
        field.Inlines.Add(R("7"));
        var p = new Paragraph();
        p.Inlines.Add(R("ab"));
        p.Inlines.Add(field);
        p.Inlines.Add(R("cd"));
        var editor = Editor(Doc(p));

        editor.Delete(Sel(p, 2, 3));

        Assert.Equal("abcd", Text(p));
        Assert.DoesNotContain(p.Inlines, i => i is SimpleField);
    }

    [Fact]
    public void InsertAfterASimpleFieldGoesAfterTheFieldInline()
    {
        var field = new SimpleField(" PAGE ");
        field.Inlines.Add(R("7"));
        var p = new Paragraph();
        p.Inlines.Add(R("a"));
        p.Inlines.Add(field);
        Editor(Doc(p)).InsertText(Caret(p, 2), "z");

        Assert.Equal("a7z", Text(p));
        Assert.IsType<Run>(p.Inlines[^1]);
    }

    [Fact]
    public void BackspaceRemovesAPictureAsOneUnit()
    {
        var drawing = new XElement(
            W + "drawing",
            new XElement(XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing") + "inline"));
        var run = new Run();
        run.Content.Add(new Picture(drawing));
        var p = new Paragraph();
        p.Inlines.Add(R("a"));
        p.Inlines.Add(run);
        var editor = Editor(Doc(p));

        editor.Backspace(At(p, 2), DeleteUnit.Cluster);

        Assert.Equal("a", Text(p));
        Assert.DoesNotContain(p.Inlines, i => ReferenceEquals(i, run));
    }

    [Fact]
    public void UnknownContentInsideADeletedRangeGoesAndAtTheEdgesStays()
    {
        var edge = new UnknownRunContent(new UnknownContent(new XElement(W + "softHyphen")));
        var inside = new UnknownRunContent(new UnknownContent(new XElement(W + "sym")));
        var tailEdge = new UnknownRunContent(new UnknownContent(new XElement(W + "lastRenderedPageBreak")));
        var run = new Run();
        run.Content.Add(new TextContent("ab"));
        run.Content.Add(edge);
        run.Content.Add(new TextContent("cd"));
        run.Content.Add(inside);
        run.Content.Add(new TextContent("ef"));
        run.Content.Add(tailEdge);
        run.Content.Add(new TextContent("gh"));
        var p = new Paragraph();
        p.Inlines.Add(run);
        var editor = Editor(Doc(p));

        // Remove "cdef" (offsets 2..6): "inside" is between two removed characters, the others touch the range from outside.
        editor.Delete(Sel(p, 2, 6));

        Assert.Equal("abgh", Text(p));
        Assert.Contains(edge, run.Content);
        Assert.DoesNotContain(inside, run.Content);
        Assert.Contains(tailEdge, run.Content);
    }

    [Fact]
    public void UnknownContentOutsideTheRangeIsNeverTouched()
    {
        var unknown = new UnknownRunContent(new UnknownContent(new XElement(W + "softHyphen")));
        var run = new Run();
        run.Content.Add(new TextContent("ab"));
        run.Content.Add(unknown);
        run.Content.Add(new TextContent("cd"));
        var p = new Paragraph();
        p.Inlines.Add(run);
        var editor = Editor(Doc(p));

        editor.Delete(Sel(p, 0, 1));
        editor.InsertText(Caret(p, 3), "x");

        Assert.Contains(unknown, run.Content);
        Assert.Equal("bcdx", Text(p));
    }

    [Fact]
    public void DeletingAWholeRunRemovesItAndKeepsTheOthers()
    {
        var p = P("one", "two", "three");
        var editor = Editor(Doc(p));

        editor.Delete(Sel(p, 3, 6));

        Assert.Equal("onethree", Text(p));
        Assert.Equal(2, Runs(p).Length);
    }

    [Fact]
    public void DeletingTheLastCharacterOfAHyperlinkRemovesTheEmptyLink()
    {
        var link = new Hyperlink { RelationshipId = "rId9" };
        link.Inlines.Add(R("x"));
        var p = new Paragraph();
        p.Inlines.Add(R("a"));
        p.Inlines.Add(link);
        p.Inlines.Add(R("b"));

        Editor(Doc(p)).Delete(Sel(p, 1, 2));

        Assert.Equal("ab", Text(p));
        Assert.DoesNotContain(p.Inlines, i => i is Hyperlink);
    }

    [Fact]
    public void TypingInsideATrackedInsertionStaysInsideItAndIsNotTracked()
    {
        var change = new TrackedChange(TrackedChangeKind.Insert, new RevisionInfo(1, "A", null));
        change.Inlines.Add(R("new"));
        var p = new Paragraph();
        p.Inlines.Add(R("old "));
        p.Inlines.Add(change);
        var document = Doc(p);

        Editor(document).InsertText(Caret(p, 5), "X");

        Assert.Equal("old nXew", Text(p));
        Assert.Equal(2, p.Inlines.Count);
        Assert.Equal("nXew", ((TextContent)((Run)change.Inlines[0]).Content[0]).Text);
    }

    [Fact]
    public void DeletedTrackedTextIsNotCountedAndNotTouched()
    {
        var deletion = new TrackedChange(TrackedChangeKind.Delete, new RevisionInfo(2, "A", null));
        var gone = new Run();
        gone.Content.Add(new DeletedText("zzz"));
        deletion.Inlines.Add(gone);
        var p = new Paragraph();
        p.Inlines.Add(R("ab"));
        p.Inlines.Add(deletion);
        p.Inlines.Add(R("cd"));

        Editor(Doc(p)).Delete(Sel(p, 1, 3));

        Assert.Equal("ad", Text(p));
        Assert.Contains(deletion, p.Inlines);
        Assert.Equal("zzz", ((DeletedText)gone.Content[0]).Text);
    }

    [Fact]
    public void ReplacingAnEmptiedParagraphKeepsEmptyRunFormattingOutOfTheWay()
    {
        var p = P("abc");
        var editor = Editor(Doc(p));

        editor.Delete(Sel(p, 0, 3));
        Assert.Equal(string.Empty, Text(p));
        editor.InsertText(Caret(p, 0), "new");
        Assert.Equal("new", Text(p));
    }

    [Fact]
    public void MultiLineInsertInTheMiddleSplitsTheSurroundingText()
    {
        var p = Styled("A", "HelloWorld");
        var document = Doc(p);

        var result = Editor(document).InsertText(Caret(p, 5), "1\n2\n3");

        Assert.Equal(["Hello1", "2", "3World"], Texts(document));
        Assert.All(document.Body.Blocks.OfType<Paragraph>(), b => Assert.Equal("A", StyleOf(b)));
        Assert.Equal(At((Paragraph)document.Body.Blocks[2], 1), result.Caret);
        var change = Assert.Single(result.Changes);
        Assert.Equal((0, 1, 3), (change.First, change.Removed, change.Added));
    }

    [Fact]
    public void InsertionAtParagraphEndAndStartOfSplitsKeepsRunFormatting()
    {
        var p = new Paragraph();
        p.Inlines.Add(R("Bold", bold: true));
        var document = Doc(p);

        Editor(document).InsertText(Caret(p, 4), "\nnext");

        var second = (Paragraph)document.Body.Blocks[1];
        Assert.Equal("next", Text(second));
        Assert.True(Runs(second)[0].Properties.Bold);
    }

    private static void AddComplexField(Paragraph paragraph, string result)
    {
        Run Of(params RunContent[] content)
        {
            var run = new Run();
            foreach (var c in content)
            {
                run.Content.Add(c);
            }

            return run;
        }

        paragraph.Inlines.Add(Of(new FieldChar(FieldCharKind.Begin)));
        paragraph.Inlines.Add(Of(new InstructionText(" PAGE ")));
        paragraph.Inlines.Add(Of(new FieldChar(FieldCharKind.Separate)));
        paragraph.Inlines.Add(Of(new TextContent(result)));
        paragraph.Inlines.Add(Of(new FieldChar(FieldCharKind.End)));
    }
}
