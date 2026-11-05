using Raffica.Core.Pages;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Organizing;

namespace Raffica.Pdf;

/// <summary>
/// The commands of the Pages tab: they act on the organize session of the active tab (made by the first of them) and on its selection. The
/// session raises <see cref="OrganizeSession.Changed"/>, which refreshes the ribbon; the flows behind Apply, Discard, Insert file and the
/// output commands arrive with their own tasks and stay unwired until then.
/// </summary>
internal sealed partial class PdfWindow
{
    // A4 in points, for a blank page when no neighbour size is known yet.
    private static readonly BlankPage FallbackBlank = new(595.28, 841.89);

    // The session of the active tab while its plan is open (the mode is on, whether the plan changes anything or not), else null.
    private OrganizeSession? OpenPlan => Workspace.Active?.OrganizeIfCreated is { IsActive: true } session ? session : null;

    private void OnOrganizeCreated(OrganizeSession session) => session.Changed += RefreshCommandStates;

    /// <summary>Enters the organize mode of the active tab and opens the pages pane, or leaves it again when the plan changes nothing.</summary>
    internal void ToggleOrganize()
    {
        if (Workspace.Active is not { } tab || tab.Session.Document is null)
        {
            return;
        }

        var session = tab.Organize;
        if (session.IsActive)
        {
            // The mode is left only with a plan that changes nothing. A plan with changes keeps the mode: leaving it means to apply or to
            // discard them, and that three-way question comes with the Apply and Discard flows.
            if (session.Plan.IsIdentity)
            {
                session.End();
            }

            RefreshCommandStates();
            return;
        }

        // The annotate tools are locked while a plan is open: the mode ends them first.
        Tools.End();
        session.Enter();
        ShowPagesPane();
        RefreshCommandStates();
    }

    private void RunPageEdit(Func<OrganizeSession, OrganizeResult> edit)
    {
        if (OpenPlan is not { } session || Workspace.Active is not { } tab)
        {
            return;
        }

        var result = edit(session);
        if (!result.IsDone && _panes.TryGetValue(tab, out var panes))
        {
            panes.Pages.Organize?.Report(result);
        }
    }

    // After the last selected page, else at the end.
    private void InsertBlankPage() => RunPageEdit(session =>
    {
        var selected = session.Selection.Positions;
        var at = selected.Count > 0 ? selected[^1] + 1 : session.Plan.Slots.Count;
        return session.InsertBlank(at, BlankSizeAt(session, at));
    });

    // The new page takes the size of the page before it (the first page for a position at the start); a page of an inserted file or one whose
    // size is not read yet counts as unknown and the first page, then A4, stands in.
    private BlankPage BlankSizeAt(OrganizeSession session, int at)
    {
        var slots = session.Plan.Slots;
        if (Workspace.Active is { } tab && _views.TryGetValue(tab, out var view) && slots.Count > 0)
        {
            var neighbour = slots[Math.Clamp(at - 1, 0, slots.Count - 1)];
            if (neighbour.Blank is { } blank)
            {
                return blank;
            }

            var size = (neighbour.Source.Value == 0 ? view.PageSizeOf(neighbour.SourceIndex) : null) ?? view.FirstPageSize;
            if (size is { } known)
            {
                return new BlankPage(known.Width, known.Height);
            }
        }

        return FallbackBlank;
    }
}
