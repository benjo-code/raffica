using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace TestDoc;

/// <summary>Checks that a DOCX has the shape the Write spikes need (spec section 4). Returns the first broken rule or null.</summary>
public static class Verifier
{
    static readonly HashSet<string> s_fonts = new(StringComparer.Ordinal) { "Carlito", "Caladea" };

    public static string? Verify(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var main = doc.MainDocumentPart ?? throw new InvalidDataException("no main document part");
        var body = main.Document!.Body!;

        var sects = body.Descendants<SectionProperties>().ToList();
        if (sects.Count != 3) return $"section count: expected 3 w:sectPr, found {sects.Count}";

        // Section 1: no page number in any footer.
        foreach (var fr in sects[0].Elements<FooterReference>())
            if (FooterHasPage(main, fr)) return "section 1 has a footer with a PAGE field";
        // Section 2
        var s2 = sects[1];
        if (s2.GetFirstChild<PageNumberType>()?.Start?.Value != 1) return "section 2 has no w:pgNumType w:start=\"1\"";
        var f2 = s2.Elements<FooterReference>().FirstOrDefault();
        if (f2 is null) return "section 2 has no footerReference of its own";
        if (!FooterHasPage(main, f2)) return "section 2 footer has no PAGE field";
        // Section 3
        var s3 = sects[2];
        var pg3 = s3.GetFirstChild<PageNumberType>();
        if (pg3?.Start?.Value != 1) return "section 3 has no w:pgNumType w:start=\"1\"";
        if (pg3.Format?.Value != NumberFormatValues.Decimal) return "section 3 page number format is not decimal";
        var f3 = s3.Elements<FooterReference>().FirstOrDefault();
        if (f3 is null) return "section 3 has no footerReference of its own";
        if (f3.Id!.Value == f2.Id!.Value) return "section 3 shares the footer of section 2";
        if (!FooterHasPrefixBeforePage(main, f3, "A-")) return "section 3 footer has no text \"A-\" before the PAGE field";

        // TOC field in section 1
        var firstSectPara = sects[0].Ancestors<Paragraph>().First();
        bool toc = false;
        foreach (var p in body.Elements<Paragraph>())
        {
            if (p.Descendants<FieldCode>().Any(c => (c.Text ?? "").TrimStart().StartsWith("TOC", StringComparison.Ordinal))) toc = true;
            if (ReferenceEquals(p, firstSectPara)) break;
        }
        if (!toc) return "no TOC field in section 1";

        // Styles and numbering
        var styles = main.StyleDefinitionsPart?.Styles ?? throw new InvalidDataException("no styles part");
        var numbering = main.NumberingDefinitionsPart?.Numbering ?? throw new InvalidDataException("no numbering part");
        int? headingAbstract = null;
        foreach (var id in new[] { "Heading1", "Heading2", "Heading3" })
        {
            var st = styles.Elements<Style>().FirstOrDefault(s => s.StyleId?.Value == id);
            if (st is null) return $"style {id} missing";
            var numId = st.StyleParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value;
            if (numId is null) return $"style {id} is not linked to a numbering";
            int abs = AbstractOf(numbering, numId.Value);
            if (headingAbstract is null) headingAbstract = abs;
            else if (headingAbstract != abs) return "Heading1 to Heading3 are not linked to one multi-level numbering";
            var font = st.StyleRunProperties?.RunFonts?.Ascii?.Value;
            if (font != "Carlito") return $"style {id} font is {font ?? "(none)"}, expected Carlito";
        }
        var ab = numbering.Elements<AbstractNum>().First(a => a.AbstractNumberId!.Value == headingAbstract);
        var texts = ab.Elements<Level>().OrderBy(l => l.LevelIndex!.Value).Select(l => l.LevelText?.Val?.Value).Take(3).ToArray();
        if (!texts.SequenceEqual(new[] { "%1", "%1.%2", "%1.%2.%3" })) return "the heading numbering is not 1, 1.1, 1.1.1";

        var def = styles.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle;
        if (def?.RunFonts?.Ascii?.Value != "Caladea" || def.FontSize?.Val?.Value != "22") return "body text is not Caladea 11 pt in docDefaults";
        if (!styles.Elements<Style>().Any(s => s.StyleId?.Value == "Normal")) return "style Normal missing";

        // Lists per chapter (Heading1 of section 2) and heading count
        int heading1 = 0, chapter = 0, section = 0;
        bool numbered = false, bulleted = false;
        string? bad = null;
        foreach (var p in body.Elements<Paragraph>())
        {
            var sid = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            if (sid == "Heading1")
            {
                if (section == 1 && chapter > 0) bad ??= CheckChapter(chapter, numbered, bulleted);
                heading1++;
                if (section == 1) { chapter++; numbered = bulleted = false; }
            }
            var nid = p.ParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value;
            if (nid is not null && nid > 0 && sid is not ("Heading1" or "Heading2" or "Heading3"))
            {
                var fmt = FormatOf(numbering, nid.Value);
                if (fmt == NumberFormatValues.Bullet) bulleted = true; else numbered = true;
            }
            if (p.ParagraphProperties?.SectionProperties is not null)
            {
                if (section == 1 && chapter > 0) bad ??= CheckChapter(chapter, numbered, bulleted);
                section++;
            }
        }
        if (bad is not null) return bad;
        if (heading1 < 30) return $"fewer than 30 Heading1 paragraphs ({heading1})";

        // Tables, footnotes, images
        var tables = body.Descendants<Table>().ToList();
        if (tables.Count != 20) return $"expected 20 tables, found {tables.Count}";
        int longTables = tables.Count(t => t.Elements<TableRow>().Count() >= 40);
        if (longTables < 5) return $"fewer than 5 tables with 40 rows or more ({longTables})";
        var fn = main.FootnotesPart?.Footnotes ?? throw new InvalidDataException("no footnotes part");
        int notes = fn.Elements<Footnote>().Count(f => f.Id!.Value > 0);
        if (notes != 100) return $"expected 100 footnotes, found {notes}";
        int refs = body.Descendants<FootnoteReference>().Count();
        if (refs != 100) return $"expected 100 footnote references in the body, found {refs}";
        int blips = body.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().Count();
        if (blips < 3) return $"fewer than 3 images ({blips})";
        foreach (var img in main.ImageParts)
        {
            using var s = img.GetStream();
            if (s.Length >= 100 * 1024) return $"image {img.Uri} is {s.Length} bytes (limit 100 KB)";
        }

        // Fonts: every w:rFonts in every part of the main document
        foreach (var root in AllRoots(main))
            foreach (var rf in root.Descendants<RunFonts>())
                foreach (var f in new[] { rf.Ascii?.Value, rf.HighAnsi?.Value, rf.ComplexScript?.Value, rf.EastAsia?.Value })
                    if (f is not null && !s_fonts.Contains(f)) return $"font {f} is neither Carlito nor Caladea";
        return null;
    }

