using Avalonia.Headless.XUnit;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>The app never decodes a picture on the render thread: the opener decodes page 1's, the view prefetches around what is visible, a draw only draws what is decoded.</summary>
public sealed class PageViewPicturesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-pictures-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static ImageRef[] PicturesOf(DocumentLayout layout, int page) =>
        [.. layout.Page(page).Items.OfType<ImageItem>().Select(i => i.Image)];

    [AvaloniaFact]
    public async Task PageOneHasItsPicturesDecodedBeforeTheFirstSharpPage()
    {
        var path = PictureDocx.Write(_folder, pages: 4, perPage: 2);
        using var h = new ViewHarness(path);

        // The first frame draws page 1 on the render thread and the event follows on the UI thread: the harness's one pump does not wait for either (a cold first run happened to
        // be slow enough, a warm one or a fast runner is not), so a frame is asked for and the event is waited for, as in PageViewTests.FirstPageIsDrawn.
        h.Capture();
        await ViewHarness.WaitAsync(() => h.FirstSharpCount >= 1);
        ViewHarness.Pump();
        Assert.Equal(1, h.FirstSharpCount);
        var pageOne = PicturesOf(h.Tab.Layout, 0);
        Assert.Equal(2, pageOne.Length);

        // Decoded by the opener (a pool thread in the app) before the view existed: the first draw found them, no draw decoded anything.
        Assert.Equal(pageOne.Length, h.DecodesAtFirstSharp);
        Assert.All(pageOne, p => Assert.NotNull(h.Tab.Pictures.TryGet(p)));
    }

    [AvaloniaFact]
    public void ADrawNeverDecodes()
    {
        var path = PictureDocx.Write(_folder, pages: 3);
        using var h = new ViewHarness(path);
        var pictures = h.Tab.Pictures;

        // No prefetch from here on: only a draw could decode now.
        h.View.PrefetchEnabled = false;
        var rect = h.View.PageBounds(0)!.Value;
        var withPicture = h.Capture().Hash((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height / 2);
        var before = h.View.DrawCount;
        pictures.Clear();
        pictures.ReleaseRetired();
        var decodes = pictures.DecodeCount;
        var refs = PicturesOf(h.Tab.Layout, 0);
        Assert.Single(refs);

        // The draw meets an undecoded picture and draws the marked box instead of decoding.
        h.View.InvalidateVisual();
        var shot = h.Capture();
        Assert.True(h.View.DrawCount > before, "a frame was drawn");
        Assert.Equal(decodes, pictures.DecodeCount);
        Assert.Null(pictures.TryGet(refs[0]));
        Assert.NotEqual(withPicture, shot.Hash((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height / 2));
    }

    [AvaloniaFact]
    public async Task ScrollingPrefetchesThePagesInViewAndOnePageEachSide()
    {
        var path = PictureDocx.Write(_folder, pages: 20);
        using var h = new ViewHarness(path);
        var uiThread = Environment.CurrentManagedThreadId;
        var threads = new List<int>();
        var decodedEvents = 0;
        h.Tab.Pictures.Decoded += () =>
        {
            lock (threads)
            {
                threads.Add(Environment.CurrentManagedThreadId);
                decodedEvents++;
            }
        };

        await h.LayOutEverythingAsync();

        var layout = h.Tab.Layout;
        h.View.ScrollToPage(10);
        ViewHarness.Pump();
        var (first, last) = h.View.Scroller!.Visible(h.View.OffsetY, h.View.Viewport.Height);
        var wanted = Enumerable.Range(Math.Max(0, first - 1), last - first + 3).SelectMany(p => PicturesOf(layout, p)).ToArray();
        Assert.True(wanted.Length >= 3);
        await ViewHarness.WaitAsync(() => wanted.All(p => h.Tab.Pictures.TryGet(p) is not null));

        // Far pages were not touched, the decoding was off the UI thread, and the view asked for a frame because of it (once per burst).
        Assert.Null(h.Tab.Pictures.TryGet(PicturesOf(layout, 17)[0]));
        lock (threads)
        {
            Assert.NotEmpty(threads);
            Assert.DoesNotContain(uiThread, threads);
        }

        await ViewHarness.WaitAsync(() => h.View.PictureRedraws >= 1);
        Assert.InRange(h.View.PictureRedraws, 1, decodedEvents);
    }

    [AvaloniaFact]
    public void ACacheDisposeWaitsForTheDrawGate()
    {
        var path = PictureDocx.Write(_folder, pages: 2);
        var h = new ViewHarness(path);
        try
        {
            var picture = PicturesOf(h.Tab.Layout, 0)[0];
            Assert.True(h.View.Gate.TryEnter(), "a draw is in flight");
            h.Tab.Dispose();

            Assert.True(h.View.Gate.IsClosed);
            Assert.False(h.View.PicturesReleased);
            Assert.NotNull(h.Tab.Pictures.TryGet(picture));

            h.View.Gate.Exit();
            Assert.True(h.View.PicturesReleased);
            Assert.Null(h.Tab.Pictures.TryGet(picture));
        }
        finally
        {
            h.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task PictureHeavyFilesStayUnderTheBudget()
    {
        // 30 pictures of 100 x 100 pixels (40,000 bytes decoded each) against a budget of ten of them.
        var path = PictureDocx.Write(_folder, pages: 30, pixels: 100);
        using var h = new ViewHarness(path, pictureLimits: new ImageCacheLimits(400_000, 20_000));
        await h.LayOutEverythingAsync();

        for (var page = 0; page < 30; page += 2)
        {
            h.View.ScrollToPage(page);
            h.Capture();
            await Task.Delay(20);
        }

        await ViewHarness.WaitAsync(() => h.Tab.Pictures.DecodeCount > 10);
        h.Capture();
        Assert.InRange(h.Tab.Pictures.DecodedBytes, 1, 400_000);
    }

    [AvaloniaFact]
    public async Task EvictedPicturesAreDisposedByTheDrawThatMayHaveHeldThem()
    {
        var path = PictureDocx.Write(_folder, pages: 12, pixels: 100);
        using var h = new ViewHarness(path, pictureLimits: new ImageCacheLimits(160_000, 20_000));
        await h.LayOutEverythingAsync();
        for (var page = 0; page < 12; page++)
        {
            h.View.ScrollToPage(page);
            h.Capture();
            await Task.Delay(30);
        }

        // Prefetch evicts (retires) from a pool thread and every draw ends with ReleaseRetired inside the gate: after the last draw nothing waits.
        await ViewHarness.WaitAsync(() => h.Tab.Pictures.DecodeCount > 4);
        h.View.InvalidateVisual();
        h.Capture();
        Assert.Equal(0, h.Tab.Pictures.RetiredCount);
    }

    [AvaloniaFact]
    public async Task AnEditThatMovesAPictureIntoViewIsPrefetchedWithoutScrolling()
    {
        // Pages: "First", "Second", then the picture on page 3. The view shows page 1 and prefetches pages 1 and 2: the picture is out of range.
        var path = PictureDocx.Write(_folder, pages: 1, pixels: 8, name: "moves.docx", leadingText: ["First", "Second"]);
        using var h = new ViewHarness(path, 900, 400);
        await h.LayOutEverythingAsync();
        var picture = PicturesOf(h.Tab.Layout, 2).Single();
        await Task.Delay(100);
        ViewHarness.Pump();
        Assert.False(h.Tab.Pictures.IsKnown(picture), "the picture is out of the prefetched range");

        // Joining the two text paragraphs removes page 2: the picture moves up to page 2, the page count changes but the view does not scroll.
        var model = h.Tab.Docx.Model;
        var first = (Paragraph)model.Body.Blocks[0];
        var second = (Paragraph)model.Body.Blocks[1];
        var redraws = h.View.PictureRedraws;
        Assert.NotNull(h.View.ApplyOperation(e => e.Delete(new TextRange(new TextPosition(StoryRef.Body, first, 5), new TextPosition(StoryRef.Body, second, 6)))));
        ViewHarness.Pump();

        await ViewHarness.WaitAsync(() => h.Tab.Pictures.IsKnown(picture));
        Assert.NotNull(h.Tab.Pictures.TryGet(picture));
        await ViewHarness.WaitAsync(() => h.View.PictureRedraws > redraws);
        Assert.Equal(0, h.View.OffsetY);
    }

    [AvaloniaFact]
    public async Task ARangeWhosePicturesExceedTheBudgetDoesNotPrefetchForever()
    {
        // Six pictures of 40,000 decoded bytes in a range against a budget of two: every pass evicts what it decoded, so "not known" never ends by itself.
        var path = PictureDocx.Write(_folder, pages: 20, perPage: 2, pixels: 100);
        using var h = new ViewHarness(path, 900, 400, pictureLimits: new ImageCacheLimits(100_000, 12_500));
        await h.LayOutEverythingAsync();
        h.View.ScrollToPage(8);
        var layout = h.Tab.Layout;
        var (first, last) = h.View.Scroller!.Visible(h.View.OffsetY, h.View.Viewport.Height);
        var inRange = Enumerable.Range(Math.Max(0, first - 1), last - first + 3).SelectMany(p => PicturesOf(layout, p)).Count();
        Assert.True(inRange * 40_000 > 100_000, "the range holds more than the budget");

        // The view goes idle: after a bounded settle (the pump runs every end of a task) no further task starts.
        for (var i = 0; i < 100; i++)
        {
            await Task.Delay(10);
            ViewHarness.Pump();
        }

        var starts = h.View.PrefetchStarts;
        var decodes = h.Tab.Pictures.DecodeCount;
        for (var i = 0; i < 50; i++)
        {
            await Task.Delay(10);
            ViewHarness.Pump();
        }

        Assert.Equal(starts, h.View.PrefetchStarts);
        Assert.Equal(decodes, h.Tab.Pictures.DecodeCount);
        Assert.InRange(starts, 1, 6);
    }

    [AvaloniaFact]
    public async Task ABurstOfRangeChangesRunsOneTaskAtATimeAndEndsWithTheNewestRange()
    {
        var path = PictureDocx.Write(_folder, pages: 24);
        using var h = new ViewHarness(path);
        await h.LayOutEverythingAsync();
        await ViewHarness.WaitAsync(() => h.View.PrefetchStarts >= 1);
        await Task.Delay(150);
        ViewHarness.Pump();
        var starts = h.View.PrefetchStarts;

        // Six ranges one after the other with no frame between: the view never has two tasks, the last range waits behind the first.
        foreach (var page in new[] { 4, 8, 12, 16, 20, 22 })
        {
            h.View.ScrollToPage(page);
        }

        Assert.InRange(h.View.PrefetchStarts - starts, 0, 1);
        var (first, last) = h.View.Scroller!.Visible(h.View.OffsetY, h.View.Viewport.Height);
        var layout = h.Tab.Layout;
        var from = Math.Max(0, first - 1);
        var to = Math.Min(layout.LaidOutPages - 1, last + 1);
        var wanted = Enumerable.Range(from, to - from + 1).SelectMany(p => PicturesOf(layout, p)).ToArray();
        await ViewHarness.WaitAsync(() => wanted.All(p => h.Tab.Pictures.IsKnown(p)));
        Assert.InRange(h.View.PrefetchStarts - starts, 1, 2);

        // The ranges in between were dropped, not decoded one after the other.
        Assert.False(h.Tab.Pictures.IsKnown(PicturesOf(layout, 10).Single()));
    }

    [AvaloniaFact]
    public async Task ARangeCancelledByANewerOneAndScrolledBackToBeforeItEndedIsDecodedInFull()
    {
        // 2 pictures on each of 20 pages, 400 pt high window: a range is the visible pages and one each side.
        var path = PictureDocx.Write(_folder, pages: 20, perPage: 2);
        var gate = new ManualResetEventSlim(initialState: true);
        var entered = 0;
        using var h = new ViewHarness(path, 900, 400, wrapPictureBytes: inner => id =>
        {
            Interlocked.Increment(ref entered);
            gate.Wait();
            return inner(id);
        });
        await h.LayOutEverythingAsync();
        await ViewHarness.WaitAsync(() => h.View.PrefetchStarts >= 1);
        await Task.Delay(150);
        ViewHarness.Pump();

        // Range A is scrolled to while the cache cannot read: the task starts and waits inside its first picture.
        gate.Reset();
        var before = Volatile.Read(ref entered);
        h.View.ScrollToPage(6);
        var layout = h.Tab.Layout;
        List<ImageRef> RangeNow()
        {
            var (first, last) = h.View.Scroller!.Visible(h.View.OffsetY, h.View.Viewport.Height);
            var from = Math.Max(0, first - 1);
            var to = Math.Min(layout.LaidOutPages - 1, last + 1);
            return [.. Enumerable.Range(from, to - from + 1).SelectMany(p => PicturesOf(layout, p))];
        }

        var rangeA = RangeNow();
        Assert.True(rangeA.Count >= 4, "a range holds several pictures");
        Assert.All(rangeA, p => Assert.False(h.Tab.Pictures.IsKnown(p)));
        await ViewHarness.WaitAsync(() => Volatile.Read(ref entered) > before);

        // A newer range B cancels the task (it waits inside its first picture); then the view goes back to A before the task has ended.
        h.View.ScrollToPage(14);
        h.View.ScrollToPage(6);
        var starts = h.View.PrefetchStarts;

        // The task ends after that picture (its token is cancelled) and leaves the others of A; the view must ask for them again.
        gate.Set();
        await ViewHarness.WaitAsync(() => rangeA.All(p => h.Tab.Pictures.IsKnown(p)), 5000);
        Assert.True(h.View.PrefetchStarts > starts, "a second task decoded the rest");
    }
}
