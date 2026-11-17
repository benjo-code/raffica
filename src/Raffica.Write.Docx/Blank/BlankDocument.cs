using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Raffica.Write.Docx;

/// <summary>Makes the bytes of a new, empty DOCX: one empty paragraph, the built-in styles and the numbering definitions. The bytes are the same for the same paper.</summary>
public static class BlankDocument
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string BodyFont = "Carlito";
    private const string HeadingFont = "Caladea";
    private static readonly DateTimeOffset s_entryTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static byte[] Create(PaperSize paper)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", ContentTypes);
            Add(zip, "_rels/.rels", RootRels);
            Add(zip, "word/document.xml", DocumentXml(paper));
            Add(zip, "word/_rels/document.xml.rels", DocumentRels);
            Add(zip, "word/styles.xml", StylesXml());
            Add(zip, "word/numbering.xml", NumberingXml());
            Add(zip, "word/settings.xml", SettingsXml);
            Add(zip, "word/fontTable.xml", FontTableXml);
            Add(zip, "docProps/core.xml", CoreXml);
            Add(zip, "docProps/app.xml", AppXml);
        }

        return stream.ToArray();
    }

    private static void Add(ZipArchive zip, string name, string xml)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = s_entryTime;
        using var output = entry.Open();
        output.Write(new UTF8Encoding(false).GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" + xml));
    }

    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    private const string ContentTypes =
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
        + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
        + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
        + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
        + "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>"
        + "<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>"
        + "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>"
        + "<Override PartName=\"/word/fontTable.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml\"/>"
        + "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>"
        + "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>"
        + "</Types>";

    private const string RootRels =
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
        + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>"
        + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>"
        + "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/>"
        + "</Relationships>";

    private const string DocumentRels =
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
        + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>"
        + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/>"
        + "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>"
        + "<Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable\" Target=\"fontTable.xml\"/>"
        + "</Relationships>";

    private static string DocumentXml(PaperSize p) =>
        $"<w:document xmlns:w=\"{W}\" xmlns:r=\"{R}\"><w:body><w:p/>"
        + $"<w:sectPr><w:pgSz w:w=\"{I(p.WidthTwips)}\" w:h=\"{I(p.HeightTwips)}\"/>"
        + $"<w:pgMar w:top=\"{I(p.MarginTwips)}\" w:right=\"{I(p.MarginTwips)}\" w:bottom=\"{I(p.MarginTwips)}\" w:left=\"{I(p.MarginTwips)}\" w:header=\"709\" w:footer=\"709\" w:gutter=\"0\"/>"
        + "<w:cols w:space=\"709\"/></w:sectPr></w:body></w:document>";

    private const string SettingsXml =
        $"<w:settings xmlns:w=\"{W}\"><w:defaultTabStop w:val=\"720\"/>"
        + "<w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat></w:settings>";

    private const string FontTableXml =
        $"<w:fonts xmlns:w=\"{W}\">"
        + "<w:font w:name=\"Carlito\"><w:panose1 w:val=\"020F0502020204030204\"/><w:charset w:val=\"00\"/><w:family w:val=\"swiss\"/><w:pitch w:val=\"variable\"/></w:font>"
        + "<w:font w:name=\"Caladea\"><w:panose1 w:val=\"02040503050406030204\"/><w:charset w:val=\"00\"/><w:family w:val=\"roman\"/><w:pitch w:val=\"variable\"/></w:font>"
        + "</w:fonts>";

    private const string CoreXml =
        "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
        + "<dcterms:created xsi:type=\"dcterms:W3CDTF\">2026-01-01T00:00:00Z</dcterms:created>"
        + "<dcterms:modified xsi:type=\"dcterms:W3CDTF\">2026-01-01T00:00:00Z</dcterms:modified></cp:coreProperties>";

    private const string AppXml =
        "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>Raffica Write</Application></Properties>";

    private static string Fonts(string name) => $"<w:rFonts w:ascii=\"{name}\" w:hAnsi=\"{name}\" w:eastAsia=\"{name}\" w:cs=\"{name}\"/>";

    private static string StylesXml()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<w:styles xmlns:w=\"{W}\"><w:docDefaults><w:rPrDefault><w:rPr>{Fonts(BodyFont)}<w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/><w:lang w:val=\"en-US\" w:eastAsia=\"en-US\" w:bidi=\"ar-SA\"/></w:rPr></w:rPrDefault>");
        sb.Append("<w:pPrDefault><w:pPr><w:spacing w:after=\"160\" w:line=\"264\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>");
        sb.Append("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>");
        sb.Append("<w:style w:type=\"character\" w:default=\"1\" w:styleId=\"DefaultParagraphFont\"><w:name w:val=\"Default Paragraph Font\"/><w:uiPriority w:val=\"1\"/><w:semiHidden/><w:unhideWhenUsed/></w:style>");
        sb.Append("<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:name w:val=\"Normal Table\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:unhideWhenUsed/>"
            + "<w:tblPr><w:tblInd w:w=\"0\" w:type=\"dxa\"/><w:tblCellMar><w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"108\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"108\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>");
        sb.Append("<w:style w:type=\"numbering\" w:default=\"1\" w:styleId=\"NoList\"><w:name w:val=\"No List\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:unhideWhenUsed/></w:style>");

        for (var level = 1; level <= 9; level++)
        {
            var (size, color, before, after, italic) = BlankStyleSet.Headings[level - 1];
            sb.Append(CultureInfo.InvariantCulture, $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{I(level)}\"><w:name w:val=\"heading {I(level)}\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:uiPriority w:val=\"9\"/>{(level == 1 ? string.Empty : "<w:unhideWhenUsed/>")}<w:qFormat/>");
            sb.Append(CultureInfo.InvariantCulture, $"<w:pPr><w:keepNext/><w:keepLines/><w:spacing w:before=\"{I(before)}\" w:after=\"{I(after)}\" w:line=\"240\" w:lineRule=\"auto\"/><w:outlineLvl w:val=\"{I(level - 1)}\"/></w:pPr>");
            sb.Append(CultureInfo.InvariantCulture, $"<w:rPr>{Fonts(HeadingFont)}<w:b/><w:bCs/>{(italic ? "<w:i/><w:iCs/>" : string.Empty)}<w:color w:val=\"{color}\"/><w:sz w:val=\"{I(size)}\"/><w:szCs w:val=\"{I(size)}\"/></w:rPr></w:style>");
        }

        sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"Title\"><w:name w:val=\"Title\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:uiPriority w:val=\"10\"/><w:qFormat/>"
            + "<w:pPr><w:spacing w:before=\"0\" w:after=\"240\" w:line=\"240\" w:lineRule=\"auto\"/><w:contextualSpacing/></w:pPr>"
            + $"<w:rPr>{Fonts(HeadingFont)}<w:b/><w:bCs/><w:color w:val=\"1D4F91\"/><w:spacing w:val=\"-6\"/><w:kern w:val=\"28\"/><w:sz w:val=\"56\"/><w:szCs w:val=\"56\"/></w:rPr></w:style>");
        sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"Caption\"><w:name w:val=\"caption\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:uiPriority w:val=\"35\"/><w:unhideWhenUsed/><w:qFormat/>"
            + "<w:pPr><w:spacing w:before=\"60\" w:after=\"200\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr>"
            + "<w:rPr><w:i/><w:iCs/><w:color w:val=\"595959\"/><w:sz w:val=\"18\"/><w:szCs w:val=\"18\"/></w:rPr></w:style>");
        for (var level = 1; level <= 9; level++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<w:style w:type=\"paragraph\" w:styleId=\"TOC{I(level)}\"><w:name w:val=\"toc {I(level)}\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:autoRedefine/><w:uiPriority w:val=\"39\"/><w:unhideWhenUsed/>");
            sb.Append(CultureInfo.InvariantCulture, $"<w:pPr><w:spacing w:after=\"100\"/><w:ind w:left=\"{I((level - 1) * 220)}\"/></w:pPr></w:style>");
        }

        sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"FootnoteText\"><w:name w:val=\"footnote text\"/><w:basedOn w:val=\"Normal\"/><w:uiPriority w:val=\"99\"/><w:unhideWhenUsed/>"
            + "<w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:rPr><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/></w:rPr></w:style>");
        sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"ListParagraph\"><w:name w:val=\"List Paragraph\"/><w:basedOn w:val=\"Normal\"/><w:uiPriority w:val=\"34\"/><w:qFormat/>"
            + "<w:pPr><w:ind w:left=\"720\"/><w:contextualSpacing/></w:pPr></w:style>");
        sb.Append("<w:style w:type=\"character\" w:styleId=\"Hyperlink\"><w:name w:val=\"Hyperlink\"/><w:basedOn w:val=\"DefaultParagraphFont\"/><w:uiPriority w:val=\"99\"/><w:unhideWhenUsed/>"
            + "<w:rPr><w:color w:val=\"0B5CAD\"/><w:u w:val=\"single\"/></w:rPr></w:style>");
        sb.Append("</w:styles>");
        return sb.ToString();
    }

    private static string NumberingXml()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<w:numbering xmlns:w=\"{W}\">");

        // 0: multi-level decimal (1., 1.1., 1.1.1. ...), linked to no style.
        sb.Append("<w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"multilevel\"/>");
        for (var level = 0; level < 9; level++)
        {
            var text = new StringBuilder();
            for (var k = 1; k <= level + 1; k++)
            {
                text.Append('%').Append(I(k)).Append('.');
            }

            var indent = 432 + (level * 144);
            sb.Append(CultureInfo.InvariantCulture, $"<w:lvl w:ilvl=\"{I(level)}\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"{text}\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"{I(indent)}\" w:hanging=\"{I(indent)}\"/></w:pPr></w:lvl>");
        }

        sb.Append("</w:abstractNum>");

        // 1: bullets, 2: numbered list; each level steps 360 twips in from a 720 start.
        sb.Append("<w:abstractNum w:abstractNumId=\"1\"><w:multiLevelType w:val=\"hybridMultilevel\"/>");
        for (var level = 0; level < 9; level++)
        {
            var (font, glyph) = (level % 3) switch
            {
                0 => ("Symbol", ""),
                1 => ("Courier New", "o"),
                _ => ("Wingdings", ""),
            };
            sb.Append(CultureInfo.InvariantCulture, $"<w:lvl w:ilvl=\"{I(level)}\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"{glyph}\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"{I(720 + (level * 360))}\" w:hanging=\"360\"/></w:pPr><w:rPr><w:rFonts w:ascii=\"{font}\" w:hAnsi=\"{font}\" w:hint=\"default\"/></w:rPr></w:lvl>");
        }

        sb.Append("</w:abstractNum>");
        sb.Append("<w:abstractNum w:abstractNumId=\"2\"><w:multiLevelType w:val=\"hybridMultilevel\"/>");
        string[] formats = ["decimal", "lowerLetter", "lowerRoman"];
        for (var level = 0; level < 9; level++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<w:lvl w:ilvl=\"{I(level)}\"><w:start w:val=\"1\"/><w:numFmt w:val=\"{formats[level % 3]}\"/><w:lvlText w:val=\"%{I(level + 1)}.\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"{I(720 + (level * 360))}\" w:hanging=\"360\"/></w:pPr></w:lvl>");
        }

        sb.Append("</w:abstractNum>");
        sb.Append("<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num><w:num w:numId=\"2\"><w:abstractNumId w:val=\"1\"/></w:num><w:num w:numId=\"3\"><w:abstractNumId w:val=\"2\"/></w:num>");
        sb.Append("</w:numbering>");
        return sb.ToString();
    }
}
