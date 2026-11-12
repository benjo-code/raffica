using System.Globalization;
using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SkiaSharp;

namespace TestDoc;

/// <summary>
/// Writes the 300-page thesis the Write spikes share: cover, table of contents, 30 chapters (section 2, numbering restarts at 1
/// on physical page 3) and an appendix (section 3, "A-1"). Deterministic: seed 4242, fixed timestamps, explicit relationship ids,
/// a normalised zip. Parts are created with the Open XML SDK; the part bodies are written as XML text.
/// </summary>
public static class Generator
{
    public const int DefaultWords = 105_000;

    const string Ns =
        "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"";

    const int Chapters = 30;
    const int TextWidth = 9072; // A4 11906 - 2 * 1417 twips (2.5 cm margins)
    static readonly int[] s_imageChapters = { 5, 12, 20, 25, 28 };

    sealed class State
    {
        public readonly TextGen Gen = new();
        public readonly StringBuilder Body = new();
        public readonly List<string> Footnotes = new();
        public int Table, Image, Figure, Chapter;
    }

    public static void Generate(string path, int words)
    {
        var st = new State();
        var tmp = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (File.Exists(tmp)) File.Delete(tmp);

        var titles = new List<string>();
        BuildBody(st, words, titles);

        var images = Enumerable.Range(1, st.Image).Select(MakePng).ToList();

        using (var doc = WordprocessingDocument.Create(tmp, WordprocessingDocumentType.Document))
        {
            doc.PackageProperties.Creator = "Raffica spikes";
            doc.PackageProperties.LastModifiedBy = "Raffica spikes";
            doc.PackageProperties.Title = "Seeded thesis for the Write spikes";
            doc.PackageProperties.Created = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
            doc.PackageProperties.Modified = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

            var main = doc.AddMainDocumentPart();
            main.AddNewPart<StyleDefinitionsPart>("rIdStyles").Styles = new Styles(StylesXml());
            main.AddNewPart<NumberingDefinitionsPart>("rIdNumbering").Numbering = new Numbering(NumberingXml());
            main.AddNewPart<DocumentSettingsPart>("rIdSettings").Settings = new Settings(SettingsXml());
            main.AddNewPart<FootnotesPart>("rIdFootnotes").Footnotes = new Footnotes(FootnotesXml(st.Footnotes));
            main.AddNewPart<HeaderPart>("rIdHeader2").Header = new Header(HeaderXml("Seeded thesis"));
            main.AddNewPart<HeaderPart>("rIdHeader3").Header = new Header(HeaderXml("Appendix"));
            main.AddNewPart<FooterPart>("rIdFooter2").Footer = new Footer(FooterXml(""));
            main.AddNewPart<FooterPart>("rIdFooter3").Footer = new Footer(FooterXml("A-"));
            for (int i = 0; i < images.Count; i++)
            {
                var ip = main.AddNewPart<ImagePart>("image/png", "rIdImg" + (i + 1));
                using var ms = new MemoryStream(images[i]);
                ip.FeedData(ms);
            }
            main.Document = new Document(DocumentXml(st, titles));
        }

        Normalize(tmp, path);
        File.Delete(tmp);
    }

    // ---- body -------------------------------------------------------------------------------------------------------

    abstract record Item;
    sealed record Head(int Level, string Text, bool Numbered = true) : Item;
    sealed record Para(List<string> Sentences) : Item { public int Footnote; }
    sealed record ListItems(bool Numbered, List<string> Texts) : Item;
    sealed record TableItem(int Index) : Item;
    sealed record ImageItem : Item;

