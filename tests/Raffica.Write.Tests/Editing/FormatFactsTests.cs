using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>The facts that the rules of the formatting commands read from a real model (<see cref="TextEditor.ReadFormatFacts"/>) and their link to the operations.</summary>
public sealed class FormatFactsTests
{
    private const string Styles =
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>"
        + "<w:style w:type=\"paragraph\" w:styleId=\"Body\"><w:name w:val=\"Body\"/><w:basedOn w:val=\"Normal\"/></w:style>"
        + "<w:style w:type=\"paragraph\" w:styleId=\"Other\"><w:name w:val=\"Other\"/></w:style>";

    private const string NoDefaultStyles = "<w:style w:type=\"paragraph\" w:styleId=\"Body\"><w:name w:val=\"Body\"/></w:style>";

    private static string Thesis => Path.Combine(TestData.Root, "testdata", "write-corpus", "thesis", "thesis.docx");

    private static string Para(string? style, string? direct, string text = "text") =>
        "<w:p><w:pPr>" + (style is null ? string.Empty : $"<w:pStyle w:val=\"{style}\"/>") + (direct ?? string.Empty) + "</w:pPr><w:r><w:t>" + text + "</w:t></w:r></w:p>";

    private static string BoldRun(string paragraph) => paragraph.Replace("<w:r>", "<w:r><w:rPr><w:b/></w:rPr>", StringComparison.Ordinal);

    private static (WordDocument Document, TextEditor Editor) Setup(string body, string? styles = Styles)
    {
        var document = LayoutModel.Document(body, styles is null ? null : LayoutModel.StylesPart(styles));
        return (document, new TextEditor(document, (_, _) => ProjectionHost.Plain));
    }

    private static int Length(Paragraph p) => ParagraphProjection.Build(p, ProjectionHost.Plain).Text.Length;

    private static TextRange Whole(WordDocument document, int first, int last)
    {
        var a = (Paragraph)document.Body.Blocks[first];
        var b = (Paragraph)document.Body.Blocks[last];
        return new TextRange(new TextPosition(StoryRef.Body, a, 0), new TextPosition(StoryRef.Body, b, Length(b)));
    }

    [Fact]
    public void AFileWithoutAStylesPartHasNoStyleToChooseOrUpdate()
    {
        var (document, editor) = Setup(Para(null, "<w:jc w:val=\"center\"/>"), styles: null);
        var facts = editor.ReadFormatFacts(Whole(document, 0, 0));
        Assert.False(facts.HasStylesPart);
        Assert.False(facts.StyleDefined);
        Assert.False(facts.CanUpdateStyle);
        Assert.True(facts.HasOverrides);
    }

    [Fact]
    public void AParagraphWithoutAStyleUsesTheDefaultParagraphStyleOfTheFile()
    {
        var (document, editor) = Setup(Para(null, "<w:jc w:val=\"center\"/>"));
        var facts = editor.ReadFormatFacts(Whole(document, 0, 0));
        Assert.True(facts.HasStylesPart);
        Assert.True(facts.StyleDefined);
        Assert.False(facts.StyleMixed);
        Assert.True(facts.CanUpdateStyle);

        // A file with no default paragraph style leaves such a paragraph without a defined style.
        var (bare, bareEditor) = Setup(Para(null, "<w:jc w:val=\"center\"/>"), NoDefaultStyles);
        var none = bareEditor.ReadFormatFacts(Whole(bare, 0, 0));
        Assert.True(none.HasStylesPart);
        Assert.False(none.StyleDefined);
        Assert.False(none.CanUpdateStyle);
    }

    [Fact]
    public void AStyleThatTheFileDoesNotDefineIsNotDefined()
    {
        var (document, editor) = Setup(Para("Ghost", "<w:jc w:val=\"center\"/>"));
        var facts = editor.ReadFormatFacts(Whole(document, 0, 0));
        Assert.True(facts.HasStylesPart);
        Assert.False(facts.StyleDefined);
        Assert.False(facts.StyleMixed);
        Assert.False(facts.CanUpdateStyle);
        Assert.True(facts.HasOverrides);
    }

    [Fact]
    public void ParagraphsOfDifferentStylesAreMixed()
    {
        var (document, editor) = Setup(Para("Body", "<w:jc w:val=\"center\"/>") + Para("Other", null));
        var facts = editor.ReadFormatFacts(Whole(document, 0, 1));
        Assert.True(facts.StyleMixed);
        Assert.False(facts.StyleDefined);
        Assert.False(facts.CanUpdateStyle);

        // One paragraph of the same selection is not mixed.
        Assert.False(editor.ReadFormatFacts(Whole(document, 0, 0)).StyleMixed);
    }

