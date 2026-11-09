using Raffica.Core.Forms;

namespace Raffica.Pdf.Forms;

/// <summary>
/// What Raffica PDF does not do for a form, in plain sentences: one per thing that applies to the document and none for what does not.
/// A pure function of the document's hints and the UI language; it claims nothing about the result being right or binding.
/// </summary>
internal static class FormNotices
{
    /// <summary>The localized sentences for the flags set in <paramref name="hints"/>; calculations and format scripts share one sentence.</summary>
    public static IReadOnlyList<string> LinesFor(FormHint hints)
    {
        List<string> lines = [];
        if ((hints & (FormHint.Calculated | FormHint.FormatScripts)) != 0)
        {
            lines.Add(PdfStrings.FormNoticeScripts);
        }

        if ((hints & FormHint.Xfa) != 0)
        {
            lines.Add(PdfStrings.FormNoticeXfa);
        }

        if ((hints & FormHint.SignatureField) != 0)
        {
            lines.Add(PdfStrings.FormNoticeSignature);
        }

        if ((hints & FormHint.Buttons) != 0)
        {
            lines.Add(PdfStrings.FormNoticeButtons);
        }

        return lines;
    }
}
