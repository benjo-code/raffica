using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>
/// The page a field result names: the page a body paragraph starts on and the number printed there (the section's format and start, the same text the footer's <c>PAGE</c> field shows, <see cref="LaidOutPage.Number"/>). One definition of a
/// printed page number, owned by the layout; a field refresh asks it through the app's host. A paragraph that is nested in a table or a content control answers with the page of the enclosing top-level block's first line (a named limit: a long table
/// that spans pages can differ from Word). Chapter prefixes of the page number (<c>w:chapStyle</c>) are not drawn by the layout and so not given here. The map from a paragraph to its top-level block is made once for a document stamp
/// (revision and derived version), so a refresh that asks for hundreds of entries walks the body once. Not thread-safe: the owner calls it on one thread (the UI thread, under the edit lease).
/// </summary>
public sealed class FieldPages(WordDocument document, DocumentLayout layout)
{
    private const int MaxDepth = 64;

    private readonly WordDocument _document = document ?? throw new ArgumentNullException(nameof(document));
    private readonly DocumentLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    private EditStamp _stamp;
    private Dictionary<Paragraph, int>? _tops;

    /// <summary>How many times the map of paragraphs was made (tests: once per stamp).</summary>
    internal int MapBuilds { get; private set; }

    /// <summary>The page index and the printed number of the page the paragraph starts on, or null for a layout of the writing view, a paragraph of no body block, or a page that is not laid out.</summary>
    public (int PageIndex, string PageText)? PageOf(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        if (_layout.Mode != LayoutMode.Pages)
        {
            return null;
        }

        var stamp = _document.Edits.Stamp;
        if (_tops is null || stamp != _stamp)
        {
            _tops = Build(_document.Body.Blocks);
            _stamp = stamp;
            MapBuilds++;
        }

        if (!_tops.TryGetValue(paragraph, out var top) || _layout.Locate(top) is not { } at)
        {
            return null;
        }

        return _layout.TryGetPage(at.Page, out var page) && page is not null ? (at.Page, page.Number) : null;
    }

    private static Dictionary<Paragraph, int> Build(IList<Block> body)
    {
        var map = new Dictionary<Paragraph, int>(ReferenceEqualityComparer.Instance);
        for (var top = 0; top < body.Count; top++)
        {
            Add(map, body[top], top, 0);
        }

        return map;
    }

    private static void Add(Dictionary<Paragraph, int> map, Block block, int top, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        switch (block)
        {
            case Paragraph paragraph:
                map.TryAdd(paragraph, top);
                break;
            case ContentControlBlock control:
                foreach (var inner in control.Content)
                {
                    Add(map, inner, top, depth + 1);
                }

                break;
            case Table table:
                AddRows(map, table.Rows, top, depth + 1);
                break;
            default:
                break;
        }
    }

    private static void AddRows(Dictionary<Paragraph, int> map, IEnumerable<RowItem> items, int top, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        foreach (var item in items)
        {
            switch (item)
            {
                case TableRow row:
                    AddRows(map, row.Items, top, depth + 1);
                    break;
                case TableCell cell:
                    foreach (var inner in cell.Blocks)
                    {
                        Add(map, inner, top, depth + 1);
                    }

                    break;
                default:
                    break;
            }
        }
    }
}
