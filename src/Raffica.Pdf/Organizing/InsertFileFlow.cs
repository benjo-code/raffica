using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;

namespace Raffica.Pdf.Organizing;

/// <summary>
/// "Insert file" of the Pages tab: the file picker, the file opened once for its page count (a password is asked as often as needed; a cancel
/// after a wrong one ends in the refusal), the insert card, then <see cref="OrganizeSession.InsertFileAsync"/>. Whatever the session refuses, and
/// whatever stops the flow before it, is told with its reason and changes nothing; a cancel anywhere changes nothing and says nothing.
/// The window gives the pieces as delegates (the system picker, the cards), the tests their own.
/// </summary>
/// <param name="pickFile">Asks for a file; null: the user canceled.</param>
/// <param name="askPassword">Asks for the password of a file (name, whether the last one was wrong); null: the user canceled.</param>
/// <param name="askChoice">Shows the insert card (file name, its page count, the text of the "after the selection" choice or null); null: canceled.</param>
/// <param name="refuse">Tells the user why nothing was inserted.</param>
/// <param name="engine">The PDF engine.</param>
internal sealed class InsertFileFlow(
    Func<Task<string?>> pickFile,
    Func<string, bool, Task<string?>> askPassword,
    Func<string, int, string?, Task<InsertChoice?>> askChoice,
    Func<OrganizeResult, Task> refuse,
    Func<PdfEngine> engine)
{
    /// <summary>A test seam: called on the thread that opens the file for its page count (never the UI thread).</summary>
    internal Action? OpenProbe { get; set; }

    /// <summary>Runs the flow for the plan that is open on <paramref name="tab"/>.</summary>
    /// <returns>The result of the insertion (a refusal was shown), or null when the user canceled or the mode ended on the way.</returns>
    public async Task<OrganizeResult?> RunAsync(PdfTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (tab.OrganizeIfCreated is not { IsActive: true } session)
        {
            return null;
        }

        var path = await pickFile();
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        // Whatever goes wrong from here on ends in a card with the general reason, never in silence.
        try
        {
            return await RunCoreAsync(tab, session, path);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            return await TellAsync(OrganizeResult.Refused(OrganizeProblem.Unreadable, ex.Message), general: true);
        }
    }

    private async Task<OrganizeResult?> RunCoreAsync(PdfTab tab, OrganizeSession session, string path)
    {

        // The refusals that need no look into the file come first, so nobody is asked for pages of a file that cannot be used.
        if (Early(tab, session, path) is { } early)
        {
            return await TellAsync(early);
        }

        string? password = null;
        var incorrect = false;
        int pageCount;
        while (true)
        {
            try
            {
                pageCount = await CountPagesAsync(path, password);
                break;
            }
            catch (PdfOpenException ex) when (ex.Error is PdfOpenError.PasswordRequired or PdfOpenError.PasswordIncorrect)
            {
                password = await askPassword(Path.GetFileName(path), incorrect);
                if (password is null)
                {
                    return incorrect ? await TellAsync(OrganizeResult.Refused(OrganizeProblem.WrongPassword)) : null;
                }

                incorrect = true;
            }
            catch (PdfOpenException ex)
            {
                return await TellAsync(OrganizeResult.Refused(OrganizeProblem.Unreadable, ex.Message));
            }
        }

        // A file without pages has nothing to insert (and no range to ask for).
        if (pageCount < 1)
        {
            return await TellAsync(OrganizeResult.Refused(OrganizeProblem.Unreadable));
        }

        // The mode may end, and the selection change, while the cards are open: both are read again after the card.
        var selected = session.IsActive ? session.Selection.Positions : [];
        var choice = await askChoice(Path.GetFileName(path), pageCount, selected.Count > 0 ? PdfStrings.Fill(PdfStrings.InsertAfterFormat, selected[^1] + 1) : null);
        if (choice is not { } chosen || !session.IsActive)
        {
            return null;
        }

        var now = session.Selection.Positions;
        var at = chosen.Position switch
        {
            InsertPosition.AtStart => 0,
            InsertPosition.AfterSelection when now.Count > 0 => now[^1] + 1,
            _ => session.Plan.Slots.Count,
        };
        var result = await session.InsertFileAsync(path, password, chosen.Range, at);
        return result.IsDone ? result : await TellAsync(result);
    }

    private static OrganizeResult? Early(PdfTab tab, OrganizeSession session, string path)
    {
        try
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (tab.Path is { } own && string.Equals(Path.GetFullPath(own), Path.GetFullPath(path), comparison))
            {
                return OrganizeResult.Refused(OrganizeProblem.SameFile);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return OrganizeResult.Refused(OrganizeProblem.Unreadable, ex.Message);
        }

        return session.Inserted.Count >= PageEditLimits.MaxInsertedFiles ? OrganizeResult.Refused(OrganizeProblem.TooManyFiles) : null;
    }

    // Opens the file only to count its pages (off the UI thread); the session opens it again for the insertion.
    private async Task<int> CountPagesAsync(string path, string? password)
    {
        var pdf = engine();
        return await Task.Run(async () =>
        {
            OpenProbe?.Invoke();
            using var document = await pdf.OpenAsync(path, password).ConfigureAwait(false);
            return document.PageCount;
        });
    }

    // general: the cause is unexpected, so the card says the pages could not be inserted (the result keeps the cause text).
    private async Task<OrganizeResult> TellAsync(OrganizeResult result, bool general = false)
    {
        var told = general ? OrganizeResult.Refused(OrganizeProblem.OutOfRange, result.Detail) : result;
        await refuse(told);
        return told;
    }
}
