using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace Raffica.Write.Tests.Support;

/// <summary>The page numbers a layout drew, read from its pages, with the section of each page for <see cref="ReferencePageNumbers"/>.</summary>
internal static class DrawnPageNumbers
{
    /// <summary>
    /// Lays the rest of the document out, then returns for every page its <c>LaidOutPage.Number</c> and the section of its first block (<see cref="DocumentLayout.Locate"/>; a page
    /// that no block starts on, a continued block or a blank page, belongs to the section of the page before it, the first one to section 0).
    /// </summary>
    public static IReadOnlyList<(string Number, int Section)> Read(DocumentLayout layout, WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(document);
        layout.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();

        var blocks = document.Body.Blocks;
        var sectionOfBlock = new int[blocks.Count];
        var section = 0;
        var firstBlockOfPage = new Dictionary<int, int>();
        for (var i = 0; i < blocks.Count; i++)
        {
            sectionOfBlock[i] = section;
            if (layout.Locate(i) is { } at && !firstBlockOfPage.ContainsKey(at.Page))
            {
                firstBlockOfPage[at.Page] = i;
            }

            if (blocks[i] is Paragraph { SectionBreak: not null })
            {
                section++;
            }
        }

        var drawn = new List<(string, int)>(layout.LaidOutPages);
        var current = 0;
        for (var p = 0; p < layout.LaidOutPages; p++)
        {
            if (firstBlockOfPage.TryGetValue(p, out var block))
            {
                current = sectionOfBlock[block];
            }

            drawn.Add((layout.Page(p).Number, current));
        }

        return drawn;
    }
}