    static void BuildBody(State st, int words, List<string> titles)
    {
        var g = st.Gen;
        var b = st.Body;

        // Section 1: cover and table of contents
        var chapterTitles = Enumerable.Range(0, Chapters).Select(_ => g.Title(2 + g.Next(4))).ToList();
        titles.AddRange(chapterTitles);
        b.Append(P("Title", g.Title(5)));
        b.Append(P("Subtitle", "A seeded document for the Raffica Write spikes"));
        b.Append(P(null, "Generated author", "<w:jc w:val=\"center\"/>"));
        b.Append(P(null, "12 November 2026", "<w:jc w:val=\"center\"/>"));
        b.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
        b.Append(P("ContentsHeading", "Contents"));
        for (int i = 0; i < Chapters; i++)
        {
            string entry = $"{i + 1}\t{chapterTitles[i]}";
            string prefix = i == 0
                ? "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-1\" \\n \\h \\z \\u </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
                : "";
            b.Append("<w:p><w:pPr><w:pStyle w:val=\"TOC1\"/></w:pPr>").Append(prefix).Append(Runs(entry)).Append("</w:p>");
        }
        // End of the field and of section 1: no footer reference, no page number.
        b.Append("<w:p><w:pPr><w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1417\" w:right=\"1417\" w:bottom=\"1417\" w:left=\"1417\" w:header=\"709\" w:footer=\"709\" w:gutter=\"0\"/></w:sectPr></w:pPr><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");

        // Section 2: 30 chapters
        int chapterWords = words / (Chapters + 1);
        for (int c = 0; c < Chapters; c++)
        {
            var items = PlanChapter(st, c, chapterTitles[c], chapterWords);
            RenderItems(st, items);
        }
        b.Append("<w:p><w:pPr><w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdHeader2\"/><w:footerReference w:type=\"default\" r:id=\"rIdFooter2\"/>" +
                 "<w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1417\" w:right=\"1417\" w:bottom=\"1417\" w:left=\"1417\" w:header=\"709\" w:footer=\"709\" w:gutter=\"0\"/>" +
                 "<w:pgNumType w:start=\"1\"/></w:sectPr></w:pPr></w:p>");

        // Section 3: appendix, numbered A-1, A-2, ...
        for (int a = 0; a < 2; a++)
        {
            var items = new List<Item> { new Head(1, "Appendix " + (char)('A' + a) + ": " + g.Title(3), Numbered: false) };
            int left = chapterWords / 2;
            while (left > 0)
            {
                var p = new Para(g.Sentences(60 + g.Next(60), out int n));
                items.Add(p);
                left -= n;
            }
            RenderItems(st, items);
        }
        // Body-level sectPr (section 3)
        b.Append("<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdHeader3\"/><w:footerReference w:type=\"default\" r:id=\"rIdFooter3\"/>" +
                 "<w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1417\" w:right=\"1417\" w:bottom=\"1417\" w:left=\"1417\" w:header=\"709\" w:footer=\"709\" w:gutter=\"0\"/>" +
                 "<w:pgNumType w:fmt=\"decimal\" w:start=\"1\"/></w:sectPr>");
    }

    static List<Item> PlanChapter(State st, int c, string title, int budget)
    {
        var g = st.Gen;
        var items = new List<Item> { new Head(1, title) };
        int blockBudget = Math.Max(60, budget / 7);
        void Paras(int words)
        {
            while (words > 0)
            {
                var p = new Para(g.Sentences(70 + g.Next(50), out int n));
                items.Add(p);
                words -= n;
            }
        }
        Paras(blockBudget);                       // intro under the chapter heading
        for (int h2 = 0; h2 < 3; h2++)
        {
            items.Add(new Head(2, g.Title(2 + g.Next(3))));
            if (h2 == 0)
            {
                items.Add(new ListItems(true, Enumerable.Range(0, 4).Select(_ => g.Title(5 + g.Next(5)) + ".").ToList()));
                items.Add(new ListItems(false, Enumerable.Range(0, 4).Select(_ => g.Title(4 + g.Next(5)) + ".").ToList()));
            }
            for (int h3 = 0; h3 < 2; h3++)
            {
                items.Add(new Head(3, g.Title(2 + g.Next(3))));
                Paras(blockBudget);
                if (h2 == 1 && h3 == 0 && c < 20) items.Add(new TableItem(c));
                if (h2 == 2 && h3 == 0 && s_imageChapters.Contains(c)) items.Add(new ImageItem());
            }
        }
        // Footnotes: 3 per chapter, 4 in the first ten chapters = 100.
        int notes = c < 10 ? 4 : 3;
        var paras = items.OfType<Para>().ToList();
        var chosen = new HashSet<int>();
        while (chosen.Count < notes) chosen.Add(g.Next(paras.Count));
        foreach (int i in chosen) paras[i].Footnote = -1; // marker, numbered while rendering in document order
        return items;
    }

