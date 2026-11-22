using Avalonia.Input;
using Raffica.Write.Model;

namespace Raffica.Write.Panes;

/// <summary>
/// What the outline pane asks of its window: the four outline commands for the pane's selected row (the same rules, reasons and run path as the ribbon, so a refusal reads the same everywhere), the
/// Alt and Shift keys typed in the list, the drop of a drag and the words of a drag that cannot start.
/// </summary>
internal sealed class OutlinePaneActions
{
    /// <summary>Whether the command may run on the selected row of the pane.</summary>
    public required Func<OutlinePane, string, bool> Allows { get; init; }

    /// <summary>Why the command may not run on the selected row, or null.</summary>
    public required Func<OutlinePane, string, string?> ReasonFor { get; init; }

    /// <summary>Runs the command on the selected row of the pane; false when it did not run.</summary>
    public required Func<OutlinePane, string, bool> Run { get; init; }

    /// <summary>Takes a key typed in the list when it is an outline key (it is then handled); false otherwise.</summary>
    public required Func<KeyEventArgs, bool> Key { get; init; }

    /// <summary>Moves the chapter of the first heading paragraph before or after the second one; the plan is made from the live model.</summary>
    public required Func<Paragraph, Paragraph, DropPlace, bool> Drop { get; init; }

    /// <summary>Shows words for a command that did nothing (a drag that does not start).</summary>
    public required Action<string> Note { get; init; }
}
