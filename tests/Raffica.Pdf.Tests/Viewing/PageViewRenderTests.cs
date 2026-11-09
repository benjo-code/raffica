using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Viewing;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Viewing;

public sealed class PageViewRenderTests
{
    private static (byte B, byte G, byte R) PixelAt((byte[] Pixels, int Stride) frame, double x, double y)
    {
        var offset = ((int)y * frame.Stride) + ((int)x * 4);
        return (frame.Pixels[offset], frame.Pixels[offset + 1], frame.Pixels[offset + 2]);
    }

    [AvaloniaFact]
    public async Task FirstSharpPageFiresOnceAfterThePageIsPainted()
    {
        var fired = 0;
        var painted = new List<bool>();
        using var fixture = await ViewFixture.OpenAsync("text-basic.pdf", configure: v => v.FirstSharpPage += () =>
        {
            fired++;
            painted.Add(v.FirstSharpPageShown);
        });
        var view = fixture.View;

        await fixture.SettleAsync();
        Assert.True(fixture.Session.Cache!.Contains(fixture.View.LastPlan.First(request => !request.Key.IsPreview).Key));
        fixture.Capture();
        await ViewFixture.WaitAsync(() => fired > 0);
        Assert.True(view.FirstSharpPageShown);

        // Later passes and later tiles do not fire it again.
        fixture.Capture();
        view.Offset = new Vector(0, 50);
        await fixture.SettleAsync();
        fixture.Capture();
        ViewFixture.Pump();
        Assert.Equal(1, fired);
        Assert.All(painted, Assert.True);
    }

    [AvaloniaFact]
    public async Task DarkPageUsesTheDarkBackgroundAndKeepsImagesInColour()
    {
        using var fixture = await ViewFixture.OpenAsync("dark-sample.pdf", 900, 1300, prepare: session => session.SetPageTheme(PageTheme.Dark));
        var view = fixture.View;
        Assert.Equal(PageTheme.Dark, view.PageTheme);
        await fixture.SettleAsync();

        var frame = fixture.Capture();
        var page = view.PageBounds(0)!.Value;

        // Empty ground of the page: the dark background of Core, not white and not an inverted picture.
        var ground = PixelAt(frame, page.X + (page.Width * 0.5), page.Y + (page.Height * 0.9));
        Assert.Equal(((byte)(PageThemeColors.DarkBackground & 0xFF), (byte)((PageThemeColors.DarkBackground >> 8) & 0xFF), (byte)((PageThemeColors.DarkBackground >> 16) & 0xFF)), ground);

        // The RGB gradient image (x 8 to 30 %, y 46 to 61 % of the page): near its corners it is blue and yellow, far from grey.
        foreach (var (x, y) in new[] { (0.105, 0.47), (0.28, 0.59) })
        {
            var image = PixelAt(frame, page.X + (page.Width * x), page.Y + (page.Height * y));
            var spread = Math.Max(image.R, Math.Max(image.G, image.B)) - Math.Min(image.R, Math.Min(image.G, image.B));
            Assert.True(spread > 120, $"the image area must stay coloured, but the pixel at {x}, {y} is {image}");
        }

        // Switching to the light page paints white ground and the same colours.
        view.PageTheme = PageTheme.Light;
        await fixture.SettleAsync();
        var light = fixture.Capture();
        Assert.Equal(((byte)255, (byte)255, (byte)255), PixelAt(light, page.X + (page.Width * 0.5), page.Y + (page.Height * 0.9)));
    }

    [AvaloniaFact]
    public async Task InvalidatedPagesAreRerendered()
    {
        using var fixture = await ViewFixture.OpenAsync("text-basic.pdf");
        await fixture.SettleAsync();
        fixture.Capture();
        var cache = fixture.Session.Cache!;
        var key = fixture.View.LastPlan.First(request => !request.Key.IsPreview && request.Key.Page == 0).Key;
        Assert.True(cache.TryGet(key, out var old));
        Assert.True(fixture.View.Bitmaps.Count > 0);

        fixture.Session.InvalidatePages(new HashSet<int> { 0 });
        Assert.False(cache.Contains(key));
        ViewFixture.Pump();

        // The view planned again by itself: the page is rendered anew (a new image), and the old bitmaps of the page are gone.
        await ViewFixture.WaitAsync(() => cache.TryGet(key, out var fresh) && !ReferenceEquals(fresh, old));
        await fixture.SettleAsync();
        Assert.True(cache.Contains(key));
        var frame = fixture.Capture();
        Assert.NotEqual(default, PixelAt(frame, 10, 10));
    }

    [AvaloniaFact]
    public async Task NothingIsRequestedForATabThatIsNotActive()
    {
        // A view that is not on screen (not in a window) plans nothing and leaves the scheduler idle.
        using var off = await ViewFixture.OpenAsync("text-basic.pdf", show: false);
        Assert.Empty(off.View.LastPlan);
        Assert.Equal(0, off.Session.Scheduler!.PendingCount);

        // Shown, it requests; taken off the screen again, it drops its requests.
        using var shown = await ViewFixture.OpenAsync("text-rich.pdf");
        Assert.NotEmpty(shown.View.LastPlan);
        shown.Window.Content = null;
        ViewFixture.Pump();
        Assert.Empty(shown.View.LastPlan);
        Assert.Equal(0, shown.Session.Scheduler!.PendingCount);
    }

    [AvaloniaFact]
    public async Task TheDocumentSlotShowsTheActiveTabAndOnlyThatTabRequests()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-pageview-").FullName;
        try
        {
            var options = PdfOptions.Parse(["--settings-file", Path.Combine(folder, "settings.json")]);
            var window = new PdfWindow(options, new Raffica.Shell.Frame.ShellSettings());
            window.Show();
            ViewFixture.Pump();

            var first = await window.Flow.OpenAsync(TestData.Pdf("text-basic.pdf"));
            var second = await window.Flow.OpenAsync(TestData.Pdf("text-rich.pdf"));
            ViewFixture.Pump();
            Assert.NotNull(first);
            Assert.NotNull(second);

            // The second tab is active and shown; the first tab's view has left the screen and requests nothing.
            var secondView = Assert.IsType<PageView>(window.DocumentPane.Content);
            Assert.Same(secondView, window.ViewOf(second));
            Assert.Same(second, secondView.Tab);
            Assert.NotEmpty(secondView.LastPlan);
            var firstView = window.ViewOf(first);
            Assert.NotNull(firstView);
            Assert.Empty(firstView.LastPlan);
            Assert.Equal(0, first.Session.Scheduler!.PendingCount);

            // Switching back shows the first view again (the same one, with its state) and quietens the second.
            window.Workspace.Select(first);
            ViewFixture.Pump();
            Assert.Same(firstView, window.DocumentPane.Content);
            Assert.NotEmpty(firstView.LastPlan);
            Assert.Empty(secondView.LastPlan);

            // Closing a tab drops its view.
            Assert.True(await window.Workspace.CloseAsync(second));
            ViewFixture.Pump();
            Assert.Null(window.ViewOf(second));
            window.Close();
            ViewFixture.Pump();
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
