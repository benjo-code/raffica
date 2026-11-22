using System.Xml.Linq;
using Raffica.Write.Model;
using Raffica.Write.Panes;
using Xunit;

namespace Raffica.WriteApp.Tests.Panes;

/// <summary>The subtree of a heading and the drop targets of the outline (pure logic over a model made by hand).</summary>
public sealed class OutlineTreeTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static Paragraph Heading(string text, int level)
    {
        var p = Body(text);
        p.Properties.Set(W + "outlineLvl", new XElement(W + "outlineLvl", new XAttribute(W + "val", level - 1)));
        return p;
    }

    private static Paragraph Body(string text)
    {
        var p = new Paragraph();
        var run = new Run();
        run.Content.Add(new TextContent(text));
        p.Inlines.Add(run);
        return p;
    }

    private static (WordDocument Document, IReadOnlyList<OutlineHeading> Headings) Read(params Block[] blocks)
    {
        var document = new WordDocument(new Story(new OoxmlRoot(W + "document", [])));
        foreach (var block in blocks)
        {
            document.Body.Blocks.Add(block);
        }

        return (document, OutlineSource.ReadHeadings(document));
    }

    [Fact]
    public void ASubtreeEndsAtTheNextHeadingOfTheSameOrAHigherLevel()
    {
        // 0 H1, 1 text, 2 H2, 3 text, 4 H3, 5 H2, 6 H1
        var (d, h) = Read(Heading("a", 1), Body("t"), Heading("b", 2), Body("t"), Heading("c", 3), Heading("d", 2), Heading("e", 1));
        Assert.Equal(new Subtree(0, 6, 5, MoveBlock.None), OutlineTree.Of(d, h, 0));
        Assert.Equal(new Subtree(2, 3, 4, MoveBlock.None), OutlineTree.Of(d, h, 1));
        Assert.Equal(new Subtree(4, 1, 4, MoveBlock.None), OutlineTree.Of(d, h, 2));
        Assert.Equal(new Subtree(5, 1, 5, MoveBlock.None), OutlineTree.Of(d, h, 3));
    }

    [Fact]
    public void ASkippedLevelEndsSubtreesCorrectly()
    {
        // H1, H3, H2, H1: the H3 is inside the first H1, and the H2 (2 <= 3) ends the subtree of the H3.
        var (d, h) = Read(Heading("a", 1), Heading("b", 3), Heading("c", 2), Heading("d", 1));
        Assert.Equal(new Subtree(0, 3, 2, MoveBlock.None), OutlineTree.Of(d, h, 0));
        Assert.Equal(new Subtree(1, 1, 1, MoveBlock.None), OutlineTree.Of(d, h, 1));
        Assert.Equal(new Subtree(2, 1, 2, MoveBlock.None), OutlineTree.Of(d, h, 2));
    }

    [Fact]
    public void TwoAdjacentHeadingsOfOneLevelAreSeparateSubtrees()
    {
        var (d, h) = Read(Heading("a", 2), Heading("b", 2));
        Assert.Equal(new Subtree(0, 1, 0, MoveBlock.None), OutlineTree.Of(d, h, 0));
        Assert.Equal(new Subtree(1, 1, 1, MoveBlock.None), OutlineTree.Of(d, h, 1));
    }

    [Fact]
    public void TheLastSubtreeRunsToTheEndOfTheBody()
    {
        var (d, h) = Read(Heading("a", 1), Body("t"), Heading("b", 1), Body("t"), Body("u"));
        Assert.Equal(new Subtree(2, 3, 2, MoveBlock.None), OutlineTree.Of(d, h, 1));
    }

    [Fact]
    public void TablesAndBlocksBetweenHeadingsBelongToTheHeadingAbove()
    {
        var (d, h) = Read(Heading("a", 1), new Table(), Body("t"), Heading("b", 1));
        Assert.Equal(new Subtree(0, 3, 0, MoveBlock.None), OutlineTree.Of(d, h, 0));
    }

    [Fact]
    public void ASingleHeadingIsItsOwnSubtree()
    {
        var (d, h) = Read(Heading("a", 1));
        Assert.Equal(new Subtree(0, 1, 0, MoveBlock.None), OutlineTree.Of(d, h, 0));
    }

    [Fact]
    public void ASectionBreakParagraphBlocksTheMove()
    {
        var breaker = Body("t");
        breaker.SectionBreak = new SectionProperties();
        var (d, h) = Read(Heading("a", 1), breaker, Heading("b", 1));
        Assert.Equal(MoveBlock.SectionBreakInside, OutlineTree.Of(d, h, 0).Blocked);
        Assert.Equal(MoveBlock.None, OutlineTree.Of(d, h, 1).Blocked);
    }

    [Fact]
    public void AHeadingInAControlIsBlocked()
    {
        var control = new ContentControlBlock();
        control.Content.Add(Heading("c", 2));
        var (d, h) = Read(Heading("a", 1), control, Heading("b", 1));
        Assert.Equal(3, h.Count);
        Assert.Equal(MoveBlock.InsideContentControl, OutlineTree.Of(d, h, 0).Blocked);
        Assert.Equal(MoveBlock.InsideContentControl, OutlineTree.Of(d, h, 1).Blocked);
        Assert.Equal(MoveBlock.None, OutlineTree.Of(d, h, 2).Blocked);
    }

    [Fact]
    public void AHeadingInATableIsNotARowAndItsTableBelongsToTheHeadingAbove()
    {
        // The outline reads body-level paragraphs only, so a heading inside a table is never listed.
        var (d, h) = Read(Heading("a", 1), new Table(), Heading("b", 1));
        Assert.Equal(2, h.Count);
        Assert.Equal(new Subtree(0, 2, 0, MoveBlock.None), OutlineTree.Of(d, h, 0));
    }

    [Fact]
    public void AnEmptyBodyHasNoRows()
    {
        var (d, h) = Read();
        Assert.Empty(h);
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 0, 0, DropPlace.After));
        Assert.Throws<ArgumentOutOfRangeException>(() => OutlineTree.Of(d, h, 0));
    }

    [Fact]
    public void DropInsideTheSubtreeIsMinusOne()
    {
        // 0 H1, 1 H2, 2 H2, 3 H1, 4 H1: dragging row 0 (blocks 0 to 2).
        var (d, h) = Read(Heading("a", 1), Heading("b", 2), Heading("c", 2), Heading("d", 1), Heading("e", 1));
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 0, 1, DropPlace.Before));
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 0, 1, DropPlace.After));
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 0, 2, DropPlace.Before));
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 0, 0, DropPlace.Before));
        Assert.Equal(4, OutlineTree.DropTarget(h, d, 0, 3, DropPlace.After));
    }

    [Fact]
    public void NoOpTargetsAreMinusOne()
    {
        var (d, h) = Read(Heading("a", 1), Heading("b", 1), Heading("c", 1));
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 1, 1, DropPlace.Before)); // target == First
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 1, 0, DropPlace.After)); // target == First
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 1, 1, DropPlace.After)); // target == First + Count
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 1, 2, DropPlace.Before)); // target == First + Count
    }

    [Fact]
    public void ADropElsewhereGivesTheIndexBeforeTheMove()
    {
        var (d, h) = Read(Heading("a", 1), Body("t"), Heading("b", 1), Heading("c", 1));
        Assert.Equal(0, OutlineTree.DropTarget(h, d, 2, 0, DropPlace.Before));
        Assert.Equal(3, OutlineTree.DropTarget(h, d, 0, 2, DropPlace.Before));
        Assert.Equal(3, OutlineTree.DropTarget(h, d, 0, 1, DropPlace.After));
    }

    [Fact]
    public void DropAfterTheLastRowIsTheEndOfTheBody()
    {
        var (d, h) = Read(Heading("a", 1), Body("t"), Heading("b", 1), Body("u"));
        Assert.Equal(4, OutlineTree.DropTarget(h, d, 0, 1, DropPlace.After));
    }

    [Fact]
    public void ADropBetweenTwoRowsOfOneControlIsMinusOneAndTheEdgesOfTheControlAreLegal()
    {
        // 0 H1 x, 1 control { H1 c1, H1 c2 }, 2 text, 3 H1 d. Rows: x, c1, c2, d.
        var control = new ContentControlBlock();
        control.Content.Add(Heading("c1", 1));
        control.Content.Add(Heading("c2", 1));
        var (d, h) = Read(Heading("x", 1), control, Body("t"), Heading("d", 1));
        Assert.Equal(4, h.Count);

        // Dragging d (block 3): before the first row of the control is legal, between the two rows is not.
        Assert.Equal(1, OutlineTree.DropTarget(h, d, 3, 1, DropPlace.Before));
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 3, 1, DropPlace.After));
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 3, 2, DropPlace.Before));

        // Dragging x (block 0): after the last row of the control is legal (it lands before d).
        Assert.Equal(3, OutlineTree.DropTarget(h, d, 0, 2, DropPlace.After));
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 0, 1, DropPlace.After));
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 0, 2, DropPlace.Before));
    }

    [Fact]
    public void DropsAroundASkippedLevelFollowTheSubtrees()
    {
        // 0 H1 a, 1 H3 b, 2 H2 c, 3 H1 d: a holds blocks 0 to 2, b only block 1.
        var (d, h) = Read(Heading("a", 1), Heading("b", 3), Heading("c", 2), Heading("d", 1));
        Assert.Equal(4, OutlineTree.DropTarget(h, d, 0, 3, DropPlace.After));
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 0, 2, DropPlace.Before));
        Assert.Equal(3, OutlineTree.DropTarget(h, d, 1, 2, DropPlace.After));
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 1, 2, DropPlace.Before));
        Assert.Equal(0, OutlineTree.DropTarget(h, d, 1, 0, DropPlace.Before));
    }

    [Fact]
    public void ABlockedSubtreeOffersNoDrop()
    {
        var breaker = Body("t");
        breaker.SectionBreak = new SectionProperties();
        var (d, h) = Read(Heading("a", 1), breaker, Heading("b", 1), Heading("c", 1));
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 0, 2, DropPlace.After));
    }

    [Fact]
    public void ADropThatWouldEndTheBodyWithATableIsIllegal()
    {
        // The dragged subtree ends in a table and goes last: the body would end with the table.
        var (d, h) = Read(Heading("a", 1), new Table(), Heading("b", 1), Body("t"));
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 0, 1, DropPlace.After));

        // Moved to the middle of a longer body it is fine.
        var (d2, h2) = Read(Heading("a", 1), new Table(), Heading("b", 1), Body("t"), Heading("c", 1));
        Assert.Equal(4, OutlineTree.DropTarget(h2, d2, 0, 1, DropPlace.After));
    }

    [Fact]
    public void MovingTheFinalSubtreeAwayFromATableBeforeItIsIllegal()
    {
        // The previous subtree ends in a table; moving the final subtree to the front would leave the table last.
        var (d, h) = Read(Heading("a", 1), new Table(), Heading("b", 1), Body("t"));
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 1, 0, DropPlace.Before));

        // With a paragraph after the table in the previous subtree the move is legal.
        var (d2, h2) = Read(Heading("a", 1), new Table(), Body("x"), Heading("b", 1), Body("t"));
        Assert.Equal(0, OutlineTree.DropTarget(h2, d2, 1, 0, DropPlace.Before));
    }

    [Fact]
    public void ABodyThatAlreadyEndsWithATableDoesNotBlockMovesThatKeepItLast()
    {
        // 0 a, 1 b, 2 c, 3 Table: the table is last already.
        var (d, h) = Read(Heading("a", 1), Heading("b", 1), Heading("c", 1), new Table());
        Assert.Equal(2, OutlineTree.DropTarget(h, d, 0, 2, DropPlace.Before)); // last stays the table
        Assert.Equal(1, OutlineTree.DropTarget(h, d, 2, 1, DropPlace.Before)); // the final subtree (with the table) moves away: b is last, a paragraph
        Assert.Equal(4, OutlineTree.DropTarget(h, d, 0, 2, DropPlace.After)); // a goes last, behind the table: a paragraph
        Assert.Equal(-1, OutlineTree.DropTarget(h, d, 2, 2, DropPlace.After)); // the end of its own range: nothing moves
    }
}
