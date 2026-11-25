using Avalonia.Controls;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write.Viewing;

/// <summary>
/// The commands of the page view that act on the text: undo, redo, cut, copy, paste and select all (the window's commands and the keys of <see cref="EditingKeys"/> end here).
/// </summary>
/// <remarks>
/// Rules. Every change goes through <see cref="ApplyGuarded"/> (one history step each, the same failure rule as a key). Undo and redo are operations of the session
/// (<c>History.Undo</c> and <c>History.Redo</c>); the caret afterwards is the one the history recorded (the history keeps a caret, not a replaced selection: after the undo of
/// a replace the caret is at the start of what was replaced). Copy and cut read the selection when the operation runs, under the edit lease, and put the content on the
/// clipboard in one data object after the operation returned (a clipboard write is asynchronous and never runs under the lease). A cut replaces the clipboard only when the delete
/// changed the document (the editor returns the copied content also for a refused delete), and when the clipboard then fails the cut is taken back as long as nothing else
/// changed the document since, so text never vanishes into a clipboard that did not take it. Paste reads the clipboard first (never blocking the UI thread) and applies it afterwards
/// to the selection that is there then: the continuation runs on the UI thread and re-checks the view, the tab and the session (a closed tab, a failed layout or a model that is
/// still opening drop the paste without a trace); during a save the operation is queued like typing. An unreadable clipboard, an empty one or an exception of the system clipboard is
/// "nothing to paste": no change. The fragment is untrusted input, which <see cref="TextEditor.Paste"/> treats as absent when it is unreadable or too large (the plain text is pasted then).
/// </remarks>
internal sealed partial class PageView
{
    private IEditClipboard? _clipboard;

    /// <summary>The clipboard of the edit commands; a test double in tests. Null (the default): the system clipboard of the window this view is in.</summary>
    internal IEditClipboard? EditClipboard
    {
        get => _clipboard;
        set => _clipboard = value;
    }

    private IEditClipboard ClipboardOfView => _clipboard ??= new AvaloniaEditClipboard(() => TopLevel.GetTopLevel(this)?.Clipboard);

    /// <summary>
    /// Runs the command of the edit family (<see cref="WriteCommandIds"/>: undo, redo, cut, copy, paste, select all) and returns true when it was taken. The work of cut, copy and paste
    /// finishes asynchronously (the clipboard); use <see cref="RunEditCommandAsync"/> to wait for it.
    /// </summary>
    internal bool RunEditCommand(string id)
    {
        var task = RunEditCommandAsync(id);
        if (task.IsCompletedSuccessfully)
        {
            return task.Result;
        }

        // The rest runs on: nobody awaits it, so its failure is observed here and nowhere else (a Release build traces only the type, see ApplyGuarded).
        _ = task.ContinueWith(t => System.Diagnostics.Trace.TraceError("An edit command failed: " + t.Exception?.GetBaseException().GetType().Name), TaskContinuationOptions.OnlyOnFaulted);
        return true;
    }

    /// <summary>Runs an edit command and completes when it is done (the clipboard included); true when the command was taken (also when it changed nothing).</summary>
    internal async Task<bool> RunEditCommandAsync(string id)
    {
        if (_released)
        {
            return false;
        }

        switch (id)
        {
            case WriteCommandIds.Undo:
                return RunHistory(undo: true);
            case WriteCommandIds.Redo:
                return RunHistory(undo: false);
            case WriteCommandIds.SelectAll:
                return SelectAll();
            case WriteCommandIds.Copy:
                return await CopyOrCutAsync(cut: false);
            case WriteCommandIds.Cut:
                return await CopyOrCutAsync(cut: true);
            case WriteCommandIds.Paste:
                return await PasteAsync();
            default:
                return false;
        }
    }

    /// <summary>
    /// Runs one operation of the editor as the session's way to change the document (the rules of <see cref="ApplyGuarded"/>): the seam of the commands and of the tests, which
    /// change nothing the keys and commands could not. The session is made on first use. Returns the result, or null when refused, queued, blocked or failed.
    /// </summary>
    internal EditResult? ApplyOperation(Func<TextEditor, EditResult?> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var session = _tab.EnsureEditor();
        AttachSession(session);
        SyncActiveMode();
        return ApplyGuarded(session, operation);
    }

    // The session for a command: made on first use, and null (with the hint) while the whole model is not there or editing failed. A save is no refusal, the callers decide.
    private EditSession? SessionForCommand()
    {
        if (BlockReason() is { } reason and not EditBlockedReason.Saving)
        {
            InputRefused?.Invoke(reason);
            return null;
        }

        var session = _tab.EnsureEditor();
        AttachSession(session);
        SyncActiveMode();
        ResetAffinity();
        _goalFor = null;
        return session;
    }

    private bool RunHistory(bool undo)
    {
        if (SessionForCommand() is null)
        {
            return true;
        }

        ApplyOperation(ed => undo ? ed.History.Undo() : ed.History.Redo());
        return true;
    }

