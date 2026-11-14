namespace Raffica.Write.Viewing;

/// <summary>
/// A place in the document that survives a change of layout (the other view, a replaced layout): the top-level body block and a character offset in it. The
/// layout reports blocks only, so for now the offset is always 0 (the start of the block); nothing may map an offset to a position through the glyph runs
/// (an automatic hyphen is a run with no text range).
/// </summary>
/// <param name="Block">The index of the top-level body block.</param>
/// <param name="Char">The character offset inside the block (0 until the layout can say more).</param>
internal readonly record struct ViewAnchor(int Block, int Char)
{
    /// <summary>The start of the document.</summary>
    public static ViewAnchor Top => default;
}

/// <summary>Where the blocks of a layout are: the question the anchors of <see cref="PageScroller"/> ask (a layout answers it, a test fakes it).</summary>
internal interface IBlockMap
{
    /// <summary>The page and the y (points) where a block starts, or null when that page is not laid out yet.</summary>
    (int Page, float Y)? Locate(int block);

    /// <summary>The block at y (points) of a laid-out page, or -1 for a page with no blocks.</summary>
    int BlockAt(int page, float y);
}
