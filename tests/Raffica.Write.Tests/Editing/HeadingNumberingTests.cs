using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Xunit;

namespace Raffica.Write.Tests.Editing;

public sealed class HeadingNumberingTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static string HeadingStyles(Func<int, string>? inner = null)
    {
        var text = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>";
        for (var i = 1; i <= 9; i++)
        {
            text += $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:pPr>{inner?.Invoke(i)}<w:outlineLvl w:val=\"{i - 1}\"/></w:pPr></w:style>";
        }

        return LayoutModel.StylesPart(text);
    }

    private static string Body(params (string Style, string Text)[] items) =>
        string.Concat(items.Select(i => $"<w:p><w:pPr><w:pStyle w:val=\"{i.Style}\"/></w:pPr><w:r><w:t>{i.Text}</w:t></w:r></w:p>"));

    private static WordDocument NewDocument(string? numbering = null, Func<int, string>? inner = null, string? body = null) =>
        LayoutModel.Document(body ?? Body(("Heading1", "A"), ("Heading2", "B"), ("Heading1", "C")), HeadingStyles(inner), numbering);

    private static TextEditor EditorOf(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static Style Heading(WordDocument document, int level) => document.Styles!.Find("Heading" + level)!;

    private static int? NumIdOf(WordDocument document, int level) => Heading(document, level).ParagraphProperties.NumberingId;

    private static string? LevelLink(AbstractNumbering definition, int level)
    {
        var item = definition.Levels.First(l => l.Level == level);
        foreach (var kept in item.KeptChildren)
        {
            if (kept.Content.Element.Name == W + "pStyle")
            {
                return (string?)kept.Content.Element.Attribute(W + "val");
            }
        }

        return null;
    }

    [Fact]
    public void SwitchingOnNumbersTheNineHeadingStylesAndLinksTheLevels()
    {
        var document = NewDocument();
        Assert.Equal(HeadingNumberingState.Off, HeadingNumbering.StateOf(document));

        var result = EditorOf(document).SetHeadingNumbering(true);

        Assert.NotNull(result);
        Assert.Empty(result.Changes);
        Assert.NotNull(result.Style);
        Assert.Equal(8, result.Style.Companions.Count);
        Assert.Equal(HeadingNumberingState.On, HeadingNumbering.StateOf(document));
        var numId = NumIdOf(document, 1)!.Value;
        Assert.True(numId > 0);
        for (var level = 1; level <= 9; level++)
        {
            Assert.Equal(numId, NumIdOf(document, level));
            var numPr = Heading(document, level).ParagraphProperties.Get(W + "numPr")!;
            var ilvl = numPr.Element(W + "ilvl");
            if (level == 1)
            {
                Assert.True(ilvl is null || (string?)ilvl.Attribute(W + "val") == "0");
            }
            else
            {
                Assert.Equal((level - 1).ToString(CultureInfo.InvariantCulture), (string?)ilvl?.Attribute(W + "val"));
            }
        }

        var definition = document.Numbering!.FindAbstract(document.Numbering.FindInstance(numId)!.AbstractNumId!.Value)!;
        for (var level = 0; level < 9; level++)
        {
            Assert.Equal("Heading" + (level + 1), LevelLink(definition, level));
        }
    }

    [Fact]
    public void AnUnlinkedMultilevelDefinitionGetsTheLinksWhenChosen()
    {
        var levels = string.Concat(Enumerable.Range(0, 9).Select(l =>
            $"<w:lvl w:ilvl=\"{l}\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"{string.Concat(Enumerable.Range(1, l + 1).Select(k => "%" + k + "."))}\"/></w:lvl>"));
        var numbering = LayoutModel.NumberingPart(
            "<w:abstractNum w:abstractNumId=\"4\"><w:multiLevelType w:val=\"multilevel\"/>" + levels + "</w:abstractNum><w:num w:numId=\"3\"><w:abstractNumId w:val=\"4\"/></w:num>");
        var document = NewDocument(numbering);

        Assert.NotNull(EditorOf(document).SetHeadingNumbering(true));

        Assert.Equal(3, NumIdOf(document, 1));
        Assert.Single(document.Numbering!.Abstracts);
        Assert.Single(document.Numbering.Instances);
        Assert.Equal("Heading9", LevelLink(document.Numbering.Abstracts[0], 8));
    }

    [Fact]
    public void SwitchingOffRemovesOnlyTheStyleLinks()
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        editor.SetHeadingNumbering(true);
        var abstracts = document.Numbering!.Abstracts.Count;
        var instances = document.Numbering.Instances.Count;

        var result = editor.SetHeadingNumbering(false);

        Assert.NotNull(result);
        Assert.Empty(result.Changes);
        Assert.Equal(HeadingNumberingState.Off, HeadingNumbering.StateOf(document));
        for (var level = 1; level <= 9; level++)
        {
            Assert.Null(Heading(document, level).ParagraphProperties.Get(W + "numPr"));
            Assert.NotNull(Heading(document, level).ParagraphProperties.Get(W + "outlineLvl"));
        }

        Assert.Equal(abstracts, document.Numbering.Abstracts.Count);
        Assert.Equal(instances, document.Numbering.Instances.Count);
        Assert.Null(editor.SetHeadingNumbering(false));
    }

    [Fact]
    public void StylesThatAlreadyCarryAListAreOnAndWriteNothing()
    {
        var numbering = LayoutModel.NumberingPart(
            "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>");
        var document = NewDocument(numbering, i => $"<w:numPr><w:ilvl w:val=\"{i - 1}\"/><w:numId w:val=\"1\"/></w:numPr>");
        var editor = EditorOf(document);

        Assert.Equal(HeadingNumberingState.On, HeadingNumbering.StateOf(document));
        Assert.Null(editor.SetHeadingNumbering(true));
        Assert.Single(document.Numbering!.Abstracts);
        Assert.Single(document.Numbering.Instances);
        Assert.Equal(0, editor.History.UndoCount);
        Assert.False(document.Edits.IsDirty);
    }

    [Fact]
    public void DirectNumPrOnAHeadingIsNeverTouched()
    {
        var body = "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"0\"/></w:numPr></w:pPr><w:r><w:t>A</w:t></w:r></w:p>";
        var document = NewDocument(body: body);
        var paragraph = (Paragraph)document.Body.Blocks[0];
        var before = new XElement(paragraph.Properties.Get(W + "numPr")!);
        var editor = EditorOf(document);

        editor.SetHeadingNumbering(true);
        Assert.True(XNode.DeepEquals(before, paragraph.Properties.Get(W + "numPr")));
        editor.SetHeadingNumbering(false);
        Assert.True(XNode.DeepEquals(before, paragraph.Properties.Get(W + "numPr")));
    }

    [Fact]
    public void AFileWithoutHeadingStylesIsUnavailable()
    {
        var document = LayoutModel.Document(
            "<w:p><w:r><w:t>x</w:t></w:r></w:p>",
            LayoutModel.StylesPart("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style><w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/></w:style>"));
        Assert.Equal(HeadingNumberingState.Unavailable, HeadingNumbering.StateOf(document));
        Assert.Null(EditorOf(document).SetHeadingNumbering(true));

        var noStyles = LayoutModel.Document("<w:p><w:r><w:t>x</w:t></w:r></w:p>");
        Assert.Equal(HeadingNumberingState.Unavailable, HeadingNumbering.StateOf(noStyles));
        Assert.Null(EditorOf(noStyles).SetHeadingNumbering(true));
        Assert.Null(noStyles.Numbering);
    }

    [Fact]
    public void OneUndoStepRestoresAllNineStyles()
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        editor.SetHeadingNumbering(true);
        Assert.Equal(1, editor.History.UndoCount);

        var undone = editor.History.Undo();

        Assert.NotNull(undone?.Style);
        Assert.Equal(HeadingNumberingState.Off, HeadingNumbering.StateOf(document));
        for (var level = 1; level <= 9; level++)
        {
            Assert.Null(Heading(document, level).ParagraphProperties.Get(W + "numPr"));
        }

        Assert.False(editor.History.CanUndo);
        Assert.False(document.Edits.IsDirty);
    }

    [Fact]
    public void RedoFindsTheDefinitionUndoLeft()
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        editor.SetHeadingNumbering(true);
        var numId = NumIdOf(document, 1)!.Value;
        editor.History.Undo();
        Assert.NotNull(document.Numbering!.FindInstance(numId));

        var redone = editor.History.Redo();

        Assert.NotNull(redone?.Style);
        Assert.Equal(HeadingNumberingState.On, HeadingNumbering.StateOf(document));
        Assert.Equal(numId, NumIdOf(document, 9));
        Assert.NotNull(document.Numbering.FindInstance(numId));
        Assert.Single(document.Numbering.Instances);
    }

    [Fact]
    public void ASwitchOnAfterUndoReusesTheDefinition()
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        editor.SetHeadingNumbering(true);
        var numId = NumIdOf(document, 1)!.Value;
        editor.History.Undo();

        editor.SetHeadingNumbering(true);

        Assert.Equal(numId, NumIdOf(document, 1));
        Assert.Single(document.Numbering!.Instances);
    }

    [Fact]
    public void TheStepNeverCoalescesWithTyping()
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        var paragraph = (Paragraph)document.Body.Blocks[0];
        var at = new TextPosition(StoryRef.Body, paragraph, 1);
        editor.InsertText(new TextRange(at, at), "x");
        Assert.Equal(1, editor.History.UndoCount);

        editor.SetHeadingNumbering(true);
        var typed = (Paragraph)document.Body.Blocks[0];
        var end = new TextPosition(StoryRef.Body, typed, 2);
        editor.InsertText(new TextRange(end, end), "y");

        Assert.Equal(3, editor.History.UndoCount);
        editor.History.Undo();
        editor.History.Undo();
        Assert.Equal(HeadingNumberingState.Off, HeadingNumbering.StateOf(document));
        Assert.Equal(1, editor.History.UndoCount);
    }

    [Fact]
    public void HistoryBytesCountEveryCompanion()
    {
        var big = new string('f', 2000);
        var document = NewDocument();
        foreach (var style in document.Styles!.Styles.Where(s => s.Id.StartsWith("Heading", StringComparison.Ordinal)))
        {
            style.RunProperties.Set(W + "rFonts", new XElement(W + "rFonts", new XAttribute(W + "ascii", big)));
        }

        var editor = EditorOf(document);
        editor.SetHeadingNumbering(true);

        Assert.True(editor.History.UndoBytes >= 9 * 2 * 2000, "bytes " + editor.History.UndoBytes);
    }

    [Fact]
    public void ADocumentWithoutANumberingPartGetsOne()
    {
        var document = NewDocument();
        Assert.Null(document.Numbering);
        var editor = EditorOf(document);

        editor.SetHeadingNumbering(true);

        Assert.NotNull(document.Numbering);
        Assert.True(document.Edits.IsDirty);
    }

    private const string Table =
        "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"2000\"/></w:tblGrid><w:tr><w:tc><w:tcPr/><w:p><w:r><w:t>Cell one</w:t></w:r></w:p><w:p><w:r><w:t>Cell target</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";

    private const string Control =
        "<w:sdt><w:sdtPr/><w:sdtContent><w:p><w:r><w:t>Control one</w:t></w:r></w:p><w:p><w:r><w:t>Control target</w:t></w:r></w:p></w:sdtContent></w:sdt>";

    private static string Plain(string text) => $"<w:p><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static Paragraph Find(WordDocument document, string text)
    {
        foreach (var story in new[] { document.Body.Blocks }.Concat(document.HeadersFooters.Values.Select(p => p.Content.Blocks)))
        {
            if (Walk(story).FirstOrDefault(p => p.Inlines.OfType<Run>().Any(r => r.Content.OfType<TextContent>().Any(t => t.Text == text))) is { } found)
            {
                return found;
            }
        }

        throw new InvalidOperationException("no paragraph " + text);
    }

    private static IEnumerable<Paragraph> Walk(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph p:
                    yield return p;
                    break;
                case Table t:
                    foreach (var row in t.Rows.OfType<TableRow>())
                    {
                        foreach (var cell in row.Items.OfType<TableCell>())
                        {
                            foreach (var p in Walk(cell.Blocks))
                            {
                                yield return p;
                            }
                        }
                    }

                    break;
                case ContentControlBlock c:
                    foreach (var p in Walk(c.Content))
                    {
                        yield return p;
                    }

                    break;
            }
        }
    }

    private static void AssertCaretStaysAfterUndoAndRedo(WordDocument document, StoryRef story, Paragraph target, int offset)
    {
        var editor = EditorOf(document);
        var caret = new TextPosition(story, target, offset);
        var length = ParagraphProjection.Build(target, ProjectionHost.Plain).Text.Length;
        Assert.InRange(offset, 0, length);

        Assert.NotNull(editor.SetHeadingNumbering(true, caret));
        var undone = editor.History.Undo();
        Assert.NotNull(undone);
        Assert.Same(target, undone.Caret.Paragraph);
        Assert.Equal(story, undone.Caret.Story);
        Assert.Equal(offset, undone.Caret.Offset);

        var redone = editor.History.Redo();
        Assert.NotNull(redone);
        Assert.Same(target, redone.Caret.Paragraph);
        Assert.Equal(story, redone.Caret.Story);
        Assert.Equal(offset, redone.Caret.Offset);
        Assert.Equal(HeadingNumberingState.On, HeadingNumbering.StateOf(document));
    }

    [Fact]
    public void ACaretInATableCellComesBackToTheSameParagraphOnUndoAndRedo()
    {
        var document = NewDocument(body: Plain("First paragraph of the body, long enough") + Table);
        AssertCaretStaysAfterUndoAndRedo(document, StoryRef.Body, Find(document, "Cell target"), 6);
    }

    [Fact]
    public void ACaretInABlockContentControlComesBackToTheSameParagraphOnUndoAndRedo()
    {
        var document = NewDocument(body: Plain("First paragraph of the body, long enough") + Control);
        AssertCaretStaysAfterUndoAndRedo(document, StoryRef.Body, Find(document, "Control target"), 7);
    }

    [Fact]
    public void ACaretInAHeaderComesBackToTheSameParagraphOnUndoAndRedo()
    {
        var document = NewDocument();
        var header = new Story(new OoxmlRoot(W + "hdr", []));
        var first = new Paragraph();
        first.Inlines.Add(new Run { Content = { new TextContent("Header one is rather long") } });
        var target = new Paragraph();
        target.Inlines.Add(new Run { Content = { new TextContent("Header target") } });
        header.Blocks.Add(first);
        header.Blocks.Add(target);
        document.HeadersFooters["h1"] = new HeaderFooterPart(header);

        AssertCaretStaysAfterUndoAndRedo(document, new StoryRef(StoryKind.HeaderFooter, "h1"), target, 4);
    }

    [Fact]
    public void WithoutACaretABodyThatStartsWithATableStillSwitches()
    {
        var document = NewDocument(body: Table + Plain("after"));
        var editor = EditorOf(document);

        var result = editor.SetHeadingNumbering(true);

        Assert.NotNull(result);
        Assert.Same(Find(document, "Cell one"), result.Caret.Paragraph);
        Assert.NotNull(editor.History.Undo());
    }

    [Fact]
    public void AMissingCompanionMakesUndoRefuseAndSwapNothing()
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        editor.SetHeadingNumbering(true);
        var before = Snapshot(document);
        document.Styles!.Styles.Remove(Heading(document, 5));
        var withoutFive = Snapshot(document);

        Assert.Null(editor.History.Undo());

        Assert.Equal(withoutFive, Snapshot(document));
        Assert.NotEqual(before, withoutFive);
        Assert.False(editor.History.CanUndo);
        Assert.False(editor.History.CanRedo);
    }

    [Fact]
    public void AMissingCompanionMakesRedoRefuseAndSwapNothing()
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        editor.SetHeadingNumbering(true);
        Assert.NotNull(editor.History.Undo());
        document.Styles!.Styles.Remove(Heading(document, 9));
        var before = Snapshot(document);

        Assert.Null(editor.History.Redo());

        Assert.Equal(before, Snapshot(document));
        Assert.False(editor.History.CanUndo);
        Assert.False(editor.History.CanRedo);
    }

    [Fact]
    public void RedoSwapsAllNineStylesAgain()
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        editor.SetHeadingNumbering(true);
        var on = Snapshot(document);
        editor.History.Undo();

        var redone = editor.History.Redo();

        Assert.Equal(8, redone!.Style!.Companions.Count);
        Assert.Equal(on, Snapshot(document));
    }

    [Fact]
    public void UndoRestoresEveryStyleExactly()
    {
        var document = NewDocument();
        var before = Snapshot(document);
        var editor = EditorOf(document);
        editor.SetHeadingNumbering(true);
        Assert.NotEqual(before, Snapshot(document));

        editor.History.Undo();

        Assert.Equal(before, Snapshot(document));
    }

    [Fact]
    public void TheWrittenStyleHasNumPrBeforeOutlineLevel()
    {
        var document = NewDocument();
        EditorOf(document).SetHeadingNumbering(true);

        using var stream = new MemoryStream();
        Raffica.Write.Docx.Xml.StylesXml.Write(document.Styles!, stream);
        var xml = XDocument.Parse(System.Text.Encoding.UTF8.GetString(stream.ToArray()));

        foreach (var style in xml.Descendants(W + "style").Where(s => ((string?)s.Attribute(W + "styleId"))?.StartsWith("Heading", StringComparison.Ordinal) == true))
        {
            var names = style.Element(W + "pPr")!.Elements().Select(e => e.Name.LocalName).ToList();
            Assert.True(names.IndexOf("numPr") >= 0 && names.IndexOf("numPr") < names.IndexOf("outlineLvl"), string.Join(",", names));
        }
    }

    // Every style as its serialized paragraph and run properties, in sheet order.
    private static List<string> Snapshot(WordDocument document) =>
        [.. document.Styles!.Styles.Select(s => s.Id + "|"
            + string.Concat(s.ParagraphProperties.Children.Select(c => c.ToString(SaveOptions.DisableFormatting))) + "|"
            + string.Concat(s.RunProperties.Children.Select(c => c.ToString(SaveOptions.DisableFormatting))))];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AStaleCaretThrowsBeforeAnythingChanges(bool on)
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        if (!on)
        {
            Assert.NotNull(editor.SetHeadingNumbering(true));
        }

        var stale = new TextPosition(StoryRef.Body, (Paragraph)LayoutModel.Document(Body(("Heading1", "gone"))).Body.Blocks[0], 0);
        var styles = Snapshot(document);
        var revision = document.Edits.Revision;
        var steps = editor.History.UndoCount;
        var numbering = document.Numbering?.Instances.Count ?? 0;

        Assert.Throws<StalePositionException>(() => editor.SetHeadingNumbering(on, stale));

        Assert.Equal(styles, Snapshot(document));
        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(steps, editor.History.UndoCount);
        Assert.Equal(numbering, document.Numbering?.Instances.Count ?? 0);
    }
}
