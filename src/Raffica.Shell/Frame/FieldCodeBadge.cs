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
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<FieldCodeBadge, string?>(nameof(Text));

    /// <summary>The field code a control shows while the Alt mode is on (for example "Q" on the search field); null for none.</summary>
    public static readonly AttachedProperty<string?> CodeProperty = AvaloniaProperty.RegisterAttached<FieldCodeBadge, Control, string?>("Code");

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public static string? GetCode(Control control) => control.GetValue(CodeProperty);

    public static void SetCode(Control control, string? code) => control.SetValue(CodeProperty, code);

    /// <summary>Puts a badge with <paramref name="text"/> on <paramref name="target"/>; null when the target is not in a window yet.</summary>
    internal static FieldCodeBadge? Show(Control target, string text)
    {
        if (AdornerLayer.GetAdornerLayer(target) is not { } layer)
        {
            return null;
        }

        var badge = new FieldCodeBadge
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -4, -6),
        };
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
