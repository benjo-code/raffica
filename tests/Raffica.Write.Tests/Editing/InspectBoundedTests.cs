using System.Text;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>The inspector read for the formatting pane is bounded: a selection over <see cref="TextEditor.MaxFactBlocks"/> blocks is not walked.</summary>
public sealed class InspectBoundedTests
{
    private static string Paragraphs(int count)
    {
        var body = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            body.Append("<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t>text</w:t></w:r></w:p>");
        }

        return body.ToString();
    }

    private static TextRange Range(WordDocument document, int first, int last) =>
        new(new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[first], 0), new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[last], 4));

    [Fact]
    public void ASelectionOverTheBoundIsNotRead()
    {
        var document = LayoutModel.Document(Paragraphs(TextEditor.MaxFactBlocks + 50), LayoutModel.StylesPart("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>"));
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain);

        Assert.Null(editor.InspectBounded(Range(document, 0, TextEditor.MaxFactBlocks + 49)));

        // At the bound and below it the read is the full one.
        var atBound = Range(document, 0, TextEditor.MaxFactBlocks - 1);
        var bounded = editor.InspectBounded(atBound);
        Assert.NotNull(bounded);
        Assert.Equal(editor.Inspect(atBound).Rows, bounded.Rows);
        Assert.Equal(2, bounded.Rows.Count);
    }
}
