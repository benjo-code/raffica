namespace Raffica.Write.Documents;

/// <summary>Why a file did not open. The app words each one (<see cref="WriteStrings.OpenError"/>); the text of an exception or of the document is never shown.</summary>
public enum OpenError
{
    /// <summary>The file does not exist (any more).</summary>
    NotFound,

    /// <summary>The file exists but cannot be read: no permission, or another program holds it.</summary>
    AccessDenied,

    /// <summary>The file is no Word document (a text file, an empty file, a folder, another kind of package).</summary>
    NotADocx,

    /// <summary>The file looks like a DOCX but is damaged (a cut-off or broken package, a part that is not well-formed).</summary>
    Corrupt,

    /// <summary>The file is larger than the app opens.</summary>
    TooLarge,

    /// <summary>Anything else (an invalid path, a failure while laying out the first page).</summary>
    Other,
}

/// <summary>What <see cref="OpenFlow.OpenAsync"/> ends in: the tab (new, or the one that already showed the file) or the reason it did not open.</summary>
internal abstract record OpenResult
{
    private protected OpenResult()
    {
    }
}

/// <summary>The file is open in <paramref name="Tab"/>, which is the active tab now.</summary>
internal sealed record Opened(WriteTab Tab) : OpenResult;

/// <summary>The file did not open; no tab was made and nothing is left behind.</summary>
internal sealed record Failed(OpenError Error) : OpenResult;

/// <summary>A file that <see cref="OpenFlow"/> could not open and why (the summary of an open of several files).</summary>
internal readonly record struct OpenFailure(string Path, OpenError Error);
