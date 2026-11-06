using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Redaction;
using Raffica.Core.Text;
using Raffica.Pdf.Redacting;
using Raffica.Shell.Frame;

namespace Raffica.Pdf.Panes;

/// <summary>
/// The Find panel of the redact pane: a term (searched when Enter is pressed or Search is clicked, never while typing) with "Match case" and
/// "Whole words", or the "Patterns" mode with the four kinds, a list of the hits with a tick each, and the buttons that turn hits into marks.
/// It says in words when a search was canceled, which pages it could not read (marking stays off until the user accepts that, for that one search),
/// that pages are pictures (with the "Recognize text…" command of the Edit tab, which this panel never starts by itself) and what is not
/// searched at all. Hit texts are shown to the user, but the names for assistive technology are the page alone. UI thread only.
/// </summary>
internal sealed class RedactFindPanel : UserControl, IDisposable
{
    private const int TextPreviewLength = 60;

    private static readonly RedactionPatternKind[] s_kinds = [RedactionPatternKind.Email, RedactionPatternKind.Iban, RedactionPatternKind.Phone, RedactionPatternKind.Card];

    private readonly FindSession _find;
    private readonly RedactPane _pane;
    private readonly TextBox _term;
    private readonly CheckBox _matchCase;
    private readonly CheckBox _wholeWords;
    private readonly ToggleButton _patterns;
    private readonly Dictionary<RedactionPatternKind, CheckBox> _kindBoxes = [];
    private readonly Button _search;
    private readonly Button _cancel;
    private readonly TextBlock _status;
    private readonly TextBlock _note;
    private readonly TextBlock _caution;
    private readonly Button _acknowledge;
    private readonly TextBlock _pictures;
    private readonly Button _ocr;
    private readonly ListBox _list;
    private readonly Button _markAll;
    private readonly Button _markTicked;
    private readonly Button _addFailed;
    private readonly ToggleButton _notSearchedToggle;
    private readonly StackPanel _notSearched;
    private IReadOnlyList<FindRow> _rows = [];
    private FindState _builtState = FindState.Idle;
    private int _builtCount = -1;
    private bool _builtPatterns;
    private bool _disposed;

