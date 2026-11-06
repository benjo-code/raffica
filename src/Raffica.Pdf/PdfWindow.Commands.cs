using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Core.Printing;
using Raffica.Core.Signatures;
using Raffica.Core.Viewing;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Platform;
using Raffica.Pdf.Printing;
using Raffica.Pdf.Signing;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;

namespace Raffica.Pdf;

/// <summary>
/// The commands of the window: what each command of <see cref="PdfCommandCatalog"/> does, when it may run (<see cref="PdfCommandRules"/>)
/// and the window-wide shortcuts. The catalog is the one list; the ribbon, the field codes, the search of the title bar and the keys all
/// run the same <see cref="CommandDefinition"/>.
/// </summary>
internal sealed partial class PdfWindow
{
    private readonly Dictionary<PdfTab, PdfPrintInfo> _printInfo = [];
    private readonly Dictionary<PdfTab, bool> _annotateAllowed = [];
    private readonly Dictionary<PdfTab, bool> _fillAllowed = [];
    private readonly PdfCommandRules _rules;
    private readonly PrintCommand _print;
    private IPrintService? _printService;
    private bool _printProbed;
    private bool _printAvailable;
    private readonly HashSet<Key> _keysDown = [];
    private SignaturePlacer? _signaturePlacer;
    private bool _signRunning;
    private Dictionary<string, CommandDefinition> _commandsById = [];

    /// <summary>The command modifier of the platform: Cmd on macOS, Ctrl elsewhere (what the frame uses for Ctrl+K).</summary>
    private static KeyModifiers CommandModifier =>
        Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyNames.CommandModifier;

    /// <summary>The tool model of the annotate mode: the active tool and the colours and widths the tools remember (in the viewer settings).</summary>
    internal ToolState Tools { get; }

    /// <summary>Raised when Save runs (Ctrl+S or the command), just before the save starts.</summary>
    internal event Action? SaveRequested;

    /// <summary>Raised when Save as runs, just before the dialog opens.</summary>
    internal event Action? SaveAsRequested;

    private PdfCommandRules CreateRules() => new(
        () => Workspace.Active is not null,
        () => Workspace.Tabs.Count,
        () => Workspace.Active is { } active && _printInfo.TryGetValue(active, out var info) ? info : null,
        PrintAvailable,
        isDirty: () => Workspace.Active is { IsDirty: true },
        canUndo: () => OpenPlan is { } plan ? plan.CanUndo : OpenRedact is { } redact ? redact.CanUndo : Workspace.Active?.Annotations.CanUndo == true,
        canRedo: () => OpenPlan is { } plan ? plan.CanRedo : OpenRedact is { } redact ? redact.CanRedo : Workspace.Active?.Annotations.CanRedo == true,
        busySaving: () => _print.IsRunning || _saves.IsSaving || _applyFlow?.IsRunning == true || _outputFlow?.IsRunning == true || _exportFlow?.IsRunning == true,
        annotateAllowed: () => Workspace.Active is not { } active || AnnotateAllowedOf(active) != false,
        hasSelection: () => Workspace.Active is { } active && AnnotateOf(active)?.Select.HasSelection == true,
        saveRunning: () => _saves.IsSaving || _applyFlow?.IsRunning == true || _outputFlow?.IsRunning == true || _exportFlow?.IsRunning == true,
        hasForm: () => Workspace.Active is { } active && TabHasForm(active),
        fillAllowed: () => Workspace.Active is { } active ? FillAllowedOf(active) : null,
        hasEmptyRequired: () => Workspace.Active is not { } active || active.FormsIfCreated is not { } forms || !forms.RequiredKnown || forms.EmptyRequired.Count > 0,
        pagesReady: () => Workspace.Active is { } active && _views.TryGetValue(active, out var view) && view.FirstSharpPageShown,
        organizeActive: () => OpenPlan is not null,
        pageSelection: () => OpenPlan is { } plan && plan.Selection.Count > 0,
        planCanUndo: () => OpenPlan?.CanUndo == true,
        planCanRedo: () => OpenPlan?.CanRedo == true,
        planChanged: () => OpenPlan is { } plan && !plan.Plan.IsIdentity,
        marksUnsaved: () => Workspace.Active?.AnnotationsIfCreated?.IsDirty == true,
        modes: new ModeExclusion(ModeIsOn));