    static void RenderItems(State st, List<Item> items)
    {
        var b = st.Body;
        foreach (var item in items)
        {
            switch (item)
            {
                case Head h:
                    if (h.Level == 1 && h.Numbered) st.Chapter++;
                    var level = "Heading" + h.Level;
                    b.Append(P(level, h.Text, h.Numbered ? "" : "<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"0\"/></w:numPr>"));
                    break;
                case Para p:
                    RenderPara(st, p);
                    break;
                case ListItems l:
                    foreach (var t in l.Texts)
                        b.Append(P("ListParagraph", t, l.Numbered
                            ? $"<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"{100 + st.Chapter}\"/></w:numPr>"
                            : "<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"2\"/></w:numPr>"));
                    break;
                case TableItem t:
                    RenderTable(st, t.Index);
                    break;
                case ImageItem:
                    RenderImage(st);
                    break;
            }
        }
    }

    static void RenderPara(State st, Para p)
    {
        var b = st.Body;
        b.Append("<w:p>");
        int refAfter = p.Footnote == -1 ? st.Gen.Next(p.Sentences.Count) : -1;
        for (int i = 0; i < p.Sentences.Count; i++)
        {
            b.Append(Runs(p.Sentences[i] + (i < p.Sentences.Count - 1 ? " " : "")));
            if (i == refAfter)
            {
                int id = st.Footnotes.Count + 1;
                st.Footnotes.Add(st.Gen.Sentences(10, out _)[0]);
                b.Append("<w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteReference w:id=\"").Append(id).Append("\"/></w:r>");
            }
        }
        b.Append("</w:p>");
    }

