using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>Why a table command refuses. The caller words it for the user in the language of the interface.</summary>
public enum TableRefusal
{
    /// <summary>There is no paragraph or table at the place (or it is not in the document).</summary>
    NoPosition,

    /// <summary>The document is opened as a prefix (the first blocks only): the editor sees a part of the body and the file cannot be saved.</summary>
    PrefixDocument,

    /// <summary>The place is in a header or a footer (a table there is deferred).</summary>
    HeaderOrFooter,

    /// <summary>The place is in a footnote or an endnote.</summary>
    Note,

    /// <summary>The place is in a comment.</summary>
    Comment,

    /// <summary>The place is in a plain-text, combo box, drop-down list or date content control, which holds text only.</summary>
    RestrictedControl,

    /// <summary>The place is in a table cell: a table cannot be inserted into a cell, and only top-level tables are edited by commands.</summary>
    NestedTable,

    /// <summary>The place is in a block content control that already holds a table: the reference of a table names the control, and a control with two tables would leave both without a reference (no command could reach either).</summary>
    ControlHoldsATable,

    /// <summary>The reference names no table of the body (the place holds none, or a content control holds more than one).</summary>
    NoTable,

    /// <summary>The count of columns or rows asked for is zero or negative.</summary>
    InvalidCount,

    /// <summary>The command would make more than <see cref="TableRules.MaxColumns"/> columns.</summary>
    TooManyColumns,

    /// <summary>The command would make more than <see cref="TableRules.MaxRows"/> rows.</summary>
    TooManyRows,

    /// <summary>A row, column or cell asked for is not in the table, or the range is empty or reversed.</summary>
    OutOfRange,

    /// <summary>The table holds something the editor does not restructure (a wrapper element between rows or cells, a row with a tracked deletion, <c>w:gridBefore</c> or <c>w:gridAfter</c>), or whose grid and cells disagree (a different number of <c>w:gridCol</c> than columns, a row shorter than the grid, a vertical-merge continuation with no head of the same column and span): repairing it would be a guess.</summary>
    UnsupportedStructure,

    /// <summary>The range is a single cell, so there is nothing to merge.</summary>
    NothingToMerge,

    /// <summary>The cell is not a merged cell: Split offers a merged cell back as its original cells, and splitting an unmerged cell into several columns is not offered.</summary>
    NotMerged,

    /// <summary>The move would cut through a vertical merge (the row is part of one, or the new place is inside one).</summary>
    WouldCutAMerge,

    /// <summary>The move would cross the border between the repeated header rows and the other rows; toggling the header row first moves that border.</summary>
    HeaderRowBoundary,

    /// <summary>The row cannot move that far (it is at the edge of the table) or the distance is zero.</summary>
    CannotMove,

    /// <summary>The change would leave a row with no cell or a cell with no paragraph, so the table would not be valid.</summary>
    WouldBreakTheTable,

    /// <summary>A value of a formatting command is outside what the file format or the table allows: a border spec that names no side, sets and clears one side or has a width outside 2 to 96 eighths of a point; a row height above the limit; widths that are not one per column, narrower than <see cref="TableRules.MinColumnTwips"/> or wider than the limit.</summary>
    InvalidValue,
}

/// <summary>Where a table may be inserted and what limits a table keeps: the body and the body of a content control that holds several paragraphs, never a cell, a header, a footer, a note or a comment.</summary>
public static class TableRules
{
    /// <summary>The most columns a table gets (the layout's own cap; a cell beyond it would share the last column).</summary>
    public const int MaxColumns = 63;

    /// <summary>The most rows an insert or an added row makes.</summary>
    public const int MaxRows = 500;

    /// <summary>The narrowest a column may be made by a width command, in twips (3 pt).</summary>
    public const int MinColumnTwips = 60;

    /// <summary>The widest a table or a row height may be made by a command, in twips (the layout's own limit of 31,680 pt).</summary>
    public const int MaxTwips = 633_600;

    /// <summary>
    /// The reason a table cannot be inserted at <paramref name="caret"/> for a table of <paramref name="columns"/> by <paramref name="rows"/>, or null when it can. The prefix document is refused first
    /// (<paramref name="prefixDocument"/>), then the counts, then the story: only the body takes a table. A paragraph in a table cell (a nested table would be made) and one in a content control of the
    /// kinds text, combo box, drop-down list or date are refused too (a paragraph of an inline control of those kinds is a single paragraph, so the paragraph itself decides).
    /// </summary>
    public static TableRefusal? Refusal(WordDocument document, TextPosition caret, int columns, int rows, bool prefixDocument = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (prefixDocument)
        {
            return TableRefusal.PrefixDocument;
        }

        if (columns < 1 || rows < 1)
        {
            return TableRefusal.InvalidCount;
        }

        if (columns > MaxColumns)
        {
            return TableRefusal.TooManyColumns;
        }

        if (rows > MaxRows)
        {
            return TableRefusal.TooManyRows;
        }

        if (caret.Paragraph is null)
        {
            return TableRefusal.NoPosition;
        }

        if (StoryRefusal(caret.Story) is { } story)
        {
            return story;
        }

        if (TextEditor.FindIn(document.Body.Blocks, caret.Paragraph) is not { } found)
        {
            return TableRefusal.NoPosition;
        }

        if (!TextEditor.IsBodyOrControlList(document.Body.Blocks, found.Container))
        {
            return TableRefusal.NestedTable;
        }

        return TextEditor.InSingleParagraphControl(document.Body.Blocks, found.Container) ? TableRefusal.RestrictedControl : null;
    }

    /// <summary>The reason a story takes no table (a header, footer, note or comment: the kind says it), null for the body, <see cref="TableRefusal.NoPosition"/> for any other.</summary>
    internal static TableRefusal? StoryRefusal(StoryRef story) => story.Kind switch
    {
        StoryKind.Body => null,
        StoryKind.HeaderFooter => TableRefusal.HeaderOrFooter,
        StoryKind.Note => TableRefusal.Note,
        StoryKind.Comment => TableRefusal.Comment,
        _ => TableRefusal.NoPosition,
    };
}
