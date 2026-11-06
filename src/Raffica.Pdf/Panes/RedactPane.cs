using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Redaction;
using Raffica.Pdf.Redacting;
using Raffica.Pdf.Viewing;
using Raffica.Shell.Frame;

namespace Raffica.Pdf.Panes;

/// <summary>
/// The pane of the redact mode, in the right slot instead of the marks pane while the mode is on: the counter ("4 marks on 2 pages"), the
/// hint that nothing is removed until the marks are applied, the marks grouped by page (kind and term for the user, but the name for assistive
/// technology is the page alone, never the marked text), a note for what was refused, and the primary button "Apply…" that appears with the
/// first mark. A click, Enter or Space on a row selects the mark and shows it on the page; Delete removes the selected marks. The Find panel
/// (<see cref="RedactFindPanel"/>) is made when it is first opened by <see cref="ToggleFind"/> and dropped, with its search, when it is closed or
/// the pane goes. The Look rows (fill, pictures, text on the marks, pages as images) are in <c>RedactPane.Look.cs</c>. Used on the UI thread only.
/// </summary>
internal sealed partial class RedactPane : UserControl, IDisposable
{
    private const int TermPreviewLength = 40;

    private readonly RedactSession _session;
    private readonly PageView _view;
    private readonly ListBox _list;
    private readonly TextBlock _counter;
    private readonly TextBlock _hint;
    private readonly TextBlock _note;
    private readonly ScrollViewer _findHost;
    private RedactFindPanel? _findPanel;
    private bool _syncing;
    private int _builtVersion = -1;
    private int _noteVersion;
    private IReadOnlyList<Row> _rows = [];
    private bool _disposed;

    public RedactPane(RedactSession session, PageView view)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        _session = session;
        _view = view;

