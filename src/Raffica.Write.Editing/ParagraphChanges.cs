using System.Globalization;
using System.Xml.Linq;

namespace Raffica.Write.Editing;

/// <summary>The paragraph alignments of <see cref="ParagraphChanges.Alignment"/>.</summary>
public enum ParagraphJustification
{
    Left,
    Center,
    Right,
    Justify,
}

/// <summary>How the line value of <see cref="ParagraphChanges.Spacing"/> is read (the <c>w:lineRule</c> attribute).</summary>
public enum LineRuleKind
{
    /// <summary>A multiple of the single line height: 1.0 is single.</summary>
    Auto,

    /// <summary>The line is exactly this many points high.</summary>
    Exact,

    /// <summary>The line is at least this many points high.</summary>
    AtLeast,
}

/// <summary>The pagination switches and the contextual spacing switch of a paragraph.</summary>
public enum ParagraphSwitch
{
    KeepNext,
    KeepLines,
    PageBreakBefore,
    WidowControl,
    ContextualSpacing,
}

/// <summary>
/// The property changes of the paragraph commands for <see cref="TextEditor.ApplyParagraphChanges"/>. Lengths are points and are written as
/// twips (1 pt = 20 twips). Choosing a value that the style already gives still writes it: the override is what the user asked for.
/// </summary>
public static class ParagraphChanges
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>The alignment (<c>w:jc</c>).</summary>
    public static IReadOnlyList<PropertyChange> Alignment(ParagraphJustification alignment)
    {
        var value = alignment switch
        {
            ParagraphJustification.Left => "left",
            ParagraphJustification.Center => "center",
            ParagraphJustification.Right => "right",
            ParagraphJustification.Justify => "both",
            _ => throw new ArgumentOutOfRangeException(nameof(alignment)),
        };
        return [new PropertyChange(W + "jc", new XElement(W + "jc", new XAttribute(W + "val", value)))];
    }

    /// <summary>
    /// The indents (<c>w:ind</c>): a null keeps that attribute as it is. A first-line indent removes a hanging indent and the other way round;
    /// both at once is refused.
    /// </summary>
    /// <exception cref="ArgumentException">Both <paramref name="firstLinePt"/> and <paramref name="hangingPt"/> are given.</exception>
    public static IReadOnlyList<PropertyChange> Indent(float? leftPt, float? rightPt, float? firstLinePt, float? hangingPt)
    {
        if (firstLinePt is not null && hangingPt is not null)
        {
            throw new ArgumentException("A first-line indent and a hanging indent exclude each other.", nameof(hangingPt));
        }

        var element = new XElement(W + "ind");
        Add(element, "left", leftPt);
        Add(element, "right", rightPt);
        Add(element, "firstLine", firstLinePt);
        Add(element, "hanging", hangingPt);
        return element.HasAttributes ? [new PropertyChange(W + "ind", element)] : [];
    }

    /// <summary>
    /// The spacing (<c>w:spacing</c>): a null keeps that attribute. A line value needs its rule, because the number means different things per
    /// rule: <paramref name="linePt"/> is a multiple of the single line for <see cref="LineRuleKind.Auto"/> (written in 240ths) and points for
    /// the other two (written as twips). A rule without a line value is a rule-only change: the number stays as it is and only
    /// <c>w:lineRule</c> changes, so it is the caller's job to pass it only when the number is meant in the new unit.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="linePt"/> is given without <paramref name="rule"/>.</exception>
    public static IReadOnlyList<PropertyChange> Spacing(float? beforePt, float? afterPt, float? linePt, LineRuleKind? rule)
    {
        if (linePt is not null && rule is null)
        {
            throw new ArgumentException("A line value needs its line rule: its unit depends on the rule.", nameof(rule));
        }

        var element = new XElement(W + "spacing");
        Add(element, "before", beforePt, signed: false);
        Add(element, "after", afterPt, signed: false);
        if (linePt is { } line)
        {
            if (rule == LineRuleKind.Auto)
            {
                if (!float.IsFinite(line) || line < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(linePt));
                }

                element.SetAttributeValue(W + "line", Format(line * 240f));
            }
            else
            {
                Check(line, signed: false);
                element.SetAttributeValue(W + "line", Format(line * 20f));
            }
        }

        if (rule is { } kind)
        {
            element.SetAttributeValue(W + "lineRule", kind switch
            {
                LineRuleKind.Auto => "auto",
                LineRuleKind.Exact => "exact",
                LineRuleKind.AtLeast => "atLeast",
                _ => throw new ArgumentOutOfRangeException(nameof(rule)),
            });
        }

        return element.HasAttributes ? [new PropertyChange(W + "spacing", element)] : [];
    }

    /// <summary>A pagination switch: on writes the element, off writes an explicit <c>w:val="0"</c> (a style may turn it on).</summary>
    public static IReadOnlyList<PropertyChange> Switch(ParagraphSwitch which, bool on)
    {
        var name = W + which switch
        {
            ParagraphSwitch.KeepNext => "keepNext",
            ParagraphSwitch.KeepLines => "keepLines",
            ParagraphSwitch.PageBreakBefore => "pageBreakBefore",
            ParagraphSwitch.WidowControl => "widowControl",
            ParagraphSwitch.ContextualSpacing => "contextualSpacing",
            _ => throw new ArgumentOutOfRangeException(nameof(which)),
        };
        return [new PropertyChange(name, on ? new XElement(name) : new XElement(name, new XAttribute(W + "val", "0")))];
    }

    private static void Add(XElement element, string attribute, float? points, bool signed = true)
    {
        if (points is { } value)
        {
            Check(value, signed);
            element.SetAttributeValue(W + attribute, Format(value * 20f));
        }
    }

    // Point values: indents are signed (a negative one is a hanging margin), the spacing values are not.
    private static void Check(float value, bool signed)
    {
        if (!float.IsFinite(value) || Math.Abs(value) > 31680f || (!signed && value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private static string Format(float twips) => ((int)MathF.Round(twips)).ToString(CultureInfo.InvariantCulture);
}
