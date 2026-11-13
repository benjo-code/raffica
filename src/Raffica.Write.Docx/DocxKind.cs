namespace Raffica.Write.Docx;

/// <summary>The kind of file by its main part's content type.</summary>
public enum DocxKind
{
    /// <summary>A document without macros.</summary>
    Docx,

    /// <summary>A macro-enabled document or template (<c>.docm</c>, <c>.dotm</c>): its macro parts are kept untouched and never run.</summary>
    Docm,
}
