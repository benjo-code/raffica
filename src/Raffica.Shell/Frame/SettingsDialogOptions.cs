using Avalonia.Controls;

namespace Raffica.Shell.Frame;

/// <summary>One extra row of the settings dialog: a label shown as secondary text above the editor the app supplies.</summary>
public sealed record SettingsExtraRow(string Label, Control Editor);

/// <summary>
/// Lets an app adjust the shared <see cref="SettingsDialog"/>: hide the autosave row (the stored value is kept on OK) and append
/// its own rows after the compact-command-bar row, and word the autosave row for the app (<paramref name="AutosaveLabel"/>; <see langword="null"/> keeps the
/// shell's text). <see langword="null"/> options give the unchanged dialog.
/// </summary>
public sealed record SettingsDialogOptions(bool ShowAutosave = true, IReadOnlyList<SettingsExtraRow>? ExtraRows = null, string? AutosaveLabel = null);
