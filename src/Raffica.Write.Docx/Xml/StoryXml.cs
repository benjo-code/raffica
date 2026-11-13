using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Docx.Xml;

/// <summary>
/// Reads and writes the block content shared by every story. The root element decides the shape: a <c>w:document</c>
/// holds its blocks in <c>w:body</c>; any other root (header, footer) holds them directly. What the model does not
/// understand is kept in place (unknown elements as <see cref="UnknownContent"/>, unknown attributes on the element).
/// </summary>
internal static class StoryXml
{
    // Reading.

    /// <summary>Reads the root element the reader is on (or before) and everything inside it.</summary>
    public static Story Read(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        reader.MoveToContent();
        if (reader.NodeType != XmlNodeType.Element)
        {
            throw new XmlException("The part has no root element.");
        }

        var name = XNamespace.Get(reader.NamespaceURI) + reader.LocalName;
        var story = new Story(new OoxmlRoot(name, XmlKit.ReadAttributes(reader)));
        if (name == Names.Document)
        {
            ReadDocumentChildren(reader, story);
        }
        else
        {
            ReadBlocks(reader, story.Blocks);
        }

        return story;
    }

    private static void ReadDocumentChildren(XmlReader reader, Story story)
    {
        if (!XmlKit.Enter(reader))
        {
            return;
        }

        var seenBody = false;
        while (XmlKit.More(reader))
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
            }
            else if (!seenBody && XmlKit.IsWord(reader, "body"))
            {
                seenBody = true;
                XmlKit.KeepRest(story, XmlKit.ReadAttributes(reader));
                ReadBlocks(reader, story.Blocks);
            }
            else
            {
                (seenBody ? story.TrailingContent : story.LeadingContent).Add(new UnknownContent(XmlKit.ReadKept(reader)));
            }
        }

        XmlKit.Leave(reader);
    }

    /// <summary>Reads the block children of the element the reader is on (its attributes are the caller's) and moves past it. Used by every story root.</summary>
    public static void ReadBlocks(XmlReader reader, IList<Block> blocks)
    {
        if (!XmlKit.Enter(reader))
        {
            return;
        }

        while (XmlKit.More(reader))
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
            }
            else if (XmlKit.IsWord(reader, "p"))
            {
                blocks.Add(ReadParagraph(reader));
            }
            else
            {
                blocks.Add(new UnknownBlock(new UnknownContent(XmlKit.ReadKept(reader))));
            }
        }

        XmlKit.Leave(reader);
    }

    private static Paragraph ReadParagraph(XmlReader reader)
    {
        var paragraph = new Paragraph();
        XmlKit.KeepRest(paragraph, XmlKit.ReadAttributes(reader));
        if (!XmlKit.Enter(reader))
        {
            return paragraph;
        }

        while (XmlKit.More(reader))
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
            }
            else if (XmlKit.IsWord(reader, "pPr"))
            {
                ReadProperties(reader, paragraph.Properties);
            }
            else
            {
                paragraph.Inlines.Add(ReadInline(reader));
            }
        }

        XmlKit.Leave(reader);
        return paragraph;
    }

    private static void ReadProperties(XmlReader reader, PropertyBag bag)
    {
        XmlKit.KeepRest(bag, XmlKit.ReadAttributes(reader));
        if (!XmlKit.Enter(reader))
        {
            return;
        }

        while (XmlKit.More(reader))
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                bag.AddLoaded(XmlKit.ReadKept(reader));
            }
            else
            {
                reader.Read();
            }
        }

        XmlKit.Leave(reader);
    }

    private static void ReadInlines(XmlReader reader, IList<Inline> inlines)
    {
        if (!XmlKit.Enter(reader))
        {
            return;
        }

        while (XmlKit.More(reader))
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                inlines.Add(ReadInline(reader));
            }
            else
            {
                reader.Read();
            }
        }

        XmlKit.Leave(reader);
    }

    private static Inline ReadInline(XmlReader reader)
    {
        if (reader.NamespaceURI == Names.WordUri)
        {
            switch (reader.LocalName)
            {
                case "r":
                    return ReadRun(reader);
                case "hyperlink":
                    return ReadHyperlink(reader);
                case "fldSimple":
                    return ReadSimpleField(reader);
                case "ins":
                    return ReadChange(reader, TrackedChangeKind.Insert);
                case "del":
                    return ReadChange(reader, TrackedChangeKind.Delete);
                case "moveFrom":
                    return ReadChange(reader, TrackedChangeKind.MoveFrom);
                case "moveTo":
                    return ReadChange(reader, TrackedChangeKind.MoveTo);
                case "bookmarkStart":
                case "bookmarkEnd":
                case "commentRangeStart":
                case "commentRangeEnd":
                    var element = XmlKit.ReadKept(reader);
                    return ConvertMarker(element) ?? new UnknownInline(new UnknownContent(element));
            }
        }

        return new UnknownInline(new UnknownContent(XmlKit.ReadKept(reader)));
    }

    private static Inline? ConvertMarker(XElement element)
    {
        if (element.HasElements || !TryInt((string?)element.Attribute(Names.Id), out var id))
        {
            return null;
        }

        Inline result;
        XName[] typed;
        switch (element.Name.LocalName)
        {
            case "bookmarkStart":
                var name = (string?)element.Attribute(Names.Name);
                if (name is null)
                {
                    return null;
                }

                result = new BookmarkStart(id, name);
                typed = [Names.Id, Names.Name];
                break;
            case "bookmarkEnd":
                result = new BookmarkEnd(id);
                typed = [Names.Id];
                break;
            case "commentRangeStart":
                result = new CommentRangeStart(id);
                typed = [Names.Id];
                break;
            default:
                result = new CommentRangeEnd(id);
                typed = [Names.Id];
                break;
        }

        XmlKit.KeepOthers(result, element, typed);
        return result;
    }

    private static Hyperlink ReadHyperlink(XmlReader reader)
    {
        var attributes = XmlKit.ReadAttributes(reader);
        var link = new Hyperlink
        {
            RelationshipId = XmlKit.Take(attributes, Names.RelationshipId),
            Anchor = XmlKit.Take(attributes, Names.Anchor),
        };
        XmlKit.KeepRest(link, attributes);
        ReadInlines(reader, link.Inlines);
        return link;
    }

    private static Inline ReadSimpleField(XmlReader reader)
    {
        var attributes = XmlKit.ReadAttributes(reader);
        var instruction = XmlKit.Take(attributes, Names.Instr);
        if (instruction is null)
        {
            return new UnknownInline(new UnknownContent(XmlKit.ReadKept(reader)));
        }

        var field = new SimpleField(instruction);
        XmlKit.KeepRest(field, attributes);
        ReadInlines(reader, field.Inlines);
        return field;
    }

    private static Inline ReadChange(XmlReader reader, TrackedChangeKind kind)
    {
        var attributes = XmlKit.ReadAttributes(reader);
        if (!TryInt(XmlKit.Take(attributes, Names.Id), out var id))
        {
            return new UnknownInline(new UnknownContent(XmlKit.ReadKept(reader)));
        }

        var info = new RevisionInfo(id, XmlKit.Take(attributes, Names.Author), XmlKit.Take(attributes, Names.Date));
        var change = new TrackedChange(kind, info);
        XmlKit.KeepRest(change, attributes);
        ReadInlines(reader, change.Inlines);
        return change;
    }

    private static Run ReadRun(XmlReader reader)
    {
        var run = new Run();
        XmlKit.KeepRest(run, XmlKit.ReadAttributes(reader));
        if (!XmlKit.Enter(reader))
        {
            return run;
        }

        while (XmlKit.More(reader))
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
            }
            else if (XmlKit.IsWord(reader, "rPr"))
            {
                ReadProperties(reader, run.Properties);
            }
            else if (reader.NamespaceURI == Names.WordUri && reader.LocalName is "t" or "delText" or "instrText")
            {
                run.Content.Add(ReadText(reader));
            }
            else
            {
                var element = XmlKit.ReadKept(reader);
                run.Content.Add(ConvertContent(element) ?? new UnknownRunContent(new UnknownContent(element)));
            }
        }

        XmlKit.Leave(reader);
        return run;
    }

    /// <summary>Reads <c>w:t</c>, <c>w:delText</c> or <c>w:instrText</c>. The text is taken exactly; <c>xml:space</c> is not kept because the writer decides it.</summary>
    private static RunContent ReadText(XmlReader reader)
    {
        var local = reader.LocalName;
        var attributes = XmlKit.ReadAttributes(reader);
        XmlKit.Take(attributes, Names.XmlSpace);
        var text = reader.ReadElementContentAsString();
        RunContent content = local switch
        {
            "t" => new TextContent(text),
            "delText" => new DeletedText(text),
            _ => new InstructionText(text),
        };
        XmlKit.KeepRest(content, attributes);
        return content;
    }

    private static RunContent? ConvertContent(XElement element)
    {
        if (element.Name.Namespace != Names.W)
        {
            return null;
        }

        switch (element.Name.LocalName)
        {
            case "tab" when !element.HasElements:
                var tab = new TabContent();
                XmlKit.KeepOthers(tab, element);
                return tab;
            case "br" when !element.HasElements:
                var type = (string?)element.Attribute(Names.Type);
                var kind = type switch
                {
                    "page" => BreakKind.Page,
                    "column" => BreakKind.Column,
                    _ => BreakKind.TextWrapping,
                };
                var line = new BreakContent(kind, (string?)element.Attribute(Names.Clear));
                XmlKit.KeepOthers(line, element, kind == BreakKind.TextWrapping ? [Names.Clear] : [Names.Type, Names.Clear]);
                return line;
            case "fldChar":
                return ConvertFieldChar(element);
            case "footnoteReference" when !element.HasElements && TryInt((string?)element.Attribute(Names.Id), out var footnote):
                var footnoteReference = new FootnoteReference(footnote);
                XmlKit.KeepOthers(footnoteReference, element, Names.Id);
                return footnoteReference;
            case "endnoteReference" when !element.HasElements && TryInt((string?)element.Attribute(Names.Id), out var endnote):
                var endnoteReference = new EndnoteReference(endnote);
                XmlKit.KeepOthers(endnoteReference, element, Names.Id);
                return endnoteReference;
            case "commentReference" when !element.HasElements && TryInt((string?)element.Attribute(Names.Id), out var comment):
                var commentReference = new CommentReference(comment);
                XmlKit.KeepOthers(commentReference, element, Names.Id);
                return commentReference;
            case "drawing" when Picture.IsRecognized(element):
                var picture = new Picture(element);
                return picture;
            default:
                return null;
        }
    }

    private static FieldChar? ConvertFieldChar(XElement element)
    {
        var kind = (string?)element.Attribute(Names.FldCharType) switch
        {
            "begin" => (FieldCharKind?)FieldCharKind.Begin,
            "separate" => FieldCharKind.Separate,
            "end" => FieldCharKind.End,
            _ => null,
        };
        if (kind is null)
        {
            return null;
        }

        // Only the spellings the writer reproduces count as typed; "false" and "0" stay an unknown attribute.
        var dirty = (string?)element.Attribute(Names.Dirty) is "true" or "1" or "on";
        var field = new FieldChar(kind.Value, dirty);
        XmlKit.KeepOthers(field, element, dirty ? [Names.FldCharType, Names.Dirty] : [Names.FldCharType]);
        foreach (var child in element.Elements())
        {
            field.KeptChildren.Add(UnknownContent.Capture(child));
        }

        return field;
    }

    private static bool TryInt(string? text, out int value) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    // Writing.

    /// <summary>Writes the root element and everything inside it. The root re-declares exactly the namespaces it was read with.</summary>
    public static void Write(Story story, XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(writer);
        var root = story.Root;
        writer.WriteStartElement(RootPrefix(root), root.Name.LocalName, root.Name.NamespaceName);
        foreach (var attribute in root.Attributes)
        {
            XmlKit.Attribute(writer, attribute);
        }

        if (root.Name == Names.Document)
        {
            foreach (var leading in story.LeadingContent)
            {
                XmlKit.Kept(writer, leading);
            }

            XmlKit.Start(writer, Names.Body);
            XmlKit.UnknownAttributes(writer, story);
            WriteBlocks(story.Blocks, writer);
            writer.WriteEndElement();
            foreach (var trailing in story.TrailingContent)
            {
                XmlKit.Kept(writer, trailing);
            }
        }
        else
        {
            WriteBlocks(story.Blocks, writer);
        }

        writer.WriteEndElement();
    }

    private static string? RootPrefix(OoxmlRoot root)
    {
        var uri = root.Name.NamespaceName;
        foreach (var attribute in root.Attributes)
        {
            if (attribute.IsNamespaceDeclaration && attribute.Value == uri)
            {
                return attribute.Name.Namespace == XNamespace.Xmlns ? attribute.Name.LocalName : string.Empty;
            }
        }

        return null;
    }

    public static void WriteBlocks(IEnumerable<Block> blocks, XmlWriter writer)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    WriteParagraph(paragraph, writer);
                    break;
                case UnknownBlock unknown:
                    XmlKit.Kept(writer, unknown.Content);
                    break;
                default:
                    throw new NotSupportedException($"No writer for the block type {block.GetType().Name}.");
            }
        }
    }

    private static void WriteParagraph(Paragraph paragraph, XmlWriter writer)
    {
        XmlKit.Start(writer, Names.W + "p");
        XmlKit.UnknownAttributes(writer, paragraph);
        WriteProperties(Names.W + "pPr", paragraph.Properties, writer);
        WriteInlines(paragraph.Inlines, writer);
        writer.WriteEndElement();
    }

    private static void WriteProperties(XName name, PropertyBag bag, XmlWriter writer)
    {
        // An empty bag without attributes is not written: an empty properties element carries nothing.
        if (bag.Children.Count == 0 && bag.UnknownAttributes.Count == 0)
        {
            return;
        }

        XmlKit.Start(writer, name);
        XmlKit.UnknownAttributes(writer, bag);
        foreach (var child in bag.Children)
        {
            child.WriteTo(writer);
        }

        writer.WriteEndElement();
    }

    private static void WriteInlines(IEnumerable<Inline> inlines, XmlWriter writer)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    WriteRun(run, writer);
                    break;
                case Hyperlink link:
                    XmlKit.Start(writer, Names.W + "hyperlink");
                    if (link.RelationshipId is not null)
                    {
                        XmlKit.Attribute(writer, Names.RelationshipId, link.RelationshipId);
                    }

                    if (link.Anchor is not null)
                    {
                        XmlKit.Attribute(writer, Names.Anchor, link.Anchor);
                    }

                    XmlKit.UnknownAttributes(writer, link);
                    WriteInlines(link.Inlines, writer);
                    writer.WriteEndElement();
                    break;
                case SimpleField field:
                    XmlKit.Start(writer, Names.W + "fldSimple");
                    XmlKit.Attribute(writer, Names.Instr, field.Instruction);
                    XmlKit.UnknownAttributes(writer, field);
                    WriteInlines(field.Inlines, writer);
                    writer.WriteEndElement();
                    break;
                case TrackedChange change:
                    XmlKit.Start(writer, Names.W + ChangeName(change.Kind));
                    XmlKit.Attribute(writer, Names.Id, change.Info.Id.ToString(CultureInfo.InvariantCulture));
                    if (change.Info.Author is not null)
                    {
                        XmlKit.Attribute(writer, Names.Author, change.Info.Author);
                    }

                    if (change.Info.DateText is not null)
                    {
                        XmlKit.Attribute(writer, Names.Date, change.Info.DateText);
                    }

                    XmlKit.UnknownAttributes(writer, change);
                    WriteInlines(change.Inlines, writer);
                    writer.WriteEndElement();
                    break;
                case BookmarkStart start:
                    WriteMarker("bookmarkStart", start, start.Id, writer, start.Name);
                    break;
                case BookmarkEnd end:
                    WriteMarker("bookmarkEnd", end, end.Id, writer, null);
                    break;
                case CommentRangeStart rangeStart:
                    WriteMarker("commentRangeStart", rangeStart, rangeStart.Id, writer, null);
                    break;
                case CommentRangeEnd rangeEnd:
                    WriteMarker("commentRangeEnd", rangeEnd, rangeEnd.Id, writer, null);
                    break;
                case UnknownInline unknown:
                    XmlKit.Kept(writer, unknown.Content);
                    break;
                default:
                    throw new NotSupportedException($"No writer for the inline type {inline.GetType().Name}.");
            }
        }
    }

    private static string ChangeName(TrackedChangeKind kind) => kind switch
    {
        TrackedChangeKind.Insert => "ins",
        TrackedChangeKind.Delete => "del",
        TrackedChangeKind.MoveFrom => "moveFrom",
        _ => "moveTo",
    };

    private static void WriteMarker(string local, OoxmlElement element, int id, XmlWriter writer, string? name)
    {
        XmlKit.Start(writer, Names.W + local);
        XmlKit.Attribute(writer, Names.Id, id.ToString(CultureInfo.InvariantCulture));
        if (name is not null)
        {
            XmlKit.Attribute(writer, Names.Name, name);
        }

        XmlKit.UnknownAttributes(writer, element);
        writer.WriteEndElement();
    }

    private static void WriteRun(Run run, XmlWriter writer)
    {
        XmlKit.Start(writer, Names.W + "r");
        XmlKit.UnknownAttributes(writer, run);
        WriteProperties(Names.W + "rPr", run.Properties, writer);
        foreach (var content in run.Content)
        {
            switch (content)
            {
                case TextContent text:
                    WriteText("t", text.Text, text, writer);
                    break;
                case DeletedText deleted:
                    WriteText("delText", deleted.Text, deleted, writer);
                    break;
                case InstructionText instruction:
                    WriteText("instrText", instruction.Text, instruction, writer);
                    break;
                case TabContent tab:
                    XmlKit.Start(writer, Names.W + "tab");
                    XmlKit.UnknownAttributes(writer, tab);
                    writer.WriteEndElement();
                    break;
                case BreakContent line:
                    XmlKit.Start(writer, Names.W + "br");
                    if (line.Kind != BreakKind.TextWrapping)
                    {
                        XmlKit.Attribute(writer, Names.Type, line.Kind == BreakKind.Page ? "page" : "column");
                    }

                    if (line.Clear is not null)
                    {
                        XmlKit.Attribute(writer, Names.Clear, line.Clear);
                    }

                    XmlKit.UnknownAttributes(writer, line);
                    writer.WriteEndElement();
                    break;
                case FieldChar fieldChar:
                    XmlKit.Start(writer, Names.W + "fldChar");
                    XmlKit.Attribute(writer, Names.FldCharType, fieldChar.Kind switch
                    {
                        FieldCharKind.Begin => "begin",
                        FieldCharKind.Separate => "separate",
                        _ => "end",
                    });
                    if (fieldChar.Dirty)
                    {
                        XmlKit.Attribute(writer, Names.Dirty, "true");
                    }

                    XmlKit.UnknownAttributes(writer, fieldChar);
                    foreach (var child in fieldChar.KeptChildren)
                    {
                        child.WriteTo(writer);
                    }

                    writer.WriteEndElement();
                    break;
                case FootnoteReference footnote:
                    WriteReference("footnoteReference", footnote, footnote.Id, writer);
                    break;
                case EndnoteReference endnote:
                    WriteReference("endnoteReference", endnote, endnote.Id, writer);
                    break;
                case CommentReference comment:
                    WriteReference("commentReference", comment, comment.Id, writer);
                    break;
                case Picture picture:
                    picture.Raw.WriteTo(writer);
                    break;
                case UnknownRunContent unknown:
                    XmlKit.Kept(writer, unknown.Content);
                    break;
                default:
                    throw new NotSupportedException($"No writer for the run content type {content.GetType().Name}.");
            }
        }

        writer.WriteEndElement();
    }

    private static void WriteReference(string local, OoxmlElement element, int id, XmlWriter writer)
    {
        XmlKit.Start(writer, Names.W + local);
        XmlKit.Attribute(writer, Names.Id, id.ToString(CultureInfo.InvariantCulture));
        XmlKit.UnknownAttributes(writer, element);
        writer.WriteEndElement();
    }

    /// <summary><c>xml:space="preserve"</c> exactly when the text starts or ends with a space; otherwise the attribute is left out.</summary>
    private static void WriteText(string local, string text, OoxmlElement element, XmlWriter writer)
    {
        XmlKit.Start(writer, Names.W + local);
        if (text.Length > 0 && (text[0] == ' ' || text[^1] == ' '))
        {
            XmlKit.Attribute(writer, Names.XmlSpace, "preserve");
        }

        XmlKit.UnknownAttributes(writer, element);
        writer.WriteString(text);
        writer.WriteEndElement();
    }
}
