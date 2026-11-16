using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Xunit;

namespace Raffica.Write.Tests.Editing;

public sealed class FormattingInspectorTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static (WordDocument Document, TextEditor Editor) Setup(string body)
    {
        var part = LayoutModel.StylesPart(
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>"
            + "<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:tblPr/></w:style>");
        var document = LayoutModel.Document(body, part);
        return (document, new TextEditor(document, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(document)));
    }

    private static string Run(string text, string props = "") =>
        $"<w:r>{(props.Length == 0 ? string.Empty : $"<w:rPr>{props}</w:rPr>")}<w:t>{text}</w:t></w:r>";

    private static string Para(string runs, string props = "") =>
        $"<w:p>{(props.Length == 0 ? string.Empty : $"<w:pPr>{props}</w:pPr>")}{runs}</w:p>";

    private static Paragraph P(WordDocument document, int index) => (Paragraph)document.Body.Blocks[index];

    private static TextRange Whole(WordDocument document, int first, int last) =>
        new(new TextPosition(StoryRef.Body, P(document, first), 0), new TextPosition(StoryRef.Body, P(document, last), ParagraphProjection.Build(P(document, last), ProjectionHost.Plain).Text.Length));

    private static string Describe(OverrideRow row) => $"{row.Scope}:{row.Element.LocalName}:{row.Presence}:{row.Value}";

    private static string[] Rows(InspectorView view) => [.. view.Rows.Select(Describe)];

    [Fact]
    public void ListsTheStylesAndEveryDirectRow()
    {
        var (document, _) = Setup(Para(
            Run("one", "<w:rStyle w:val=\"Strong\"/><w:b/><w:color w:val=\"FF0000\"/><w:sz w:val=\"28\"/>"),
            "<w:pStyle w:val=\"Heading1\"/><w:keepNext/><w:jc w:val=\"center\"/><w:ind w:left=\"720\" w:hanging=\"360\"/>"));

        var view = FormattingInspector.Read(document, Whole(document, 0, 0));

        Assert.Equal("Heading1", view.ParagraphStyleId);
        Assert.Equal("Strong", view.CharacterStyleId);
        Assert.False(view.ParagraphStyleMixed);
        Assert.False(view.CharacterStyleMixed);
        Assert.Equal(
            [
                "Paragraph:keepNext:All:",
                "Paragraph:jc:All:center",
                "Paragraph:ind:All:left=720 hanging=360",
                "Run:b:All:",
                "Run:color:All:FF0000",
                "Run:sz:All:28",
            ],
            Rows(view));
        Assert.All(view.Rows, r => Assert.Equal(W.NamespaceName, r.Element.NamespaceName));
    }

    [Fact]
    public void ExcludesTheStructuralElements()
    {
        var (document, _) = Setup(Para(
            "<w:r><w:rPr><w:rStyle w:val=\"Strong\"/><w:i/><w:rPrChange w:id=\"1\" w:author=\"a\"><w:rPr/></w:rPrChange></w:rPr><w:t>text</w:t></w:r>",
            "<w:pStyle w:val=\"Heading1\"/><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr><w:jc w:val=\"left\"/>"
            + "<w:rPr><w:b/></w:rPr><w:pPrChange w:id=\"2\" w:author=\"a\"><w:pPr/></w:pPrChange>"));

        var view = FormattingInspector.Read(document, Whole(document, 0, 0));

        // The mark's rPr of a paragraph with text has no row, and neither has any of the structural elements.
        Assert.Equal(["Paragraph:jc:All:left", "Run:i:All:"], Rows(view));
    }

    [Fact]
    public void ParagraphMarkRowsOnlyForAnEmptyParagraph()
    {
        var (document, _) = Setup(
            Para(string.Empty, "<w:jc w:val=\"right\"/><w:rPr><w:b/><w:color w:val=\"00FF00\"/><w:ins w:id=\"3\" w:author=\"a\"/></w:rPr>")
            + Para(Run("text"), "<w:rPr><w:i/></w:rPr>"));

        Assert.Equal(
            ["Paragraph:jc:All:right", "ParagraphMark:b:All:", "ParagraphMark:color:All:00FF00"],
            Rows(FormattingInspector.Read(document, Whole(document, 0, 0))));
        Assert.Equal([], Rows(FormattingInspector.Read(document, Whole(document, 1, 1))));

        // Over both: the mark rows count the empty paragraph only, so they are "all" of that one.
        Assert.Equal(
            ["Paragraph:jc:Partly:right", "ParagraphMark:b:All:", "ParagraphMark:color:All:00FF00"],
            Rows(FormattingInspector.Read(document, Whole(document, 0, 1))));
    }

    [Fact]
    public void PartlyAndMixedOverSeveralParagraphsAndRuns()
    {
        var (document, _) = Setup(
            Para(Run("a", "<w:b/><w:color w:val=\"111111\"/>") + Run("b", "<w:b/><w:color w:val=\"222222\"/>"), "<w:pStyle w:val=\"A\"/><w:keepNext/><w:jc w:val=\"left\"/>")
            + Para(Run("c", "<w:b/><w:i/>"), "<w:pStyle w:val=\"B\"/><w:jc w:val=\"right\"/>"));

        var view = FormattingInspector.Read(document, Whole(document, 0, 1));

        Assert.Equal(
            ["Paragraph:keepNext:Partly:", "Paragraph:jc:Mixed:left", "Run:b:All:", "Run:color:Partly:111111", "Run:i:Partly:"],
            Rows(view));
        Assert.True(view.ParagraphStyleMixed);
        Assert.Null(view.ParagraphStyleId);
        Assert.False(view.CharacterStyleMixed);

        var first = FormattingInspector.Read(document, Whole(document, 0, 0));
        Assert.Contains("Run:color:Mixed:111111", Rows(first));
        Assert.Equal("A", first.ParagraphStyleId);
    }

    [Fact]
    public void ACharacterStyleOnSomeRunsIsMixed()
    {
        var (document, _) = Setup(Para(Run("a", "<w:rStyle w:val=\"Strong\"/>") + Run("b")));

        var view = FormattingInspector.Read(document, Whole(document, 0, 0));

        Assert.True(view.CharacterStyleMixed);
        Assert.Null(view.CharacterStyleId);
    }

    [Fact]
    public void ARunOnlyPartlyCoveredCountsAndACollapsedCaretReadsNoRunRows()
    {
        var (document, _) = Setup(Para(Run("hello", "<w:b/>") + Run(" world", "<w:i/>"), "<w:jc w:val=\"center\"/>"));
        var paragraph = P(document, 0);

        var inFirst = FormattingInspector.Read(document, new TextRange(new TextPosition(StoryRef.Body, paragraph, 1), new TextPosition(StoryRef.Body, paragraph, 3)));
        Assert.Equal(["Paragraph:jc:All:center", "Run:b:All:"], Rows(inFirst));

        var across = FormattingInspector.Read(document, new TextRange(new TextPosition(StoryRef.Body, paragraph, 4), new TextPosition(StoryRef.Body, paragraph, 6)));
        Assert.Equal(["Paragraph:jc:All:center", "Run:b:Partly:", "Run:i:Partly:"], Rows(across));

        var caret = FormattingInspector.Read(document, new TextRange(new TextPosition(StoryRef.Body, paragraph, 2), new TextPosition(StoryRef.Body, paragraph, 2)));
        Assert.Equal(["Paragraph:jc:All:center"], Rows(caret));
    }

    [Fact]
    public void UnknownElementsGetARowNamedByTheElement()
    {
        var (document, _) = Setup(Para(
            Run("x", "<w:vanish/><w:lang w:val=\"de-DE\" w:eastAsia=\"ja-JP\"/><w14:ligatures xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" w14:val=\"all\"/>"),
            "<w:tabs><w:tab w:val=\"left\" w:pos=\"720\"/></w:tabs><w:shd w:val=\"clear\" w:fill=\"FFFF00\"/>"));

        var view = FormattingInspector.Read(document, Whole(document, 0, 0));

        Assert.Equal(
            ["Paragraph:tabs:All:", "Paragraph:shd:All:clear", "Run:vanish:All:", "Run:lang:All:de-DE", "Run:ligatures:All:val=all"],
            Rows(view));
        Assert.Equal(XName.Get("ligatures", "http://schemas.microsoft.com/office/word/2010/wordml"), view.Rows[^1].Element);
    }

    [Fact]
    public void ReadingChangesNothing()
    {
        var (document, editor) = Setup(Para(Run("hello", "<w:b/>") + Run(" world", "<w:i/>"), "<w:jc w:val=\"center\"/>") + Para(string.Empty, "<w:rPr><w:b/></w:rPr>"));
        var revision = document.Edits.Revision;
        var runs = P(document, 0).Inlines.Count;

        _ = FormattingInspector.Read(document, new TextRange(new TextPosition(StoryRef.Body, P(document, 0), 2), new TextPosition(StoryRef.Body, P(document, 1), 0)));
        _ = editor.Inspect(Whole(document, 0, 1));

        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(runs, P(document, 0).Inlines.Count);
        Assert.Equal(0, editor.History.UndoCount);
        Assert.Equal("hello", ((TextContent)((Run)P(document, 0).Inlines[0]).Content[0]).Text);
    }
}
