using Raffica.Core.Pdf;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Redacting;
using Raffica.Pdf.Viewing;
using Raffica.Shell.Frame;

namespace Raffica.Pdf;

/// <summary>
/// The commands of the Redact tab that act on the marks: they work on the redact session of the active tab (made by the first of them). The mode
/// puts the redact pane into the right slot, the badge over the page area and the marks over the pages; leaving it with marks asks first
/// (Apply…, Discard, Cancel). The Find panel opens with its own command, "Turn page into image" toggles the current page, and Apply runs the redaction flow (the Apply command, the pane
/// button, Save and Save as with marks, the close question).
/// </summary>
internal sealed partial class PdfWindow
{
    // What exists for a tab only while its redact mode is on: made when the mode starts, disposed when it ends or the tab goes.
    private readonly Dictionary<PdfTab, RedactParts> _redacts = [];
    private bool _redactStarting;

    // The session of the active tab while its mode is on, else null.
    private RedactSession? OpenRedact => Workspace.Active?.RedactIfCreated is { IsActive: true } session ? session : null;

    /// <summary>The pieces of the redact mode of a tab while it is on (for tests); null otherwise.</summary>
    internal RedactParts? RedactOf(PdfTab tab) => _redacts.GetValueOrDefault(tab);

    /// <summary>The badge of the redact mode of a tab while it is on (for tests); null otherwise.</summary>
    internal RedactModeBadge? RedactBadgeOf(PdfTab tab) => _redacts.GetValueOrDefault(tab)?.Badge;

    /// <summary>Starts the redact mode of the active tab, or leaves it again (with the question when marks are not applied).</summary>
    internal void ToggleRedact() => _ = GuardAsync(ToggleRedactAsync);

