using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Viewing;
using Raffica.Shell.Frame;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;

namespace Raffica.Write;

/// <summary>The part of the window that shows the documents: the page view of each tab, the ribbon with its commands and the status bar cells that follow the view.</summary>
internal sealed partial class WriteWindow
{
    private WriteCommandRules _rules = null!;
    private Dictionary<string, Action> _actions = null!;
    private readonly HashSet<WriteTab> _faultShown = [];
    private FormatPickers _pickers = null!;
    private FormatDisplayRefresh? _formatRefresh;
    private FormatDisplayRefresh? _viewStatesRefresh;
    private bool _formatClosed;
    private string? _editHint;
    private int _editHintGeneration;

    /// <summary>How long the hint of a refused key stays when nothing ends it sooner.</summary>
    internal static readonly TimeSpan EditHintDuration = TimeSpan.FromSeconds(6);

    /// <summary>The ribbon (the tabs Home, Layout and View); its parts sit in the frame while a document tab is open.</summary>
    public RibbonView Ribbon { get; private set; } = null!;

    /// <summary>The page view of the active tab, or null (the start view).</summary>
    internal PageView? ActiveView => Workspace.Active is { } tab && _areas.TryGetValue(tab, out var area) ? area as PageView : null;

    /// <summary>The page view of a tab that has been shown (tests); null before.</summary>
    internal PageView? ViewOf(WriteTab tab) => AreaOf(tab) as PageView;

    // Made once, before the frame shows anything: the commands, the rules that decide when they run and the ribbon built from them.
    private void CreateRibbon()
    {
        _rules = new WriteCommandRules(() => Workspace.Active is not null, () => ActiveView?.Zoom ?? ZoomSteps.Default, () => true, () => EditCommandState.Of(Workspace.Active), () => SaveCommandState.Of(Workspace.Active, Workspace.Active is { } active && _saves.IsRunning(active)), () => EditCommandState.FactsOf(Workspace.Active), () => IsClosing, ReadStructureFacts, ReadSectionFacts, ReadObjectFacts, ReadTableFacts, ReadPictureFacts);
        _actions = CreateActions();
        _pickers = new FormatPickers(() => EditCommandState.FormatOf(Workspace.Active), () => Workspace.Active?.Docx.Model, () => Workspace.Active?.Fonts, (id, value) => RunCommand(id, value));
        AddFormatActions();
        AddStructureActions();
        CreateSectionPickers();
        AddSectionActions();
        AddObjectActions();
        AddFieldActions();
        CreateTablePickers();
        AddTableActions();
        CreatePicturePickers();
        AddPictureActions();
        var definition = WriteRibbon.Create(_actions, _rules, IsCommandOn, id => _pickers.For(id) ?? _sectionPickers.For(id) ?? _tablePickers.For(id) ?? _picturePickers.For(id));
        Ribbon = new RibbonView { Definition = definition, ActiveTab = definition.Tabs.Single(t => t.Letter == WriteRibbon.HomeLetter) };
        if (_deferRibbon)
        {
            Ribbon.HoldBand();
        }

        TitleSearch.Source = definition;
        TitleSearch.CommandChosen += ChooseFromSearch;
    }

    /// <summary>How long a held ribbon band waits for the first page before it is shown anyway (a file that never draws, a layout that failed); tests make it short.</summary>
    internal static TimeSpan RibbonReleaseTimeout { get; set; } = TimeSpan.FromSeconds(1);

    private bool _ribbonTimerStarted;

    // The band was held when the first tab appeared: whatever happens to the first page, it is shown after the timeout.
    private void StartRibbonReleaseTimer()
    {
        if (!Ribbon.IsBandHeld || _ribbonTimerStarted)
        {
            return;
        }

        _ribbonTimerStarted = true;
        DispatcherTimer.RunOnce(Ribbon.ReleaseBand, RibbonReleaseTimeout);
    }

