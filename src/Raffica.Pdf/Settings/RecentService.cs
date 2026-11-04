using Raffica.Core.Settings;
using RecentEntry = Raffica.Shell.Frame.RecentFile;

namespace Raffica.Pdf.Settings;

/// <summary>
/// The recently opened files and the position per file. Nothing is read before <see cref="LoadAsync"/>, which the window calls after
/// the first sharp page (never in the start path); changes made before that are kept and applied after the load. A missing, corrupt or
/// oversized file gives an empty list. A change is applied in memory at once and written by a background writer (<see cref="CoalescingWriter"/>: one
/// writer, the newest list wins, failures are ignored), so the UI thread never waits for the disk; <see cref="FlushAsync"/> and
/// <see cref="Flush"/> wait for what was posted (the window flushes when it closes). UI thread only.
/// </summary>
/// <param name="store">The file with the list.</param>
internal sealed class RecentService(RecentFilesStore store)
{
    private readonly List<Action<RecentFiles>> _pending = [];
    private readonly CoalescingWriter _writer = new();
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

    /// <summary>Completes when the changes made so far have been written to the file (tests wait for it instead of sleeping).</summary>
    public Task FlushAsync() => _writer.FlushAsync();

    /// <summary>Waits for the changes made so far to be written, for at most <paramref name="timeout"/> (the window closing); returns whether they were.</summary>
    public bool Flush(TimeSpan timeout) => _writer.Flush(timeout);

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
            Save(loaded);
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
        Save(_files);
        Changed?.Invoke();
    }

    // The list is copied here, on the UI thread (it is not thread-safe); the writer only ever sees the copy.
    private void Save(RecentFiles files)
    {
        var snapshot = new RecentFiles(files.Items);
        _writer.Post(() => store.TrySave(snapshot));
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
