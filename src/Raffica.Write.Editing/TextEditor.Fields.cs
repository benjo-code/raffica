using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    /// <summary>
    /// Brings the results of <c>SEQ</c>, <c>STYLEREF 1 \s</c>, <c>REF</c> and <c>NOTEREF</c> fields of the body up to date, from the model and <paramref name="host"/> alone. It is not an edit: no revision, no dirty flag, no history step
    /// and no caret (a table of contents whose paragraph count changed is <see cref="FieldRefreshResult.Structural"/>: attach it with <see cref="EditHistory.AttachDerived"/>); a refresh that wrote something raises <see cref="DocumentEdits.DerivedVersion"/> and says so to the snapshot tracker, and the caller brings <see cref="FieldRefreshResult.Content"/> into the layout
    /// (<c>DocumentLayout.ApplyEdit</c>). <paramref name="touched"/> are the block changes of the edit that made the refresh necessary (null for the whole body, as for the first refresh of a document); the walk then writes only to those blocks
    /// unless one of them (before or after the change) holds a heading, a bookmark, a note reference, a <c>SEQ</c> field or a numbered paragraph, which other results depend on, or a change names a nested list (a table cell, a content control), when the whole body is walked. <b>Pass null</b> for an <see cref="EditResult"/> with <see cref="EditResult.Style"/> or <see cref="EditResult.Sections"/> set (a heading-numbering switch changes every label and has no block changes) and for any caller that cannot name the changed blocks; the changes of an undo or a redo are valid as <paramref name="touched"/>. Run it under the edit lease on the UI thread, never while a save or an IME composition holds the session.
    /// </summary>
    public FieldRefreshResult RefreshFields(IFieldHost host, RefreshScope scope, IReadOnlyList<BlockChange>? touched = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        // A change in the number of paragraphs of a table of contents is carried by the newest history step (the caller attaches it), which cannot happen while a redo is pending: the table of contents is then left as it is and the result says so, before anything is changed.
        return FieldRefresh.Run(_document, host, scope, touched, allowStructural: !History.CanRedo);
    }
}