    [Fact]
    public void DirectFormattingDecidesHasOverridesAndCanUpdateStyle()
    {
        var (document, editor) = Setup(Para("Body", "<w:jc w:val=\"center\"/>") + Para("Body", null) + BoldRun(Para("Body", null)));
        var direct = editor.ReadFormatFacts(Whole(document, 0, 0));
        Assert.True(direct.HasOverrides);
        Assert.True(direct.CanUpdateStyle);

        var plain = editor.ReadFormatFacts(Whole(document, 1, 1));
        Assert.False(plain.HasOverrides);
        Assert.False(plain.CanUpdateStyle);
        Assert.True(plain.StyleDefined);

        // A run override that is on every run counts too; a collapsed selection covers no run.
        var bold = editor.ReadFormatFacts(Whole(document, 2, 2));
        Assert.True(bold.HasOverrides);
        Assert.True(bold.CanUpdateStyle);
        var third = (Paragraph)document.Body.Blocks[2];
        var caret = new TextRange(new TextPosition(StoryRef.Body, third, 0), new TextPosition(StoryRef.Body, third, 0));
        Assert.False(editor.ReadFormatFacts(caret).HasOverrides);
    }

    [Fact]
    public void ASelectionBeyondTheBoundIsNotReadAndStaysUsable()
    {
        var mixed = string.Concat(Enumerable.Range(0, TextEditor.MaxFactBlocks + 1).Select(i => Para(i % 2 == 0 ? "Body" : "Other", null)));
        var (document, editor) = Setup(mixed);
        var facts = editor.ReadFormatFacts(Whole(document, 0, TextEditor.MaxFactBlocks));
        Assert.Equal(FormatFacts.Unknown, facts);

        // The facts say nothing of the mix, but the operation itself still refuses it.
        Assert.Null(editor.UpdateStyleToMatch(Whole(document, 0, TextEditor.MaxFactBlocks)));

        // At the bound the selection is read.
        Assert.True(editor.ReadFormatFacts(Whole(document, 0, TextEditor.MaxFactBlocks - 1)).StyleMixed);

        // The styles part is read for any size.
        var (bare, bareEditor) = Setup(mixed, styles: null);
        Assert.False(bareEditor.ReadFormatFacts(Whole(bare, 0, TextEditor.MaxFactBlocks)).HasStylesPart);
    }

    // The fixtures of the equivalence test: the style and override situations above, as (body, first paragraph, last paragraph).
    public static TheoryData<string, int, int> Fixtures => new()
    {
        { Para("Body", "<w:jc w:val=\"center\"/>"), 0, 0 },
        { Para("Body", null), 0, 0 },
        { Para(null, "<w:keepNext/>"), 0, 0 },
        { Para("Ghost", "<w:jc w:val=\"center\"/>"), 0, 0 },
        { Para("Body", "<w:jc w:val=\"center\"/>") + Para("Other", null), 0, 1 },
        { Para("Body", null) + Para("Body", "<w:jc w:val=\"right\"/>"), 0, 1 },
        { Para("Body", "<w:jc w:val=\"right\"/>") + Para("Body", null), 0, 1 },
        { BoldRun(Para("Body", null)), 0, 0 },
        { "<w:p><w:pPr><w:pStyle w:val=\"Body\"/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t>a</w:t></w:r><w:r><w:t>b</w:t></w:r></w:p>", 0, 0 },
        { "<w:p><w:pPr><w:pStyle w:val=\"Body\"/></w:pPr></w:p>", 0, 0 },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void CanUpdateStyleEqualsTheOperationChangingSomething(string body, int first, int last)
    {
        foreach (var styles in new[] { Styles, NoDefaultStyles, null })
        {
            var (document, editor) = Setup(body, styles);
            var range = Whole(document, first, last);
            var predicted = editor.CanUpdateStyleToMatch(range);
            var result = editor.UpdateStyleToMatch(range);
            Assert.Equal(predicted, result is { Changes.Count: > 0 });
            Assert.Equal(predicted, result?.Style is not null);
        }
    }

    [Fact]
    public void CanUpdateStyleEqualsTheOperationOnTheFirstParagraphsOfTheThesis()
    {
        var checkedCount = 0;
        using (var probe = DocxFile.Open(Thesis))
        {
            var indexes = probe.Model.Body.Blocks.Select((b, i) => (b, i)).Where(x => x.b is Paragraph).Select(x => x.i).Take(40).ToList();
            foreach (var i in indexes)
            {
                // A fresh read per paragraph: the operation changes the style, which the next paragraph would see.
                using var fresh = DocxFile.Open(Thesis);
                var editor = new TextEditor(fresh.Model, (_, _) => ProjectionHost.Plain);
                var paragraph = (Paragraph)fresh.Model.Body.Blocks[i];
                var range = new TextRange(new TextPosition(StoryRef.Body, paragraph, 0), new TextPosition(StoryRef.Body, paragraph, Length(paragraph)));
                var predicted = editor.CanUpdateStyleToMatch(range);
                Assert.Equal(predicted, editor.ReadFormatFacts(range).CanUpdateStyle);
                var result = editor.UpdateStyleToMatch(range);
                Assert.Equal(predicted, result is { Changes.Count: > 0 });
                checkedCount++;
            }
        }

        Assert.Equal(40, checkedCount);
    }
}