    // Which modes are on for the active tab: the annotate tool is the window's, the others are the sessions of the tab (none is made by asking).
    private bool ModeIsOn(PdfMode mode) => ModeIsOnFor(Workspace.Active, mode);

    private bool ModeIsOnFor(PdfTab? tab, PdfMode mode) => mode switch
    {
        PdfMode.Annotate => Tools.Tool != AnnotateTool.None,
        PdfMode.Organize => tab?.OrganizeIfCreated is { IsActive: true },
        PdfMode.Redact => tab?.RedactIfCreated?.IsActive == true,
        PdfMode.Ocr => tab?.OcrIfCreated?.IsActive == true,
        PdfMode.TextEdit => tab?.TextEditIfCreated?.IsActive == true,
        _ => false,
    };

    // The rules ask for every tab, with or without a form: a document without an AcroForm creates no form session (and its subscription) for it.
    private static bool TabHasForm(PdfTab tab)
    {
        try
        {
            return tab.FormsIfCreated?.HasForm ?? tab.Session.Document is { HasAcroForm: true };
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>Whether the permissions of a tab allow changing annotations, once they were read (null before: the tools are not refused yet).</summary>
    internal bool? AnnotateAllowedOf(PdfTab tab) => _annotateAllowed.TryGetValue(tab, out var allowed) ? allowed : null;

    /// <summary>Whether the permissions of the document allow filling in forms; <see langword="null"/> until they are read.</summary>
    internal bool? FillAllowedOf(PdfTab tab) => _fillAllowed.TryGetValue(tab, out var allowed) ? allowed : null;

    // Whether the system can print is not asked while the first page is on its way (on Linux that loads libcups): until the first sharp page
    // has been seen Print counts as available, then the answer is asked once and kept.
    private bool PrintAvailable() => !_printProbed || _printAvailable;

    private void ProbePrint()
    {
        if (_printProbed)
        {
            return;
        }

        try
        {
            _printAvailable = _print.IsAvailable;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _printAvailable = false;
        }

        _printProbed = true;
        RefreshCommandStates();
    }

    /// <summary>What the permissions of a tab say about printing, once they were read (for tests); null before.</summary>
    internal PdfPrintInfo? PrintInfoOf(PdfTab tab) => _printInfo.TryGetValue(tab, out var info) ? info : null;

    private PdfCommands CreateCommands() => new(
        Open: () => _ = GuardAsync(OpenFromDialogAsync),
        Print: () => _ = GuardAsync(_print.RunAsync),
        CloseTab: () => _ = GuardAsync(CloseActiveTabAsync),
        Search: ShowSearchBar,
        SearchNext: SearchNext,
        SearchPrevious: SearchPrevious,
        GoToPage: () => _ = GuardAsync(GoToPageAsync),
        PreviousPage: () => MovePage(-1),
        NextPage: () => MovePage(1),
        FirstPage: () => MovePage(int.MinValue),
        LastPage: () => MovePage(int.MaxValue),
        NextTab: () => Workspace.SelectNext(),
        PreviousTab: () => Workspace.SelectPrevious(),
        SelectAll: SelectAll,
        Copy: () => _ = GuardAsync(CopySelectionAsync),
        Help: () => _ = GuardAsync(ShowHelpAsync),
        ZoomIn: () => ActiveView?.ZoomIn(),
        ZoomOut: () => ActiveView?.ZoomOut(),
        FitWidth: () => ActiveView?.SetZoomMode(ZoomMode.FitWidth),
        FitPage: () => ActiveView?.SetZoomMode(ZoomMode.FitPage),
        ActualSize: () => ActiveView?.SetZoomMode(ZoomMode.ActualSize),
        Continuous: () => ActiveView?.SetLayout(PageLayoutMode.Continuous),
        SinglePage: () => ActiveView?.SetLayout(PageLayoutMode.SinglePage),
        TwoPages: () => ActiveView?.SetLayout(PageLayoutMode.TwoPages),
        TwoPagesCover: () => ActiveView?.SetLayout(PageLayoutMode.TwoPagesWithCover),
        Settings: () => Settings.Open(),
        DarkPage: ToggleDarkPage,
        PagesPane: ShowPagesPane,
        OutlinePane: ShowOutlinePane,
        Save: () =>
        {
            SaveRequested?.Invoke();
            _ = GuardAsync(SaveActiveAsync);
        },
        SaveAs: () =>
        {
            SaveAsRequested?.Invoke();
            _ = GuardAsync(SaveActiveAsAsync);
        },
        Undo: () => _ = GuardAsync(UndoActiveAsync),
        Redo: () => _ = GuardAsync(RedoActiveAsync),
        AnnotateMode: () =>
        {
            if (Tools.Tool == AnnotateTool.None)
            {
                Tools.Choose(AnnotateTool.Select);
            }
            else
            {
                Tools.End();
            }
        },
        Highlight: () => Tools.Choose(AnnotateTool.Highlight),
        Underline: () => Tools.Choose(AnnotateTool.Underline),
        StrikeOut: () => Tools.Choose(AnnotateTool.StrikeOut),
        SelectMark: () => Tools.Choose(AnnotateTool.Select),
        Freehand: () => Tools.Choose(AnnotateTool.Freehand),
        Rectangle: () => Tools.Choose(AnnotateTool.Rectangle),
        Ellipse: () => Tools.Choose(AnnotateTool.Ellipse),
        Line: () => Tools.Choose(AnnotateTool.Line),
        Arrow: () => Tools.Choose(AnnotateTool.Arrow),
        TextBox: () => Tools.Choose(AnnotateTool.TextBox),
        AnnotateDone: () => Tools.End(),
        MarksPane: ToggleMarksPane,
        DeleteMark: () =>
        {
            if (Workspace.Active is { } active && AnnotateOf(active) is { } interaction)
            {
                _ = interaction.Select.DeleteSelectionAsync();
            }
        },
        Sign: () => _ = GuardAsync(SignAsync),
        FormsNext: () => MoveFormFocus(forward: true),
        FormsPrevious: () => MoveFormFocus(forward: false),
        FormsNextRequired: RunNextRequired,
        FormsHighlight: ToggleFormHighlight,
        Organize: ToggleOrganize,
        RotateLeft: () => RunPageEdit(session => session.Rotate(-90)),
        RotateRight: () => RunPageEdit(session => session.Rotate(90)),
        DuplicatePages: () => RunPageEdit(session => session.Duplicate()),
        BlankPage: InsertBlankPage,
        InsertFile: InsertFile,
        MovePagesUp: () => RunPageEdit(session => session.MoveBy(-1)),
        MovePagesDown: () => RunPageEdit(session => session.MoveBy(1)),
        PagesUndo: () => OpenPlan?.Undo(),
        PagesRedo: () => OpenPlan?.Redo(),
        ApplyPages: () => _ = GuardAsync(ApplyActiveAsync),
        DiscardPages: DiscardPlan,
        DeletePages: () => RunPageEdit(session => session.Delete()),
        ExtractPages: ExtractPages,
        SplitDocument: SplitDocument,
        ExportImages: ExportImages,
        RedactMode: ToggleRedact,
        RedactMarkSelection: MarkRedactSelection,
        RedactDeleteMarks: DeleteRedactMarks,
        RedactDiscard: DiscardRedact);

    /// <summary>
    /// The Sign command: with no saved signature the new-signature card opens straight away, else the picker (a pick places that signature,
    /// "New signature" opens the card); the signature that results is placed at the centre of the visible part of the current page and
    /// selected with the select tool. A document that forbids annotating (a signature is an annotation) or no document does nothing.
    /// </summary>
    internal async Task SignAsync()
    {
        // Two quick clicks would queue two pickers behind each other and place twice: a Sign while one runs does nothing.
        if (_signRunning)
        {
            return;
        }

        _signRunning = true;
        try
        {
            await SignCoreAsync();
        }
        finally
        {
            _signRunning = false;
        }
    }

    private async Task SignCoreAsync()
    {
        if (Workspace.Active is not { } tab || AnnotateAllowedOf(tab) == false || !_views.TryGetValue(tab, out var view) || AnnotateOf(tab) is not { } interaction)
        {
            return;
        }

        IReadOnlyList<SavedSignature> items;
        try
        {
            items = await Signatures.LoadAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The folder cannot be read: say so (no path in the text) instead of letting the click do nothing.
            await Dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.SignaturePlaceTitle, PdfStrings.SignaturePlaceFailed, () => done(true)));
            return;
        }

        SavedSignature? chosen;
        if (items.Count == 0)
        {
            chosen = await ShowNewSignatureAsync();
        }
        else
        {
            var answer = await Dialogs.ShowAsync<PickerResult>(done => SignaturePickerCard.Create(items, Signatures, done));
            chosen = answer switch
            {
                PickerResult.Picked picked => picked.Signature,
                PickerResult.New => await ShowNewSignatureAsync(),
                _ => null,
            };
        }

        // The cards are modal, so the tab is the one that was active; a tab that was closed meanwhile (by a path around the dialog) gets nothing.
        if (chosen is null || !Workspace.Tabs.Contains(tab) || AnnotateAllowedOf(tab) == false)
        {
            return;
        }

        _signaturePlacer ??= new SignaturePlacer(Signatures, Tools, Dialogs);
        await _signaturePlacer.PlaceAsync(tab, view, interaction.Select, chosen);
    }

