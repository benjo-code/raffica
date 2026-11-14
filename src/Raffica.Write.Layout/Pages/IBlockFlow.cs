namespace Raffica.Write.Layout;

/// <summary>The two pieces of a block that did not fit: the part that stays (null: nothing fits) and the part that moves on (null: everything fits).</summary>
internal readonly record struct FlowSplit(IBlockFlow? Head, IBlockFlow? Tail);

/// <summary>
/// A body block laid out at one width, as the page filler sees it: something with a height that can be cut into a head and a tail.
/// Paragraphs, tables (Task 10) and anything that arrives later all go through this one seam; the filler knows nothing else about
/// them. Flows are immutable: <see cref="Split"/> makes new flows and never changes this one, so the filler can try a split, throw
/// it away and try again (balancing columns does).
/// </summary>
internal interface IBlockFlow
{
    /// <summary>The index of the top-level body block this flow belongs to (for <see cref="DocumentLayout.Locate"/> and <see cref="DocumentLayout.BlockAt"/>).</summary>
    int BodyIndex { get; }

    /// <summary>True for the piece that holds the start of the block; false for a tail and for what follows a page or column break inside a paragraph.</summary>
    bool IsFirstPiece { get; }

    /// <summary>The height of everything this piece still has to place, without the space before and after.</summary>
    float Height { get; }

    /// <summary>The gap above this piece. A tail has none.</summary>
    float SpaceBefore { get; }

    /// <summary>The gap below this piece. A head that is followed by a tail has none.</summary>
    float SpaceAfter { get; }

    /// <summary>Keep this block on one page with the next one (<c>w:keepNext</c>).</summary>
    bool KeepNext { get; }

    /// <summary>Start a new page before this block (<c>w:pageBreakBefore</c>); only the first piece asks.</summary>
    bool PageBreakBefore { get; }

    /// <summary>When not null, the space between this block and a neighbour with the same value is dropped (<c>w:contextualSpacing</c>: the style id).</summary>
    string? ContextualGroup { get; }

    /// <summary>The height of the smallest head this block allows (the lines that must stay together at a page end); the filler uses it to keep a heading with the next block.</summary>
    float MinimumHead { get; }

    /// <summary>The footnote references that this piece draws (their notes go to the bottom of the page the piece lands on). Empty for almost every piece.</summary>
    IReadOnlyList<NoteRef> Notes { get; }

    /// <summary>A page or column break that ends this piece (a manual break inside a paragraph); null otherwise.</summary>
    BreakKind? EndBreak { get; }

    /// <summary>The block's content after <see cref="EndBreak"/>, laid out at <paramref name="width"/>; null when <see cref="EndBreak"/> is null.</summary>
    IBlockFlow? AfterBreak(float width);

    /// <summary>
    /// Cuts the flow so that the head is at most <paramref name="available"/> points high. Everything fits: <c>(this, null)</c>.
    /// Nothing may stay: <c>(null, this)</c>, but only when <paramref name="mustProgress"/> is false. When it is true the flow is at
    /// the top of an empty column and must give a head with at least some content even if that is taller than the room, so that
    /// content bigger than a page is clipped, never looped over.
    /// </summary>
    FlowSplit Split(float available, bool mustProgress);

    /// <summary>Adds the items of this piece, whose top left corner is (<paramref name="x"/>, <paramref name="y"/>) on the page, and its text.</summary>
    void Emit(PageBuilder page, float x, float y);
}

/// <summary>
/// A flow whose layout depends on where it is placed because of floating objects: its own, or the ones of earlier blocks on the page.
/// The band asks before it places a flow; a flow that wants it is laid out again at its place and gives the objects it owns, which
/// the band then registers for the blocks that follow. Flows that do not implement it are placed as they are.
/// </summary>
internal interface IFloatAware : IBlockFlow
{
    /// <summary>True when this flow, placed with its top at <paramref name="y"/>, has to be laid out again at its place.</summary>
    bool NeedsPlacement(PageFloats page, float y);

    /// <summary>This flow laid out for its place; its <see cref="Owned"/> objects are positioned. The result does not need placement again.</summary>
    IBlockFlow AtPlace(FloatContext context);

    /// <summary>The floating objects this piece draws (only the piece that holds the start of the paragraph has any).</summary>
    IReadOnlyList<FloatBox> Owned { get; }
}
