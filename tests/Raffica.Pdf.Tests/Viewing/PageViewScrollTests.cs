using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Viewing;

public sealed class PageViewScrollTests
{
    [Fact]
    public void WheelStepIs48DipAndClampsAtBothEnds()
    {
        Assert.Equal(48, PageViewScroll.WheelStep);

        // One notch down (the wheel reports -1) moves 48 DIP, one up moves back; the ends clamp.
        Assert.Equal(48, PageViewScroll.ByWheel(0, -1, 5000, 600));
        Assert.Equal(96, PageViewScroll.ByWheel(48, -1, 5000, 600));
        Assert.Equal(0, PageViewScroll.ByWheel(48, 1, 5000, 600));
        Assert.Equal(0, PageViewScroll.ByWheel(10, 1, 5000, 600));
        Assert.Equal(4400, PageViewScroll.ByWheel(4390, -1, 5000, 600));
        Assert.Equal(4400, PageViewScroll.ByWheel(0, -1000, 5000, 600));

        // Content that fits does not scroll; odd input stays finite.
        Assert.Equal(0, PageViewScroll.ByWheel(0, -1, 300, 600));
        Assert.Equal(0, PageViewScroll.Clamp(double.NaN, 5000, 600));
        Assert.Equal(0, PageViewScroll.MaxOffset(double.NaN, 600));
    }

    [Fact]
    public void KeysMoveByLinesAndPages()
    {
        Assert.Equal(48, PageViewScroll.ByKey(Key.Down, 0, 5000, 600));
        Assert.Equal(0, PageViewScroll.ByKey(Key.Up, 20, 5000, 600));
        Assert.Equal(552, PageViewScroll.ByKey(Key.PageDown, 0, 5000, 600));
        Assert.Equal(0, PageViewScroll.ByKey(Key.PageUp, 100, 5000, 600));
        Assert.Equal(0, PageViewScroll.ByKey(Key.Home, 3000, 5000, 600));
        Assert.Equal(4400, PageViewScroll.ByKey(Key.End, 0, 5000, 600));
        Assert.Null(PageViewScroll.ByKey(Key.A, 0, 5000, 600));
        Assert.Equal(48, PageViewScroll.ByHorizontalKey(Key.Right, 0, 2000, 600));
        Assert.Null(PageViewScroll.ByHorizontalKey(Key.Down, 0, 2000, 600));
    }

    [Fact]
    public void KeepAnchorPutsTheSameSpotAtTheTop()
    {
        var layout = new DocumentLayout(50, new PdfPageSize(595, 842));
        layout.SetSizes(0, Enumerable.Repeat(new PdfPageSize(595, 842), 50).ToArray());
        var anchor = layout.GetAnchor(layout.GetOffset(new PageAnchor(12, 0.25)));
        Assert.Equal(12, anchor.Page);

        // A zoom change moves every page; the offset that keeps the anchor puts the same spot at the top again.
        layout.Zoom = 1.75;
        var offset = PageViewScroll.KeepAnchor(layout, anchor, 600);
        var after = layout.GetAnchor(offset);

        Assert.Equal(12, after.Page);
        Assert.Equal(0.25, after.Fraction, 6);

        // A viewport taller than the rest of the content pulls the offset back to the end.
        Assert.Equal(PageViewScroll.MaxOffset(layout.ContentHeight, 600), PageViewScroll.KeepAnchor(layout, new PageAnchor(49, 0.9), 600));
    }

    [AvaloniaFact]
    public async Task WheelScrollsInStepsOfFortyEightAndStopsAtBothEnds()
    {
        using var fixture = await ViewFixture.OpenAsync("text-rich.pdf");
        var view = fixture.View;
        Assert.Equal(0, view.Offset.Y);

        fixture.Window.MouseWheel(new Point(300, 300), new Vector(0, -1));
        view.CompleteScroll();
        Assert.Equal(48, view.Offset.Y);

        fixture.Window.MouseWheel(new Point(300, 300), new Vector(0, 1));
        view.CompleteScroll();
        Assert.Equal(0, view.Offset.Y);

        fixture.Window.MouseWheel(new Point(300, 300), new Vector(0, 1));
        view.CompleteScroll();
        Assert.Equal(0, view.Offset.Y);

        fixture.Window.MouseWheel(new Point(300, 300), new Vector(0, -100_000));
        view.CompleteScroll();
        Assert.Equal(view.MaxOffsetY, view.Offset.Y);
    }

    [AvaloniaFact]
    public async Task WheelWithControlDoesNotScroll()
    {
        using var fixture = await ViewFixture.OpenAsync("text-rich.pdf");

        fixture.Window.MouseWheel(new Point(300, 300), new Vector(0, -1), RawInputModifiers.Control);
        fixture.View.CompleteScroll();

        Assert.Equal(0, fixture.View.Offset.Y);
    }

