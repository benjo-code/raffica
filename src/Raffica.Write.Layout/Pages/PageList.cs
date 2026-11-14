namespace Raffica.Write.Layout;

/// <summary>
/// The published pages: append-only, one writer (the layout, under its gate), any number of readers. A reader that reads
/// <see cref="Count"/> may read every page below it; the page is stored before the count that covers it is published.
/// </summary>
internal sealed class PageList
{
    private LaidOutPage[] _items = new LaidOutPage[32];
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public LaidOutPage this[int index]
    {
        get
        {
            var count = Count;
            if ((uint)index >= (uint)count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "The page is not laid out yet; check LaidOutPages first.");
            }

            // Read after the count: the array is the one that held the page or a later copy of it.
            return Volatile.Read(ref _items)[index];
        }
    }

    /// <summary>Puts a new version of a published page in its place (the total of a page count that was unknown). Only the layout thread calls this.</summary>
    public void Replace(int index, LaidOutPage page)
    {
        if ((uint)index >= (uint)_count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Only a published page can be replaced.");
        }

        Volatile.Write(ref _items[index], page);
    }

    /// <summary>Adds a page. Only the layout thread calls this, one call at a time.</summary>
    public void Add(LaidOutPage page)
    {
        var items = _items;
        var count = _count;
        if (count == items.Length)
        {
            var bigger = new LaidOutPage[items.Length * 2];
            Array.Copy(items, bigger, count);
            Volatile.Write(ref _items, bigger);
            items = bigger;
        }

        items[count] = page;
        Volatile.Write(ref _count, count + 1);
    }
}
