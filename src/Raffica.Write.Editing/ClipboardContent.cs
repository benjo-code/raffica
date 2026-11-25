namespace Raffica.Write.Editing;

/// <summary>
/// What a copy puts on the clipboard: the plain text (format <c>text/plain</c>) and, when the editor has a fragment codec, the private
/// fragment (format <c>application/x-raffica-write-fragment</c>). <paramref name="SourceId"/> names the editor that made the copy; a paste
/// by the same editor is a paste into the same document. It is not part of the fragment bytes, so a fragment from anywhere else never
/// counts as the same document. <paramref name="SourcePart"/> names the package part of the story the copy came from (the body, one header
/// or footer, the footnotes, the endnotes or the comments): relationship ids belong to a part, so relationship-bearing content keeps its
/// meaning only in a paste into the same part of the same document. <paramref name="Image"/> is a picture another program put on the clipboard (the bytes of
/// a PNG, a JPEG or any other format <see cref="ImageHeaderProbe"/> knows; null for none). A copy by this editor never carries one. A paste uses it only when
/// there is neither a readable fragment nor any text: the order is own fragment, then text, then picture.
/// </summary>
public sealed record ClipboardContent(string PlainText, byte[]? Fragment, string? SourceId = null, string? SourcePart = null, ReadOnlyMemory<byte>? Image = null);

/// <summary>The result of <see cref="TextEditor.Cut"/>: the copied content and the delete that followed it.</summary>
public sealed record CutResult(ClipboardContent Content, EditResult Result);