    [AvaloniaFact]
    public async Task HomeAndEndGoToTheFirstAndLastPage()
    {
        using var fixture = await ViewFixture.OpenAsync("thousand-pages.pdf");
        var view = fixture.View;
        Assert.Equal(1000, view.PageCount);
        view.Focus();
        Assert.True(view.IsFocused);

        fixture.Window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        Assert.Equal(999, view.CurrentPage);
        Assert.Equal(view.MaxOffsetY, view.Offset.Y);

        fixture.Window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);
        Assert.Equal(0, view.CurrentPage);
        Assert.Equal(0, view.Offset.Y);

        fixture.Window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None);
        Assert.Equal(PageViewScroll.PageStep(view.Bounds.Height), view.Offset.Y);
        fixture.Window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        Assert.Equal(PageViewScroll.PageStep(view.Bounds.Height) - 48, view.Offset.Y);
    }

    [AvaloniaFact]
    public async Task JumpToTheEndPlansOnlyTheVisiblePages()
    {
        using var fixture = await ViewFixture.OpenAsync("thousand-pages.pdf");
        var view = fixture.View;
        var scheduler = fixture.Session.Scheduler!;
        Assert.Equal(1000, view.PageCount);
        Assert.NotEmpty(view.LastPlan);
        Assert.Contains(view.LastPlan, request => request.Key.Page == 0);

        // The scroll bar drag, the End key and "go to page" all end in the same plan: jump while renders of page 0 are pending.
        view.GoToPage(999);
        FixtureCheck(view, scheduler);

        view.Offset = new Vector(0, 0);
        view.Offset = new Vector(0, view.MaxOffsetY);
        FixtureCheck(view, scheduler);

        await fixture.SettleAsync();
        var cache = fixture.Session.Cache!;
        Assert.True(cache.UsedBytes <= cache.BudgetBytes);
        Assert.True(view.Bitmaps.Bytes <= TileBitmaps.DefaultMaxBytes);
        Assert.Equal(999, view.VisiblePages.Last);
        Assert.NotEmpty(view.LastPlan);
        Assert.DoesNotContain(view.LastPlan, request => request.Key.Page < 900);

        static void FixtureCheck(PageView view, RenderScheduler scheduler)
        {
            var plan = view.LastPlan;
            var (first, last) = view.VisiblePages;
            var visible = last - first + 1;
            Assert.True(visible > 0);
            Assert.Equal(999, last);
            Assert.DoesNotContain(plan, request => request.Key.Page == 0);
            Assert.DoesNotContain(plan, request => request.Key.Page < first - ViewportPlannerBound);

            // Previews: the visible pages and at most the capped buffer previews; sharp tiles: visible pages only.
            Assert.True(plan.Count(request => request.Key.IsPreview) <= visible + ViewportPlanner.MaxPreviewRequests);
            Assert.All(plan.Where(request => !request.Key.IsPreview), request => Assert.InRange(request.Key.Page, first, last));

            // The old requests of the first page are gone: the scheduler wants no more than the new plan.
            Assert.True(scheduler.PendingCount <= plan.Count);
        }
    }

    private const int ViewportPlannerBound = ViewportPlanner.MaxPreviewRequests;

    [AvaloniaFact]
    public async Task ResizeKeepsTheTopAnchor()
    {
        using var fixture = await ViewFixture.OpenAsync("text-rich.pdf", 900, 700);
        var view = fixture.View;
        Assert.True(view.PageCount > 1);
        view.GoToPage(1);
        view.Offset = new Vector(0, view.Offset.Y + 100);
        var before = view.CaptureView();

        fixture.Window.Width = 1100;
        fixture.Window.Height = 450;
        ViewFixture.Pump();
        var after = view.CaptureView();

        Assert.Equal(before.Anchor.Page, after.Anchor.Page);
        Assert.Equal(before.Anchor.Fraction, after.Anchor.Fraction, 6);

        // A fit mode changes the zoom with the window width; the top of the view stays at the same spot of the same page.
        view.ZoomMode = ZoomMode.FitWidth;
        ViewFixture.Pump();
        var fitted = view.CaptureView();
        fixture.Window.Width = 700;
        ViewFixture.Pump();
        var narrowed = view.CaptureView();

        Assert.Equal(fitted.Anchor.Page, narrowed.Anchor.Page);
        Assert.Equal(fitted.Anchor.Fraction, narrowed.Anchor.Fraction, 6);
        Assert.NotEqual(1.0, view.Zoom);
    }
}
