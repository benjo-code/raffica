using System.IO.Compression;
using System.Text;

namespace Raffica.WriteApp.Tests.Commands;

/// <summary>Builds small documents for the tests of the pickers: paragraphs of run XML, and optionally a styles part.</summary>
internal static class StyledDocx
{
    public const string W = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

    /// <summary>The styles of the tests: the default Normal, Heading 1 and Title (listed), a hidden and a semi-hidden one, an unused latent one and one that is not marked but may be used.</summary>
    public const string Styles = """
        <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>
        <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:uiPriority w:val="9"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="240"/></w:pPr><w:rPr><w:b/><w:sz w:val="32"/></w:rPr></w:style>
        <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:uiPriority w:val="10"/><w:qFormat/></w:style>
        <w:style w:type="paragraph" w:styleId="Zeta"><w:name w:val="Zeta"/><w:uiPriority w:val="9"/><w:qFormat/></w:style>
        <w:style w:type="paragraph" w:styleId="Quote"><w:name w:val="Quote"/><w:semiHidden/><w:qFormat/></w:style>
        <w:style w:type="paragraph" w:styleId="Gone"><w:name w:val="Gone"/><w:hidden/><w:qFormat/></w:style>
        <w:style w:type="paragraph" w:styleId="Latent"><w:name w:val="Latent"/></w:style>
        <w:style w:type="paragraph" w:styleId="UsedPlain"><w:name w:val="Used plain"/></w:style>
        <w:style w:type="character" w:default="1" w:styleId="DefaultParagraphFont"><w:name w:val="Default Paragraph Font"/><w:uiPriority w:val="1"/></w:style>
        <w:style w:type="character" w:styleId="Strong"><w:name w:val="Strong"/><w:uiPriority w:val="22"/><w:qFormat/><w:rPr><w:b/></w:rPr></w:style>
        <w:style w:type="character" w:styleId="Faint"><w:name w:val="Faint"/><w:semiHidden/><w:qFormat/></w:style>
        <w:style w:type="character" w:styleId="LatentChar"><w:name w:val="Latent char"/></w:style>
        """;

    /// <summary>Writes a document of the given paragraphs (the XML inside each <c>w:p</c>); <paramref name="styles"/> (the <c>w:style</c> elements) adds a styles part, null leaves it out.</summary>
    public static string Write(string folder, string name, string? styles, params string[] paragraphs)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, Bytes(styles, paragraphs));
        return path;
    }

    /// <summary>Normal, Heading 1 and Heading 2 with their outline levels (the outline lists a paragraph by its outline level).</summary>
    public const string StylesWithHeading2 = """
        <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>
        <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:uiPriority w:val="9"/><w:qFormat/><w:pPr><w:keepNext/><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:sz w:val="32"/></w:rPr></w:style>
        <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:uiPriority w:val="9"/><w:qFormat/><w:pPr><w:keepNext/><w:outlineLvl w:val="1"/></w:pPr><w:rPr><w:b/></w:rPr></w:style>
        """;

    /// <summary>A numbering part with one bullet definition (<c>w:num</c> 1) over three levels.</summary>
    public const string BulletNumbering = """
        <w:abstractNum w:abstractNumId="0"><w:multiLevelType w:val="hybridMultilevel"/>
        <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="-"/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr></w:lvl>
        <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="-"/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="1440" w:hanging="360"/></w:pPr></w:lvl>
        <w:lvl w:ilvl="2"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="-"/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="2160" w:hanging="360"/></w:pPr></w:lvl>
        </w:abstractNum>
        <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
        """;

    /// <summary>Writes a document with a numbering part (the <c>w:abstractNum</c> and <c>w:num</c> elements) and the given paragraphs.</summary>
    public static string WriteWithNumbering(string folder, string name, string styles, string numbering, params string[] paragraphs)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, Bytes(styles, numbering, paragraphs));
        return path;
    }

    public static byte[] Bytes(string? styles, params string[] paragraphs) => Bytes(styles, null, paragraphs);

    public static byte[] Bytes(string? styles, string? numbering, params string[] paragraphs)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(zip, "[Content_Types].xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  {(styles is null ? string.Empty : "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>")}
                  {(numbering is null ? string.Empty : "<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>")}
                </Types>
                """);
            Entry(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Entry(zip, "word/_rels/document.xml.rels", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  {(styles is null ? string.Empty : "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>")}
                  {(numbering is null ? string.Empty : "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/>")}
                </Relationships>
                """);
            if (styles is not null)
            {
                Entry(zip, "word/styles.xml", $"""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <w:styles {W}>{styles}</w:styles>
                    """);
            }

            if (numbering is not null)
            {
                Entry(zip, "word/numbering.xml", $"""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <w:numbering {W}>{numbering}</w:numbering>
                    """);
            }

            Entry(zip, "word/document.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document {W}>
                  <w:body>
                    {string.Concat(paragraphs.Select(p => $"<w:p>{p}</w:p>"))}
                    <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>
                  </w:body>
                </w:document>
                """);
        }

        return memory.ToArray();
    }

    private static void Entry(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text.TrimStart().Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
