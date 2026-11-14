using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Viewing;
using Raffica.Write.Layout;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;
using Xunit;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.WriteApp.Tests.Viewing;

public sealed class PageViewTests
{
    [AvaloniaFact]
    public async Task FirstPageIsDrawn()
    {
        using var harness = new ViewHarness(WindowFixture.Thesis);
        // The first frame draws page 1 on the render thread; the event follows on the UI thread, once.
        var shot = harness.Capture();
        await ViewHarness.WaitAsync(() => harness.FirstSharpCount == 1);
        harness.Capture();
        harness.Capture();
        ViewHarness.Pump();
        Assert.Equal(1, harness.FirstSharpCount);
        Assert.True(harness.View.FirstSharpPageShown);
        Assert.True(harness.View.DrawCount >= 1);

        // Dark pixels in the text area of the paper, none in the gap above it, and the paper itself is white.
        var page = harness.View.PageBounds(0)!.Value;
        var left = (int)page.Left;
        var top = (int)page.Top;
        Assert.True(shot.DarkPixels(left + 40, top + 40, (int)page.Width - 80, (int)page.Height - 80) > 50, "no text was drawn on page 1");
        Assert.Equal(0, shot.DarkPixels(0, 0, shot.Width, Math.Max(0, top - 2)));
        Assert.Equal(0xFFFFFFFFu, shot.At(left + 4, top + 4));
    }

