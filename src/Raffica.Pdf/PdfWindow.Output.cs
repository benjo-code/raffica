using Avalonia.Platform.Storage;
using Raffica.Core.Pdf;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Organizing;

namespace Raffica.Pdf;

/// <summary>
/// The output commands of the Pages tab that write new files from the open document: Extract pages and Split document. They read the document
/// without a plan, so the command rules lock them while a plan is open or marks are unsaved; the flow behind them is made on first use.
/// </summary>
internal sealed partial class PdfWindow
{
    /// <summary>Replaces the save dialog of "Extract pages" (tests): gets the tab and the suggested name, returns the chosen path, null when canceled. Null: the system dialog.</summary>
    internal Func<PdfTab, string, Task<string?>>? ExtractPathPicker { get; set; }

    /// <summary>Replaces the folder dialog of "Split document" (tests): returns the chosen folder, null when canceled. Null: the system dialog.</summary>
    internal Func<Task<string?>>? FolderPicker { get; set; }

    private SplitFlow? _outputFlow;

    /// <summary>Gets whether the flow of the output commands exists (it is made by the first of them; for tests).</summary>
    internal bool OutputFlowCreated => _outputFlow is not null;

    internal SplitFlow Output => _outputFlow ??= new SplitFlow(
        Dialogs,
        (tab, name) => ExtractPathPicker is { } picker ? picker(tab, name) : PickPathAsync(tab, name, PdfStrings.ExtractSaveTitle),
        _ => FolderPicker is { } picker ? picker() : PickFolderAsync(),
        () => _engine ??= new PdfEngine(),
        tab => _views.TryGetValue(tab, out var view) ? view.CurrentPage : 0,
        (tab, token) => OutlineOf(tab) is { } outline
            ? outline.ReadAsync(token)
            : tab.Session.Document is { } document ? document.GetOutlineAsync(token) : Task.FromResult<IReadOnlyList<PdfOutlineItem>>([]),
        path => Flow.OpenAsync(path),
        RefreshCommandStates,
        () => _applyFlow?.IsRunning == true);

    /// <summary>Extracts pages of the active document into a new file (the Extract pages command).</summary>
    internal void ExtractPages() => _ = GuardAsync(ExtractPagesAsync);

    internal Task ExtractPagesAsync() =>
        Workspace.Active is { } tab && _rules.Allows(PdfCommandIds.ExtractPages) ? Output.ExtractAsync(tab) : Task.CompletedTask;

    /// <summary>Splits the active document into new files (the Split document command).</summary>
    internal void SplitDocument() => _ = GuardAsync(SplitDocumentAsync);

    internal Task SplitDocumentAsync() =>
        Workspace.Active is { } tab && _rules.Allows(PdfCommandIds.SplitDocument) ? Output.SplitAsync(tab) : Task.CompletedTask;

    private async Task<string?> PickFolderAsync()
    {
        // As with the other pickers: a picker may take a key up.
        _keysDown.Clear();
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = PdfStrings.SplitFolderPickTitle,
                AllowMultiple = false,
                SuggestedStartLocation = Workspace.Active?.Path is { } path && Path.GetDirectoryName(path) is { Length: > 0 } folder
                    ? await StorageProvider.TryGetFolderFromPathAsync(folder)
                    : null,
            });
            return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
        }
        finally
        {
            _keysDown.Clear();
        }
    }
}
