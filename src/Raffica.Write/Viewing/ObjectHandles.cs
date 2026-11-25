using Avalonia;
using Raffica.Write.Layout;

namespace Raffica.Write.Viewing;

/// <summary>Where one of the eight handles of a picture is: <paramref name="Square"/> is the square that is drawn and <paramref name="HitArea"/> the larger one that takes the pointer; both in DIPs from the top left of the page.</summary>
internal readonly record struct HandlePlacement(HandleSlot Slot, Rect Square, Rect HitArea);

/// <summary>The geometry of the handles of a selected picture (milestone 4.5a), in DIPs. Pure: no visual, no model.</summary>
internal static class ObjectHandles
{
    /// <summary>The side of a drawn handle, in DIPs.</summary>
    public const double DrawnSize = 8;

    /// <summary>The side of the area that takes the pointer, in DIPs: larger than the handle so that it is easy to hit.</summary>
    public const double HitSize = 12;

    /// <summary>
    /// The eight handles of the box, at its corners and edge middles, clockwise from the top left. <paramref name="scale"/> is DIPs per point (the page scale of the view); the squares are centred on
    /// the points of <see cref="PictureBox.HandleCentre"/>, so a hit at a centre is a hit of the layout's hit test too.
    /// </summary>
    public static IReadOnlyList<HandlePlacement> Slots(PictureBox box, float scale)
    {
        ArgumentNullException.ThrowIfNull(box);
        var result = new HandlePlacement[8];
        for (var i = 0; i < result.Length; i++)
        {
            var slot = (HandleSlot)i;
            var (x, y) = box.HandleCentre(slot);
            double cx = x * scale;
            double cy = y * scale;
            result[i] = new HandlePlacement(
                slot,
                new Rect(cx - (DrawnSize / 2), cy - (DrawnSize / 2), DrawnSize, DrawnSize),
                new Rect(cx - (HitSize / 2), cy - (HitSize / 2), HitSize, HitSize));
        }

        return result;
    }

    /// <summary>The reach of a handle for <c>DocumentLayout.HitObject</c>: half the hit area, in points.</summary>
    public static float TolerancePoints(float scale) => scale > 0 ? (float)(HitSize / 2 / scale) : 0f;

    /// <summary>The rectangle a box takes when the pointer drags the handle <paramref name="slot"/> by (<paramref name="dx"/>, <paramref name="dy"/>) DIPs: the dragged edges move, the opposite ones stay; a box cannot turn inside out (a side is at least one DIP).</summary>
    public static Rect Resize(Rect box, HandleSlot slot, double dx, double dy)
    {
        var (left, top, right, bottom) = (box.Left, box.Top, box.Right, box.Bottom);
        if (slot is HandleSlot.TopLeft or HandleSlot.Left or HandleSlot.BottomLeft)
        {
            left = Math.Min(left + dx, right - 1);
        }

        if (slot is HandleSlot.TopRight or HandleSlot.Right or HandleSlot.BottomRight)
        {
            right = Math.Max(right + dx, left + 1);
        }

        if (slot is HandleSlot.TopLeft or HandleSlot.Top or HandleSlot.TopRight)
        {
            top = Math.Min(top + dy, bottom - 1);
        }

        if (slot is HandleSlot.BottomLeft or HandleSlot.Bottom or HandleSlot.BottomRight)
        {
            bottom = Math.Max(bottom + dy, top + 1);
        }

        return new Rect(left, top, right - left, bottom - top);
    }
}