    [AvaloniaFact]
    public void TheViewIsFocusableAndNamedAfterTheDocument()
    {
        using var harness = new ViewHarness(WindowFixture.Poi);
        Assert.True(harness.View.Focusable);
        Assert.True(harness.View.IsFocused);
        Assert.StartsWith("Document: poi-test-document.docx", AutomationProperties.GetName(harness.View), StringComparison.Ordinal);
        Assert.Contains("page 1 of", AutomationProperties.GetName(harness.View), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void WheelScrollsAndClamps()
    {
        using var harness = new ViewHarness(WindowFixture.Thesis);
        var view = harness.View;
        var point = new Point(300, 300);
        Assert.Equal(0, view.OffsetY);

        // Three notches down (a negative delta scrolls down) move three steps of 48 DIPs.
        harness.Window.MouseWheel(point, new Vector(0, -3));
        ViewHarness.Pump();
        Assert.Equal(3 * PageScroller.WheelStep, view.OffsetY);

        // A wheel up beyond the start stops at the top.
        harness.Window.MouseWheel(point, new Vector(0, 100));
        ViewHarness.Pump();
        Assert.Equal(0, view.OffsetY);

        // And down beyond the end stops at the end of what is laid out.
        harness.Window.MouseWheel(point, new Vector(0, -1000));
        ViewHarness.Pump();
        Assert.Equal(PageScroller.MaxOffset(view.ContentHeight, view.Viewport.Height), view.OffsetY);
        Assert.True(view.OffsetY > 0);
    }

    [AvaloniaFact]
    public void CtrlWheelZooms()
    {
        using var harness = new ViewHarness(WindowFixture.Thesis);
        var view = harness.View;
        Assert.Equal(ZoomMode.FitWidth, view.ZoomMode);
        var fit = view.Zoom;
        var point = new Point(450, 300);
        var before = view.PageBounds(0)!.Value;

        harness.Window.MouseWheel(point, new Vector(0, 1), RawInputModifiers.Control);
        ViewHarness.Pump();
        Assert.Equal(ZoomSteps.Next(fit), view.Zoom);
        Assert.Equal(ZoomMode.Free, view.ZoomMode);
        // The point under the cursor stays under it: the same fraction of the page is there before and after.
        var after = view.PageBounds(0)!.Value;
        Assert.Equal((point.Y - before.Top) / before.Height, (point.Y - after.Top) / after.Height, 4);

        harness.Window.MouseWheel(point, new Vector(0, -2), RawInputModifiers.Control);
        ViewHarness.Pump();
        Assert.Equal(ZoomSteps.Previous(ZoomSteps.Previous(ZoomSteps.Next(fit))), view.Zoom);
    }

    [AvaloniaFact]
    public async Task PageDownMovesOnePage()
    {
        using var harness = new ViewHarness(WindowFixture.Thesis);
        var view = harness.View;
        view.Runner.Start();
        await ViewHarness.WaitAsync(() => view.PageCount >= 4);
        Assert.Equal(0, view.CurrentPage);

        harness.Window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.Equal(1, view.CurrentPage);
        Assert.Equal(view.Scroller!.RectOf(1).Y - PageView.PageGap, view.OffsetY, 6);

        harness.Window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.Equal(2, view.CurrentPage);

        // PageUp goes back to the top of the page before.
        harness.Window.KeyPressQwerty(PhysicalKey.PageUp, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.Equal(1, view.CurrentPage);
        Assert.Equal(view.Scroller!.RectOf(1).Y - PageView.PageGap, view.OffsetY, 6);
    }

    [AvaloniaFact]
    public async Task HomeAndEndGoToTheEnds()
    {
        using var harness = new ViewHarness(WindowFixture.Thesis);
        var view = harness.View;
        await harness.LayOutEverythingAsync();
        Assert.True(view.PageCount > 100);

        harness.Window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.Equal(PageScroller.MaxOffset(view.ContentHeight, view.Viewport.Height), view.OffsetY);
        Assert.Equal(view.PageCount - 1, view.CurrentPage);

        harness.Window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.Equal(0, view.OffsetY);
        Assert.Equal(0, view.CurrentPage);
    }

    [AvaloniaFact]
    public async Task PagesAppearWhileTheLayoutGoesOn()
    {
        using var harness = new ViewHarness(WindowFixture.Thesis);
        var view = harness.View;
        Assert.Equal(1, view.PageCount);
        var firstHeight = view.ContentHeight;
        var changes = 0;
        view.ViewChanged += () => changes++;
        var started = Stopwatch.StartNew();

        view.Runner.Start();
        await ViewHarness.WaitAsync(() => view.PageCount > 1);
        Assert.True(view.ContentHeight > firstHeight);
        await ViewHarness.WaitAsync(() => view.IsLayoutComplete, 30000);
        var elapsed = started.ElapsedMilliseconds;
        Assert.True(view.PageCount > 100);
        Assert.Equal(view.Runner.PageLayout.LaidOutPages, view.PageCount);

        // The view was told, but never more often than every 100 ms of progress (plus the events of the start, the replaced prefix and the end).
        Assert.True(changes >= 2, "the view was not told about new pages");
        Assert.True(changes <= (elapsed / LayoutRunner.ProgressIntervalMs) + 6, $"{changes} changes in {elapsed} ms");
    }

    [AvaloniaFact]
    public async Task NoPaperChromeInWritingView()
    {
        // A wide window, so the column of the writing view has desk on both sides.
        using var page = new ViewHarness(WindowFixture.Thesis, 1100, 700, WriteView.Page, ZoomMode.ActualSize);
        var pageShot = page.Capture();
        var paper = page.View.PageBounds(0)!.Value;
        var middle = (int)(paper.Top + (paper.Height / 2));
        Assert.Equal(0xFFFFFFFFu, pageShot.At((int)paper.Left + 3, middle));

        // The page view has a shadow: the desk right next to the paper is not the plain desk colour.
        var desk = 0xFF000000u | ((uint)ViewHarness.Desk.R << 16) | ((uint)ViewHarness.Desk.G << 8) | ViewHarness.Desk.B;
        var shadowed = Enumerable.Range(1, 6).Any(d => pageShot.At((int)paper.Left - d, 300) != desk);
        Assert.True(shadowed, "the page view draws no shadow");
        Assert.Equal(desk, pageShot.At(2, 300));

        using var writing = new ViewHarness(WindowFixture.Thesis, 1100, 700, WriteView.Writing, ZoomMode.ActualSize);
        await ViewHarness.WaitAsync(() => writing.View.PageCount > 0);
        var shot = writing.Capture();
        var column = writing.View.PageBounds(0)!.Value;

        // One column of the width of the text, paper coloured, centred; the desk beside it is flat (no edge, no shadow, no margin lines).
        Assert.InRange(column.Width, (LayoutRunner.FlowTextWidth * PageScroller.DipsPerPoint) - 1, (LayoutRunner.FlowTextWidth * PageScroller.DipsPerPoint) + 1);
        Assert.Equal((1100 - column.Width) / 2, column.Left, 1);
        var row = (int)(column.Top + 200);
        Assert.Equal(0xFFFFFFFFu, shot.At((int)column.Left + 2, row));
        Assert.All(Enumerable.Range(1, 12), d => Assert.Equal(desk, shot.At((int)column.Left - d, row)));
        Assert.All(Enumerable.Range(1, 12), d => Assert.Equal(desk, shot.At((int)column.Right + d, row)));
    }

    [AvaloniaFact]
    public async Task AProvisionalPageIsReadAgainWhenTheLayoutCompletes()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-write-numpages-").FullName;
        try
        {
            var path = ViewHarness.WriteNumPagesDocument(folder);
            using var harness = new ViewHarness(path, 900, 1300, WriteView.Page, ZoomMode.ActualSize);
            var layout = harness.Tab.Layout;
            var first = layout.Page(0);
            Assert.True(first.IsProvisional, "the footer of page 1 shows a page count that is not known yet");
            Assert.Equal(0, layout.Revision);

            var footer = (Func<FrameShot, long>)(shot =>
            {
                var page = harness.View.PageBounds(0)!.Value;
                return shot.Hash((int)page.Left + 20, (int)(page.Bottom - 110), (int)page.Width - 40, 90);
            });
            var before = footer(harness.Capture());

            await harness.LayOutEverythingAsync();
            Assert.Equal(1, layout.Revision);
            Assert.True(layout.IsComplete);
            Assert.Equal(5, layout.LaidOutPages);
            var replaced = layout.Page(0);
            Assert.NotSame(first, replaced);
            Assert.False(replaced.IsProvisional);

            // The view drew the replacement: the footer pixels changed ("of …" became "of 5").
            var after = footer(harness.Capture());
            Assert.NotEqual(before, after);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task TheFullOpenReplacesThePrefixAndTheLayoutCompletes()
    {
        using var harness = new ViewHarness(WindowFixture.Thesis);
        var tab = harness.Tab;
        Assert.True(tab.Docx.IsPrefix, "the thesis opens as a prefix");
        var prefixLayout = tab.Layout;

        await harness.LayOutEverythingAsync();

        Assert.False(tab.Docx.IsPrefix);
        Assert.Same(prefixLayout, harness.View.Runner.PageLayout);
        Assert.Same(prefixLayout, tab.Layout);
        Assert.True(prefixLayout.IsComplete);
        Assert.Null(harness.View.Runner.Fault);

        // The same layout as one made over the whole file from the start.
        var reference = DocumentLayout.Create(tab.Docx.Model, tab.Fonts, new LayoutOptions(LayoutMode.Pages, ImageBytes: tab.Images.Read));
        await reference.LayOutRestAsync(null, CancellationToken.None);
        Assert.Equal(reference.LaidOutPages, prefixLayout.LaidOutPages);
        Assert.True(prefixLayout.LaidOutPages > 100);
        for (var i = 0; i < reference.LaidOutPages; i += 17)
        {
            Assert.Equal(reference.Page(i).Text, prefixLayout.Page(i).Text);
        }

        // The pictures still come out of the package after the swap (the source reads the whole file now).
        var pictureId = Enumerable.Range(0, prefixLayout.LaidOutPages)
            .SelectMany(i => prefixLayout.Page(i).Items.OfType<ImageItem>())
            .Select(item => item.Image.Id)
            .FirstOrDefault();
        Assert.NotNull(pictureId);
        Assert.NotEqual(0, tab.Images.Read(pictureId!).Length);
    }

    [AvaloniaFact]
    public async Task ARefusedReplaceModelStartsOverOnTheWholeModel()
    {
        using var harness = new ViewHarness(WindowFixture.Thesis);
        var tab = harness.Tab;
        var original = tab.Layout;
        var firstText = original.Page(0).Text;
        harness.View.Runner.BeforeReplaceModel = () => throw new InvalidOperationException("the first page differs");

        await harness.LayOutEverythingAsync();

        // A new layout object took the place of the prefix layout, in the runner and in the tab; the view ends with a complete, correct layout.
        var runner = harness.View.Runner;
        Assert.NotSame(original, runner.PageLayout);
        Assert.Same(runner.PageLayout, tab.Layout);
        Assert.Null(runner.Fault);
        Assert.True(runner.PageLayout.IsComplete);
        Assert.Equal(firstText, runner.PageLayout.Page(0).Text);
        var reference = DocumentLayout.Create(tab.Docx.Model, tab.Fonts, new LayoutOptions(LayoutMode.Pages, ImageBytes: tab.Images.Read));
        await reference.LayOutRestAsync(null, CancellationToken.None);
        Assert.Equal(reference.LaidOutPages, runner.PageLayout.LaidOutPages);
        Assert.Equal(reference.LaidOutPages, harness.View.PageCount);
        Assert.Equal(reference.Page(reference.LaidOutPages - 1).Text, runner.PageLayout.Page(reference.LaidOutPages - 1).Text);
    }

    [AvaloniaFact]
    public void ADrawThatRunsKeepsThePicturesAliveUntilItEndsAndTheCloseNeverWaits()
    {
        var harness = new ViewHarness(WindowFixture.Thesis);
        try
        {
            var view = harness.View;
            Assert.True(view.Gate.TryEnter(), "a draw is in flight");

            // Closing the tab returns at once although the draw has not ended: no wait on the UI thread.
            var watch = Stopwatch.StartNew();
            harness.Tab.Dispose();
            Assert.True(watch.ElapsedMilliseconds < 500, $"the close took {watch.ElapsedMilliseconds} ms");

            // The draw that runs still has its pictures, and no new draw may start.
            Assert.True(view.Gate.IsClosed);
            Assert.False(view.PicturesReleased);
            Assert.False(view.Gate.TryEnter());

            view.Gate.Exit();
            Assert.True(view.PicturesReleased);
            Assert.True(view.IsReleased);
        }
        finally
        {
            harness.Dispose();
        }
    }

    [AvaloniaFact]
    public void ClosingATabWithNoDrawInFlightReleasesThePicturesAtOnce()
    {
        var harness = new ViewHarness(WindowFixture.Thesis);
        harness.Capture();
        harness.Tab.Dispose();
        Assert.True(harness.View.PicturesReleased);
        harness.Dispose();
    }

    [Fact]
    public async Task TheGateFreesAfterTheLastDrawEndsAndNeverBefore()
    {
        var gate = new DrawGate();
        var freed = 0;
        Assert.True(gate.TryEnter());
        Assert.True(gate.TryEnter());
        gate.Close(() => Interlocked.Increment(ref freed));
        Assert.True(gate.IsClosed);
        Assert.False(gate.TryEnter());
        gate.Exit();
        Assert.Equal(0, Volatile.Read(ref freed));

        // The last draw ends on another thread (the render thread): the action runs there, after it ended.
        var thread = Task.Run(gate.Exit, TestContext.Current.CancellationToken);
        await thread;
        Assert.Equal(1, Volatile.Read(ref freed));

        // A second close does nothing; a gate with no draw in flight frees at once.
        gate.Close(() => Interlocked.Increment(ref freed));
        Assert.Equal(1, Volatile.Read(ref freed));
        var idle = new DrawGate();
        var ran = false;
        idle.Close(() => ran = true);
        Assert.True(ran);
        Assert.False(idle.TryEnter());
    }

    [Fact]
    public void ZoomByWheelTakesOneStepPerWholeNotchAndCollectsTheRest()
    {
        var remainder = 0.0;
        Assert.Equal(1.0, PageView.ZoomByWheel(1.0, 0.4, ref remainder));
        Assert.Equal(1.25, PageView.ZoomByWheel(1.0, 0.6, ref remainder));
        Assert.Equal(1.0, PageView.ZoomByWheel(1.0, double.NaN, ref remainder));
    }

    [AvaloniaFact]
    public async Task ARunnerThatIsStartedWithTheWritingViewMakesTheStripsAfterTheFullOpen()
    {
        using var harness = new ViewHarness(WindowFixture.Thesis, 1100, 700, WriteView.Writing, ZoomMode.FitWidth);
        var view = harness.View;
        await ViewHarness.WaitAsync(() => view.Runner.FlowLayout is not null && view.PageCount > 0);
        Assert.True(view.Runner.FullModelReady);
        Assert.True(view.Runner.FlowLayout!.Page(0).IsStrip);
        Assert.True(view.Zoom <= PageView.WritingFitLimit + 1e-9);
        await ViewHarness.WaitAsync(() => view.Runner.FlowLayout.IsComplete, 30000);
        Assert.True(view.PageCount >= 1);
        Assert.Equal(ZoomSteps.Clamp(view.Zoom), view.Zoom);
    }

    [AvaloniaFact]
    public void TheSettingsDecideTheModeAndTheZoomOfAView()
    {
        using var harness = new ViewHarness(WindowFixture.Poi, mode: WriteView.Writing, zoom: ZoomMode.FitPage);
        Assert.Equal(WriteView.Writing, harness.View.Mode);
        Assert.Equal(ZoomMode.FitPage, harness.View.ZoomMode);
    }
}