    private async Task<bool> CopyOrCutAsync(bool cut)
    {
        if (SessionForCommand() is not { } session || session.BlockedReason == EditBlockedReason.Saving)
        {
            // A save queues undo, redo and paste, but cut and copy would have to tell the clipboard later what they took: the rules disable them, and a key does nothing.
            return true;
        }

        ClipboardContent? content = null;
        var model = _tab.Docx.Model;
        var result = ApplyOperation(ed =>
        {
            if (session.Selection is not { IsEmpty: false } range)
            {
                return null;
            }

            if (!cut)
            {
                content = ed.Copy(range);
                return null;
            }

            var done = ed.Cut(range);
            content = done.Content;
            return done.Result;
        });

        // A cut whose delete was refused (no result) copies nothing: the clipboard keeps what it had. An empty copy (a selection that holds no text) does not clear it either.
        if (content is null || (cut && result is null) || (content.PlainText.Length == 0 && content.Fragment is null))
        {
            return true;
        }

        var revisionAfterCut = model.Edits.Revision;
        if (!await WriteSafeAsync(ClipboardOfView, content) && cut)
        {
            TakeBackCut(session, model, revisionAfterCut);
        }

        return true;
    }

    // The clipboard did not take a cut: bring the text back, but only while nothing else changed the document (the user may have typed during the write).
    private void TakeBackCut(EditSession session, WordDocument model, int revisionAfterCut)
    {
        if (_released || _tab.IsDisposed || !ReferenceEquals(_tab.Docx.Model, model) || model.Edits.Revision != revisionAfterCut || session.BlockedReason is not null)
        {
            return;
        }

        ApplyOperation(ed => ed.History.Undo());
    }

    private async Task<bool> PasteAsync()
    {
        if (SessionForCommand() is not { } session)
        {
            return true;
        }

        var content = await ReadSafeAsync(ClipboardOfView);
        if (content is { PlainText.Length: 0, ImageRefusal: { } unreadable } && !(content.Fragment is { } readable && EditSession.FragmentReadable(readable)))
        {
            // A picture was offered and could not be handed over (too many pixels, unreadable): nothing is pasted and the user hears why (a fixed wording, nothing of the picture).
            var text = PictureWording.ReasonText(unreadable);
            if (Dispatcher.CheckAccess())
            {
                StructureNote?.Invoke(text);
            }
            else
            {
                await Dispatcher.InvokeAsync(() => StructureNote?.Invoke(text));
            }

            return true;
        }

        if (content is null || (content.PlainText.Length == 0 && !ImageOnly(content) && !(content.Fragment is { } bytes && EditSession.FragmentReadable(bytes))))
        {
            // Nothing usable: no text, no readable fragment and no picture. Pasting "" over a selection would delete it, so nothing happens at all.
            return true;
        }

        // The continuation belongs to the UI thread (the clipboard of the system may finish elsewhere).
        if (!Dispatcher.CheckAccess())
        {
            await Dispatcher.InvokeAsync(() => ApplyPaste(session, content));
            return true;
        }

        ApplyPaste(session, content);
        return true;
    }

    // After the await: the tab may be closed, the session failed or replaced and the selection somewhere else. A closed tab or a failed session drops the paste; otherwise the operation
    // reads the selection when it runs (and is queued during a save).
    private void ApplyPaste(EditSession session, ClipboardContent content)
    {
        if (_released || _tab.IsDisposed || _runner.IsFaulted || !ReferenceEquals(_tab.Editor, session) || session.BlockedReason is EditBlockedReason.Faulted or EditBlockedReason.StillOpening)
        {
            return;
        }

        ResetAffinity();
        _goalFor = null;
        // Own fragment, then text, then a picture: a picture is pasted only when it is all the clipboard holds that can be used. It replaces the selection as text does (one step) and is refused with its reason.
        var picture = ImageOnly(content) ? content.Image : null;
        PictureRefusal? refusal = null;
        var prefix = IsPrefixDocument;
        var result = ApplyOperation(ed =>
        {
            if (session.Selection is not { } selection)
            {
                return null;
            }

            if (picture is { } bytes && (refusal = ed.RefusalForPastePicture(selection, bytes, prefix)) is not null)
            {
                return null;
            }

            var styles = _tab.Docx.Model.Styles;
            return ed.Paste(selection, content, id => styles?.Find(id) is not null);
        });

        // A refused picture says why (a save that queued the paste says nothing now: the refusal is not known yet and a queued paste that fails says nothing either).
        if (result is null && refusal is { } reason && session.BlockedReason is null)
        {
            StructureNote?.Invoke(PictureWording.ReasonText(reason));
        }
    }

    // True when the clipboard holds a picture and nothing else a paste could use: no text and no readable fragment.
    private static bool ImageOnly(ClipboardContent content) =>
        content.Image is not null && content.PlainText.Length == 0 && !(content.Fragment is { } fragment && EditSession.FragmentReadable(fragment));

    private static async Task<bool> WriteSafeAsync(IEditClipboard clipboard, ClipboardContent content)
    {
        try
        {
            return await clipboard.WriteAsync(content);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    private static async Task<ClipboardContent?> ReadSafeAsync(IEditClipboard clipboard)
    {
        try
        {
            return await clipboard.ReadAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}
