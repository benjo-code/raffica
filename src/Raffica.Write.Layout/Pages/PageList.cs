namespace Raffica.Write.Layout;

/// <summary>
/// The published pages: one writer (the layout, under its gate), any number of readers that take no lock.
/// </summary>
/// <remarks>
/// The state is one immutable-by-convention <see cref="Snapshot"/> (the page array and the number of pages in use) that the writer replaces
/// with a single volatile write. A reader takes the reference once (an acquire read) and sees the array and the count of the same moment, so
/// it never sees a count that belongs to another array, and every slot below the count was written before the snapshot that covers it was
/// published. Three kinds of change:
/// <list type="bullet">
/// <item><see cref="Add"/> writes the slot after the last page of the current array and publishes a new snapshot with one more page: a reader
/// that holds the older snapshot never reads that slot.</item>
/// <item><see cref="Replace"/> stores a new version of a page in place (a page whose page count became known): the page has the same index and
/// position, so a reader that holds an older snapshot of the same array may see either version, both complete.</item>
/// <item><see cref="Splice"/>, <see cref="ReplaceRange"/> and <see cref="Truncate"/> always make a new array and publish it as a whole, so a
/// reader sees the pages from before the call or the pages from after it, never a mixture, and an array a reader may hold is never written to
/// again (a later <see cref="Add"/> writes into the new array only).</item>
/// </list>
/// The number of pages may therefore fall (an edit made the document shorter, or cut the pages that follow the edited one until they are
/// laid out again). A reader that read <see cref="Count"/> and then asks for a page below it can find the list shorter: <see cref="this[int]"/>
/// throws for an index that is not below the count of the moment, <see cref="TryGet"/> says so instead. Every page object in the list has
/// <see cref="LaidOutPage.Index"/> equal to its position.
/// </remarks>
internal sealed class PageList
{
    private Snapshot _state = new(new LaidOutPage[32], 0);

    private sealed class Snapshot(LaidOutPage[] items, int count)
    {
        public LaidOutPage[] Items { get; } = items;

        public int Count { get; } = count;
    }

    public int Count => Volatile.Read(ref _state).Count;

    public LaidOutPage this[int index]
    {
        get
        {
            var state = Volatile.Read(ref _state);
            if ((uint)index >= (uint)state.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "The page is not laid out (yet, or any more); check LaidOutPages first, or use TryGet.");
            }

            return Volatile.Read(ref state.Items[index]);
        }
    }

    /// <summary>The page at <paramref name="index"/>, or false when the list has no such page now.</summary>
    public bool TryGet(int index, out LaidOutPage page)
    {
        var state = Volatile.Read(ref _state);
        if ((uint)index >= (uint)state.Count)
        {
            page = null!;
            return false;
        }

        page = Volatile.Read(ref state.Items[index]);
        return true;
    }

    /// <summary>Puts a new version of a published page in its place (the total of a page count that was unknown). Only the layout thread calls this.</summary>
    public void Replace(int index, LaidOutPage page)
    {
        var state = _state;
        if ((uint)index >= (uint)state.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Only a published page can be replaced.");
        }

        Volatile.Write(ref state.Items[index], page);
    }

    /// <summary>Adds a page. Only the layout thread calls this, one call at a time.</summary>
    public void Add(LaidOutPage page)
    {
        var state = _state;
        var items = state.Items;
        var count = state.Count;
        if (count == items.Length)
        {
            var bigger = new LaidOutPage[items.Length * 2];
            Array.Copy(items, bigger, count);
            items = bigger;
        }

        Volatile.Write(ref items[count], page);
        Volatile.Write(ref _state, new Snapshot(items, count + 1));
    }

    /// <summary>
    /// Publishes in one step the pages <c>[0, first)</c> of the list as they are, then <paramref name="pages"/>, then the old pages from
    /// <paramref name="keepFrom"/> on (none when it is at or past the count): the list afterwards is that sequence. Every page must have its position as its index,
    /// so a caller that keeps pages from <c>keepFrom</c> makes the new pages end right before it (<c>first + pages.Count == keepFrom</c>) or keeps none;
    /// <paramref name="adjustKept"/> may replace a kept page by a version with the same index.
    /// </summary>
    public void Splice(int first, IReadOnlyList<LaidOutPage> pages, int keepFrom, Func<LaidOutPage, LaidOutPage>? adjustKept = null)
    {
        ArgumentNullException.ThrowIfNull(pages);
        var state = _state;
        if (first < 0 || first > state.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(first), first, "The first replaced page must be a published page or the one after the last.");
        }

        var kept = Math.Max(0, state.Count - Math.Max(keepFrom, first));
        var total = first + pages.Count + kept;
        var items = new LaidOutPage[Math.Max(32, total + (total >> 2))];
        Array.Copy(state.Items, items, first);
        for (var i = 0; i < pages.Count; i++)
        {
            items[first + i] = pages[i];
        }

        for (var i = 0; i < kept; i++)
        {
            var page = state.Items[Math.Max(keepFrom, first) + i];
            items[first + pages.Count + i] = adjustKept is null ? page : adjustKept(page);
        }

        Volatile.Write(ref _state, new Snapshot(items, total));
    }

    /// <summary>Replaces the pages from <paramref name="first"/> on by <paramref name="pages"/>, as one step; the count stays when the new pages end at or before the old end, and rises to the new end otherwise.</summary>
    public void ReplaceRange(int first, IReadOnlyList<LaidOutPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        Splice(first, pages, first + pages.Count);
    }

    /// <summary>Publishes the first <paramref name="count"/> pages only, as one step (a count at or above the current one changes nothing).</summary>
    public void Truncate(int count)
    {
        var state = _state;
        if (count >= state.Count)
        {
            return;
        }

        var items = new LaidOutPage[Math.Max(32, count + (count >> 2))];
        Array.Copy(state.Items, items, count);
        Volatile.Write(ref _state, new Snapshot(items, count));
    }
}
