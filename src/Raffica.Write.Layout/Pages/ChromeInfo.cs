using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>
/// What a page remembers of its header and footer so that they can be made again without touching the rest: where they sit in the item, text and line arrays (header first, footer last), the
/// page's own number and section, and the heights and parts they were made with. Immutable; a rebuilt page carries the new values.
/// </summary>
internal sealed record ChromeInfo(
    SectionPlan Section,
    int Number,
    bool IsFirstOfSection,
    int HeaderItems,
    int FooterItems,
    int HeaderLines,
    int FooterLines,
    int HeaderRaw,
    int FooterRaw,
    int RawLength,
    float HeaderHeight,
    float FooterHeight,
    string? HeaderPart,
    string? FooterPart,
    bool UsesTotals = false,
    HeaderFooterType Type = HeaderFooterType.Default)
{
    /// <summary>What a finished page builder says about the chrome it holds.</summary>
    public static ChromeInfo Of(PageBuilder page) => new(
        page.Section,
        page.Number,
        page.IsFirstOfSection,
        page.ItemCount(PageRegion.Header),
        page.ItemCount(PageRegion.Footer),
        page.LineCount(PageRegion.Header),
        page.LineCount(PageRegion.Footer),
        page.RawLengthOf(PageRegion.Header),
        page.RawLengthOf(PageRegion.Footer),
        page.RawLength,
        page.HeaderHeight,
        page.FooterHeight,
        page.HeaderPart,
        page.FooterPart,
        page.UsesTotals,
        page.ChromeType);

    /// <summary>
    /// The page with its header and footer taken from <paramref name="scratch"/> (a builder that holds only those two regions) and everything else, body, notes, text and spans, as
    /// <paramref name="published"/> has it (its arrays are copied around the chrome, the body items themselves are shared).
    /// </summary>
    public LaidOutPage Rebuild(LaidOutPage published, PageBuilder scratch, bool isProvisional)
    {
        var header = scratch.ItemsOf(PageRegion.Header);
        var footer = scratch.ItemsOf(PageRegion.Footer);
        var old = published.ItemArray;
        var items = new PageItem[header.Length + (old.Length - HeaderItems - FooterItems) + footer.Length];
        header.CopyTo(items, 0);
        Array.Copy(old, HeaderItems, items, header.Length, old.Length - HeaderItems - FooterItems);
        footer.CopyTo(items, items.Length - footer.Length);

        var raw = published.Text.Length < RawLength ? published.Text + '\n' : published.Text;
        var newHeaderRaw = scratch.RawTextOf(PageRegion.Header);
        var newFooterRaw = scratch.RawTextOf(PageRegion.Footer);
        var raw2 = string.Concat(newHeaderRaw, raw.AsSpan(HeaderRaw, RawLength - HeaderRaw - FooterRaw), newFooterRaw);
        var text = raw2.Length == 0 ? string.Empty : raw2[..^1];

        var headerLines = scratch.LinesOf(PageRegion.Header);
        var footerLines = scratch.LinesOf(PageRegion.Footer);
        var oldLines = published.TextLines;
        var lines = new TextLine[headerLines.Length + (oldLines.Length - HeaderLines - FooterLines) + footerLines.Length];
        headerLines.CopyTo(lines, 0);
        Array.Copy(oldLines, HeaderLines, lines, headerLines.Length, oldLines.Length - HeaderLines - FooterLines);
        footerLines.CopyTo(lines, lines.Length - footerLines.Length);

        var info = this with
        {
            HeaderItems = header.Length,
            FooterItems = footer.Length,
            HeaderLines = headerLines.Length,
            FooterLines = footerLines.Length,
            HeaderRaw = newHeaderRaw.Length,
            FooterRaw = newFooterRaw.Length,
            RawLength = raw2.Length,
            HeaderHeight = scratch.HeaderHeight,
            FooterHeight = scratch.FooterHeight,
            HeaderPart = scratch.HeaderPart,
            FooterPart = scratch.FooterPart,
            UsesTotals = scratch.UsesTotals,
            Type = scratch.ChromeType,
        };
        return new LaidOutPage(published.Index, published.Number, published.Width, published.Height, published.IsStrip, items, text, published.Spans, isProvisional, lines, info);
    }
}
