namespace Raffica.Write.Documents;

/// <summary>What the user chose when asked about a document with unsaved changes.</summary>
internal enum CloseChoice
{
    Save,
    DontSave,
    Cancel,
}

/// <summary>
/// Decides whether a tab may close, in one place for every way a tab goes: the close button and command, the close of the window, and a quit (Cmd+Q through the host).
/// A clean tab closes at once; a tab with unsaved changes asks once. <c>Save</c> closes only if the save worked and nothing is unsaved again (what was typed while the file
/// was written is queued and replayed after the save, so it would be lost with the tab); <c>Don't save</c> closes; <c>Cancel</c> keeps the tab. A save that is already running
/// is waited for first (it is never discarded), and its end decides whether there is still something to ask. The guard never closes a tab itself.
/// </summary>
/// <remarks>
/// The PDF app's <c>CloseGuard</c> is a near neighbour (the same three answers, the same order for several tabs) but is written around its own modes (plan, redaction,
/// recognition) and tab type, so Write has its own small class. What could be shared once a second app needs it: the loop over several tabs and the "ask, then save, then check
/// that nothing is unsaved again" rule, behind an interface of the tab (<c>IsDirty</c>, <c>Title</c>); the question card and the dialog-queue plumbing of <c>AskAsync</c> are the same too.
/// </remarks>
/// <param name="prompt">Asks the question for a tab (the window selects the tab, brings itself to the front and shows <c>UnsavedChangesCard</c> through its dialog queue).</param>
/// <param name="save">Saves the tab (with every error shown to the user already) and says whether it worked.</param>
/// <param name="whenIdle">Completes when no save of the tab runs (null: no save can run).</param>
internal sealed class CloseGuard(Func<WriteTab, Task<CloseChoice>> prompt, Func<WriteTab, Task<bool>> save, Func<WriteTab, Task>? whenIdle = null)
{
    public async Task<bool> CanCloseAsync(WriteTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (whenIdle is not null)
        {
            await whenIdle(tab);
        }

        if (!tab.IsDirty)
        {
            return true;
        }

        return await prompt(tab) switch
        {
            CloseChoice.Save => await save(tab) && !tab.IsDirty,
            CloseChoice.DontSave => true,
            _ => false,
        };
    }

    /// <summary>Asks for every tab in turn, in the order given, and stops at the first one that must stay.</summary>
    public async Task<bool> CanCloseAllAsync(IEnumerable<WriteTab> tabs)
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
