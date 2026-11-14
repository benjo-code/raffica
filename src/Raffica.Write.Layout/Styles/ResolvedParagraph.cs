namespace Raffica.Write.Layout;

public enum ParagraphAlignment
{
    Left,
    Center,
    Right,
    Justify,
}

public enum LineSpacingRule
{
    /// <summary>The value is a multiple of the font's natural line height (1.0 is single spacing).</summary>
    Auto,

    /// <summary>The value is a minimum line height in points.</summary>
    AtLeast,

    /// <summary>The value is the line height in points.</summary>
    Exact,
}

public enum TabAlignment
{
    Left,
    Center,
    Right,
    DecimalPoint,
    Bar,
}

public enum TabLeader
{
    None,
    Dot,
    Hyphen,
    Underscore,
    Heavy,
    MiddleDot,
}

/// <summary>A tab stop; <see cref="Position"/> is in points from the paragraph's text edge (the page margin, not the indent).</summary>
public readonly record struct TabStop(float Position, TabAlignment Alignment, TabLeader Leader);

/// <summary>
/// The formatting of one paragraph with document defaults, the style chain, numbering indents and direct formatting applied. All
/// lengths are in points. One instance is shared by every paragraph that has the same style and no direct formatting.
/// </summary>
public sealed class ResolvedParagraph
{
    private Dictionary<string, ResolvedRun>? _runs;
    private RunProps? _mark;

    internal ResolvedParagraph(ParaProps props, RunProps baseRun, string? styleId, System.Xml.Linq.XElement? markRunProperties)
    {
        BaseRun = baseRun;
        StyleId = styleId;
        MarkRunProperties = markRunProperties;
        Alignment = props.Alignment ?? ParagraphAlignment.Left;
        LeftIndent = props.Left ?? 0f;
        RightIndent = props.Right ?? 0f;
        FirstLineIndent = props.FirstLine ?? 0f;
        HangingIndent = props.Hanging ?? 0f;
        SpaceBefore = props.Before ?? 0f;
        SpaceAfter = props.After ?? 0f;
        LineRule = props.LineRule ?? LineSpacingRule.Auto;
        LineValue = props.LineValue ?? 1f;
        ContextualSpacing = props.ContextualSpacing ?? false;
        KeepNext = props.KeepNext ?? false;
        KeepLines = props.KeepLines ?? false;
        PageBreakBefore = props.PageBreakBefore ?? false;
        WidowControl = props.WidowControl ?? true;
        SuppressAutoHyphens = props.SuppressAutoHyphens ?? false;
        TabStops = props.TabStops.Count == 0 ? [] : props.TabStops.OrderBy(t => t.Position).ToArray();
        OutlineLevel = props.OutlineLevel is >= 0 and <= 8 ? props.OutlineLevel : null;
        NumberingId = props.NumId is > 0 ? props.NumId.Value : 0;
        NumberingLevel = props.Ilvl is >= 0 ? props.Ilvl.Value : 0;
    }

    public string? StyleId { get; }

    public ParagraphAlignment Alignment { get; }

    /// <summary>Left indent in points (the start edge; right-to-left text is laid out left to right, so start is left).</summary>
    public float LeftIndent { get; }

    public float RightIndent { get; }

    /// <summary>Extra indent of the first line; 0 when the paragraph has a hanging indent.</summary>
    public float FirstLineIndent { get; }

    /// <summary>How far the first line sticks out to the left of <see cref="LeftIndent"/>; 0 when the paragraph has a first-line indent.</summary>
    public float HangingIndent { get; }

    public float SpaceBefore { get; }

    public float SpaceAfter { get; }

    public LineSpacingRule LineRule { get; }

    /// <summary>A multiple for <see cref="LineSpacingRule.Auto"/>, points for the other rules.</summary>
    public float LineValue { get; }

    public bool ContextualSpacing { get; }

    public bool KeepNext { get; }

    public bool KeepLines { get; }

    public bool PageBreakBefore { get; }

    public bool WidowControl { get; }

    public IReadOnlyList<TabStop> TabStops { get; }

    /// <summary>0 to 8 for a heading level, null for body text.</summary>
    public int? OutlineLevel { get; }

    /// <summary>The list this paragraph belongs to; 0 when it is not numbered.</summary>
    public int NumberingId { get; }

    public int NumberingLevel { get; }

    public bool SuppressAutoHyphens { get; }

    internal RunProps BaseRun { get; }

    internal System.Xml.Linq.XElement? MarkRunProperties { get; }

    /// <summary>The run formatting of the paragraph mark, without its character style: the paragraph's run base plus the direct mark properties.</summary>
    internal RunProps MarkRun
    {
        get
        {
            if (_mark is null)
            {
                var mark = BaseRun.Clone();
                if (MarkRunProperties is not null)
                {
                    mark.Overlay(MarkRunProperties.Elements());
                }

                _mark = mark;
            }

            return _mark;
        }
    }

    // The layout of one document runs on one thread at a time, so the per-paragraph run cache needs no lock.
    internal ResolvedRun CachedRun(string key, Func<ResolvedRun> make)
    {
        _runs ??= [];
        if (!_runs.TryGetValue(key, out var run))
        {
            run = make();
            _runs[key] = run;
        }

        return run;
    }
}
