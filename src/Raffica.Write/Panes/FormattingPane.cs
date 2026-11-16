using System.Xml.Linq;
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

namespace Raffica.Write.Panes;

/// <summary>What the formatting pane may ask of the window: the rules and the commands (the window owns both).</summary>
/// <param name="Allows">Whether a command of the catalog may run now.</param>
/// <param name="ReasonFor">Why a command is disabled (null when it is not or when there is nothing to tell).</param>
/// <param name="Run">Runs a command of the catalog by id (as the ribbon does: the page view takes the focus back).</param>
/// <param name="Remove">Removes one override row from the current selection of the tab (one undo step).</param>
internal sealed record FormattingPaneActions(Func<string, bool> Allows, Func<string, string?> ReasonFor, Func<string, bool> Run, Func<OverrideScope, XName, bool> Remove);

/// <summary>
/// The formatting inspector of one document tab (256 px, the right slot of the frame): the paragraph style and the character style of the selection, one row for every direct
/// override of it (each with a button that removes exactly that element), the two buttons "Update style to match" and "Clear direct formatting" and, once per tab, the note that
/// a style is shared. It is made when the user first opens it and kept until the tab closes; nothing exists before that (no pane, no timer, no read). It follows the session: the
/// selection or the document changing asks for a refresh that waits for typing and selecting to pause (<see cref="RefreshDelay"/>, one timer made by the first request), and the read
/// happens on the UI thread, where the model is edited, remembered by the session for the model, revision and selection and bounded: a selection of more than
/// <see cref="TextEditor.MaxFactBlocks"/> blocks is not listed (the pane says so) and costs one lookup. The words are the app's (<see cref="FormattingRowWording"/>), never the text of the
/// document. A row's state ("only part of the selection", "different values") is said in words, not by colour alone, and every row and button has a name for assistive technology.
/// </summary>
internal sealed class FormattingPane : UserControl, IDisposable
{
    private readonly WriteTab _tab;
    private readonly FormattingPaneActions _actions;
    private readonly FormatDisplayRefresh _refresh;
    private readonly TextBlock _hint;
    private readonly StackPanel _details;
    private readonly TextBlock _paragraphStyle;
    private readonly TextBlock _characterStyle;
    private readonly TextBlock _note;
    private readonly TextBlock _noRows;
    private readonly ItemsControl _rowList;
    private readonly Button _update;
    private readonly TextBlock _updateReason;
    private readonly Button _clear;
    private readonly TextBlock _clearReason;
    private List<RowItem> _rows = [];
    private (InspectorView View, string ParagraphStyle, string CharacterStyle)? _shown;
    private EditSession? _session;
    private bool _noteDone;
    private bool _noteVisible;
    private bool _disposed;

    /// <summary>Makes the pane of a tab; it follows the tab's session once there is one (the session is made by the first caret or key, never by the pane).</summary>
    public FormattingPane(WriteTab tab, FormattingPaneActions actions)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(actions);
        _tab = tab;
        _actions = actions;
        _refresh = new FormatDisplayRefresh(RefreshNow, RefreshDelay);

        _hint = Text("fmt-hint");
        _hint.Focusable = true;
        _hint.IsTabStop = true;

        _paragraphStyle = Text("fmt-value");
        _characterStyle = Text("fmt-value");
        _note = Text("fmt-note");
        _note.IsVisible = false;
        _noRows = Text("fmt-hint");
        _noRows.Text = WriteStrings.FormattingNoRows;

        var heading = Text("fmt-heading");
        heading.Text = WriteStrings.FormattingRowsHeading;
        AutomationProperties.SetName(heading, WriteStrings.FormattingRowsHeading);

        _rowList = new ItemsControl
        {
            ItemTemplate = new FuncDataTemplate<RowItem>((item, _) => item is null ? null : CreateRow(item)),
        };
        AutomationProperties.SetName(_rowList, WriteStrings.FormattingRowsHeading);

        _update = ActionButton(WriteStrings.CmdFormatUpdateStyle, WriteCommandIds.UpdateStyle);
        _updateReason = Text("fmt-reason");
        _clear = ActionButton(WriteStrings.CmdFormatClearDirect, WriteCommandIds.ClearDirect);
        _clearReason = Text("fmt-reason");

