using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Raffica.Shell.Frame;

namespace Raffica.Shell.Tests.Frame;

/// <summary>
/// Finds field-code badges (key tips) that sit badly in the ribbon band: a badge must cover no visible text of any control of the band
/// (its own button, the buttons around it, the group labels) and must lie inside the band vertically (a tip that hangs out of the band
/// lands on the document). Text is the rectangle of every visible <see cref="TextBlock"/> with a text.
/// </summary>
internal static class BadgeFit
{
    private const double Tolerance = 0.5;

    /// <summary>The offenders, one line each.</summary>
    public static IReadOnlyList<string> Find(Window window, Control band)
    {
        var bandOrigin = band.TranslatePoint(default, window) ?? default;
        var bandRect = new Rect(bandOrigin, band.Bounds.Size);
        var texts = band.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => !string.IsNullOrEmpty(t.Text) && t.IsEffectivelyVisible && t.Bounds.Width > 0)
            .Select(t => (Text: t.Text!, Rect: new Rect(t.TranslatePoint(default, window) ?? default, t.Bounds.Size)))
            .ToList();

        var offenders = new List<string>();
        foreach (var badge in window.GetVisualDescendants().OfType<FieldCodeBadge>())
        {
            var rect = new Rect(badge.TranslatePoint(default, window) ?? default, badge.Bounds.Size);
            if (rect.Width <= 0)
            {
                continue;
            }

            // Only the badges of the band are judged here (the tab letters belong to the tab row).
            if (AdornerLayer.GetAdornedElement(badge) is not { } adorned || !band.IsVisualAncestorOf(adorned))
            {
                continue;
            }

            foreach (var (text, textRect) in texts)
            {
                if (rect.Inflate(-Tolerance).Intersects(textRect.Inflate(-Tolerance)))
                {
                    offenders.Add($"badge '{badge.Text}' {rect} covers the text '{text}' {textRect}");
                }
            }

            if (rect.Top < bandRect.Top - Tolerance || rect.Bottom > bandRect.Bottom + Tolerance)
            {
                offenders.Add($"badge '{badge.Text}' lies outside the band vertically ({rect.Top:F1} to {rect.Bottom:F1}, band {bandRect.Top:F1} to {bandRect.Bottom:F1})");
            }
        }

        return offenders;
    }
}