    // The commands of the Forms tab work on the active tab and hand the keyboard to its page view, so Enter and Space act on the field right away.
    private void MoveFormFocus(bool forward) => RunFormMove(mover => mover.MoveAsync(forward));

    // The command and the counter button of the form badge are the same thing.
    private void RunNextRequired() => RunFormMove(mover => mover.MoveToNextRequiredAsync());

    private void RunFormMove(Func<FormMover, Task<bool>> move)
    {
        if (Workspace.Active is not { } active || FormsOf(active) is not { } parts)
        {
            return;
        }

        // A draft whose text is refused stops the walk and is shown, so its hint says why.
        if (!parts.Interaction.EndOrReveal())
        {
            return;
        }

        _ = GuardAsync(async () =>
        {
            await move(parts.Interaction.Mover);
            if (ReferenceEquals(Workspace.Active, active) && _views.TryGetValue(active, out var view))
            {
                view.Focus();
            }
        });
    }

    // The ribbon has no checked state: the name of the command says what it does, and the highlight shows on the page.
    private void ToggleFormHighlight()
    {
        if (Workspace.Active is { } active && FormsOf(active) is { } parts)
        {
            parts.Markers.ApplyHighlight(!active.Forms.Highlight);
        }
    }

    // While the page plan or the redact plan is open Undo and Redo (the Home tab's and Ctrl+Z, Ctrl+Y) belong to its history; the marks are locked and clean then.
    private Task UndoActiveAsync()
    {
        if (OpenPlan is { } plan)
        {
            plan.Undo();
            return Task.CompletedTask;
        }

        if (OpenRedact is { } redact)
        {
            redact.Undo();
            return Task.CompletedTask;
        }

        return Workspace.Active is { } tab ? tab.Annotations.UndoAsync() : Task.CompletedTask;
    }

