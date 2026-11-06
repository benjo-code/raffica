namespace Raffica.Pdf.Commands;

/// <summary>The working modes of the PDF app that exclude one another (see <see cref="ModeExclusion"/>).</summary>
internal enum PdfMode
{
    /// <summary>An annotate tool is active.</summary>
    Annotate,

    /// <summary>Forms are being filled in.</summary>
    Forms,

    /// <summary>A page plan is open.</summary>
    Organize,

    /// <summary>Redactions are marked and not applied yet.</summary>
    Redact,

    /// <summary>A text recognition runs.</summary>
    Ocr,

    /// <summary>The text of the page is being edited.</summary>
    TextEdit,
}

/// <summary>
/// Which modes may run together: annotating and filling in forms, nothing else. Every other pair excludes, and the refusal comes with a reason in
/// the UI language, so a command that would start a second mode is disabled with it. A mode is active when it was entered with
/// <see cref="Enter"/> or when the probe of the window says so (the sessions of the active tab; the window gives it, this class knows no UI).
/// </summary>
internal sealed class ModeExclusion(Func<PdfMode, bool>? probe = null)
{
    private readonly HashSet<PdfMode> _entered = [];

    /// <summary>Whether the mode is on now.</summary>
    public bool IsActive(PdfMode mode) => _entered.Contains(mode) || probe?.Invoke(mode) == true;

    /// <summary>Whether <paramref name="mode"/> may start now; otherwise <paramref name="reason"/> tells why (a mode that is already on may be entered again).</summary>
    public bool CanEnter(PdfMode mode, out string? reason)
    {
        foreach (var active in Enum.GetValues<PdfMode>())
        {
            if (active != mode && IsActive(active) && !Together(active, mode))
            {
                reason = ReasonOf(active);
                return false;
            }
        }

        reason = null;
        return true;
    }

    /// <summary>Marks the mode as on.</summary>
    public void Enter(PdfMode mode) => _entered.Add(mode);

    /// <summary>Marks the mode as off.</summary>
    public void Leave(PdfMode mode) => _entered.Remove(mode);

    private static bool Together(PdfMode a, PdfMode b) =>
        (a == PdfMode.Annotate && b == PdfMode.Forms) || (a == PdfMode.Forms && b == PdfMode.Annotate);

    // The reason names the mode that is in the way.
    private static string ReasonOf(PdfMode active) => active switch
    {
        PdfMode.Annotate => PdfStrings.ModeAnnotateReason,
        PdfMode.Forms => PdfStrings.ModeFormsReason,
        PdfMode.Organize => PdfStrings.PageChangesLockedReason,
        PdfMode.Redact => PdfStrings.ModeRedactReason,
        PdfMode.Ocr => PdfStrings.ModeOcrReason,
        PdfMode.TextEdit => PdfStrings.ModeTextEditReason,
        _ => throw new ArgumentOutOfRangeException(nameof(active), active, "This mode has no reason."),
    };
}
