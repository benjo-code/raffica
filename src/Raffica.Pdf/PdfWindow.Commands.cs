using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
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
    private readonly PdfCommandRules _rules;
    private Dictionary<string, CommandDefinition> _commandsById = [];

    /// <summary>The command modifier of the platform: Cmd on macOS, Ctrl elsewhere (what the frame uses for Ctrl+K).</summary>
    private static KeyModifiers CommandModifier =>
        Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyNames.CommandModifier;

    private PdfCommandRules CreateRules() => new(
        () => Workspace.Active is not null,
        () => Workspace.Tabs.Count,
        () => Workspace.Active is { } active && _printInfo.TryGetValue(active, out var info) ? info : null);

    private PdfCommands CreateCommands() => new(
        Open: () => _ = GuardAsync(OpenFromDialogAsync),
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
        DarkPage: ToggleDarkPage,
        PagesPane: ShowPagesPane,
        OutlinePane: ShowOutlinePane);

    /// <summary>Indexes the commands of the ribbon by id and attaches the window-wide shortcuts. Called once, after the ribbon exists.</summary>
    private void AttachShortcuts(RibbonDefinition definition)
    {
        _commandsById = definition.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).ToDictionary(c => c.Id, StringComparer.Ordinal);
        AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// The one place that turns a shortcut into a command. It runs on the way down, before the focused control, so the shortcut works
    /// wherever the focus is, and the page view has no key handling of its own for these keys. While a dialog is open nothing happens
    /// (the dialog is modal). Ctrl+C and Ctrl+A stay with a text box that has the focus. A shortcut whose command is disabled is
    /// swallowed and does nothing.
    /// </summary>
    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || Host.IsOpen)
        {
            return;
        }

        if (PdfCommandCatalog.Find(e.Key, e.KeyModifiers, CommandModifier) is not { } entry || !_commandsById.TryGetValue(entry.Id, out var command))
        {
            return;
        }

        if (entry.Id is PdfCommandIds.Copy or PdfCommandIds.SelectAll && FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

        e.Handled = true;
        if (command.IsEnabled)
        {
            command.Run?.Invoke();
        }
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

    /// <summary>Shows the list of keyboard shortcuts and returns when it is closed.</summary>
    internal Task ShowHelpAsync() =>
        Dialogs.ShowAsync<bool>(done => HelpCard.Create(PdfCommandCatalog.HelpEntries(), () => done(true)));

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
