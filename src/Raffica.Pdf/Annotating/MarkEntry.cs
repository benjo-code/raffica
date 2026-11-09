using Raffica.Core.Annotations;

namespace Raffica.Pdf.Annotating;

/// <summary>Which marks the list of the marks pane shows.</summary>
internal enum MarkFilter
{
    All,
    Highlight,
    Underline,
    StrikeOut,
    Drawings,
    Other,
}

/// <summary>
/// One mark of the document as the marks pane lists it: where it is, what Core read, the text it marks (markup only) and why it cannot be
/// changed, if it cannot.
/// </summary>
/// <param name="Locator">Finds the mark again (by name when it has one, else by index).</param>
/// <param name="Annotation">The mark as last read.</param>
/// <param name="Excerpt">The text under a highlight, underline or strike-out (at most 120 characters), else <see langword="null"/>.</param>
/// <param name="LockReason">Why the mark cannot be changed or commented (shown with the entry), or <see langword="null"/>.</param>
internal sealed record MarkEntry(AnnotationLocator Locator, PdfAnnotation Annotation, string? Excerpt, string? LockReason)
{
    /// <summary>The 0-based page.</summary>
    public int Page => Annotation.Page;

    /// <summary>
    /// The comment: the <c>/Contents</c> of a markup or a drawing. A text box has no comment, its <c>/Contents</c> is the text it shows
    /// (see <see cref="Excerpt"/>), which is changed in the document.
    /// </summary>
    public string? Comment => Annotation.Kind == AnnotationKind.TextBox || string.IsNullOrEmpty(Annotation.Contents) ? null : Annotation.Contents;

    /// <summary>Whether the pane offers its comment field for this mark.</summary>
    public bool CanComment => Annotation.Editable && Annotation.Kind is not (AnnotationKind.TextBox or AnnotationKind.Other);

    /// <summary>The kind in the UI language; a kind Raffica does not know says "Other: " and the subtype.</summary>
    public string KindText => Annotation.Kind switch
    {
        AnnotationKind.Highlight => PdfStrings.MarkKindHighlight,
        AnnotationKind.Underline => PdfStrings.MarkKindUnderline,
        AnnotationKind.StrikeOut => PdfStrings.MarkKindStrikeOut,
        AnnotationKind.Ink => PdfStrings.MarkKindFreehand,
        AnnotationKind.Rectangle => PdfStrings.MarkKindRectangle,
        AnnotationKind.Ellipse => PdfStrings.MarkKindEllipse,
        AnnotationKind.Line => PdfStrings.MarkKindLine,
        AnnotationKind.Arrow => PdfStrings.MarkKindArrow,
        AnnotationKind.TextBox => PdfStrings.MarkKindTextBox,
        AnnotationKind.Signature => PdfStrings.MarkKindSignature,
        _ => PdfStrings.Fill(PdfStrings.MarkKindOtherFormat, Annotation.Subtype),
    };

    /// <summary>Whether the mark belongs to the filter.</summary>
    public bool Matches(MarkFilter filter) => filter switch
    {
        MarkFilter.All => true,
        MarkFilter.Highlight => Annotation.Kind == AnnotationKind.Highlight,
        MarkFilter.Underline => Annotation.Kind == AnnotationKind.Underline,
        MarkFilter.StrikeOut => Annotation.Kind == AnnotationKind.StrikeOut,
        MarkFilter.Drawings => Annotation.Kind is AnnotationKind.Ink or AnnotationKind.Rectangle or AnnotationKind.Ellipse or AnnotationKind.Line
            or AnnotationKind.Arrow or AnnotationKind.TextBox or AnnotationKind.Signature,
        MarkFilter.Other => Annotation.Kind == AnnotationKind.Other,
        _ => false,
    };

    /// <summary>A freehand stroke without a comment: the kind of mark that collapses into a group when there are many.</summary>
    public bool IsPlainStroke => Annotation.Kind == AnnotationKind.Ink && string.IsNullOrEmpty(Annotation.Contents);

    /// <summary>Whether the entry is the one for this mark (the same page and name, or index when it has no name).</summary>
    public bool IsFor(AnnotationLocator locator) =>
        Locator.Page == locator.Page && (Locator.Name is not null ? Locator.Name == locator.Name : locator.Name is null && Locator.Index == locator.Index);
}

/// <summary>A row of the marks list: one mark or a group of freehand strokes.</summary>
internal abstract record MarkRow;

/// <summary>A row for one mark.</summary>
internal sealed record EntryRow(MarkEntry Entry, bool InGroup = false) : MarkRow;

/// <summary>A row for many freehand strokes without a comment on one page; the strokes follow it while it is expanded.</summary>
internal sealed record GroupRow(int Page, IReadOnlyList<MarkEntry> Strokes, bool Expanded) : MarkRow
{
    /// <summary>"Freehand, N strokes" in the UI language.</summary>
    public string Text => PdfStrings.Fill(PdfStrings.MarkGroupFormat, Strokes.Count);

    // The strokes are compared by content: a list is made anew for every call of the model, and an unchanged group must stay equal.
    public bool Equals(GroupRow? other) =>
        other is not null && Page == other.Page && Expanded == other.Expanded && Strokes.SequenceEqual(other.Strokes);

    public override int GetHashCode() => HashCode.Combine(Page, Expanded, Strokes.Count);
}
