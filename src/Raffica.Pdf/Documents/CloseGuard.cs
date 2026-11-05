namespace Raffica.Pdf.Documents;

/// <summary>What the user chose when asked about a document with unsaved changes.</summary>
internal enum CloseChoice
{
    Save,
    DontSave,
    Cancel,
}

/// <summary>The question of a tab with an open page plan: what the user chose, and the Apply flow behind "Apply…" (true when the plan was written).</summary>
internal sealed record PlanClose(Func<PdfTab, Task<CloseChoice>> Prompt, Func<PdfTab, Task<bool>> Apply);

/// <summary>
/// Decides whether a tab may close: a clean tab closes at once, a dirty tab asks once. <c>Save</c> closes only if the save worked and no unresolved draft is left,
/// <c>Cancel</c> keeps the tab. A tab with an open page plan that changes something asks its own question first (<paramref name="plan"/>, "Discard the page
/// changes?"): <c>Save</c> is Apply and closes only if it succeeded, <c>DontSave</c> discards the plan, <c>Cancel</c> keeps tab and plan. Marks that are unsaved
/// next to the plan (committed before the mode started) are never dropped with it: after the plan is dealt with, the normal question about them follows.
/// A plan that changes nothing and no unsaved marks leave silently.
/// </summary>
internal sealed class CloseGuard(Func<PdfTab, Task<CloseChoice>> prompt, Func<PdfTab, Task<bool>> save, PlanClose? plan = null)
{
    public async Task<bool> CanCloseAsync(PdfTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        // A mark that is still being made counts: wait for it, so the dirty state is current before it is read.
        if (tab.AnnotationsIfCreated is { } annotations)
        {
            await annotations.FlushAsync();
        }

        // The tab is dirty while a changed plan is open, but the generic "Save changes?" would be the wrong question for it: the plan has its own.
        if (plan is not null && tab.OrganizeIfCreated is { IsActive: true } open && !open.Plan.IsIdentity)
        {
            switch (await plan.Prompt(tab))
            {
                case CloseChoice.Save:
                    return await plan.Apply(tab);
                case CloseChoice.DontSave:
                    // The plan ends here, so that the tab is dirty only by what is not the plan's (unsaved marks), which the question below asks about
                    // and the save of that question writes as an ordinary save.
                    open.End();
                    break;
                default:
                    return false;
            }
        }

        if (!tab.IsDirty)
        {
            return true;
        }

        return await prompt(tab) switch
        {
            CloseChoice.Save => await save(tab) && !HoldsUnresolvedEdit(tab),
            CloseChoice.DontSave => true,
            _ => false,
        };
    }

    // A draft that could not be made (a refused text box text, a comment that could not be applied) is not in the file after a save: the tab
    // stays open so the user can fix the draft or discard it, instead of losing it with the close.
    private static bool HoldsUnresolvedEdit(PdfTab tab) => tab.AnnotationsIfCreated is { HasUnresolved: true };

    /// <summary>Asks for every tab in turn and stops at the first one that must stay.</summary>
    public async Task<bool> CanCloseAllAsync(IEnumerable<PdfTab> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        foreach (var tab in tabs)
        {
            if (!await CanCloseAsync(tab))
            {
                return false;
            }
        }

        return true;
    }
}