        _details = new StackPanel { Spacing = 6 };
        _details.Children.Add(StyleLine(WriteStrings.FormattingParagraphStyle, _paragraphStyle));
        _details.Children.Add(StyleLine(WriteStrings.FormattingCharacterStyle, _characterStyle));
        _details.Children.Add(_note);
        _details.Children.Add(heading);
        _details.Children.Add(_noRows);
        _details.Children.Add(_rowList);

        var buttons = new StackPanel { Spacing = 4, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(_update);
        buttons.Children.Add(_updateReason);
        buttons.Children.Add(_clear);
        buttons.Children.Add(_clearReason);

        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(_hint);
        content.Children.Add(_details);
        content.Children.Add(buttons);
        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        _tab.EditorCreated += OnEditorCreated;
        _tab.Disposing += Dispose;
        if (_tab.Editor is { } existing)
        {
            Attach(existing);
        }

        RefreshNow();
    }

    /// <summary>How long typing and selecting must pause before the pane reads the selection again.</summary>
    internal static TimeSpan RefreshDelay => FormatDisplayRefresh.DefaultDelay;

    /// <summary>The refresh that waits for the pause (tests run it now with <see cref="FormatDisplayRefresh.Flush"/>).</summary>
    internal FormatDisplayRefresh Refresh => _refresh;

    /// <summary>How many times the pane showed a state (the first fill and every refresh that ran).</summary>
    internal int RefreshCount { get; private set; }

    /// <summary>The text of the line that stands in for the details (no selection, too large, opening, unavailable); null while the details are shown.</summary>
    internal string? HintText => _hint.IsVisible ? _hint.Text : null;

    /// <summary>The paragraph style line as shown.</summary>
    internal string ParagraphStyleText => _paragraphStyle.Text ?? string.Empty;

    /// <summary>The character style line as shown.</summary>
    internal string CharacterStyleText => _characterStyle.Text ?? string.Empty;

    /// <summary>The rows as shown: the wording and the state of each, in order.</summary>
    internal IReadOnlyList<RowItem> Rows => _rows;

