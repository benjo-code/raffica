using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// Makes the paragraphs of a clipboard fragment fit to be copied or pasted. Both directions drop what must never travel on its own: a
/// section break, <c>w14:paraId</c> and <c>w14:textId</c>, bookmarks and comment range markers (a comment goes only through its own
/// command and a bookmark name is unique), comment references and tracked changes (the accepted text stays as plain runs, deleted text
/// goes). A paste also flattens what cannot be pasted as itself: a field becomes its current result text, a picture, a note reference and
/// a drawing become nothing, a content control becomes its content, and (into another document) a link with a relationship id becomes
/// its text, a style id the target does not have is dropped and the numbering reference is dropped.
/// </summary>
internal static class FragmentCleaner
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";

    /// <summary>The cleaning of a copy: what the clipboard must not carry.</summary>
    public static void ForCopy(Paragraph paragraph) => Clean(paragraph, new Options(false, true, null));

    /// <summary>The cleaning of a paste (the fragment is untrusted: the copy rules apply as well).</summary>
    public static void ForPaste(Paragraph paragraph, bool sameDocument, Func<string, bool> styleExists) =>
        Clean(paragraph, new Options(true, sameDocument, styleExists));

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

        var fields = new List<bool>();
        Walk(paragraph.Inlines, options, fields);
    }

    private static void Walk(IList<Inline> list, Options options, List<bool> fields)
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

    private static void Emit(Inline inline, List<Inline> output, Options options, List<bool> fields)
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
            case Hyperlink link when options.Paste && !options.SameDocument && link.RelationshipId is not null:
                foreach (var child in link.Inlines)
                {
                    Emit(child, output, options, fields);
                }

                break;
            case SimpleField field when options.Paste:
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
            case UnknownInline unknown when IsAnchorLike(unknown.Content.Element.Name):
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

    private static bool IsAnchorLike(XName name) =>
        name.NamespaceName == W.NamespaceName && (name.LocalName.EndsWith("RangeStart", StringComparison.Ordinal) || name.LocalName.EndsWith("RangeEnd", StringComparison.Ordinal) || name.LocalName.StartsWith("perm", StringComparison.Ordinal));

    private static void CleanRun(Run run, Options options, List<bool> fields)
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

    private static bool Keep(RunContent content, Options options, List<bool> fields)
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
            case FieldChar { Kind: FieldCharKind.Begin }:
                fields.Add(false);
                return false;
            case FieldChar { Kind: FieldCharKind.Separate }:
                if (fields.Count > 0)
                {
                    fields[^1] = true;
                }

                return false;
            case FieldChar:
                if (fields.Count > 0)
                {
                    fields.RemoveAt(fields.Count - 1);
                }

                return false;
            case InstructionText:
                return false;
        }

        if (fields.Contains(false))
        {
            return false;
        }

        return content switch
        {
            Picture or FootnoteReference or EndnoteReference => false,
            UnknownRunContent unknown => !IsObjectLike(unknown.Content.Element.Name),
            _ => true,
        };
    }

    private static bool IsObjectLike(XName name) =>
        name.LocalName is "drawing" or "pict" or "object" or "AlternateContent" or "footnoteRef" or "endnoteRef" or "footnoteReference" or "endnoteReference" or "commentReference" or "annotationRef";

    private sealed record Options(bool Paste, bool SameDocument, Func<string, bool>? StyleExists);
}
