using Raffica.Pdf.Commands;
using Raffica.Pdf.Documents;
using Raffica.Pdf.TextEditing;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf;

/// <summary>
/// The text edit mode of the window: the commands Edit text (Ctrl+Shift+W) and Done editing work on the text edit session of the active tab (made by the
/// first of them). The mode puts the outlines of the lines over the pages of the tab, the hint bar into the badge slot and the field over a line when the user
/// opens one; leaving it writes an open draft first (a refused one stays, with its reason). Nothing is made or read before the first use: the lines of the pages
/// in view are read when the mode starts, and again when the view moves or the document changes.
/// </summary>
internal sealed partial class PdfWindow
{
    // What exists for a tab only while its text edit mode is on: made when the mode starts, disposed when it ends or the tab goes.
    private readonly Dictionary<PdfTab, TextEditParts> _textEdits = [];
    private bool _textEditStarting;
    private string? _textEditHintText;

    /// <summary>The pieces of the text edit mode of a tab while it is on (for tests); null otherwise.</summary>
    internal TextEditParts? TextEditOf(PdfTab tab) => _textEdits.GetValueOrDefault(tab);

    /// <summary>Starts the text edit mode of the active tab, or leaves it again (an open draft is written first).</summary>
    internal void ToggleTextEdit() => _ = GuardAsync(ToggleTextEditAsync);

    internal async Task ToggleTextEditAsync()
    {
        if (_textEditStarting || Workspace.Active is not { } tab || tab.Session.Document is null || !_views.TryGetValue(tab, out var view))
        {
            return;
        }

        if (tab.TextEditIfCreated is { IsActive: true } open)
        {
            await LeaveTextEditAsync(tab, open);
            return;
        }

        // A running save holds the document; another mode in the way, a document that forbids changes or a page that is not on screen yet refuse the start.
        var modes = new ModeExclusion(mode => ModeIsOnFor(tab, mode));
        if (_saves.IsSaving || !view.FirstSharpPageShown || AnnotateAllowedOf(tab) == false || !modes.CanEnter(PdfMode.TextEdit, out _))
        {
            return;
        }

        // An open field edit and a typed comment of the marks pane are written first, as the other modes do; a value that is refused stays on screen and the mode does not start.
        var editing = FormsOf(tab)?.Interaction.IsEditing == true;
        var typed = MarksOf(tab)?.HasUnappliedComment == true;
        if (editing && !FormsOf(tab)!.Interaction.EndOrReveal())
        {
            return;
        }

        if (editing || typed)
        {
            _textEditStarting = true;
            try
            {
                await tab.Annotations.FlushAsync();
            }
            finally
            {
                _textEditStarting = false;
            }

            if (!Workspace.Tabs.Contains(tab) || !ReferenceEquals(Workspace.Active, tab) || tab.TextEditIfCreated is { IsActive: true }
                || FormsOf(tab)?.Interaction.IsEditing == true || MarksOf(tab)?.HasUnappliedComment == true || !modes.CanEnter(PdfMode.TextEdit, out _))
            {
                return;
            }
        }

        var session = tab.TextEdit;
        session.Enter();
        var parts = BuildTextEditParts(tab, view, session);
        RefreshCommandStates();

        // The sign says "Esc to finish", and the keys of the lines work on the page view: it gets the keyboard now (not a window-wide key capture).
        // The click on the ribbon button may take the focus after this runs, so it is asked once more when the click is over.
        view.Focus();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (tab.TextEditIfCreated is { IsActive: true } && ReferenceEquals(Workspace.Active, tab))
            {
                view.Focus();
            }
        }, Avalonia.Threading.DispatcherPriority.Input);
        await parts.Interaction.LoadVisibleAsync();
    }

    /// <summary>Leaves the text edit mode of the active tab (the Done editing command).</summary>
    internal void FinishTextEdit() => _ = GuardAsync(FinishTextEditAsync);

    internal Task FinishTextEditAsync() =>
        Workspace.Active is { } tab && tab.TextEditIfCreated is { IsActive: true } session ? LeaveTextEditAsync(tab, session) : Task.CompletedTask;

    // Ends the mode: an open draft is written first; a draft that is refused stays open with its reason and the mode stays on.
    private async Task LeaveTextEditAsync(PdfTab tab, TextEditSession session)
    {
        if (session.HasDraft)
        {
            await session.CommitAsync();
            if (session.HasDraft)
            {
                return;
            }
        }

        if (Workspace.Tabs.Contains(tab) && session.IsActive)
        {
            EndTextEdit(tab, session);
        }
    }

    private void EndTextEdit(PdfTab tab, TextEditSession session)
    {
        // The pieces go first (the session raises Ending while it is still on), the sign and the outlines follow the end.
        if (_textEdits.Remove(tab, out var parts))
        {
            DisposeTextEditParts(tab, parts);
        }

        session.End();
        ClearTextEditHint();
        RefreshCommandStates();
        if (_views.TryGetValue(tab, out var view))
        {
            view.Focus();
        }
    }

    private TextEditParts BuildTextEditParts(PdfTab tab, PageView view, TextEditSession session)
    {
        var interaction = new TextEditInteraction(tab, view, session);
        var outlines = new RunOutlines(session);
        var bar = new TextEditHintBar(session);
        view.ModeOverlay = outlines;
        if (_hosts.TryGetValue(tab, out var host))
        {
            host.TextEdit = bar;
        }

        interaction.EndRequested += () => _ = GuardAsync(() => tab.TextEditIfCreated is { IsActive: true } open ? LeaveTextEditAsync(tab, open) : Task.CompletedTask);
        var parts = new TextEditParts(interaction, outlines, bar);
        _textEdits[tab] = parts;
        return parts;
    }

    private void DisposeTextEditParts(PdfTab tab, TextEditParts parts)
    {
        if (_views.TryGetValue(tab, out var view) && ReferenceEquals(view.ModeOverlay, parts.Outlines))
        {
            view.ModeOverlay = null;
        }

        if (_hosts.TryGetValue(tab, out var host) && ReferenceEquals(host.TextEdit, parts.Bar))
        {
            host.TextEdit = null;
        }

        parts.Dispose();
    }

    // The first save of edited text of a tab: the hint bar says where the old text stays, and so does the status bar (once per tab). A hint of another owner is never overwritten.
    private void OnTextEditsSaved(PdfTab tab)
    {
        tab.TextEditIfCreated?.ShowOldTextNote();
        if (ReferenceEquals(Workspace.Active, tab) && string.IsNullOrEmpty(Status.Hint))
        {
            Status.Hint = TextEditWording.OldText;
            _textEditHintText = TextEditWording.OldText;
        }
    }

    // The cell of the status bar is ours only while it holds the text this feature wrote.
    private void ClearTextEditHint()
    {
        if (_textEditHintText is not null)
        {
            if (Status.Hint == _textEditHintText)
            {
                Status.Hint = null;
            }

            _textEditHintText = null;
        }
    }

    /// <summary>The pieces of the text edit mode of one tab.</summary>
    internal sealed class TextEditParts(TextEditInteraction interaction, RunOutlines outlines, TextEditHintBar bar) : IDisposable
    {
        public TextEditInteraction Interaction { get; } = interaction;

        public RunOutlines Outlines { get; } = outlines;

        public TextEditHintBar Bar { get; } = bar;

        public void Dispose()
        {
            Interaction.Dispose();
            Outlines.Dispose();
            Bar.Dispose();
        }
    }
}
