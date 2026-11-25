using Avalonia;
using Avalonia.Headless.XUnit;
using Raffica.Write.Layout;
using Raffica.Write.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>The geometry of the eight handles of a selected picture (milestone 4.5a, Task 5).</summary>
public sealed class ObjectHandlesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-handles-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // The system cleans the temp folder.
        }
    }

    // The box of the picture of the object document, on page 1.
    private PictureBox Box(out ViewHarness harness)
    {
        harness = new ViewHarness(ObjectDocx.Write(_folder));
        return Assert.Single(harness.View.Runner.PageLayout.Page(0).Objects.Pictures);
    }

    [AvaloniaFact]
    public void HandlesSitOnTheCornersAndEdges()
    {
        var box = Box(out var h);
        using var _ = h;
        const float scale = 2f;
        var slots = ObjectHandles.Slots(box, scale);

        Assert.Equal(8, slots.Count);
        Assert.Equal(Enum.GetValues<HandleSlot>(), slots.Select(s => s.Slot).ToArray());
        var (x, y, w, tall) = (box.X * scale, box.Y * scale, box.Width * scale, box.Height * scale);
        var expected = new (double X, double Y)[]
        {
            (x, y), (x + (w / 2), y), (x + w, y), (x + w, y + (tall / 2)), (x + w, y + tall), (x + (w / 2), y + tall), (x, y + tall), (x, y + (tall / 2)),
        };
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(expected[i].X, slots[i].Square.Center.X, 3);
            Assert.Equal(expected[i].Y, slots[i].Square.Center.Y, 3);
            Assert.Equal(slots[i].Square.Center, slots[i].HitArea.Center);
        }
    }

    [AvaloniaFact]
    public void TheHitAreaIsLargerThanTheDrawnHandle()
    {
        var box = Box(out var h);
        using var _ = h;
        foreach (var slot in ObjectHandles.Slots(box, 1.5f))
        {
            Assert.Equal(8, slot.Square.Width);
            Assert.Equal(8, slot.Square.Height);
            Assert.Equal(12, slot.HitArea.Width);
            Assert.Equal(12, slot.HitArea.Height);
            Assert.True(slot.HitArea.Contains(new Point(slot.Square.Left, slot.Square.Top)));
        }

        // The reach of the layout's hit test is the half of the hit area in points: a point 5 DIPs from a centre hits it, one of 7 DIPs does not.
        const float scale = 2f;
        Assert.Equal(6f / scale, ObjectHandles.TolerancePoints(scale), 4);
    }

    [AvaloniaFact]
    public void ResizingMovesTheDraggedEdgesOnly()
    {
        var box = new Rect(100, 100, 80, 60);
        Assert.Equal(new Rect(100, 100, 100, 60), ObjectHandles.Resize(box, HandleSlot.Right, 20, 999));
        Assert.Equal(new Rect(90, 110, 90, 50), ObjectHandles.Resize(box, HandleSlot.TopLeft, -10, 10));
        // A box never turns inside out.
        var squeezed = ObjectHandles.Resize(box, HandleSlot.Left, 500, 0);
        Assert.True(squeezed.Width >= 1);
        Assert.Equal(box.Right, squeezed.Right);
    }
}
