using System.Globalization;
using System.Text;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace Raffica.Write.Tests.Layout;

/// <summary>Small documents and readings of laid-out pages for the page layout tests.</summary>
internal static class PageTestKit
{
    public static readonly FontCatalog Fonts = FontCatalog.CreateShipped(new NoSystemFonts());

    private static float? s_lineHeight;

    private sealed class NoSystemFonts : ISystemFonts
    {
        public FontFace? Match(string family, bool bold, bool italic) => null;
    }

    public static string Pgsz(float widthPoints, float heightPoints, string extra = "") =>
        string.Create(CultureInfo.InvariantCulture, $"<w:pgSz w:w=\"{widthPoints * 20}\" w:h=\"{heightPoints * 20}\"{extra}/>");

    public static string Margins(float left, float top, float right, float bottom) =>
        string.Create(CultureInfo.InvariantCulture, $"<w:pgMar w:top=\"{top * 20}\" w:right=\"{right * 20}\" w:bottom=\"{bottom * 20}\" w:left=\"{left * 20}\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/>");

    /// <summary>A section: <c>w:sectPr</c> with the given extra children first (type, cols, pgNumType), then size and margins.</summary>
    public static string Sect(string extra = "", float width = 612f, float height = 792f, float margin = 72f) =>
        $"<w:sectPr>{extra}{Pgsz(width, height)}{Margins(margin, margin, margin, margin)}</w:sectPr>";

    public static string P(string text, string pPr = "") =>
        $"<w:p><w:pPr>{pPr}</w:pPr><w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>";

    /// <summary>The last paragraph of a section: it carries the section's <c>w:sectPr</c>.</summary>
    public static string PEnd(string text, string sect, string pPr = "") =>
        $"<w:p><w:pPr>{pPr}{sect}</w:pPr><w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>";

    public static DocumentLayout Create(string body, string? styles = null, string? numbering = null, LayoutOptions? options = null)
    {
        var document = LayoutModel.Document(body, styles, numbering);
        return DocumentLayout.Create(document, Fonts, options ?? new LayoutOptions());
    }

    /// <summary>A layout of a document with a settings part (the inner XML of <c>w:settings</c>).</summary>
    public static DocumentLayout CreateWithSettings(string body, string settingsInner, LayoutOptions? options = null)
    {
        var document = LayoutModel.Document(body);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"<w:settings xmlns:w=\"{LayoutModel.W}\">{settingsInner}</w:settings>"));
        document.Settings = Raffica.Write.Docx.Xml.SettingsXml.Read(stream);
        return DocumentLayout.Create(document, Fonts, options ?? new LayoutOptions());
    }

    public static DocumentLayout Complete(string body, string? styles = null, string? numbering = null, LayoutOptions? options = null)
    {
        var layout = Create(body, styles, numbering, options);
        layout.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        return layout;
    }

    public static DocumentLayout Finish(DocumentLayout layout)
    {
        layout.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        return layout;
    }

    public static List<LaidOutPage> Pages(DocumentLayout layout)
    {
        var pages = new List<LaidOutPage>();
        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            pages.Add(layout.Page(i));
        }

        return pages;
    }

    public static List<GlyphRunItem> Runs(LaidOutPage page) => page.Items.OfType<GlyphRunItem>().ToList();

    /// <summary>The distinct baselines on a page, top to bottom.</summary>
    public static List<float> Baselines(LaidOutPage page) =>
        Runs(page).Select(r => r.Baseline).Distinct().OrderBy(b => b).ToList();

    public static string[] Lines(LaidOutPage page) => page.Text.Length == 0 ? [] : page.Text.Split('\n');

    /// <summary>The distance between the baselines of two single-line paragraphs of the default font without spacing.</summary>
    public static float LineHeight()
    {
        if (s_lineHeight is { } known)
        {
            return known;
        }

        var layout = Complete(P("a") + P("b") + Sect());
        var baselines = Baselines(layout.Page(0));
        s_lineHeight = baselines[1] - baselines[0];
        return s_lineHeight.Value;
    }

    /// <summary>A page of the given height whose text area holds a bit more than <paramref name="lines"/> default lines.</summary>
    public static string TallFor(int lines, string extra = "", float width = 612f)
    {
        var margin = 36f;
        var height = (margin * 2f) + (LineHeight() * lines) + (LineHeight() * 0.4f);
        return Sect(extra, width, height, margin);
    }

    public static string Repeat(string text, int count)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            builder.Append(text);
        }

        return builder.ToString();
    }

    public static string Numbered(string format, int count)
    {
        var builder = new StringBuilder();
        for (var i = 1; i <= count; i++)
        {
            builder.Append(P(string.Format(CultureInfo.InvariantCulture, format, i)));
        }

        return builder.ToString();
    }
}
