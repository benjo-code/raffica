using Raffica.Core.Pdf;
using Raffica.Pdf.Documents;

namespace Raffica.Pdf.Panes;

/// <summary>A bookmark of the document as the palette lists it: its title and its 0-based page.</summary>
/// <param name="Title">The title.</param>
/// <param name="Page">The page the bookmark leads to.</param>
internal sealed record OutlineEntry(string Title, int Page);

/// <summary>
/// The outline of one document tab, read once and shared: the outline pane shows it as a tree and the command palette lists its entries.
/// The read starts with <see cref="Start"/> (the window calls it after the first sharp page) or with the first <see cref="ReadAsync"/>,
/// whichever comes first, and never runs twice. A read that fails or is canceled gives an empty outline. Everything but the read itself
/// belongs to the UI thread.
/// </summary>
internal sealed class OutlineSource : IDisposable
{
    // Whatever a title holds, the palette lists this much of it.
    private const int MaxTitleLength = 200;

    private readonly Func<CancellationToken, Task<IReadOnlyList<PdfOutlineItem>>> _read;
    private readonly CancellationTokenSource _cancel = new();
    private Task<IReadOnlyList<PdfOutlineItem>>? _task;
    private bool _disposed;

    /// <summary>Makes the source of a tab: the outline comes from the document of its session.</summary>
    public OutlineSource(PdfTab tab)
        : this(ReaderOf(tab))
    {
    }

    /// <summary>Makes a source that reads its outline with <paramref name="read"/> (for tests: an outline no file has).</summary>
    internal OutlineSource(Func<CancellationToken, Task<IReadOnlyList<PdfOutlineItem>>> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        _read = read;
    }

    /// <summary>How many times the outline was read (at most once).</summary>
    public int ReadCount { get; private set; }

    /// <summary>The outline as read; null until the read has ended.</summary>
    public IReadOnlyList<PdfOutlineItem>? Items { get; private set; }

    /// <summary>The bookmarks in document order, depth first, those with a title and a page only; null until the read has ended.</summary>
    public IReadOnlyList<OutlineEntry>? Flat { get; private set; }

    /// <summary>Completes when the read has ended (also when it failed or the source was disposed); not completed before <see cref="Start"/>.</summary>
    public Task LoadTask { get; private set; } = new TaskCompletionSource().Task;

    /// <summary>Starts the read if it has not started yet.</summary>
    public void Start() => _ = Load();

    /// <summary>The outline; the first call (or <see cref="Start"/>) starts the read, later calls share it. The token only ends this wait.</summary>
    public Task<IReadOnlyList<PdfOutlineItem>> ReadAsync(CancellationToken cancellationToken) => Load().WaitAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancel.Cancel();
        _cancel.Dispose();
    }

    private static Func<CancellationToken, Task<IReadOnlyList<PdfOutlineItem>>> ReaderOf(PdfTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        return token => tab.Session.Document is { } document
            ? document.GetOutlineAsync(token)
            : Task.FromResult<IReadOnlyList<PdfOutlineItem>>([]);
    }

    private Task<IReadOnlyList<PdfOutlineItem>> Load()
    {
        if (_task is not null)
        {
            return _task;
        }

        if (_disposed)
        {
            return Task.FromResult<IReadOnlyList<PdfOutlineItem>>([]);
        }

        ReadCount++;
        var done = new TaskCompletionSource();
        LoadTask = done.Task;
        return _task = ReadCoreAsync(done);
    }

    private async Task<IReadOnlyList<PdfOutlineItem>> ReadCoreAsync(TaskCompletionSource done)
    {
        IReadOnlyList<PdfOutlineItem> items;
        try
        {
            items = await _read(_cancel.Token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A bad file, a canceled read or a failing reader all end in an empty outline.
            items = [];
        }

        if (!_disposed)
        {
            Items = items;
            Flat = Flatten(items);
        }

        done.TrySetResult();
        return items;
    }

    // Depth first without recursion; an entry that is its own ancestor (or listed twice) counts once, and the list never grows past what Core returns at most.
    private static List<OutlineEntry> Flatten(IReadOnlyList<PdfOutlineItem> items)
    {
        var flat = new List<OutlineEntry>();
        var seen = new HashSet<PdfOutlineItem>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<IEnumerator<PdfOutlineItem>>();
        stack.Push(items.GetEnumerator());
        while (stack.Count > 0 && flat.Count < PdfDocument.MaxOutlineItems)
        {
            var level = stack.Peek();
            if (!level.MoveNext())
            {
                stack.Pop();
                continue;
            }

            var item = level.Current;
            if (item is null || !seen.Add(item))
            {
                continue;
            }

            if (item.PageIndex is { } page && page >= 0 && !string.IsNullOrWhiteSpace(item.Title))
            {
                flat.Add(new OutlineEntry(item.Title.Length > MaxTitleLength ? item.Title[..MaxTitleLength] : item.Title, page));
            }

            if (item.Children.Count > 0 && stack.Count < PdfDocument.MaxOutlineDepth)
            {
                stack.Push(item.Children.GetEnumerator());
            }
        }

        return flat;
    }
}
