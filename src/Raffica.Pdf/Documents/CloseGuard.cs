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
/// <c>Cancel</c> keeps the tab.
/// </summary>
internal sealed class CloseGuard(Func<PdfTab, Task<CloseChoice>> prompt, Func<PdfTab, Task<bool>> save)
{
    public async Task<bool> CanCloseAsync(PdfTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        // A mark that is still being made counts: wait for it, so the dirty state is current before it is read.
        if (tab.AnnotationsIfCreated is { } annotations)
        {
            await annotations.FlushAsync();
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
