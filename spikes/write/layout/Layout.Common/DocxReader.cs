using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Layout.Common;

/// <summary>
/// Reads a DOCX into a <see cref="DocModel"/>: the body is read forward-only (<c>OpenXmlReader</c>) so the first page does not wait for
/// the rest of the document. Styles are resolved along their basedOn chain, heading and list numbering is turned into a label in front of
/// the text, a tab becomes three no-break spaces. Only what the spikes need is understood.
/// </summary>
public static class DocxReader
{
    const string Nbsp = " ";

    public static DocModel Read(string path) => Read(path, int.MaxValue);

    /// <summary>Reads at most about <paramref name="maxBlocks"/> blocks (the open section then gets default properties).</summary>
    public static DocModel Read(string path, int maxBlocks)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var main = doc.MainDocumentPart ?? throw new InvalidDataException("no main document part");
        var ctx = new Context(main);

        var blocks = new List<Block>();
        var sections = new List<Section>();
        int sectionStart = 0;

        using var reader = OpenXmlReader.Create(main);
        while (reader.Read())
        {
            if (blocks.Count >= maxBlocks) break;
            var type = reader.ElementType;
            if (type == typeof(W.Paragraph))
            {
                var p = (W.Paragraph)reader.LoadCurrentElement()!;
                blocks.Add(ctx.ReadBodyBlock(p));
                var sect = p.ParagraphProperties?.SectionProperties;
                if (sect is not null)
                {
                    sections.Add(ctx.MakeSection(sectionStart, sect));
                    sectionStart = blocks.Count;
                }
            }
            else if (type == typeof(W.Table))
            {
                blocks.Add(ctx.ReadTable((W.Table)reader.LoadCurrentElement()!));
            }
            else if (type == typeof(W.SectionProperties))
            {
                var sect = (W.SectionProperties)reader.LoadCurrentElement()!;
                sections.Add(ctx.MakeSection(sectionStart, sect));
                sectionStart = blocks.Count;
            }
        }
        if (sectionStart < blocks.Count || sections.Count == 0)
            sections.Add(new Section(sectionStart, null, false, "", ""));

