using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Raffica.Pdf.Organizing;

/// <summary>
/// The line between two items of the organize list that shows where dragged pages will land. It lies over the list, takes no input and is
/// visible only during a drag.
/// </summary>
internal sealed class InsertMarker : Control
{
    private const double Thickness = 3;

    public InsertMarker()
    {
        IsHitTestVisible = false;
        IsVisible = false;
        Height = Thickness;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
    }

    /// <summary>Puts the line <paramref name="y"/> device-independent pixels below the top of the list and shows it.</summary>
    public void ShowAt(double y)
    {
        Margin = new Thickness(0, Math.Max(0, y - Thickness / 2), 0, 0);
        IsVisible = true;
    }

    public void Hide() => IsVisible = false;

    public override void Render(DrawingContext context)
    {
        if (this.TryFindResource("Raffica.Brush.Ink", ActualThemeVariant, out var value) && value is IBrush brush)
        {
            context.FillRectangle(brush, new Rect(Bounds.Size));
        }
    }
}

/// <summary>The arithmetic of a drag of pages in the organize list.</summary>
internal static class OrganizeDrag
{
    /// <summary>
    /// Whether dropping the pages at <paramref name="selected"/> into the gap <paramref name="gap"/> leaves them where they are: the pages are
    /// gathered at the gap (<c>OrganizeSession.Move</c>), so the drop changes nothing only when they already are one block that ends up in place.
    /// </summary>
    /// <param name="selected">The selected positions, ascending.</param>
    /// <param name="gap">The gap of the list as it is now (0 is before the first page, the page count after the last).</param>
    public static bool IsNoOp(IReadOnlyList<int> selected, int gap)
    {
        if (selected.Count == 0)
        {
            return true;
        }

        var before = 0;
        foreach (var position in selected)
        {
            if (position < gap)
            {
                before++;
            }
        }

        var start = gap - before;
        for (var i = 0; i < selected.Count; i++)
        {
            if (selected[i] != start + i)
            {
                return false;
            }
        }

        return true;
    }
}