    static void RenderTable(State st, int index)
    {
        var g = st.Gen;
        var b = st.Body;
        st.Table++;
        int cols = 4 + index % 2;
        int rows = index % 4 == 0 ? 45 + g.Next(16) : 6 + g.Next(9);
        int w = TextWidth / cols;
        b.Append(P("Caption", $"Table {st.Table}. {g.Title(3 + g.Next(3))}"));
        b.Append("<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"").Append(w * cols).Append("\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/></w:tblPr><w:tblGrid>");
        for (int c = 0; c < cols; c++) b.Append("<w:gridCol w:w=\"").Append(w).Append("\"/>");
        b.Append("</w:tblGrid>");
        for (int r = 0; r < rows; r++)
        {
            b.Append("<w:tr><w:trPr><w:cantSplit/>").Append(r == 0 ? "<w:tblHeader/>" : "").Append("</w:trPr>");
            for (int c = 0; c < cols; c++)
            {
                string text = r == 0 ? g.Title(1 + g.Next(2)) : g.Cell(c > 0 && (c + r) % 3 != 0);
                b.Append("<w:tc><w:tcPr><w:tcW w:w=\"").Append(w).Append("\" w:type=\"dxa\"/>")
                 .Append(r == 0 ? "<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"D9D9D9\"/>" : "").Append("</w:tcPr>")
                 .Append("<w:p><w:pPr><w:pStyle w:val=\"TableText\"/></w:pPr>")
                 .Append(r == 0 ? "<w:r><w:rPr><w:b/></w:rPr><w:t xml:space=\"preserve\">" + text + "</w:t></w:r>" : Runs(text))
                 .Append("</w:p></w:tc>");
            }
            b.Append("</w:tr>");
        }
        b.Append("</w:tbl>");
        b.Append("<w:p><w:pPr><w:spacing w:after=\"0\"/></w:pPr></w:p>");
    }

    static void RenderImage(State st)
    {
        int id = ++st.Image;
        st.Figure++;
        const int cx = 4572000, cy = 2286000;
        var b = st.Body;
        b.Append("<w:p><w:pPr><w:keepNext/><w:jc w:val=\"center\"/></w:pPr><w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">")
         .Append($"<wp:extent cx=\"{cx}\" cy=\"{cy}\"/><wp:docPr id=\"{id}\" name=\"Picture {id}\" descr=\"Generated figure {id}\"/>")
         .Append("<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>")
         .Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic>")
         .Append($"<pic:nvPicPr><pic:cNvPr id=\"{id}\" name=\"figure{id}.png\"/><pic:cNvPicPr/></pic:nvPicPr>")
         .Append($"<pic:blipFill><a:blip r:embed=\"rIdImg{id}\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>")
         .Append($"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>")
         .Append("</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>");
        b.Append(P("Caption", $"Figure {st.Figure}. {st.Gen.Title(3)}", "<w:jc w:val=\"center\"/>"));
    }

    // ---- XML helpers ------------------------------------------------------------------------------------------------

    static string P(string? style, string text, string extraPPr = "")
    {
        var sb = new StringBuilder("<w:p>");
        if (style is not null || extraPPr.Length > 0)
        {
            sb.Append("<w:pPr>");
            if (style is not null) sb.Append("<w:pStyle w:val=\"").Append(style).Append("\"/>");
            sb.Append(extraPPr).Append("</w:pPr>");
        }
        sb.Append(Runs(text)).Append("</w:p>");
        return sb.ToString();
    }

    /// <summary>Runs for a text; a tab character becomes a w:tab.</summary>
    static string Runs(string text)
    {
        var parts = text.Split('\t');
        var sb = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append("<w:r><w:tab/></w:r>");
            if (parts[i].Length > 0) sb.Append("<w:r><w:t xml:space=\"preserve\">").Append(parts[i]).Append("</w:t></w:r>");
        }
        return sb.ToString();
    }

    static string Fonts(string f) => $"<w:rFonts w:ascii=\"{f}\" w:hAnsi=\"{f}\" w:cs=\"{f}\" w:eastAsia=\"{f}\"/>";

    static string DocumentXml(State st, List<string> titles) =>
        $"<w:document {Ns}><w:body>{st.Body}</w:body></w:document>";

    static string StylesXml()
    {
        string heading(int level, string name, int sz, string spacing, string extra, string numLvl) =>
            $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{level}\"><w:name w:val=\"{name}\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/>" +
            $"<w:pPr><w:keepNext/><w:keepLines/>{extra}<w:numPr><w:ilvl w:val=\"{numLvl}\"/><w:numId w:val=\"1\"/></w:numPr>{spacing}<w:outlineLvl w:val=\"{level - 1}\"/></w:pPr>" +
            $"<w:rPr>{Fonts("Carlito")}<w:b/><w:sz w:val=\"{sz}\"/><w:szCs w:val=\"{sz}\"/></w:rPr></w:style>";
        string para(string id, string name, string ppr, string rpr = "") =>
            $"<w:style w:type=\"paragraph\" w:styleId=\"{id}\"><w:name w:val=\"{name}\"/><w:basedOn w:val=\"Normal\"/><w:qFormat/><w:pPr>{ppr}</w:pPr>" + (rpr.Length > 0 ? $"<w:rPr>{rpr}</w:rPr>" : "") + "</w:style>";
        var sb = new StringBuilder($"<w:styles {Ns}>");
        sb.Append($"<w:docDefaults><w:rPrDefault><w:rPr>{Fonts("Caladea")}<w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/><w:lang w:val=\"en-US\" w:eastAsia=\"en-US\" w:bidi=\"ar-SA\"/></w:rPr></w:rPrDefault>");
        sb.Append("<w:pPrDefault><w:pPr><w:spacing w:after=\"120\" w:line=\"360\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>");
        sb.Append("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>");
        sb.Append("<w:style w:type=\"character\" w:default=\"1\" w:styleId=\"DefaultParagraphFont\"><w:name w:val=\"Default Paragraph Font\"/><w:uiPriority w:val=\"1\"/><w:semiHidden/></w:style>");
        sb.Append("<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:name w:val=\"Normal Table\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:tblPr><w:tblInd w:w=\"0\" w:type=\"dxa\"/><w:tblCellMar><w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"108\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"108\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>");
        sb.Append("<w:style w:type=\"numbering\" w:default=\"1\" w:styleId=\"NoList\"><w:name w:val=\"No List\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/></w:style>");
        sb.Append(heading(1, "heading 1", 36, "<w:spacing w:before=\"0\" w:after=\"360\" w:line=\"240\" w:lineRule=\"auto\"/>", "<w:pageBreakBefore/>", "0"));
        sb.Append(heading(2, "heading 2", 28, "<w:spacing w:before=\"360\" w:after=\"120\" w:line=\"240\" w:lineRule=\"auto\"/>", "", "1"));
        sb.Append(heading(3, "heading 3", 24, "<w:spacing w:before=\"240\" w:after=\"120\" w:line=\"240\" w:lineRule=\"auto\"/>", "", "2"));
        sb.Append(para("Title", "Title", "<w:spacing w:before=\"2400\" w:after=\"240\" w:line=\"240\" w:lineRule=\"auto\"/><w:jc w:val=\"center\"/>", Fonts("Carlito") + "<w:b/><w:sz w:val=\"56\"/><w:szCs w:val=\"56\"/>"));
        sb.Append(para("Subtitle", "Subtitle", "<w:spacing w:after=\"1200\" w:line=\"240\" w:lineRule=\"auto\"/><w:jc w:val=\"center\"/>", Fonts("Carlito") + "<w:sz w:val=\"32\"/><w:szCs w:val=\"32\"/>"));
        sb.Append(para("ContentsHeading", "Contents Heading", "<w:keepNext/><w:spacing w:after=\"240\" w:line=\"240\" w:lineRule=\"auto\"/>", Fonts("Carlito") + "<w:b/><w:sz w:val=\"32\"/><w:szCs w:val=\"32\"/>"));
        sb.Append(para("TOC1", "toc 1", $"<w:tabs><w:tab w:val=\"left\" w:pos=\"567\"/><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"{TextWidth}\"/></w:tabs><w:spacing w:after=\"60\" w:line=\"240\" w:lineRule=\"auto\"/>"));
        sb.Append(para("ListParagraph", "List Paragraph", "<w:ind w:left=\"720\"/><w:contextualSpacing/>"));
        sb.Append(para("Header", "header", $"<w:tabs><w:tab w:val=\"right\" w:pos=\"{TextWidth}\"/></w:tabs><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>", Fonts("Carlito") + "<w:sz w:val=\"18\"/><w:szCs w:val=\"18\"/>"));
        sb.Append(para("Footer", "footer", $"<w:tabs><w:tab w:val=\"center\" w:pos=\"{TextWidth / 2}\"/></w:tabs><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/><w:jc w:val=\"center\"/>", Fonts("Carlito") + "<w:sz w:val=\"18\"/><w:szCs w:val=\"18\"/>"));
        sb.Append(para("FootnoteText", "footnote text", "<w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>", "<w:sz w:val=\"18\"/><w:szCs w:val=\"18\"/>"));
        sb.Append(para("Caption", "caption", "<w:keepNext/><w:spacing w:before=\"240\" w:after=\"120\" w:line=\"240\" w:lineRule=\"auto\"/>", Fonts("Carlito") + "<w:b/><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/>"));
        sb.Append(para("TableText", "Table Text", "<w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>", "<w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/>"));
        sb.Append("<w:style w:type=\"character\" w:styleId=\"FootnoteReference\"><w:name w:val=\"footnote reference\"/><w:basedOn w:val=\"DefaultParagraphFont\"/><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr></w:style>");
        sb.Append("<w:style w:type=\"table\" w:styleId=\"TableGrid\"><w:name w:val=\"Table Grid\"/><w:basedOn w:val=\"TableNormal\"/><w:tblPr><w:tblBorders>" +
                  string.Concat(new[] { "top", "left", "bottom", "right", "insideH", "insideV" }.Select(e => $"<w:{e} w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/>")) +
                  "</w:tblBorders></w:tblPr></w:style>");
        sb.Append("</w:styles>");
        return sb.ToString();
    }

    static string NumberingXml()
    {
        var sb = new StringBuilder($"<w:numbering {Ns}>");
        // abstractNum 0: multi-level heading numbering 1, 1.1, 1.1.1
        sb.Append("<w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"multilevel\"/>");
        for (int l = 0; l < 9; l++)
        {
            string text = string.Join(".", Enumerable.Range(1, l + 1).Select(i => "%" + i));
            string pStyle = l < 3 ? $"<w:pStyle w:val=\"Heading{l + 1}\"/>" : "";
            int ind = 432 + l * 144;
            sb.Append($"<w:lvl w:ilvl=\"{l}\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/>{pStyle}<w:lvlText w:val=\"{text}\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"{ind}\" w:hanging=\"{ind}\"/></w:pPr></w:lvl>");
        }
        sb.Append("</w:abstractNum>");
        // abstractNum 1: numbered list
        sb.Append("<w:abstractNum w:abstractNumId=\"1\"><w:multiLevelType w:val=\"hybridMultilevel\"/><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl></w:abstractNum>");
        // abstractNum 2: bullets (the bullet glyph is in Caladea)
        sb.Append($"<w:abstractNum w:abstractNumId=\"2\"><w:multiLevelType w:val=\"hybridMultilevel\"/><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"•\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr><w:rPr>{Fonts("Caladea")}</w:rPr></w:lvl></w:abstractNum>");
        sb.Append("<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>");
        sb.Append("<w:num w:numId=\"2\"><w:abstractNumId w:val=\"2\"/></w:num>");
        // One numbered-list instance per chapter (numIds 101 to 130), each restarting at 1.
        for (int c = 1; c <= Chapters; c++)
            sb.Append($"<w:num w:numId=\"{100 + c}\"><w:abstractNumId w:val=\"1\"/><w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"1\"/></w:lvlOverride></w:num>");
        sb.Append("</w:numbering>");
        return sb.ToString();
    }

    static string SettingsXml() =>
        $"<w:settings {Ns}><w:zoom w:percent=\"100\"/><w:defaultTabStop w:val=\"720\"/>" +
        "<w:footnotePr><w:footnote w:id=\"-1\"/><w:footnote w:id=\"0\"/></w:footnotePr>" +
        "<w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat></w:settings>";

    static string FootnotesXml(List<string> notes)
    {
        var sb = new StringBuilder($"<w:footnotes {Ns}>");
        sb.Append("<w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:r><w:separator/></w:r></w:p></w:footnote>");
        sb.Append("<w:footnote w:type=\"continuationSeparator\" w:id=\"0\"><w:p><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>");
        for (int i = 0; i < notes.Count; i++)
            sb.Append($"<w:footnote w:id=\"{i + 1}\"><w:p><w:pPr><w:pStyle w:val=\"FootnoteText\"/></w:pPr><w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteRef/></w:r><w:r><w:t xml:space=\"preserve\"> {notes[i]}</w:t></w:r></w:p></w:footnote>");
        sb.Append("</w:footnotes>");
        return sb.ToString();
    }

    static string HeaderXml(string text) =>
        $"<w:hdr {Ns}><w:p><w:pPr><w:pStyle w:val=\"Header\"/></w:pPr><w:r><w:tab/></w:r><w:r><w:t>{text}</w:t></w:r></w:p></w:hdr>";

    static string FooterXml(string prefix) =>
        $"<w:ftr {Ns}><w:p><w:pPr><w:pStyle w:val=\"Footer\"/></w:pPr>" +
        (prefix.Length > 0 ? $"<w:r><w:t xml:space=\"preserve\">{prefix}</w:t></w:r>" : "") +
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> PAGE </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>" +
        "</w:p></w:ftr>";

    // ---- images and the zip -------------------------------------------------------------------------------------------

    /// <summary>A small deterministic PNG (gradient, a few shapes); no dithering so it stays small.</summary>
    static byte[] MakePng(int n)
    {
        var rng = new Random(4242 + n);
        using var bmp = new SKBitmap(600, 300);
        using var canvas = new SKCanvas(bmp);
        var c1 = new SKColor((byte)rng.Next(60, 200), (byte)rng.Next(60, 200), (byte)rng.Next(60, 200));
        var c2 = new SKColor((byte)rng.Next(100, 255), (byte)rng.Next(100, 255), (byte)rng.Next(100, 255));
        using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(600, 300), new[] { c1, c2 }, SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader, IsDither = false })
            canvas.DrawRect(0, 0, 600, 300, paint);
        for (int i = 0; i < 6; i++)
        {
            using var p = new SKPaint { Color = new SKColor((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), 160), IsAntialias = true };
            canvas.DrawCircle(rng.Next(600), rng.Next(300), 20 + rng.Next(60), p);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    /// <summary>Rewrites the zip with fixed timestamps and a fixed entry order so the file is byte-identical across runs.</summary>
    static void Normalize(string source, string target)
    {
        var entries = new List<(string Name, byte[] Data)>();
        using (var zin = ZipFile.OpenRead(source))
            foreach (var e in zin.Entries)
            {
                using var s = e.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                entries.Add((e.FullName, ms.ToArray()));
            }
        // The SDK names the core properties part and the package relationships with random ids; make both stable.
        for (int i = 0; i < entries.Count; i++)
        {
            var (name, data) = entries[i];
            if (name.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal))
                entries[i] = ("package/services/metadata/core-properties/core.psmdcp", data);
        }
        for (int i = 0; i < entries.Count; i++)
        {
            var (name, data) = entries[i];
            if (name is not ("_rels/.rels" or "[Content_Types].xml")) continue;
            var text = Encoding.UTF8.GetString(data);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"core-properties/[0-9a-fA-F]{32}\.psmdcp", "core-properties/core.psmdcp");
            if (name == "_rels/.rels")
            {
                int n = 0;
                text = System.Text.RegularExpressions.Regex.Replace(text, "Id=\"[^\"]*\"", _ => $"Id=\"rIdPkg{++n}\"");
            }
            entries[i] = (name, Encoding.UTF8.GetBytes(text));
        }
        static int Rank(string n) => n == "[Content_Types].xml" ? 0 : n == "_rels/.rels" ? 1 : 2;
        entries.Sort((x, y) => Rank(x.Name) != Rank(y.Name) ? Rank(x.Name).CompareTo(Rank(y.Name)) : string.CompareOrdinal(x.Name, y.Name));
        if (File.Exists(target)) File.Delete(target);
        using var zout = ZipFile.Open(target, ZipArchiveMode.Create);
        foreach (var (name, data) in entries)
        {
            var e = zout.CreateEntry(name, CompressionLevel.SmallestSize);
            e.LastWriteTime = new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
            using var s = e.Open();
            s.Write(data);
        }
    }
}
