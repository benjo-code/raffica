namespace Raffica.Write.Docx;

/// <summary>The built-in styles of a new document. The ids are the ones Word uses, so a Write document keeps its look in Word.</summary>
public static class BlankStyleSet
{
    public static IReadOnlyList<string> Ids { get; } = BuildIds();

    private static string[] BuildIds()
    {
        var ids = new List<string> { "Normal" };
        for (var i = 1; i <= 9; i++)
        {
            ids.Add("Heading" + i);
        }

        ids.Add("Title");
        ids.Add("Caption");
        for (var i = 1; i <= 9; i++)
        {
            ids.Add("TOC" + i);
        }

        ids.Add("FootnoteText");
        ids.Add("ListParagraph");
        ids.Add("Hyperlink");
        return [.. ids];
    }

    // Heading look: Caladea bold, size in half-points, colour, space before and after in twips, italic.
    // Levels 1 to 3 carry the Write blue (1D4F91, 8.6:1 on white); 4 to 9 a dark slate (2F3F57, 10.6:1) so deep levels stay readable.
    internal static readonly (int Size, string Color, int Before, int After, bool Italic)[] Headings =
    [
        (40, "1D4F91", 360, 120, false),
        (32, "1D4F91", 320, 100, false),
        (28, "1D4F91", 280, 80, false),
        (24, "2F3F57", 240, 60, false),
        (22, "2F3F57", 200, 60, false),
        (22, "2F3F57", 200, 60, true),
        (22, "2F3F57", 160, 40, false),
        (22, "2F3F57", 160, 40, true),
        (22, "2F3F57", 160, 40, false),
    ];
}
