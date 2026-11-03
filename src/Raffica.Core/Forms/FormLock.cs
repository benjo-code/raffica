namespace Raffica.Core.Forms;

/// <summary>Whether Raffica lets the user change a field.</summary>
public enum FormLock
{
    /// <summary>Raffica can change the field.</summary>
    None,

    /// <summary>The field is read-only (flag or document rights).</summary>
    ReadOnly,

    /// <summary>Raffica cannot write the field: a signature, a file selection, a button or a field without a name.</summary>
    Unsupported,
}
