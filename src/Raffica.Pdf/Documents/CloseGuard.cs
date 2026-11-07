namespace Raffica.Pdf.Documents;

/// <summary>What the user chose when asked about a document with unsaved changes.</summary>
internal enum CloseChoice
{
    Save,
    DontSave,
    Cancel,
}

/// <summary>
/// The question of a tab with an open page plan: what the user chose, the Apply flow behind "Apply…" (true when the plan was written) and the save of the
/// marks alone (<paramref name="SaveMarks"/>, used when the plan was discarded but is still open until the tab goes; without it the guard's own save is used).
/// </summary>
internal sealed record PlanClose(Func<PdfTab, Task<CloseChoice>> Prompt, Func<PdfTab, Task<bool>> Apply, Func<PdfTab, Task<bool>>? SaveMarks = null);

/// <summary>The question of a tab with a text recognition that still runs: what the user chose (Stop recognition is <see cref="CloseChoice.DontSave"/>, Keep going is Cancel) and the stop that ends the run and waits for it.</summary>
internal sealed record OcrClose(Func<PdfTab, Task<CloseChoice>> Prompt, Func<PdfTab, Task> Stop);

/// <summary>
/// Decides whether a tab may close: a clean tab closes at once, a dirty tab asks once. <c>Save</c> closes only if the save worked and no unresolved draft is left,
/// <c>Cancel</c> keeps the tab. A tab with an open page plan that changes something asks its own question first (<paramref name="plan"/>, "Discard the page
/// changes?"): <c>Save</c> is Apply and closes only if it succeeded, <c>DontSave</c> discards the plan, <c>Cancel</c> keeps tab and plan. Marks that are unsaved
/// next to the plan (committed before the mode started) are never dropped with it: after the plan is dealt with, the normal question about them follows.
/// A tab whose text recognition still runs asks first of all (<paramref name="ocr"/>, "Cancel the text recognition?"): Stop recognition ends the run without keeping any page and goes on,
/// Keep going keeps the tab. A tab in the redact mode with marks that are not applied asks its own question first (<paramref name="redact"/>, "Discard the redactions?"): <c>Save</c> is Apply and closes only if the
/// redactions were written, <c>DontSave</c> (Discard) lets the tab close, <c>Cancel</c> keeps tab and marks. A plan that changes nothing and no unsaved marks leave silently. The guard never ends a plan itself: "Discard" is only remembered, so a Cancel at the
/// question about the marks (or at a later tab of Close all) keeps the plan; the plan goes with the tab when the tab really closes.
/// </summary>
internal sealed class CloseGuard(Func<PdfTab, Task<CloseChoice>> prompt, Func<PdfTab, Task<bool>> save, PlanClose? plan = null, PlanClose? redact = null, OcrClose? ocr = null)
{
    public async Task<bool> CanCloseAsync(PdfTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        // A recognition that goes on is stopped (and its pages dropped) before anything else is asked; Keep going keeps the tab.
        if (ocr is not null && tab.OcrIfCreated is { IsActive: true })
        {
            if (await ocr.Prompt(tab) != CloseChoice.DontSave)
            {
                return false;
            }

            await ocr.Stop(tab);
        }

        // A mark that is still being made counts: wait for it, so the dirty state is current before it is read.
        if (tab.AnnotationsIfCreated is { } annotations)
        {
            await annotations.FlushAsync();
        }

        // Redactions that are marked and not applied are no change of the file, so the tab is not dirty; they still ask before they are lost.
        // Apply… closes only after a write; Discard is remembered like the page plan's (the marks go with the tab), so the marks of the annotations
        // are asked next, and their save never applies the redactions.
        var planDiscarded = false;
        if (redact is not null && tab.RedactIfCreated is { IsActive: true } marking && !marking.Plan.IsEmpty)
        {
            switch (await redact.Prompt(tab))
            {
                case CloseChoice.Save:
                    return await redact.Apply(tab);
                case CloseChoice.DontSave:
                    planDiscarded = true;
                    break;
                default:
                    return false;
            }
        }

        // The tab is dirty while a changed plan is open, but the generic "Save changes?" would be the wrong question for it: the plan has its own.
        if (plan is not null && tab.OrganizeIfCreated is { IsActive: true } open && !open.Plan.IsIdentity)
        {
            switch (await plan.Prompt(tab))
            {
                case CloseChoice.Save:
                    return await plan.Apply(tab);
                case CloseChoice.DontSave:
                    // The plan stays open until the tab is gone (a Cancel below must find it as it was). From here on only the marks decide whether
                    // the generic question is asked, and its save writes the marks alone, never the plan.
                    planDiscarded = true;
                    break;
                default:
                    return false;
            }
        }

        // A discarded plan makes the tab dirty by itself, so then only the marks count.
        if (planDiscarded ? tab.AnnotationsIfCreated is not { IsDirty: true } : !tab.IsDirty)
        {
            return true;
        }

        var saveNow = planDiscarded && (plan?.SaveMarks ?? redact?.SaveMarks) is { } marksOnly ? marksOnly : save;
        return await prompt(tab) switch
        {
            CloseChoice.Save => await saveNow(tab) && !HoldsUnresolvedEdit(tab),
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
