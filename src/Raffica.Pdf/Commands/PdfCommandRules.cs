using Raffica.Core.Pdf;

namespace Raffica.Pdf.Commands;

/// <summary>
/// When a command may run: without a document only Open (and Help and Settings, which need none) is enabled; moving between tabs needs a
/// second tab; Print is refused for a document whose permissions forbid printing, and where the system cannot print, each with its reason.
/// Save needs a dirty tab and no running save or print, Undo and Redo a history, Delete a selection, and the annotate tools and Sign a document that
/// allows changing annotations (with a reason when it does not; a permission not read yet does not refuse). The window gives the questions;
/// the rules themselves know nothing of the UI. The page commands follow the organize mode and its plan (the edits also a selection, apply a changed
/// plan); the tools, Sign, the forms and the output commands (extract, split, export) are locked while the mode is on, and the output commands
/// also while marks are unsaved. Entering the mode, Apply and the output commands also wait for a running save (or another file job), each with its reason.
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
    Func<bool>? marksUnsaved = null,
    Func<bool>? redactMarked = null,
    Func<bool>? ocrLibrary = null,
    Func<bool>? ocrStarter = null,
    Func<bool>? ocrRunning = null,
    ModeExclusion? modes = null)
{
    // Without the window's own exclusion the only mode known is the page plan, which organizeActive tells.
    private readonly ModeExclusion _modes = modes ?? new ModeExclusion(mode => mode == PdfMode.Organize && organizeActive?.Invoke() == true);

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

    // Run only while their mode is on (they end it or work inside it).
    private static readonly Dictionary<string, PdfMode> InsideMode = new()
    {
        [PdfCommandIds.RedactMarkSelection] = PdfMode.Redact,
        [PdfCommandIds.RedactFind] = PdfMode.Redact,
        [PdfCommandIds.RedactRasterPage] = PdfMode.Redact,
        [PdfCommandIds.RedactDeleteMarks] = PdfMode.Redact,
        [PdfCommandIds.RedactDiscard] = PdfMode.Redact,
        [PdfCommandIds.RedactApply] = PdfMode.Redact,
        [PdfCommandIds.OcrCancel] = PdfMode.Ocr,
        [PdfCommandIds.TextEditDone] = PdfMode.TextEdit,
    };

    // Start a mode: allowed when ModeExclusion lets the mode in (or it is on already, which the command then ends).
    private static readonly Dictionary<string, PdfMode> StartsMode = new()
    {
        [PdfCommandIds.RedactMode] = PdfMode.Redact,
        [PdfCommandIds.OcrRun] = PdfMode.Ocr,
        [PdfCommandIds.TextEdit] = PdfMode.TextEdit,
    };

    /// <summary>Whether the command may run now (not counting whether an action is wired behind it).</summary>
    public bool Allows(string id) => id switch
    {
        PdfCommandIds.Open or PdfCommandIds.Settings or PdfCommandIds.Help => true,
        PdfCommandIds.NextTab or PdfCommandIds.PreviousTab => tabCount() > 1,
        PdfCommandIds.Print => hasDocument() && !PrintForbidden() && !NoPrinter() && !Ask(saveRunning, false) && !Ask(ocrRunning, false),
        PdfCommandIds.Save => hasDocument() && Ask(isDirty, false) && !Ask(busySaving, false) && !Ask(ocrRunning, false),
        PdfCommandIds.SaveAs => hasDocument() && !Ask(busySaving, false) && !Ask(ocrRunning, false),
        PdfCommandIds.Undo => hasDocument() && Ask(canUndo, false) && !_modes.IsActive(PdfMode.Ocr),
        PdfCommandIds.Redo => hasDocument() && Ask(canRedo, false) && !_modes.IsActive(PdfMode.Ocr),
        PdfCommandIds.DeleteMark => hasDocument() && Ask(hasSelection, false),
        PdfCommandIds.RedactApply => hasDocument() && _modes.IsActive(PdfMode.Redact) && Ask(redactMarked, false) && !Ask(saveRunning, false),
        _ when InsideMode.TryGetValue(id, out var inside) => hasDocument() && _modes.IsActive(inside),
        PdfCommandIds.RedactMode => hasDocument() && (_modes.IsActive(PdfMode.Redact) || (_modes.CanEnter(PdfMode.Redact, out _) && Ask(pagesReady, true) && !Ask(saveRunning, false))),
        PdfCommandIds.TextEdit => hasDocument() && (_modes.IsActive(PdfMode.TextEdit) || (_modes.CanEnter(PdfMode.TextEdit, out _) && Ask(pagesReady, true) && Ask(annotateAllowed, true) && !Ask(saveRunning, false))),
        PdfCommandIds.OcrRun => hasDocument() && _modes.CanEnter(PdfMode.Ocr, out _) && Ask(ocrLibrary, true) && Ask(ocrStarter, true) && Ask(annotateAllowed, true) && Ask(pagesReady, true) && !Ask(ocrRunning, false) && !Ask(saveRunning, false),
        _ when StartsMode.TryGetValue(id, out var starts) => hasDocument() && _modes.CanEnter(starts, out _),
        PdfCommandIds.OcrLanguages => hasDocument() && !_modes.IsActive(PdfMode.Ocr),
        PdfCommandIds.Organize => hasDocument() && (PlanOpen() || (_modes.CanStart(PdfMode.Organize, out _) && Ask(pagesReady, true) && !Ask(saveRunning, false))),
        _ when PageEdits.Contains(id) => hasDocument() && PlanOpen() && Ask(pageSelection, false),
        PdfCommandIds.BlankPage or PdfCommandIds.InsertFile => hasDocument() && PlanOpen(),
        PdfCommandIds.PagesUndo => hasDocument() && PlanOpen() && Ask(planCanUndo, false),
        PdfCommandIds.PagesRedo => hasDocument() && PlanOpen() && Ask(planCanRedo, false),
        PdfCommandIds.ApplyPages => hasDocument() && PlanOpen() && Ask(planChanged, false) && !Ask(saveRunning, false),
        PdfCommandIds.DiscardPages => hasDocument() && PlanOpen(),
        _ when PageOutputs.Contains(id) => hasDocument() && !PlanOpen() && OutputModeReason() is null && !Ask(marksUnsaved, false) && !Ask(saveRunning, false),
        _ when Tools.Contains(id) => hasDocument() && _modes.CanEnter(PdfMode.Annotate, out _) && Ask(annotateAllowed, true),
        PdfCommandIds.FormsNextRequired => hasDocument() && _modes.CanEnter(PdfMode.Forms, out _) && FormsReady() && Ask(hasEmptyRequired, true),
        _ when FormCommands.Contains(id) => hasDocument() && _modes.CanEnter(PdfMode.Forms, out _) && FormsReady(),
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
        if (PageOutputs.Contains(id) && PlanOpen())
        {
            return PdfStrings.PageChangesLockedReason;
        }

        // A mode in the way gives its reason: the tools and Sign ask for the annotate mode, the form commands for the forms, the starting commands for theirs.
        var entering = Tools.Contains(id) ? PdfMode.Annotate : FormCommands.Contains(id) ? PdfMode.Forms : StartsMode.TryGetValue(id, out var starts) ? starts : (PdfMode?)null;
        if (entering is { } mode && !_modes.CanEnter(mode, out var inTheWay))
        {
            return inTheWay;
        }

        // Without the OCR library next to the program recognition cannot run; the reason says so (a missing starter has none: it is not the user's to fix).
        if (id == PdfCommandIds.OcrRun && !Ask(ocrLibrary, true))
        {
            return PdfStrings.OcrNoLibraryReason;
        }

        // Recognition writes a text layer into the document: it needs the permission the annotate tools need, with the same reason.
        if (id == PdfCommandIds.OcrRun && !Ask(annotateAllowed, true))
        {
            return PdfStrings.AnnotateLockedReason;
        }

        // A run that goes on (in this tab or in another one) holds the file: what would write to it or start another job waits, with the reason of the run.
        if (Ask(ocrRunning, false) && id is PdfCommandIds.Save or PdfCommandIds.SaveAs or PdfCommandIds.Print or PdfCommandIds.OcrRun)
        {
            return PdfStrings.ModeOcrReason;
        }

        if (id is PdfCommandIds.Undo or PdfCommandIds.Redo && _modes.IsActive(PdfMode.Ocr))
        {
            return PdfStrings.ModeOcrReason;
        }

        // Editing a line writes into the document: it needs the permission the annotate tools need, with the same reason, and waits for a running save.
        if (id == PdfCommandIds.TextEdit && !_modes.IsActive(PdfMode.TextEdit))
        {
            return !Ask(annotateAllowed, true) ? PdfStrings.AnnotateLockedReason : Ask(saveRunning, false) ? BusyReason() : null;
        }

        if (id == PdfCommandIds.OcrRun)
        {
            return Ask(saveRunning, false) ? BusyReason() : null;
        }

        // The language files are read when an engine is made and a file in use cannot be replaced: no import while a recognition runs.
        if (id == PdfCommandIds.OcrLanguages)
        {
            return _modes.ReasonWhileActive(PdfMode.Ocr);
        }

        if (PageOutputs.Contains(id) && OutputModeReason() is { } modeReason)
        {
            return modeReason;
        }

        if (id == PdfCommandIds.Organize && !PlanOpen() && !_modes.CanStart(PdfMode.Organize, out var organizeBlocked))
        {
            return organizeBlocked;
        }

        if (PageOutputs.Contains(id))
        {
            return Ask(marksUnsaved, false) ? PdfStrings.SaveMarksFirstReason : Ask(saveRunning, false) ? BusyReason() : null;
        }

        // A save, an apply or an output job that runs holds the file: entering the mode and applying wait for it.
        if ((id == PdfCommandIds.Organize && !PlanOpen()) || id == PdfCommandIds.ApplyPages || id == PdfCommandIds.RedactApply || (id == PdfCommandIds.RedactMode && !_modes.IsActive(PdfMode.Redact)))
        {
            return Ask(saveRunning, false) ? BusyReason() : null;
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
    // The output commands read the open document without the plan; a redaction plan, a running recognition or a text edit holds it.
    private string? OutputModeReason() => _modes.ReasonWhileActive(PdfMode.Redact, PdfMode.Ocr, PdfMode.TextEdit);

    private bool PlanOpen() => _modes.IsActive(PdfMode.Organize);

    // The busy list of the window includes a text recognition (in any tab): when one goes, the reason names it, not a save.
    private string BusyReason() => Ask(ocrRunning, false) ? PdfStrings.ModeOcrReason : PdfStrings.SaveBusy;

    private static bool Ask(Func<bool>? question, bool unknown) => question is null ? unknown : question();

    // A document with fillable fields that does not forbid filling; a permission that is not read yet does not refuse.
    private bool FormsReady() => Ask(hasForm, false) && fillAllowed?.Invoke() != false;

    // Asked only with a document open, so a system without printing (Linux without CUPS) is probed when a file is opened, not at startup.
    private bool NoPrinter() => printerAvailable is not null && !printerAvailable();

    // A document whose permissions have not been read yet is not refused; the answer arrives a moment after it was opened.
    private bool PrintForbidden() => printInfo() is { PrintAllowed: false };
}
