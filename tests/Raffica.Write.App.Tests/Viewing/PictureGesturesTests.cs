using Avalonia;
using Avalonia.Input;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>The arithmetic of the picture gestures (milestone 4.5a, Task 12): resizing by a handle, the cursors and the offsets of a moved floating picture. Pure: no view, no model.</summary>
public sealed class PictureGesturesTests
{
    private static readonly Rect Box = new(100, 200, 144, 72);

    [Fact]
    public void ACornerKeepsTheProportionsByTheSideThatMovedMore()
    {
        var grown = PictureGestures.Resize(Box, HandleSlot.BottomRight, 36, 2, free: false);
        Assert.Equal(180, grown.Width, 6);
        Assert.Equal(90, grown.Height, 6);
        Assert.Equal((100.0, 200.0), (grown.X, grown.Y));

        // The height moved more: it decides, and the opposite corner (the bottom right) stays when the top left is dragged.
        var shrunk = PictureGestures.Resize(Box, HandleSlot.TopLeft, 3, 36, free: false);
        Assert.Equal(2.0, shrunk.Width / shrunk.Height, 6);
        Assert.Equal(Box.Bottom, shrunk.Bottom, 6);
        Assert.Equal(Box.Right, shrunk.Right, 6);
        Assert.Equal(36, shrunk.Height, 6);
    }

    [Fact]
    public void FreeingTheRatioChangesBothSidesBySetAmounts()
    {
        var free = PictureGestures.Resize(Box, HandleSlot.TopRight, 10, -30, free: true);
        Assert.Equal((154.0, 102.0), (free.Width, free.Height));
        Assert.Equal(Box.Left, free.Left, 6);
        Assert.Equal(Box.Bottom, free.Bottom, 6);
    }

    [Fact]
    public void AnEdgeChangesOneSideAndLeavesTheOtherAlone()
    {
        var right = PictureGestures.Resize(Box, HandleSlot.Right, 20, 99, free: false);
        Assert.Equal((164.0, 72.0, 100.0, 200.0), (right.Width, right.Height, right.X, right.Y));
        var top = PictureGestures.Resize(Box, HandleSlot.Top, 99, -20, free: false);
        Assert.Equal((144.0, 92.0, 100.0, 180.0), (top.Width, top.Height, top.X, top.Y));
        var left = PictureGestures.Resize(Box, HandleSlot.Left, -10, 0, free: false);
        Assert.Equal((154.0, 72.0, 90.0, 200.0), (left.Width, left.Height, left.X, left.Y));
        var bottom = PictureGestures.Resize(Box, HandleSlot.Bottom, 0, 8, free: false);
        Assert.Equal((144.0, 80.0, 100.0, 200.0), (bottom.Width, bottom.Height, bottom.X, bottom.Y));
    }

    [Fact]
    public void ASideNeverGoesBelowTheMinimumOrAboveTheMaximum()
    {
        var small = PictureGestures.Resize(Box, HandleSlot.BottomRight, -1000, -1000, free: false);
        Assert.Equal(PictureGestures.MinSizePt, Math.Min(small.Width, small.Height), 6);
        Assert.Equal(2.0, small.Width / small.Height, 6);
        var freeSmall = PictureGestures.Resize(Box, HandleSlot.BottomRight, -1000, -1000, free: true);
        Assert.Equal((PictureGestures.MinSizePt, PictureGestures.MinSizePt), (freeSmall.Width, freeSmall.Height));

        var large = PictureGestures.Resize(Box, HandleSlot.BottomRight, 100_000, 100_000, free: false);
        Assert.Equal(PictureGestures.MaxSizePt, Math.Max(large.Width, large.Height), 6);
        Assert.Equal(2.0, large.Width / large.Height, 6);
        var edge = PictureGestures.Resize(Box, HandleSlot.Left, -100_000, 0, free: false);
        Assert.Equal(PictureGestures.MaxSizePt, edge.Width, 6);
        Assert.Equal(Box.Right, edge.Right, 6);
        Assert.Equal(8.5, PictureGestures.MinSizePt);
        Assert.Equal(1584, PictureGestures.MaxSizePt, 6);
    }