        _counter = new TextBlock { Name = "PART_Counter", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(_counter, AutomationLiveSetting.Polite);
        _hint = new TextBlock { Name = "PART_Hint", Text = PdfStrings.RedactHint, TextWrapping = TextWrapping.Wrap };
        _hint.Classes.Add("nav-hint");
        _note = new TextBlock { Name = "PART_Note", TextWrapping = TextWrapping.Wrap, IsVisible = false };
        AutomationProperties.SetLiveSetting(_note, AutomationLiveSetting.Polite);
        _list = new ListBox
        {
            Name = "PART_List",
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<Row>((row, _) => RowView(row)),
        };
        _list.Classes.Add("nav-list");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(_list, PdfStrings.RedactListName);
        _list.ContainerPrepared += (_, e) =>
        {
            if (e.Container is ListBoxItem item && item.DataContext is Row row)
            {
                item.IsEnabled = !row.IsHeader;
                item.Focusable = !row.IsHeader;
                AutomationProperties.SetName(item, row.Name);
            }
        };
        _list.SelectionChanged += OnListSelectionChanged;
        _list.AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        ApplyButton = new Button { Name = "PART_Apply", Content = PdfStrings.RedactApplyButton, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        ApplyButton.Classes.Add("primary");
        ApplyButton.Click += (_, _) => ApplyRequested?.Invoke();

        BuildLook();
        var top = new StackPanel { Spacing = 6, Margin = new Thickness(12, 12, 12, 8), Children = { _counter, _hint, _lookToggle } };
        var bottom = new StackPanel { Spacing = 8, Margin = new Thickness(12, 8, 12, 12), Children = { _note, ApplyButton } };
        _findHost = new ScrollViewer { Name = "PART_FindHost", MaxHeight = 460, IsVisible = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto") };
        Grid.SetRow(top, 0);
        Grid.SetRow(_findHost, 1);
        Grid.SetRow(_lookScroll, 2);
        Grid.SetRow(_list, 3);
        Grid.SetRow(bottom, 4);
        grid.Children.Add(top);
        grid.Children.Add(_findHost);
        grid.Children.Add(_lookScroll);
        grid.Children.Add(_list);
        grid.Children.Add(bottom);
        Content = grid;

        _note.Bind(TextBlock.ForegroundProperty, _note.GetResourceObservable("Raffica.Brush.Text.Primary"));
        session.Changed += OnSessionChanged;
        session.Selection.Changed += SyncLook;
        view.ViewChanged += OnViewChanged;
        Refresh();
    }

    /// <summary>Raised when the user asks to apply the marks. Nothing listens yet: the apply flow arrives with its own step.</summary>
    public event Action? ApplyRequested;

    /// <summary>
    /// The "Recognize text…" command of the ribbon, given by the window; the Find panel offers it when pages are pictures. It runs only when it is
    /// wired and allowed, and the panel never starts it by itself.
    /// </summary>
    public Func<CommandDefinition?>? OcrCommand { get; set; }

    /// <summary>Whether the Find panel is open.</summary>
    public bool IsFindOpen => _findPanel is not null;

    /// <summary>The Find panel while it is open, else null (nothing exists before the first toggle).</summary>
    internal RedactFindPanel? FindPanel => _findPanel;

    /// <summary>Opens the Find panel, or closes it (dropping its search and hits).</summary>
    public void ToggleFind()
    {
        if (_disposed)
        {
            return;
        }

        if (_findPanel is { } open)
        {
            CloseFind(open);
            return;
        }

        var panel = new RedactFindPanel(new FindSession(_session), this);
        _findPanel = panel;
        _findHost.Content = panel;
        _findHost.IsVisible = true;
        panel.TermBox.Focus();
    }

    private void CloseFind(RedactFindPanel panel)
    {
        _findPanel = null;
        _findHost.IsVisible = false;
        _findHost.Content = null;
        var find = panel.Find;
        panel.Dispose();
        find.Dispose();
    }

    /// <summary>The primary button; visible once the plan has a mark.</summary>
    public Button ApplyButton { get; }

    /// <summary>The list of the marks (for tests).</summary>
    internal ListBox List => _list;

    /// <summary>The counter text ("4 marks on 2 pages").</summary>
    public string CounterText => _counter.Text ?? string.Empty;

    /// <summary>The hint that nothing is removed before the marks are applied.</summary>
    public string HintText => _hint.Text ?? string.Empty;

    /// <summary>What the last refusal said; empty when there is none to show.</summary>
    public string NoteText => _note.IsVisible ? _note.Text ?? string.Empty : string.Empty;

    /// <summary>The names of the rows in order: the header of a page, then the names of its marks (the page, never the marked text).</summary>
    public IReadOnlyList<string> RowNames => [.. _rows.Select(r => r.Name)];

    /// <summary>Selects a mark and shows it on the page (a click, Enter or Space on its row).</summary>
    public void Choose(Guid id)
    {
        _session.Selection.Select(id);
        RevealPrimary();
    }

    /// <summary>Removes the selected marks (the Delete key of the list).</summary>
    public void DeleteSelected()
    {
        if (_session.Selection.Count > 0)
        {
            Report(_session.Delete(new HashSet<Guid>(_session.Selection.Ids)));
        }
    }

    /// <summary>Says why something was refused (until the plan changes again).</summary>
    public void Report(RedactResult result)
    {
        if (result.IsDone)
        {
            return;
        }

        _note.Text = PdfStrings.RedactRefusal(result);
        _note.IsVisible = true;
        _noteVersion = _session.Version;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Changed -= OnSessionChanged;
        _session.Selection.Changed -= SyncLook;
        _view.ViewChanged -= OnViewChanged;
        if (_findPanel is { } panel)
        {
            CloseFind(panel);
        }
    }

    private static TextBlock RowView(Row? row)
    {
        var text = new TextBlock { Text = row?.Text ?? string.Empty, TextTrimming = TextTrimming.CharacterEllipsis };
        if (row?.IsHeader == true)
        {
            text.FontWeight = FontWeight.SemiBold;
            text.Classes.Add("nav-hint");
        }

        return text;
    }

    private void OnSessionChanged()
    {
        if (_note.IsVisible && _session.Version != _noteVersion)
        {
            _note.IsVisible = false;
        }

        Refresh();
    }

    private void Refresh()
    {
        var plan = _session.Plan;
        _counter.Text = PdfStrings.RedactCounter(plan.Marks.Count, plan.Pages.Count);
        ApplyButton.IsVisible = !plan.IsEmpty;
        if (_builtVersion != _session.Version)
        {
            _builtVersion = _session.Version;
            _rows = BuildRows(plan);
            _syncing = true;
            try
            {
                _list.ItemsSource = _rows;
            }
            finally
            {
                _syncing = false;
            }
        }

        SyncSelection();
        SyncLook();
    }

    private static List<Row> BuildRows(RedactionPlan plan)
    {
        var rows = new List<Row>();
        var page = -1;
        foreach (var mark in RedactSelection.ReadingOrder(plan))
        {
            if (mark.Page != page)
            {
                page = mark.Page;
                var header = PdfStrings.RedactPageHeaderFormat.Replace("{0}", (page + 1).ToString(PdfStrings.Culture), StringComparison.Ordinal);
                rows.Add(new Row(null, header, header, true));
            }

            rows.Add(new Row(mark.Id, TextOf(mark), RedactOverlay.NameOf(mark), false));
        }

        return rows;
    }

    // What the user reads in the row: the kind, and for a text mark the term (cut short). The name for assistive technology is the page alone.
    private static string TextOf(RedactionMark mark)
    {
        var kind = mark.Kind switch
        {
            RedactionKind.Text => PdfStrings.RedactKindText,
            RedactionKind.Hit => PdfStrings.RedactKindHit,
            _ => PdfStrings.RedactKindArea,
        };
        if (mark.Term is not { Length: > 0 } term)
        {
            return kind;
        }

        var shown = term.Length > TermPreviewLength ? term[..TermPreviewLength] + "…" : term;
        return PdfStrings.RedactRowTextFormat.Replace("{0}", kind, StringComparison.Ordinal).Replace("{1}", shown, StringComparison.Ordinal);
    }

    private void SyncSelection()
    {
        _syncing = true;
        try
        {
            var wanted = _rows.Where(r => r.Id is { } id && _session.Selection.Contains(id)).ToList();
            var selected = _list.SelectedItems!;
            var same = selected.Count == wanted.Count && wanted.All(selected.Contains);
            if (!same)
            {
                selected.Clear();
                foreach (var row in wanted)
                {
                    selected.Add(row);
                }
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _disposed)
        {
            return;
        }

        _session.Selection.Set(_list.SelectedItems!.OfType<Row>().Where(r => r.Id is not null).Select(r => r.Id!.Value));
        RevealPrimary();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (e.Key is Key.Delete or Key.Back && _session.Selection.Count > 0)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _session.Selection.Primary is not null)
        {
            RevealPrimary();
            e.Handled = true;
        }
    }

    private void RevealPrimary()
    {
        if (_session.Selection.Primary is { } id && _session.Plan.Marks.FirstOrDefault(m => m.Id == id) is { } mark)
        {
            _view.Reveal(mark.Page, mark.Areas[0]);
        }
    }

    private sealed record Row(Guid? Id, string Text, string Name, bool IsHeader);
}
