using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Shell.Frame;

namespace Raffica.Shell.Tests.Frame;

/// <summary>
/// Finds text that does not fit where it is shown. For every visible <see cref="TextBlock"/> with a text the text is measured again at
/// the font and size of the block without trimming and without a line limit (a probe block, so the result is the layout the text engine
/// would produce); it must fit the block's <see cref="Visual.Bounds"/> in width, a wrapping block must show all its lines and break no
/// word, and the rectangle of the block must lie inside the window and inside every ancestor that clips to its bounds. A window with an
/// <see cref="AppFrame"/> is checked in the title bar (document tabs), the tab row, the band and the status bar; any other window
/// is checked as a whole.
/// </summary>
internal static class LabelFit
{
    private const double Tolerance = 0.5;

    /// <summary>The offenders as <c>"&lt;Text&gt; (&lt;where&gt;)"</c>, in visual order.</summary>
    public static IReadOnlyList<string> FindClipped(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var roots = new List<Visual>();
        if (window.GetVisualDescendants().OfType<AppFrame>().FirstOrDefault() is { } frame)
        {
            roots.AddRange(new Visual?[] { frame.TitleBar, frame.TabRow, frame.Band, frame.StatusBar }.OfType<Visual>());
        }
        else
        {
            roots.Add(window);
        }

        var offenders = new List<string>();
        foreach (var root in roots)
        {
            var blocks = root.GetVisualDescendants().OfType<TextBlock>().ToList();
            if (root is TextBlock self)
            {
                blocks.Insert(0, self);
            }

            foreach (var block in blocks)
            {
                if (!string.IsNullOrEmpty(block.Text) && block.IsEffectivelyVisible && block.Bounds.Width > 0 && !Fits(block, window))
                {
                    offenders.Add($"{block.Text} ({Where(block, root)})");
                }
            }
        }

        return offenders;
    }

    private static bool Fits(TextBlock block, Window window)
    {
        var text = block.Text!;
        var width = block.Bounds.Width;
        var wraps = block.TextWrapping != TextWrapping.NoWrap;

        var probe = Probe(block, text, wraps);
        probe.Measure(new Size(wraps ? width : double.PositiveInfinity, double.PositiveInfinity));
        if (probe.DesiredSize.Width > width + Tolerance || (wraps && probe.DesiredSize.Height > block.Bounds.Height + Tolerance))
        {
            return false;
        }

        if (wraps)
        {
            // A word wider than the block is broken in the middle; the engine would still report a fitting width.
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var single = Probe(block, word, wraps: false);
                single.Measure(Size.Infinity);
                if (single.DesiredSize.Width > width + Tolerance)
                {
                    return false;
                }
            }
        }

        return Contained(block, window);
    }

    private static TextBlock Probe(TextBlock block, string text, bool wraps) => new()
    {
        Text = text,
        FontFamily = block.FontFamily,
        FontSize = block.FontSize,
        FontWeight = block.FontWeight,
        FontStyle = block.FontStyle,
        FontStretch = block.FontStretch,
        FontFeatures = block.FontFeatures,
        LineHeight = block.LineHeight,
        LetterSpacing = block.LetterSpacing,
        TextWrapping = wraps ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = TextTrimming.None,
        MaxLines = 0,
    };

    private static bool Contained(TextBlock block, Window window)
    {
        var size = block.Bounds.Size;
        var rect = new Rect(block.TranslatePoint(default, window) ?? default, size);
        if (!new Rect(window.Bounds.Size).Inflate(Tolerance).Contains(rect))
        {
            return false;
        }

        for (var parent = block.GetVisualParent(); parent is not null && parent != window; parent = parent.GetVisualParent())
        {
            if (parent is Control { ClipToBounds: true } clipping && clipping.TranslatePoint(default, window) is { } origin
                && !new Rect(origin, clipping.Bounds.Size).Inflate(Tolerance).Contains(rect))
            {
                return false;
            }
        }

        return true;
    }

    private static string Where(Control block, Visual root)
    {
        var owner = block.FindAncestorOfType<Button>() as Control;
        var part = root switch
        {
            StatusBar => "status bar",
            Control { Parent: not null } c when c.Classes.Contains("ribbon-band") => "ribbon band",
            Control c when c.Classes.Contains("ribbon-tabrow") => "ribbon tab row",
            _ => root.GetType().Name,
        };
        return owner is null ? part : $"{part}, {owner.GetType().Name}";
    }
}
