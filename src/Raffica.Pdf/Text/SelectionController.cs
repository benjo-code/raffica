using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Raffica.Core.Pdf;
using Raffica.Core.Text;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Text;

/// <summary>
/// The text selection of one document tab. A press puts the caret (by character, a double click takes a word, a triple click a line),
/// a drag moves the other end of the selection (by character, word or line, as it started), a release ends it; a click without a drag
/// clears the selection. The geometry is Core's (<see cref="PageText.CaretAt"/>, <see cref="PageText.WordAt"/>, <see cref="PageText.LineAt"/>,
/// <see cref="PageText.RectsOf"/>), the copied text is Core's <see cref="TextFlow"/>. The text of a page is read from Core's cache;
/// a page whose text is not loaded yet is requested (once the first sharp page was drawn) and meanwhile nothing happens on it: no
/// selection starts, nothing is drawn, nothing throws. All members belong to the UI thread.
/// </summary>
internal sealed class SelectionController : IDisposable
{
    private readonly PdfTab _tab;
    private readonly PageView _view;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Dictionary<int, IReadOnlyList<PageRect>> _rects = [];
    private readonly HashSet<int> _requested = [];
    private TextSelection? _selection;
    private TextPosition _anchorStart;
    private TextPosition _anchorEnd;
    private int _mode = 1;
    private bool _dragging;
    private bool _disposed;

