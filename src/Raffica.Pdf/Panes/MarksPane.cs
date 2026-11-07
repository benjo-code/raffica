using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Annotations;
using Raffica.Core.Pdf;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Ocr;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Viewing;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Panes;

/// <summary>
/// The marks pane of one document tab (256 px, the right slot of the frame): a filter, the list of the marks, a summary line and, below
/// the list, the comment of the selected mark. Click or Enter on an entry reveals the mark in the document and selects it there (it enters
/// the Select tool when the annotate mode is off, as long as the document allows changing annotations); Delete inside the mode deletes
/// the mark of the entry. The comment is a plain-text field: Ctrl+Enter applies it (an empty text removes the comment), Esc discards the
/// edit, Ctrl+Z undoes typing inside the field. A mark that cannot be changed shows the reason instead of the field. Enter or Space on a
/// group row of many freehand strokes opens or closes the group.
/// </summary>
internal sealed class MarksPane : UserControl, IDisposable
{
    private readonly MarksModel _model;
    private readonly PdfTab _tab;
    private readonly PageView _view;
    private readonly ToolState _tools;
    private readonly SelectController _select;
    private readonly DialogQueue? _dialogs;
    private readonly Func<bool> _selectAllowed;
    private readonly ObservableCollection<MarkRow> _rows = [];
    private readonly SelectBox _filter;
    private readonly ListBox _list;
    private readonly HashSet<Key> _heldKeys = [];
    private readonly TextBlock _empty;
    private readonly TextBlock _summary;
    private readonly TextBlock _note;
    private readonly TextBox _comment;
    private readonly TextBlock _commentHint;
    private readonly TextBlock _help;
    private Task _work = Task.CompletedTask;
    private AnnotationLocator? _editFor;
    private string _editBase = string.Empty;
    private IDisposable? _unresolved;
    private Window? _deactivatedHost;
    private int _applying;
    private bool _rebuildPosted;
    private bool _syncing;
    private bool _disposed;

    /// <param name="model">The marks of the document.</param>
    /// <param name="tab">The document.</param>
    /// <param name="view">The page view that shows the marks.</param>
    /// <param name="tools">The tool model of the annotate mode.</param>
    /// <param name="select">The select controller of the document (selection, delete).</param>
    /// <param name="dialogs">Where a failure is told on a card; <see langword="null"/>: nowhere (tests).</param>
    /// <param name="selectAllowed">Whether the document allows the annotate mode (null: yes); a click only reveals when it does not.</param>
    /// <param name="otherModeLock">The reason a mode of the tab that is not a plan or a recognition (Edit text) locks the marks, or null when none does (null: none).</param>
    public MarksPane(MarksModel model, PdfTab tab, PageView view, ToolState tools, SelectController select, DialogQueue? dialogs = null, Func<bool>? selectAllowed = null, Func<string?>? otherModeLock = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(select);
        _model = model;
        _tab = tab;
        _view = view;
        _tools = tools;
        _select = select;
        _dialogs = dialogs;
        _otherModeLock = otherModeLock;
        _selectAllowed = selectAllowed ?? (() => true);

        _filter = new SelectBox
        {
            ItemsSource = new[]
            {
                PdfStrings.MarkFilterAll, PdfStrings.MarkFilterHighlight, PdfStrings.MarkFilterUnderline,
                PdfStrings.MarkFilterStrikeOut, PdfStrings.MarkFilterDrawings, PdfStrings.MarkFilterOther,
            },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 8),
        };
        AutomationProperties.SetName(_filter, PdfStrings.MarkFilterName);
        _filter.SelectionChanged += (_, _) => Rebuild();

