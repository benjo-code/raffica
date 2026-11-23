using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public enum PageNumberLook
{
    /// <summary>The number alone.</summary>
    Plain,

    /// <summary>"Page 3".</summary>
    Page,

    /// <summary>"Page 3 of 12".</summary>
    PageOfTotal,

    /// <summary>"3 / 12".</summary>
    Slash,
}

public enum PageNumberAlign
{
    Left,
    Center,
    Right,
}

/// <summary>Builds and recognizes the paragraph of a page number: complex fields (begin, instruction, separate, result, end), so the layout and Word both evaluate them.</summary>
internal static class PageNumberField
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>
    /// A paragraph with a <c>PAGE</c> field (and a <c>NUMPAGES</c> field for the looks with a total). <paramref name="prefix"/> is a plain run just before the first field
    /// ("A-" gives "A-1"); the word of the look ("Page", "of") comes from the caller, so it can be localized. The cached results are what a reader that does not evaluate fields shows.
    /// </summary>
    public static Paragraph Build(PageNumberLook look, PageNumberAlign align, string prefix, string pageWord, string ofWord, string cachedPage, string cachedTotal)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(pageWord);
        ArgumentNullException.ThrowIfNull(ofWord);
        ArgumentNullException.ThrowIfNull(cachedPage);
        ArgumentNullException.ThrowIfNull(cachedTotal);
        var paragraph = new Paragraph();
        var value = align switch
        {
            PageNumberAlign.Center => "center",
            PageNumberAlign.Right => "right",
            _ => "left",
        };
        paragraph.Properties.Set(W + "jc", new XElement(W + "jc", new XAttribute(W + "val", value)));

        if (look is PageNumberLook.Page or PageNumberLook.PageOfTotal && pageWord.Length > 0)
        {
            paragraph.Inlines.Add(TextRun(pageWord + " "));
        }

        if (prefix.Length > 0)
        {
            paragraph.Inlines.Add(TextRun(prefix));
        }

        AddField(paragraph, "PAGE", cachedPage);
        switch (look)
        {
            case PageNumberLook.PageOfTotal:
                paragraph.Inlines.Add(TextRun(" " + ofWord + " "));
                AddField(paragraph, "NUMPAGES", cachedTotal);
                break;
            case PageNumberLook.Slash:
                paragraph.Inlines.Add(TextRun(" / "));
                AddField(paragraph, "NUMPAGES", cachedTotal);
                break;
        }

        return paragraph;
    }

    /// <summary>True when the paragraph holds a <c>PAGE</c> field (not <c>NUMPAGES</c>, <c>SECTIONPAGES</c> or <c>SECTION</c>).</summary>
    public static bool HoldsPage(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        foreach (var field in FieldScanner.Scan(paragraph.Inlines))
        {
            var instruction = field.Instruction.AsSpan().TrimStart();
            var end = instruction.IndexOfAny(' ', '\t');
            var word = end < 0 ? instruction : instruction[..end];
            if (word.Equals("PAGE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static Run TextRun(string text)
    {
        var run = new Run();
        run.Content.Add(new TextContent(text));
        return run;
    }

    private static Run Mark(FieldCharKind kind)
    {
        var run = new Run();
        run.Content.Add(new FieldChar(kind));
        return run;
    }

    private static void AddField(Paragraph paragraph, string instruction, string cached)
    {
        paragraph.Inlines.Add(Mark(FieldCharKind.Begin));
        var code = new Run();
        code.Content.Add(new InstructionText(" " + instruction + " "));
        paragraph.Inlines.Add(code);
        paragraph.Inlines.Add(Mark(FieldCharKind.Separate));
        paragraph.Inlines.Add(TextRun(cached));
        paragraph.Inlines.Add(Mark(FieldCharKind.End));
    }
}
