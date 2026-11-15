namespace Raffica.Write.Editing;

/// <summary>
/// What a copy puts on the clipboard: the plain text (format <c>text/plain</c>) and, when the editor has a fragment codec, the private
/// fragment (format <c>application/x-raffica-write-fragment</c>). <paramref name="SourceId"/> names the editor that made the copy; a paste
/// by the same editor is a paste into the same document. It is not part of the fragment bytes, so a fragment from anywhere else never
/// counts as the same document.
/// </summary>
public sealed record ClipboardContent(string PlainText, byte[]? Fragment, string? SourceId = null);

/// <summary>The result of <see cref="TextEditor.Cut"/>: the copied content and the delete that followed it.</summary>
public sealed record CutResult(ClipboardContent Content, EditResult Result);