    // The first page is on screen (and the measurement of the first page has taken its frames): the ribbon follows two frames later, in an idle moment.
    private void ReleaseRibbonAfterTheFirstFrames()
    {
        if (!Ribbon.IsBandHeld)
        {
            return;
        }

        RequestAnimationFrame(_ => RequestAnimationFrame(_ => Dispatcher.UIThread.Post(Ribbon.ReleaseBand, DispatcherPriority.Background)));
    }

    /// <summary>What a choice in the command search does: a picker has nothing to run, so it opens (its list drops down, or the size box takes the focus); every other command runs.</summary>
    internal void ChooseFromSearch(CommandDefinition command)
    {
        if (command.Picker is not null)
        {
            Ribbon.OpenPicker(command);
        }
        else
        {
            command.Run?.Invoke();
        }
    }

    // What each command does.
    private Dictionary<string, Action> CreateActions() => new()
    {
        [WriteCommandIds.New] = () => _ = GuardAsync(() => NewDocumentAsync()),
        [WriteCommandIds.Open] = () => _ = GuardAsync(OpenFromDialogAsync),
        [WriteCommandIds.Save] = () => _ = GuardAsync(() => SaveActiveAsync()),
        [WriteCommandIds.SaveAs] = () => _ = GuardAsync(() => SaveAsActiveAsync()),
        [WriteCommandIds.CloseTab] = CloseActiveTab,
        [WriteCommandIds.Settings] = () => Settings.Open(),
        [WriteCommandIds.Search] = TitleSearch.FocusAndSelect,
        [WriteCommandIds.ZoomIn] = () => ActiveView?.ZoomIn(),
        [WriteCommandIds.ZoomOut] = () => ActiveView?.ZoomOut(),
        [WriteCommandIds.ActualSize] = () => ActiveView?.ActualSize(),
        [WriteCommandIds.FitWidth] = () => ActiveView?.FitWidth(),
        [WriteCommandIds.FitPage] = () => ActiveView?.FitPage(),
        [WriteCommandIds.PageView] = () => SetView(WriteView.Page),
        [WriteCommandIds.WritingView] = () => SetView(WriteView.Writing),
        [WriteCommandIds.Outline] = ToggleOutline,
        [WriteCommandIds.FormattingPane] = ToggleFormatting,
        [WriteCommandIds.SectionsPane] = ToggleSections,
        [WriteCommandIds.Undo] = () => RunEditFromUi(WriteCommandIds.Undo),
        [WriteCommandIds.Redo] = () => RunEditFromUi(WriteCommandIds.Redo),
        [WriteCommandIds.Cut] = () => RunEditFromUi(WriteCommandIds.Cut),
        [WriteCommandIds.Copy] = () => RunEditFromUi(WriteCommandIds.Copy),
        [WriteCommandIds.Paste] = () => RunEditFromUi(WriteCommandIds.Paste),
        [WriteCommandIds.SelectAll] = () => RunEditFromUi(WriteCommandIds.SelectAll),
    };

    // The formatting commands: those without a value are ribbon actions; the value commands (see CatalogCommand.TakesPayload) run through RunCommand(id, payload) with the value their picker chose (the two size steps are buttons and have an action that computes the target size).
    private void AddFormatActions()
    {
        foreach (var command in WriteCommandCatalog.Commands.Where(c => WriteCommandIds.IsFormatting(c.Id)))
        {
            var id = command.Id;
            if (command.TakesPayload)
            {
                _payloadActions[id] = payload => RunFormatFromUi(id, payload);

                // Grow font and Shrink font are buttons: they step from the size shown (the picker computes the target size).
                if (id is WriteCommandIds.SizeUp or WriteCommandIds.SizeDown)
                {
                    _actions[id] = _pickers.StepAction(id, up: id == WriteCommandIds.SizeUp);
                }
            }
            else
            {
                _actions[id] = () => _ = RunFormatFromUi(id, null);
            }
        }
    }