    static string? CheckChapter(int chapter, bool numbered, bool bulleted) =>
        !numbered ? $"chapter {chapter} has no numbered list" : !bulleted ? $"chapter {chapter} has no bulleted list" : null;

    static IEnumerable<OpenXmlElement> AllRoots(MainDocumentPart main)
    {
        yield return main.Document!;
        if (main.StyleDefinitionsPart is { } sp) yield return sp.Styles!;
        if (main.NumberingDefinitionsPart is { } np) yield return np.Numbering!;
        if (main.FootnotesPart is { } fp) yield return fp.Footnotes!;
        foreach (var h in main.HeaderParts) yield return h.Header!;
        foreach (var f in main.FooterParts) yield return f.Footer!;
    }

    static int AbstractOf(Numbering n, int numId) =>
        n.Elements<NumberingInstance>().First(x => x.NumberID!.Value == numId).AbstractNumId!.Val!.Value;

    static NumberFormatValues? FormatOf(Numbering n, int numId)
    {
        int abs = AbstractOf(n, numId);
        return n.Elements<AbstractNum>().First(a => a.AbstractNumberId!.Value == abs).Elements<Level>().First().NumberingFormat?.Val?.Value;
    }

    static FooterPart Part(MainDocumentPart main, FooterReference fr) => (FooterPart)main.GetPartById(fr.Id!.Value!);

    static bool FooterHasPage(MainDocumentPart main, FooterReference fr) =>
        Part(main, fr).Footer!.Descendants<FieldCode>().Any(c => (c.Text ?? "").Contains("PAGE", StringComparison.Ordinal));

    static bool FooterHasPrefixBeforePage(MainDocumentPart main, FooterReference fr, string prefix)
    {
        var footer = Part(main, fr).Footer!;
        var before = new System.Text.StringBuilder();
        foreach (var e in footer.Descendants())
        {
            if (e is FieldCode c && (c.Text ?? "").Contains("PAGE", StringComparison.Ordinal)) break;
            if (e is Text t) before.Append(t.Text);
        }
        return before.ToString().EndsWith(prefix, StringComparison.Ordinal);
    }
}
