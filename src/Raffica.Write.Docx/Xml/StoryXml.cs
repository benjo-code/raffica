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
                ReadBlockList(reader, story.Blocks, story);
            }
            else
            {
                (seenBody ? story.TrailingContent : story.LeadingContent).Add(new UnknownContent(XmlKit.ReadKept(reader)));
            }
        }

        XmlKit.Leave(reader);
    }

    /// <summary>Reads the block children of the element the reader is on (its attributes are the caller's) and moves past it. Used by every story root.</summary>
    public static void ReadBlocks(XmlReader reader, IList<Block> blocks) => ReadBlockList(reader, blocks, null);

    /// <summary>As <see cref="ReadBlocks"/>; with an owner (the body of a document) a <c>w:sectPr</c> that is the last child becomes its <see cref="Story.FinalSection"/>. One that is followed by anything else stays an <see cref="UnknownBlock"/> at its place.</summary>
    private static void ReadBlockList(XmlReader reader, IList<Block> blocks, Story? owner)
    {
        if (!XmlKit.Enter(reader))
        {
            return;
        }

        XElement? pending = null;
        while (XmlKit.More(reader))
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }

            if (pending is not null)
            {
                blocks.Add(new UnknownBlock(new UnknownContent(pending)));
                pending = null;
            }

            if (owner is not null && XmlKit.IsWord(reader, "sectPr"))
            {
                pending = XmlKit.ReadKept(reader);
            }
            else
            {
                blocks.Add(ReadBlock(reader));
            }
        }

        if (pending is not null)
        {
            owner!.FinalSection = ConvertSection(pending);
        }

        XmlKit.Leave(reader);
    }

    private static Block ReadBlock(XmlReader reader)
    {
        if (reader.NamespaceURI == Names.WordUri)
        {
            switch (reader.LocalName)
            {
                case "p":
                    return ReadParagraph(reader);
                case "tbl":
                    return ReadTable(reader);
                case "sdt":
                    var control = new ContentControlBlock();
                    XmlKit.KeepRest(control, XmlKit.ReadAttributes(reader));
                    ReadControl(reader, control.Frame, r => ReadBlocks(r, control.Content));
                    return control;
            }
        }

        return new UnknownBlock(new UnknownContent(XmlKit.ReadKept(reader)));
    }

    // Tables.

    private static Table ReadTable(XmlReader reader)
    {
        var table = new Table();
        XmlKit.KeepRest(table, XmlKit.ReadAttributes(reader));
        if (!XmlKit.Enter(reader))
        {
            return table;
        }

        var seenProperties = false;
        var seenGrid = false;
        while (XmlKit.More(reader))
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
            }
            else if (!seenProperties && XmlKit.IsWord(reader, "tblPr"))
            {
                seenProperties = true;
                table.PropertiesPosition = table.Rows.Count;
                ReadProperties(reader, table.Properties);
            }
            else if (!seenGrid && XmlKit.IsWord(reader, "tblGrid"))
            {
                seenGrid = true;
                table.GridPosition = table.Rows.Count;
                ReadGrid(reader, table);
            }
            else if (XmlKit.IsWord(reader, "tr"))
            {
                table.Rows.Add(ReadRow(reader));
            }
            else
            {
                table.Rows.Add(new UnknownRowItem(new UnknownContent(XmlKit.ReadKept(reader))));
            }
        }

        XmlKit.Leave(reader);
        return table;
    }

    private static void ReadGrid(XmlReader reader, Table table)
    {
        var element = XmlKit.ReadKept(reader);
        var widths = new List<int>();
        foreach (var column in element.Elements(Names.GridCol))
        {
            widths.Add(TryInt((string?)column.Attribute(Names.Width), out var width) ? width : 0);
        }

        table.SetKeptGrid(new UnknownContent(element), widths);
    }

    private static TableRow ReadRow(XmlReader reader)
    {
        var row = new TableRow();
        XmlKit.KeepRest(row, XmlKit.ReadAttributes(reader));
        if (!XmlKit.Enter(reader))
        {
            return row;
        }

        var seenProperties = false;
        while (XmlKit.More(reader))
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
            }
            else if (!seenProperties && XmlKit.IsWord(reader, "trPr"))
            {
                seenProperties = true;
                row.PropertiesPosition = row.Items.Count;
                ReadProperties(reader, row.Properties);
            }
            else if (XmlKit.IsWord(reader, "tc"))
            {
                row.Items.Add(ReadCell(reader));
            }
            else
            {
                row.Items.Add(new UnknownRowItem(new UnknownContent(XmlKit.ReadKept(reader))));
            }
        }

        XmlKit.Leave(reader);
        return row;
    }

    private static TableCell ReadCell(XmlReader reader)
    {
        var cell = new TableCell();
        XmlKit.KeepRest(cell, XmlKit.ReadAttributes(reader));
        if (!XmlKit.Enter(reader))
        {
            return cell;
        }

        var seenProperties = false;
        while (XmlKit.More(reader))
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
            }
            else if (!seenProperties && XmlKit.IsWord(reader, "tcPr"))
            {
                seenProperties = true;
                cell.PropertiesPosition = cell.Blocks.Count;
                ReadProperties(reader, cell.Properties);
            }
            else
            {
                cell.Blocks.Add(ReadBlock(reader));
            }
        }

        XmlKit.Leave(reader);
        return cell;
    }

    // Sections.

    private static SectionProperties ReadSection(XmlReader reader) => ConvertSection(XmlKit.ReadKept(reader));

    private static SectionProperties ConvertSection(XElement source)
    {
        var section = new SectionProperties();
        foreach (var attribute in source.Attributes())
        {
            section.UnknownAttributes.Add(new XAttribute(attribute));
        }

        foreach (var element in source.Elements())
        {
            var reference = ConvertReference(element);
            if (reference is null)
            {
                section.AddLoaded(element);
            }
            else
            {
                reference.BagPosition = section.Children.Count;
                section.HeaderFooterReferences.Add(reference);
            }
        }

        return section;
    }

    private static HeaderFooterReference? ConvertReference(XElement element)
    {
        if (element.HasElements || element.Name.Namespace != Names.W)
        {
            return null;
        }

        HeaderFooterKind kind;
        switch (element.Name.LocalName)
        {
            case "headerReference":
                kind = HeaderFooterKind.Header;
                break;
            case "footerReference":
                kind = HeaderFooterKind.Footer;
                break;
            default:
                return null;
        }

        var type = (string?)element.Attribute(Names.Type) switch
        {
            "default" => (HeaderFooterType?)HeaderFooterType.Default,
            "first" => HeaderFooterType.First,
            "even" => HeaderFooterType.Even,
            _ => null,
        };
        var id = (string?)element.Attribute(Names.RelationshipId);
        if (type is null || id is null)
        {
            return null;
        }

        var reference = new HeaderFooterReference(kind, type.Value, id);
        XmlKit.KeepOthers(reference, element, Names.Type, Names.RelationshipId);
        return reference;
    }

    // Content controls.

    /// <summary>Reads the children of a <c>w:sdt</c> the reader is on (attributes already taken): properties, end properties and content go to the frame, anything else is kept in place.</summary>
    private static void ReadControl(XmlReader reader, ContentControlFrame frame, Action<XmlReader> readContent)
    {
        frame.HasContent = false;
        var seenProperties = false;
        var seenEnd = false;
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
            else if (!seenProperties && XmlKit.IsWord(reader, "sdtPr"))
            {
                seenProperties = true;
                frame.PropertiesPosition = frame.OtherChildren.Count;
                ReadProperties(reader, frame.Properties);
            }
            else if (!seenEnd && XmlKit.IsWord(reader, "sdtEndPr"))
            {
                seenEnd = true;
                frame.EndPropertiesPosition = frame.OtherChildren.Count;
                frame.EndProperties = new UnknownContent(XmlKit.ReadKept(reader));
            }
            else if (!frame.HasContent && XmlKit.IsWord(reader, "sdtContent"))
            {
                frame.HasContent = true;
                frame.ContentPosition = frame.OtherChildren.Count;
                foreach (var attribute in XmlKit.ReadAttributes(reader))
                {
                    frame.ContentAttributes.Add(attribute);
                }

                readContent(reader);
            }
            else
            {
                frame.OtherChildren.Add(new UnknownContent(XmlKit.ReadKept(reader)));
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
                ReadParagraphProperties(reader, paragraph);
            }
            else
            {
                paragraph.Inlines.Add(ReadInline(reader));
            }
        }

        XmlKit.Leave(reader);
        return paragraph;
    }

    /// <summary>Reads <c>w:pPr</c>; its first <c>w:sectPr</c> becomes the paragraph's section break (remembering its place among the other children).</summary>
    private static void ReadParagraphProperties(XmlReader reader, Paragraph paragraph)
    {
        var bag = paragraph.Properties;
        bag.WasPresent = true;
        XmlKit.KeepRest(bag, XmlKit.ReadAttributes(reader));
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
            else if (paragraph.SectionBreak is null && XmlKit.IsWord(reader, "sectPr"))
            {
                paragraph.SectionBreak = ReadSection(reader);
            }
            else
            {
                bag.AddLoaded(XmlKit.ReadKept(reader));
            }
        }

        XmlKit.Leave(reader);
    }

    private static void ReadProperties(XmlReader reader, PropertyBag bag)
    {
        bag.WasPresent = true;
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
                case "sdt":
                    var control = new ContentControlInline();
                    XmlKit.KeepRest(control, XmlKit.ReadAttributes(reader));
                    ReadControl(reader, control.Frame, r => ReadInlines(r, control.Inlines));
                    return control;
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
        XmlKit.Attributes(writer, root.Attributes);

        if (root.Name == Names.Document)
        {
            foreach (var leading in story.LeadingContent)
            {
                XmlKit.Kept(writer, leading);
            }

            XmlKit.Start(writer, Names.Body);
            XmlKit.UnknownAttributes(writer, story);
            WriteBlocks(story.Blocks, writer);
            if (story.FinalSection is not null)
            {
                WriteSection(story.FinalSection, writer);
            }

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
                case Table table:
                    WriteTable(table, writer);
                    break;
                case ContentControlBlock control:
                    WriteControl(control, control.Frame, writer, w => WriteBlocks(control.Content, w));
                    break;
                case UnknownBlock unknown:
                    XmlKit.Kept(writer, unknown.Content);
                    break;
                default:
                    throw new NotSupportedException($"No writer for the block type {block.GetType().Name}.");
            }
        }
    }

    /// <summary>
    /// Writes the items in order and puts each fixed part (properties, grid, ...) in front of the item at its remembered
    /// position, so what the reader found between the parts stays between them. A part whose position is beyond the
    /// items is written after the last one; parts that share a position are written in the order given.
    /// </summary>
    private static void Interleave<T>(IList<T> items, Action<T> writeItem, params (int Position, Action Write)[] parts) =>
        Interleave(items.Count, i => writeItem(items[i]), parts);

    private static void Interleave<T>(IReadOnlyList<T> items, Action<T> writeItem, params (int Position, Action Write)[] parts) =>
        Interleave(items.Count, i => writeItem(items[i]), parts);

    private static void Interleave(int count, Action<int> writeAt, (int Position, Action Write)[] parts)
    {
        var written = new bool[parts.Length];
        for (var i = 0; i <= count; i++)
        {
            for (var p = 0; p < parts.Length; p++)
            {
                if (!written[p] && parts[p].Position <= i)
                {
                    written[p] = true;
                    parts[p].Write();
                }
            }

            if (i < count)
            {
                writeAt(i);
            }
        }
    }

    private static void WriteTable(Table table, XmlWriter writer)
    {
        XmlKit.Start(writer, Names.W + "tbl");
        XmlKit.UnknownAttributes(writer, table);
        Interleave(
            table.Rows,
            item => WriteRowItem(item, writer),
            (table.PropertiesPosition, () => WriteProperties(Names.W + "tblPr", table.Properties, writer)),
            (table.GridPosition, () => WriteGrid(table, writer)));
        writer.WriteEndElement();
    }

    private static void WriteGrid(Table table, XmlWriter writer)
    {
        if (table.KeptGrid is not null && table.GridIsUnedited)
        {
            XmlKit.Kept(writer, table.KeptGrid);
            return;
        }

        if (table.KeptGrid is null && table.GridColumns.Count == 0)
        {
            return;
        }

        var source = table.KeptGrid?.Element;
        XmlKit.Start(writer, Names.W + "tblGrid");
        if (source is not null)
        {
            XmlKit.Attributes(writer, source.Attributes());
        }

        // Extra attributes of a gridCol (and the original text of a width) stay with the column at the same place while the column count is unchanged; otherwise only w:w is written.
        var sourceColumns = source?.Elements(Names.GridCol).ToList();
        var sameCount = sourceColumns is not null && sourceColumns.Count == table.GridColumns.Count;
        for (var i = 0; i < table.GridColumns.Count; i++)
        {
            XmlKit.Start(writer, Names.GridCol);
            var original = sameCount ? sourceColumns![i] : null;
            var originalWidth = (string?)original?.Attribute(Names.Width);
            if (original is not null && originalWidth is null && table.KeptWidthAt(i) == table.GridColumns[i])
            {
                // The source column had no usable width and it was not edited: write none.
            }
            else
            {
                var unchanged = original is not null && table.KeptWidthAt(i) == table.GridColumns[i];
                XmlKit.Attribute(writer, Names.Width, unchanged && originalWidth is not null ? originalWidth : table.GridColumns[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (original is not null)
            {
                XmlKit.Attributes(writer, original.Attributes().Where(a => a.Name != Names.Width));
            }

            writer.WriteEndElement();
        }

        if (source is not null)
        {
            foreach (var child in source.Elements().Where(e => e.Name != Names.GridCol))
            {
                child.WriteTo(writer);
            }
        }

        writer.WriteEndElement();
    }

    private static void WriteRowItem(RowItem item, XmlWriter writer)
    {
        switch (item)
        {
            case TableRow row:
                XmlKit.Start(writer, Names.W + "tr");
                XmlKit.UnknownAttributes(writer, row);
                Interleave(
                    row.Items,
                    child => WriteRowItem(child, writer),
                    (row.PropertiesPosition, () => WriteProperties(Names.W + "trPr", row.Properties, writer)));
                writer.WriteEndElement();
                break;
            case TableCell cell:
                XmlKit.Start(writer, Names.W + "tc");
                XmlKit.UnknownAttributes(writer, cell);
                Interleave(
                    cell.Blocks,
                    block => WriteBlocks([block], writer),
                    (cell.PropertiesPosition, () => WriteProperties(Names.W + "tcPr", cell.Properties, writer)));
                writer.WriteEndElement();
                break;
            case UnknownRowItem unknown:
                XmlKit.Kept(writer, unknown.Content);
                break;
            default:
                throw new NotSupportedException($"No writer for the row item type {item.GetType().Name}.");
        }
    }

    private static void WriteSection(SectionProperties section, XmlWriter writer)
    {
        XmlKit.Start(writer, Names.W + "sectPr");
        XmlKit.UnknownAttributes(writer, section);
        var references = section.HeaderFooterReferences;
        var parts = new (int Position, Action Write)[references.Count];
        for (var i = 0; i < parts.Length; i++)
        {
            var reference = references[i];
            parts[i] = (reference.BagPosition ?? 0, () => WriteReference(reference, writer));
        }

        Interleave(section.Children, child => child.WriteTo(writer), parts);
        writer.WriteEndElement();
    }

    private static void WriteReference(HeaderFooterReference reference, XmlWriter writer)
    {
        XmlKit.Start(writer, Names.W + (reference.Kind == HeaderFooterKind.Header ? "headerReference" : "footerReference"));
        XmlKit.Attribute(writer, Names.Type, reference.Type switch
        {
            HeaderFooterType.First => "first",
            HeaderFooterType.Even => "even",
            _ => "default",
        });
        XmlKit.Attribute(writer, Names.RelationshipId, reference.RelationshipId);
        XmlKit.UnknownAttributes(writer, reference);
        writer.WriteEndElement();
    }

    private static void WriteControl(OoxmlElement element, ContentControlFrame frame, XmlWriter writer, Action<XmlWriter> writeContent)
    {
        XmlKit.Start(writer, Names.W + "sdt");
        XmlKit.UnknownAttributes(writer, element);
        var parts = new List<(int Position, Action Write)>
        {
            (frame.PropertiesPosition, () => WriteProperties(Names.W + "sdtPr", frame.Properties, writer)),
        };
        if (frame.EndProperties is not null)
        {
            parts.Add((frame.EndPropertiesPosition, () => XmlKit.Kept(writer, frame.EndProperties)));
        }

        parts.Add((frame.ContentPosition, WriteContent));
        Interleave(frame.OtherChildren, other => XmlKit.Kept(writer, other), [.. parts]);
        writer.WriteEndElement();

        void WriteContent()
        {
            if (!frame.HasContent)
            {
                return;
            }

            XmlKit.Start(writer, Names.W + "sdtContent");
            XmlKit.Attributes(writer, frame.ContentAttributes);
            writeContent(writer);
            writer.WriteEndElement();
        }
    }

    private static void WriteParagraph(Paragraph paragraph, XmlWriter writer)
    {
        XmlKit.Start(writer, Names.W + "p");
        XmlKit.UnknownAttributes(writer, paragraph);
        WriteParagraphProperties(paragraph, writer);
        WriteInlines(paragraph.Inlines, writer);
        writer.WriteEndElement();
    }

    private static void WriteParagraphProperties(Paragraph paragraph, XmlWriter writer)
    {
        var bag = paragraph.Properties;
        var section = paragraph.SectionBreak;
        if (section is null)
        {
            WriteProperties(Names.W + "pPr", bag, writer);
            return;
        }

        var position = SchemaPosition(bag);
        XmlKit.Start(writer, Names.W + "pPr");
        XmlKit.UnknownAttributes(writer, bag);
        Interleave(bag.Children, child => child.WriteTo(writer), (position, () => WriteSection(section, writer)));
        writer.WriteEndElement();
    }

    /// <summary>Where the section break goes: before the first known child the schema order puts after <c>w:sectPr</c> (<c>w:pPrChange</c>), else last. Computed on every write, so edits to the bag never leave it out of order.</summary>
    private static int SchemaPosition(PropertyBag bag)
    {
        var mine = SchemaOrder.IndexOf(PropertyKind.Paragraph, Names.W + "sectPr");
        for (var i = 0; i < bag.Children.Count; i++)
        {
            if (SchemaOrder.IndexOf(PropertyKind.Paragraph, bag.Children[i].Name) is int other && other > mine)
            {
                return i;
            }
        }

        return bag.Children.Count;
    }

    private static void WriteProperties(XName name, PropertyBag bag, XmlWriter writer)
    {
        // An empty bag without attributes is written only when the source had that empty element.
        if (bag.Children.Count == 0 && bag.UnknownAttributes.Count == 0 && !bag.WasPresent)
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
                case ContentControlInline control:
                    WriteControl(control, control.Frame, writer, w => WriteInlines(control.Inlines, w));
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
