using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Core.Printing;
using Raffica.Core.Viewing;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Platform;
using Raffica.Pdf.Printing;
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
    private readonly PdfCommandRules _rules;
    private readonly PrintCommand _print;
    private IPrintService? _printService;
    private bool _printProbed;
    private bool _printAvailable;
    private readonly HashSet<Key> _keysDown = [];
    private Dictionary<string, CommandDefinition> _commandsById = [];

    /// <summary>The command modifier of the platform: Cmd on macOS, Ctrl elsewhere (what the frame uses for Ctrl+K).</summary>
    private static KeyModifiers CommandModifier =>
        Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyNames.CommandModifier;

    /// <summary>The tool model of the annotate mode: the active tool and the colours and widths the tools remember (in the viewer settings).</summary>
    internal ToolState Tools { get; }

    /// <summary>Raised when Save runs (Ctrl+S or the command); the save controller of the milestone takes it over.</summary>
    internal event Action? SaveRequested;

    /// <summary>Raised when Save as runs; the save controller of the milestone takes it over.</summary>
    internal event Action? SaveAsRequested;

    private PdfCommandRules CreateRules() => new(
        () => Workspace.Active is not null,
        () => Workspace.Tabs.Count,
        () => Workspace.Active is { } active && _printInfo.TryGetValue(active, out var info) ? info : null,
        PrintAvailable,
        isDirty: () => Workspace.Active is { IsDirty: true },
        canUndo: () => Workspace.Active?.Annotations.CanUndo == true,
        canRedo: () => Workspace.Active?.Annotations.CanRedo == true,
        busySaving: () => _print.IsRunning,
        annotateAllowed: () => Workspace.Active is not { } active || AnnotateAllowedOf(active) != false,
        hasSelection: () => Workspace.Active is { } active && AnnotateOf(active)?.Select.HasSelection == true);

    /// <summary>Whether the permissions of a tab allow changing annotations, once they were read (null before: the tools are not refused yet).</summary>
    internal bool? AnnotateAllowedOf(PdfTab tab) => _annotateAllowed.TryGetValue(tab, out var allowed) ? allowed : null;

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
        Save: () => SaveRequested?.Invoke(),
        SaveAs: () => SaveAsRequested?.Invoke(),
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
        });

    private Task UndoActiveAsync() => Workspace.Active is { } tab ? tab.Annotations.UndoAsync() : Task.CompletedTask;

    private Task RedoActiveAsync() => Workspace.Active is { } tab ? tab.Annotations.RedoAsync() : Task.CompletedTask;

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

        // While the annotate mode is on, the active tool gets the key first (Esc ends the mode), unless a text field or the search bar owns it.
        // The marks pane keeps its keys (the arrows move in its list, Delete acts on the entry that has the focus), except Esc, which still ends the mode.
        if (Tools.Tool != AnnotateTool.None && !KeyBelongsToTextOrSearch() && !(e.Key != Key.Escape && FocusIsInMarksPane()) && Workspace.Active is { } annotated
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

    private bool FocusIsInMarksPane() =>
        FocusManager?.GetFocusedElement() is Visual focused && (ReferenceEquals(focused, CommentsPane) || focused.GetVisualAncestors().Contains(CommentsPane));

    private bool FocusIsInNavigationPane() =>
        FocusManager?.GetFocusedElement() is Visual focused && (ReferenceEquals(focused, NavigationPane) || focused.GetVisualAncestors().Contains(NavigationPane));
}