    /// <summary>The remove button of the row at <paramref name="index"/> (a test presses it).</summary>
    internal Button RemoveButtonOf(int index) => this.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("fmt-remove")).ElementAt(index);

    internal Button UpdateButton => _update;

    internal Button ClearButton => _clear;

    internal string? UpdateReasonText => _updateReason.IsVisible ? _updateReason.Text : null;

    internal string? ClearReasonText => _clearReason.IsVisible ? _clearReason.Text : null;

    /// <summary>The note that the style is shared, or null when it is not on show.</summary>
    internal string? NoteText => _note.IsVisible ? _note.Text : null;

    internal bool IsDisposed => _disposed;

    /// <summary>Puts the keyboard focus on the first control of the pane that can take it.</summary>
    public void FocusContent()
    {
        var target = this.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(e => e.Focusable && e.IsTabStop && e.IsEffectivelyVisible && e.IsEffectivelyEnabled);
        target?.Focus(NavigationMethod.Tab);
    }

    /// <summary>The pane left the screen (closed, or another tab's pane took its place): the note of a shared style has been seen and is not shown again in this tab.</summary>
    public void Hidden()
    {
        if (_noteVisible)
        {
            _noteDone = true;
            _noteVisible = false;
            _note.IsVisible = false;
        }
    }

    /// <summary>Asks for a refresh once typing and selecting pause (the window asks when something else changed the rules, a save ending).</summary>
    public void RequestRefresh()
    {
        if (!_disposed)
        {
            _refresh.Request();
        }
    }

    /// <summary>Reads the buttons' rules again (cheap: the facts are remembered by the session); the state of the details waits for <see cref="RequestRefresh"/>.</summary>
    public void RefreshButtons()
    {
        if (!_disposed)
        {
            UpdateButtons();
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
        if (!_disposed)
        {
            Attach(session);
            RequestRefresh();
        }
    }

    private void Attach(EditSession session)
    {
        Detach();
        _session = session;
        session.SelectionChanged += RequestRefresh;
        session.Changed += RequestRefresh;
    }

    private void Detach()
    {
        if (_session is { } session)
        {
            session.SelectionChanged -= RequestRefresh;
            session.Changed -= RequestRefresh;
            _session = null;
        }
    }

    // The read and the redraw of the pane (UI thread only: the model is edited there).
    private void RefreshNow()
    {
        if (_disposed)
        {
            return;
        }

        RefreshCount++;
        var session = _tab.Editor;
        string? hint = null;
        InspectorView? view = null;
        switch (session?.BlockedReason)
        {
            case null when session is null:
                hint = WriteStrings.FormattingNoSelection;
                break;
            case EditBlockedReason.Saving:
                // The model is not read while a save holds it; what is on show stays until the save ends (the window asks again then).
                UpdateButtons();
                return;
            case EditBlockedReason.StillOpening:
                hint = WriteStrings.ReasonEditOpening;
                break;
            case EditBlockedReason.Faulted:
                hint = WriteStrings.EditUnavailable;
                break;
            default:
                if (session!.Selection is not { } selection)
                {
                    hint = WriteStrings.FormattingNoSelection;
                    break;
                }

                try
                {
                    view = session.ReadInspector(selection);
                    hint = view is null ? WriteStrings.FormattingTooLarge : null;
                }
                catch (StalePositionException)
                {
                    // A selection that does not belong to the model any more shows nothing; the next selection or edit shows again.
                    hint = WriteStrings.FormattingNoSelection;
                }

                break;
        }

        if (hint is not null)
        {
            _shown = null;
            _hint.Text = hint;
            _hint.IsVisible = true;
            _details.IsVisible = false;
            AutomationProperties.SetName(_hint, hint);
            HideNote();
        }
        else
        {
            _hint.IsVisible = false;
            _details.IsVisible = true;
            Show(view!);
        }

        UpdateButtons();
    }

    private void Show(InspectorView view)
    {
        var paragraphStyle = StyleText(view.ParagraphStyleId, view.ParagraphStyleMixed, paragraph: true);
        var characterStyle = StyleText(view.CharacterStyleId, view.CharacterStyleMixed, paragraph: false);
        _paragraphStyle.Text = paragraphStyle;
        _characterStyle.Text = characterStyle;
        ShowNote(view);
        if (_shown is { } shown && shown.ParagraphStyle == paragraphStyle && shown.CharacterStyle == characterStyle && SameRows(shown.View.Rows, view.Rows))
        {
            _shown = (view, paragraphStyle, characterStyle);
            return;
        }

        _shown = (view, paragraphStyle, characterStyle);
        _rows = [.. view.Rows.Select(row => new RowItem(row, FormattingRowWording.Describe(row), FormattingRowWording.PresenceText(row.Presence)))];
        _rowList.ItemsSource = _rows;
        _noRows.IsVisible = _rows.Count == 0;
    }

    // The note says once per tab that a style is shared: it shows with the first style the file defines and goes when the pane leaves the screen or Update style runs.
    private void ShowNote(InspectorView view)
    {
        if (_noteDone || _noteVisible || !IsDefined(view.ParagraphStyleId, view.ParagraphStyleMixed))
        {
            return;
        }

        _noteVisible = true;
        _note.Text = WriteStrings.FormattingSharedNote;
        _note.IsVisible = true;
    }

    private void HideNote()
    {
        if (_noteVisible)
        {
            _noteVisible = false;
            _note.IsVisible = false;
        }
    }

    private bool IsDefined(string? id, bool mixed)
    {
        if (mixed)
        {
            return false;
        }

        var sheet = _tab.Docx.Model.Styles;
        return sheet is not null && (id is not null ? sheet.Find(id) is not null : sheet.Styles.Any(s => s.IsDefault && (s.Type ?? "paragraph") == "paragraph"));
    }

    // The style line: the name the file gives the style, "mixed" for several, the file's default paragraph style for a paragraph that names none, an id the file does not define as that id.
    private string StyleText(string? id, bool mixed, bool paragraph)
    {
        if (mixed)
        {
            return WriteStrings.FormattingStyleMixed;
        }

        var sheet = _tab.Docx.Model.Styles;
        if (id is null)
        {
            var type = paragraph ? "paragraph" : "character";
            return paragraph && sheet?.Styles.FirstOrDefault(s => s.IsDefault && (s.Type ?? "paragraph") == type) is { } fallback
                ? fallback.Name ?? fallback.Id
                : WriteStrings.StyleNone;
        }

        return sheet?.Find(id) is { } style ? style.Name ?? style.Id : WriteStrings.Fill(WriteStrings.FormattingStyleUndefinedFormat, Clean(id));
    }

    private static string Clean(string text) => text.Length <= FormattingRowWording.MaxValueLength ? text : text[..FormattingRowWording.MaxValueLength] + "…";

    private static bool SameRows(IReadOnlyList<OverrideRow> a, IReadOnlyList<OverrideRow> b) => a.Count == b.Count && a.Zip(b).All(pair => pair.First == pair.Second);

    // The buttons: enabled by the rules, and a disabled one says why right under it (a tooltip alone would hide the reason from the keyboard).
    private void UpdateButtons()
    {
        Rule(_update, _updateReason, WriteCommandIds.UpdateStyle);
        Rule(_clear, _clearReason, WriteCommandIds.ClearDirect);
    }

    private void Rule(Button button, TextBlock reason, string id)
    {
        var allowed = _actions.Allows(id);
        button.IsEnabled = allowed;
        var why = allowed ? null : _actions.ReasonFor(id);
        reason.Text = why;
        reason.IsVisible = why is not null;
        ToolTip.SetTip(button, why);
        AutomationProperties.SetHelpText(button, why ?? string.Empty);
    }

    private Button ActionButton(string label, string id)
    {
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Classes.Add("boxed");
        button.Classes.Add("wrap");
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) =>
        {
            // The state is read again first: a selection that moved a moment ago must not decide what the button does.
            _refresh.Flush();
            if (id == WriteCommandIds.UpdateStyle)
            {
                // The note has been seen once the user acts on it.
                _noteDone = true;
                HideNote();
            }

            if (!_actions.Allows(id) || !_actions.Run(id))
            {
                UpdateButtons();
                return;
            }

            _refresh.Request();
            _refresh.Flush();
        };
        return button;
    }

    private static TextBlock Text(string style)
    {
        var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        text.Classes.Add(style);
        return text;
    }

    private static StackPanel StyleLine(string label, TextBlock value)
    {
        var caption = Text("fmt-caption");
        caption.Text = label;
        var line = new StackPanel { Spacing = 0 };
        line.Children.Add(caption);
        line.Children.Add(value);
        AutomationProperties.SetName(line, label);
        return line;
    }

    private Grid CreateRow(RowItem item)
    {
        var wording = Text("fmt-row");
        wording.Text = item.Wording;
        var text = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(wording);
        if (item.State is not null)
        {
            var state = Text("fmt-state");
            state.Text = item.State;
            text.Children.Add(state);
        }

        var name = item.State is null ? item.Wording : WriteStrings.Fill(WriteStrings.FormattingRowNameFormat, item.Wording, item.State);
        var remove = new Button { Content = "×" };
        remove.Classes.Add("icon");
        remove.Classes.Add("fmt-remove");
        var removeName = WriteStrings.Fill(WriteStrings.FormattingRemoveFormat, item.Wording);
        AutomationProperties.SetName(remove, removeName);
        ToolTip.SetTip(remove, removeName);
        remove.Click += (_, _) => RemoveRow(item, remove);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 28 };
        Grid.SetColumn(remove, 1);
        grid.Children.Add(text);
        grid.Children.Add(remove);
        AutomationProperties.SetName(grid, name);
        return grid;
    }

    // Removes the element of the row from the current selection. The pending refresh runs first, so a row that no longer belongs to the selection does nothing; the pane then shows the
    // result at once, and a button that had the focus hands it to the row that took its place.
    private void RemoveRow(RowItem item, Button button)
    {
        _refresh.Flush();
        var index = _rows.FindIndex(r => r.Row == item.Row);
        if (index < 0)
        {
            return;
        }

        var hadFocus = button.IsFocused || button.IsKeyboardFocusWithin;
        if (!_actions.Remove(item.Row.Scope, item.Row.Element))
        {
            return;
        }

        _refresh.Request();
        _refresh.Flush();
        if (hadFocus)
        {
            FocusAfterRemoval(index);
        }
    }

    // After a removal the row's button is gone: the focus goes to the button of the row that took its place, else the one before, else the Clear button, else the pane's text.
    private void FocusAfterRemoval(int index) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                if (_disposed)
                {
                    return;
                }

                var buttons = this.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("fmt-remove") && b.IsEffectivelyEnabled).ToList();
                InputElement target = buttons.Count > 0 ? buttons[Math.Min(index, buttons.Count - 1)] : _clear.IsEffectivelyEnabled ? _clear : _hint;
                target.Focus(NavigationMethod.Tab);
            },
            Avalonia.Threading.DispatcherPriority.Loaded);

    /// <summary>One row of the list: the inspector row, its wording and its state ("only part of the selection"), null for a row that is on the whole selection with one value.</summary>
    internal sealed record RowItem(OverrideRow Row, string Wording, string? State);
}
