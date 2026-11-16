using Avalonia.Controls;
using Raffica.Shell.Frame;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Panes;

namespace Raffica.Write;

/// <summary>
/// The formatting pane of the window: the right slot of the frame with the pane of the active tab. As with the outline, nothing exists before the user asks for it (neither the slot
/// nor a pane, nor a timer or a read); each tab's pane is made on first use and kept until the tab closes.
/// </summary>
internal sealed partial class WriteWindow
{
    /// <summary>The width of the formatting pane in device-independent pixels.</summary>
    internal const double FormattingPaneWidth = 256;

    private readonly Dictionary<WriteTab, FormattingPane> _formattingPanes = [];
    private Pane? _formattingHost;
    private bool _formattingWanted;

    /// <summary>The right slot of the frame; null until the formatting pane was asked for the first time (tests).</summary>
    internal Pane? FormattingHost => _formattingHost;

    /// <summary>Whether the formatting pane is on (the user asked for it and a document is open).</summary>
    internal bool IsFormattingOpen => _formattingWanted && _formattingHost is { IsOpen: true };

    /// <summary>The formatting pane of a tab, or null while none was made (tests).</summary>
    internal FormattingPane? FormattingOf(WriteTab tab) => _formattingPanes.GetValueOrDefault(tab);

    // The command of the pane: a switch. Opening it puts the focus in the pane; closing it gives the focus back to the document.
    private void ToggleFormatting()
    {
        _formattingWanted = !_formattingWanted;
        PlaceFormatting();
        if (_formattingWanted && Workspace.Active is { } tab && _formattingPanes.TryGetValue(tab, out var pane))
        {
            pane.FocusContent();
        }
        else
        {
            FocusActiveView();
        }

        RefreshCommandStates();
    }

    // Puts the pane of the active tab into the slot (the slot and the pane are made when first needed); no tab or no wish: the slot is closed.
    private void PlaceFormatting()
    {
        if (_formattingHost is null && !_formattingWanted)
        {
            return;
        }

        if (_formattingHost is null)
        {
            // F6 and Shift+F6 run through the panes and the document (the slot joins the ring after the document, when it is first needed).
            _formattingHost = new Pane { Title = WriteStrings.CmdFormatting, Width = FormattingPaneWidth, IsOpen = false };
            _paneFocus.Insert(int.MaxValue, _formattingHost);
        }

        var host = _formattingHost;
        var active = Workspace.Active;
        var show = _formattingWanted && active is not null && Tabs.Items.Count > 0 && ViewOf(active) is not null;
        var focusWasInside = host.IsKeyboardFocusWithin;
        var before = host.Content as FormattingPane;
        var next = show ? FormattingPaneOf(active!) : null;
        if (before is not null && !ReferenceEquals(before, next))
        {
            before.Hidden();
        }

        host.IsOpen = show;
        host.Content = next;
        Frame.RightPane = Tabs.Items.Count > 0 ? host : null;
        if (focusWasInside && (!show || !ReferenceEquals(before, next)))
        {
            FocusActiveView();
        }
    }

    private FormattingPane FormattingPaneOf(WriteTab tab)
    {
        if (!_formattingPanes.TryGetValue(tab, out var pane))
        {
            var actions = new FormattingPaneActions(
                _rules.Allows,
                _rules.ReasonFor,
                RunCommand,
                (scope, element) => ViewOf(tab)?.RemoveOverride(scope, element) ?? false);
            pane = new FormattingPane(tab, actions);
            _formattingPanes[tab] = pane;
        }

        return pane;
    }

    // A tab closed: its pane goes with it.
    private void ForgetFormatting(WriteTab tab)
    {
        if (_formattingPanes.Remove(tab, out var pane))
        {
            pane.Dispose();
        }
    }

    // The rules of the commands may have changed (a save began or ended, the selection moved): the pane of the active tab reads its buttons again, and once a save has ended it reads the selection.
    private void FormattingStatesChanged()
    {
        if (_formattingWanted && Workspace.Active is { } tab && _formattingPanes.TryGetValue(tab, out var pane))
        {
            pane.RefreshButtons();
        }
    }

    private void FormattingSaveChanged()
    {
        if (_formattingWanted && Workspace.Active is { } tab && _formattingPanes.TryGetValue(tab, out var pane))
        {
            pane.RequestRefresh();
        }
    }
}