    // The ribbon, the command search and the menu run a formatting command on the active view (see RunEditFromUi: the view takes the focus back).
    private bool RunFormatFromUi(string id, object? payload)
    {
        if (ActiveView is not { } view)
        {
            return false;
        }

        var taken = view.RunFormatCommand(id, payload);
        if (!view.IsFocused)
        {
            view.Focus();
        }

        return taken;
    }

    // Whether a switch of the ribbon is on: the view in force and the zoom mode of the active tab.
    private bool IsCommandOn(string id)
    {
        if (ActiveView is not { } view)
        {
            return false;
        }

        if (WriteCommandIds.IsFormatting(id))
        {
            return FormatIsOn(id);
        }

        if (WriteCommandIds.IsStructure(id))
        {
            return StructureIsOn(id);
        }

        if (WriteCommandIds.IsSection(id))
        {
            return SectionIsOn(id);
        }

        if (WriteCommandIds.IsTable(id))
        {
            return id == WriteCommandIds.TableHeaderRow && ReadTableFacts(id).HeaderOn;
        }

        return id switch
        {
            WriteCommandIds.PageView => view.Mode == WriteView.Page,
            WriteCommandIds.WritingView => view.Mode == WriteView.Writing,
            WriteCommandIds.ActualSize => view.ZoomMode == ZoomMode.ActualSize,
            WriteCommandIds.FitWidth => view.ZoomMode == ZoomMode.FitWidth,
            WriteCommandIds.FitPage => view.ZoomMode == ZoomMode.FitPage,
            WriteCommandIds.Outline => _outlineWanted,
            WriteCommandIds.FormattingPane => _formattingWanted,
            WriteCommandIds.SectionsPane => _sectionsWanted,
            _ => false,
        };
    }

    // Whether a formatting switch is pressed: the effective value of the whole selection (at a caret: what typing there takes, the pending format first). A mixed selection is not pressed.
    // Read from the format the session remembers for the selection and revision; nothing is read before the session exists.
    private bool FormatIsOn(string id)
    {
        if (EditCommandState.FormatOf(Workspace.Active) is not { } format)
        {
            return false;
        }

        return id switch
        {
            WriteCommandIds.Bold => format.Bold.Is(true),
            WriteCommandIds.Italic => format.Italic.Is(true),
            WriteCommandIds.Underline => format.Underline.Is(true),
            WriteCommandIds.Strike => format.Strike.Is(true),
            WriteCommandIds.Superscript => format.Superscript.Is(true),
            WriteCommandIds.Subscript => format.Subscript.Is(true),
            WriteCommandIds.SmallCaps => format.SmallCaps.Is(true),
            WriteCommandIds.AllCaps => format.AllCaps.Is(true),
            WriteCommandIds.AlignLeft => format.Alignment.Is(Raffica.Write.Layout.ParagraphAlignment.Left),
            WriteCommandIds.AlignCenter => format.Alignment.Is(Raffica.Write.Layout.ParagraphAlignment.Center),
            WriteCommandIds.AlignRight => format.Alignment.Is(Raffica.Write.Layout.ParagraphAlignment.Right),
            WriteCommandIds.Justify => format.Alignment.Is(Raffica.Write.Layout.ParagraphAlignment.Justify),
            WriteCommandIds.KeepNext => format.KeepNext.Is(true),
            WriteCommandIds.KeepLines => format.KeepLines.Is(true),
            WriteCommandIds.PageBreakBefore => format.PageBreakBefore.Is(true),
            WriteCommandIds.WidowControl => format.WidowControl.Is(true),
            _ => false,
        };
    }

    // The session of a tab changed the selection, the document or the pending format: the display of the formatting is read again once the typing pauses (never per keystroke).
    private void FollowFormat(WriteTab tab, EditSession session)
    {
        void Changed()
        {
            if (!_formatClosed && ReferenceEquals(Workspace.Active, tab))
            {
                (_formatRefresh ??= new FormatDisplayRefresh(RefreshCommandStates, FormatDisplayRefresh.DefaultDelay)).Request();
            }
        }

        session.SelectionChanged += Changed;
        session.Changed += Changed;
        session.Pending.Changed += Changed;
    }

