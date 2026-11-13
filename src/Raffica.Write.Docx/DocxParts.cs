namespace Raffica.Write.Docx;

/// <summary>Which parts of a DOCX file <see cref="DocxFile"/> loads into the model. The body is always loaded.</summary>
[Flags]
public enum DocxParts
{
    Document = 1,
    Styles = 2,
    Numbering = 4,
    Settings = 8,
    Notes = 16,
    Comments = 32,
    HeadersFooters = 64,
    All = 127,
}
