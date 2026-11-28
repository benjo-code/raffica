using System.IO.Compression;
using System.Text;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.WriteApp.Tests.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Editing;

/// <summary>A one-shot timer that the test fires by hand; it records how it was started.</summary>
internal sealed class ManualFieldClock : IFieldClock
{
    private Action? _tick;

    /// <summary>How many times the timer was started or started over.</summary>
    public int Restarts { get; private set; }

    /// <summary>The delay of the last start.</summary>
    public TimeSpan LastDelay { get; private set; }

    /// <summary>True from a start until the tick or a stop.</summary>
    public bool IsArmed { get; private set; }

    /// <summary>How many timers were made.</summary>
    public int Created { get; private set; }

    public IFieldTimer CreateTimer(Action tick)
    {
        Created++;
        _tick = tick;
        return new Timer(this);
    }

    /// <summary>The tick, when the timer is armed (a stopped timer does not tick).</summary>
    public void Fire()
    {
        if (IsArmed)
        {
            IsArmed = false;
            _tick?.Invoke();
        }
    }

    private sealed class Timer(ManualFieldClock owner) : IFieldTimer
    {
        public bool IsArmed => owner.IsArmed;

        public void Restart(TimeSpan delay)
        {
            owner.Restarts++;
            owner.LastDelay = delay;
            owner.IsArmed = true;
        }

        public void Stop() => owner.IsArmed = false;

        public void Dispose() => owner.IsArmed = false;
    }
}

/// <summary>
/// A document of five pages with stale field results: two figure captions (<c>SEQ Figure</c>, stored 77 and 77, so each is two characters long and the refresh makes them 1 and 2), a field Write does not update (<c>INDEX</c>, stored
/// "idx") and a last paragraph.
/// </summary>
internal static class FieldTestDocument
{
    public const string Stale = "77";

