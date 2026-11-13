using System.IO.Compression;
using System.Xml;

namespace WriteCorpus;

/// <summary>
/// Decides which corpus feature tags a DOCX file carries, from its part XML alone (XmlReader, no reflection, nothing resolved
/// or fetched, no document text kept). How each tag is decided (parts: the document, header*, footer*, footnotes, endnotes and
/// comments parts under <c>word/</c>; "body parts" means these, except where noted):
/// <list type="table">
/// <item><term>paragraphs-runs</term><description>a <c>w:r</c> in the document part.</description></item>
/// <item><term>styles</term><description>the styles part has more than one <c>w:style</c>.</description></item>
/// <item><term>numbering</term><description>a numbering part exists and a <c>w:numPr</c> is used in a body part or the styles part.</description></item>
/// <item><term>sections</term><description>two or more <c>w:sectPr</c> in the document part (a section break exists).</description></item>
/// <item><term>tables / merged-cells</term><description>a <c>w:tbl</c> / a <c>w:gridSpan</c>, <c>w:vMerge</c> or <c>w:hMerge</c> in a body part.</description></item>
/// <item><term>footnotes / endnotes</term><description>a <c>w:footnote</c> / <c>w:endnote</c> without a separator type (a real note) in its part.</description></item>
/// <item><term>fields</term><description>a <c>w:fldChar</c> or <c>w:fldSimple</c> in a body part.</description></item>
/// <item><term>toc</term><description>a field instruction (<c>w:instrText</c> or the <c>w:instr</c> of a <c>w:fldSimple</c>) that starts with <c>TOC</c>.</description></item>
/// <item><term>comments</term><description>a <c>w:commentReference</c> in a body part.</description></item>
/// <item><term>tracked-changes</term><description><c>w:ins</c>, <c>w:del</c>, <c>w:moveFrom</c>, <c>w:moveTo</c> or a <c>w:*PrChange</c> record in a body part.</description></item>
/// <item><term>pictures-inline / pictures-floating</term><description><c>wp:inline</c> / <c>wp:anchor</c> in a body part.</description></item>
/// <item><term>text-boxes</term><description><c>w:txbxContent</c> in a body part.</description></item>
/// <item><term>headers-footers</term><description>a <c>w:headerReference</c> or <c>w:footerReference</c> in the document part.</description></item>
/// <item><term>hyperlinks / bookmarks</term><description><c>w:hyperlink</c> / <c>w:bookmarkStart</c> in a body part.</description></item>
/// <item><term>content-controls</term><description><c>w:sdt</c> in a body part.</description></item>
/// <item><term>equations</term><description><c>m:oMath</c> or <c>m:oMathPara</c> in a body part.</description></item>
/// <item><term>rtl-text</term><description><c>w:rtl</c> or <c>w:bidi</c> in a body part whose <c>w:val</c> is not off (<c>0</c>, <c>false</c>, <c>off</c>).</description></item>
/// </list>
/// </summary>
internal static class FeatureDetector
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    /// <summary>The tags of the file; throws <see cref="InvalidDataException"/> or <see cref="XmlException"/> when it is not a readable package.</summary>
    public static IReadOnlySet<string> Detect(string docxPath)
    {
        ArgumentNullException.ThrowIfNull(docxPath);
        var found = new HashSet<string>(StringComparer.Ordinal);
        using var archive = ZipFile.OpenRead(docxPath);
        var scan = new Scan();
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (!name.StartsWith("word/", StringComparison.Ordinal) || !name.EndsWith(".xml", StringComparison.Ordinal) || name.IndexOf('/', 5) >= 0)
            {
                continue;
            }

            var kind = KindOf(name[5..^4]);
            if (kind == PartKind.Other)
            {
                continue;
            }

            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            scan.Read(reader, kind);
        }

        scan.Collect(found);
        return found;
    }

    private static PartKind KindOf(string name) => name switch
    {
        "document" => PartKind.Document,
        "styles" => PartKind.Styles,
        "numbering" => PartKind.Numbering,
        "footnotes" => PartKind.Footnotes,
        "endnotes" => PartKind.Endnotes,
        "comments" => PartKind.Body,
        _ when IsNumbered(name, "header") || IsNumbered(name, "footer") => PartKind.Body,
        _ => PartKind.Other,
    };

    private static bool IsNumbered(string name, string prefix) =>
        name.StartsWith(prefix, StringComparison.Ordinal) && name.AsSpan(prefix.Length).ToString().All(char.IsAsciiDigit);

    private enum PartKind
    {
        Other,
        Document,
        Body,
        Styles,
        Numbering,
        Footnotes,
        Endnotes,
    }

    private sealed class Scan
    {
        private readonly HashSet<string> _tags = new(StringComparer.Ordinal);
        private bool _hasRun;
        private int _styleCount;
        private bool _numberingPart;
        private bool _numberingUsed;
        private int _sectionProperties;

        public void Collect(HashSet<string> found)
        {
            found.UnionWith(_tags);
            if (_hasRun)
            {
                found.Add("paragraphs-runs");
            }

            if (_styleCount > 1)
            {
                found.Add("styles");
            }

            if (_numberingPart && _numberingUsed)
            {
                found.Add("numbering");
            }

            if (_sectionProperties >= 2)
            {
                found.Add("sections");
            }
        }

        public void Read(XmlReader reader, PartKind kind)
        {
            if (kind == PartKind.Numbering)
            {
                _numberingPart = true;
                return;
            }

            var more = reader.Read();
            while (more)
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    more = reader.Read();
                    continue;
                }

                var local = reader.LocalName;
                var ns = reader.NamespaceURI;
                if (kind == PartKind.Styles)
                {
                    if (ns == W && local == "style")
                    {
                        _styleCount++;
                    }
                    else if (ns == W && local == "numPr")
                    {
                        _numberingUsed = true;
                    }

                    more = reader.Read();
                    continue;
                }

                if (ns == W && local == "instrText")
                {
                    // The instruction text is read only to decide the TOC tag and is not kept.
                    if (IsToc(reader.ReadElementContentAsString()))
                    {
                        _tags.Add("toc");
                    }

                    _tags.Add("fields");

                    // ReadElementContentAsString already moved to the next node; reading again would skip it.
                    more = !reader.EOF;
                    continue;
                }

                Element(reader, ns, local, kind);
                more = reader.Read();
            }
        }

        private void Element(XmlReader reader, string ns, string local, PartKind kind)
        {
            if (ns == Wp)
            {
                Tag(local == "inline" ? "pictures-inline" : local == "anchor" ? "pictures-floating" : null);
                return;
            }

            if (ns == M)
            {
                Tag(local is "oMath" or "oMathPara" ? "equations" : null);
                return;
            }

            if (ns != W)
            {
                return;
            }

            switch (local)
            {
                case "r" when kind == PartKind.Document:
                    _hasRun = true;
                    break;
                case "sectPr" when kind == PartKind.Document:
                    _sectionProperties++;
                    break;
                case "headerReference" or "footerReference" when kind == PartKind.Document:
                    _tags.Add("headers-footers");
                    break;
                case "numPr":
                    _numberingUsed = true;
                    break;
                case "tbl":
                    _tags.Add("tables");
                    break;
                case "gridSpan" or "vMerge" or "hMerge":
                    _tags.Add("merged-cells");
                    break;
                case "footnote" or "endnote":
                    if (kind is PartKind.Footnotes or PartKind.Endnotes && IsRealNote(reader.GetAttribute("type", W)))
                    {
                        _tags.Add(kind == PartKind.Footnotes ? "footnotes" : "endnotes");
                    }

                    break;
                case "fldChar":
                    _tags.Add("fields");
                    break;
                case "fldSimple":
                    _tags.Add("fields");
                    if (IsToc(reader.GetAttribute("instr", W)))
                    {
                        _tags.Add("toc");
                    }

                    break;
                case "commentReference":
                    _tags.Add("comments");
                    break;
                case "ins" or "del" or "moveFrom" or "moveTo":
                case var _ when local.EndsWith("PrChange", StringComparison.Ordinal):
                    _tags.Add("tracked-changes");
                    break;
                case "txbxContent":
                    _tags.Add("text-boxes");
                    break;
                case "hyperlink":
                    _tags.Add("hyperlinks");
                    break;
                case "bookmarkStart":
                    _tags.Add("bookmarks");
                    break;
                case "sdt":
                    _tags.Add("content-controls");
                    break;
                case "rtl" or "bidi":
                    if (!IsOff(reader.GetAttribute("val", W)))
                    {
                        _tags.Add("rtl-text");
                    }

                    break;
                default:
                    break;
            }
        }

        private void Tag(string? tag)
        {
            if (tag is not null)
            {
                _tags.Add(tag);
            }
        }

        private static bool IsRealNote(string? type) => type is null or "normal";

        private static bool IsOff(string? value) => value is "0" or "false" or "off";

        private static bool IsToc(string? instruction)
        {
            var text = instruction?.TrimStart() ?? "";
            return text.StartsWith("TOC", StringComparison.Ordinal) && (text.Length == 3 || !char.IsLetterOrDigit(text[3]));
        }
    }
}