    public SelectionController(PdfTab tab, PageView view)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(view);
        _tab = tab;
        _view = view;
        view.ViewChanged += Prefetch;
        view.FirstSharpPage += Prefetch;
    }

    /// <summary>Raised when the selection or the rectangles to draw changed.</summary>
    public event Action? Changed;

    /// <summary>The selection (null: none; its two ends may be equal while the pointer is down).</summary>
    public TextSelection? Selection => _selection;

    /// <summary>Whether any text is selected.</summary>
    public bool HasSelection => _selection is { IsEmpty: false };

    /// <summary>Puts the caret at the place of the pointer (a double click selects the word there, a triple click the line).</summary>
    /// <param name="point">The place in the coordinates of the page view.</param>
    /// <param name="clickCount">1, 2 or 3 (more counts as 3).</param>
    public void Press(Point point, int clickCount)
    {
        _dragging = false;
        if (_view.PageAt(point) is not { } spot)
        {
            Set(null);
            return;
        }

        if (!TryText(spot.Page, out var text))
        {
            Set(null);
            Request(spot.Page);
            return;
        }

        _mode = Math.Clamp(clickCount, 1, 3);
        var caret = new TextPosition(spot.Page, text.CaretAt(spot.X, spot.Y));
        var index = _mode == 1 ? -1 : text.CharAt(spot.X, spot.Y);
        if (index < 0)
        {
            // A press between the characters (or a single click) is a caret; a drag from it selects by character.
            _mode = 1;
            _anchorStart = _anchorEnd = caret;
            Set(new TextSelection(caret, caret));
        }
        else
        {
            var (start, end) = Unit(text, index);
            _anchorStart = new TextPosition(spot.Page, start);
            _anchorEnd = new TextPosition(spot.Page, end);
            Set(new TextSelection(_anchorStart, _anchorEnd));
        }

        _dragging = true;
    }

    /// <summary>Moves the other end of the selection to the place of the pointer, which may be outside the pages (the nearest page counts).</summary>
    public void Drag(Point point)
    {
        if (!_dragging || _view.NearestSpot(point) is not { } spot)
        {
            return;
        }

        if (!TryText(spot.Page, out var text))
        {
            Request(spot.Page);
            return;
        }

        var caret = new TextPosition(spot.Page, text.CaretAt(spot.X, spot.Y));
        if (_mode == 1)
        {
            Set(new TextSelection(_anchorStart, caret));
            return;
        }

        // By word or line: the unit under the pointer is added to the unit that was pressed, in the direction of the drag.
        var index = text.CharAt(spot.X, spot.Y);
        var (start, end) = index >= 0 ? Unit(text, index) : (caret.Caret, caret.Caret);
        var unitStart = new TextPosition(spot.Page, start);
        var unitEnd = new TextPosition(spot.Page, end);
        Set(unitStart < _anchorStart
            ? new TextSelection(_anchorEnd, unitStart)
            : new TextSelection(_anchorStart, unitEnd > _anchorEnd ? unitEnd : _anchorEnd));
    }

    /// <summary>Ends the drag; a click that selected nothing clears the selection.</summary>
    public void Release()
    {
        _dragging = false;
        if (_selection is { IsEmpty: true })
        {
            Set(null);
        }
    }

    /// <summary>
    /// Selects the text of the pages that are shown, from the start of the first to the end of the last. Does nothing while the text of
    /// those two pages is not loaded (it is requested).
    /// </summary>
    public void SelectAll()
    {
        var (first, last) = _view.VisiblePages;
        if (last < first)
        {
            return;
        }

        var ready = TryText(first, out _) & TryText(last, out var lastText);
        if (!ready)
        {
            Request(first);
            Request(last);
            return;
        }

        _dragging = false;
        Set(new TextSelection(new TextPosition(first, 0), new TextPosition(last, lastText!.Chars.Count)));
    }

    /// <summary>Drops the selection.</summary>
    public void Clear()
    {
        _dragging = false;
        Set(null);
    }

    /// <summary>The rectangles of the selection on a page in page points; empty while the text of the page is not loaded.</summary>
    public IReadOnlyList<PageRect> RectsOn(int page)
    {
        if (_selection is not { IsEmpty: false } selection || page < selection.Start.Page || page > selection.End.Page || !TryText(page, out var text))
        {
            return [];
        }

        if (_rects.TryGetValue(page, out var cached))
        {
            return cached;
        }

        var count = text.Chars.Count;
        var from = Math.Clamp(page == selection.Start.Page ? selection.Start.Caret : 0, 0, count);
        var to = Math.Clamp(page == selection.End.Page ? selection.End.Caret : count, 0, count);
        var rects = from < to ? text.RectsOf(from, to) : [];
        _rects[page] = rects;
        return rects;
    }

    /// <summary>
    /// The rectangles of the selection on a page as a text markup wants them: like <see cref="RectsOn"/>, but without the blanks and line
    /// breaks at either end of the selection on that page (a line that was selected with its trailing space is not marked past its last letter).
    /// </summary>
    /// <param name="page">The 0-based page index.</param>
    /// <returns>The rectangles in page points; empty while the text of the page is not loaded or nothing of the page is selected.</returns>
    public IReadOnlyList<PageRect> MarkupRectsOn(int page)
    {
        if (_selection is not { IsEmpty: false } selection || page < selection.Start.Page || page > selection.End.Page || !TryText(page, out var text))
        {
            return [];
        }

        var count = text.Chars.Count;
        var from = Math.Clamp(page == selection.Start.Page ? selection.Start.Caret : 0, 0, count);
        var to = Math.Clamp(page == selection.End.Page ? selection.End.Caret : count, 0, count);
        while (from < to && IsBlank(text.Chars[from]))
        {
            from++;
        }

        while (to > from && IsBlank(text.Chars[to - 1]))
        {
            to--;
        }

        return from < to ? text.RectsOf(from, to) : [];
    }

    private static bool IsBlank(TextChar c) => c.IsGenerated || (System.Text.Rune.IsValid(c.CodePoint) && System.Text.Rune.IsWhiteSpace(new System.Text.Rune(c.CodePoint)));

    /// <summary>Whether a character of the page lies under the point (the I-beam cursor); false while the text of the page is not loaded.</summary>
    public bool IsOverText(Point point)
    {
        if (_view.PageAt(point) is not { } spot)
        {
            return false;
        }

        if (!TryText(spot.Page, out var text))
        {
            Request(spot.Page);
            return false;
        }

        return text.CharAt(spot.X, spot.Y) >= 0;
    }

    /// <summary>
    /// The selected text in both forms, or null when nothing is selected. The text of every page the selection covers is awaited first
    /// (the cache of Core holds only a few pages, so the pages are held here for the one call).
    /// </summary>
    public async Task<SelectedText?> GetTextAsync()
    {
        if (_selection is not { IsEmpty: false } selection || PageTexts.SourceOf(_tab) is not { } source)
        {
            return null;
        }

        try
        {
            var pages = new Dictionary<int, PageText>();
            for (var page = selection.Start.Page; page <= selection.End.Page; page++)
            {
                pages[page] = await source.GetAsync(page, PdfPriority.Interactive, _cancel.Token);
            }

            var slices = TextFlow.SlicesOf(selection, page => pages[page]);
            var plain = TextFlow.Plain(slices);
            return plain.Length == 0 ? null : new SelectedText(plain, TextFlow.Html(slices));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A closed document, a canceled wait or a damaged page: there is nothing to copy.
            return null;
        }
    }

    /// <summary>
    /// Copies the selection to the clipboard as plain text and as HTML. Returns whether something was copied; nothing selected, no
    /// clipboard or a clipboard that refuses leave everything as it was.
    /// </summary>
    /// <param name="clipboard">The clipboard; null takes the one of the window of the view.</param>
    public async Task<bool> CopyAsync(IClipboard? clipboard = null)
    {
        var text = await GetTextAsync();
        clipboard ??= TopLevel.GetTopLevel(_view)?.Clipboard;
        if (text is null || clipboard is null)
        {
            return false;
        }

        try
        {
            await PageClipboard.SetAsync(clipboard, text);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>Loads the text of every page that is shown and returns when it is there (the view asks for it itself as it scrolls).</summary>
    public async Task EnsureVisibleTextAsync()
    {
        var (first, last) = _view.VisiblePages;
        if (PageTexts.SourceOf(_tab) is not { } source)
        {
            return;
        }

        for (var page = first; page <= last; page++)
        {
            try
            {
                await source.GetAsync(page, PdfPriority.Interactive, _cancel.Token);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A damaged page has no text; the others still load.
            }
        }

        _rects.Clear();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _view.ViewChanged -= Prefetch;
        _view.FirstSharpPage -= Prefetch;
        _cancel.Cancel();
        _cancel.Dispose();
    }

    // The word (double click) or the line (triple click) around a character.
    private (int Start, int End) Unit(PageText text, int index) => _mode == 2 ? text.WordAt(index) : text.LineAt(index);

    private bool TryText(int page, out PageText text)
    {
        if (PageTexts.TryGet(_tab, page, out var found))
        {
            text = found;
            return true;
        }

        text = null!;
        return false;
    }

    private void Set(TextSelection? selection)
    {
        if (_selection == selection)
        {
            return;
        }

        _selection = selection;
        _rects.Clear();
        Changed?.Invoke();
    }

    // The pages on screen get their text, so a press, the cursor and the drawing of the selection find it there. Only after the first
    // sharp page: the index is heavy work that must not compete with it, and it runs as background work so it never takes the place of a
    // tile that is on screen. A press, a copy or Select all needs the text now and asks at the interactive priority (a load that is already
    // running keeps the priority it started with).
    private void Prefetch()
    {
        if (_disposed || !_view.FirstSharpPageShown)
        {
            return;
        }

        var (first, last) = _view.VisiblePages;
        for (var page = first; page <= last; page++)
        {
            Request(page, PdfPriority.Background);
        }
    }

    private void Request(int page, PdfPriority priority = PdfPriority.Interactive)
    {
        if (_disposed || !_view.FirstSharpPageShown || PageTexts.SourceOf(_tab) is not { } source)
        {
            return;
        }

        try
        {
            if (source.TryGet(page, out _) || !_requested.Add(page))
            {
                return;
            }
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        _ = LoadAsync(source, page, priority);
    }

    private async Task LoadAsync(PageTextSource source, int page, PdfPriority priority)
    {
        try
        {
            await source.GetAsync(page, priority, _cancel.Token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A canceled wait, a closed document or a damaged page: the page keeps no text.
        }
        finally
        {
            _requested.Remove(page);
        }

        if (!_disposed)
        {
            // What the selection draws on this page can be there now.
            _rects.Remove(page);
            if (_selection is { IsEmpty: false })
            {
                Changed?.Invoke();
            }
        }
    }
}