    /// <param name="numbered">Gives Heading1 a numbering (decimal, "1.") through a numbering part: the label of the first heading is "1.".</param>
    /// <param name="onePage">Leaves out the page breaks: the whole document is one page, so its layout is complete when it opens.</param>
    /// <param name="styleRef">Adds a paragraph with a <c>STYLEREF 1 s</c> field behind the two captions (it needs the chapter label of the host).</param>
    /// <param name="headings">Makes the first paragraph a Heading1 (a style step, the heading numbering switch, needs it; the style sheet with the nine heading styles is always there).</param>
    public static byte[] Bytes(bool headings = false, bool styleRef = false, bool numbered = false, bool onePage = false)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
                </Types>
                """.Replace("</Types>", numbered ? "<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/></Types>" : "</Types>", StringComparison.Ordinal));
            Entry(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Entry(zip, "word/_rels/document.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """.Replace("</Relationships>", numbered ? "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/></Relationships>" : "</Relationships>", StringComparison.Ordinal));
            Entry(zip, "word/styles.xml", Styles(numbered));
            if (numbered)
            {
                Entry(zip, "word/numbering.xml", "<w:numbering xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num></w:numbering>");
            }

            var body = new StringBuilder();
            body.Append(headings ? "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>Intro</w:t></w:r></w:p>" : "<w:p><w:r><w:t>Intro</w:t></w:r></w:p>");
            body.Append(Caption());
            body.Append(Caption());
            if (styleRef)
            {
                body.Append(Paragraph("<w:r><w:t xml:space=\"preserve\">Chapter </w:t></w:r>" + Field(" STYLEREF 1 \\s ", "9")));
            }

            body.Append(Paragraph("<w:r><w:t xml:space=\"preserve\">Index </w:t></w:r>" + Field(" INDEX \\h ", "idx")));
            body.Append(Paragraph("<w:r><w:t>End</w:t></w:r>"));
            var bodyText = onePage ? body.ToString().Replace("<w:pPr><w:pageBreakBefore/></w:pPr>", string.Empty, StringComparison.Ordinal) : body.ToString();
            Entry(zip, "word/document.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>{bodyText}
                    <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>
                  </w:body>
                </w:document>
                """);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// A document for the page results: an empty first paragraph (the place for a table of contents), an "intro" paragraph, three Heading1 chapters (Alpha, Beta, Gamma) with a line of text each, a paragraph that holds the bookmark <c>Target</c> and a last one with a stale
    /// <c>PAGEREF Target</c> (stored "77"). Nothing starts a page of its own, so every block is on the first page until text is typed above them.
    /// </summary>
    public static byte[] TocBytes() => Package(
        "<w:p/>" + Plain("intro") + Chapter("Alpha") + Plain("alpha text") + Chapter("Beta") + Plain("beta text") + Chapter("Gamma") + Plain("gamma text")
            + "<w:p><w:bookmarkStart w:id=\"1\" w:name=\"Target\"/><w:r><w:t>the target</w:t></w:r><w:bookmarkEnd w:id=\"1\"/></w:p>"
            + "<w:p><w:r><w:t xml:space=\"preserve\">see page </w:t></w:r>" + Field(" PAGEREF Target \\h ", Stale) + "</w:p>");

    /// <summary>The chapters of <see cref="LongTocBytes"/>.</summary>
    public const int LongChapters = 60;

    /// <summary>
    /// A long document (about twenty pages) that begins with a stale table of contents of two paragraphs, followed by <see cref="LongChapters"/> Heading1 chapters with fifteen lines each: the first refresh makes the list sixty lines long, so every page after it starts at
    /// another line.
    /// </summary>
    public static byte[] LongTocBytes()
    {
        var body = new StringBuilder("<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-3\" \\h </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>stale</w:t></w:r></w:p>");
        body.Append("<w:p><w:r><w:t>stale too</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");
        for (var chapter = 1; chapter <= LongChapters; chapter++)
        {
            body.Append(Chapter("Chapter " + chapter.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            for (var line = 0; line < 15; line++)
            {
                body.Append(Plain("a line of text"));
            }
        }

        return Package(body.ToString());
    }

    /// <summary>
    /// A short document of four pages that begins with a stale table of contents of two paragraphs, followed by the chapters Alpha, Beta and Gamma, each on a page of its own: small enough to be opened whole, so its pages layout
    /// holds page 1 alone until something lays it out.
    /// </summary>
    public static byte[] PagedTocBytes()
    {
        var body = new StringBuilder("<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-3\" \\h </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>stale</w:t></w:r></w:p>");
        body.Append("<w:p><w:r><w:t>stale too</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");
        foreach (var name in (string[])["Alpha", "Beta", "Gamma"])
        {
            body.Append(Chapter(name).Replace("<w:pStyle w:val=\"Heading1\"/>", "<w:pStyle w:val=\"Heading1\"/><w:pageBreakBefore/>", StringComparison.Ordinal));
            body.Append(Plain("a line of text"));
        }

        return Package(body.ToString());
    }

    private static string Plain(string text) => "<w:p><w:r><w:t xml:space=\"preserve\">" + text + "</w:t></w:r></w:p>";

    private static string Chapter(string text) => "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>" + text + "</w:t></w:r></w:p>";

    // A document with the style sheet of the nine headings and the given body on Letter paper.
    private static byte[] Package(string body)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
                </Types>
                """);
            Entry(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Entry(zip, "word/_rels/document.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            Entry(zip, "word/styles.xml", Styles());
            Entry(zip, "word/document.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>{body}
                    <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>
                  </w:body>
                </w:document>
                """);
        }

        return stream.ToArray();
    }

    /// <summary>A document with no field at all, five pages.</summary>
    public static byte[] PlainBytes()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
            Entry(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            var body = new StringBuilder();
            for (var i = 0; i < 5; i++)
            {
                body.Append(Paragraph($"<w:r><w:t>Text {i}</w:t></w:r>"));
            }

            Entry(zip, "word/document.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>{body}
                    <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>
                  </w:body>
                </w:document>
                """);
        }

        return stream.ToArray();
    }

    private static string Styles(bool numbered = false)
    {
        var text = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>");
        for (var i = 1; i <= 9; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:pPr>{(numbered && i == 1 ? "<w:numPr><w:numId w:val=\"1\"/></w:numPr>" : string.Empty)}<w:outlineLvl w:val=\"{i - 1}\"/></w:pPr></w:style>");
        }

        return text.Append("</w:styles>").ToString();
    }

    // Every paragraph but the first starts a page.
    private static string Paragraph(string inner) => "<w:p><w:pPr><w:pageBreakBefore/></w:pPr>" + inner + "</w:p>";

    private static string Caption() => Paragraph("<w:r><w:t xml:space=\"preserve\">Figure </w:t></w:r>" + Field(" SEQ Figure \\* ARABIC ", Stale));

    private static string Field(string instruction, string result) =>
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
        + $"<w:r><w:instrText xml:space=\"preserve\">{instruction}</w:instrText></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
        + $"<w:r><w:t>{result}</w:t></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    private static void Entry(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text.TrimStart());
    }

    /// <summary>The visible text of the nth body paragraph (the stored results of the fields).</summary>
    public static string TextOf(WriteTab tab, int block) => ParagraphProjection.Build((Paragraph)tab.Docx.Model.Body.Blocks[block], ProjectionHost.Plain).Text;
}

