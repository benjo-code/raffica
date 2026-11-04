using Raffica.Core.Settings;
using RecentEntry = Raffica.Shell.Frame.RecentFile;

namespace Raffica.Pdf.Settings;

/// <summary>
/// The recently opened files and the position per file. Nothing is read before <see cref="LoadAsync"/>, which the window calls after
/// the first sharp page (never in the start path); changes made before that are kept and applied after the load. A missing, corrupt or
/// oversized file gives an empty list. Every change is saved at once (small file, atomic write, failures are ignored). UI thread only.
/// </summary>
/// <param name="store">The file with the list.</param>
internal sealed class RecentService(RecentFilesStore store)
{
    private readonly List<Action<RecentFiles>> _pending = [];
    private RecentFiles? _files;
    private Task? _loading;

    /// <summary>Raised on the UI thread after the list changed (loaded, touched, cleared, ...).</summary>
    public event Action? Changed;

    /// <summary>Reads the file off the UI thread, then applies the changes made meanwhile. A second call waits for the first.</summary>
    public Task LoadAsync() => _loading ??= LoadCoreAsync();

    /// <summary>The newest files for the start view (5 in the app); empty until loaded.</summary>
    public IReadOnlyList<RecentEntry> ForStartView(int count) => Entries(count);

    /// <summary>The newest files for the command palette (10 in the app); empty until loaded.</summary>
    public IReadOnlyList<RecentEntry> ForPalette(int count) => Entries(count);

    /// <summary>Where the file was last read, or null (unknown file, nothing loaded yet).</summary>
    public RememberedView? ViewOf(string path) => _files?.TryGet(path)?.View;

    /// <summary>The file was opened: it moves to the top and keeps its position.</summary>
    public void Touch(string path) => Change(files => files.Touch(path, DateTimeOffset.UtcNow));

    /// <summary>Stores the position of the file (on tab close and window close); the file moves to the top.</summary>
    public void Remember(string path, RememberedView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Change(files => files.Remember(path, view, DateTimeOffset.UtcNow));
    }

    /// <summary>Removes the file from the list.</summary>
    public void Remove(string path) => Change(files => files.Remove(path));

    /// <summary>Empties the list.</summary>
    public void Clear() => Change(files => files.Clear());

    private async Task LoadCoreAsync()
    {
        // RecentFilesStore.Load never throws; the list is built off the UI thread and only used on it.
        var loaded = await Task.Run(store.Load);
        _files = loaded;
        var changed = _pending.Count > 0;
        foreach (var change in _pending)
        {
            change(loaded);
        }

        _pending.Clear();
        if (changed)
        {
            store.TrySave(loaded);
        }

        Changed?.Invoke();
    }

    private void Change(Action<RecentFiles> change)
    {
        if (_files is null)
        {
            _pending.Add(change);
            return;
        }

        change(_files);
        store.TrySave(_files);
        Changed?.Invoke();
    }

    private IReadOnlyList<RecentEntry> Entries(int count) =>
        _files is null || count <= 0
            ? []
            : [.. _files.Items.Take(count).Select(item => new RecentEntry(DisplayName(item.Path), FolderOf(item.Path), item.LastOpened))];

    private static string DisplayName(string path)
    {
        var name = Path.GetFileName(path);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static string FolderOf(string path) => Path.GetDirectoryName(path) ?? string.Empty;
}