        _list = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            ItemsSource = _rows,
            ItemTemplate = new FuncDataTemplate<MarkRow>((row, _) => Build(row)),
        };
        _list.Classes.Add("nav-list");
        _list.Classes.Add("marks-list");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(_list, PdfStrings.PaneMarks);
        _list.SelectionChanged += (_, _) => OnListSelectionChanged();
        _list.ContainerPrepared += (_, e) => Decorate(e.Container, e.Index);
        _list.AddHandler(PointerPressedEvent, (_, _) => _heldKeys.Clear(), RoutingStrategies.Tunnel, handledEventsToo: true);
        _list.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        _list.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        _list.AddHandler(KeyUpEvent, (_, e) => _heldKeys.Remove(e.Key), RoutingStrategies.Tunnel, handledEventsToo: true);

        // A key whose release never reaches the list (the focused row went away with a delete, the focus moved on) is not held any more:
        // when the focus has left the list the flag is dropped. A move from row to row stays inside and keeps it.
        _list.AddHandler(LostFocusEvent, (_, _) => Dispatcher.UIThread.Post(DropHeldKeysWhenFocusLeft), RoutingStrategies.Bubble, handledEventsToo: true);
        _list.DetachedFromVisualTree += (_, _) =>
        {
            _heldKeys.Clear();
            UnhookDeactivated();
        };
        _list.AttachedToVisualTree += (_, _) => HookDeactivated();
        _list.AddHandler(GotFocusEvent, (_, e) => NavList.EnterAt(_list, e, () => Math.Max(0, _list.SelectedIndex)), RoutingStrategies.Bubble, handledEventsToo: true);

        _empty = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Margin = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Top,
            Focusable = true,
            IsTabStop = true,
        };
        _empty.Classes.Add("nav-hint");

        _summary = new TextBlock { Margin = new Thickness(0, 6, 0, 6), TextWrapping = TextWrapping.Wrap };
        _summary.Classes.Add("secondary");
        _summary.Classes.Add("small");

        _note = new TextBlock { IsVisible = false, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        _note.Classes.Add("ui");

        _comment = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            MaxHeight = 160,
            IsVisible = false,
        };
        AutomationProperties.SetName(_comment, PdfStrings.MarkCommentName);
        _comment.AddHandler(KeyDownEvent, OnCommentKeyDown, RoutingStrategies.Tunnel);
        _comment.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                UpdateUnresolved();
            }
        };

        _commentHint = new TextBlock { Text = PdfStrings.MarkCommentHint, IsVisible = false, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        _commentHint.Classes.Add("secondary");
        _commentHint.Classes.Add("small");

        _help = new TextBlock { Text = PdfStrings.MarksHelp, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        _help.Classes.Add("secondary");
        _help.Classes.Add("small");

        var listArea = new Grid();
        listArea.Children.Add(_list);
        listArea.Children.Add(_empty);

        var editor = new StackPanel { Children = { _note, _comment, _commentHint } };

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto") };
        grid.Children.Add(_filter);
        grid.Children.Add(listArea);
        grid.Children.Add(_summary);
        grid.Children.Add(editor);
        grid.Children.Add(_help);
        Grid.SetRow(listArea, 1);
        Grid.SetRow(_summary, 2);
        Grid.SetRow(editor, 3);
        Grid.SetRow(_help, 4);
        Content = grid;

        _model.Changed += OnModelChanged;
        _select.SelectionChanged += OnDocumentSelectionChanged;
        _tab.Annotations.FlushRequested += OnFlushRequested;

        // The comments are read-only while the page plan is open; the session is not made here (it is made on the first Pages command).
        _tab.OrganizeCreated += AttachPlan;
        if (_tab.OrganizeIfCreated is { } existing)
        {
            AttachPlan(existing);
        }

        // Likewise while a text recognition runs (its session is not made here either).
        _tab.OcrCreated += AttachOcr;
        if (_tab.OcrIfCreated is { } running)
        {
            AttachOcr(running);
        }

        Rebuild();
    }

    private OrganizeSession? _plan;

    // The page plan of the tab is open: the marks are shown but not changed (the plan owns Undo and Redo then).
    private bool PlanLocked => _plan is { IsActive: true } || OcrLocked || _otherModeLock?.Invoke() is not null;

    private readonly Func<string?>? _otherModeLock;

    private OcrSession? _ocr;

    // A text recognition runs: the same lock, with the reason of the run.
    private bool OcrLocked => _ocr is { IsActive: true };

    private void AttachOcr(OcrSession session)
    {
        _ocr = session;
        session.Changed += OnPlanChanged;
    }

    private void AttachPlan(OrganizeSession session)
    {
        _plan = session;
        session.Changed += OnPlanChanged;
    }

    private void OnPlanChanged()
    {
        if (!_disposed && _comment.IsReadOnly != PlanLocked)
        {
            UpdateEditor();
        }
    }

    /// <summary>The list of marks (for tests).</summary>
    internal ListBox List => _list;

    /// <summary>The filter (for tests).</summary>
    internal SelectBox Filter => _filter;

    /// <summary>The comment field of the selected mark (for tests).</summary>
    internal TextBox CommentEditor => _comment;

    /// <summary>The note with the reason a mark cannot be changed (for tests).</summary>
    internal TextBlock Note => _note;

    /// <summary>The summary line (for tests).</summary>
    internal TextBlock Summary => _summary;

    /// <summary>The text shown instead of the list while it is empty (for tests).</summary>
    internal TextBlock EmptyText => _empty;

    /// <summary>The help text under the list (for tests).</summary>
    internal TextBlock Help => _help;

    /// <summary>The marks of the document.</summary>
    internal MarksModel Model => _model;

    /// <summary>Replaces the engine call that applies a comment (tests make it fail or wait); <see langword="null"/>: the editor of the tab.</summary>
    internal Func<AnnotationLocator, string, Task>? UpdateComment { get; set; }

    /// <summary>Whether the comment field holds text that was typed and not applied to the mark yet (the tab counts as changed then).</summary>
    internal bool HasUnappliedComment => HasUnappliedText;

    /// <summary>How many window subscriptions for the loss of activation the pane holds now (0 while detached, else 1; for tests).</summary>
    internal int DeactivationHooks => _deactivatedHost is null ? 0 : 1;

    /// <summary>The rows of the list at the moment.</summary>
    internal int RowCount => _rows.Count;

    /// <summary>The filter in force.</summary>
    internal MarkFilter CurrentFilter => _filter.SelectedIndex is >= 0 and var index && index <= (int)MarkFilter.Other ? (MarkFilter)index : MarkFilter.All;

    /// <summary>The task that ends when every change started by the pane so far is done (for tests).</summary>
    internal Task Completion => _work;

    /// <summary>The mark of a row, or <see langword="null"/> for a group row.</summary>
    internal MarkEntry? EntryAt(int row) => row >= 0 && row < _rows.Count ? (_rows[row] as EntryRow)?.Entry : null;

    /// <summary>The text of a group row, or <see langword="null"/> for an entry.</summary>
    internal string? GroupTextAt(int row) => row >= 0 && row < _rows.Count ? (_rows[row] as GroupRow)?.Text : null;

    /// <summary>The list item of a row, or <see langword="null"/> when it is not realised.</summary>
    internal ListBoxItem? ContainerOf(int row) => _list.ContainerFromIndex(row) as ListBoxItem;

    /// <summary>
    /// Reveals the mark of an entry in the document and selects it there: when the annotate mode is off (or another tool is active) the
    /// Select tool starts, unless the document does not allow changing annotations, then the mark is only revealed. Never throws.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The task that ends when the mark is selected.</returns>
    internal Task ChooseAsync(MarkEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_disposed)
        {
            return _work;
        }

        _view.Reveal(entry.Page, entry.Annotation.SelectionShape?.Box ?? entry.Annotation.Bounds);
        if (DrawingHitTest.IsSelectable(entry.Annotation) && _selectAllowed() && !PlanLocked)
        {
            if (_tools.Tool != AnnotateTool.Select)
            {
                _tools.Choose(AnnotateTool.Select);
            }

            Run(() => _select.SelectAsync(entry.Locator));
        }

        return _work;
    }

    /// <summary>
    /// Deletes the mark of an entry, only inside the annotate mode (outside it nothing happens). The mark is selected first and deleted
    /// through the select controller, so the delete is one history step like the Delete key in the document.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The task that ends when the mark is deleted.</returns>
    internal Task DeleteAsync(MarkEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_disposed || PlanLocked || _tools.Tool == AnnotateTool.None || !DrawingHitTest.IsSelectable(entry.Annotation))
        {
            return _work;
        }

        Run(async () =>
        {
            await _select.SelectAsync(entry.Locator);
            if (_select.Selected is { } selected && entry.IsFor(new AnnotationLocator(selected.Page, selected.Index, selected.Name)))
            {
                await _select.DeleteSelectionAsync();
            }
        });
        return _work;
    }

    /// <summary>
    /// Applies the text of the comment field to the selected mark (an empty text removes the comment) as one history step. Nothing
    /// happens when the text is the one the mark has, or is already on its way (a second Ctrl+Enter before the list has read the mark again
    /// does not repeat the change). The tab counts as dirty from the start until the change is made.
    /// </summary>
    /// <returns>The task that ends when the change is made.</returns>
    internal Task ApplyCommentAsync()
    {
        if (_disposed || PlanLocked || _editFor is not { } locator || !_comment.IsVisible)
        {
            return _work;
        }

        var text = TypedText;
        if (text == _editBase)
        {
            return _work;
        }

        var pending = _tab.Annotations.BeginPending();
        var previous = _editBase;

        // The text counts as applied from now on; a failure takes that back.
        _editBase = text;
        UpdateUnresolved();
        _applying++;
        Run(() => ApplyAsync(locator, text, previous, pending));
        return _work;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _model.Changed -= OnModelChanged;
        _select.SelectionChanged -= OnDocumentSelectionChanged;
        _tab.Annotations.FlushRequested -= OnFlushRequested;
        _tab.OrganizeCreated -= AttachPlan;
        _tab.OcrCreated -= AttachOcr;
        if (_plan is not null)
        {
            _plan.Changed -= OnPlanChanged;
        }

        if (_ocr is not null)
        {
            _ocr.Changed -= OnPlanChanged;
        }

        UnhookDeactivated();
        ReleaseUnresolved();
    }

    // A window that loses the activation drops the keys held in the list (their release will not arrive). One subscription at a time,
    // taken when the list enters a window and given back when it leaves it, so a pane that is shown again and again does not pile them up.
    private void HookDeactivated()
    {
        UnhookDeactivated();
        if (!_disposed && TopLevel.GetTopLevel(_list) is Window window)
        {
            _deactivatedHost = window;
            window.Deactivated += OnWindowDeactivated;
        }
    }

    private void UnhookDeactivated()
    {
        if (_deactivatedHost is { } window)
        {
            _deactivatedHost = null;
            window.Deactivated -= OnWindowDeactivated;
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => _heldKeys.Clear();

    private async Task ApplyAsync(AnnotationLocator locator, string text, string previousBase, IDisposable pending)
    {
        string? failure = null;
        var applied = false;
        var gone = false;
        try
        {
            try
            {
                await (UpdateComment is { } update ? update(locator, text) : _tab.Annotations.Editor.UpdateAsync(locator, new AnnotationEdit(Contents: text)));
                applied = true;
            }
            catch (OperationCanceledException)
            {
                // The document went away: nothing more to do.
            }
            catch (PdfAnnotationNotFoundException)
            {
                // The mark is gone (undone or deleted meanwhile): the text has no mark to go to, and the card says so.
                gone = true;
                failure = PdfStrings.MarkCommentFailed;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failure = PdfStrings.MarkCommentFailed;
            }

            if (applied || gone)
            {
                // The list reads the mark again now, so the field is in line with what the document has before anything else happens.
                await _model.RefreshAsync(new HashSet<int> { locator.Page });
            }
        }
        finally
        {
            pending.Dispose();
            _applying--;
        }

        if (!_disposed)
        {
            if (!applied && !gone && _editFor is { } shown && SameMark(shown, locator) && _editBase == text)
            {
                // Not applied: the mark is as it was and the typed text stays in the field, still counted as an unsaved change.
                _editBase = previousBase;
            }
            else if (gone && _editFor is { } vanished && SameMark(vanished, locator))
            {
                // The mark is gone: nothing to apply to, so the text is not tried again (the card has told).
                _editBase = text;
            }

            if (applied || gone)
            {
                Rebuild();
            }
            else
            {
                UpdateUnresolved();
            }
        }

        if (failure is not null && !_disposed && _dialogs is { } dialogs)
        {
            // The card is not waited for: the work of the pane goes on behind it (the comment is retried with the next Ctrl+Enter).
            _ = ShowNoticeAsync(dialogs, failure);
        }
    }

    private static async Task ShowNoticeAsync(DialogQueue dialogs, string message)
    {
        try
        {
            await dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.AnnotateCardTitle, message, () => done(true)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The card could not be shown; the comment was not changed either way.
        }
    }

    private static bool SameMark(AnnotationLocator a, AnnotationLocator b) =>
        a.Page == b.Page && (a.Name is not null ? a.Name == b.Name : b.Name is null && a.Index == b.Index);

    // The text of the field as it would be applied (a blank text is no comment).
    private string TypedText => string.IsNullOrWhiteSpace(_comment.Text) ? string.Empty : _comment.Text!;

    private bool HasUnappliedText => _editFor is not null && _comment.IsVisible && TypedText != _editBase;

    // While the field holds text that is not applied, the tab counts as changed (closing it asks, Save is possible); the flush of a save or
    // a close applies the text first.
    private void UpdateUnresolved()
    {
        if (_disposed)
        {
            return;
        }

        if (HasUnappliedText)
        {
            _unresolved ??= _tab.Annotations.BeginUnresolved();
        }
        else
        {
            ReleaseUnresolved();
        }
    }

    private void ReleaseUnresolved()
    {
        if (_unresolved is { } held)
        {
            _unresolved = null;
            held.Dispose();
        }
    }

    private void OnFlushRequested()
    {
        if (!_disposed && HasUnappliedText)
        {
            _ = ApplyCommentAsync();
        }
    }

    // Work of the pane runs one after the other and never faults the chain.
    private void Run(Func<Task> work) => _work = RunAfterAsync(_work, work);

    private static async Task RunAfterAsync(Task previous, Func<Task> work)
    {
        await previous;
        try
        {
            await work();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The select controller and the editor report their own failures; a mark that went away needs nothing more.
        }
    }

    private void OnModelChanged()
    {
        if (_rebuildPosted || _disposed)
        {
            return;
        }

        // Many pages arrive one by one on a long first read: the list is rebuilt once per turn of the loop, not once per page.
        _rebuildPosted = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _rebuildPosted = false;
                if (!_disposed)
                {
                    Rebuild();
                }
            },
            DispatcherPriority.Input);
    }

    private void Rebuild()
    {
        var filter = CurrentFilter;
        var key = KeyOf(_list.SelectedItem as MarkRow);
        var rows = _model.Rows(filter);
        _syncing = true;
        try
        {
            Sync(rows);
            if (key is not null)
            {
                var again = _rows.ToList().FindIndex(r => KeyOf(r) == key);
                if (again != _list.SelectedIndex)
                {
                    _list.SelectedIndex = again;
                }
            }
        }
        finally
        {
            _syncing = false;
        }

        var (marks, _, _) = _model.Count(filter);
        _summary.Text = _model.Summary(filter);
        _summary.IsVisible = marks > 0;
        _empty.IsVisible = _rows.Count == 0 && _model.IsLoaded;
        _empty.Text = _model.HasMarks ? PdfStrings.MarksNoneOfKind : PdfStrings.MarksEmpty;
        if (_empty.IsVisible)
        {
            AutomationProperties.SetName(_empty, _empty.Text);
        }

        UpdateEditor();
    }

    // Brings the rows in line with the new ones by replacing only what differs, so that the list keeps its scroll position.
    private void Sync(IReadOnlyList<MarkRow> rows)
    {
        var start = 0;
        while (start < _rows.Count && start < rows.Count && Equals(_rows[start], rows[start]))
        {
            start++;
        }

        var oldEnd = _rows.Count;
        var newEnd = rows.Count;
        while (oldEnd > start && newEnd > start && Equals(_rows[oldEnd - 1], rows[newEnd - 1]))
        {
            oldEnd--;
            newEnd--;
        }

        for (var i = oldEnd - 1; i >= start; i--)
        {
            _rows.RemoveAt(i);
        }

        for (var i = start; i < newEnd; i++)
        {
            _rows.Insert(i, rows[i]);
        }
    }

    private static string? KeyOf(MarkRow? row) => row switch
    {
        EntryRow e => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"e{e.Entry.Page}/{e.Entry.Locator.Name ?? "#" + e.Entry.Locator.Index}"),
        GroupRow g => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"g{g.Page}"),
        _ => null,
    };

    private void OnListSelectionChanged()
    {
        if (!_syncing)
        {
            UpdateEditor();
        }
    }

    // The comment field and the note follow the selected row. Typing is kept while the same mark stays selected. When another mark (or none)
    // is selected, the text that was typed and not applied is applied to the mark it was typed for first (as if the field lost its focus),
    // so a change of the selection never drops it; if that cannot be done the card says so.
    private void UpdateEditor()
    {
        _comment.IsReadOnly = PlanLocked;
        var entry = _list.SelectedItem is EntryRow row ? row.Entry : null;
        var stays = entry is { CanComment: true } && _editFor is { } current && entry.IsFor(current);
        if (!stays && HasUnappliedText)
        {
            _ = ApplyCommentAsync();
        }

        if (entry is null)
        {
            _editFor = null;
            _editBase = string.Empty;
            _comment.IsVisible = false;
            _commentHint.IsVisible = false;
            _note.IsVisible = false;
            UpdateUnresolved();
            return;
        }

        var note = PlanLocked && entry.CanComment ? (OcrLocked ? PdfStrings.ModeOcrReason : _otherModeLock?.Invoke() ?? PdfStrings.PageChangesLockedReason) : entry.LockReason ?? (entry.Annotation.Kind == AnnotationKind.TextBox && entry.Annotation.Editable ? PdfStrings.MarkTextBoxNote : null);
        _note.Text = note;
        _note.IsVisible = note is not null;
        _comment.IsVisible = entry.CanComment;
        _commentHint.IsVisible = entry.CanComment;
        if (!entry.CanComment)
        {
            _editFor = null;
            _editBase = string.Empty;
            UpdateUnresolved();
            return;
        }

        var contents = entry.Comment ?? string.Empty;
        if (stays)
        {
            // A change on its way owns the field; a comment that changed meanwhile (an undo) becomes the new base and the typed text stays.
            if (_applying > 0 || _editBase == contents)
            {
                return;
            }

            var typed = HasUnappliedText;
            _editBase = contents;
            if (!typed)
            {
                _comment.Text = contents;
            }

            UpdateUnresolved();
            return;
        }

        _editFor = entry.Locator;
        _editBase = contents;
        _comment.Text = contents;
        UpdateUnresolved();
    }

    // The mark was selected (or the selection ended) in the document: the list follows without choosing anything again.
    private void OnDocumentSelectionChanged()
    {
        if (_select.Selected is not { } selected)
        {
            return;
        }

        var locator = new AnnotationLocator(selected.Page, selected.Index, selected.Name);
        var row = _rows.ToList().FindIndex(r => r is EntryRow e && e.Entry.IsFor(locator));
        if (row < 0 || row == _list.SelectedIndex)
        {
            return;
        }

        _list.SelectedIndex = row;
        _list.ScrollIntoView(row);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed
            || e.Source is not Visual source
            || source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is not { } item
            || _list.IndexFromContainer(item) is not (>= 0 and var index) || index >= _rows.Count)
        {
            return;
        }

        _list.SelectedIndex = index;
        Act(_rows[index]);
    }

    private void DropHeldKeysWhenFocusLeft()
    {
        var focused = TopLevel.GetTopLevel(_list)?.FocusManager?.GetFocusedElement();
        if (focused is not Visual visual || !(ReferenceEquals(visual, _list) || _list.IsVisualAncestorOf(visual)))
        {
            _heldKeys.Clear();
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || NavList.RowForKey(_list) is not (>= 0 and var index) || index >= _rows.Count)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
            case Key.Space:
                e.Handled = true;
                Act(_rows[index]);
                break;
            case Key.Delete or Key.Back:
                // Avalonia does not say whether a key down is a repeat: a key that is held down deletes once per physical press.
                var repeat = !_heldKeys.Add(e.Key);
                if (_rows[index] is EntryRow entry && _tools.Tool != AnnotateTool.None)
                {
                    e.Handled = true;
                    if (!repeat)
                    {
                        _ = DeleteAsync(entry.Entry);
                    }
                }

                break;
        }
    }

    // Click or Enter on a row: a group opens or closes, an entry is shown and selected in the document.
    private void Act(MarkRow row)
    {
        switch (row)
        {
            case GroupRow group:
                _model.ToggleGroup(group.Page);
                break;
            case EntryRow entry:
                _ = ChooseAsync(entry.Entry);
                break;
        }
    }

    private void OnCommentKeyDown(object? sender, KeyEventArgs e)
    {
        var applies = e.Key is Key.Enter or Key.Return && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta));
        if (applies)
        {
            e.Handled = true;
            _ = ApplyCommentAsync();
        }
        else if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            // The edit is dropped: the field shows what the mark has, and the keyboard returns to the list.
            e.Handled = true;
            _comment.Text = _editBase;
            _list.ContainerFromIndex(Math.Max(0, _list.SelectedIndex))?.Focus(NavigationMethod.Tab);
        }

        // Every other key (Ctrl+Z, Ctrl+A, the arrows) belongs to the field.
    }

    private void Decorate(Control container, int index)
    {
        if (index >= 0 && index < _rows.Count)
        {
            AutomationProperties.SetName(container, NameOf(_rows[index]));
        }
    }

    private static string NameOf(MarkRow row)
    {
        switch (row)
        {
            case GroupRow group:
                return group.Text + ", " + PdfStrings.Fill(PdfStrings.MarkPageTagFormat, group.Page + 1) + ", " + (group.Expanded ? PdfStrings.OutlineExpanded : PdfStrings.OutlineCollapsed);
            case EntryRow { Entry: var e }:
                var parts = new List<string>
                {
                    e.KindText,
                    PdfStrings.Fill(PdfStrings.MarkPageTagFormat, e.Page + 1),
                };
                AddIfAny(parts, e.Annotation.Author);
                AddIfAny(parts, e.Excerpt);
                AddIfAny(parts, e.Comment);
                if (e.LockReason is not null)
                {
                    parts.Add(PdfStrings.MarkLockedLabel);
                }

                return string.Join(", ", parts);
            default:
                return string.Empty;
        }
    }

    private static void AddIfAny(List<string> parts, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            parts.Add(text);
        }
    }

    // One row of the list: the edge that marks the selected row by its shape (the tint comes from the style), then the lines of the entry.
    private static Control Build(MarkRow row) => row switch
    {
        GroupRow group => BuildGroup(group),
        EntryRow entry => BuildEntry(entry),
        _ => new Border(),
    };

    private static Grid BuildGroup(GroupRow group)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        line.Children.Add(Icon(group.Expanded ? "ChevronDown" : "ChevronRight"));
        line.Children.Add(PageTag(group.Page));
        line.Children.Add(new TextBlock { Text = group.Text, VerticalAlignment = VerticalAlignment.Center, Classes = { "ui" } });
        return WithEdge(line, inGroup: false);
    }

    private static Grid BuildEntry(EntryRow row)
    {
        var entry = row.Entry;
        var stack = new StackPanel { Spacing = 2 };

        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        head.Children.Add(PageTag(entry.Page));
        head.Children.Add(Icon(IconOf(entry.Annotation.Kind)));
        head.Children.Add(new TextBlock { Text = entry.KindText, VerticalAlignment = VerticalAlignment.Center, Classes = { "ui-medium" } });
        if ((entry.Annotation.Color ?? entry.Annotation.TextBox?.Color) is { } color)
        {
            head.Children.Add(new Border
            {
                Width = 12,
                Height = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)),
                Classes = { "mark-swatch" },
            });
        }

        if (entry.LockReason is not null)
        {
            head.Children.Add(Icon("TriangleAlert"));
        }

        stack.Children.Add(head);

        var who = string.Join(" · ", new[] { entry.Annotation.Author, entry.Annotation.Modified?.ToLocalTime().ToString("g", PdfStrings.Culture) }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (who.Length > 0)
        {
            stack.Children.Add(new TextBlock { Text = who, TextWrapping = TextWrapping.Wrap, Classes = { "secondary", "small" } });
        }

        if (entry.Excerpt is { } excerpt)
        {
            stack.Children.Add(new TextBlock { Text = excerpt, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "ui" } });
        }

        if (entry.Comment is { } comment)
        {
            stack.Children.Add(new Border
            {
                Child = new TextBlock { Text = comment, TextWrapping = TextWrapping.Wrap, MaxLines = 4, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "ui" } },
                Classes = { "mark-comment" },
            });
        }

        return WithEdge(stack, row.InGroup);
    }

    private static Grid WithEdge(Control content, bool inGroup)
    {
        var edge = new Border { Width = 3, Margin = new Thickness(0, 0, 6, 0), Classes = { "mark-edge" } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(inGroup ? 16 : 0, 0, 0, 0) };
        grid.Children.Add(edge);
        grid.Children.Add(content);
        Grid.SetColumn(content, 1);
        return grid;
    }

    private static TextBlock PageTag(int page)
    {
        var tag = new TextBlock { Text = PdfStrings.Fill(PdfStrings.MarkPageTagFormat, page + 1), VerticalAlignment = VerticalAlignment.Center };
        tag.Classes.Add("secondary");
        tag.Classes.Add("small");
        tag.Classes.Add("tnum");
        return tag;
    }

    private static StrokeIcon Icon(string name)
    {
        var icon = new StrokeIcon { Size = 16, VerticalAlignment = VerticalAlignment.Center };
        icon.Bind(StrokeIcon.DataProperty, icon.GetResourceObservable("Raffica.Icon." + name, value => value as Geometry));
        return icon;
    }

    // The icon set has no marker, pen or shape icons yet: the closest of the set stand in, as in the Annotate tab.
    private static string IconOf(AnnotationKind kind) => kind switch
    {
        AnnotationKind.Highlight => "Bold",
        AnnotationKind.Underline => "Underline",
        AnnotationKind.StrikeOut => "Minus",
        AnnotationKind.Ink => "Plus",
        AnnotationKind.Rectangle => "AlignLeft",
        AnnotationKind.Ellipse => "AlignCenter",
        AnnotationKind.Line => "Minus",
        AnnotationKind.Arrow => "ChevronRight",
        AnnotationKind.TextBox => "Italic",
        AnnotationKind.Signature => "Plus",
        _ => "Info",
    };
}