    public RedactFindPanel(FindSession find, RedactPane pane)
    {
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(pane);
        _find = find;
        _pane = pane;

        _term = new TextBox { Name = "PART_FindTerm", PlaceholderText = PdfStrings.FindTermName };
        AutomationProperties.SetName(_term, PdfStrings.FindTermName);
        _term.AddHandler(KeyDownEvent, OnTermKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _matchCase = new CheckBox { Name = "PART_FindMatchCase", Content = PdfStrings.FindMatchCase };
        _wholeWords = new CheckBox { Name = "PART_FindWholeWords", Content = PdfStrings.FindWholeWords };
        _patterns = new ToggleButton { Name = "PART_FindPatterns", Content = PdfStrings.FindPatternsMode, HorizontalAlignment = HorizontalAlignment.Left };
        _patterns.IsCheckedChanged += (_, _) => Refresh();

        var kindPanel = new StackPanel { Spacing = 2 };
        foreach (var kind in s_kinds)
        {
            var box = new CheckBox { Name = "PART_FindKind" + kind, Content = PdfStrings.FindKindText(kind), IsChecked = true };
            _kindBoxes[kind] = box;
            kindPanel.Children.Add(box);
        }

        _search = new Button { Name = "PART_FindSearch", Content = PdfStrings.FindSearchButton };
        _search.Classes.Add("primary");
        _search.Click += (_, _) => StartSearch();
        _cancel = new Button { Name = "PART_FindCancel", Content = PdfStrings.FindCancelButton, IsVisible = false };
        _cancel.Click += (_, _) => _find.Cancel();

        _status = new TextBlock { Name = "PART_FindStatus", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _note = new TextBlock { Name = "PART_FindNote", TextWrapping = TextWrapping.Wrap, IsVisible = false };
        AutomationProperties.SetLiveSetting(_note, AutomationLiveSetting.Polite);
        _caution = new TextBlock { Name = "PART_FindCaution", Text = PdfStrings.FindPatternCaution, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        _caution.Classes.Add("nav-hint");
        _acknowledge = new Button { Name = "PART_FindAcknowledge", Content = PdfStrings.FindAcknowledge, IsVisible = false };
        _acknowledge.Click += (_, _) => _find.Acknowledge();
        _pictures = new TextBlock { Name = "PART_FindPictures", TextWrapping = TextWrapping.Wrap, IsVisible = false };
        _ocr = new Button { Name = "PART_FindOcr", Content = PdfStrings.CmdOcrRun, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Left };
        _ocr.Click += (_, _) => RunOcr();

        _list = new ListBox
        {
            Name = "PART_FindList",
            MaxHeight = 220,
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new FuncDataTemplate<FindRow>((row, _) => RowView(row)),
        };
        _list.Classes.Add("nav-list");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(_list, PdfStrings.FindListName);
        _list.ContainerPrepared += (_, e) =>
        {
            if (e.Container is ListBoxItem item && item.DataContext is FindRow row)
            {
                item.Focusable = !row.IsHeader;
                AutomationProperties.SetName(item, row.Name);
            }
        };

        _markAll = new Button { Name = "PART_FindMarkAll", Content = PdfStrings.FindMarkAll };
        _markAll.Click += (_, _) => _pane.Report(_find.MarkAll());
        _markTicked = new Button { Name = "PART_FindMarkTicked", Content = PdfStrings.FindMarkTicked };
        _markTicked.Click += (_, _) => _pane.Report(_find.MarkTicked());
        _addFailed = new Button { Name = "PART_FindAddFailed", Content = PdfStrings.FindAddFailed, IsVisible = false };
        _addFailed.Click += (_, _) => _pane.Report(_find.AddFailed());

        _notSearchedToggle = new ToggleButton { Name = "PART_FindNotSearchedToggle", Content = PdfStrings.FindNotSearchedToggle, HorizontalAlignment = HorizontalAlignment.Left };
        _notSearchedToggle.IsCheckedChanged += (_, _) => _notSearched!.IsVisible = _notSearchedToggle.IsChecked == true;
        _notSearched = new StackPanel { Name = "PART_FindNotSearched", Spacing = 4, IsVisible = false };
        AutomationProperties.SetName(_notSearched, PdfStrings.FindNotSearchedName);
        foreach (var line in PdfStrings.FindNotSearchedLines())
        {
            var text = new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap };
            text.Classes.Add("nav-hint");
            _notSearched.Children.Add(text);
        }

        var options = new StackPanel { Spacing = 2, Children = { _matchCase, _wholeWords } };
        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _search, _cancel } };
        var markRow = new WrapPanel { Orientation = Orientation.Horizontal, Children = { _markAll, _markTicked, _addFailed } };
        foreach (var button in new[] { _markAll, _markTicked, _addFailed })
        {
            button.Margin = new Thickness(0, 0, 8, 4);
        }

        Content = new StackPanel
        {
            Name = "PART_FindPanel",
            Spacing = 8,
            Margin = new Thickness(12, 4, 12, 8),
            Children = { _term, _patterns, options, kindPanel, searchRow, _status, _note, _caution, _acknowledge, _pictures, _ocr, _list, markRow, _notSearchedToggle, _notSearched },
        };
        _kindPanel = kindPanel;
        _options = options;
        AutomationProperties.SetName(this, PdfStrings.FindPanelName);
        _status.Bind(TextBlock.ForegroundProperty, _status.GetResourceObservable("Raffica.Brush.Text.Primary"));

        _find.Changed += OnFindChanged;
        Refresh();
    }

    private readonly StackPanel _kindPanel;
    private readonly StackPanel _options;

    /// <summary>The search session behind the panel.</summary>
    internal FindSession Find => _find;

    /// <summary>The term box (for tests).</summary>
    internal TextBox TermBox => _term;

    internal CheckBox MatchCaseBox => _matchCase;

    internal CheckBox WholeWordsBox => _wholeWords;

    internal ToggleButton PatternsToggle => _patterns;

    internal CheckBox KindBox(RedactionPatternKind kind) => _kindBoxes[kind];

    internal Button SearchButton => _search;

    internal Button AcknowledgeButton => _acknowledge;

    internal Button OcrButton => _ocr;

    internal Button MarkAllButton => _markAll;

    internal Button MarkTickedButton => _markTicked;

    internal Button AddFailedButton => _addFailed;

    internal ToggleButton NotSearchedToggle => _notSearchedToggle;

    internal ListBox List => _list;

    /// <summary>What the status line says now.</summary>
    public string StatusText => _status.Text ?? string.Empty;

    /// <summary>The note under the status: why a term was refused, or what the last marking did; empty when there is none.</summary>
    public string NoteText => _note.IsVisible ? _note.Text ?? string.Empty : string.Empty;

    /// <summary>The line about pages that are pictures; empty when there are none.</summary>
    public string PictureText => _pictures.IsVisible ? _pictures.Text ?? string.Empty : string.Empty;

