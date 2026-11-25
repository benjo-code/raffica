using Avalonia;
using Avalonia.Input;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace Raffica.Write.Viewing;

/// <summary>
/// The arithmetic of the picture gestures (milestone 4.5a), in points of a page, pure: no visual, no model. The commit of a gesture and the outline the overlay draws while the pointer is down use the same functions,
/// so what is shown is what is written.
/// </summary>
internal static class PictureGestures
{
    /// <summary>The smallest side a gesture makes a picture: 0.3 cm, 8.5 points.</summary>
    public const double MinSizePt = 8.5;

    /// <summary>The largest side a gesture makes a picture: Word's limit, 1584 points (the editor's own limit).</summary>
    public const double MaxSizePt = PictureRules.MaxExtentEmu / (double)PictureRules.EmuPerPoint;

    /// <summary>
    /// The frame the picture has when the handle <paramref name="slot"/> is dragged by (<paramref name="dx"/>, <paramref name="dy"/>) points. A corner keeps the proportions (the side that moved more decides) unless
    /// <paramref name="free"/> (Shift) says otherwise; an edge changes one side only. The dragged edges move and the opposite ones stay. Each side stays between <see cref="MinSizePt"/> and <see cref="MaxSizePt"/>
    /// (a kept ratio holds both sides inside the range together).
    /// </summary>
    public static Rect Resize(Rect box, HandleSlot slot, double dx, double dy, bool free)
    {
        var left = slot is HandleSlot.TopLeft or HandleSlot.Left or HandleSlot.BottomLeft;
        var right = slot is HandleSlot.TopRight or HandleSlot.Right or HandleSlot.BottomRight;
        var top = slot is HandleSlot.TopLeft or HandleSlot.Top or HandleSlot.TopRight;
        var bottom = slot is HandleSlot.BottomLeft or HandleSlot.Bottom or HandleSlot.BottomRight;
        var corner = (left || right) && (top || bottom);
        var (w, h) = (Math.Max(box.Width, 1e-6), Math.Max(box.Height, 1e-6));
        var newW = w + (left ? -dx : right ? dx : 0);
        var newH = h + (top ? -dy : bottom ? dy : 0);
        if (corner && !free)
        {
            var (sx, sy) = (newW / w, newH / h);
            var factor = Math.Abs(sx - 1) >= Math.Abs(sy - 1) ? sx : sy;
            var high = Math.Min(MaxSizePt / w, MaxSizePt / h);
            var low = Math.Min(Math.Max(MinSizePt / w, MinSizePt / h), high);
            factor = Math.Clamp(factor, low, high);
            (newW, newH) = (w * factor, h * factor);
        }
        else
        {
            newW = left || right ? Math.Clamp(newW, MinSizePt, MaxSizePt) : w;
            newH = top || bottom ? Math.Clamp(newH, MinSizePt, MaxSizePt) : h;
        }

        return new Rect(left ? box.Right - newW : box.X, top ? box.Bottom - newH : box.Y, newW, newH);
    }

    /// <summary>The cursor of a handle: the resize arrow of its direction.</summary>
    public static StandardCursorType CursorOf(HandleSlot slot) => slot switch
    {
        HandleSlot.TopLeft => StandardCursorType.TopLeftCorner,
        HandleSlot.BottomRight => StandardCursorType.BottomRightCorner,
        HandleSlot.TopRight => StandardCursorType.TopRightCorner,
        HandleSlot.BottomLeft => StandardCursorType.BottomLeftCorner,
        HandleSlot.Top or HandleSlot.Bottom => StandardCursorType.SizeNorthSouth,
        _ => StandardCursorType.SizeWestEast,
    };

    /// <summary>
    /// The offsets (EMU) a floating picture gets when it is dropped with its top left corner at (<paramref name="desiredX"/>, <paramref name="desiredY"/>) on the page of its new paragraph. An offset is measured from what its axis
    /// is measured from. Where the picture has an offset now, that origin is what its current box minus the offset gives (the page, the margin or the column: the same for every paragraph of the section); a vertical offset
    /// measured from the paragraph or the line starts at the first line of the new paragraph (<paramref name="paragraphTop"/>, the top of its caret box: the space before the paragraph is not counted, and a wrap of another
    /// picture may move it a little); an axis placed by an alignment keyword has no offset to take the origin from, so it uses 0 for the page and the new paragraph's first line (<paramref name="paragraphLeft"/>,
    /// <paramref name="paragraphTop"/>) for anything else. The picture is moved to the start of the paragraph, so its line is the first one.
    /// </summary>
    public static (long DxEmu, long DyEmu) MoveOffsets(PictureAnchorInfo anchor, Rect currentBox, double desiredX, double desiredY, double paragraphLeft, double paragraphTop)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        var originX = anchor.HorizontalOffsetEmu is { } ox
            ? currentBox.X - (ox / (double)PictureRules.EmuPerPoint)
            : (anchor.HorizontalFrom == "page" ? 0 : paragraphLeft);
        var originY = anchor.VerticalFrom is "paragraph" or "line"
            ? paragraphTop
            : anchor.VerticalOffsetEmu is { } oy
                ? currentBox.Y - (oy / (double)PictureRules.EmuPerPoint)
                : (anchor.VerticalFrom == "page" ? 0 : paragraphTop);
        return (ToEmu(desiredX - originX), ToEmu(desiredY - originY));
    }

    private static long ToEmu(double points) => (long)Math.Round(Math.Clamp(points, -PictureRules.MaxOffsetEmu / (double)PictureRules.EmuPerPoint, PictureRules.MaxOffsetEmu / (double)PictureRules.EmuPerPoint) * PictureRules.EmuPerPoint);
}
