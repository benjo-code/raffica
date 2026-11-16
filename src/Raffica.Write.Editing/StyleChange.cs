using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// A change of one style of the style sheet, as the history keeps it: <paramref name="Old"/> and <paramref name="New"/> are read-only snapshots (clones) of
/// the style before and after, never the live object. Callers must not change them.
/// </summary>
public sealed record StyleChange(Style Old, Style New);
