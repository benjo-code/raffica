using Raffica.Core.Viewing;

namespace Raffica.Write.Commands;

/// <summary>
/// When a command may run: Open, Settings and the command search always; the others need a document. The zoom commands also have limits (the range of
/// <see cref="ZoomSteps"/>) and the outline waits for its pane; each disabled command has its reason, so nothing is dimmed silently. The window gives the
/// questions; the rules themselves know nothing of the UI.
/// </summary>
/// <param name="hasDocument">Whether a document tab is open.</param>
/// <param name="zoom">The zoom of the view of the active tab (1 = 100 %).</param>
/// <param name="outlineAvailable">Whether the outline pane exists (the step that builds it says yes).</param>
internal sealed class WriteCommandRules(Func<bool> hasDocument, Func<double> zoom, Func<bool>? outlineAvailable = null)
{
    private const double Tolerance = 1e-9;

    /// <summary>Whether the command may run now (not counting whether an action is wired behind it).</summary>
    public bool Allows(string id) => id switch
    {
        WriteCommandIds.Open or WriteCommandIds.Settings or WriteCommandIds.Search => true,
        WriteCommandIds.ZoomIn => hasDocument() && zoom() < ZoomSteps.Max - Tolerance,
        WriteCommandIds.ZoomOut => hasDocument() && zoom() > ZoomSteps.Min + Tolerance,
        WriteCommandIds.Outline => hasDocument() && outlineAvailable?.Invoke() == true,
        _ => hasDocument(),
    };

    /// <summary>
    /// Why the command is disabled, in the UI language, when there is something to tell (null otherwise). With a document open every disabled command has a
    /// reason; without one the start view shows no commands, so nothing is told.
    /// </summary>
    public string? ReasonFor(string id)
    {
        if (!hasDocument() || Allows(id))
        {
            return null;
        }

        return id switch
        {
            WriteCommandIds.ZoomIn => WriteStrings.ReasonZoomMax,
            WriteCommandIds.ZoomOut => WriteStrings.ReasonZoomMin,
            WriteCommandIds.Outline => WriteStrings.ReasonOutlineSoon,
            _ => null,
        };
    }
}
