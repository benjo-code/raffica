using Avalonia;
using Raffica.Write.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Viewing;

public sealed class PageScrollerTests
{
    private const float Letter = 612f;
    private const float LetterHeight = 792f;
    private const double Dips = 96.0 / 72.0;

    private static (float, float)[] Pages(int count, float width = Letter, float height = LetterHeight) =>
        [.. Enumerable.Repeat((width, height), count)];

    // Blocks 0..n-1; block i starts on page i / 2 at y = 100 + (i % 2) * 300 (points).
    private sealed class FakeMap(int blocks) : IBlockMap
    {
        public (int Page, float Y)? Locate(int block) => block < 0 || block >= blocks ? null : (block / 2, 100f + ((block % 2) * 300f));

        public int BlockAt(int page, float y) => Math.Min((page * 2) + (y >= 400f ? 1 : 0), blocks - 1);
    }

    [Fact]
    public void RectsStackWithGap()
    {
        var scroller = new PageScroller(Pages(3), gap: 10, zoom: 1.0);
        var scale = Dips;
        var first = scroller.RectOf(0);
        var second = scroller.RectOf(1);
        Assert.Equal(10, first.Y, 6);
        Assert.Equal(Letter * scale, first.Width, 6);
        Assert.Equal(LetterHeight * scale, first.Height, 6);
        Assert.Equal(first.Bottom + 10, second.Y, 6);
        Assert.Equal(first.X, second.X, 6);
        Assert.Equal(10 + (3 * LetterHeight * scale) + (3 * 10), scroller.ContentHeight, 6);
        Assert.Equal((Letter * scale) + 20, scroller.ContentWidth, 6);
    }

    [Fact]
    public void NarrowerPagesAreCentred()
    {
        var scroller = new PageScroller([(612f, 792f), (306f, 792f)], gap: 10, zoom: 1.0);
        var wide = scroller.RectOf(0);
        var narrow = scroller.RectOf(1);
        Assert.Equal(wide.Center.X, narrow.Center.X, 6);
        Assert.True(narrow.Width < wide.Width);
    }

    [Fact]
    public void ZoomScalesThePages()
    {
        var one = new PageScroller(Pages(2), 10, 1.0);
        var two = new PageScroller(Pages(2), 10, 2.0);
        Assert.Equal(one.RectOf(0).Width * 2, two.RectOf(0).Width, 6);
        Assert.Equal(one.RectOf(0).Height * 2, two.RectOf(0).Height, 6);
    }

    [Fact]
    public void VisibleRangeAtOffsets()
    {
        var scroller = new PageScroller(Pages(10), gap: 10, zoom: 1.0);
        var page = LetterHeight * Dips;
        Assert.Equal((0, 0), scroller.Visible(0, 200));

        // The viewport reaches into the gap and the top of page 1.
        Assert.Equal((0, 1), scroller.Visible(0, page + 30));

        // Scrolled so that page 1 fills the view: only page 1 (the gap above and below belong to nobody).
        var top = scroller.RectOf(1).Y;
        Assert.Equal((1, 1), scroller.Visible(top, page));
        Assert.Equal((1, 2), scroller.Visible(top + 1, page + 20));

        // Beyond the content nothing is visible.
        Assert.Equal((0, -1), scroller.Visible(scroller.ContentHeight + 5, 100));
        Assert.Equal((0, -1), new PageScroller([], 10, 1.0).Visible(0, 100));

        // The last pixel of the content shows the last page.
        Assert.Equal(9, scroller.Visible(scroller.ContentHeight - 50, 50).Last);
    }

    [Fact]
    public void PageAtOffsetUsesTheMiddleOfTheViewport()
    {
        var scroller = new PageScroller(Pages(10), gap: 10, zoom: 1.0);
        Assert.Equal(0, scroller.PageAt(0));
        Assert.Equal(3, scroller.PageAt(scroller.RectOf(3).Center.Y));
        Assert.Equal(9, scroller.PageAt(scroller.ContentHeight + 1000));
        Assert.Equal(0, scroller.PageAt(-50));
    }

    [Fact]
    public void AnchorRoundTrip()
    {
        var map = new FakeMap(20);
        var scroller = new PageScroller(Pages(10), gap: 10, zoom: 1.5, map);

        // The top of the view at the start of block 7 (page 3, y = 400).
        var offset = scroller.RectOf(3).Y + (400 * 1.5 * Dips);
        var anchor = scroller.AnchorAt(offset);
        Assert.Equal(7, anchor.Block);
        Assert.Equal(0, anchor.Character);
        Assert.Equal(offset, scroller.OffsetOf(anchor)!.Value, 6);

        // Another zoom: the same block is at the top again.
        var other = new PageScroller(Pages(10), gap: 10, zoom: 0.5, map);
        Assert.Equal(other.RectOf(3).Y + (400 * 0.5 * Dips), other.OffsetOf(anchor)!.Value, 6);
    }

    [Fact]
    public void ABlockThatStartedOnAnEarlierPageIsNotTheAnchorOfALaterPageTop()
    {
        // Page 3 begins inside block 5 (which starts on page 2): the anchor is the next block, so going back never jumps up a page.
        var map = new FakeMap(20);
        var scroller = new PageScroller(Pages(10), gap: 10, zoom: 1.0, new ContinuingMap(map));
        var anchor = scroller.AnchorAt(scroller.RectOf(3).Y);
        Assert.Equal(6, anchor.Block);
    }