    internal async Task ToggleRedactAsync()
    {
        if (_redactStarting || Workspace.Active is not { } tab || tab.Session.Document is null || !_views.TryGetValue(tab, out var view))
        {
            return;
        }

        if (tab.RedactIfCreated is { IsActive: true } open)
        {
            await LeaveRedactAsync(tab, open);
            return;
        }

        // A running save holds the document (the incremental one is not modal): the mode starts after it. Another mode in the way refuses it.
        var modes = new ModeExclusion(mode => ModeIsOnFor(tab, mode));
        if (_saves.IsSaving || !view.FirstSharpPageShown || !modes.CanEnter(PdfMode.Redact, out _))
        {
            return;
        }

        // An open field edit and a typed comment of the marks pane are written first (the file is untouched by the mode itself, but the tools
        // that held them are locked from here on); a value that is refused stays on screen with its reason and the mode does not start.
        var editing = FormsOf(tab)?.Interaction.IsEditing == true;
        var typed = MarksOf(tab)?.HasUnappliedComment == true;
        if (editing && !FormsOf(tab)!.Interaction.EndOrReveal())
        {
            return;
        }

        if (editing || typed)
        {
            _redactStarting = true;
            try
            {
                await tab.Annotations.FlushAsync();
            }
            finally
            {
                _redactStarting = false;
            }

            if (!Workspace.Tabs.Contains(tab) || !ReferenceEquals(Workspace.Active, tab) || tab.RedactIfCreated is { IsActive: true }
                || FormsOf(tab)?.Interaction.IsEditing == true || MarksOf(tab)?.HasUnappliedComment == true || !modes.CanEnter(PdfMode.Redact, out _))
            {
                return;
            }
        }

        var session = tab.Redact;
        session.Enter();
        BuildRedactParts(tab, view, session);
        UpdateMarksPane();
        RefreshCommandStates();

        // The sign says "Esc to finish", and the keys of the marks work on the page view: it gets the keyboard now (not a window-wide key capture).
        // The click on the ribbon button may take the focus after this runs, so it is asked once more when the click is over.
        view.Focus();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (tab.RedactIfCreated is { IsActive: true } && ReferenceEquals(Workspace.Active, tab))
            {
                view.Focus();
            }
        }, Avalonia.Threading.DispatcherPriority.Input);
    }

    /// <summary>Drops the redactions of the active tab and leaves the mode (the Discard command): the question when marks are not applied.</summary>
    internal void DiscardRedact() => _ = GuardAsync(DiscardRedactAsync);

    internal Task DiscardRedactAsync() =>
        Workspace.Active is { } tab && tab.RedactIfCreated is { IsActive: true } session ? LeaveRedactAsync(tab, session) : Task.CompletedTask;

    // Ends the mode: silently when nothing is marked, else after the question. Apply… runs the redaction flow (the mode ends with its success),
    // Discard ends it, Cancel keeps mode and marks.
    private async Task LeaveRedactAsync(PdfTab tab, RedactSession session)
    {
        if (!session.Plan.IsEmpty)
        {
            switch (await AskRedactAsync(tab))
            {
                case CloseChoice.Save:
                    await Redacting.RunAsync(tab);
                    RefreshCommandStates();
                    return;
                case CloseChoice.DontSave:
                    break;
                default:
                    return;
            }
        }

        if (Workspace.Tabs.Contains(tab) && session.IsActive)
        {
            EndRedact(tab, session);
        }
    }

    private RedactApplyFlow? _redactFlow;

    /// <summary>The redaction apply flow of the window (made on first use; for tests).</summary>
    internal RedactApplyFlow Redacting => _redactFlow ??= new RedactApplyFlow(
        Dialogs,
        tab => _saves.SaveMarksAsync(tab),
        PickRedactPathAsync,
        () => _engine ??= new PdfEngine(),
        tab => _views.TryGetValue(tab, out var view) ? view.CurrentPage : 0,
        RebuildTab,
        OnSavedUnderNewPath,
        RefreshCommandStates,
        () => _print.IsRunning,
        _time,
        () => _saves.IsSaving || _applyFlow?.IsRunning == true || _outputFlow?.IsRunning == true || _exportFlow?.IsRunning == true);

    /// <summary>Gets whether the redaction apply flow exists (it is made by the first Apply; for tests).</summary>
    internal bool RedactFlowCreated => _redactFlow is not null;

    // The target of applied redactions: never the name of the open file, so that a quick Enter cannot replace it.
    private Task<string?> PickRedactPathAsync(PdfTab tab) =>
        PickPathAsync(tab, PdfStrings.Fill(PdfStrings.RedactApplySuggestedNameFormat, Path.GetFileNameWithoutExtension(tab.Title)));

    /// <summary>Applies the redactions of the active tab (the Apply command, the pane button; Save and Save as with marks come here too).</summary>
    internal void ApplyRedact() => _ = GuardAsync(ApplyRedactAsync);

    internal Task<bool> ApplyRedactAsync() => Workspace.Active is { } tab ? Redacting.RunAsync(tab) : Task.FromResult(false);

    private void EndRedact(PdfTab tab, RedactSession session)
    {
        // The pieces go first (the session raises Ending while it is still on), the right slot follows the end.
        if (_redacts.Remove(tab, out var parts))
        {
            DisposeRedactParts(tab, parts);
        }

        session.End();
        UpdateMarksPane();
        RefreshCommandStates();
        if (_views.TryGetValue(tab, out var view))
        {
            view.Focus();
        }
    }

    /// <summary>Marks the selected text of the active tab (the Mark selection command and Ctrl+Enter).</summary>
    internal void MarkRedactSelection() => _ = GuardAsync(MarkRedactSelectionAsync);

    internal Task MarkRedactSelectionAsync() =>
        Workspace.Active is { } tab && tab.RedactIfCreated is { IsActive: true } && _redacts.TryGetValue(tab, out var parts) ? parts.Interaction.MarkSelection() : Task.CompletedTask;

    /// <summary>Opens the Find panel of the redact pane of the active tab, or closes it (the Find command). Nothing is made before the first toggle.</summary>
    internal void ToggleRedactFind()
    {
        if (Workspace.Active is { } tab && tab.RedactIfCreated is { IsActive: true } && _redacts.TryGetValue(tab, out var parts))
        {
            parts.Pane.ToggleFind();
        }
    }

    /// <summary>Turns the page the view shows into an image, or back (the Turn page into image command); a refusal ends in the note of the pane.</summary>
    internal void ToggleRedactRasterPage()
    {
        if (Workspace.Active is { } tab && tab.RedactIfCreated is { IsActive: true } session && _redacts.TryGetValue(tab, out var parts) && _views.TryGetValue(tab, out var view))
        {
            var page = view.CurrentPage;
            parts.Pane.Report(session.SetRasterPage(page, !session.Plan.RasterPages.Contains(page)));
        }
    }

    /// <summary>The ribbon command with an id (for the offers of the panes); null when the ribbon is not built.</summary>
    private CommandDefinition? RibbonCommand(string id) =>
        Ribbon.Definition?.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).FirstOrDefault(c => c.Id == id);

    /// <summary>Removes the selected marks of the active tab (the Delete marks command).</summary>
    internal void DeleteRedactMarks()
    {
        if (Workspace.Active is { } tab && tab.RedactIfCreated is { IsActive: true } && _redacts.TryGetValue(tab, out var parts))
        {
            parts.Pane.DeleteSelected();
        }
    }

    private void BuildRedactParts(PdfTab tab, PageView view, RedactSession session)
    {
        var text = _texts[tab];
        var interaction = new RedactInteraction(tab, view, session, text.Selection, _time);
        var overlay = new RedactOverlay(session);
        var badge = new RedactModeBadge(session);
        var pane = new RedactPane(session, view) { OcrCommand = () => RibbonCommand(PdfCommandIds.OcrRun) };
        view.ModeOverlay = overlay;
        if (_hosts.TryGetValue(tab, out var host))
        {
            host.Redact = badge;
        }

        interaction.Refused += pane.Report;
        pane.ApplyRequested += ApplyRedact;
        interaction.EndRequested += () => _ = GuardAsync(() => tab.RedactIfCreated is { IsActive: true } open ? LeaveRedactAsync(tab, open) : Task.CompletedTask);
        session.Changed += RefreshCommandStates;
        _redacts[tab] = new RedactParts(interaction, overlay, badge, pane);
    }

    private void DisposeRedactParts(PdfTab tab, RedactParts parts)
    {
        if (tab.RedactIfCreated is { } session)
        {
            session.Changed -= RefreshCommandStates;
        }

        if (_views.TryGetValue(tab, out var view) && ReferenceEquals(view.ModeOverlay, parts.Overlay))
        {
            view.ModeOverlay = null;
        }

        if (_hosts.TryGetValue(tab, out var host) && ReferenceEquals(host.Redact, parts.Badge))
        {
            host.Redact = null;
        }

        parts.Dispose();
    }

    /// <summary>The pieces of the redact mode of one tab.</summary>
    internal sealed class RedactParts(RedactInteraction interaction, RedactOverlay overlay, RedactModeBadge badge, RedactPane pane) : IDisposable
    {
        public RedactInteraction Interaction { get; } = interaction;

        public RedactOverlay Overlay { get; } = overlay;

        public RedactModeBadge Badge { get; } = badge;

        public RedactPane Pane { get; } = pane;

        public void Dispose()
        {
            Interaction.Dispose();
            Overlay.Dispose();
            Badge.Dispose();
            Pane.Dispose();
        }
    }
}
