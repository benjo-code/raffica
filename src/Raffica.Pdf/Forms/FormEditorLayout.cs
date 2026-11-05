using Avalonia;
using Raffica.Core.Forms;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Forms;

/// <summary>
/// Where a field editor sits and how big its text is. The widget rectangle is Core's (page points, origin top left, rotation and CropBox
/// already applied), so the one mapping to the view is <see cref="PageView.ToView(int, Raffica.Core.Text.PageRect)"/> and a rotated or
/// cropped page needs no case of its own. The editor is upright whatever the page rotation is. Pure functions; UI thread only.
/// </summary>
internal static class FormEditorLayout
{
    /// <summary>The smallest font size, in points, of a single-line field.</summary>
    public const double MinFontPoints = 6;

    /// <summary>The largest font size, in points, of a single-line field.</summary>
    public const double MaxFontPoints = 14;

    /// <summary>The font size, in points, of a multi-line field.</summary>
    public const double MultilineFontPoints = 11;

    /// <summary>The place of the widget in the view, or <see langword="null"/> when its page is not shown or its size is not known.</summary>
    public static Rect? RectOf(PageView view, FormField field)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(field);
        return view.ToView(field.Key.Page, field.Bounds) is { } rect && rect.Width > 0 && rect.Height > 0 ? rect : null;
    }

    /// <summary>Whether any part of the rectangle lies in the view (a field scrolled out of sight has no editor on screen).</summary>
    public static bool IsInView(PageView view, Rect rect)
    {
        ArgumentNullException.ThrowIfNull(view);
        return rect.Intersects(new Rect(view.Bounds.Size));
    }

    /// <summary>The factor from page points to DIPs at the current zoom.</summary>
    public static double ScaleOf(Rect rect, FormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Bounds.Width > 0 ? rect.Width / field.Bounds.Width : 1;
    }

    /// <summary>The font size in DIPs: about 60 percent of the height of a single-line widget, kept between 6 and 14 points, times the zoom.</summary>
    public static double FontSize(FormField field, double scale)
    {
        ArgumentNullException.ThrowIfNull(field);
        var points = field.Traits.HasFlag(FormFieldTraits.Multiline)
            ? MultilineFontPoints
            : Math.Clamp(field.Bounds.Height * 0.6, MinFontPoints, MaxFontPoints);
        return Math.Max(1, points * scale);
    }
}
