using System.Globalization;
using Raffica.Write.Commands;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>A value of a selection: the value the whole selection has, or <see cref="Mixed"/> when the covered text or paragraphs differ.</summary>
internal readonly record struct Mixable<T>(T Value, bool Mixed = false)
{
    /// <summary>Whether the selection has this value everywhere (a mixed value is never on).</summary>
    public bool Is(T other) => !Mixed && EqualityComparer<T>.Default.Equals(Value, other);
}

/// <summary>
/// What the ribbon shows about the selection: the effective formatting (styles resolved, as the page shows it) of the covered runs and paragraphs, one value each, or mixed. It is read at
/// most once per selection and document revision (<see cref="EditSession.ReadSelectionFormat"/>) and never walks more than <see cref="TextEditor.MaxFactBlocks"/> blocks: a larger selection
/// shows the formatting at its start (<see cref="Truncated"/>). At a caret it is the formatting typed text would take, with the session's pending format on top (<see cref="WithPending"/>).
/// </summary>
internal sealed record SelectionFormat
{
    public required Mixable<bool> Bold { get; init; }

    public required Mixable<bool> Italic { get; init; }

    public required Mixable<bool> Underline { get; init; }

    public required Mixable<bool> Strike { get; init; }

    public required Mixable<bool> Superscript { get; init; }

    public required Mixable<bool> Subscript { get; init; }

    public required Mixable<bool> SmallCaps { get; init; }

    public required Mixable<bool> AllCaps { get; init; }

    public required Mixable<string> Font { get; init; }

    public required Mixable<float> Size { get; init; }

    /// <summary>The text colour 0xAARRGGBB, or null for automatic.</summary>
    public required Mixable<uint?> Color { get; init; }

    /// <summary>The highlight colour 0xAARRGGBB, or null for none.</summary>
    public required Mixable<uint?> Highlight { get; init; }

    public required Mixable<ParagraphAlignment> Alignment { get; init; }

    public required Mixable<float> SpaceBefore { get; init; }

    public required Mixable<float> SpaceAfter { get; init; }

    public required Mixable<(LineSpacingRule Rule, float Value)> LineSpacing { get; init; }

    public required Mixable<bool> KeepNext { get; init; }

    public required Mixable<bool> KeepLines { get; init; }

    public required Mixable<bool> PageBreakBefore { get; init; }

    public required Mixable<bool> WidowControl { get; init; }

    /// <summary>The paragraph style id (null: the paragraph names none and the file has no default).</summary>
    public required Mixable<string?> ParagraphStyle { get; init; }

    /// <summary>The character style id; null is none.</summary>
    public required Mixable<string?> CharacterStyle { get; init; }

    /// <summary>True when the selection was too large to read whole and this is the formatting at its start.</summary>
    public bool Truncated { get; init; }

