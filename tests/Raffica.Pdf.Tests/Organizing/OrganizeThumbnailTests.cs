using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Raffica.Core.Pages;
using Raffica.Core.Viewing;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

public sealed class OrganizeThumbnailTests
{
    private static readonly TileKey First = new(0, TileKey.PreviewScaleKey, 0, 0);

    private static OrganizeThumbnail ThumbnailOf(OrganizeListFixture f, int position) =>
        f.Item(position).GetVisualDescendants().OfType<OrganizeThumbnail>().Single();

    // The BGRA pixels of a control drawn on its own.
    private static (byte[] Pixels, int Width, int Height) Draw(Control control)
    {
        var size = new PixelSize((int)control.Bounds.Width, (int)control.Bounds.Height);
        using var frame = new RenderTargetBitmap(size, new Vector(96, 96));
        frame.Render(control);
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var buffer = Marshal.AllocHGlobal(pixels.Length);
        try
        {
            frame.CopyPixels(new PixelRect(size), buffer, pixels.Length, stride);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return (pixels, size.Width, size.Height);
    }

    // The centre of the dark pixels inside the frame of the thumbnail, or null if there are none.
    private static (double X, double Y)? Centroid(byte[] pixels, int width, int height)
    {
        double sx = 0, sy = 0;
        var n = 0;
        for (var y = 4; y < height - 4; y++)
        {
            for (var x = 4; x < width - 4; x++)
            {
                var i = (y * width + x) * 4;
                if (pixels[i] + pixels[i + 1] + pixels[i + 2] < 3 * 110)
                {
                    sx += x;
                    sy += y;
                    n++;
                }
            }
        }

        return n == 0 ? null : (sx / n, sy / n);
    }

    [AvaloniaFact]
    public async Task RotatedThumbnailIsDrawnTurned()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        await f.Inner.FirstSharpAsync();
        var thumbnails = f.Tab.Session.EnsureThumbnails()!;
        await ViewFixture.WaitAsync(() => thumbnails.Cache.Contains(new TileKey(2, TileKey.PreviewScaleKey, 0, 0)));
        ViewFixture.Pump();

        // Page 3 of pages-marked.pdf is landscape (400 x 300).
        var plain = ThumbnailOf(f, 2);
        Assert.True(plain.Bounds.Width > plain.Bounds.Height);
        var before = Draw(plain);
        var beforeCentre = Centroid(before.Pixels, before.Width, before.Height);

        f.Session.Selection.Select(2);
        f.Session.Rotate(90);
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => thumbnails.Cache.Contains(new TileKey(2, TileKey.PreviewScaleKey, 0, 0)));
        var turned = ThumbnailOf(f, 2);
        Assert.True(turned.Bounds.Height > turned.Bounds.Width);
        Assert.NotNull(f.List.Thumbnails.BitmapFor(f.Session.Plan.Slots[2]));
        var after = Draw(turned);
        var afterCentre = Centroid(after.Pixels, after.Width, after.Height);

