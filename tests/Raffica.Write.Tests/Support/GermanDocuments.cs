using System.Globalization;
using System.Security;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;

namespace Raffica.Write.Tests.Support;

/// <summary>
/// Documents as a German Word makes them, built in code: the style ids are German (<c>Standard</c>, <c>berschrift1</c>, <c>Verzeichnis1</c>, <c>Abbildungsverzeichnis</c>, <c>Beschriftung</c>), the names are Word's English built-in
/// names (<c>Normal</c>, <c>heading 1</c>, <c>toc 1</c>, <c>table of figures</c>, <c>caption</c>). The file has the entry styles of two levels only, as Word writes the ones a document used.
/// </summary>
internal static class GermanDocuments
{
    private const string Section = "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1417\" w:right=\"1417\" w:bottom=\"1134\" w:left=\"1417\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/></w:sectPr>";

    /// <summary>One paragraph style: its id, its name (null for none), the style it is based on and the content of its <c>w:pPr</c>; <paramref name="attributes"/> are further attributes of the element.</summary>
    public static string Style(string id, string? name, string? basedOn = "Standard", string paragraph = "", string attributes = "", string? type = "paragraph") =>
        $"<w:style{(type is null ? string.Empty : $" w:type=\"{type}\"")} w:styleId=\"{id}\"{attributes}>"
        + (name is null ? string.Empty : $"<w:name w:val=\"{name}\"/>")
        + (basedOn is null ? string.Empty : $"<w:basedOn w:val=\"{basedOn}\"/>")
        + $"<w:pPr>{paragraph}</w:pPr></w:style>";

    /// <summary>The default paragraph style of a German file, with the content of its <c>w:pPr</c>.</summary>
    public static string Standard(string paragraph = "") =>
        $"<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Standard\"><w:name w:val=\"Normal\"/><w:pPr>{paragraph}</w:pPr></w:style>";

    public static string Level(int value) => $"<w:outlineLvl w:val=\"{value.ToString(CultureInfo.InvariantCulture)}\"/>";

    /// <summary>The heading styles <c>berschrift1</c> to <c>berschrift3</c>; with <paramref name="numbered"/> each carries the list of <see cref="Numbering"/> at its level.</summary>
    public static string Headings(bool numbered)
    {
        var styles = string.Empty;
        for (var i = 1; i <= 3; i++)
        {
            var number = numbered ? $"<w:numPr>{(i == 1 ? string.Empty : $"<w:ilvl w:val=\"{i - 1}\"/>")}<w:numId w:val=\"1\"/></w:numPr>" : string.Empty;
            styles += Style($"berschrift{i}", $"heading {i}", "Standard", number + Level(i - 1));
        }

        return styles;
    }

    /// <summary>The caption style and the entry styles a German file holds: two levels of contents and the list of figures.</summary>
    public static string ListStyles() =>
        Style("Beschriftung", "caption")
        + Style("Verzeichnis1", "toc 1", "Standard", "<w:spacing w:after=\"100\"/>")
        + Style("Verzeichnis2", "toc 2", "Standard", "<w:spacing w:after=\"100\"/><w:ind w:left=\"220\"/>")
        + Style("Abbildungsverzeichnis", "table of figures", "Standard", "<w:spacing w:after=\"0\"/>");

    public static string Numbering() =>
        "<w:abstractNum w:abstractNumId=\"0\">"
        + "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1\"/></w:lvl>"
        + "<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.%2\"/></w:lvl>"
        + "<w:lvl w:ilvl=\"2\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.%2.%3\"/></w:lvl>"
        + "</w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";

    /// <summary>A German document of the body given (its section properties are added); <paramref name="styles"/> replaces the usual styles.</summary>
    public static WordDocument Document(string body, bool numbered = false, string? styles = null) =>
        LayoutModel.Document(body + Section, LayoutModel.StylesPart(styles ?? Standard() + Headings(numbered) + ListStyles()), LayoutModel.NumberingPart(Numbering()));

    public static string Run(string text) => $"<w:r><w:t xml:space=\"preserve\">{SecurityElement.Escape(text)}</w:t></w:r>";

    private static string Mark(string kind) => $"<w:r><w:fldChar w:fldCharType=\"{kind}\"/></w:r>";

    private static string Code(string instruction) => $"<w:r><w:instrText xml:space=\"preserve\"> {SecurityElement.Escape(instruction)} </w:instrText></w:r>";

    public static string Field(string instruction, string result = "?") => Mark("begin") + Code(instruction) + Mark("separate") + Run(result) + Mark("end");

    public static string P(string inner, string? style = null, string extra = "") =>
        $"<w:p><w:pPr>{(style is null ? string.Empty : $"<w:pStyle w:val=\"{style}\"/>")}{extra}</w:pPr>{inner}</w:p>";

    /// <summary>A paragraph of text in a style, with further children of its <c>w:pPr</c>.</summary>
    public static string Text(string text, string? style = null, string extra = "") => P(Run(text), style, extra);

    /// <summary>A table of contents with a stale entry: the field with the instruction, its result "stale" and the end mark in a paragraph of its own.</summary>
    public static string StaleToc(string instruction, string? style = null) =>
        P(Mark("begin") + Code(instruction) + Mark("separate") + Run("stale"), style) + P(Mark("end"));
}
