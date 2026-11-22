namespace Raffica.Write.Editing;

/// <summary>Why a list command is not available for a selection (<see cref="ListReason.None"/> when it is). The commands word each member in both languages.</summary>
public enum ListReason
{
    /// <summary>The command is available.</summary>
    None,

    /// <summary>The paragraph is not in a list.</summary>
    NotInAList,

    /// <summary>No paragraph before it belongs to a list of the same definition, so there is nothing to continue.</summary>
    NothingBefore,

    /// <summary>The paragraph already continues the previous list.</summary>
    AlreadyContinues,

    /// <summary>The selection leaves one list of blocks (a table cell, a content control, a note), which no list command crosses.</summary>
    SelectionAcrossContainers,
}

/// <summary>
/// What the list commands need to know about a selection (<see cref="TextEditor.ReadListFacts"/>). <see cref="Kind"/> is the kind of every touched paragraph when
/// they are all in lists of one kind, else null. Restart and continue act on the paragraph at the start of the selection. A selection of more than
/// <see cref="TextEditor.MaxFactBlocks"/> blocks is read from its first paragraph only.
/// </summary>
public readonly record struct ListFacts(
    bool InList,
    ListKind? Kind,
    ListReason ToggleReason,
    bool CanRestart,
    ListReason RestartReason,
    bool CanContinue,
    ListReason ContinueReason);
