namespace Layout.Common;

// Units are points (1/72 inch) everywhere.

public readonly record struct RectF(float X, float Y, float Width, float Height);

public enum Alignment { Left, Center }

/// <summary>The resolved paragraph style. Family and Size are the paragraph's default font (used by an empty paragraph).</summary>
public sealed record ParagraphFormat(
    float SpaceBefore, float SpaceAfter, float LineSpacing, float LeftIndent, Alignment Alignment,
    bool KeepNext, bool PageBreakBefore, bool PageBreakAfter, string Family, float Size);

public sealed record TextRun(int Start, int Length, string Family, float Size, bool Bold, bool Italic);

/// <summary>A footnote reference mark at <paramref name="Offset"/> of the paragraph text (the mark itself has no glyph in the spike).</summary>
public sealed record FootnoteRef(int Offset, int FootnoteId);

public abstract record Block;

public sealed record Paragraph(string Text, IReadOnlyList<TextRun> Runs, ParagraphFormat Format, IReadOnlyList<FootnoteRef> FootnoteRefs) : Block;

/// <summary>A table; a row is the unit that breaks across pages. The first <paramref name="HeaderRows"/> rows repeat on every page.</summary>
public sealed record Table(IReadOnlyList<IReadOnlyList<Paragraph>> Rows, IReadOnlyList<float> ColumnWidths, int HeaderRows) : Block;

public sealed record ImageBlock(float Width, float Height, int ImageIndex) : Block;

public sealed record Footnote(int Id, Paragraph Content);

/// <summary>A section starts on a new page. <paramref name="PageNumberStart"/> restarts the page number (null: continue).</summary>
public sealed record Section(int FirstBlock, int? PageNumberStart, bool HasFooter, string FooterPrefix, string HeaderText);

public sealed record DocModel(IReadOnlyList<Section> Sections, IReadOnlyList<Block> Blocks, IReadOnlyList<Footnote> Footnotes, IReadOnlyList<byte[]> Images);

public sealed record PageSetup(float Width, float Height, float MarginLeft, float MarginTop, float MarginRight, float MarginBottom, float HeaderDistance, float FooterDistance)
{
    /// <summary>A4 with 2.5 cm margins (11906 x 16838 twips, 1417 twips).</summary>
    public static readonly PageSetup A4 = new(595.3f, 841.9f, 70.85f, 70.85f, 70.85f, 70.85f, 35.45f, 35.45f);
    public float TextWidth => Width - MarginLeft - MarginRight;
}

/// <summary>Where a page starts: a block and the first line (paragraph) or row (table) of it on that page.</summary>
public readonly record struct PageStart(int BlockIndex, int LineIndex);

/// <summary>A position in the text of a paragraph block (a position in a table is reported as offset 0 of the table block).</summary>
public readonly record struct TextPosition(int Block, int Offset);

public readonly record struct LineMetrics(int Start, int Length, float Ascent, float Descent, float Width);
