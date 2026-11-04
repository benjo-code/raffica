using Raffica.Core.Pdf;

namespace Raffica.Pdf.Commands;

/// <summary>
/// When a command may run: without a document only Open (and Help and Settings, which need none) is enabled; moving between tabs needs a
/// second tab; Print is refused for a document whose permissions forbid printing, with the reason. The window gives the three questions;
/// the rules themselves know nothing of the UI.
/// </summary>
internal sealed class PdfCommandRules(Func<bool> hasDocument, Func<int> tabCount, Func<PdfPrintInfo?> printInfo)
{
    /// <summary>Whether the command may run now (not counting whether an action is wired behind it).</summary>
    public bool Allows(string id) => id switch
    {
        PdfCommandIds.Open or PdfCommandIds.Settings or PdfCommandIds.Help => true,
        PdfCommandIds.NextTab or PdfCommandIds.PreviousTab => tabCount() > 1,
        PdfCommandIds.Print => hasDocument() && !PrintForbidden(),
        _ => hasDocument(),
    };

    /// <summary>Why the command is disabled, in the UI language, when there is something to tell (null otherwise).</summary>
    public string? ReasonFor(string id) => id == PdfCommandIds.Print && hasDocument() && PrintForbidden() ? PdfStrings.PrintLockedReason : null;

    // A document whose permissions have not been read yet is not refused; the answer arrives a moment after it was opened.
    private bool PrintForbidden() => printInfo() is { PrintAllowed: false };
}