/// <summary>A tab over <see cref="FieldTestDocument"/> with a manual clock for the refresh timer, opened whole (small), its layout started or not.</summary>
internal sealed class FieldHarness : IDisposable
{
    private FieldHarness(string folder, WriteTab tab, ManualFieldClock clock)
    {
        Folder = folder;
        Tab = tab;
        Clock = clock;
    }

    public string Folder { get; }

    public WriteTab Tab { get; }

    public ManualFieldClock Clock { get; }

    public string Path => System.IO.Path.Combine(Folder, "fields.docx");

    public static async Task<FieldHarness> OpenAsync(bool layoutComplete = true, byte[]? bytes = null)
    {
        var folder = Directory.CreateTempSubdirectory("raffica-write-fields-").FullName;
        var path = System.IO.Path.Combine(folder, "fields.docx");
        File.WriteAllBytes(path, bytes ?? FieldTestDocument.Bytes());
        var attempt = DocumentOpener.Open(path, new OpenWork(), DocumentOpener.DefaultMaxFileLength, CancellationToken.None);
        Assert.NotNull(attempt.Document);
        var tab = new WriteTab(path, attempt.Document!);
        var clock = new ManualFieldClock();
        tab.FieldClock = clock;
        tab.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(60);
        var harness = new FieldHarness(folder, tab, clock);
        if (layoutComplete)
        {
            await harness.CompleteLayoutAsync();
        }

        return harness;
    }

    public async Task CompleteLayoutAsync()
    {
        Tab.Runner.Start();
        await ViewHarness.WaitAsync(() => Tab.Runner.IsFaulted || (Tab.Runner.FullModelReady && Tab.Runner.PageLayout.IsComplete), 30000);
        Assert.True(Tab.Runner.PageLayout.IsComplete);
    }

    /// <summary>Types a letter at the start of the first paragraph through the session: an edit that makes the document dirty.</summary>
    public EditResult Type(string text = "x")
    {
        var session = Tab.EnsureEditor();
        var result = session.Apply(e => e.InsertText(session.Selection!.Value, text), 0);
        Assert.NotNull(result);
        return result;
    }

    /// <summary>
    /// The timer tick once the layout is complete: a refresh of a document that has page results (a table of contents, a PAGEREF) waits for that, and an edit makes the layout incomplete until its background pass is over, so a test that ticks right after an edit would
    /// otherwise race with the layout.
    /// </summary>
    public async Task FireAsync()
    {
        await ViewHarness.WaitAsync(() => Tab.Runner.IsFaulted || (Tab.Editor?.ActiveLayout ?? Tab.Runner.PageLayout) is { IsComplete: true }, 60000);
        Clock.Fire();
    }

    /// <summary>Ticks until the timer stays quiet (a refresh whose results moved the pages waits for the background layout between its passes, one tick each), at most ten times.</summary>
    public async Task FireUntilIdleAsync()
    {
        for (var i = 0; i < 10 && (i == 0 || Clock.IsArmed); i++)
        {
            await FireAsync();
        }
    }

    public static void Pump() => ViewHarness.Pump();

    public void Dispose()
    {
        Tab.Dispose();
        Pump();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // A file the test left open; the temporary folder is cleaned up by the system later.
        }
    }
}