    /// <summary>The pickers of the Home tab (tests read how often a list was asked).</summary>
    internal FormatPickers Pickers => _pickers;

    /// <summary>The refresh of the formatting display that waits for typing to pause (tests run it now); null before the first request.</summary>
    internal FormatDisplayRefresh? FormatRefresh => _formatRefresh;

    /// <summary>The refresh of the ribbon's commands that waits for the view to pause once the tab has an editing session (tests run it now); null before the first request.</summary>
    internal FormatDisplayRefresh? ViewStatesRefresh => _viewStatesRefresh;

    // An edit command that came from the ribbon, the command search or the menu works on the view's selection, so the view takes the focus back: a ribbon button that kept it would
    // swallow the typing and run the command again on Space or Enter. The keys of the page view do not come here.
    private void RunEditFromUi(string id)
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        view.RunEditCommand(id);
        if (!view.IsFocused)
        {
            view.Focus();
        }
    }

    /// <summary>Runs a command of the catalog by id (the keys, the macOS menu and the tests); false when it is not allowed or has no action.</summary>
    internal bool RunCommand(string id)
    {
        if (!_rules.Allows(id) || !_actions.TryGetValue(id, out var run))
        {
            return false;
        }

        run();
        return true;
    }

    // What the value commands do with the value their picker chose (<see cref="CatalogCommand.TakesPayload"/>); the formatting step wires them, a command with no entry does nothing.
    private readonly Dictionary<string, Func<object?, bool>> _payloadActions = [];

    /// <summary>Runs a value command of the catalog (a font, a size, a colour, a spacing, a style) with the value its picker chose; false when it is not allowed, has no action, takes no value or the value is not usable (nothing changes then).</summary>
    internal bool RunCommand(string id, object? payload)
    {
        if (!WriteCommandIds.TakesPayload(id) || !_rules.Allows(id) || !_payloadActions.TryGetValue(id, out var run))
        {
            return false;
        }

        return run(payload);
    }

    /// <summary>Shows the active document in another view (a no-op without one).</summary>
    internal void SetView(WriteView view)
    {
        if (ActiveView is { } active)
        {
            active.Mode = view;
        }
    }

    // The area of a tab: its page view in the view and zoom the settings give a new tab. The first drawn page starts the rest of the layout.
    private PageView CreateDocumentArea(WriteTab tab)
    {
        var view = new PageView(tab, Settings.App.DefaultView, Settings.App.DefaultZoomMode);
        view.ViewChanged += () =>
        {
            if (ReferenceEquals(ActiveView, view))
            {
                // The view changes with every typed character (the caret, the scroll): once the tab has an editing session the status text follows at once and the commands of the ribbon (a read of every one of them, 4 to 5 ms on a laptop) when it pauses; before the first caret or key nothing is deferred and no timer exists.
                UpdateViewStatus(refreshStatesNow: view.Tab.Editor is null);
            }
        };
        // The keys of the page view run the command on the view that has the focus and the rules decide (no focus change: the view has it).
        view.EditCommandHandler = id => (_rules.Allows(id) || view.TakesClipboardKeyOfSelectedPicture(id)) && view.RunEditCommand(id);
        view.FormatKeyHandler = id => _rules.Allows(id) && view.RunFormatCommand(id);
        view.StructureKeyHandler = id => RunStructureKey(view, id);
        view.OutlineParagraphSource = EngagedOutlineParagraph;
        view.StructureNote += text => ShowStructureNote(view, text);
        view.SectionRowRunner = action => RunSectionRow(tab, action);
        view.ActiveStoryChanged += () =>
        {
            if (ReferenceEquals(ActiveView, view))
            {
                Status.Count = StoryStatusText(view);
            }
        };
        view.SelectionStateChanged += () =>
        {
            if (ReferenceEquals(ActiveView, view))
            {
                RefreshCommandStates();
            }
        };
        view.InputRefused += reason => OnInputRefused(view, tab, reason);
        view.FirstSharpPage += () =>
        {
            FirstSharpPageDrawn?.Invoke(tab);
            RequestRecoveryOffer();
            ReleaseRibbonAfterTheFirstFrames();
            tab.Runner.Start();
            if (ReferenceEquals(ActiveView, view))
            {
                FocusActiveView();
            }
        };
        view.AttachedToVisualTree += (_, _) =>
        {
            if (ReferenceEquals(ActiveView, view))
            {
                FocusActiveView();
            }
        };
        tab.Runner.Faulted += () => OnLayoutFaulted(tab);

        // The session is made by the first caret or key; the display of the formatting follows it from then on.
        tab.EditorCreated += session => FollowFormat(tab, session);
        if (tab.Editor is { } existing)
        {
            FollowFormat(tab, existing);
        }

        return view;
    }

    // Typing or an editing key was refused (only a prefix is open, or editing failed): one hint in the status bar per burst of keys. It stays while the keys go on, goes when the whole
    // file is open (the first case) or after a few seconds, and a later key shows it again. A key during a save is queued and says nothing.
    private void OnInputRefused(PageView view, WriteTab tab, EditBlockedReason reason)
    {
        if (!ReferenceEquals(ActiveView, view))
        {
            return;
        }

        var text = reason == EditBlockedReason.StillOpening ? WriteStrings.EditStillOpening : tab.Runner.FaultedByEdit ? WriteStrings.EditFailed : WriteStrings.EditUnavailable;
        if (_editHint == text && Status.Hint == text)
        {
            return;
        }

        Status.Hint = text;
        _editHint = text;
        var generation = ++_editHintGeneration;
        void Clear()
        {
            if (generation == _editHintGeneration)
            {
                _editHint = null;
                if (Status.Hint == text)
                {
                    Status.Hint = null;
                }
            }
        }

        if (reason == EditBlockedReason.StillOpening)
        {
            var ui = Dispatcher.UIThread;
            _ = tab.Runner.FullModelTask.ContinueWith(_ => ui.Post(Clear), TaskScheduler.Default);
        }

        DispatcherTimer.RunOnce(Clear, EditHintDuration);
    }

    // A layout failed (the whole file could not be read after page 1, or a bug of the layout): one card per tab, in the wording of the open errors (never the text of
    // the exception). The tab stays open on what is laid out; the status no longer promises more pages. Nothing is shown for a window that is closing or a closed tab.
    private void OnLayoutFaulted(WriteTab tab)
    {
        if (_closeConfirmed || tab.IsDisposed)
        {
            return;
        }

        if (tab.Runner.FaultedByEdit)
        {
            // Editing failed in an open document: no card about a file that cannot be read; the status says editing stopped (and that Save as may still keep the work).
            if (ActiveView is { } active && ReferenceEquals(active.Tab, tab))
            {
                OnInputRefused(active, tab, EditBlockedReason.Faulted);
                UpdateViewStatus();
            }

            return;
        }

        if (!_faultShown.Add(tab))
        {
            return;
        }

        if (tab.Path is not { } path)
        {
            // A new document has no file that could fail to open, and so no open error to show (a restored copy is the same kind); the commands that need the layout (Save,
            // Save as) are disabled with their reason, so they are asked again.
            if (ReferenceEquals(Workspace.Active, tab))
            {
                UpdateViewStatus();
            }

            return;
        }

        ShowOpenError(path, tab.Runner.Fault is DocxFormatException or InvalidDataException ? OpenError.Corrupt : OpenError.Other);
        if (ReferenceEquals(Workspace.Active, tab))
        {
            UpdateViewStatus();
        }
    }

    // Puts the focus on the page view of the active tab so the keys scroll it. A focus that does not take (the view is not on screen yet) is tried again a few times; a
    // text box that has the focus (the command search) and the outline list keep it.
    private void FocusActiveView(int attempts = 6)
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (!ReferenceEquals(ActiveView, view) || view.IsFocused || FocusedElement() is TextBox || InOutline(FocusedElement()) || InFormatting(FocusedElement()))
                {
                    return;
                }

                if (!(view.IsEffectivelyVisible && view.Focus()) && attempts > 1)
                {
                    DispatcherTimer.RunOnce(() => FocusActiveView(attempts - 1), TimeSpan.FromMilliseconds(50));
                }
            },
            DispatcherPriority.Background);
    }

    // Whether the focus is inside the outline pane: its list keeps the focus (and its keys) until the user chooses a heading or leaves it.
    private bool InOutline(IInputElement? focused) => _outlineHost is { } host && focused is Avalonia.Visual visual && host.IsVisualAncestorOf(visual);

    // Whether the focus is inside the formatting pane: it keeps the focus like the outline list. A retry or a re-activation that was queued before the user went into the pane
    // must not pull the focus back to the page (the pane gives it back itself when it closes or a command ran).
    private bool InFormatting(IInputElement? focused) => _formattingHost is { } host && focused is Avalonia.Visual visual && host.IsVisualAncestorOf(visual);

    private IInputElement? FocusedElement() => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();

    // Navigation keys that reach the window while the page view does not have the focus: they scroll the active view, so a lost focus cannot swallow them. The arrows
    // only when nothing has the focus (they move inside the ribbon and the tabs); PageUp, PageDown, Home and End also when a button or tab has it, never a text box, a slider or the outline list (they are its own keys).
    private bool ForwardNavigationKey(KeyEventArgs e)
    {
        if (ActiveView is not { IsFocused: false } view || !PageView.IsNavigationKey(e.Key, e.KeyModifiers))
        {
            return false;
        }

        var focused = FocusedElement();
        var arrow = e.Key is Key.Up or Key.Down or Key.Left or Key.Right;
        var inOutline = InOutline(focused);
        var allowed = focused is null || ReferenceEquals(focused, this) || (!arrow && !inOutline && focused is not TextBox and not ComboBox and not Slider);
        return allowed && view.HandleNavigationKey(e.Key, e.KeyModifiers);
    }

    /// <summary>The cells of the status bar follow the view of the active tab: the page (with a plus while the count may still grow) and the zoom.</summary>
    private void UpdateViewStatus(bool refreshStatesNow = true)
    {
        Status.Count = ActiveView is { } active ? StoryStatusText(active) : null;
        if (ActiveView is { PageCount: > 0 } view)
        {
            var format = view.IsLayoutComplete ? WriteStrings.StatusPositionFormat : WriteStrings.StatusPositionMoreFormat;
            Status.Position = WriteStrings.Fill(format, view.CurrentPage + 1, view.PageCount);
            Status.ZoomText = ZoomText(view.Zoom);
            Status.Zoom = view.Zoom * 100;
        }
        else
        {
            Status.Position = null;
        }

        if (refreshStatesNow)
        {
            RefreshCommandStates();
        }
        else if (!_formatClosed)
        {
            (_viewStatesRefresh ??= new FormatDisplayRefresh(RefreshCommandStates, FormatDisplayRefresh.DefaultDelay)).Request();
        }
    }

    /// <summary>The zoom cell: the zoom as a whole percentage, for example <c>125%</c>.</summary>
    internal static string ZoomText(double zoom) =>
        (double.IsFinite(zoom) ? Math.Round(zoom * 100, MidpointRounding.AwayFromZero) : 100).ToString("0", WriteStrings.Culture) + "%";

    /// <summary>Asks every command of the ribbon again whether it may run, and the macOS menu to read its states.</summary>
    private void RefreshCommandStates()
    {
        Ribbon.RefreshCommandStates();
        RefreshOutlineButtons();
        FormattingStatesChanged();
        StatesChanged?.Invoke();
    }
}
