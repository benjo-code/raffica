using Avalonia.Platform.Storage;
using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Organizing;

namespace Raffica.Pdf;

/// <summary>
/// The commands of the Pages tab: they act on the organize session of the active tab (made by the first of them) and on its selection. The
/// session raises <see cref="OrganizeSession.Changed"/>, which refreshes the ribbon; the flows behind Apply, Discard, Insert file and the
/// output commands arrive with their own tasks and stay unwired until then.
/// </summary>
internal sealed partial class PdfWindow
{
    // A4 in points, for a blank page when no neighbour size is known yet.
    private static readonly BlankPage FallbackBlank = new(595.28, 841.89);

    // The session of the active tab while its plan is open (the mode is on, whether the plan changes anything or not), else null.
    private OrganizeSession? OpenPlan => Workspace.Active?.OrganizeIfCreated is { IsActive: true } session ? session : null;

    private static bool PlanOpenOf(PdfTab tab) => tab.OrganizeIfCreated is { IsActive: true };

    private bool _organizeStarting;

    private void OnOrganizeCreated(OrganizeSession session) => session.Changed += RefreshCommandStates;

    /// <summary>Enters the organize mode of the active tab and opens the pages pane, or leaves it again when the plan changes nothing.</summary>
    internal void ToggleOrganize() => _ = GuardAsync(ToggleOrganizeAsync);

    private async Task ToggleOrganizeAsync()
    {
        if (_organizeStarting || Workspace.Active is not { } tab || tab.Session.Document is null)
        {
            return;
        }

        var session = tab.Organize;
        if (session.IsActive)
        {
            // The mode is left only with a plan that changes nothing. A plan with changes keeps the mode: leaving it means to apply or to
            // discard them, and that three-way question comes with the Apply and Discard flows.
            if (session.Plan.IsIdentity)
            {
                session.End();
            }

            RefreshCommandStates();
            return;
        }

        // The annotate tools are locked while a plan is open: the mode ends them first. So do an open field edit and a typed comment of the
        // marks pane: a value that is valid is written first (the marks are then unsaved, which Apply asks about), one that is refused stays on
        // screen with its reason and the mode does not start. Nothing of the marks changes while the plan is open.
        Tools.End();
        var editing = FormsOf(tab)?.Interaction.IsEditing == true;
        var typed = MarksOf(tab)?.HasUnappliedComment == true;
        if (editing && !FormsOf(tab)!.Interaction.EndOrReveal())
        {
            return;
        }

        if (editing || typed)
        {
            _organizeStarting = true;
            try
            {
                await tab.Annotations.FlushAsync();
            }
            finally
            {
                _organizeStarting = false;
            }

            if (!Workspace.Tabs.Contains(tab) || !ReferenceEquals(Workspace.Active, tab) || session.IsActive
                || FormsOf(tab)?.Interaction.IsEditing == true || MarksOf(tab)?.HasUnappliedComment == true)
            {
                return;
            }
        }

        session.Enter();
        ShowPagesPane();
        RefreshCommandStates();
    }

    private void RunPageEdit(Func<OrganizeSession, OrganizeResult> edit)
    {
        if (OpenPlan is not { } session || Workspace.Active is not { } tab)
        {
            return;
        }

        var result = edit(session);
        if (!result.IsDone)
        {
            // The refusal is said in the list of the pages pane: show the pane first (the outline or nothing may be in the slot).
            ShowPagesPane();
            if (_panes.TryGetValue(tab, out var panes))
            {
                panes.Pages.Organize?.Report(result);
            }
        }
    }

    /// <summary>Replaces the system file dialog of "Insert file" (tests): returns the chosen path, null when the user canceled. Null: the system dialog.</summary>
    internal Func<Task<string?>>? InsertFilePicker { get; set; }

    private InsertFileFlow? _insertFlow;

    private InsertFileFlow InsertFlow => _insertFlow ??= new InsertFileFlow(
        () => InsertFilePicker is { } picker ? picker() : PickInsertFileAsync(),
        AskPasswordAsync,
        (name, count, after) => Dialogs.ShowAsync<InsertChoice?>(done => InsertFileCard.Create(name, count, after, done)),
        result => Dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.InsertRefusedTitle, PdfStrings.InsertRefusal(result), () => done(true))),
        () => _engine ??= new PdfEngine());

    private ApplyFlow? _applyFlow;

    /// <summary>The apply flow of the window (made on first use; for tests).</summary>
    internal ApplyFlow Applying => _applyFlow ??= new ApplyFlow(
        Dialogs,
        tab => _saves.SaveMarksAsync(tab),
        PickApplyPathAsync,
        () => _engine ??= new PdfEngine(),
        tab => _views.TryGetValue(tab, out var view) ? view.CurrentPage : 0,
        RebuildTab,
        OnSavedUnderNewPath,
        RefreshCommandStates,
        () => _print.IsRunning,
        _time);

    /// <summary>Applies the page plan of the active tab (the Apply command; Save and Save as with an open plan come here too).</summary>
    internal Task<bool> ApplyActiveAsync() => Workspace.Active is { } tab ? Applying.RunAsync(tab) : Task.FromResult(false);

    private bool _insertRunning;

    /// <summary>Asks for a PDF and inserts pages of it into the open plan (the position and the pages are asked on a card).</summary>
    internal void InsertFile() => _ = GuardAsync(InsertFileAsync);

    internal async Task InsertFileAsync()
    {
        // A held key or two clicks must not pile up pickers and cards.
        if (_insertRunning || OpenPlan is null || Workspace.Active is not { } tab)
        {
            return;
        }

        _insertRunning = true;
        try
        {
            var result = await InsertFlow.RunAsync(tab);
            if (result is { IsDone: true } && _panes.TryGetValue(tab, out var panes))
            {
                panes.Pages.Organize?.Report(result.Value);
            }
        }
        finally
        {
            _insertRunning = false;
        }
    }

    private async Task<string?> PickInsertFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = PdfStrings.InsertFilePickTitle,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(PdfStrings.OpenFilterPdf)
                {
                    Patterns = ["*.pdf", "*.PDF"],
                    MimeTypes = ["application/pdf"],
                    AppleUniformTypeIdentifiers = ["com.adobe.pdf"],
                },
            ],
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    // After the last selected page, else at the end.
    private void InsertBlankPage() => RunPageEdit(session =>
    {
        var selected = session.Selection.Positions;
        var at = selected.Count > 0 ? selected[^1] + 1 : session.Plan.Slots.Count;
        return session.InsertBlank(at, BlankSizeAt(session, at));
    });

    // The new page takes the size of the page before it (the first page for a position at the start), as the document has it: the extra rotation
    // of that page is deliberately not copied. A page of an inserted file or one whose size is not read yet counts as unknown; the first page, then A4, stands in.
    private BlankPage BlankSizeAt(OrganizeSession session, int at)
    {
        var slots = session.Plan.Slots;
        if (Workspace.Active is { } tab && _views.TryGetValue(tab, out var view) && slots.Count > 0)
        {
            var neighbour = slots[Math.Clamp(at - 1, 0, slots.Count - 1)];
            if (neighbour.Blank is { } blank)
            {
                return blank;
            }

            var size = (neighbour.Source.Value == 0 ? view.PageSizeOf(neighbour.SourceIndex) : null) ?? view.FirstPageSize;
            if (size is { } known)
            {
                return new BlankPage(known.Width, known.Height);
            }
        }

        return FallbackBlank;
    }
}