    private sealed class ContinuingMap(FakeMap inner) : IBlockMap
    {
        public (int Page, float Y)? Locate(int block) => inner.Locate(block);

        // At the top of every page the block that started on the page before is reported.
        public int BlockAt(int page, float y) => y < 1f && page > 0 ? (page * 2) - 1 : inner.BlockAt(page, y);
    }

    [Fact]
    public void AnAnchorOfABlockThatIsNotLaidOutYetHasNoOffset()
    {
        var scroller = new PageScroller(Pages(2), 10, 1.0, new FakeMap(4));
        Assert.Null(scroller.OffsetOf(new ViewAnchor(99, 0)));
        Assert.Null(new PageScroller(Pages(2), 10, 1.0).OffsetOf(new ViewAnchor(1, 0)));
        Assert.Equal(ViewAnchor.Top, new PageScroller(Pages(2), 10, 1.0).AnchorAt(300));
    }

    [Theory]
    [InlineData(0.0, 120.0, 2.0)]
    [InlineData(1500.0, 300.0, 0.5)]
    [InlineData(4000.0, 0.0, 1.25)]
    public void ZoomKeepsThePointUnderTheCursor(double offset, double cursor, double newZoom)
    {
        var before = new PageScroller(Pages(30), gap: 10, zoom: 1.0);
        var after = new PageScroller(Pages(30), gap: 10, zoom: newZoom);
        var next = before.OffsetKeepingPoint(offset, cursor, after);

        // The page and the fraction of the page under the cursor are the same before and after.
        var contentY = offset + cursor;
        var page = before.PageAt(contentY);
        var rect = before.RectOf(page);
        var fraction = (contentY - rect.Y) / rect.Height;
        var newRect = after.RectOf(page);
        Assert.Equal(newRect.Y + (fraction * newRect.Height) - cursor, next, 4);
    }

    [Fact]
    public void ZoomKeepsTheHorizontalPointToo()
    {
        var before = new PageScroller(Pages(3), gap: 10, zoom: 2.0);
        var after = new PageScroller(Pages(3), gap: 10, zoom: 4.0);
        var next = before.HorizontalOffsetKeepingPoint(offset: 300, cursor: 100, after);
        var fraction = (300 + 100 - 10) / (before.ContentWidth - 20);
        Assert.Equal(10 + (fraction * (after.ContentWidth - 20)) - 100, next, 4);
    }

    [Fact]
    public void ExtentGrowsWhenPagesArePublished()
    {
        var map = new FakeMap(40);
        var three = new PageScroller(Pages(3), 10, 1.0, map);
        var five = new PageScroller(Pages(5), 10, 1.0, map);
        Assert.True(five.ContentHeight > three.ContentHeight);
        Assert.Equal(three.ContentHeight + (2 * ((LetterHeight * Dips) + 10)), five.ContentHeight, 6);

        // What was above stays where it was, so a scroll position stays valid.
        for (var page = 0; page < 3; page++)
        {
            Assert.Equal(three.RectOf(page), five.RectOf(page));
        }
    }

    [Fact]
    public void AStripColumnHasNoGapAndTakesItsEdgeFromTheArgument()
    {
        var strips = new PageScroller([(520f, 300f), (520f, 200f)], gap: 0, zoom: 1.0, edge: 24);
        Assert.Equal(24, strips.RectOf(0).Y, 6);
        Assert.Equal(strips.RectOf(0).Bottom, strips.RectOf(1).Y, 6);
        Assert.Equal(24, strips.RectOf(0).X, 6);
        Assert.Equal((520 * Dips) + 48, strips.ContentWidth, 6);
    }

    [Fact]
    public void ClampKeepsTheOffsetInRange()
    {
        Assert.Equal(0, PageScroller.Clamp(-5, 1000, 400));
        Assert.Equal(600, PageScroller.Clamp(900, 1000, 400));
        Assert.Equal(0, PageScroller.Clamp(10, 300, 400));
        Assert.Equal(0, PageScroller.Clamp(double.NaN, 1000, 400));
        Assert.Equal(600, PageScroller.MaxOffset(1000, 400));
    }

    [Fact]
    public void RectOfAPageOutOfRangeThrows() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageScroller(Pages(2), 10, 1.0).RectOf(2));

    [Fact]
    public void TheAutoScrollStepGrowsWithTheDistanceAndKeepsTheSign()
    {
        Assert.Equal(0, PageScroller.AutoScrollStep(0));
        Assert.Equal(0, PageScroller.AutoScrollStep(double.NaN));
        Assert.Equal(4.4, PageScroller.AutoScrollStep(1), 6);
        Assert.Equal(-4.4, PageScroller.AutoScrollStep(-1), 6);
        Assert.True(PageScroller.AutoScrollStep(100) > PageScroller.AutoScrollStep(10));
        Assert.Equal(96, PageScroller.AutoScrollStep(100000));
        Assert.Equal(-96, PageScroller.AutoScrollStep(-100000));
    }
}
