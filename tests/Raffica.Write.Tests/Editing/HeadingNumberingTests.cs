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
}