    /// <summary>The note that patterns only find what the text shows; empty outside the pattern mode.</summary>
    public string CautionText => _caution.IsVisible ? _caution.Text ?? string.Empty : string.Empty;

    /// <summary>Whether the list of what is not searched is shown.</summary>
    public bool NotSearchedVisible => _notSearched.IsVisible;

    /// <summary>The lines of what is not searched.</summary>
    public IReadOnlyList<string> NotSearchedLines => [.. _notSearched.Children.OfType<TextBlock>().Select(t => t.Text ?? string.Empty)];

    /// <summary>The texts of the rows in order (group headings and hits).</summary>
    public IReadOnlyList<string> RowTexts => [.. _rows.Select(r => r.Text)];

    /// <summary>The names of the rows for assistive technology: never the hit text.</summary>
    public IReadOnlyList<string> RowNames => [.. _rows.Select(r => r.Name)];

    /// <summary>Whether the panel is in the pattern mode.</summary>
    public bool PatternMode => _patterns.IsChecked == true;

    /// <summary>Runs the search of the panel as it is set (Enter and the Search button).</summary>
    public Task<bool> SearchAsync()
    {
        if (PatternMode)
        {
            var kinds = s_kinds.Where(k => _kindBoxes[k].IsChecked == true).ToHashSet();
            return _find.RunPatternsAsync(kinds);
        }

        return _find.RunAsync(_term.Text ?? string.Empty, new SearchOptions(_matchCase.IsChecked == true, _wholeWords.IsChecked == true));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _find.Changed -= OnFindChanged;
    }

    private static string Cut(string text) => text.Length > TextPreviewLength ? text[..TextPreviewLength] + "…" : text;