        // A quarter turn clockwise takes the point (x, y) of a w x h picture to (h - y, x) of the h x w one.
        Assert.NotNull(beforeCentre);
        Assert.NotNull(afterCentre);
        Assert.InRange(afterCentre.Value.X, before.Height - beforeCentre.Value.Y - 8, before.Height - beforeCentre.Value.Y + 8);
        Assert.InRange(afterCentre.Value.Y, beforeCentre.Value.X - 8, beforeCentre.Value.X + 8);
    }

    [AvaloniaFact]
    public async Task InsertedPageThumbnailComesFromItsOwnSource()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        await f.Inner.FirstSharpAsync();
        Assert.Equal(0, f.List.Thumbnails.InsertedSourceCount);
        var inserted = await f.Session.InsertFileAsync(TestData.Pdf("hello_world.pdf"), null, "all", 0);
        Assert.True(inserted.IsDone);
        ViewFixture.Pump();

        var id = f.Session.Plan.Slots[0].Source;
        Assert.NotEqual(0, id.Value);
        await ViewFixture.WaitAsync(() => f.List.Thumbnails.InsertedSource(id) is { } s && s.Cache.Contains(First));
        var own = f.List.Thumbnails.InsertedSource(id)!;
        Assert.NotSame(f.Tab.Session.EnsureThumbnails(), own);
        Assert.Contains(f.List.Thumbnails.PlanOf(id), r => r.Key.Page == 0);
        ViewFixture.Pump();
        Assert.NotNull(f.List.Thumbnails.BitmapFor(f.Session.Plan.Slots[0]));
    }

    [AvaloniaFact]
    public async Task BlankPageIsDrawnNotRendered()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        await f.Inner.FirstSharpAsync();
        f.Session.InsertBlank(0, new BlankPage(595, 842));
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => f.List.Thumbnails.PlanOf(default).Count > 0);

        Assert.True(f.Session.Plan.Slots[0].Blank is not null);
        Assert.Null(f.List.Thumbnails.BitmapFor(f.Session.Plan.Slots[0]));
        Assert.All(f.List.Thumbnails.PlanOf(default), r => Assert.True(r.Key.Page >= 0));
        Assert.Equal(f.List.Thumbnails.PlanOf(default).Select(r => r.Key.Page).Distinct().Count(), f.List.Thumbnails.PlanOf(default).Count);
        var blank = ThumbnailOf(f, 0);
        Assert.True(blank.Bounds.Width > 0 && blank.Bounds.Height > blank.Bounds.Width);
        Assert.Equal(0, f.List.Thumbnails.InsertedSourceCount);
    }

    [AvaloniaFact]
    public async Task ThumbnailsAreRequestedOnlyAfterTheFirstSharpPageAndOnlyWhileShown()
    {
        // The view of the tab is not shown, so it never draws a sharp page: the list requests nothing.
        using (var hidden = await OrganizeListFixture.OpenAsync(showView: false))
        {
            for (var i = 0; i < 8; i++)
            {
                await Task.Delay(25);
                ViewFixture.Pump();
            }

            Assert.False(hidden.List.HasStarted);
            Assert.Empty(hidden.List.Thumbnails.PlanOf(default));
            Assert.Equal(0, hidden.Tab.Session.EnsureThumbnails()!.Scheduler.PendingCount);
        }

        using var f = await OrganizeListFixture.OpenAsync();
        await f.Inner.FirstSharpAsync();
        await ViewFixture.WaitAsync(() => f.List.Thumbnails.PlanOf(default).Count > 0);
        Assert.All(f.List.Thumbnails.PlanOf(default), r => Assert.Contains(r.Key.Page, f.List.RealizedPositions));

        // Out of the tree (the tab is not shown): no wish, no converted bitmaps.
        var left = (Raffica.Shell.Frame.Pane)f.List.Parent!;
        left.Content = null;
        ViewFixture.Pump();
        Assert.Empty(f.List.Thumbnails.PlanOf(default));
        Assert.Equal(0, f.List.Thumbnails.BitmapCount);
    }

    [AvaloniaFact]
    public async Task ThumbnailSourceOfAnInsertedFileIsDisposedWhenTheModeEnds()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        await f.Inner.FirstSharpAsync();
        Assert.True((await f.Session.InsertFileAsync(TestData.Pdf("hello_world.pdf"), null, "all", 0)).IsDone);
        ViewFixture.Pump();
        var id = f.Session.Plan.Slots[0].Source;
        await ViewFixture.WaitAsync(() => f.List.Thumbnails.InsertedSource(id) is { } s && s.Cache.Contains(First));
        var own = f.List.Thumbnails.InsertedSource(id)!;

        // The file's last slot goes: its source goes with it; undo makes it again on first need.
        f.Session.Selection.Select(0);
        f.Session.Delete();
        ViewFixture.Pump();
        Assert.Equal(0, f.List.Thumbnails.InsertedSourceCount);
        Assert.Equal(0, own.Cache.Count);
        f.Session.Undo();
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => f.List.Thumbnails.InsertedSource(id) is { } again && again.Cache.Contains(First));
        var second = f.List.Thumbnails.InsertedSource(id)!;
        Assert.NotSame(own, second);

        f.Session.End();
        ViewFixture.Pump();
        Assert.Equal(0, f.List.Thumbnails.InsertedSourceCount);
        Assert.Equal(0, second.Cache.Count);
    }
}
