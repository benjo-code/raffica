using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Editing;

public sealed class TextUnitsTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static Paragraph P(string text)
    {
        var run = new Run();
        run.Content.Add(new TextContent(text));
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(run);
        return paragraph;
    }

    private static TextPosition At(Paragraph paragraph, int offset) => new(StoryRef.Body, paragraph, offset);

    private static ParagraphProjection Projection(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain);

    // "Hello, world  x": H0 e1 l2 l3 o4 ,5 ' '6 w7 o8 r9 l10 d11 ' '12 ' '13 x14. An offset is the boundary before the character; trailing means the pointer was on the later half of the
    // character before it, so that character is the one under the pointer.
    [Theory]
    [InlineData("Hello, world  x", 1, false, 0, 5)]
    [InlineData("Hello, world  x", 5, true, 0, 5)]
    [InlineData("Hello, world  x", 5, false, 5, 6)]
    [InlineData("Hello, world  x", 6, true, 5, 6)]
    [InlineData("Hello, world  x", 6, false, 6, 7)]
    [InlineData("Hello, world  x", 7, true, 6, 7)]
    [InlineData("Hello, world  x", 8, false, 7, 12)]
    [InlineData("Hello, world  x", 12, true, 7, 12)]
    [InlineData("Hello, world  x", 12, false, 12, 14)]
    [InlineData("Hello, world  x", 14, true, 12, 14)]
    [InlineData("Hello, world  x", 15, true, 14, 15)]
    [InlineData("Hello, world  x", 15, false, 14, 15)]
    [InlineData("Hello, world  x", 0, false, 0, 5)]
    [InlineData("Hello, world  x", 0, true, 0, 5)]
    public void AWordIsTheRunOfOneKindUnderThePointer(string text, int offset, bool trailing, int start, int end)
    {
        var paragraph = P(text);
        var range = TextUnits.WordAt(At(paragraph, offset), Projection(paragraph), trailing);
        Assert.Equal((start, end), (range.Start.Offset, range.End.Offset));
        Assert.Same(paragraph, range.Start.Paragraph);
    }

    [Fact]
    public void AnEmptyParagraphHasAnEmptyWordAndAParagraphIsItsWholeText()
    {
        var empty = new Paragraph();
        Assert.True(TextUnits.WordAt(At(empty, 0), Projection(empty), trailing: true).IsEmpty);
        var paragraph = P("two words");
        var whole = TextUnits.ParagraphAt(At(paragraph, 4), Projection(paragraph));
        Assert.Equal((0, 9), (whole.Start.Offset, whole.End.Offset));
    }

    [Fact]
    public void TheContainerOfAParagraphIsTheCellItIsInAndAHitInATableMapsToTheTable()
    {
        var before = P("before");
        var inCell = P("in the cell");
        var cell = new TableCell();
        cell.Blocks.Add(inCell);
        var row = new TableRow();
        row.Items.Add(cell);
        var table = new Table();
        table.Rows.Add(row);
        var after = P("after");
        var story = new Story(new OoxmlRoot(W + "document", []));
        story.Blocks.Add(before);
        story.Blocks.Add(table);
        story.Blocks.Add(after);
        var document = new WordDocument(story);

        Assert.Same(cell.Blocks, TextUnits.ContainerOf(story.Blocks, inCell));
        Assert.Same(story.Blocks, TextUnits.ContainerOf(story.Blocks, after));
        Assert.Null(TextUnits.ContainerOf(story.Blocks, P("elsewhere")));
        Assert.Equal(0, TextUnits.IndexUnder(story.Blocks, before));
        Assert.Equal(1, TextUnits.IndexUnder(story.Blocks, inCell));
        Assert.Equal(2, TextUnits.IndexUnder(story.Blocks, after));
        Assert.Equal(-1, TextUnits.IndexUnder(cell.Blocks, after));
        Assert.Same(story.Blocks, TextUnits.BlocksOf(document, StoryRef.Body));
        Assert.Null(TextUnits.BlocksOf(document, new StoryRef(StoryKind.HeaderFooter, "rIdNone")));
    }
}
