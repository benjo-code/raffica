using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    /// <summary>
    /// Brings the results of <c>SEQ</c>, <c>STYLEREF 1 \s</c>, <c>REF</c> and <c>NOTEREF</c> fields of the body up to date, from the model and <paramref name="host"/> alone. It is not an edit: no revision, no dirty flag, no history step
    /// and no caret; a refresh that wrote something raises <see cref="DocumentEdits.DerivedVersion"/> and says so to the snapshot tracker, and the caller brings <see cref="FieldRefreshResult.Content"/> into the layout
    /// (<c>DocumentLayout.ApplyEdit</c>). <paramref name="touched"/> are the block changes of the edit that made the refresh necessary (null for the whole body, as for the first refresh of a document); the walk then writes only to those blocks
    /// unless one of them holds a heading, a bookmark, a note reference or a <c>SEQ</c> field, which other results depend on. Run it under the edit lease on the UI thread, never while a save or an IME composition holds the session.
    /// </summary>
    public FieldRefreshResult RefreshFields(IFieldHost host, RefreshScope scope, IReadOnlyList<BlockChange>? touched = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        return FieldRefresh.Run(_document, host, scope, touched);
    }
}
