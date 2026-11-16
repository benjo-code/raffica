namespace Raffica.Write.Docx;

/// <summary>What a save of a <see cref="DocxFile"/> has to tell besides that it worked.</summary>
/// <param name="Path">The full path the file was written to (the path that was asked for, not the file a symbolic link points to).</param>
/// <param name="LeftoverPath">
/// The path of the old file when the replace could not delete it: on Windows, a file system without the atomic rename makes the replace move the open target aside, and
/// a virus scanner or an indexer may still hold that file after four attempts. The new file is in place; the old one (<c>.pw-&lt;12 hex&gt;.old</c>) may be removed by hand. Null otherwise.
/// </param>
public sealed record SaveReport(string Path, string? LeftoverPath);
