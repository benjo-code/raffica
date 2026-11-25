using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    // A resolved picture: where it lives (the list of blocks that holds its paragraph, the index there), the live objects and what the XML says now.
    private sealed record PictureTarget(ObjectId Id, IList<Block> Container, int Index, Paragraph Paragraph, Run Run, Picture Picture, PictureInfo Info);

    private readonly record struct PictureAttempt(EditResult? Result, PictureRefusal? Refusal);

    // A move rewrites the blocks between the two paragraphs (the one step snapshots them); beyond this many the move is refused.
    private const int MaxMoveSpan = 20_000;

    /// <summary>
    /// Where the bytes of a picture of the document come from when the editor needs the original size (<see cref="ResetPictureSize"/>): the relationship id of the picture in the main part, its bytes or null. The owner of the package
    /// sets it; without one only pictures that are new (<see cref="WordDocument.Media"/>) can be reset.
    /// </summary>
    public Func<string, ReadOnlyMemory<byte>?>? PictureBytes { get; set; }

    // ---- where pictures go -------------------------------------------------------------------------------------------------------------

    /// <summary>The identity of the picture whose character starts at <paramref name="position"/> (the offset of a picture in the paragraph's text), or null when the position holds none.</summary>
    public ObjectId? PictureAt(TextPosition position)
    {
        if (position.Paragraph is null || position.Story.Kind != StoryKind.Body || StoryResolver.TryBlocks(_document, position.Story) is not { } blocks)
        {
            return null;
        }

        var projection = Map(position.Story, position.Paragraph).Projection;
        return projection.Pieces.FirstOrDefault(p => p.Start == position.Offset && p.Content is Picture) is { Run: { } run, Content: { } content }
            ? ObjectIds.Of(position.Story, blocks, position.Paragraph, run, content)
            : null;
    }

    /// <summary>The reason a picture cannot be inserted at <paramref name="caret"/> (see <see cref="PictureRules.Refusal"/>), or null: also a caret in an inline content control of the kinds text, combo box, drop-down list or date, which only the offset can tell.</summary>
    public PictureRefusal? RefusalForPicture(TextPosition caret, bool prefixDocument = false)
    {
        if (PictureRules.Refusal(_document, caret, prefixDocument) is { } refused)
        {
            return refused;
        }

        var (story, _, _) = Locate(caret);
        var map = Map(story, caret.Paragraph);
        var point = ResolvePoint(map, EditRules.Snap(map, Math.Max(0, caret.Offset)));
        return point.Run is { } run && InRestrictedInlineControl(caret.Paragraph.Inlines, run, 0) ? PictureRefusal.RestrictedControl : null;
    }

    /// <summary>The reason <see cref="InsertPicture"/> refuses, or null: the place, then the bytes (size, header, pixels), the alt text and the room in the document's picture store. Nothing changes.</summary>
    public PictureRefusal? RefusalForInsertPicture(TextPosition caret, ReadOnlyMemory<byte> bytes, string? altText = null, bool prefixDocument = false)
    {
        if (RefusalForPicture(caret, prefixDocument) is { } place)
        {
            return place;
        }

        if (PictureRules.CheckBytes(bytes.Span).Refusal is { } file)
        {
            return file;
        }

        if (PictureRules.CheckAltText(altText).Refusal is { } alt)
        {
            return alt;
        }

        return _document.Media.TotalBytes + bytes.Length > MediaStore.MaximumTotalBytes ? PictureRefusal.TooMuchMedia : null;
    }

    // ---- insert ------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Inserts a picture at the caret as one history step: the bytes go into the document's picture store (<see cref="WordDocument.Media"/>; the save writes them as a media part, and an undone insert leaves none), and an inline drawing
    /// as Word writes one is put into a run of its own at the caret, with a <c>wp:docPr</c> id above every other drawing's. The size is the picture's pixels at 96 DPI, kept in proportion to fit the text width of the section it
    /// lands in (and Word's largest side). <paramref name="altText"/> becomes the picture's description. The bytes are checked before anything changes (<see cref="PictureRules.CheckBytes"/>) and the place by
    /// <see cref="RefusalForInsertPicture"/>; null means refused and nothing changed. The result's caret is right after the picture and <see cref="EditResult.Objects"/> names the media entry. A selection is not replaced here: the caller deletes
    /// it first. A position that is not in the document is refused (<see cref="PictureRefusal.NoPosition"/>) before anything changes. The owner of the layout's picture cache clears it after an insert (see <see cref="WordDocument.Media"/> and the rule of <c>ImageCache</c>).
    /// </summary>
    public EditResult? InsertPicture(TextPosition at, ReadOnlyMemory<byte> bytes, string? altText, bool prefixDocument = false)
    {
        if (RefusalForInsertPicture(at, bytes, altText, prefixDocument) is not null)
        {
            return null;
        }

        var info = PictureRules.CheckBytes(bytes.Span).Info!.Value;
        var alt = PictureRules.CheckAltText(altText).Text;
        var (story, container, index) = Locate(at);
        var revision = History.BeginOperation();
        var paragraph = at.Paragraph;
        var map = Map(story, paragraph);
        var offset = EditRules.Snap(map, Math.Max(0, at.Offset));
        var old = paragraph.Clone();
        var (width, height) = InsertSize(info, TextWidthEmu(paragraph));
        var docPrId = PictureOperations.NextDocPrId(_document);

        MediaRef media;
        try
        {
            media = _document.Media.Add(bytes, info.ContentType);
        }
        catch (InvalidOperationException)
        {
            // The store filled up since the check (another insert on another thread): nothing was added.
            return null;
        }

        try
        {
            var run = new Run();
            run.Content.Add(new Picture(DrawingBuilder.Inline(media.RelationshipId, width, height, docPrId, alt)));
            PlaceRun(paragraph, map, offset, run);
        }
        catch
        {
            _document.Media.Remove(media);
            throw;
        }

        _document.Edits.Raise();
        var result = new EditResult(new TextPosition(story, paragraph, offset + 1), [new BlockChange(story, container, index, 1, 1, [old])], Objects: new ObjectChange([], [], [media], []));
        result.Attribute(_document);
        History.Record(result, new EditHistory.Point(index, offset), revision, null, after: new EditHistory.Point(index, offset + 1));
        return result;
    }

    /// <summary>The reason <see cref="PastePicture"/> refuses, or null: the rules of <see cref="RefusalForInsertPicture"/> at the start of the selection, a selection that runs over a section break or leaves its list of blocks. Nothing changes.</summary>
    public PictureRefusal? RefusalForPastePicture(TextRange selection, ReadOnlyMemory<byte> bytes, bool prefixDocument = false)
    {
        if (Resolve(selection) is not { } span || (!span.IsCollapsed && RemovesSectionBreak(span)))
        {
            return PictureRefusal.NoPosition;
        }

        return RefusalForInsertPicture(new TextPosition(span.Story, span.First, span.FirstOffset), bytes, null, prefixDocument);
    }

    /// <summary>
    /// Pastes a picture over the selection as ONE history step: the selected text is deleted as a paste would delete it (a footnote or endnote whose reference goes is removed with it) and the picture is inserted where it was, with no
    /// description and the size rule of <see cref="InsertPicture"/>. The result's caret is right after the picture (as after pasted text). With nothing selected this is <see cref="InsertPicture"/>. Null when refused (see
    /// <see cref="RefusalForPastePicture"/>); nothing changes then.
    /// </summary>
    public EditResult? PastePicture(TextRange selection, ReadOnlyMemory<byte> bytes, bool prefixDocument = false)
    {
        if (RefusalForPastePicture(selection, bytes, prefixDocument) is not null)
        {
            return null;
        }

        var span = Resolve(selection)!;
        if (span.IsCollapsed)
        {
            return InsertPicture(new TextPosition(span.Story, span.First, span.FirstOffset), bytes, null, prefixDocument);
        }

        var info = PictureRules.CheckBytes(bytes.Span).Info!.Value;
        var revision = History.BeginOperation();
        MediaRef media;
        try
        {
            media = _document.Media.Add(bytes, info.ContentType);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        var old = CloneBlocks(span);
        var references = ReferencesIn(span);
        Paragraph paragraph;
        int offset;
        int removed;
        ObjectChange? gone;
        try
        {
            var deleted = DeleteSpan(span);
            paragraph = deleted.Survivor;
            removed = deleted.Removed;
            var map = Map(span.Story, paragraph);
            offset = EditRules.Snap(map, deleted.Offset);
            var (width, height) = InsertSize(info, TextWidthEmu(paragraph));
            var run = new Run();
            run.Content.Add(new Picture(DrawingBuilder.Inline(media.RelationshipId, width, height, PictureOperations.NextDocPrId(_document), null)));
            PlaceRun(paragraph, map, offset, run);
            gone = RemoveNotesOf(references);
        }
        catch
        {
            _document.Media.Remove(media);
            throw;
        }

        _document.Edits.Raise();
        var objects = new ObjectChange([], gone?.NotesRemoved ?? [], [media], []);
        var result = new EditResult(new TextPosition(span.Story, paragraph, offset + 1), [new BlockChange(span.Story, span.Container, span.FirstIndex, removed, 1, old)], Objects: objects);
        result.Attribute(_document);
        History.Record(result, new EditHistory.Point(span.FirstIndex, span.FirstOffset), revision, null, after: new EditHistory.Point(span.FirstIndex, offset + 1));
        return result;
    }

    // The pixels at 96 DPI in EMU, kept in proportion within the text width and Word's largest side.
    private static (long Width, long Height) InsertSize(ImageHeaderInfo info, long maxWidth)
    {
        double width = info.Width * (double)PictureRules.EmuPerPixel;
        double height = info.Height * (double)PictureRules.EmuPerPixel;
        var scale = 1.0;
        if (maxWidth > 0 && width > maxWidth)
        {
            scale = maxWidth / width;
        }

        scale = Math.Min(scale, Math.Min(PictureRules.MaxExtentEmu / width, PictureRules.MaxExtentEmu / height));
        scale = Math.Min(scale, 1.0);
        return (Math.Max(1, (long)Math.Round(width * scale)), Math.Max(1, (long)Math.Round(height * scale)));
    }

    // The width of the text area of the section the paragraph is in, in EMU; a paragraph in a cell of a table (not a nested one) is limited to the width of the cell's content as well.
    private long TextWidthEmu(Paragraph paragraph)
    {
        var blocks = _document.Body.Blocks;
        var top = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (ReferenceEquals(blocks[i], paragraph) || (blocks[i] is not Paragraph && FindIn([blocks[i]], paragraph) is not null))
            {
                top = i;
                break;
            }
        }

        var twips = TableWidthTwips(blocks, top, paragraph, 1, false);
        if (blocks[top] is not Paragraph && CellContentTwips(blocks[top], paragraph) is { } cell)
        {
            twips = Math.Min(twips, cell);
        }

        return twips * 635L;
    }

    /// <summary>
    /// The width of the content of the table cell that holds <paramref name="paragraph"/> directly, in twips: the grid columns of the cell's span summed, minus its left and right margins (the cell's own <c>w:tcMar</c>, else
    /// the table's <c>w:tblCellMar</c>, else 108 twips each as Word's default; the margins of a table style are not read). Only a table of fixed layout (<c>w:tblLayout fixed</c>) with a regular, consistent grid whose widths are all
    /// positive is read; an autofit or percentage table sizes its columns by content, so the grid is only advice there and null (the section's text width applies) is returned, as it is for a paragraph in no cell or in a nested one.
    /// At least 20 twips.
    /// </summary>
    internal static int? CellContentTwips(Block top, Paragraph paragraph)
    {
        if (FindCell(top, paragraph, 0) is not { } found
            || found.Table.Properties.Get(W + "tblLayout") is not { } layout || (string?)layout.Attribute(W + "type") != "fixed")
        {
            return null;
        }

        var map = TableGridMap.Of(found.Table);
        if (!map.IsRegular || !map.IsConsistent || map.Cells.FirstOrDefault(c => ReferenceEquals(c.Cell, found.Cell)) is not { } place
            || place.Column < 0 || place.LastColumn >= found.Table.GridColumns.Count)
        {
            return null;
        }

        long width = 0;
        for (var c = place.Column; c <= place.LastColumn; c++)
        {
            if (found.Table.GridColumns[c] <= 0)
            {
                return null;
            }

            width += found.Table.GridColumns[c];
        }

        var margins = found.Cell.Properties.Get(W + "tcMar") ?? found.Table.Properties.Get(W + "tblCellMar");
        var content = width - MarginTwips(margins, "left", "start") - MarginTwips(margins, "right", "end");
        return (int)Math.Clamp(content, 20, int.MaxValue);
    }

    private static int MarginTwips(XElement? margins, string name, string alternative)
    {
        var side = margins?.Element(W + name) ?? margins?.Element(W + alternative);
        return side is not null && (string?)side.Attribute(W + "type") is null or "dxa" && int.TryParse((string?)side.Attribute(W + "w"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0, 31680)
            : 108;
    }

    // ---- resolving a picture -----------------------------------------------------------------------------------------------------------

    private (PictureTarget? Target, PictureRefusal? Refusal) FindPicture(ObjectId id, bool prefixDocument)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (prefixDocument)
        {
            return (null, PictureRefusal.PrefixDocument);
        }

        if (PictureRules.StoryRefusal(id.Story) is { } story)
        {
            return (null, story);
        }

        var blocks = _document.Body.Blocks;
        if (ObjectIds.Find(blocks, id) is not { } found || PictureView.Of(found.Picture) is not { } info || FindIn(blocks, found.Paragraph) is not { } place)
        {
            return (null, PictureRefusal.NoPicture);
        }

        return (new PictureTarget(id, place.Container, place.Index, found.Paragraph, found.Run, found.Picture, info), null);
    }

    private bool InCell(PictureTarget target) => TableDepthOf(_document.Body.Blocks, target.Container) > 0;

    private int OffsetOf(PictureTarget target) =>
        Map(StoryRef.Body, target.Paragraph).Projection.Pieces.FirstOrDefault(p => ReferenceEquals(p.Content, target.Picture))?.Start ?? 0;

    /// <summary>How many table cells the list of blocks <paramref name="target"/> is inside, counted from <paramref name="blocks"/> (0 for the story itself or a content control of it), or -1 when it is not reached.</summary>
    internal static int TableDepthOf(IList<Block> blocks, IList<Block> target, int depth = 0, int guard = 0)
    {
        if (ReferenceEquals(blocks, target))
        {
            return depth;
        }

        if (guard > 64)
        {
            return -1;
        }

        foreach (var block in blocks)
        {
            switch (block)
            {
                case ContentControlBlock control when TableDepthOf(control.Content, target, depth, guard + 1) is var inControl and >= 0:
                    return inControl;
                case Table table:
                    foreach (var row in table.Rows)
                    {
                        if (TableDepthIn(row, target, depth, guard + 1) is var inTable and >= 0)
                        {
                            return inTable;
                        }
                    }

                    break;
                default:
                    break;
            }
        }

        return -1;
    }

    private static int TableDepthIn(RowItem item, IList<Block> target, int depth, int guard)
    {
        switch (item)
        {
            case TableRow row:
                foreach (var child in row.Items)
                {
                    if (TableDepthIn(child, target, depth, guard + 1) is var found and >= 0)
                    {
                        return found;
                    }
                }

                return -1;
            case TableCell cell:
                return guard > 128 ? -1 : TableDepthOf(cell.Blocks, target, depth + 1, guard + 1);
            default:
                return -1;
        }
    }

    // Runs one operation on a picture's XML in place: the rule says no (a refusal, nothing changes), or the change is made on the live paragraph (a clone of it is the undo snapshot) and recorded as one step.
    private PictureAttempt Change(ObjectId id, bool prefixDocument, bool commit, Func<PictureTarget, PictureRefusal?> rule, Action<PictureTarget> apply, bool keepSelection = true, object? nudge = null)
    {
        var (target, refusal) = FindPicture(id, prefixDocument);
        if (target is null)
        {
            return new PictureAttempt(null, refusal);
        }

        if (rule(target) is { } refused)
        {
            return new PictureAttempt(null, refused);
        }

        if (!commit)
        {
            return new PictureAttempt(null, null);
        }

        var revision = History.BeginOperation();
        var offset = OffsetOf(target);
        var old = target.Paragraph.Clone();
        apply(target);
        _document.Edits.Raise();
        var result = new EditResult(new TextPosition(StoryRef.Body, target.Paragraph, offset), [new BlockChange(StoryRef.Body, target.Container, target.Index, 1, 1, [old])], KeepSelection: keepSelection);
        result.Attribute(_document);
        var point = new EditHistory.Point(target.Index, offset);
        History.Record(result, point, revision, null, after: point, nudge: nudge);
        return new PictureAttempt(result, null);
    }

    // ---- size --------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Sets the frame of a picture to <paramref name="widthEmu"/> by <paramref name="heightEmu"/> (EMU) as one history step. Only <c>wp:extent</c> and the <c>a:ext</c> of the picture's transform are written: a crop, the effect extent,
    /// the wrap and every other child stay byte for byte, so a cropped picture keeps its crop. Refused for a rotated picture, a size of zero or beyond Word's largest side (<see cref="PictureRules.MaxExtentEmu"/>) and a size it already has.
    /// </summary>
    public EditResult? ResizePicture(ObjectId id, long widthEmu, long heightEmu, bool prefixDocument = false) =>
        Change(id, prefixDocument, true, t => ResizeRule(t, widthEmu, heightEmu), t => PictureOperations.SetExtent(t.Picture, widthEmu, heightEmu)).Result;

    /// <summary>The reason <see cref="ResizePicture"/> refuses, or null. Nothing changes.</summary>
    public PictureRefusal? RefusalForResizePicture(ObjectId id, long widthEmu, long heightEmu, bool prefixDocument = false) =>
        Change(id, prefixDocument, false, t => ResizeRule(t, widthEmu, heightEmu), _ => { }).Refusal;

    private static PictureRefusal? ResizeRule(PictureTarget target, long width, long height)
    {
        if (target.Info.IsRotated)
        {
            return PictureRefusal.Rotated;
        }

        if (width < 1 || height < 1 || width > PictureRules.MaxExtentEmu || height > PictureRules.MaxExtentEmu)
        {
            return PictureRefusal.BadSize;
        }

        return target.Info.ExtentEmu == new EmuSize(width, height) ? PictureRefusal.NothingToChange : null;
    }

    /// <summary>
    /// Puts the picture back to its original size as one history step: its pixels at 96 DPI (not capped to the text width), and a cropped picture shows its crop at that scale (the frame is the visible part of the original). The
    /// pixels come from the header of the picture's bytes (<see cref="PictureBytes"/>, or the document's new pictures). Refused for a rotated picture, bytes that are not at hand or not readable
    /// (<see cref="PictureRefusal.OriginalSizeUnknown"/>), a size beyond Word's largest side and a picture that already has that size.
    /// </summary>
    public EditResult? ResetPictureSize(ObjectId id, bool prefixDocument = false)
    {
        (long Width, long Height)? size = null;
        return Change(id, prefixDocument, true, t => ResetRule(t, out size), t => PictureOperations.SetExtent(t.Picture, size!.Value.Width, size.Value.Height)).Result;
    }

    /// <summary>The reason <see cref="ResetPictureSize"/> refuses, or null. Nothing changes.</summary>
    public PictureRefusal? RefusalForResetPictureSize(ObjectId id, bool prefixDocument = false) =>
        Change(id, prefixDocument, false, t => ResetRule(t, out _), _ => { }).Refusal;

    private PictureRefusal? ResetRule(PictureTarget target, out (long Width, long Height)? size)
    {
        size = null;
        if (target.Info.IsRotated)
        {
            return PictureRefusal.Rotated;
        }

        ReadOnlyMemory<byte>? bytes = null;
        if (target.Info.Media is { } rid)
        {
            bytes = PictureBytes?.Invoke(rid) ?? (_document.HasMedia ? _document.Media.TryGet(rid) : null);
        }

        if (bytes is not { } data || ImageHeaderProbe.Read(data.Span) is not { } header)
        {
            return PictureRefusal.OriginalSizeUnknown;
        }

        var width = (double)header.Width * PictureRules.EmuPerPixel;
        var height = (double)header.Height * PictureRules.EmuPerPixel;
        if (target.Info.Crop is { } crop)
        {
            width *= (100_000.0 - crop.Left - crop.Right) / 100_000.0;
            height *= (100_000.0 - crop.Top - crop.Bottom) / 100_000.0;
        }

        var w = (long)Math.Round(width);
        var h = (long)Math.Round(height);
        if (w < 1 || h < 1 || w > PictureRules.MaxExtentEmu || h > PictureRules.MaxExtentEmu)
        {
            return PictureRefusal.BadSize;
        }

        size = (w, h);
        return target.Info.ExtentEmu == new EmuSize(w, h) ? PictureRefusal.NothingToChange : null;
    }

    // ---- alt text ----------------------------------------------------------------------------------------------------------------------

    /// <summary>Sets the description of a picture (<c>wp:docPr/@descr</c>, and <c>pic:cNvPr/@descr</c> where it has one) as one history step; an empty text removes it. At most 4096 characters; characters XML cannot hold are dropped. The text is never put into a message.</summary>
    public EditResult? SetAltText(ObjectId id, string text, bool prefixDocument = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        var (clean, _) = PictureRules.CheckAltText(text);
        return Change(id, prefixDocument, true, t => AltRule(t, text), t => PictureOperations.SetAltText(t.Picture, clean)).Result;
    }

    /// <summary>The reason <see cref="SetAltText"/> refuses, or null. Nothing changes.</summary>
    public PictureRefusal? RefusalForAltText(ObjectId id, string text, bool prefixDocument = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Change(id, prefixDocument, false, t => AltRule(t, text), _ => { }).Refusal;
    }

    private static PictureRefusal? AltRule(PictureTarget target, string text)
    {
        var (clean, refusal) = PictureRules.CheckAltText(text);
        if (refusal is not null)
        {
            return refusal;
        }

        return string.Equals(string.IsNullOrEmpty(clean) ? null : clean, string.IsNullOrEmpty(target.Info.AltText) ? null : target.Info.AltText, StringComparison.Ordinal) ? PictureRefusal.NothingToChange : null;
    }

    // ---- wrap --------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Changes how the text treats the picture as one history step (<see cref="PictureWrap"/>). Inline to floating writes Word's defaults (an anchor at the paragraph, offsets 0 from the column and the paragraph, <c>relativeHeight</c> above every other drawing),
    /// floating to inline restores the inline form, and between floating wraps only the wrap element and <c>behindDoc</c> change. Refused for a floating wrap of a picture in a table cell (<see cref="PictureRefusal.FloatInCell"/>) and for the wrap it already has.
    /// </summary>
    public EditResult? SetPictureWrap(ObjectId id, PictureWrap wrap, bool prefixDocument = false) =>
        Change(id, prefixDocument, true, t => WrapRule(t, wrap), t => PictureOperations.SetWrap(t.Picture, wrap, PictureOperations.NextRelativeHeight(_document))).Result;

    /// <summary>The reason <see cref="SetPictureWrap"/> refuses, or null. Nothing changes.</summary>
    public PictureRefusal? RefusalForPictureWrap(ObjectId id, PictureWrap wrap, bool prefixDocument = false) =>
        Change(id, prefixDocument, false, t => WrapRule(t, wrap), _ => { }).Refusal;

    private PictureRefusal? WrapRule(PictureTarget target, PictureWrap wrap)
    {
        if (!Enum.IsDefined(wrap))
        {
            return PictureRefusal.NothingToChange;
        }

        if (target.Info.Wrap == wrap && !(wrap == PictureWrap.Square && PictureOperations.IsTightOrThrough(target.Picture)))
        {
            return PictureRefusal.NothingToChange;
        }

        return wrap != PictureWrap.Inline && InCell(target) ? PictureRefusal.FloatInCell : null;
    }

    // ---- position ----------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Moves a floating picture to the start of <paramref name="anchorParagraph"/> (a floating picture is anchored to a paragraph, not to a character) and writes the offsets <paramref name="dxEmu"/> and <paramref name="dyEmu"/> as its
    /// position, each measured from what the picture's axis is measured from (an alignment keyword is replaced by the offset); the caller works the offsets out from the drag against the new paragraph. Offsets are clamped to
    /// &#177;<see cref="PictureRules.MaxOffsetEmu"/>. One history step. Refused for an inline picture (<see cref="PictureRefusal.NotFloating"/>), a picture or a target in a table cell, a target that is not in the body's own list of
    /// blocks as the picture (<see cref="PictureRefusal.AnotherPlace"/>) and a move across 20,000 blocks or more (<see cref="PictureRefusal.TooFar"/>). A stale target throws <see cref="StalePositionException"/> before anything changes.
    /// </summary>
    public EditResult? MovePicture(ObjectId id, TextPosition anchorParagraph, long dxEmu, long dyEmu, bool prefixDocument = false) =>
        Move(id, anchorParagraph, dxEmu, dyEmu, prefixDocument, commit: true).Result;

    /// <summary>The reason <see cref="MovePicture"/> refuses, or null. Nothing changes.</summary>
    public PictureRefusal? RefusalForMovePicture(ObjectId id, TextPosition anchorParagraph, bool prefixDocument = false) =>
        Move(id, anchorParagraph, 0, 0, prefixDocument, commit: false).Refusal;

    private PictureAttempt Move(ObjectId id, TextPosition anchor, long dx, long dy, bool prefixDocument, bool commit)
    {
        var (target, refusal) = FindPicture(id, prefixDocument);
        if (target is null)
        {
            return new PictureAttempt(null, refusal);
        }

        if (!target.Info.IsFloating)
        {
            return new PictureAttempt(null, PictureRefusal.NotFloating);
        }

        if (anchor.Paragraph is null)
        {
            return new PictureAttempt(null, PictureRefusal.NoPosition);
        }

        if (PictureRules.StoryRefusal(anchor.Story) is { } story)
        {
            return new PictureAttempt(null, story);
        }

        var (_, targetContainer, targetIndex) = Locate(anchor);
        if (InCell(target) || TableDepthOf(_document.Body.Blocks, targetContainer) > 0)
        {
            return new PictureAttempt(null, PictureRefusal.FloatInCell);
        }

        if (!ReferenceEquals(targetContainer, target.Container))
        {
            return new PictureAttempt(null, PictureRefusal.AnotherPlace);
        }

        if (Math.Abs(targetIndex - target.Index) >= MaxMoveSpan)
        {
            return new PictureAttempt(null, PictureRefusal.TooFar);
        }

        if (InSingleParagraphControl(_document.Body.Blocks, targetContainer))
        {
            return new PictureAttempt(null, PictureRefusal.RestrictedControl);
        }

        if (!commit)
        {
            return new PictureAttempt(null, null);
        }

        var container = target.Container;
        var first = Math.Min(target.Index, targetIndex);
        var last = Math.Max(target.Index, targetIndex);
        var revision = History.BeginOperation();
        var offset = OffsetOf(target);
        var old = new List<Block>(last - first + 1);
        for (var i = first; i <= last; i++)
        {
            old.Add(container[i].Clone());
        }

        var targetParagraph = anchor.Paragraph;
        RemovePicture(target);
        var run = new Run();
        run.Content.Add(target.Picture);
        targetParagraph.Inlines.Insert(0, run);
        PictureOperations.SetOffsets(target.Picture, Math.Clamp(dx, -PictureRules.MaxOffsetEmu, PictureRules.MaxOffsetEmu), Math.Clamp(dy, -PictureRules.MaxOffsetEmu, PictureRules.MaxOffsetEmu));
        _document.Edits.Raise();
        var result = new EditResult(new TextPosition(StoryRef.Body, targetParagraph, 0), [new BlockChange(StoryRef.Body, container, first, last - first + 1, last - first + 1, old)]);
        result.Attribute(_document);
        History.Record(result, new EditHistory.Point(target.Index, offset), revision, null, after: new EditHistory.Point(targetIndex, 0));
        return new PictureAttempt(result, null);
    }

    /// <summary>
    /// Moves a floating picture by <paramref name="dxPt"/> and <paramref name="dyPt"/> points (its offsets change by that many times 12,700 EMU, clamped to &#177;<see cref="PictureRules.MaxOffsetEmu"/>). Consecutive nudges of
    /// one picture less than a second apart are one history step. Refused for an inline picture (<see cref="PictureRefusal.NotFloating"/>), a picture in a table cell, a picture placed by an alignment keyword on a moved axis
    /// (<see cref="PictureRefusal.AlignedPosition"/>) and a nudge of nothing.
    /// </summary>
    public EditResult? NudgePicture(ObjectId id, int dxPt, int dyPt, bool prefixDocument = false) =>
        Change(id, prefixDocument, true, t => NudgeRule(t, dxPt, dyPt), t => Nudge(t, dxPt, dyPt), nudge: id).Result;

    /// <summary>The reason <see cref="NudgePicture"/> refuses, or null. Nothing changes.</summary>
    public PictureRefusal? RefusalForNudgePicture(ObjectId id, int dxPt, int dyPt, bool prefixDocument = false) =>
        Change(id, prefixDocument, false, t => NudgeRule(t, dxPt, dyPt), _ => { }).Refusal;

    private PictureRefusal? NudgeRule(PictureTarget target, int dx, int dy)
    {
        if (!target.Info.IsFloating || target.Info.Anchor is not { } anchor)
        {
            return PictureRefusal.NotFloating;
        }

        if (InCell(target))
        {
            return PictureRefusal.FloatInCell;
        }

        if (dx == 0 && dy == 0)
        {
            return PictureRefusal.NothingToChange;
        }

        return (dx != 0 && anchor.HorizontalOffsetEmu is null) || (dy != 0 && anchor.VerticalOffsetEmu is null) ? PictureRefusal.AlignedPosition : null;
    }

    private static void Nudge(PictureTarget target, int dx, int dy)
    {
        var anchor = target.Info.Anchor!;
        long? h = dx == 0 ? null : Math.Clamp((anchor.HorizontalOffsetEmu ?? 0) + (dx * PictureRules.EmuPerPoint), -PictureRules.MaxOffsetEmu, PictureRules.MaxOffsetEmu);
        long? v = dy == 0 ? null : Math.Clamp((anchor.VerticalOffsetEmu ?? 0) + (dy * PictureRules.EmuPerPoint), -PictureRules.MaxOffsetEmu, PictureRules.MaxOffsetEmu);
        PictureOperations.SetOffsets(target.Picture, h, v);
    }

    // ---- delete ------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Removes a picture as one history step: its drawing leaves the run (a run that holds nothing else goes with it) and the paragraph stays. The picture's bytes stay in the document's picture store: a save writes a part only
    /// for a picture the document still refers to, so a deleted picture leaves none, and an undo brings the picture back with its bytes. The caret is where the picture was.
    /// </summary>
    public EditResult? DeletePicture(ObjectId id, bool prefixDocument = false)
    {
        var (target, _) = FindPicture(id, prefixDocument);
        if (target is null)
        {
            return null;
        }

        var revision = History.BeginOperation();
        var offset = OffsetOf(target);
        var old = target.Paragraph.Clone();
        RemovePicture(target);
        _document.Edits.Raise();
        var result = new EditResult(new TextPosition(StoryRef.Body, target.Paragraph, offset), [new BlockChange(StoryRef.Body, target.Container, target.Index, 1, 1, [old])]);
        result.Attribute(_document);
        var point = new EditHistory.Point(target.Index, offset);
        History.Record(result, point, revision, null, after: point);
        return result;
    }

    /// <summary>The reason <see cref="DeletePicture"/> refuses, or null.</summary>
    public PictureRefusal? RefusalForDeletePicture(ObjectId id, bool prefixDocument = false) => FindPicture(id, prefixDocument).Refusal;

    // Takes the drawing out of its run, and the run out of its list when nothing else is in it.
    private void RemovePicture(PictureTarget target)
    {
        var map = Map(StoryRef.Body, target.Paragraph);
        var item = map.Items.FirstOrDefault(i => ReferenceEquals(i.Content, target.Picture));
        target.Run.Content.Remove(target.Picture);
        if (target.Run.Content.Count == 0)
        {
            item.Owner?.Remove(target.Run);
        }
    }
}
