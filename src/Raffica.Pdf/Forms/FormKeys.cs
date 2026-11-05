using Avalonia.Input;
using Avalonia.Interactivity;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Forms;

/// <summary>
/// The keys of the form of one tab, on the page view and only while no annotate tool is active, no card is open and the document allows filling:
/// Tab and Shift+Tab walk the widgets (the first Tab enters the form on the page that is on screen), Enter and Space act on the focused widget
/// (toggle, choose, or open the editor; a held key acts once), and Esc ends the form focus. Inside an open editor Tab and Shift+Tab write the
/// text and move on, and Esc is the editor's (it drops the draft first). At the end of the form Tab passes on, so the keyboard is never trapped.
/// The focused widget also names the page view for a screen reader. UI thread only.
/// </summary>
internal sealed class FormKeys : IDisposable
{
    private readonly PageView _view;
    private readonly ToolState _tools;
    private readonly FormSession _forms;
    private readonly FormFocus _focus;
    private readonly FormInteraction _interaction;
    private readonly DialogQueue _dialogs;
    private readonly Func<bool?> _fillAllowed;
    private readonly HashSet<Key> _down = [];
    private bool _disposed;

    /// <summary>Whether the handler took the last key it saw (it set <c>Handled</c> itself); a key it leaves alone is passed on. For tests.</summary>
    internal bool TookLastKey { get; private set; }

    public FormKeys(PageView view, ToolState tools, FormSession forms, FormFocus focus, FormInteraction interaction, DialogQueue dialogs, Func<bool?>? fillAllowed = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(forms);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(dialogs);
        _view = view;
        _tools = tools;
        _forms = forms;
        _focus = focus;
        _interaction = interaction;
        _dialogs = dialogs;
        _fillAllowed = fillAllowed ?? (() => null);

        // Tunnel, so the keys are seen before the scrolling of the view (Space) and the focus navigation of the window (Tab); handled events too,
        // so a key another handler took still counts as down.
        view.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        view.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        view.LostFocus += OnLostFocus;
        focus.Changed += UpdateName;
        forms.Changed += UpdateName;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _view.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _view.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
        _view.LostFocus -= OnLostFocus;
        _focus.Changed -= UpdateName;
        _forms.Changed -= UpdateName;
        _view.FocusedFieldName = null;
    }

    private bool Live => !_disposed && _tools.Tool == AnnotateTool.None && _forms.HasForm && !_dialogs.IsOpen && _fillAllowed() != false;

    private void OnKeyUp(object? sender, KeyEventArgs e) => _down.Remove(e.Key);

    private void OnLostFocus(object? sender, RoutedEventArgs e) => _down.Clear();

    // The page view carries the name of the focused widget: a check box or a radio button has no element of its own.
    private void UpdateName() => _view.FocusedFieldName = _focus.CurrentField(_forms)?.DisplayName;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var repeat = !_down.Add(e.Key);
        TookLastKey = false;
        if (e.Handled || !Live || (e.KeyModifiers & ~KeyModifiers.Shift) != 0)
        {
            return;
        }

        // Inside an open editor only Tab is ours (it writes the text and moves on); every other key, Esc included, is the editor's.
        if (_interaction.IsEditing)
        {
            if (e.Key == Key.Tab)
            {
                e.Handled = TookLastKey = true;
                _interaction.CommitAndMove(forward: e.KeyModifiers == KeyModifiers.None);
            }

            return;
        }

        // The other keys act on the page view itself, not on the ribbon, a pane or a field that lives somewhere else.
        if (!ReferenceEquals(e.Source, _view))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Tab:
                TabFromView(e);
                break;
            case Key.Enter or Key.Space when e.KeyModifiers == KeyModifiers.None && _focus.CurrentField(_forms) is { } field:
                // A held key acts once: the repeat is taken and does nothing.
                e.Handled = TookLastKey = true;
                if (!repeat)
                {
                    _interaction.Activate(field);
                }

                break;
            case Key.Escape when e.KeyModifiers == KeyModifiers.None && _focus.Current is not null:
                e.Handled = TookLastKey = true;
                _focus.Set(null);
                break;
        }
    }

    // Tab moves the focus when the walk knows where to; when it has to read a page first the key is taken and the move follows; at a true end it passes.
    private void TabFromView(KeyEventArgs e)
    {
        var forward = e.KeyModifiers == KeyModifiers.None;
        switch (_interaction.Mover.Step(forward).Step)
        {
            case FormStep.Moved:
                e.Handled = TookLastKey = true;
                break;
            case FormStep.NeedsPage:
                e.Handled = TookLastKey = true;
                _ = MoveAfterReadAsync(forward);
                break;
        }
    }

    private async Task MoveAfterReadAsync(bool forward)
    {
        try
        {
            if (!_disposed)
            {
                await _interaction.Mover.MoveAsync(forward);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The focus stays where it is.
        }
    }
}
