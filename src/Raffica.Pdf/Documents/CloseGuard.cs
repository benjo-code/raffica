namespace Raffica.Pdf.Documents;

/// <summary>What the user chose when asked about a document with unsaved changes.</summary>
internal enum CloseChoice
{
    Save,
    DontSave,
    Cancel,
}

/// <summary>
/// Decides whether a tab may close: a clean tab closes at once, a dirty tab asks once. <c>Save</c> closes only if the save worked and no unresolved draft is left,
/// <c>Cancel</c> keeps the tab. A tab with an open page plan that changes something asks its own question instead (<paramref name="planPrompt"/>, "Discard the page
/// changes?"): <c>Save</c> is Apply and closes only if <paramref name="applyPlan"/> succeeded, <c>DontSave</c> discards the plan, <c>Cancel</c> keeps tab and plan.
/// A plan that changes nothing leaves silently.
/// </summary>
internal sealed class CloseGuard(
    Func<PdfTab, Task<CloseChoice>> prompt,
    Func<PdfTab, Task<bool>> save,
    Func<PdfTab, Task<CloseChoice>>? planPrompt = null,
    Func<PdfTab, Task<bool>>? applyPlan = null)
{
    public async Task<bool> CanCloseAsync(PdfTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        // A mark that is still being made counts: wait for it, so the dirty state is current before it is read.
        if (tab.AnnotationsIfCreated is { } annotations)
        {
            await annotations.FlushAsync();
        }

        // The tab is dirty while a plan is open, but the generic "Save changes?" would be the wrong question: the plan has its own. Marks cannot be
        // open at the same time (the mode locks them), so nothing else is asked.
        if (planPrompt is not null && applyPlan is not null && tab.OrganizeIfCreated is { IsActive: true } plan)
        {
            return plan.Plan.IsIdentity || await planPrompt!(tab) switch
            {
                CloseChoice.Save => await applyPlan!(tab),
                CloseChoice.DontSave => true,
                _ => false,
            };
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
