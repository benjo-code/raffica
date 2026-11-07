using Raffica.Pdf.Documents;

namespace Raffica.Pdf.Updating;

/// <summary>
/// The question before an update restarts the app: every tab with unsaved work is asked once (Save, Don't save, Cancel; a page plan or redactions
/// that are not applied ask their own card first, as on close), all of it through <see cref="CloseGuard.AskForUpdateAsync"/>, which never closes
/// a tab. The answers are collected first and the saves run afterwards, so a Cancel anywhere keeps every tab as it was and nothing is written
/// before the last answer. Only when every answer is in and every save worked does <c>afterLastAnswer</c> run (the window writes the session
/// of the restart there), and only then may the installer start.
/// </summary>
internal sealed class UpdateInstallGuard(CloseGuard guard)
{
    /// <summary>Gets whether the question is open or its saves run (a second "Install" while it does is ignored).</summary>
    public bool IsAsking { get; private set; }

    /// <summary>
    /// Asks for every tab. Returns <see langword="true"/> only if every tab with unsaved work was answered Save (and saved), Don't save, or was clean;
    /// <see langword="false"/> after a Cancel, a refused or failed save, or while another question is open.
    /// </summary>
    /// <param name="tabs">The tabs of the window, in the order they are asked.</param>
    /// <param name="afterLastAnswer">Runs once, after the last answer and the saves, before this method returns <see langword="true"/>; not at all otherwise.</param>
    public async Task<bool> AskAllTabsAsync(IReadOnlyList<PdfTab> tabs, Action? afterLastAnswer = null)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        if (IsAsking)
        {
            return false;
        }

        IsAsking = true;
        try
        {
            if (!await guard.AskForUpdateAsync([.. tabs]))
            {
                return false;
            }

            afterLastAnswer?.Invoke();
            return true;
        }
        finally
        {
            IsAsking = false;
        }
    }
}