    [Fact]
    public void EveryHandleHasItsOwnKindOfResizeCursor()
    {
        Assert.Equal(StandardCursorType.TopLeftCorner, PictureGestures.CursorOf(HandleSlot.TopLeft));
        Assert.Equal(StandardCursorType.BottomRightCorner, PictureGestures.CursorOf(HandleSlot.BottomRight));
        Assert.Equal(StandardCursorType.TopRightCorner, PictureGestures.CursorOf(HandleSlot.TopRight));
        Assert.Equal(StandardCursorType.BottomLeftCorner, PictureGestures.CursorOf(HandleSlot.BottomLeft));
        Assert.Equal(StandardCursorType.SizeNorthSouth, PictureGestures.CursorOf(HandleSlot.Top));
        Assert.Equal(StandardCursorType.SizeNorthSouth, PictureGestures.CursorOf(HandleSlot.Bottom));
        Assert.Equal(StandardCursorType.SizeWestEast, PictureGestures.CursorOf(HandleSlot.Left));
        Assert.Equal(StandardCursorType.SizeWestEast, PictureGestures.CursorOf(HandleSlot.Right));
    }

    [Fact]
    public void TheOffsetsOfAMoveTakeTheirOriginFromWhatEachAxisIsMeasuredFrom()
    {
        var box = new Rect(250, 130, 72, 72);

        // Measured from the column and the paragraph with offsets now: the horizontal origin is what the box and its offset give (the column's left edge, 72), the vertical one is the new paragraph's first line.
        var anchor = new PictureAnchorInfo("column", 2_260_600, "paragraph", 0, 1, false);
        var (dx, dy) = PictureGestures.MoveOffsets(anchor, box, desiredX: 300, desiredY: 400, paragraphLeft: 80, paragraphTop: 380);
        Assert.Equal((long)Math.Round((300 - (250 - (2_260_600 / 12700.0))) * 12700), dx);
        Assert.Equal(20 * 12700, dy);

        // Measured from the page: the origin is the page's corner whatever the paragraph.
        var page = new PictureAnchorInfo("page", 3_175_000, "page", 1_651_000, 1, false);
        var (px, py) = PictureGestures.MoveOffsets(page, box, desiredX: 300, desiredY: 400, paragraphLeft: 80, paragraphTop: 380);
        Assert.Equal((long)Math.Round((300 - (250 - (3_175_000 / 12700.0))) * 12700), px);
        Assert.Equal((long)Math.Round((400 - (130 - (1_651_000 / 12700.0))) * 12700), py);

        // An axis placed by an alignment keyword has no offset: the page is 0, anything else is the new paragraph's first line.
        var aligned = new PictureAnchorInfo("margin", null, "page", null, 1, false);
        var (ax, ay) = PictureGestures.MoveOffsets(aligned, box, desiredX: 300, desiredY: 400, paragraphLeft: 80, paragraphTop: 380);
        Assert.Equal(220 * 12700, ax);
        Assert.Equal(400 * 12700, ay);
        var alignedPage = new PictureAnchorInfo("page", null, "paragraph", null, 1, false);
        var (bx, by) = PictureGestures.MoveOffsets(alignedPage, box, desiredX: 300, desiredY: 400, paragraphLeft: 80, paragraphTop: 380);
        Assert.Equal(300 * 12700, bx);
        Assert.Equal(20 * 12700, by);
    }

    [Fact]
    public void AMoveFarOutsideThePageIsClampedToTheEditorsRange()
    {
        var anchor = new PictureAnchorInfo("page", 0, "page", 0, 1, false);
        var (dx, dy) = PictureGestures.MoveOffsets(anchor, new Rect(0, 0, 72, 72), 1e9, -1e9, 0, 0);
        Assert.Equal(Raffica.Write.Editing.PictureRules.MaxOffsetEmu, dx);
        Assert.Equal(-Raffica.Write.Editing.PictureRules.MaxOffsetEmu, dy);
    }
}
