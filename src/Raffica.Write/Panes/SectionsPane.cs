using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Viewing;

namespace Raffica.Write.Panes;

/// <summary>What a control of the Sections pane asks for.</summary>
internal enum SectionRowKind
{
    /// <summary>Gives the section its own default header, or makes it the same as the one before.</summary>
    OwnHeader,

    /// <summary>Gives the section its own default footer, or makes it the same as the one before.</summary>
    OwnFooter,

    /// <summary>Sets or clears "different first page" of the section.</summary>
    FirstPage,

    /// <summary>Merges the section into the one before it.</summary>
    RemoveMarker,

    /// <summary>Opens the number-pages card for the section.</summary>
    NumberFrom,

    /// <summary>Turns left and right pages on or off (document-wide; no section).</summary>
    LeftRight,
}

/// <summary>One request of the pane: the control, the section it names (index and properties object, so a row that no longer belongs to the document is refused) and the state asked for.</summary>
internal sealed record SectionRowAction(SectionRowKind Kind, int Section, SectionProperties? Identity, bool On);

/// <summary>What the Sections pane may ask of the window.</summary>
/// <param name="PageOfBlock">The page index the layout put a top-level body block on, -1 when that page is not laid out.</param>
/// <param name="ActiveSection">The section the caret or the active header or footer belongs to, null when there is none.</param>
/// <param name="BlockedReason">Why the controls cannot change the document now (a save is running, the document is still opening), null when they can.</param>
/// <param name="Run">Runs a request; false when it was refused before anything changed.</param>
/// <param name="Note">Shows a short note in the window (a refused stale row says so); null in a pane without a window.</param>
internal sealed record SectionsPaneActions(Func<int, int> PageOfBlock, Func<int?> ActiveSection, Func<string?> BlockedReason, Func<SectionRowAction, bool> Run, Action<string>? Note = null);

/// <summary>
/// The Sections pane of one document tab (256 px, the right slot of the frame, shared with the formatting pane): one row for each section (where it starts, how its pages are numbered, whether its first page
/// differs, whether its header and footer are its own or the same as the section before, its page setup in one line, and the buttons Number from here and Remove marker), the row of the section the caret (or the
/// active header or footer) belongs to marked, and the document row "Different left and right pages". A row names its section by index and properties object and acts on that section only, after the pending refresh
/// has run: a row that no longer belongs to the document does nothing. The header and footer switches show and change the default part; the first-page and even parts follow the first-page and left-right switches.
/// Like the formatting inspector it is made when first opened and kept until the tab closes (nothing exists before), follows the session through one debounced refresh (never a read per key), reads on the UI thread
/// where the model is edited, remembers its answer (<see cref="SectionsSource"/>), lists at most <see cref="SectionsSource.MaximumSections"/> sections, and lets go of everything while hidden.
/// </summary>
internal sealed class SectionsPane : UserControl, IDisposable, IRightSlotPane
{
    private readonly WriteTab _tab;
    private readonly SectionsPaneActions _actions;
    private readonly SectionsSource _source = new();
    private readonly FormatDisplayRefresh _refresh;
    private readonly TextBlock _hint;
    private readonly TextBlock _defaultsHint;
    private readonly TextBlock _notice;
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<int, RowView> _rowViews = [];
    private readonly StackPanel _details;
    private readonly ItemsControl _rowList;
    private readonly CheckBox _leftRight;
    private readonly List<RowItem> _items = [];
    private readonly List<(Control Control, Func<string?> Disabled)> _controls = [];
    private SectionsView? _view;
    private int? _shownActive;
    private EditSession? _session;
    private LayoutRunner? _runner;
    private string? _focusName;
    private bool _forceRebuild;
    private bool _active = true;
    private bool _disposed;

    /// <summary>Makes the pane of a tab; it follows the tab's session once there is one (the pane never makes the session).</summary>
    public SectionsPane(WriteTab tab, SectionsPaneActions actions)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(actions);
        _tab = tab;
        _actions = actions;
        _refresh = new FormatDisplayRefresh(RefreshNow, RefreshDelay);

        _hint = Text("sections-hint");
        _hint.Text = WriteStrings.SectionsHint;
        _hint.Focusable = true;
        _hint.IsTabStop = true;
        AutomationProperties.SetName(_hint, WriteStrings.SectionsHint);

        _defaultsHint = Text("sections-hint");
        _defaultsHint.Text = WriteStrings.SectionsDefaultsHint;
        AutomationProperties.SetName(_defaultsHint, WriteStrings.SectionsDefaultsHint);

