using Raffica.Write.Documents;

namespace Raffica.Write.Commands;

/// <summary>Why a save was not even started: each one is also the reason the command shows while it is disabled.</summary>
internal enum SaveRefusal
{
    /// <summary>No document is open.</summary>
    NoDocument,

    /// <summary>Only a prefix of the file is open: nothing can be saved before the whole model is there.</summary>
    StillOpening,

    /// <summary>A save of this tab is already running (or its picker is open).</summary>
    Running,

    /// <summary>Save over the file only: there is nothing changed.</summary>
    NothingToSave,

    /// <summary>The document cannot be saved over its file (editing failed), or cannot be saved at all (it failed to open or is closed).</summary>
    Unavailable,
}

/// <summary>What the rules of Save and Save as need to know about the active tab.</summary>
/// <param name="HasDocument">Whether a tab is open.</param>
/// <param name="StillOpening">Whether only the prefix of the file is open.</param>
/// <param name="Running">Whether a save of the tab runs.</param>
/// <param name="Faulted">Whether the open, the layout or an edit failed (or the tab is closed).</param>
/// <param name="FaultedByEdit">Whether the fault came from an edit: the model was open and whole, so Save as may still keep it (an explicit act of the user).</param>
/// <param name="Dirty">Whether the document has unsaved changes.</param>
/// <param name="Untitled">Whether the tab belongs to no file yet: Save is then Save as and is never "nothing to save".</param>
internal readonly record struct SaveCommandState(bool HasDocument, bool StillOpening, bool Running, bool Faulted, bool FaultedByEdit, bool Dirty, bool Untitled = false)
{
    /// <summary>
    /// The state of a tab, read without making an editing session. Save as stays possible after a failed edit (the user decides to keep what is in memory; the file is
    /// written under a new name and checked like any save, and nothing says that its content is right); Save over the file does not.
    /// </summary>
    public static SaveCommandState Of(WriteTab? tab, bool flowRunning)
    {
        if (tab is null)
        {
            return default;
        }

        var runner = tab.Runner;
        var faulted = tab.IsDisposed || runner.IsFaulted;
        return new SaveCommandState(
            HasDocument: true,
            StillOpening: !faulted && !runner.FullModelReady,
            Running: flowRunning || tab.Editor?.IsSaving == true,
            Faulted: faulted,
            FaultedByEdit: !tab.IsDisposed && runner.FaultedByEdit,
            Dirty: tab.IsDirty,
            Untitled: tab.IsUntitled);
    }

    /// <summary>Why <paramref name="saveAs"/> (or Save over the file) cannot run now, or null when it can. The order is the order of the reasons a user can fix: a fault first, then the open, then a running save, then "nothing to save".</summary>
    public SaveRefusal? Refusal(bool saveAs)
    {
        if (!HasDocument)
        {
            return SaveRefusal.NoDocument;
        }

        if (Faulted && !(saveAs && FaultedByEdit))
        {
            return SaveRefusal.Unavailable;
        }

        if (StillOpening)
        {
            return SaveRefusal.StillOpening;
        }

        if (Running)
        {
            return SaveRefusal.Running;
        }

        return !saveAs && !Untitled && !Dirty ? SaveRefusal.NothingToSave : null;
    }
}