        return new DocModel(sections, blocks, ctx.ReadFootnotes(), ctx.Images);
    }

    sealed class RunProps
    {
        public string? Family; public float? Size; public bool? Bold, Italic;
        public RunProps Clone() => (RunProps)MemberwiseClone();
        public void Apply(W.RunProperties? rp)
        {
            if (rp is null) return;
            if (rp.RunFonts?.Ascii?.Value is { } f) Family = f;
            if (rp.FontSize?.Val?.Value is { } s && float.TryParse(s, out var half)) Size = half / 2f;
            if (rp.Bold is { } b) Bold = b.Val?.Value ?? true;
            if (rp.Italic is { } i) Italic = i.Val?.Value ?? true;
        }
    }

    sealed class ParaProps
    {
        public float SpaceBefore, SpaceAfter = 6, LineSpacing = 1, LeftIndent;
        public Alignment Alignment; public bool KeepNext, PageBreakBefore;
        public int? NumId, Level;
        public ParaProps Clone() => (ParaProps)MemberwiseClone();
        public void Apply(W.ParagraphProperties? pp)
        {
            if (pp is null) return;
            if (pp.SpacingBetweenLines is { } sp)
            {
                if (sp.Before?.Value is { } bf && float.TryParse(bf, out var b)) SpaceBefore = b / 20f;
                if (sp.After?.Value is { } af && float.TryParse(af, out var a)) SpaceAfter = a / 20f;
                if (sp.Line?.Value is { } ln && float.TryParse(ln, out var l) && (sp.LineRule is null || sp.LineRule.Value == W.LineSpacingRuleValues.Auto)) LineSpacing = l / 240f;
            }
            if (pp.Indentation?.Left?.Value is { } il && float.TryParse(il, out var left)) LeftIndent = left / 20f;
            if (pp.Justification?.Val?.Value == W.JustificationValues.Center) Alignment = Alignment.Center;
            if (pp.KeepNext is { } kn) KeepNext = kn.Val?.Value ?? true;
            if (pp.PageBreakBefore is { } pb) PageBreakBefore = pb.Val?.Value ?? true;
            if (pp.NumberingProperties is { } np)
            {
                if (np.NumberingId?.Val?.Value is { } id) NumId = id;
                if (np.NumberingLevelReference?.Val?.Value is { } lv) Level = lv;
            }
        }
    }

    sealed record NumLevel(string Format, string Text);

    sealed class Context
    {
        readonly MainDocumentPart _main;
        readonly Dictionary<string, W.Style> _styles = new(StringComparer.Ordinal);
        readonly W.DocDefaults? _defaults;
        readonly Dictionary<int, int> _numToAbstract = new();
        readonly Dictionary<int, Dictionary<int, NumLevel>> _abstractLevels = new();
        readonly Dictionary<int, int[]> _counters = new();
        readonly Dictionary<string, int> _imageByRid = new(StringComparer.Ordinal);
        public readonly List<byte[]> Images = new();

        public Context(MainDocumentPart main)
        {
            _main = main;
            var styles = main.StyleDefinitionsPart?.Styles;
            if (styles is not null)
            {
                _defaults = styles.DocDefaults;
                foreach (var s in styles.Elements<W.Style>()) if (s.StyleId?.Value is { } id) _styles[id] = s;
            }
            var numbering = main.NumberingDefinitionsPart?.Numbering;
            if (numbering is not null)
            {
                foreach (var a in numbering.Elements<W.AbstractNum>())
                {
                    var levels = new Dictionary<int, NumLevel>();
                    foreach (var l in a.Elements<W.Level>())
                        levels[l.LevelIndex!.Value] = new NumLevel(
                            l.NumberingFormat?.Val?.InnerText ?? "decimal", l.LevelText?.Val?.Value ?? "%1");
                    _abstractLevels[a.AbstractNumberId!.Value] = levels;
                }
                foreach (var n in numbering.Elements<W.NumberingInstance>())
                    _numToAbstract[n.NumberID!.Value] = n.AbstractNumId!.Val!.Value;
            }
        }

        IEnumerable<W.Style> Chain(string? styleId)
        {
            var list = new List<W.Style>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string? id = styleId ?? "Normal";
            while (id is not null && seen.Add(id) && _styles.TryGetValue(id, out var s))
            {
                list.Add(s);
                id = s.BasedOn?.Val?.Value;
            }
            list.Reverse();
            return list;
        }

        readonly Dictionary<string, (ParaProps, RunProps)> _resolved = new(StringComparer.Ordinal);

        (ParaProps Para, RunProps Run) Resolve(string? styleId, W.ParagraphProperties? direct)
        {
            string key = styleId ?? "";
            if (!_resolved.TryGetValue(key, out var baseProps))
            {
                var bp = new ParaProps();
                var br = new RunProps();
                bp.Apply(_defaults?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle is { } dp ? new W.ParagraphProperties(dp.OuterXml) : null);
                br.Apply(_defaults?.RunPropertiesDefault?.RunPropertiesBaseStyle is { } dr ? new W.RunProperties(dr.OuterXml) : null);
                foreach (var s in Chain(styleId))
                {
                    bp.Apply(s.StyleParagraphProperties is { } sp ? ToParagraphProperties(sp) : null);
                    br.Apply(s.StyleRunProperties is { } sr ? ToRunProperties(sr) : null);
                }
                _resolved[key] = baseProps = (bp, br);
            }
            var pp = baseProps.Item1.Clone();
            pp.Apply(direct);
            return (pp, baseProps.Item2.Clone());
        }

        // StyleParagraphProperties and StyleRunProperties hold the same members as ParagraphProperties and RunProperties.
        static W.ParagraphProperties ToParagraphProperties(W.StyleParagraphProperties sp) => new(sp.OuterXml);
        static W.RunProperties ToRunProperties(W.StyleRunProperties sr) => new(sr.OuterXml);

        public Paragraph ReadBodyParagraph(W.Paragraph p)
        {
            var direct = p.ParagraphProperties;
            string? styleId = direct?.ParagraphStyleId?.Val?.Value;
            var (pp, rp) = Resolve(styleId, direct);
            var format = MakeFormat(pp, rp, p);
            var runs = new List<TextRun>();
            var text = new StringBuilder();
            var refs = new List<FootnoteRef>();

            // Numbering label for headings and list items.
            if (pp.NumId is { } numId && numId > 0)
            {
                string label = NextLabel(numId, pp.Level ?? 0);
                if (label.Length > 0)
                    AddText(runs, text, label + Nbsp + Nbsp, rp.Family ?? "Caladea", rp.Size ?? 11, rp.Bold ?? false, rp.Italic ?? false);
            }

            foreach (var run in p.Descendants<W.Run>())
            {
                var rrp = new RunProps { Family = rp.Family, Size = rp.Size, Bold = rp.Bold, Italic = rp.Italic };
                if (run.RunProperties?.RunStyle?.Val?.Value is { } rs)
                    foreach (var s in Chain(rs)) rrp.Apply(s.StyleRunProperties is { } sr ? ToRunProperties(sr) : null);
                rrp.Apply(run.RunProperties);
                foreach (var child in run.ChildElements)
                {
                    switch (child)
                    {
                        case W.Text t:
                            AddText(runs, text, t.Text, rrp.Family ?? "Caladea", rrp.Size ?? 11, rrp.Bold ?? false, rrp.Italic ?? false);
                            break;
                        case W.TabChar:
                            AddText(runs, text, Nbsp + Nbsp + Nbsp, rrp.Family ?? "Caladea", rrp.Size ?? 11, rrp.Bold ?? false, rrp.Italic ?? false);
                            break;
                        case W.FootnoteReference fr:
                            refs.Add(new FootnoteRef(text.Length, (int)fr.Id!.Value));
                            break;
                    }
                }
            }
            return new Paragraph(text.ToString(), runs, format, refs);
        }

        /// <summary>A body paragraph or, when it holds a picture, an image block.</summary>
        public Block ReadBodyBlock(W.Paragraph p)
        {
            var drawing = p.Descendants<W.Drawing>().FirstOrDefault();
            if (drawing is null) return ReadBodyParagraph(p);
            var extent = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().First();
            var blip = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().First();
            return new ImageBlock(extent.Cx!.Value / 12700f, extent.Cy!.Value / 12700f, ImageIndex(blip.Embed!.Value!));
        }

        static ParagraphFormat MakeFormat(ParaProps pp, RunProps rp, W.Paragraph p)
        {
            bool breakAfter = p.Descendants<W.Break>().Any(b => b.Type?.Value == W.BreakValues.Page);
            return new ParagraphFormat(pp.SpaceBefore, pp.SpaceAfter, pp.LineSpacing, pp.LeftIndent, pp.Alignment,
                pp.KeepNext, pp.PageBreakBefore, breakAfter, rp.Family ?? "Caladea", rp.Size ?? 11);
        }

        static void AddText(List<TextRun> runs, StringBuilder text, string s, string family, float size, bool bold, bool italic)
        {
            if (s.Length == 0) return;
            if (runs.Count > 0 && runs[^1] is { } last && last.Family == family && last.Size == size && last.Bold == bold && last.Italic == italic)
                runs[^1] = last with { Length = last.Length + s.Length };
            else
                runs.Add(new TextRun(text.Length, s.Length, family, size, bold, italic));
            text.Append(s);
        }

        string NextLabel(int numId, int level)
        {
            if (!_numToAbstract.TryGetValue(numId, out int abs) || !_abstractLevels.TryGetValue(abs, out var levels) || !levels.TryGetValue(level, out var lvl))
                return "";
            if (lvl.Format == "bullet") return lvl.Text;
            if (!_counters.TryGetValue(numId, out var c)) _counters[numId] = c = new int[9];
            c[level]++;
            for (int i = level + 1; i < 9; i++) c[i] = 0;
            var sb = new StringBuilder(lvl.Text);
            for (int i = 8; i >= 0; i--) sb.Replace("%" + (i + 1), c[i].ToString());
            return sb.ToString();
        }

        public Table ReadTable(W.Table t)
        {
            var widths = (t.GetFirstChild<W.TableGrid>()?.Elements<W.GridColumn>() ?? Enumerable.Empty<W.GridColumn>())
                .Select(g => float.Parse(g.Width!.Value!) / 20f).ToList();
            var rows = new List<IReadOnlyList<Paragraph>>();
            int headerRows = 0;
            bool inHeader = true;
            foreach (var tr in t.Elements<W.TableRow>())
            {
                bool isHeader = tr.TableRowProperties?.Elements<W.TableHeader>().Any() == true;
                if (inHeader && isHeader) headerRows++; else inHeader = false;
                var cells = new List<Paragraph>();
                foreach (var tc in tr.Elements<W.TableCell>())
                {
                    var cp = tc.Elements<W.Paragraph>().First();
                    cells.Add(ReadBodyParagraph(cp));
                }
                rows.Add(cells);
            }
            return new Table(rows, widths, headerRows);
        }

        public Section MakeSection(int firstBlock, W.SectionProperties sect)
        {
            int? start = sect.GetFirstChild<W.PageNumberType>()?.Start?.Value is { } s ? (int)s : null;
            string header = "", prefix = "";
            bool hasFooter = false;
            if (sect.Elements<W.HeaderReference>().FirstOrDefault() is { } hr && _main.GetPartById(hr.Id!.Value!) is HeaderPart hp)
                header = TextOf(hp.Header!.Descendants<W.Run>(), stopAtField: false);
            if (sect.Elements<W.FooterReference>().FirstOrDefault() is { } fr && _main.GetPartById(fr.Id!.Value!) is FooterPart fp)
            {
                hasFooter = true;
                prefix = TextOf(fp.Footer!.Descendants<W.Run>(), stopAtField: true);
            }
            return new Section(firstBlock, start, hasFooter, prefix, header);
        }

        static string TextOf(IEnumerable<W.Run> runs, bool stopAtField)
        {
            var sb = new StringBuilder();
            foreach (var r in runs)
            {
                if (stopAtField && r.Elements<W.FieldChar>().Any()) break;
                foreach (var c in r.ChildElements)
                    if (c is W.Text t) sb.Append(t.Text);
            }
            return sb.ToString();
        }

        public IReadOnlyList<Footnote> ReadFootnotes()
        {
            var list = new List<Footnote>();
            var fn = _main.FootnotesPart?.Footnotes;
            if (fn is null) return list;
            foreach (var f in fn.Elements<W.Footnote>())
            {
                int id = (int)f.Id!.Value;
                if (id <= 0) continue;
                var p = f.Elements<W.Paragraph>().First();
                var para = ReadFootnoteParagraph(id, p);
                list.Add(new Footnote(id, para));
            }
            return list;
        }

        Paragraph ReadFootnoteParagraph(int id, W.Paragraph p)
        {
            var (pp, rp) = Resolve(p.ParagraphProperties?.ParagraphStyleId?.Val?.Value, p.ParagraphProperties);
            var format = MakeFormat(pp, rp, p);
            var runs = new List<TextRun>();
            var text = new StringBuilder();
            AddText(runs, text, id.ToString(), rp.Family ?? "Caladea", rp.Size ?? 9, false, false);
            foreach (var t in p.Descendants<W.Text>())
                AddText(runs, text, t.Text, rp.Family ?? "Caladea", rp.Size ?? 9, false, false);
            return new Paragraph(text.ToString(), runs, format, Array.Empty<FootnoteRef>());
        }

        public int ImageIndex(string rid)
        {
            if (_imageByRid.TryGetValue(rid, out int i)) return i;
            var part = (ImagePart)_main.GetPartById(rid);
            using var s = part.GetStream();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            Images.Add(ms.ToArray());
            return _imageByRid[rid] = Images.Count - 1;
        }
    }
}