    /// <summary>Reads the covered runs and paragraphs (no model change).</summary>
    public static SelectionFormat Read(CoveredFormat covered, LayoutEffectiveFormat format, string? defaultParagraphStyle)
    {
        ArgumentNullException.ThrowIfNull(covered);
        ArgumentNullException.ThrowIfNull(format);
        var runs = covered.Targets.Select(t => (Resolved: format.ResolveRun(t.Story, t.Paragraph, t.Run), t.Run)).ToList();
        var paragraphs = covered.Paragraphs.Select(p => (Resolved: format.ResolveParagraph(p.Story, p.Paragraph), p.Paragraph)).ToList();
        return new SelectionFormat
        {
            Bold = Fold(runs, r => r.Resolved.Bold),
            Italic = Fold(runs, r => r.Resolved.Italic),
            Underline = Fold(runs, r => r.Resolved.Underline != UnderlineKind.None),
            Strike = Fold(runs, r => r.Resolved.Strike),
            Superscript = Fold(runs, r => r.Resolved.VerticalAlignment == RunVerticalAlignment.Superscript),
            Subscript = Fold(runs, r => r.Resolved.VerticalAlignment == RunVerticalAlignment.Subscript),
            SmallCaps = Fold(runs, r => r.Resolved.SmallCaps),
            AllCaps = Fold(runs, r => r.Resolved.Caps),
            Font = Fold(runs, r => r.Resolved.FontName),
            Size = Fold(runs, r => r.Resolved.SizePoints),
            Color = Fold(runs, r => r.Resolved.Color),
            Highlight = Fold(runs, r => r.Resolved.Highlight),
            CharacterStyle = Fold(runs, r => r.Run?.Properties.StyleId),
            Alignment = Fold(paragraphs, p => p.Resolved.Alignment),
            SpaceBefore = Fold(paragraphs, p => p.Resolved.SpaceBefore),
            SpaceAfter = Fold(paragraphs, p => p.Resolved.SpaceAfter),
            LineSpacing = Fold(paragraphs, p => (p.Resolved.LineRule, p.Resolved.LineValue)),
            KeepNext = Fold(paragraphs, p => p.Resolved.KeepNext),
            KeepLines = Fold(paragraphs, p => p.Resolved.KeepLines),
            PageBreakBefore = Fold(paragraphs, p => p.Resolved.PageBreakBefore),
            WidowControl = Fold(paragraphs, p => p.Resolved.WidowControl),
            ParagraphStyle = Fold(paragraphs, p => p.Paragraph.Properties.StyleId ?? defaultParagraphStyle),
            Truncated = covered.Truncated,
        };
    }

    private static Mixable<T> Fold<TItem, T>(List<TItem> items, Func<TItem, T> read)
    {
        if (items.Count == 0)
        {
            return default;
        }

        var first = read(items[0]);
        for (var i = 1; i < items.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(first, read(items[i])))
            {
                return new Mixable<T>(first, true);
            }
        }

        return new Mixable<T>(first);
    }

    /// <summary>
    /// The format typed text at a caret takes: this one with the pending changes of the session on top. A value the pending format does not name stays as it is; a pending switch is never mixed.
    /// </summary>
    public SelectionFormat WithPending(IReadOnlyList<PropertyChange> pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        if (pending.Count == 0)
        {
            return this;
        }

        var result = this with
        {
            Bold = Toggle(Bold, pending, CharacterToggle.Bold),
            Italic = Toggle(Italic, pending, CharacterToggle.Italic),
            Underline = Toggle(Underline, pending, CharacterToggle.Underline),
            Strike = Toggle(Strike, pending, CharacterToggle.Strike),
            Superscript = Toggle(Superscript, pending, CharacterToggle.Superscript),
            Subscript = Toggle(Subscript, pending, CharacterToggle.Subscript),
            SmallCaps = Toggle(SmallCaps, pending, CharacterToggle.SmallCaps),
            AllCaps = Toggle(AllCaps, pending, CharacterToggle.AllCaps),
        };
        foreach (var change in pending)
        {
            var element = change.Value;
            var ns = change.Name.Namespace;
            switch (change.Name.LocalName)
            {
                case "rFonts" when element is not null && (element.Attribute(ns + "ascii") ?? element.Attribute(ns + "hAnsi")) is { } family:
                    result = result with { Font = new Mixable<string>(family.Value) };
                    break;
                case "sz" when element is not null && int.TryParse(element.Attribute(ns + "val")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var half):
                    result = result with { Size = new Mixable<float>(half / 2f) };
                    break;
                case "color" when element is not null && uint.TryParse(element.Attribute(ns + "val")?.Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb):
                    result = result with { Color = new Mixable<uint?>(0xFF000000u | rgb) };
                    break;
                case "highlight":
                    result = result with { Highlight = new Mixable<uint?>(element?.Attribute(ns + "val")?.Value is { } name ? ColorChoices.HighlightValue(name) : null) };
                    break;
                default:
                    break;
            }
        }

        return result;
    }

    private static Mixable<bool> Toggle(Mixable<bool> current, IReadOnlyList<PropertyChange> pending, CharacterToggle toggle) =>
        FormatChanges.IsOn(pending, toggle) is { } on ? new Mixable<bool>(on) : current;
}
