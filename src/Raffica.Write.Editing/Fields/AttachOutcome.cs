namespace Raffica.Write.Editing;

/// <summary>What <see cref="EditHistory.AttachDerived"/> did with a record.</summary>
public enum AttachOutcome
{
    /// <summary>The record belongs to the newest step now and follows its undo and redo.</summary>
    Attached,

    /// <summary>The history has no step to carry it (or the record was empty or could not be placed): nothing was attached.</summary>
    NothingToProtect,

    /// <summary>A redo is pending: the structure would change again when it is redone, so the caller must wait until the redo stack is empty and refresh again.</summary>
    MustWait,
}