    private Task RedoActiveAsync()
    {
        if (OpenPlan is { } plan)
        {
            plan.Redo();
            return Task.CompletedTask;
        }

        if (OpenRedact is { } redact)
        {
            redact.Redo();
            return Task.CompletedTask;
        }

        return Workspace.Active is { } tab ? tab.Annotations.RedoAsync() : Task.CompletedTask;
    }

    /// <summary>Indexes the commands of the ribbon by id and attaches the window-wide shortcuts. Called once, after the ribbon exists.</summary>
    private void AttachShortcuts(RibbonDefinition definition)
    {
        _commandsById = definition.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).ToDictionary(c => c.Id, StringComparer.Ordinal);

        // Avalonia does not say whether a key down is the repeat of a held key, so the window keeps the keys that are down: a key down for a
        // key that is already down is a repeat. Handled events count too, or a key taken by another handler would look new next time.
        AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, (_, e) => _keysDown.Remove(e.Key), RoutingStrategies.Tunnel, handledEventsToo: true);
        Deactivated += (_, _) => _keysDown.Clear();
    }

    /// <summary>
    /// The one place that turns a shortcut into a command. It runs on the way down, before the focused control, so the shortcut works
    /// wherever the focus is, and the page view has no key handling of its own for these keys. While a dialog is open nothing happens
    /// (the dialog is modal). Ctrl+C and Ctrl+A stay with a text box that has the focus. A shortcut whose command is disabled is
    /// swallowed and does nothing.
    /// </summary>
    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        var isRepeat = !_keysDown.Add(e.Key);
        if (e.Handled || Host.IsOpen)
        {
            return;
        }

        // While the annotate mode is on, Esc ends it from wherever the focus is (the tool drops what it holds first), unless a text field or the
        // search bar owns the key. Every other key belongs to the tool only when it comes from inside the document: the page view handles it on
        // the way up (AnnotateInteraction), so Tab, the arrows, Delete and Enter keep working in the ribbon, the tab strip and the panes.
        if (e.Key == Key.Escape && Tools.Tool != AnnotateTool.None && !KeyBelongsToTextOrSearch() && Workspace.Active is { } annotated
            && _annotates.TryGetValue(annotated, out var interaction) && interaction.HandleKey(e))
        {
            return;
        }

        if (PdfCommandCatalog.Find(e.Key, e.KeyModifiers, CommandModifier) is not { } entry || !_commandsById.TryGetValue(entry.Id, out var command))
        {
            return;
        }

        // Copy, Select all, Undo and Redo belong to a text field that has the focus (it undoes its own typing).
        if (entry.Id is PdfCommandIds.Copy or PdfCommandIds.SelectAll or PdfCommandIds.Undo or PdfCommandIds.Redo && FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

        // Select all and Copy belong to the list of the organize mode while the focus is in it: Ctrl+A selects the pages there (the catalog command
        // would select the text of the page view behind it). Undo and Redo stay with the window, which sends them to the plan.
        if (entry.Id is PdfCommandIds.Copy or PdfCommandIds.SelectAll && FocusIsInOrganizeList())
        {
            return;
        }

        e.Handled = true;
        if (isRepeat && entry.Shortcut is { AllowsRepeat: false })
        {
            // A held key runs a one-shot command once (the dark page does not flicker, no pile of file pickers); the repeat is swallowed.
            return;
        }

        if (command.IsEnabled)
        {
            command.Run?.Invoke();
        }
    }

    // True while a plan is open and the focus is on the organize list of the active tab's pages pane or inside it.
    private bool FocusIsInOrganizeList() =>
        OpenPlan is not null && NavigationPane.Content is PagesPane { Organize: { } list }
        && FocusManager?.GetFocusedElement() is Visual focused && (ReferenceEquals(list, focused) || list.IsVisualAncestorOf(focused));

    // A text field (the comment, a text box editor, the search field) or anything in the search bar keeps its keys, Esc included.
    private bool KeyBelongsToTextOrSearch()
    {
        var focused = FocusManager?.GetFocusedElement();
        return focused is TextBox || (focused is Visual visual && ShownSearchBar is { } bar && (ReferenceEquals(bar, visual) || bar.IsVisualAncestorOf(visual)));
    }

    /// <summary>Closes the active tab through the close guard (a document with unsaved changes asks first). Does nothing without a tab.</summary>
    internal async Task CloseActiveTabAsync()
    {
        if (Workspace.Active is { } active)
        {
            await Workspace.CloseAsync(active);
        }
    }

    private void MovePage(int delta)
    {
        if (ActiveView is { PageCount: > 0 } view)
        {
            view.GoToPage((int)Math.Clamp((long)view.CurrentPage + delta, 0, view.PageCount - 1));
        }
    }

    /// <summary>Switches the pages of the active tab between the light and the dark theme. It belongs to the tab; nothing is stored.</summary>
    internal void ToggleDarkPage()
    {
        if (ActiveView is { } view)
        {
            view.PageTheme = view.PageTheme == PageTheme.Dark ? PageTheme.Light : PageTheme.Dark;
        }
    }

    /// <summary>Gives the pages of every open tab the theme the page appearance setting asks for (it overrides the per-tab switch).</summary>
    private void SetPageThemeOfAllTabs(PageTheme theme)
    {
        foreach (var view in _views.Values)
        {
            view.PageTheme = theme;
        }
    }

    /// <summary>Shows the list of keyboard shortcuts and returns when it is closed.</summary>
    internal Task ShowHelpAsync() =>
        Dialogs.ShowAsync<bool>(done => HelpCard.Create(PdfCommandCatalog.HelpEntries(), () => done(true)));

    private void OnToolsChanged()
    {
        // A tool chosen by a path around the disabled commands (a card, the marks pane) is ended again while a plan is open.
        if (Tools.Tool != AnnotateTool.None && (OpenPlan is not null || OpenRedact is not null))
        {
            Tools.End();
            return;
        }

        // The form markers are hidden while a tool is active.
        foreach (var parts in _formParts.Values)
        {
            parts.Markers.Invalidate();
        }

        RefreshCommandStates();
        EnforceAnnotatePermission(showCard: true);
    }

    /// <summary>
    /// A document that forbids changing annotations has no annotate mode: the commands are disabled with a reason, and a path that gets
    /// around them (the palette, a key) ends the mode again here, with a card that names the reason when the user asked for the mode
    /// (not when only the tab changed). A permission that is not read yet (null) refuses nothing.
    /// </summary>
    private void EnforceAnnotatePermission(bool showCard)
    {
        if (Tools.Tool == AnnotateTool.None || Workspace.Active is not { } active || AnnotateAllowedOf(active) != false)
        {
            return;
        }

        Tools.End();
        if (showCard)
        {
            _ = GuardAsync(() => Dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.AnnotateCardTitle, PdfStrings.AnnotateLockedReason, () => done(true))));
        }
    }

    /// <summary>The ribbon, the field codes and the search ask the commands again: the tabs, the active document or its permissions changed.</summary>
    private void RefreshCommandStates() => Ribbon.RefreshCommandStates();

    /// <summary>Reads the print permission of a tab once (the document answers on its own thread); the ribbon follows when the answer is in.</summary>
    private void LoadPrintInfo(PdfTab tab)
    {
        if (_printInfo.ContainsKey(tab) || tab.Session.Document is not { } document)
        {
            return;
        }

        _ = ReadPrintInfoAsync(tab, document);
        _ = ReadAnnotatePermissionAsync(tab, document);
        _ = ReadFillPermissionAsync(tab, document);
    }

    private async Task ReadFillPermissionAsync(PdfTab tab, PdfDocument document)
    {
        try
        {
            var allowed = await document.MayFillFormsAsync();
            if (Workspace.Tabs.Contains(tab))
            {
                _fillAllowed[tab] = allowed;
                RefreshCommandStates();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A document that went away meanwhile has no permissions to show; filling stays as it was.
        }
    }

    private async Task ReadAnnotatePermissionAsync(PdfTab tab, PdfDocument document)
    {
        try
        {
            var allowed = await document.MayAnnotateAsync();
            if (Workspace.Tabs.Contains(tab))
            {
                _annotateAllowed[tab] = allowed;
                if (ReferenceEquals(Workspace.Active, tab))
                {
                    EnforceAnnotatePermission(showCard: true);
                }

                RefreshCommandStates();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A document that went away meanwhile has no permissions to show; the tools stay as they were.
        }
    }

    private async Task ReadPrintInfoAsync(PdfTab tab, PdfDocument document)
    {
        try
        {
            var info = await document.GetPrintInfoAsync();
            if (Workspace.Tabs.Contains(tab))
            {
                _printInfo[tab] = info;
                RefreshCommandStates();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A document that went away meanwhile has no permissions to show; print stays as it was.
        }
    }

    private bool FocusIsInNavigationPane() =>
        FocusManager?.GetFocusedElement() is Visual focused && (ReferenceEquals(focused, NavigationPane) || focused.GetVisualAncestors().Contains(NavigationPane));
}
