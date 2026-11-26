using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    // What a page reference shows while the layout knows no page for the place (the real page comes with the refresh of the page results).
    private const string UnknownPage = "?";

    /// <summary>The reason the last <see cref="InsertCrossReference"/> returned null, or null when it succeeded. Set by every call.</summary>
    public CrossRefRefusal? LastCrossRefRefusal { get; private set; }

    /// <summary>
    /// The reason a cross-reference cannot stand at <paramref name="at"/>, or null: a prefix document (<paramref name="prefixDocument"/>), a place outside the body, a one-line content control and a table of contents. What the target
    /// allows is judged by <see cref="CrossReferences.Refusal"/> and when the reference is made. A stale position throws <see cref="StalePositionException"/>.
    /// </summary>
    public CrossRefRefusal? RefusalForCrossReference(TextPosition at, bool prefixDocument = false)
    {
        if (NoteRules.Refusal(_document, at, prefixDocument) is { } refused)
        {
            return refused switch
            {
                NoteRefusal.PrefixDocument => CrossRefRefusal.Prefix,
                NoteRefusal.NoPosition => CrossRefRefusal.NoPosition,
                NoteRefusal.RestrictedControl => CrossRefRefusal.RestrictedControl,
                _ => CrossRefRefusal.NotInBody,
            };
        }

        var (story, container, index) = Locate(at);
        if (_tocs.Around(container, index) is not null)
        {
            return CrossRefRefusal.InsideToc;
        }

        var map = Map(story, at.Paragraph);
        var point = ResolvePoint(map, EditRules.Snap(map, Math.Clamp(at.Offset, 0, map.Projection.Text.Length)));
        return point.Run is { } run && InRestrictedInlineControl(at.Paragraph.Inlines, run, 0) ? CrossRefRefusal.RestrictedControl : null;
    }

    /// <summary>
    /// Inserts a cross-reference to <paramref name="target"/> at <paramref name="at"/> as one history step: a hidden bookmark <c>_Ref</c> and digits over the target (a caption: its label and number, its number or the whole caption; a
    /// heading: its text; a note: its reference; a bookmark: its range; the bookmark of an earlier reference to the same place is reused) and a field that points at it, with <c>\h</c> so that it is a link: <c>REF</c> for the text, the
    /// label and number or the number of a caption, <c>REF ... \n</c> for the number of a heading, <c>NOTEREF</c> for the number of a note, <c>PAGEREF</c> for the page. The field holds the result the refresh would give (the mark of a
    /// note and the number label of a heading come from <paramref name="host"/>; a page the host does not know shows <c>?</c> until the page results are refreshed). The caret ends behind the field. Undo takes the field and a bookmark
    /// that was made for it away together. Returns null, and changes nothing, when the choice is not offered for the target or the place takes no reference (<see cref="LastCrossRefRefusal"/> says why; a table of contents also sets
    /// <see cref="LastFieldRefusal"/>). A stale position throws <see cref="StalePositionException"/> before anything changes.
    /// </summary>
    public EditResult? InsertCrossReference(TextPosition at, CrossRefTarget target, CrossRefKind kind, IFieldHost host, bool prefixDocument = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(host);
        LastCrossRefRefusal = null;
        LastFieldRefusal = null;
        if (CrossReferences.Refusal(kind, target) is { } notOffered)
        {
            LastCrossRefRefusal = notOffered;
            return null;
        }

        if (TocRefusesAt(at))
        {
            LastCrossRefRefusal = CrossRefRefusal.InsideToc;
            return null;
        }

        if (RefusalForCrossReference(at, prefixDocument) is { } refusal)
        {
            LastCrossRefRefusal = refusal;
            return null;
        }

        if (ExtentOf(target, kind, out var gone) is not { } extent)
        {
            LastCrossRefRefusal = gone;
            return null;
        }

        var (story, atContainer, atIndex) = Locate(at);
        var form = FormOf(target, kind);
        if (CheckPlan(extent, form, at, host) is { } planRefusal)
        {
            LastCrossRefRefusal = planRefusal;
            return null;
        }

        // The bookmark of an earlier reference to the same place, if it is still exactly there and shows what a reference reads.
        var reused = BookmarkOperations.Scan(_document, _hosts).FirstOrDefault(f =>
            CrossReferences.IsRefName(f.Name) && ReferenceEquals(f.StartParagraph, extent.First) && f.StartOffset == extent.FirstOffset && f.EndParagraph is not null && ReferenceEquals(f.EndParagraph, extent.Last) && f.EndOffset == extent.LastOffset);
        int? id = reused?.Id;
        string? name = reused?.Name;
        if (reused is null)
        {
            name = CrossReferences.NextRefName(BookmarkOperations.AllNames(_document));
            id = BookmarkOperations.NextId(_document);
            if (name is null || id is null)
            {
                LastCrossRefRefusal = CrossRefRefusal.IdsUsedUp;
                return null;
            }
        }

        var revision = History.BeginOperation();

        // The paragraphs the step changes and nothing between them: the caret's (the field goes in) and, for a bookmark made now, the one or two that get its markers. The caret's is the step's block change; the others
        // are derived changes of the step (the markers are zero-width, so no block moves): undo and redo swap them with the step, in the layout, the snapshot tracker and the history, and the history keeps clones of
        // these few paragraphs only, never of the blocks that lie between them.
        var marked = new List<(Paragraph Paragraph, IList<Block> Container, int Index, Block Old)>();
        if (reused is null)
        {
            foreach (var paragraph in ReferenceEquals(extent.First, extent.Last) ? [extent.First] : new[] { extent.First, extent.Last })
            {
                if (!ReferenceEquals(paragraph, at.Paragraph) && FindIn(_document.Body.Blocks, paragraph) is { } place)
                {
                    marked.Add((paragraph, place.Container, place.Index, paragraph.Clone()));
                }
            }
        }

        var old = at.Paragraph.Clone();
        if (reused is null)
        {
            PlaceBookmarkMarkers(StoryRef.Body, extent.First, extent.FirstOffset, extent.Last, extent.LastOffset, id!.Value, name!);
        }

        var text = ResultOf(form, extent, id!.Value, host);
        if (text is null)
        {
            // A reused bookmark that no longer reads as a reference does: nothing has changed yet. A bookmark made just now always does (both markers stand in the paragraph, start first, and a note
            // reference of the target is visible: see ExtentOf).
            LastCrossRefRefusal = reused is null ? throw new InvalidOperationException("The cross-reference has no result.") : CrossRefRefusal.TargetGone;
            return null;
        }

        var map = Map(story, at.Paragraph);
        var offset = EditRules.Snap(map, Math.Clamp(at.Offset, 0, map.Projection.Text.Length));
        var point = ResolvePoint(map, offset);
        var like = point.Run is { } near && near.Content.All(c => c is TextContent) ? near : null;
        var inlines = FieldWriter.Make(form.Instruction(name!), text, like);
        PlaceRun(at.Paragraph, map, offset, inlines[0]);
        var placed = BookmarkOperations.Find(at.Paragraph.Inlines, inlines[0]) ?? throw new InvalidOperationException("The field was not placed.");
        for (var i = 1; i < inlines.Count; i++)
        {
            placed.Owner.Insert(placed.Index + i, inlines[i]);
        }

        _document.Edits.Raise();
        var behind = Map(story, at.Paragraph).Projection.Pieces.FirstOrDefault(p => ReferenceEquals(p.Run, inlines[3]))?.End ?? offset + text.Length;
        var caret = new TextPosition(story, at.Paragraph, behind);
        var change = new BlockChange(story, atContainer, atIndex, 1, 1, [old]);
        var derived = marked.Select(m => new BlockChange(StoryRef.Body, m.Container, m.Index, 1, 1, [m.Old])).ToList();
        var result = new EditResult(caret, [change], DerivedChanges: derived.Count > 0 ? derived : null);

        // One attribution call per raise: the caret's block and the paragraphs that got markers.
        _document.Edits.Attribute([change, .. derived]);
        History.Record(result, new EditHistory.Point(atIndex, offset), revision, null, after: new EditHistory.Point(atIndex, behind));
        if (derived.Count > 0)
        {
            // Attached to the step just recorded: a history that cannot place the record clears itself (nothing can then be undone across it), never a half step.
            History.AttachDerived(new DerivedRecord(derived));
        }

        return result;
    }

    // The way a reference is written: which field, which switches, and what its result is made of.
    private sealed record Form(string Field, string? Switches, ResultSource Source)
    {
        public string Instruction(string bookmark) => Field + " " + bookmark + (Switches is null ? string.Empty : " " + Switches) + @" \h";
    }

    private enum ResultSource
    {
        Text,
        Number,
        Note,
        Page,
    }

    // A reference to a heading's number reads the paragraph's number label (REF \n); a note's number is NOTEREF; a page is PAGEREF; everything else is the text between the markers.
    private static Form FormOf(CrossRefTarget target, CrossRefKind kind) => kind switch
    {
        CrossRefKind.Page => new Form("PAGEREF", null, ResultSource.Page),
        CrossRefKind.NumberOnly when target.Kind == CrossRefTargetKind.Heading => new Form("REF", @"\n", ResultSource.Number),
        CrossRefKind.NumberOnly when target.Kind is CrossRefTargetKind.Footnote or CrossRefTargetKind.Endnote => new Form("NOTEREF", null, ResultSource.Note),
        _ => new Form("REF", null, ResultSource.Text),
    };

    // The part of the body a reference points at: the paragraphs and the offsets (in the editor's projection) of its two markers, and the note of a note reference.
    private sealed record Extent(Paragraph First, int FirstOffset, Paragraph Last, int LastOffset, RunContent? Reference);

    // What the place and the target allow, judged before anything changes.
    private CrossRefRefusal? CheckPlan(Extent extent, Form form, TextPosition at, IFieldHost host)
    {
        if (form.Source == ResultSource.Number && host.NumberLabel(extent.First) is not { Length: > 0 })
        {
            return CrossRefRefusal.NoNumber;
        }

        if (form.Source == ResultSource.Note && NoteMarkOf(extent, host) is null)
        {
            return CrossRefRefusal.NoNumber;
        }

        if (form.Source != ResultSource.Text)
        {
            return null;
        }

        if (!ReferenceEquals(extent.First, extent.Last))
        {
            return CrossRefRefusal.SeveralParagraphs;
        }

        // A reference that stands in the text it shows would show itself.
        if (ReferenceEquals(at.Paragraph, extent.First))
        {
            var map = Map(at.Story, at.Paragraph);
            var offset = EditRules.Snap(map, Math.Clamp(at.Offset, 0, map.Projection.Text.Length));
            if (offset >= extent.FirstOffset && offset <= extent.LastOffset)
            {
                return CrossRefRefusal.InsideTarget;
            }
        }

        return null;
    }

    private static string? NoteMarkOf(Extent extent, IFieldHost host) => extent.Reference switch
    {
        FootnoteReference f => host.NoteMarkText(f.Id, NoteType.Footnote),
        EndnoteReference e => host.NoteMarkText(e.Id, NoteType.Endnote),
        _ => null,
    };

    // The result the refresh would give for the field that points at the bookmark with this id (the markers are in place). A page the host does not know is a question mark.
    private static string? ResultOf(Form form, Extent extent, int id, IFieldHost host) => form.Source switch
    {
        ResultSource.Page => host.PageOf(new TextPosition(StoryRef.Body, extent.First, extent.FirstOffset)) is { } page ? page.PageText : UnknownPage,
        ResultSource.Note => FieldRefresh.NoteMark(extent.First, id, host),
        ResultSource.Number => FieldRefresh.ReferenceText(extent.First, id, number: true, host),
        _ => FieldRefresh.ReferenceText(extent.First, id, number: false, host),
    };

    // The extent of the target for the choice, or null with the reason: the target is not there as the card listed it (TargetGone), or it is a caption whose number cannot be located (CaptionNumberNotRead).
    private Extent? ExtentOf(CrossRefTarget target, CrossRefKind kind, out CrossRefRefusal refusal)
    {
        refusal = CrossRefRefusal.TargetGone;
        var paragraph = target.Position.Paragraph;
        if (paragraph is null || target.Position.Story.Kind != StoryKind.Body || FindIn(_document.Body.Blocks, paragraph) is not { } place || _tocs.Around(place.Container, place.Index) is not null)
        {
            return null;
        }

        var map = Map(StoryRef.Body, paragraph);
        var length = map.Projection.Text.Length;
        switch (target.Kind)
        {
            case CrossRefTargetKind.Heading:
                return FieldRefresh.HeadingLevel(paragraph) is null ? null : new Extent(paragraph, 0, paragraph, length, null);
            case CrossRefTargetKind.Caption:
                return CaptionExtent(paragraph, map, kind, out refusal);
            case CrossRefTargetKind.Footnote or CrossRefTargetKind.Endnote:
                var plain = ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Pieces.OfType<AtomicPiece>()
                    .FirstOrDefault(p => p.Atomic == AtomicKind.NoteReference && p.Length > 0 && p.Start == target.Position.Offset);
                var wanted = target.Kind == CrossRefTargetKind.Footnote ? plain?.Content as FootnoteReference : (RunContent?)(plain?.Content as EndnoteReference);

                // A reference that is not visible (a tracked deletion) has no length: it is no target, so the markers always enclose a reference that the refresh finds.
                var piece = wanted is null ? null : map.Projection.Pieces.OfType<AtomicPiece>().FirstOrDefault(p => ReferenceEquals(p.Content, wanted) && p.Length > 0);
                return piece is null ? null : new Extent(paragraph, piece.Start, paragraph, piece.End, wanted);
            default:
                var found = BookmarkOperations.Scan(_document, _hosts).FirstOrDefault(f => !BookmarkNames.IsHidden(f.Name) && string.Equals(f.Name, target.Display, StringComparison.OrdinalIgnoreCase));
                return found is null ? null : new Extent(found.StartParagraph, found.StartOffset, found.EndParagraph ?? found.StartParagraph, found.EndParagraph is null ? found.StartOffset : found.EndOffset, null);
        }
    }

    // A caption: its label and number (from the start to the end of the sequence number), its number alone (the chapter number in front of it included) or all of it. The fields and their results are paired by their order,
    // as the status text pairs them. Where they cannot be paired the text (the whole caption) is still a target, and so is the page (of the whole caption); the label and number and the number alone are refused with their own reason.
    private static Extent? CaptionExtent(Paragraph paragraph, ParagraphMap map, CrossRefKind kind, out CrossRefRefusal refusal)
    {
        refusal = CrossRefRefusal.TargetGone;
        var length = map.Projection.Text.Length;
        if (!CrossReferences.HasCaptionNumber(paragraph))
        {
            return null;
        }

        var whole = new Extent(paragraph, 0, paragraph, length, null);
        if (kind == CrossRefKind.Text)
        {
            return whole;
        }

        var spans = FieldView.InParagraph(paragraph).Where(s => s.Parent is null).ToList();
        var results = map.Projection.Pieces.OfType<AtomicPiece>().Where(p => p.Atomic == AtomicKind.FieldResult).ToList();
        var seq = spans.FindIndex(s => s.Support.Kind == FieldKind.Seq && FieldRefresh.IsUsable(s));
        if (spans.Count != results.Count || seq < 0)
        {
            if (kind == CrossRefKind.Page)
            {
                return whole;
            }

            refusal = CrossRefRefusal.CaptionNumberNotRead;
            return null;
        }

        var end = results[seq].End;
        if (kind == CrossRefKind.NumberOnly)
        {
            var start = seq > 0 && spans[seq - 1].Support.Kind == FieldKind.StyleRef && FieldRefresh.IsUsable(spans[seq - 1]) ? results[seq - 1].Start : results[seq].Start;
            return new Extent(paragraph, start, paragraph, end, null);
        }

        return new Extent(paragraph, 0, paragraph, end, null);
    }
}
