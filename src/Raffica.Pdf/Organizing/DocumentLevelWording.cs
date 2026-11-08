using Raffica.Core.Pages;

namespace Raffica.Pdf.Organizing;

/// <summary>
/// Words what every part of an extract or split keeps from the document level (<see cref="DocumentLevelContent"/>): one line per item that is
/// there, singular and plural in both languages. The lines count and name things; they never say that anything is removed or safe, because the
/// parts keep it and the way to leave it out is the Redact tab.
/// </summary>
internal static class DocumentLevelWording
{
    /// <summary>The lines of the items that are there, in a fixed order (attached files, scripts, fields, bookmarks); empty when there are none.</summary>
    public static IReadOnlyList<string> Lines(DocumentLevelContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var lines = new List<string>();
        if (content.AttachedFiles > 0)
        {
            lines.Add(content.AttachedFiles == 1 ? PdfStrings.StaysAttachedOne : PdfStrings.Fill(PdfStrings.StaysAttachedManyFormat, content.AttachedFiles));
        }

        if (content.HasScripts)
        {
            lines.Add(PdfStrings.StaysScripts);
        }

        if (content.FieldsOnOtherPages > 0)
        {
            lines.Add(content.FieldsOnOtherPages == 1 ? PdfStrings.StaysFieldsOne : PdfStrings.Fill(PdfStrings.StaysFieldsManyFormat, content.FieldsOnOtherPages));
        }

        if (content.BookmarksToOtherPages > 0)
        {
            lines.Add(content.BookmarksToOtherPages == 1 ? PdfStrings.StaysBookmarksOne : PdfStrings.Fill(PdfStrings.StaysBookmarksManyFormat, content.BookmarksToOtherPages));
        }

        return lines;
    }
}
