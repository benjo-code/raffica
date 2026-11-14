using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>How a section starts (<c>w:type</c> of its <c>w:sectPr</c>).</summary>
internal enum SectionStart
{
    NextPage,
    Continuous,
    EvenPage,
    OddPage,
    NextColumn,
}

/// <summary>A body block with the index of the top-level block of the body it belongs to (blocks inside a content control carry the control's index).</summary>
internal readonly record struct FlatBlock(Block Block, int BodyIndex);

/// <summary>One section of the document: its properties, repaired geometry, how it starts, and the range of flat blocks it holds.</summary>
internal sealed class SectionPlan
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private SectionPlan(int index, SectionProperties? properties, PageGeometry geometry, int first, int end)
    {
        Index = index;
        Properties = properties;
        Geometry = geometry;
        FirstBlock = first;
        EndBlock = end;
        Start = ReadStart(properties);
        var numbering = properties?.Get(W + "pgNumType");
        PageNumberFormat = (string?)numbering?.Attribute(W + "fmt");
        PageNumberStart = int.TryParse((string?)numbering?.Attribute(W + "start"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var start) ? start : null;
        TitlePage = properties?.GetFlag(W + "titlePg") ?? false;
    }

    public int Index { get; }

    /// <summary>The <c>w:sectPr</c> (null for a body without one). Headers and footers (Task 9) read its references.</summary>
    public SectionProperties? Properties { get; }

    public PageGeometry Geometry { get; }

    public SectionStart Start { get; }

    /// <summary>The first and one past the last index into the flat block list.</summary>
    public int FirstBlock { get; }

    public int EndBlock { get; }

    public bool IsEmpty => EndBlock <= FirstBlock;

    /// <summary>The <c>w:fmt</c> of <c>w:pgNumType</c>, or null for decimal.</summary>
    public string? PageNumberFormat { get; }

    /// <summary>The <c>w:start</c> of <c>w:pgNumType</c>: the section's first page gets this number; null continues the count.</summary>
    public int? PageNumberStart { get; }

    /// <summary><c>w:titlePg</c>: the first page of the section has its own header and footer.</summary>
    public bool TitlePage { get; }

    /// <summary>Splits the body into sections at the paragraphs that carry a section break; the body's own <c>w:sectPr</c> ends the last one.</summary>
    public static List<SectionPlan> Build(IReadOnlyList<FlatBlock> blocks, SectionProperties? final, List<string> notes)
    {
        var sections = new List<SectionPlan>();
        var first = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Block is Paragraph { SectionBreak: { } properties })
            {
                sections.Add(Make(sections.Count, properties, first, i + 1, notes));
                first = i + 1;
            }
        }

        sections.Add(Make(sections.Count, final, first, blocks.Count, notes));
        return sections;
    }

    /// <summary>The one section of the writing view: all blocks, one column of strip geometry.</summary>
    public static SectionPlan ForStrips(PageGeometry geometry, int blockCount) => new(0, null, geometry, 0, blockCount);

    private static SectionPlan Make(int index, SectionProperties? properties, int first, int end, List<string> notes) =>
        new(index, properties, PageGeometry.Read(properties, "Section " + (index + 1).ToString(CultureInfo.InvariantCulture), notes), first, end);

    private static SectionStart ReadStart(SectionProperties? properties) => (string?)properties?.Get(W + "type")?.Attribute(W + "val") switch
    {
        "continuous" => SectionStart.Continuous,
        "evenPage" => SectionStart.EvenPage,
        "oddPage" => SectionStart.OddPage,
        "nextColumn" => SectionStart.NextColumn,
        _ => SectionStart.NextPage,
    };
}

/// <summary>Formats a page number the way <c>w:pgNumType w:fmt</c> asks.</summary>
internal static class PageNumbers
{
    private static readonly (int Value, string Roman)[] s_roman =
    [
        (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"), (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i"),
    ];

    /// <summary>decimal, lowerRoman, upperRoman, lowerLetter, upperLetter; any other format, and a number below 1, is decimal.</summary>
    public static string Format(int number, string? format)
    {
        if (number < 1)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        return format switch
        {
            "lowerRoman" => number < 4000 ? Roman(number) : number.ToString(CultureInfo.InvariantCulture),
            "upperRoman" => number < 4000 ? Roman(number).ToUpperInvariant() : number.ToString(CultureInfo.InvariantCulture),
            "lowerLetter" => Letters(number, 'a'),
            "upperLetter" => Letters(number, 'A'),
            _ => number.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static string Roman(int number)
    {
        var text = new StringBuilder();
        foreach (var (value, roman) in s_roman)
        {
            while (number >= value)
            {
                text.Append(roman);
                number -= value;
            }
        }

        return text.ToString();
    }

    // Word's letter numbering: a..z, then aa, bb, cc (the same letter repeated), not base 26.
    private static string Letters(int number, char first)
    {
        var letter = (char)(first + ((number - 1) % 26));
        var count = ((number - 1) / 26) + 1;
        return count > 64 ? number.ToString(CultureInfo.InvariantCulture) : new string(letter, count);
    }
}

/// <summary>Flattens the blocks of a story: a block content control's blocks stand in its place.</summary>
internal static class FlatBlocks
{
    private const int MaximumDepth = 32;
    private const int TopLevelIndex = -2;

    /// <summary>The blocks in reading order; every one carries <paramref name="bodyIndex"/>, or the index of its top-level block when that is left out (the body).</summary>
    public static List<FlatBlock> From(IList<Block> blocks, int bodyIndex = TopLevelIndex)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var flat = new List<FlatBlock>(blocks.Count);
        for (var i = 0; i < blocks.Count; i++)
        {
            Add(flat, blocks[i], bodyIndex == TopLevelIndex ? i : bodyIndex, 0);
        }

        return flat;
    }

    private static void Add(List<FlatBlock> flat, Block block, int bodyIndex, int depth)
    {
        if (block is ContentControlBlock control && depth < MaximumDepth)
        {
            foreach (var inner in control.Content)
            {
                Add(flat, inner, bodyIndex, depth + 1);
            }

            return;
        }

        flat.Add(new FlatBlock(block, bodyIndex));
    }
}
