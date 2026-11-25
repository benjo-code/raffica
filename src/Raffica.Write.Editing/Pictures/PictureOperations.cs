using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;
using static Raffica.Write.Editing.DrawingBuilder;

namespace Raffica.Write.Editing;

/// <summary>The XML edits of a picture: each one changes exactly the attributes and elements it names and leaves every other child of the drawing in place, byte for byte.</summary>
internal static class PictureOperations
{
    private const string DefaultSideDistance = "114300";

    private static readonly string[] s_wrapNames = ["wrapNone", "wrapSquare", "wrapTight", "wrapThrough", "wrapTopAndBottom"];

    private static readonly string[] s_anchorAttributes = ["distT", "distB", "distL", "distR", "simplePos", "relativeHeight", "behindDoc", "locked", "layoutInCell", "allowOverlap", "hidden"];

    private static readonly string[] s_anchorChildren = ["simplePos", "positionH", "positionV", "wrapNone", "wrapSquare", "wrapTight", "wrapThrough", "wrapTopAndBottom"];

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    // ---- the ids and heights of the document ------------------------------------------------------------------------------------------

    /// <summary>The XML of every drawing-like run content of the document (pictures and unknown run content such as alternate content), over every story the model owns.</summary>
    public static IEnumerable<XElement> DrawingRoots(WordDocument document)
    {
        foreach (var root in Roots(document.Body.Blocks, 0))
        {
            yield return root;
        }

        foreach (var part in document.HeadersFooters.Values)
        {
            foreach (var root in Roots(part.Content.Blocks, 0))
            {
                yield return root;
            }
        }

        foreach (var collection in new[] { document.Footnotes, document.Endnotes })
        {
            foreach (var note in collection?.Notes ?? [])
            {
                foreach (var root in Roots(note.Content.Blocks, 0))
                {
                    yield return root;
                }
            }
        }

        foreach (var comment in (IEnumerable<Comment>?)document.Comments ?? [])
        {
            foreach (var root in Roots(comment.Content.Blocks, 0))
            {
                yield return root;
            }
        }
    }

