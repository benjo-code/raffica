using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace Raffica.Shell.Frame;

/// <summary>
/// The small ink label that shows a field code while the Alt mode is on (18 px high, at least 18 px wide, 3 px radius, ink fill with
/// on-ink text). A badge lives in the adorner layer of the window and sits at the bottom right of the control it names (4 px
/// beyond its right edge, 6 px below its bottom edge), so it never takes part in the layout of the frame; at rest there are none.
/// Controls that name a code without the ribbon (the search field, the panes) carry it as <see cref="CodeProperty"/>.
/// </summary>
public class FieldCodeBadge : TemplatedControl
{
    // How far a badge beside a small button reaches over the button's right edge (its padding is 6 px, so the text is not touched).
    private const double BesideOverlap = 4;

    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<FieldCodeBadge, string?>(nameof(Text));

    /// <summary>The field code a control shows while the Alt mode is on (for example "Q" on the search field); null for none.</summary>
    public static readonly AttachedProperty<string?> CodeProperty = AvaloniaProperty.RegisterAttached<FieldCodeBadge, Control, string?>("Code");

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public static string? GetCode(Control control) => control.GetValue(CodeProperty);

    public static void SetCode(Control control, string? code) => control.SetValue(CodeProperty, code);

    /// <summary>
    /// Puts a badge with <paramref name="text"/> on <paramref name="target"/>; null when the target is not in a window yet. The
    /// <paramref name="placement"/> says where; the default is the bottom right corner of the spec (4 px beyond the right edge, 6 px below the
    /// bottom edge). The ribbon uses the other places so that a tip never covers text.
    /// </summary>
    internal static FieldCodeBadge? Show(Control target, string text, BadgePlacement placement = BadgePlacement.BottomRight)
    {
        if (AdornerLayer.GetAdornerLayer(target) is not { } layer)
        {
            return null;
        }

        var (vertical, margin) = placement switch
        {
            // Inside the right edge, vertically centred (the items of a menu, whose popup would clip a badge that hangs over the edge; a group label).
            BadgePlacement.InsideRight => (VerticalAlignment.Center, new Thickness(0, 0, 6, 0)),

            // Flush with the right edge, vertically centred (the label row of a ribbon group, which keeps 20 px free on the right).
            BadgePlacement.GroupLabel => (VerticalAlignment.Center, new Thickness(0, 0, 1, 0)),

            // The top right corner inside a large tile: over the icon, never over the label below it.
            BadgePlacement.TileCorner => (VerticalAlignment.Top, new Thickness(0, 2, 2, 0)),

            // Beside a small button: vertically centred on it, 4 px over its right edge and the rest beyond it (over the padding and the icon
            // of the next button, not its text); the margin follows the width of the badge, which a long code widens.
            BadgePlacement.BesideRight => (VerticalAlignment.Center, new Thickness(0, 0, -14, 0)),
            _ => (VerticalAlignment.Bottom, new Thickness(0, 0, -4, -6)),
        };
        var badge = new FieldCodeBadge
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = vertical,
            Margin = margin,
        };
        if (placement == BadgePlacement.BesideRight)
        {
            badge.SizeChanged += (_, e) => badge.Margin = new Thickness(0, 0, BesideOverlap - e.NewSize.Width, 0);
        }

        AdornerLayer.SetAdornedElement(badge, target);
        AdornerLayer.SetIsClipEnabled(badge, false);
        layer.Children.Add(badge);
        return badge;
    }

    /// <summary>Takes every badge in <paramref name="badges"/> off its layer and empties the list.</summary>
    internal static void RemoveAll(List<FieldCodeBadge> badges)
    {
        foreach (var badge in badges)
        {
            (badge.Parent as Panel)?.Children.Remove(badge);
        }

        badges.Clear();
    }
}

/// <summary>Where <see cref="FieldCodeBadge"/> puts a badge on its control.</summary>
internal enum BadgePlacement
{
    /// <summary>4 px beyond the right edge, 6 px below the bottom edge (the spec's place, for tabs, the search field and the panes).</summary>
    BottomRight,

    /// <summary>Inside the right edge, vertically centred.</summary>
    InsideRight,

    /// <summary>Flush with the right edge of the label row of a group, vertically centred.</summary>
    GroupLabel,

    /// <summary>The top right corner inside a large tile, over its icon.</summary>
    TileCorner,

    /// <summary>Beside a small button, vertically centred, 14 px beyond its right edge.</summary>
    BesideRight,
}
