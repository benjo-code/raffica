using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// Makes the paragraphs of a clipboard fragment fit to be copied or pasted. Both directions drop what must never travel on its own: a
/// section break, <c>w14:paraId</c> and <c>w14:textId</c>, bookmarks and comment range markers (a comment goes only through its own
/// command and a bookmark name is unique), comment references and tracked changes (the accepted text stays as plain runs, deleted text
/// goes). A paste also flattens what cannot be pasted as itself: a field becomes its current result text (except a complete, supported <c>SEQ</c> or <c>STYLEREF</c> field, which stays a field: a caption keeps its number), a picture, a note reference and
/// a drawing become nothing, a content control becomes its content, and, unless the paste is into the
/// same part of the document it was copied from (relationship ids belong to a part), a link with a relationship id becomes its text and
/// unknown inline or run content with any attribute of the relationships namespace is dropped. Into another document a style id the
/// target does not have is dropped as well, and so is the numbering reference.
/// </summary>
internal static class FragmentCleaner
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";

    /// <summary>The cleaning of a copy: what the clipboard must not carry.</summary>
    public static void ForCopy(Paragraph paragraph) => Clean(paragraph, new Options(false, true, true, null));

    /// <summary>The cleaning of a paste (the fragment is untrusted: the copy rules apply as well). Returns how many pictures and drawings it left out (a picture or a drawing is never pasted).</summary>
    public static int ForPaste(Paragraph paragraph, bool sameDocument, bool sameRelationships, Func<string, bool> styleExists)
    {
        var options = new Options(true, sameDocument, sameRelationships, styleExists);
        Clean(paragraph, options);
        return options.DrawingsDropped;
    }

    private static void Clean(Paragraph paragraph, Options options)
    {
        paragraph.SectionBreak = null;
        for (var i = paragraph.UnknownAttributes.Count - 1; i >= 0; i--)
        {
            var name = paragraph.UnknownAttributes[i].Name;
            if (name.Namespace == W14 && name.LocalName is "paraId" or "textId")
            {
                paragraph.UnknownAttributes.RemoveAt(i);
            }
        }

        if (options.Paste)
        {
            paragraph.Properties.Remove(W + "pPrChange");
            if (!options.SameDocument)
            {
                paragraph.Properties.Remove(W + "numPr");
                if (paragraph.Properties.StyleId is { } style && !options.StyleExists!(style))
                {
                    paragraph.Properties.StyleId = null;
                }
            }
        }

        var fields = new List<FieldFrame>();
        options.KeptFields = options.Paste ? KeptFieldsOf(paragraph) : null;
        Walk(paragraph.Inlines, options, fields);
    }

    private static void Walk(IList<Inline> list, Options options, List<FieldFrame> fields)
    {
        var output = new List<Inline>(list.Count);
        foreach (var inline in list)
        {
            Emit(inline, output, options, fields);
        }

        list.Clear();
        foreach (var inline in output)
        {
            list.Add(inline);
        }
    }

    private static void Emit(Inline inline, List<Inline> output, Options options, List<FieldFrame> fields)
    {
        switch (inline)
        {
            case Run run:
                CleanRun(run, options, fields);
                if (run.Content.Count > 0)
                {
                    output.Add(run);
                }

                break;
            case BookmarkStart or BookmarkEnd or CommentRangeStart or CommentRangeEnd:
                break;
            case TrackedChange change:
                if (change.Kind is TrackedChangeKind.Insert or TrackedChangeKind.MoveTo)
                {
                    foreach (var child in change.Inlines)
                    {
                        Emit(child, output, options, fields);
                    }
                }

                break;
            case Hyperlink link when options.Paste && !options.SameRelationships && link.RelationshipId is not null:
                foreach (var child in link.Inlines)
                {
                    Emit(child, output, options, fields);
                }

                break;
            case SimpleField field when options.Paste && !IsKeptSimpleField(field):
                foreach (var child in field.Inlines)
                {
                    Emit(child, output, options, fields);
                }

                break;
            case ContentControlInline control when options.Paste:
                foreach (var child in control.Inlines)
                {
                    Emit(child, output, options, fields);
                }

                break;
            case UnknownInline unknown when IsAnchorLike(unknown.Content.Element.Name) || (options.Paste && !options.SameRelationships && CarriesRelationship(unknown.Content.Element)):
                break;
            default:
                if (ParagraphMap.ChildrenOf(inline) is { } children)
                {
                    Walk(children, options, fields);
                }

                output.Add(inline);
                break;
        }
    }

    // The complete fields of the paragraph a paste keeps as fields: a SEQ or STYLEREF Write supports, whole (begin, instruction, separate, result and end), at the top level of the paragraph, holding no other field and
    // outside a tracked change. The keys are the begin characters. Everything else flattens to its result text, as does a field the fragment cut off.
    private static HashSet<FieldChar>? KeptFieldsOf(Paragraph paragraph)
    {
        var infos = FieldScanner.Scan(paragraph.Inlines, intoContentControls: true);
        HashSet<FieldChar>? kept = null;
        foreach (var info in infos)
        {
            if (info.Simple is not null || info.Parent is not null || !info.IsComplete || info.InTrackedChange || info.BeginRun is null || info.SeparateRun is null || info.EndRun is null)
            {
                continue;
            }

            if (!KeepsKind(info.Instruction) || infos.Any(other => ReferenceEquals(other.Parent, info)))
            {
                continue;
            }

            if (info.BeginRun.Content.OfType<FieldChar>().FirstOrDefault(c => c.Kind == FieldCharKind.Begin) is { } begin)
            {
                (kept ??= new HashSet<FieldChar>(ReferenceEqualityComparer.Instance)).Add(begin);
            }
        }

        return kept;
    }

    private static bool IsKeptSimpleField(SimpleField field) => KeepsKind(field.Instruction);

    private static bool KeepsKind(string instruction)
    {
        var support = FieldSupport.Of(FieldInstruction.Parse(instruction));
        return support.IsSupported && support.Kind is FieldKind.Seq or FieldKind.StyleRef;
    }

    private static bool IsAnchorLike(XName name) =>
        name.NamespaceName == W.NamespaceName && (name.LocalName.EndsWith("RangeStart", StringComparison.Ordinal) || name.LocalName.EndsWith("RangeEnd", StringComparison.Ordinal) || name.LocalName.StartsWith("perm", StringComparison.Ordinal));

    private static void CleanRun(Run run, Options options, List<FieldFrame> fields)
    {
        if (options.Paste)
        {
            run.Properties.Remove(W + "rPrChange");
            if (!options.SameDocument && run.Properties.StyleId is { } style && !options.StyleExists!(style))
            {
                run.Properties.StyleId = null;
            }
        }

        var keep = new List<RunContent>(run.Content.Count);
        foreach (var content in run.Content)
        {
            if (Keep(content, options, fields))
            {
                keep.Add(content);
            }
        }

        run.Content.Clear();
        foreach (var content in keep)
        {
            run.Content.Add(content);
        }
    }

    private static bool Keep(RunContent content, Options options, List<FieldFrame> fields)
    {
        if (content is DeletedText or CommentReference)
        {
            return false;
        }

        if (!options.Paste)
        {
            return true;
        }

        switch (content)
        {
            case FieldChar { Kind: FieldCharKind.Begin } begin:
                var frame = new FieldFrame(options.KeptFields?.Contains(begin) == true);
                fields.Add(frame);
                return frame.Kept;
            case FieldChar { Kind: FieldCharKind.Separate }:
                if (fields.Count > 0)
                {
                    fields[^1].Separated = true;
                    return fields[^1].Kept;
                }

                return false;
            case FieldChar:
                if (fields.Count > 0)
                {
                    var closed = fields[^1];
                    fields.RemoveAt(fields.Count - 1);
                    return closed.Kept;
                }

                return false;
            case InstructionText:
                return fields.Count > 0 && fields[^1] is { Kept: true, Separated: false };
        }

        if (fields.Any(f => !f.Separated))
        {
            return false;
        }

        switch (content)
        {
            case Picture:
                options.DrawingsDropped++;
                return false;
            case FootnoteReference or EndnoteReference:
                return false;
            case UnknownRunContent unknown:
                if (IsDrawingLike(unknown.Content.Element.Name))
                {
                    options.DrawingsDropped++;
                    return false;
                }

                return !IsObjectLike(unknown.Content.Element.Name) && (options.SameRelationships || !CarriesRelationship(unknown.Content.Element));
            default:
                return true;
        }
    }

    // A drawing, a picture of the old kind, an embedded object or an alternate content (which holds a drawing): what the user sees as a picture or a shape.
    private static bool IsDrawingLike(XName name) => name.LocalName is "drawing" or "pict" or "object" or "AlternateContent";

    private static bool IsObjectLike(XName name) =>
        name.LocalName is "drawing" or "pict" or "object" or "AlternateContent" or "footnoteRef" or "endnoteRef" or "footnoteReference" or "endnoteReference" or "commentReference" or "annotationRef";

    // Any attribute in the relationships namespace (r:id, r:embed, r:link, r:pict, ...) on the element or below it.
    private static bool CarriesRelationship(XElement element)
    {
        foreach (var e in element.DescendantsAndSelf())
        {
            foreach (var attribute in e.Attributes())
            {
                if (attribute.Name.Namespace == Rel)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // One open complex field of a paste: whether it stays a field and whether its separate was seen.
    private sealed class FieldFrame(bool kept)
    {
        public bool Kept { get; } = kept;

        public bool Separated { get; set; }
    }

    private sealed record Options(bool Paste, bool SameDocument, bool SameRelationships, Func<string, bool>? StyleExists)
    {
        /// <summary>The pictures and drawings a paste left out (counted so that the caller can say so).</summary>
        public int DrawingsDropped { get; set; }

        /// <summary>The begin characters of the complex fields the paste keeps (see <see cref="KeptFieldsOf"/>).</summary>
        public HashSet<FieldChar>? KeptFields { get; set; }
    }
}