    private static IEnumerable<XElement> Roots(IEnumerable<Block> blocks, int depth)
    {
        if (depth > 64)
        {
            yield break;
        }

        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    foreach (var root in InlineRoots(paragraph.Inlines, 0))
                    {
                        yield return root;
                    }

                    break;
                case ContentControlBlock control:
                    foreach (var root in Roots(control.Content, depth + 1))
                    {
                        yield return root;
                    }

                    break;
                case Table table:
                    foreach (var item in table.Rows)
                    {
                        foreach (var root in RowRoots(item, depth + 1))
                        {
                            yield return root;
                        }
                    }

                    break;
                case UnknownBlock unknown:
                    yield return unknown.Content.Element;
                    break;
                default:
                    break;
            }
        }
    }

    private static IEnumerable<XElement> RowRoots(RowItem item, int depth)
    {
        if (depth > 64)
        {
            yield break;
        }

        switch (item)
        {
            case TableRow row:
                foreach (var child in row.Items)
                {
                    foreach (var root in RowRoots(child, depth + 1))
                    {
                        yield return root;
                    }
                }

                break;
            case TableCell cell:
                foreach (var root in Roots(cell.Blocks, depth + 1))
                {
                    yield return root;
                }

                break;
            case UnknownRowItem unknown:
                yield return unknown.Content.Element;
                break;
            default:
                break;
        }
    }

    private static IEnumerable<XElement> InlineRoots(IEnumerable<Inline> inlines, int depth)
    {
        if (depth > 64)
        {
            yield break;
        }

        foreach (var inline in inlines)
        {
            if (inline is Run run)
            {
                foreach (var content in run.Content)
                {
                    switch (content)
                    {
                        case Picture picture:
                            yield return picture.Raw;
                            break;
                        case UnknownRunContent unknown:
                            yield return unknown.Content.Element;
                            break;
                        default:
                            break;
                    }
                }
            }
            else if (inline is UnknownInline unknownInline)
            {
                yield return unknownInline.Content.Element;
            }
            else if (ParagraphMap.ChildrenOf(inline) is { } children)
            {
                foreach (var root in InlineRoots(children, depth + 1))
                {
                    yield return root;
                }
            }
        }
    }

    private const long UnsignedIntMaximum = uint.MaxValue;

    /// <summary>
    /// One more than the highest <c>wp:docPr</c> id of any drawing of the document (at least 1): a new drawing's id is unique. The id is an unsigned 32-bit number in the schema; ids beyond it are not counted, and when
    /// the highest valid one is already <see cref="uint.MaxValue"/> the lowest id nobody uses is taken.
    /// </summary>
    public static long NextDocPrId(WordDocument document)
    {
        var used = new HashSet<long>();
        long highest = 0;
        foreach (var root in DrawingRoots(document))
        {
            foreach (var docPr in root.DescendantsAndSelf(Wp + "docPr"))
            {
                if (long.TryParse((string?)docPr.Attribute("id"), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id <= UnsignedIntMaximum)
                {
                    used.Add(id);
                    highest = Math.Max(highest, id);
                }
            }
        }

        if (highest < UnsignedIntMaximum)
        {
            return highest + 1;
        }

        var free = 1L;
        while (used.Contains(free))
        {
            free++;
        }

        return free;
    }

    /// <summary>
    /// One more than the highest <c>relativeHeight</c> of any anchored drawing of the document: the new float is above all others. The attribute is an unsigned 32-bit number, so a document whose highest is already
    /// <see cref="uint.MaxValue"/> gets <see cref="uint.MaxValue"/> (level with it; there is nothing above).
    /// </summary>
    public static long NextRelativeHeight(WordDocument document)
    {
        long highest = 0;
        foreach (var root in DrawingRoots(document))
        {
            foreach (var anchor in root.DescendantsAndSelf(Wp + "anchor"))
            {
                if (long.TryParse((string?)anchor.Attribute("relativeHeight"), NumberStyles.None, CultureInfo.InvariantCulture, out var height) && height <= UnsignedIntMaximum && height > highest)
                {
                    highest = height;
                }
            }
        }

        return Math.Min(highest + 1, UnsignedIntMaximum);
    }


    // ---- size and alt text ------------------------------------------------------------------------------------------------------------

    /// <summary>Writes the frame size: <c>wp:extent</c> and the <c>a:ext</c> of the picture's transform, nothing else (a crop, the effect extent, the wrap and every other child stay as they are).</summary>
    public static void SetExtent(Picture picture, long width, long height)
    {
        var container = PictureView.ContainerOf(picture)!;
        var extent = container.Element(Wp + "extent")!;
        extent.SetAttributeValue("cx", Text(width));
        extent.SetAttributeValue("cy", Text(height));
        if (PictureView.PicOf(container)?.Element(Pic + "spPr")?.Element(A + "xfrm")?.Element(A + "ext") is { } ext)
        {
            ext.SetAttributeValue("cx", Text(width));
            ext.SetAttributeValue("cy", Text(height));
        }
    }

    /// <summary>Writes the alt text to <c>wp:docPr/@descr</c> (the attribute goes when the text is empty) and keeps the <c>pic:cNvPr/@descr</c> of the picture in step.</summary>
    public static void SetAltText(Picture picture, string? text)
    {
        var container = PictureView.ContainerOf(picture)!;
        var value = string.IsNullOrEmpty(text) ? null : text;
        container.Element(Wp + "docPr")?.SetAttributeValue("descr", value);
        if (PictureView.PicOf(container)?.Element(Pic + "nvPicPr")?.Element(Pic + "cNvPr") is { } cNvPr)
        {
            cNvPr.SetAttributeValue("descr", value);
        }
    }

    // ---- wrap -------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Changes how the text treats the picture. Inline to floating writes Word's defaults (an anchor at the paragraph: offset 0 from the column and the paragraph, distances 0 and 9 pt, locked off, overlap on,
    /// <c>relativeHeight</c> above every other drawing), floating to inline restores the inline form with the same size, name, alt text and graphic, and between floating wraps only the wrap element and
    /// <c>behindDoc</c> change. Children the editor does not know stay in place. Returns false when the picture already has that wrap.
    /// </summary>
    public static bool SetWrap(Picture picture, PictureWrap target, long relativeHeight)
    {
        var current = PictureView.Of(picture)!.Wrap;
        if (current == target && !(target == PictureWrap.Square && IsTightOrThrough(picture)))
        {
            return false;
        }

        var container = PictureView.ContainerOf(picture)!;
        if (target == PictureWrap.Inline)
        {
            container.ReplaceWith(ToInline(container));
        }
        else if (current == PictureWrap.Inline)
        {
            container.ReplaceWith(ToAnchor(container, target, relativeHeight));
        }
        else
        {
            var replaced = container.Elements().Where(e => e.Name.Namespace == Wp && s_wrapNames.Contains(e.Name.LocalName)).ToList();
            foreach (var old in replaced)
            {
                old.Remove();
            }

            var wrap = WrapElement(target);
            if (target == PictureWrap.Square && replaced.FirstOrDefault(e => e.Name.LocalName is "wrapTight" or "wrapThrough") is { } polygon)
            {
                // Tight and through become square with the same side and distances (the polygon goes).
                foreach (var name in new[] { "wrapText", "distT", "distB", "distL", "distR" })
                {
                    if (polygon.Attribute(name) is { } kept)
                    {
                        wrap.SetAttributeValue(name, kept.Value);
                    }
                }
            }

            var before = container.Element(Wp + "docPr");
            if (before is not null)
            {
                before.AddBeforeSelf(wrap);
            }
            else
            {
                container.Add(wrap);
            }

            container.SetAttributeValue("behindDoc", target == PictureWrap.Behind ? "1" : "0");
        }

        return true;
    }

    /// <summary>True when the picture's wrap element is `wrapTight` or `wrapThrough`, which read as Square (see <see cref="PictureView"/>) but are a different element.</summary>
    public static bool IsTightOrThrough(Picture picture) =>
        PictureView.ContainerOf(picture)?.Elements().Any(e => e.Name.Namespace == Wp && e.Name.LocalName is "wrapTight" or "wrapThrough") == true;

    private static XElement WrapElement(PictureWrap wrap) => wrap switch
    {
        PictureWrap.Square => new XElement(Wp + "wrapSquare", new XAttribute("wrapText", "bothSides")),
        PictureWrap.TopAndBottom => new XElement(Wp + "wrapTopAndBottom"),
        _ => new XElement(Wp + "wrapNone"),
    };

    private static XElement ToAnchor(XElement inline, PictureWrap target, long relativeHeight)
    {
        var anchor = new XElement(
            Wp + "anchor",
            new XAttribute("distT", "0"),
            new XAttribute("distB", "0"),
            new XAttribute("distL", DefaultSideDistance),
            new XAttribute("distR", DefaultSideDistance),
            new XAttribute("simplePos", "0"),
            new XAttribute("relativeHeight", Text(relativeHeight)),
            new XAttribute("behindDoc", target == PictureWrap.Behind ? "1" : "0"),
            new XAttribute("locked", "0"),
            new XAttribute("layoutInCell", "1"),
            new XAttribute("allowOverlap", "1"));
        foreach (var attribute in inline.Attributes().Where(a => a.IsNamespaceDeclaration || a.Name.Namespace != XNamespace.None))
        {
            anchor.Add(new XAttribute(attribute));
        }

        anchor.Add(
            new XElement(Wp + "simplePos", new XAttribute("x", "0"), new XAttribute("y", "0")),
            new XElement(Wp + "positionH", new XAttribute("relativeFrom", "column"), new XElement(Wp + "posOffset", "0")),
            new XElement(Wp + "positionV", new XAttribute("relativeFrom", "paragraph"), new XElement(Wp + "posOffset", "0")),
            new XElement(inline.Element(Wp + "extent")!),
            inline.Element(Wp + "effectExtent") is { } effect ? new XElement(effect) : new XElement(Wp + "effectExtent", new XAttribute("l", "0"), new XAttribute("t", "0"), new XAttribute("r", "0"), new XAttribute("b", "0")),
            WrapElement(target));
        AddKnownThenUnknown(inline, anchor, ["extent", "effectExtent"]);
        return anchor;
    }

    private static XElement ToInline(XElement anchor)
    {
        var inline = new XElement(Wp + "inline", new XAttribute("distT", "0"), new XAttribute("distB", "0"), new XAttribute("distL", "0"), new XAttribute("distR", "0"));
        foreach (var attribute in anchor.Attributes().Where(a => a.IsNamespaceDeclaration || a.Name.Namespace != XNamespace.None))
        {
            inline.Add(new XAttribute(attribute));
        }

        inline.Add(
            new XElement(anchor.Element(Wp + "extent")!),
            anchor.Element(Wp + "effectExtent") is { } effect ? new XElement(effect) : new XElement(Wp + "effectExtent", new XAttribute("l", "0"), new XAttribute("t", "0"), new XAttribute("r", "0"), new XAttribute("b", "0")));
        AddKnownThenUnknown(anchor, inline, [.. s_anchorChildren, "extent", "effectExtent"]);
        return inline;
    }

    // The children of the old container that stay (docPr, cNvGraphicFramePr, graphic, then the unknown ones in their order), except the names in <skip>.
    private static void AddKnownThenUnknown(XElement from, XElement to, string[] skip)
    {
        foreach (var name in new[] { "docPr", "cNvGraphicFramePr" })
        {
            if (from.Element(Wp + name) is { } known)
            {
                to.Add(new XElement(known));
            }
        }

        if (from.Element(A + "graphic") is { } graphic)
        {
            to.Add(new XElement(graphic));
        }

        foreach (var other in from.Elements())
        {
            var known = (other.Name.Namespace == Wp && (other.Name.LocalName is "docPr" or "cNvGraphicFramePr" || skip.Contains(other.Name.LocalName))) || other.Name == A + "graphic";
            if (!known)
            {
                to.Add(new XElement(other));
            }
        }
    }

    // ---- position ---------------------------------------------------------------------------------------------------------------------

    /// <summary>Writes the position offsets of a floating picture in EMU, each axis keeping what it is measured from (an alignment keyword on an axis is replaced by the offset). A null offset leaves its axis as it is.</summary>
    public static void SetOffsets(Picture picture, long? horizontal, long? vertical)
    {
        var container = PictureView.ContainerOf(picture)!;
        Offset(container.Element(Wp + "positionH"), horizontal);
        Offset(container.Element(Wp + "positionV"), vertical);
    }

    private static void Offset(XElement? position, long? value)
    {
        if (position is null || value is null)
        {
            return;
        }

        var offset = position.Element(Wp + "posOffset");
        if (offset is null)
        {
            foreach (var align in position.Elements(Wp + "align").ToList())
            {
                align.Remove();
            }

            position.Add(new XElement(Wp + "posOffset", Text(value.Value)));
        }
        else
        {
            offset.Value = Text(value.Value);
        }
    }
}
