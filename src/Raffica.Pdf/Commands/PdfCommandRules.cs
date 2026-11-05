using Raffica.Core.Pdf;

namespace Raffica.Pdf.Commands;

/// <summary>
/// When a command may run: without a document only Open (and Help and Settings, which need none) is enabled; moving between tabs needs a
/// second tab; Print is refused for a document whose permissions forbid printing, and where the system cannot print, each with its reason.
/// Save needs a dirty tab and no running save or print, Undo and Redo a history, Delete a selection, and the annotate tools and Sign a document that
/// allows changing annotations (with a reason when it does not; a permission not read yet does not refuse). The window gives the questions;
/// the rules themselves know nothing of the UI. The page commands follow the organize mode and its plan (the edits also a selection, apply a changed
/// plan); the tools, Sign, the forms and the output commands (extract, split, export) are locked while the mode is on, and the output commands
/// also while marks are unsaved, each with its reason.
/// </summary>
internal sealed class PdfCommandRules(
    Func<bool> hasDocument,
    Func<int> tabCount,
    Func<PdfPrintInfo?> printInfo,
    Func<bool>? printerAvailable = null,
    Func<bool>? isDirty = null,
    Func<bool>? canUndo = null,
    Func<bool>? canRedo = null,
    Func<bool>? busySaving = null,
    Func<bool>? annotateAllowed = null,
    Func<bool>? hasSelection = null,
    Func<bool>? saveRunning = null,
    Func<bool>? hasForm = null,
    Func<bool?>? fillAllowed = null,
    Func<bool>? hasEmptyRequired = null,
    Func<bool>? pagesReady = null,
    Func<bool>? organizeActive = null,
    Func<bool>? pageSelection = null,
    Func<bool>? planCanUndo = null,
    Func<bool>? planCanRedo = null,
    Func<bool>? planChanged = null,
    Func<bool>? marksUnsaved = null)
{
    private static readonly HashSet<string> Tools =
    [
        PdfCommandIds.AnnotateMode, PdfCommandIds.Highlight, PdfCommandIds.Underline, PdfCommandIds.StrikeOut, PdfCommandIds.SelectMark,
        PdfCommandIds.Freehand, PdfCommandIds.Rectangle, PdfCommandIds.Ellipse, PdfCommandIds.Line, PdfCommandIds.Arrow, PdfCommandIds.TextBox,
        PdfCommandIds.Sign, // a signature is an annotation: the same permission and the same reason
    ];

    private static readonly HashSet<string> FormCommands =
    [
        PdfCommandIds.FormsNext, PdfCommandIds.FormsPrevious, PdfCommandIds.FormsNextRequired, PdfCommandIds.FormsHighlight,
    ];

    // Needs the mode and at least one selected page.
    private static readonly HashSet<string> PageEdits =
    [
        PdfCommandIds.RotateLeft, PdfCommandIds.RotateRight, PdfCommandIds.DuplicatePages, PdfCommandIds.MovePagesUp, PdfCommandIds.MovePagesDown,
        PdfCommandIds.DeletePages,
    ];

    // Read the open document without the plan, so they need no open plan and no unsaved marks.
    private static readonly HashSet<string> PageOutputs = [PdfCommandIds.ExtractPages, PdfCommandIds.SplitDocument, PdfCommandIds.ExportImages];

    /// <summary>Whether the command may run now (not counting whether an action is wired behind it).</summary>
    public bool Allows(string id) => id switch
    {
        PdfCommandIds.Open or PdfCommandIds.Settings or PdfCommandIds.Help => true,
        PdfCommandIds.NextTab or PdfCommandIds.PreviousTab => tabCount() > 1,
        PdfCommandIds.Print => hasDocument() && !PrintForbidden() && !NoPrinter() && !Ask(saveRunning, false),
        PdfCommandIds.Save => hasDocument() && Ask(isDirty, false) && !Ask(busySaving, false),
        PdfCommandIds.SaveAs => hasDocument() && !Ask(busySaving, false),
        PdfCommandIds.Undo => hasDocument() && Ask(canUndo, false),
        PdfCommandIds.Redo => hasDocument() && Ask(canRedo, false),
        PdfCommandIds.DeleteMark => hasDocument() && Ask(hasSelection, false),
        PdfCommandIds.Organize => hasDocument() && (PlanOpen() || Ask(pagesReady, true)),
        _ when PageEdits.Contains(id) => hasDocument() && PlanOpen() && Ask(pageSelection, false),
        PdfCommandIds.BlankPage or PdfCommandIds.InsertFile => hasDocument() && PlanOpen(),
        PdfCommandIds.PagesUndo => hasDocument() && PlanOpen() && Ask(planCanUndo, false),
        PdfCommandIds.PagesRedo => hasDocument() && PlanOpen() && Ask(planCanRedo, false),
        PdfCommandIds.ApplyPages => hasDocument() && PlanOpen() && Ask(planChanged, false),
        PdfCommandIds.DiscardPages => hasDocument() && PlanOpen(),
        _ when PageOutputs.Contains(id) => hasDocument() && !PlanOpen() && !Ask(marksUnsaved, false),
        _ when Tools.Contains(id) => hasDocument() && !PlanOpen() && Ask(annotateAllowed, true),
        PdfCommandIds.FormsNextRequired => hasDocument() && !PlanOpen() && FormsReady() && Ask(hasEmptyRequired, true),
        _ when FormCommands.Contains(id) => hasDocument() && !PlanOpen() && FormsReady(),
        _ => hasDocument(),
    };

    /// <summary>Why the command is disabled, in the UI language, when there is something to tell (null otherwise).</summary>
    public string? ReasonFor(string id)
    {
        if (!hasDocument())
        {
            return null;
        }

        // While a plan is open the tools, Sign, the forms and the output commands are locked: the page view shows the document as it is, not
        // as the plan will leave it.
        if ((Tools.Contains(id) || FormCommands.Contains(id) || PageOutputs.Contains(id)) && PlanOpen())
        {
            return PdfStrings.PageChangesLockedReason;
        }

        if (PageOutputs.Contains(id))
        {
            return Ask(marksUnsaved, false) ? PdfStrings.SaveMarksFirstReason : null;
        }

        if (Tools.Contains(id))
        {
            return Ask(annotateAllowed, true) ? null : PdfStrings.AnnotateLockedReason;
        }

        if (FormCommands.Contains(id))
        {
            if (!Ask(hasForm, false))
            {
                return PdfStrings.FormsNoFormReason;
            }

            if (fillAllowed?.Invoke() == false)
            {
                return PdfStrings.FormFillForbidden;
            }

            return id == PdfCommandIds.FormsNextRequired && !Ask(hasEmptyRequired, true) ? PdfStrings.FormsNoRequiredReason : null;
        }

        if (id != PdfCommandIds.Print)
        {
            return null;
        }

        return PrintForbidden() ? PdfStrings.PrintLockedReason : NoPrinter() ? PdfStrings.PrintNoPrinter : null;
    }

    // The mode is on: the plan is open, identity or not (leaving an identity plan is one command, so the locks do not wait for the first change).
    private bool PlanOpen() => Ask(organizeActive, false);

    private static bool Ask(Func<bool>? question, bool unknown) => question is null ? unknown : question();

    // A document with fillable fields that does not forbid filling; a permission that is not read yet does not refuse.
    private bool FormsReady() => Ask(hasForm, false) && fillAllowed?.Invoke() != false;

    // Asked only with a document open, so a system without printing (Linux without CUPS) is probed when a file is opened, not at startup.
    private bool NoPrinter() => printerAvailable is not null && !printerAvailable();

    // A document whose permissions have not been read yet is not refused; the answer arrives a moment after it was opened.
    private bool PrintForbidden() => printInfo() is { PrintAllowed: false };
}