        _notice = Text("sections-hint");
        _notice.IsVisible = false;

        _rowList = new ItemsControl { ItemTemplate = new FuncDataTemplate<RowItem>((item, _) => item is null ? null : CreateRow(item)) };
        AutomationProperties.SetName(_rowList, WriteStrings.CmdSections);

        var documentHeading = Text("sections-heading");
        documentHeading.Text = WriteStrings.SectionsDocumentHeading;
        AutomationProperties.SetName(documentHeading, WriteStrings.SectionsDocumentHeading);
        _leftRight = new CheckBox { Name = "PART_LeftRight", Content = WriteStrings.SectionLeftRight };
        AutomationProperties.SetName(_leftRight, WriteStrings.SectionLeftRight);
        _leftRight.Click += (_, _) => Act(null, SectionRowKind.LeftRight, "PART_LeftRight");
        _controls.Add((_leftRight, () => null));

        _details = new StackPanel { Spacing = 6 };
        _details.Children.Add(_rowList);
        _details.Children.Add(documentHeading);
        _details.Children.Add(_leftRight);

        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(_hint);
        content.Children.Add(_defaultsHint);
        content.Children.Add(_notice);
        content.Children.Add(_details);
        _scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Content = _scroll;

        _tab.EditorCreated += OnEditorCreated;
        _tab.Disposing += Dispose;
        Attach();
        RefreshNow();
    }

    /// <summary>How long typing and selecting must pause before the pane reads again.</summary>
    internal static TimeSpan RefreshDelay => FormatDisplayRefresh.DefaultDelay;

    /// <summary>The refresh that waits for the pause (tests run it now with <see cref="FormatDisplayRefresh.Flush"/>).</summary>
    internal FormatDisplayRefresh Refresh => _refresh;

    /// <summary>The source the pane reads through (tests count its reads).</summary>
    internal SectionsSource Source => _source;

    /// <summary>How many times the pane showed a state (the first fill and every refresh that ran).</summary>
    internal int RefreshCount { get; private set; }

    /// <summary>What is on show: the sections as last read (null while a line stands in for them).</summary>
    internal SectionsView? View => _view;

    /// <summary>The rows as shown, in order, with the state of each (the row of the active section is marked).</summary>
    internal IReadOnlyList<RowItem> Rows => [.. _items.Select(i => i with { IsActive = i.Row.Index == _shownActive })];

    /// <summary>How many rows were made (a row is about a dozen controls; a refresh that finds the rows as they are makes none).</summary>
    internal int RowsBuilt { get; private set; }

    /// <summary>How many times the list was thrown away and made again.</summary>
    internal int Rebuilds { get; private set; }

    /// <summary>The scroll viewer of the pane (tests read and set its offset).</summary>
    internal ScrollViewer Scroll => _scroll;

    /// <summary>The line that stands in for the list (opening, too many sections); null while the rows are shown.</summary>
    internal string? NoticeText => _notice.IsVisible ? _notice.Text : null;

    /// <summary>The text of the hint at the top.</summary>
    internal string HintText => _hint.Text ?? string.Empty;

    internal bool IsDisposed => _disposed;

    /// <summary>A control of a row by its name (<c>PART_S2_Remove</c> and so on), or the left and right switch (<c>PART_LeftRight</c>); null when there is none.</summary>
    internal Control? ControlOf(string name) => this.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name);

    /// <summary>Puts the keyboard focus on the first control of the pane that can take it.</summary>
    public void FocusContent()
    {
        var target = this.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(e => e.Focusable && e.IsTabStop && e.IsEffectivelyVisible && e.IsEffectivelyEnabled);
        target?.Focus(NavigationMethod.Tab);
    }

    /// <summary>The pane left the screen: nothing is read until it is shown again.</summary>
    public void Hidden()
    {
        if (_disposed || !_active)
        {
            return;
        }

        _active = false;
        Detach();
        _refresh.Cancel();
    }

    /// <summary>The pane is on screen again: it follows the session and the layout again and shows what the document says now (one read).</summary>
    public void Shown()
    {
        if (_disposed || _active)
        {
            return;
        }

        _active = true;
        Attach();
        RefreshNow();
    }

    /// <summary>Asks for a refresh once typing and selecting pause.</summary>
    public void RequestRefresh()
    {
        if (!_disposed && _active)
        {
            _refresh.Request();
        }
    }

    /// <summary>Reads the rules of the controls again (cheap); the rows wait for <see cref="RequestRefresh"/>.</summary>
    public void RefreshButtons()
    {
        if (!_disposed && _active)
        {
            UpdateControls();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tab.EditorCreated -= OnEditorCreated;
        _tab.Disposing -= Dispose;
        Detach();
        _refresh.Dispose();
    }

    private void OnEditorCreated(EditSession session)
    {
        if (!_disposed && _active)
        {
            Attach();
            RequestRefresh();
        }
    }

    // The pane follows the session (a selection or an edit asks for the debounced refresh, which is the only read) and the layout (a page that comes into being gives a start page its number).
    private void Attach()
    {
        Detach();
        if (_tab.Editor is { } session)
        {
            _session = session;
            session.SelectionChanged += RequestRefresh;
            session.Changed += RequestRefresh;
        }

        _runner = _tab.Runner;
        _runner.Changed += RequestRefresh;
    }

    private void Detach()
    {
        if (_session is { } session)
        {
            session.SelectionChanged -= RequestRefresh;
            session.Changed -= RequestRefresh;
            _session = null;
        }

        if (_runner is { } runner)
        {
            runner.Changed -= RequestRefresh;
            _runner = null;
        }
    }

    // The read and the redraw of the pane (UI thread only: the model is edited there).
    private void RefreshNow()
    {
        if (_disposed || !_active)
        {
            return;
        }

        RefreshCount++;
        if (_tab.Docx.IsPrefix)
        {
            ShowNotice(WriteStrings.ReasonEditOpening);
            return;
        }

        if (_tab.Editor?.BlockedReason == EditBlockedReason.Saving)
        {
            // The model is not read while a save holds it; what is on show stays until the save ends (the window asks again then).
            UpdateControls();
            return;
        }

        var view = _source.Read(_tab.Docx.Model, _actions.PageOfBlock);
        if (view.TooMany)
        {
            ShowNotice(WriteStrings.SectionsTooMany);
            return;
        }

        _notice.IsVisible = false;
        _details.IsVisible = true;
        var active = ActiveSectionOrNull();

        // A new revision gives a new view, but the rows are what they were nearly always (typing in a paragraph): the controls stay, which keeps the focus and the scroll position and costs nothing.
        // Only rows that differ by value (what they show or the section they name) are made again; a change of the active section moves the mark.
        var same = !_forceRebuild && _view is { } shown && SameRows(shown.Rows, view.Rows);
        if (same)
        {
            // The start pages come from the layout, which goes on while the pane is open: a page that is known now is written into its line, the row is not made again.
            UpdatePages(view.Rows);
        }

        _view = view;
        if (!same)
        {
            _forceRebuild = false;
            _shownActive = active;
            Rebuild(view, active);
        }
        else if (active != _shownActive)
        {
            _shownActive = active;
            MarkActive(active);
        }

        _leftRight.IsChecked = view.LeftRight;
        UpdateControls();
    }

    private int? ActiveSectionOrNull()
    {
        try
        {
            return _actions.ActiveSection();
        }
        catch (StalePositionException)
        {
            // A caret that does not belong to the model any more marks no row; the next selection or edit marks again.
            return null;
        }
    }

    // Whether the rows are the same but for the page each starts on.
    private static bool SameRows(IReadOnlyList<SectionRow> a, IReadOnlyList<SectionRow> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].FirstPage != b[i].FirstPage ? !(a[i] with { FirstPage = b[i].FirstPage }).Equals(b[i]) : !a[i].Equals(b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void UpdatePages(IReadOnlyList<SectionRow> rows)
    {
        for (var i = 0; i < rows.Count && i < _items.Count; i++)
        {
            if (_items[i].Row.FirstPage != rows[i].FirstPage)
            {
                _items[i] = _items[i] with { Row = rows[i] };
                if (_rowViews.TryGetValue(rows[i].Index, out var view))
                {
                    view.Start.Text = SectionsRowWording.Start(rows[i]);
                }
            }
        }
    }

    private void ShowNotice(string text)
    {
        _view = null;
        _shownActive = null;
        _notice.Text = text;
        _notice.IsVisible = true;
        AutomationProperties.SetName(_notice, text);
        _details.IsVisible = false;
        _items.Clear();
        _rowViews.Clear();
        _rowList.ItemsSource = null;
    }

    private void Rebuild(SectionsView view, int? active)
    {
        // The controls of the old rows are let go of; the template makes the new ones (each registers its own rule). The control that has the focus and the scroll position are put back afterwards.
        Rebuilds++;
        CaptureFocus();
        var offset = _scroll.Offset;
        _controls.RemoveAll(c => !ReferenceEquals(c.Control, _leftRight));
        _rowViews.Clear();
        _items.Clear();
        for (var i = 0; i < view.Rows.Count; i++)
        {
            _items.Add(new RowItem(view.Rows[i], i > 0 ? view.Rows[i - 1] : null, view.Rows[i].Index == active));
        }

        _rowList.ItemsSource = null;
        _rowList.ItemsSource = _items.ToList();
        RestoreFocus(offset);
    }

    // The name of the control of the pane that has the focus now, when no action named one already.
    private void CaptureFocus()
    {
        if (_focusName is not null || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Control { Name: { } name } focused || !this.IsVisualAncestorOf(focused))
        {
            return;
        }

        _focusName = name;
    }

    // The active section's row is marked by its look, by a line of words and by its name for assistive technology.
    private void MarkActive(int? active)
    {
        foreach (var (index, view) in _rowViews)
        {
            view.Mark(index == active);
        }
    }

    // After a change the controls are made again; the one that had the focus gets it back (by name), so the keyboard user stays where they were.
    private void RestoreFocus(Vector offset)
    {
        var name = _focusName;
        _focusName = null;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                if (_disposed)
                {
                    return;
                }

                // The scroll position first (the new rows must have been measured), so the focus does not scroll the list.
                _scroll.UpdateLayout();
                _scroll.Offset = offset;
                if (name is null)
                {
                    return;
                }

                if (ControlOf(name) is { IsEffectivelyEnabled: true } target)
                {
                    target.Focus(NavigationMethod.Tab);
                }
                else
                {
                    FocusContent();
                }
            },
            Avalonia.Threading.DispatcherPriority.Loaded);
    }

    // The controls: enabled by the rules, and a disabled one says why in its tooltip and help text.
    private void UpdateControls()
    {
        var blocked = _actions.BlockedReason();
        foreach (var (control, disabled) in _controls)
        {
            ApplyRule(control, blocked ?? disabled());
        }
    }

    private static void ApplyRule(Control control, string? why)
    {
        control.IsEnabled = why is null;
        ToolTip.SetTip(control, why);

        // A disabled control shows no tooltip unless it is told to: the reason must reach the mouse user as well as the keyboard and screen reader user.
        ToolTip.SetShowOnDisabled(control, true);
        AutomationProperties.SetHelpText(control, why ?? string.Empty);
    }

    // A control made after the last read (the list makes its rows when it is first laid out) takes the rule at once.
    private void Register(Control control, Func<string?> disabled)
    {
        _controls.Add((control, disabled));
        ApplyRule(control, _actions.BlockedReason() ?? disabled());
    }

    // A control acts on the section its row names, as the document is now: the pending refresh runs first, and a row that is not among the rows read now (a section removed or added before it) does nothing.
    private void Act(RowItem? item, SectionRowKind kind, string focusName)
    {
        _refresh.Flush();
        SectionRow? live = null;
        if (item is not null)
        {
            live = _view?.Rows.FirstOrDefault(r => r.Index == item.Row.Index && ReferenceEquals(r.Identity, item.Row.Identity));
            if (live is null)
            {
                _focusName = null;
                _actions.Note?.Invoke(WriteStrings.NoteSectionRowStale);
                ForceRefresh();
                return;
            }
        }

        var on = kind switch
        {
            SectionRowKind.OwnHeader => !live!.HeaderOwn,
            SectionRowKind.OwnFooter => !live!.FooterOwn,
            SectionRowKind.FirstPage => !live!.FirstPageDifferent,
            SectionRowKind.LeftRight => !(_view?.LeftRight ?? false),
            _ => true,
        };
        _focusName = focusName;
        _ = _actions.Run(new SectionRowAction(kind, live?.Index ?? -1, live?.Identity, on));

        // A switch the click turned over is put back to what the document says; a removed marker takes its row with it.
        ForceRefresh();
    }

    private void ForceRefresh()
    {
        _forceRebuild = true;
        RefreshNow();
        if (_notice.IsVisible)
        {
            _focusName = null;
        }
    }

    private Border CreateRow(RowItem item)
    {
        var row = item.Row;
        var title = Text("sections-title");
        title.Text = SectionsRowWording.Title(row);
        var lines = new StackPanel { Spacing = 2 };
        lines.Children.Add(title);
        var state = Text("sections-state");
        state.Text = WriteStrings.SectionCurrent;
        state.IsVisible = item.IsActive;
        lines.Children.Add(state);

        var start = Line(SectionsRowWording.Start(row));
        lines.Children.Add(start);
        lines.Children.Add(Line(SectionsRowWording.Numbers(row)));
        lines.Children.Add(Line(SectionsRowWording.Setup(row)));

        var prefix = $"PART_S{row.Index}_";
        lines.Children.Add(Toggle(item, prefix + "Header", SectionsRowWording.Header(row), row.HeaderOwn, SectionRowKind.OwnHeader, row.Index == 0 ? WriteStrings.SectionFirstOwnTip : null));
        lines.Children.Add(Toggle(item, prefix + "Footer", SectionsRowWording.Footer(row), row.FooterOwn, SectionRowKind.OwnFooter, row.Index == 0 ? WriteStrings.SectionFirstOwnTip : null));
        lines.Children.Add(Toggle(item, prefix + "First", WriteStrings.SectionFirstPage, row.FirstPageDifferent, SectionRowKind.FirstPage, null));

        var buttons = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(ActionButton(item, prefix + "Number", WriteStrings.SectionNumberFrom, SectionRowKind.NumberFrom, row.CanNumber ? null : WriteStrings.SectionNumberNoParagraph));
        if (item.Previous is { } previous)
        {
            var remove = ActionButton(item, prefix + "Remove", WriteStrings.SectionRemoveMarker, SectionRowKind.RemoveMarker, null);
            var tip = SectionsRowWording.RemoveTip(row, previous);
            buttons.Children.Add(remove);
            if (row.DiffersFrom(previous))
            {
                // The consequence is on the page, not in a tooltip alone: it is what the user must read before the marker goes.
                var note = Text("sections-warn");
                note.Text = tip;
                buttons.Children.Add(note);
            }

            AutomationProperties.SetItemStatus(remove, tip);
        }

        lines.Children.Add(buttons);
        var border = new Border { Child = lines, Padding = new Thickness(8, 6), Margin = new Thickness(0, 0, 0, 6) };
        border.Classes.Add("sections-row");
        var view = new RowView(border, state, start, SectionsRowWording.Title(row));
        view.Mark(item.IsActive);
        _rowViews[row.Index] = view;
        RowsBuilt++;
        return border;
    }

    private CheckBox Toggle(RowItem item, string name, string words, bool on, SectionRowKind kind, string? fixedReason)
    {
        var box = new CheckBox { Name = name, Content = words, IsChecked = on };
        AutomationProperties.SetName(box, SectionsRowWording.ButtonName(words, item.Row));
        box.Click += (_, _) => Act(item, kind, name);
        Register(box, () => fixedReason);
        return box;
    }

    private Button ActionButton(RowItem item, string name, string words, SectionRowKind kind, string? fixedReason)
    {
        var button = new Button { Name = name, Content = words, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Classes.Add("boxed");
        button.Classes.Add("wrap");
        AutomationProperties.SetName(button, SectionsRowWording.ButtonName(words, item.Row));
        button.Click += (_, _) => Act(item, kind, name);
        Register(button, () => fixedReason);
        return button;
    }

    private static TextBlock Line(string text)
    {
        var line = Text("sections-line");
        line.Text = text;
        return line;
    }

    private static TextBlock Text(string style)
    {
        var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        text.Classes.Add(style);
        return text;
    }

    // The parts of a row that show whether it is the active section: the border (class and name for assistive technology) and the line of words.
    private sealed class RowView(Border border, TextBlock state, TextBlock start, string title)
    {
        public TextBlock Start => start;

        public void Mark(bool active)
        {
            if (active)
            {
                border.Classes.Add("active");
            }
            else
            {
                border.Classes.Remove("active");
            }

            state.IsVisible = active;
            AutomationProperties.SetName(border, active ? WriteStrings.Fill(WriteStrings.SectionButtonNameFormat, title, WriteStrings.SectionCurrent) : title);
        }
    }

    /// <summary>One row of the list: the section as read, the one before it (for what Remove marker takes over) and whether it is the section the caret belongs to.</summary>
    internal sealed record RowItem(SectionRow Row, SectionRow? Previous, bool IsActive);
}
