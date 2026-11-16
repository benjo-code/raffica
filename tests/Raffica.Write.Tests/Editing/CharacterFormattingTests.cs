using System.Xml.Linq;
using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Editing;

public sealed class CharacterFormattingTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static string Root => Path.Combine(TestData.Root, "testdata", "write-corpus");

    private static (WordDocument Document, TextEditor Editor) Setup(string body, string styles = "")
    {
        var part = LayoutModel.StylesPart(
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>"
            + "<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:tblPr/></w:style>" + styles);
        var document = LayoutModel.Document(body, part);
        return (document, new TextEditor(document, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(document)));
    }

    private static string Run(string text, string props = "", string attributes = "") =>
        $"<w:r{attributes}>{(props.Length == 0 ? string.Empty : $"<w:rPr>{props}</w:rPr>")}<w:t>{text}</w:t></w:r>";

    private static Paragraph P(WordDocument document, int index = 0) => (Paragraph)document.Body.Blocks[index];

    private static TextPosition At(Paragraph paragraph, int offset) => new(StoryRef.Body, paragraph, offset);

    private static TextRange Sel(Paragraph paragraph, int start, int end) => new(At(paragraph, start), At(paragraph, end));

    private static string Props(PropertyBag bag) =>
        string.Join(",", bag.Children.Select(c => c.Name.LocalName + (c.HasAttributes ? "(" + string.Join(" ", c.Attributes().Select(a => a.Name.LocalName + "=" + a.Value)) + ")" : string.Empty)));

    private static string Contents(Run run) =>
        string.Concat(run.Content.Select(c => c switch { TextContent t => t.Text, TabContent => "\t", _ => "<" + c.GetType().Name + ">" }));

    private static string Dump(Paragraph paragraph) => string.Concat(paragraph.Inlines.Select(DumpInline));

    private static string DumpInline(Inline inline) => inline switch
    {
        Run run => $"[{Props(run.Properties)}|{Contents(run)}]",
        _ => "{" + inline.GetType().Name + (ParagraphMap.ChildrenOf(inline) is { } children ? ":" + string.Concat(children.Select(DumpInline)) : string.Empty) + "}",
    };

    private static string Mark(Paragraph paragraph) =>
        paragraph.Properties.Get(W + "rPr") is { } mark ? string.Join(",", mark.Elements().Select(e => e.Name.LocalName + "=" + (string?)e.Attribute(W + "val"))) : string.Empty;

    [Fact]
    public void BoldOnAWordSplitsTheRunAndMergesItBack()
    {
        var (doc, editor) = Setup("<w:p>" + Run("abcd") + "</w:p>");
        editor.ToggleCharacter(Sel(P(doc), 1, 3), CharacterToggle.Bold);
        Assert.Equal("[|a][b|bc][|d]", Dump(P(doc)));

        editor.ToggleCharacter(Sel(P(doc), 1, 3), CharacterToggle.Bold);
        Assert.Equal("[|abcd]", Dump(P(doc)));
    }

    [Fact]
    public void ToggleOnAMixedSelectionTurnsEverythingOn()
    {
        var (doc, editor) = Setup("<w:p>" + Run("ab", "<w:b/>") + Run("cd") + "</w:p>");
        editor.ToggleCharacter(Sel(P(doc), 0, 4), CharacterToggle.Bold);
        Assert.Equal("[b|abcd]", Dump(P(doc)));
    }

    [Fact]
    public void ToggleOffAStyleValueWritesExplicitOff()
    {
        var (doc, editor) = Setup(
            "<w:p><w:pPr><w:pStyle w:val=\"Strong1\"/></w:pPr>" + Run("ab") + "</w:p>",
            "<w:style w:type=\"paragraph\" w:styleId=\"Strong1\"><w:rPr><w:b/></w:rPr></w:style>");
        editor.ToggleCharacter(Sel(P(doc), 0, 2), CharacterToggle.Bold);
        Assert.Equal("[b(val=0)|ab]", Dump(P(doc)));
    }

    [Fact]
    public void ToggleOffADirectValueRemovesTheElement()
    {
        var (doc, editor) = Setup("<w:p>" + Run("abcd", "<w:b/>") + "</w:p>");
        editor.ToggleCharacter(Sel(P(doc), 1, 3), CharacterToggle.Bold);
        Assert.Equal("[b|a][|bc][b|d]", Dump(P(doc)));
    }

    [Fact]
    public void SuperscriptAndSubscriptExcludeEachOther()
    {
        var (doc, editor) = Setup("<w:p>" + Run("ab") + "</w:p>");
        editor.ToggleCharacter(Sel(P(doc), 0, 2), CharacterToggle.Superscript);
        Assert.Equal("[vertAlign(val=superscript)|ab]", Dump(P(doc)));
        editor.ToggleCharacter(Sel(P(doc), 0, 2), CharacterToggle.Subscript);
        Assert.Equal("[vertAlign(val=subscript)|ab]", Dump(P(doc)));
        editor.ToggleCharacter(Sel(P(doc), 0, 2), CharacterToggle.Subscript);
        Assert.Equal("[|ab]", Dump(P(doc)));
    }

    [Fact]
    public void FontWritesAsciiAndHAnsiAndDropsThemeAttributes()
    {
        var (doc, editor) = Setup(
            "<w:p>" + Run("ab", "<w:rFonts w:ascii=\"A\" w:asciiTheme=\"majorHAnsi\" w:hAnsiTheme=\"majorHAnsi\" w:eastAsia=\"E\" w:cs=\"C\"/>") + "</w:p>");
        editor.ApplyCharacterChanges(Sel(P(doc), 0, 2), FormatChanges.Font("Z"));
        var fonts = ((Run)P(doc).Inlines[0]).Properties.Get(W + "rFonts")!;
        Assert.Equal("Z", (string?)fonts.Attribute(W + "ascii"));
        Assert.Equal("Z", (string?)fonts.Attribute(W + "hAnsi"));
        Assert.Equal("E", (string?)fonts.Attribute(W + "eastAsia"));
        Assert.Equal("C", (string?)fonts.Attribute(W + "cs"));
        Assert.Null(fonts.Attribute(W + "asciiTheme"));
        Assert.Null(fonts.Attribute(W + "hAnsiTheme"));
    }

    [Fact]
    public void ColorDropsThemeAttributes()
    {
        var (doc, editor) = Setup("<w:p>" + Run("ab", "<w:color w:val=\"000000\" w:themeColor=\"accent1\" w:themeShade=\"BF\" w:themeTint=\"80\"/>") + "</w:p>");
        editor.ApplyCharacterChanges(Sel(P(doc), 0, 2), FormatChanges.Color(0xFF8800));
        Assert.Equal("color(val=FF8800)", Props(((Run)P(doc).Inlines[0]).Properties));
    }

    [Fact]
    public void SizeWritesHalfPointsToBothAttributes()
    {
        var (doc, editor) = Setup("<w:p>" + Run("ab") + "</w:p>");
        editor.ApplyCharacterChanges(Sel(P(doc), 0, 2), FormatChanges.Size(10.5f));
        Assert.Equal("sz(val=21),szCs(val=21)", Props(((Run)P(doc).Inlines[0]).Properties));
    }

    [Fact]
    public void NoRunsAreMergedThatDifferInAttributesIgnoringRsid()
    {
        var (doc, _) = Setup("<w:p>" + Run("a", string.Empty, " w:rsidR=\"00000001\"") + Run("b", string.Empty, " w:rsidR=\"00000002\"") + Run("c", "<w:i/>") + "</w:p>");
        var runs = P(doc).Inlines.OfType<Run>().ToArray();
        Assert.True(RunMerger.CanMerge(runs[0], runs[1]));
        Assert.False(RunMerger.CanMerge(runs[1], runs[2]));
        runs[1].UnknownAttributes.Add(new XAttribute(W + "rsidP", "1"));
        Assert.True(RunMerger.CanMerge(runs[0], runs[1]));
        runs[1].UnknownAttributes.Add(new XAttribute("custom", "1"));
        Assert.False(RunMerger.CanMerge(runs[0], runs[1]));
    }

    [Fact]
    public void ARunWithUnknownContentIsNeverMerged()
    {
        var (doc, editor) = Setup("<w:p>" + Run("a") + "<w:r><w:sym w:font=\"Wingdings\" w:char=\"F0FC\"/><w:t>b</w:t></w:r></w:p>");
        var runs = P(doc).Inlines.OfType<Run>().ToArray();
        Assert.False(RunMerger.CanMerge(runs[0], runs[1]));
        editor.ToggleCharacter(Sel(P(doc), 0, 2), CharacterToggle.Bold);
        Assert.Equal(2, P(doc).Inlines.OfType<Run>().Count());
        Assert.All(P(doc).Inlines.OfType<Run>(), r => Assert.Equal("b", Props(r.Properties)));
    }

    [Fact]
    public void OnlyTouchedRunsAreMerged()
    {
        var (doc, editor) = Setup("<w:p>" + Run("a") + Run("b") + Run("c") + Run("d") + Run("e") + "</w:p>");
        editor.ToggleCharacter(Sel(P(doc), 2, 3), CharacterToggle.Bold);
        Assert.Equal("[|a][|b][b|c][|d][|e]", Dump(P(doc)));
        editor.ToggleCharacter(Sel(P(doc), 2, 3), CharacterToggle.Bold);
        Assert.Equal("[|a][|bcd][|e]", Dump(P(doc)));
    }

    [Fact]
    public void TypingFormatWinsOverTheNeighbourRun()
    {
        var (doc, editor) = Setup("<w:p>" + Run("ab", "<w:b/>") + "</w:p>");
        var off = new[] { new PropertyChange(W + "b", new XElement(W + "b", new XAttribute(W + "val", "0"))) };
        var result = editor.InsertText(Sel(P(doc), 2, 2), "X", off);
        Assert.Equal("[b|ab][b(val=0)|X]", Dump(P(doc)));
        Assert.Equal(3, result.Caret.Offset);
        Assert.Single(result.Changes);
    }

    [Fact]
    public void TypingFormatThatEqualsTheNeighbourMergesBack()
    {
        var (doc, editor) = Setup("<w:p>" + Run("ab", "<w:b/>") + "</w:p>");
        editor.InsertText(Sel(P(doc), 2, 2), "X", [new PropertyChange(W + "b", new XElement(W + "b"))]);
        Assert.Equal("[b|abX]", Dump(P(doc)));
    }

    [Fact]
    public void AnEmptyParagraphFormatsTheParagraphMark()
    {
        var (doc, editor) = Setup("<w:p/>");
        editor.ToggleCharacter(Sel(P(doc), 0, 0), CharacterToggle.Bold);
        Assert.Equal("b=", Mark(P(doc)));
        editor.ToggleCharacter(Sel(P(doc), 0, 0), CharacterToggle.Bold);
        Assert.Equal(string.Empty, Mark(P(doc)));
        editor.ApplyCharacterChanges(Sel(P(doc), 0, 0), FormatChanges.Size(14));
        Assert.Equal("sz=28,szCs=28", Mark(P(doc)));
    }

    [Fact]
    public void ACollapsedSelectionInTextChangesNothing()
    {
        var (doc, editor) = Setup("<w:p>" + Run("abcd") + "</w:p>");
        var revision = doc.Edits.Revision;
        var result = editor.ToggleCharacter(Sel(P(doc), 2, 2), CharacterToggle.Bold);
        Assert.Empty(result.Changes);
        Assert.Equal(revision, doc.Edits.Revision);
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void ToggleWithoutAResolverSaysSo()
    {
        var document = LayoutModel.Document("<w:p>" + Run("ab") + "</w:p>");
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain);
        var exception = Assert.Throws<InvalidOperationException>(() => editor.ToggleCharacter(Sel(P(document), 0, 2), CharacterToggle.Bold));
        Assert.Contains("IEffectiveFormat", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OneBlockChangePerOperationAndOneUndoStep()
    {
        var (doc, editor) = Setup("<w:p>" + Run("abcd") + "</w:p><w:p>" + Run("efgh") + "</w:p><w:p>" + Run("ijkl") + "</w:p>");
        string Both() => Dump(P(doc, 0)) + "/" + Dump(P(doc, 1)) + "/" + Dump(P(doc, 2));
        var before = Both();
        var result = editor.ToggleCharacter(new TextRange(At(P(doc, 0), 1), At(P(doc, 2), 2)), CharacterToggle.Bold);
        Assert.Single(result.Changes);
        Assert.Equal(3, result.Changes[0].Removed);
        Assert.Equal(3, result.Changes[0].Added);
        var after = Both();
        Assert.NotEqual(before, after);
        Assert.Equal(1, editor.History.UndoCount);

        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, Both());
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(after, Both());
    }

    [Fact]
    public void FormattingKeepsContainersFieldsAndUnknownContent()
    {
        var (doc, editor) = Setup(
            "<w:p>"
            + "<w:ins w:id=\"1\" w:author=\"a\" w:date=\"2020-01-01T00:00:00Z\"><w:r><w:t>ins</w:t></w:r></w:ins>"
            + "<w:hyperlink w:anchor=\"x\"><w:r><w:t>link</w:t></w:r></w:hyperlink>"
            + "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> PAGE </w:instrText></w:r>"
            + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>7</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>"
            + "<w:r><w:sym w:font=\"Wingdings\" w:char=\"F0FC\"/></w:r>"
            + "<w:r><w:t>tail</w:t></w:r></w:p>");
        var paragraph = P(doc);
        var length = ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text.Length;
        var instruction = string.Concat(Flatten(paragraph).SelectMany(r => r.Content).OfType<InstructionText>().Select(i => i.Text));

        editor.ToggleCharacter(Sel(paragraph, 0, length), CharacterToggle.Italic);

        Assert.Single(paragraph.Inlines.OfType<TrackedChange>());
        Assert.Single(paragraph.Inlines.OfType<Hyperlink>());
        Assert.Equal(instruction, string.Concat(Flatten(paragraph).SelectMany(r => r.Content).OfType<InstructionText>().Select(i => i.Text)));
        Assert.Contains(Flatten(paragraph).SelectMany(r => r.Content), c => c is UnknownRunContent);
        Assert.Equal(length, ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text.Length);
        Assert.True(Flatten(paragraph).Where(r => r.Content.Any(c => c is TextContent)).All(r => r.Properties.GetFlag(W + "i")));
        Assert.Equal(1, editor.History.UndoCount);
    }

    [Theory]
    [InlineData("poi-tika-792")]
    [InlineData("poi-test-document")]
    [InlineData("poi-fld-simple")]
    public async Task FormattingACorpusFileKeepsItsStructureThroughSaveAndReopen(string id)
    {
        var entry = CorpusManifest.Load(Root).Files.Single(e => e.Id == id);
        var path = CorpusCheck.Resolve(Root, entry.Path);
        Assert.NotNull(path);
        using var folder = new TempFolder();
        var saved = folder.File("saved.docx");
        string[] texts;
        int[] shape;
        using (var file = DocxFile.Open(path))
        {
            var editor = new TextEditor(file.Model, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(file.Model));
            var paragraphs = file.Model.Body.Blocks.OfType<Paragraph>().ToList();
            texts = [.. paragraphs.Select(p => ParagraphProjection.Build(p, ProjectionHost.Plain).Text)];
            shape = Shape(paragraphs);
            var last = paragraphs[^1];
            var result = editor.ToggleCharacter(new TextRange(At(paragraphs[0], 0), At(last, texts[^1].Length)), CharacterToggle.Underline);
            Assert.Single(result.Changes);
            Assert.Equal(texts, file.Model.Body.Blocks.OfType<Paragraph>().Select(p => ParagraphProjection.Build(p, ProjectionHost.Plain).Text));
            Assert.Equal(shape, Shape(file.Model.Body.Blocks.OfType<Paragraph>()));
            await file.SaveAsync(saved, TestContext.Current.CancellationToken);
        }

        using var reopened = DocxFile.Open(saved);
        var again = reopened.Model.Body.Blocks.OfType<Paragraph>().ToList();
        Assert.Equal(texts, again.Select(p => ParagraphProjection.Build(p, ProjectionHost.Plain).Text));
        Assert.Equal(shape, Shape(again));
    }

    [Fact]
    public void ASelectionAcrossStoriesOrACellEdgeChangesNothing()
    {
        var (doc, editor) = Setup(
            "<w:p>" + Run("abcd") + "</w:p><w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"3000\"/></w:tblGrid><w:tr><w:tc><w:p>" + Run("cell") + "</w:p></w:tc></w:tr></w:tbl>");
        var cell = (Paragraph)((TableCell)((TableRow)((Table)doc.Body.Blocks[1]).Rows[0]).Items[0]).Blocks[0];
        var revision = doc.Edits.Revision;
        var before = Dump(P(doc)) + Dump(cell);
        var result = editor.ToggleCharacter(new TextRange(At(P(doc), 1), At(cell, 2)), CharacterToggle.Bold);
        Assert.Empty(result.Changes);

        var other = editor.ToggleCharacter(new TextRange(At(P(doc), 1), new TextPosition(new StoryRef(StoryKind.HeaderFooter, "h1"), cell, 2)), CharacterToggle.Bold);
        Assert.Empty(other.Changes);
        Assert.Equal(revision, doc.Edits.Revision);
        Assert.Equal(before, Dump(P(doc)) + Dump(cell));
    }

    private static IEnumerable<Run> Flatten(Paragraph paragraph) => Flatten(paragraph.Inlines);

    private static IEnumerable<Run> Flatten(IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run)
            {
                yield return run;
            }
            else if (ParagraphMap.ChildrenOf(inline) is { } children)
            {
                foreach (var nested in Flatten(children))
                {
                    yield return nested;
                }
            }
        }
    }

    // Per paragraph: how many inlines of each kind other than runs it holds (links, fields, tracked changes, anchors, unknown), in one number.
    private static int[] Shape(IEnumerable<Paragraph> paragraphs) =>
        [.. paragraphs.Select(p => CountNonRuns(p.Inlines))];

    private static int CountNonRuns(IEnumerable<Inline> inlines) =>
        inlines.Sum(i => i is Run ? 0 : 1 + (ParagraphMap.ChildrenOf(i) is { } children ? CountNonRuns(children) : 0));
}
