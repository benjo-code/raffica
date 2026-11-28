namespace Raffica.Write.Model;

/// <summary>
/// What a reader of the content keys on: the <see cref="DocumentEdits.Revision"/> (an edit, an undo or a redo moved the history) and the
/// <see cref="DocumentEdits.DerivedVersion"/> (a derived change, like a field refresh, replaced text without being an edit). Two stamps are equal only when the content is the same.
/// </summary>
public readonly record struct EditStamp(int Revision, int DerivedVersion);