    private Control RowView(FindRow? row)
    {
        if (row is null)
        {
            return new TextBlock();
        }

        if (row.IsHeader)
        {
            var header = new TextBlock { Text = row.Text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
            header.Classes.Add("nav-hint");
            return header;
        }

        var box = new CheckBox
        {
            Content = new TextBlock { Text = row.Text, TextWrapping = TextWrapping.Wrap },
            IsChecked = _find.IsTicked(row.Index),
            IsEnabled = row.CanTick,
        };
        AutomationProperties.SetName(box, row.Name);
        box.IsCheckedChanged += (_, _) =>
        {
            if (!_disposed && !_find.SetTicked(row.Index, box.IsChecked == true))
            {
                box.IsChecked = false;
            }
        };
        return box;
    }

    private void OnTermKeyDown(object? sender, KeyEventArgs e)
    {
        // The search runs on Enter, never while typing: a long document is read only when the user asks.
        if (e.Key is Key.Enter or Key.Return && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            StartSearch();
        }
    }

    // The search is started from an event handler: the guard of the window observes whatever escapes it (a search that ends after the pane went).
    private void StartSearch() => _ = PdfWindow.GuardAsync(async () => await SearchAsync());

    private void RunOcr()
    {
        if (_pane.OcrCommand?.Invoke() is { Run: { } run, IsEnabled: true })
        {
            run();
        }
    }

    private void OnFindChanged() => Refresh();

    private void Refresh()
    {
        var patterns = PatternMode;
        _term.IsVisible = !patterns;
        _options.IsVisible = !patterns;
        _kindPanel.IsVisible = patterns;
        _caution.IsVisible = patterns;
        _cancel.IsVisible = _find.IsRunning;
        _search.IsEnabled = !_find.IsRunning;

        var done = _find.State == FindState.Done;
        _status.Text = StatusOf();
        var note = _find.Refusal == FindRefusal.TermTooLong
            ? PdfStrings.FindTermTooLong()
            : _find.HasMarkFeedback ? PdfStrings.FindMarked(_find.LastAdded, _find.LastNotMarkable) : string.Empty;
        _note.Text = note;
        _note.IsVisible = note.Length > 0;
        _acknowledge.IsVisible = _find.IsIncomplete && !_find.AcknowledgedIncomplete;
        _addFailed.IsVisible = done && _find.Hits.Any(h => h.Certainty == PatternCertainty.ChecksumFailed && h.Markable);
        _markAll.IsEnabled = _markTicked.IsEnabled = _addFailed.IsEnabled = done;

        var offerOcr = done && _find.PictureCount > 0;
        _pictures.IsVisible = offerOcr;
        _pictures.Text = offerOcr ? PdfStrings.FindPictures(_find.PictureCount) : string.Empty;
        _ocr.IsVisible = offerOcr;
        UpdateOcrButton();

        foreach (var kind in s_kinds)
        {
            var count = done && _find.IsPatternSearch ? PdfStrings.FindKindCountFormat
                .Replace("{0}", PdfStrings.FindKindText(kind), StringComparison.Ordinal)
                .Replace("{1}", _find.CountOf(kind).ToString(PdfStrings.Culture), StringComparison.Ordinal) : PdfStrings.FindKindText(kind);
            _kindBoxes[kind].Content = count;
        }

        if (_builtState != _find.State || _builtCount != _find.Hits.Count || _builtPatterns != _find.IsPatternSearch)
        {
            _builtState = _find.State;
            _builtCount = _find.Hits.Count;
            _builtPatterns = _find.IsPatternSearch;
            _rows = BuildRows();
            _list.ItemsSource = _rows;
        }
    }

    /// <summary>Enables the offer of the recognition command only when the command is wired and allowed; otherwise it shows the reason.</summary>
    internal void UpdateOcrButton()
    {
        var command = _pane.OcrCommand?.Invoke();
        var usable = command is { Run: not null, IsEnabled: true };
        _ocr.IsEnabled = usable;
        ToolTip.SetTip(_ocr, usable ? null : command?.CurrentDisabledReason);
    }

    private string StatusOf()
    {
        switch (_find.State)
        {
            case FindState.Running:
                return PdfStrings.FindRunning;
            case FindState.Canceled:
                return PdfStrings.FindCanceled;
            case FindState.Failed:
                return PdfStrings.FindFailed;
            case FindState.Done:
                var text = _find.Hits.Count == 0
                    ? _find.Report is { IsComplete: false } ? PdfStrings.FindNoMatchesIncomplete : PdfStrings.FindNoMatches
                    : PdfStrings.FindMatches(_find.Hits.Count);
                if (_find.Report is { IsComplete: false } report)
                {
                    if (report.IncompletePages.Count > 0)
                    {
                        var pages = string.Join(", ", report.IncompletePages.Select(p => (p + 1).ToString(PdfStrings.Culture)));
                        text += " " + PdfStrings.FindIncompletePagesFormat.Replace("{0}", pages, StringComparison.Ordinal);
                    }

                    if (report.LimitReached)
                    {
                        text += " " + PdfStrings.FindIncompleteLimitFormat.Replace("{0}", RedactionSearch.MaxHits.ToString("N0", PdfStrings.Culture), StringComparison.Ordinal);
                    }

                    if (_find.AcknowledgedIncomplete)
                    {
                        text += " " + PdfStrings.FindAcknowledged;
                    }
                }

                return text;
            default:
                return PatternMode ? string.Empty : PdfStrings.FindEmptyTerm;
        }
    }

    private List<FindRow> BuildRows()
    {
        var rows = new List<FindRow>();
        if (_find.State != FindState.Done && _find.State != FindState.Running)
        {
            return rows;
        }

        if (_find.IsPatternSearch)
        {
            foreach (var group in _find.Groups)
            {
                rows.Add(new FindRow(-1, group.Heading, group.Heading, true, false));
                foreach (var hit in group.Hits)
                {
                    rows.Add(HitRow(hit));
                }
            }
        }
        else
        {
            foreach (var hit in _find.Hits)
            {
                rows.Add(HitRow(hit));
            }
        }

        return rows;
    }

    private FindRow HitRow(RedactionHit hit)
    {
        var shown = Cut(hit.Text);
        var text = PdfStrings.FindHitRowFormat
            .Replace("{0}", (hit.Page + 1).ToString(PdfStrings.Culture), StringComparison.Ordinal)
            .Replace("{1}", shown, StringComparison.Ordinal);
        if (hit.Invisible)
        {
            text += " (" + PdfStrings.FindHitHidden + ")";
        }

        if (!hit.Markable)
        {
            text = PdfStrings.FindHitNotMarkableFormat
                .Replace("{0}", text, StringComparison.Ordinal)
                .Replace("{1}", NotMarkableReasons.TextOf(hit), StringComparison.Ordinal);
        }

        var name = PdfStrings.FindHitName.Replace("{0}", (hit.Page + 1).ToString(PdfStrings.Culture), StringComparison.Ordinal);
        return new FindRow(_find.IndexOf(hit), text, name, false, hit.Markable && hit.Certainty != PatternCertainty.ChecksumFailed);
    }

    private sealed record FindRow(int Index, string Text, string Name, bool IsHeader, bool CanTick);
}
