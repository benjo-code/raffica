using System.Globalization;
using System.Text;
using Raffica.Write.Docx.Xml;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Layout;

/// <summary>Documents with header and footer parts, footnotes and endnotes for the chrome tests.</summary>
internal static class ChromeTestKit
{
    private const string W = LayoutModel.W;
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static MemoryStream Utf8(string xml) => new(Encoding.UTF8.GetBytes(xml));

    public static string Header(string type, string id) => $"<w:headerReference w:type=\"{type}\" r:id=\"{id}\"/>";

    public static string Footer(string type, string id) => $"<w:footerReference w:type=\"{type}\" r:id=\"{id}\"/>";

    /// <summary>A one-line paragraph with the given text.</summary>
    public static string Line(string text) => $"<w:p><w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>";

    /// <summary>The five runs of a complex field with a cached result.</summary>
    public static string Field(string instruction, string cached) =>
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
        + $"<w:r><w:instrText xml:space=\"preserve\"> {instruction} </w:instrText></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
        + $"<w:r><w:t>{cached}</w:t></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    public static string Text(string text) => $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    public static string Para(string inner) => $"<w:p>{inner}</w:p>";

    /// <summary>A section that holds about <paramref name="lines"/> body lines between 72 pt margins (the header and footer sit in the margins).</summary>
    public static string Tall(int lines, string extra = "") =>
        Sect(extra, 612f, 144f + (LineHeight() * (lines + 0.4f)), 72f);

    public static string HeaderPart(string inner) => $"<w:hdr xmlns:w=\"{W}\">{inner}</w:hdr>";

    public static string FooterPart(string inner) => $"<w:ftr xmlns:w=\"{W}\">{inner}</w:ftr>";

    public static string Footnote(int id, string text) =>
        $"<w:footnote w:id=\"{id}\"><w:p><w:r><w:footnoteRef/></w:r><w:r><w:t xml:space=\"preserve\"> {text}</w:t></w:r></w:p></w:footnote>";

    public static string Endnote(int id, string text) =>
        $"<w:endnote w:id=\"{id}\"><w:p><w:r><w:endnoteRef/></w:r><w:r><w:t xml:space=\"preserve\"> {text}</w:t></w:r></w:p></w:endnote>";

    public static string FootnotesPart(params string[] notes) =>
        $"<w:footnotes xmlns:w=\"{W}\"><w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>"
        + $"<w:footnote w:type=\"continuationSeparator\" w:id=\"0\"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>{string.Concat(notes)}</w:footnotes>";

    public static string EndnotesPart(params string[] notes) =>
        $"<w:endnotes xmlns:w=\"{W}\"><w:endnote w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:endnote>{string.Concat(notes)}</w:endnotes>";

    public static string FootnoteRef(int id) => $"<w:r><w:footnoteReference w:id=\"{id}\"/></w:r>";

    public static string EndnoteRef(int id) => $"<w:r><w:endnoteReference w:id=\"{id}\"/></w:r>";

    public static WordDocument Document(
        string body,
        IReadOnlyDictionary<string, string>? parts = null,
        string? footnotes = null,
        string? endnotes = null,
        string? settingsInner = null)
    {
        using var stream = Utf8($"<w:document xmlns:w=\"{W}\" xmlns:r=\"{R}\"><w:body>{body}</w:body></w:document>");
        var document = new WordDocument(DocumentXml.Read(stream));
        foreach (var (id, xml) in parts ?? new Dictionary<string, string>())
        {
            using var part = Utf8(xml);
            document.HeadersFooters[id] = StoryXml.ReadPart(part);
        }

        if (footnotes is not null)
        {
            using var s = Utf8(footnotes);
            document.Footnotes = NotesXml.Read(s, "footnotes");
        }

        if (endnotes is not null)
        {
            using var s = Utf8(endnotes);
            document.Endnotes = NotesXml.Read(s, "endnotes");
        }

        if (settingsInner is not null)
        {
            using var s = Utf8($"<w:settings xmlns:w=\"{W}\">{settingsInner}</w:settings>");
            document.Settings = SettingsXml.Read(s);
        }

        return document;
    }

    public static DocumentLayout Laid(WordDocument document) => Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions()));

    public static string FirstLine(LaidOutPage page) => Lines(page)[0];

    public static string LastLine(LaidOutPage page) => Lines(page)[^1];

    /// <summary>Paragraphs "prefix 1", "prefix 2", ... one per line.</summary>
    public static string Body(string prefix, int count)
    {
        var builder = new StringBuilder();
        for (var i = 1; i <= count; i++)
        {
            builder.Append(Line(string.Create(CultureInfo.InvariantCulture, $"{prefix} {i}")));
        }

        return builder.ToString();
    }

    public static int CountLines(LaidOutPage page, string prefix) => Lines(page).Count(l => l.StartsWith(prefix, StringComparison.Ordinal));

    public static IEnumerable<RuleItem> Rules(LaidOutPage page) => page.Items.OfType<RuleItem>();
}
