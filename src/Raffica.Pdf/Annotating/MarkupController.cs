using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Raffica.Core.Annotations;
using Raffica.Core.Pdf;
using Raffica.Core.Text;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Text;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Annotating;

/// <summary>
/// The text markup tools (highlight, underline, strike-out). The text is selected as in the viewer (the shared <see cref="SelectionController"/>);
/// releasing after a drag, a double click (word) or a triple click (line) marks the selection, one markup per page. A click that moved less
/// than 4 DIPs does nothing. A double click waits for the platform double-click time so that a triple click makes one markup, not two; the
/// wait is registered as pending with the tab, so closing right then still counts the mark. Nothing is read or created before the first
/// sharp page was drawn. Used on the UI thread only.
/// </summary>
internal sealed class MarkupController : IToolController
{
    // Farther than this (in DIPs) from where the button went down, a press is a drag and no click.
    private const double ClickSlop = 4;
    private static readonly TimeSpan DefaultDoubleClick = TimeSpan.FromMilliseconds(500);

    private readonly PdfTab _tab;
    private readonly PageView _view;
    private readonly ToolState _tools;
    private readonly SelectionController _selection;
    private readonly DialogQueue _dialogs;
    private readonly TimeProvider _time;
    private bool _pressed;
    private bool _moved;
    private int _clicks;
    private Point _pressPoint;
    private ITimer? _waitTimer;
    private IDisposable? _waitPending;
    private AnnotateTool _waitTool;
    private int _generation;
    private bool _disposed;

    public MarkupController(PdfTab tab, PageView view, ToolState tools, SelectionController selection, DialogQueue dialogs, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(dialogs);
        _tab = tab;
        _view = view;
        _tools = tools;
        _selection = selection;
        _dialogs = dialogs;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Never raised: the markup has no preview of its own (the selection is drawn by the view).</summary>
    public event Action? Changed
    {
        add { }
        remove { }
    }

    /// <summary>Whether a double click is waiting for a possible third click.</summary>
    public bool IsWaiting => _waitTimer is not null;

    public bool Handles(AnnotateTool tool) => tool is AnnotateTool.Highlight or AnnotateTool.Underline or AnnotateTool.StrikeOut;

    public void Render(DrawingContext context, PageView view)
    {
    }

    public Cursor? CursorAt(Point point) => _selection.IsOverText(point) ? TextInteraction.IBeamCursor : null;

    public void Press(Point point, int clickCount)
    {
        if (_waitTimer is not null || _waitPending is not null)
        {
            if (clickCount <= 1)
            {
                // The time of a double click ran out: this is a new gesture and the word is marked now.
                _ = ApplyAsync(_waitTool);
            }
            else
            {
                StopTimer();
            }
        }

        _pressed = false;
        if (!_view.FirstSharpPageShown)
        {
            return;
        }

        _pressed = true;
        _moved = false;
        _pressPoint = point;
        _clicks = clickCount;
        _selection.Press(point, clickCount);
    }

    public void Move(Point point)
    {
        if (!_pressed)
        {
            return;
        }

        if (!_moved && Distance(point, _pressPoint) > ClickSlop)
        {
            _moved = true;
        }

        _selection.Drag(point);
    }

    public void Release(Point point)
    {
        if (!_pressed)
        {
            return;
        }

        _pressed = false;
        var tool = _tools.Tool;
        if (_clicks == 1 && !_moved)
        {
            // A click puts a caret and marks nothing.
            _selection.Clear();
            return;
        }

        _selection.Release();
        if (!_selection.HasSelection)
        {
            DropWait();
            return;
        }

        if (_moved || _clicks >= 3)
        {
            _ = ApplyAsync(tool);
            return;
        }

        // A double click: a third click may follow, which would make a line out of the word.
        _waitTool = tool;
        _waitPending ??= _tab.Annotations.BeginPending();
        var generation = ++_generation;
        _waitTimer?.Dispose();
        _waitTimer = _time.CreateTimer(
            _ => Dispatcher.UIThread.Post(() => OnWaitElapsed(generation)),
            null,
            DoubleClickTime(),
            Timeout.InfiniteTimeSpan);
    }

    public bool Cancel()
    {
        var dropped = _pressed || _waitPending is not null || _selection.HasSelection;
        _pressed = false;
        DropWait();
        _selection.Clear();
        return dropped;
    }

    /// <summary>
    /// Marks the selected text with the colour of the tool: one markup per page, the rectangles read from the selection at once, the
    /// selection cleared. The tab counts as dirty from the start until the last markup is made; a failure ends in a card and nothing is created.
    /// </summary>
    /// <param name="kind">The markup tool.</param>
    /// <returns>A task that ends when the markups are made (and a failure card was closed).</returns>
    public async Task ApplyAsync(AnnotateTool kind)
    {
        // Taken before anything is awaited: closing the tab waits for it. It is released on success, failure and cancel.
        var pending = _waitPending ?? _tab.Annotations.BeginPending();
        _waitPending = null;
        StopTimer();
        string? failure = null;
        try
        {
            if (ToKind(kind) is not { } markup || !_view.FirstSharpPageShown || _selection.Selection is not { IsEmpty: false } selection)
            {
                return;
            }

            var jobs = new List<(int Page, IReadOnlyList<PageRect> Rects)>();
            for (var page = selection.Start.Page; page <= selection.End.Page; page++)
            {
                if (_selection.MarkupRectsOn(page) is { Count: > 0 } rects)
                {
                    jobs.Add((page, rects));
                }
            }

            _selection.Clear();
            var color = _tools.MarkupColor(kind);
            var author = _tools.Author;
            var editor = _tab.Annotations.Editor;
            foreach (var (page, rects) in jobs)
            {
                await editor.ApplyMarkupAsync(page, markup, rects, color, null, author);
            }
        }
        catch (OperationCanceledException)
        {
            // The document went away or the work was canceled: nothing more to do.
        }
        catch (PdfAnnotationLimitException)
        {
            failure = PdfStrings.AnnotateLimitReached;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failure = PdfStrings.AnnotateFailed;
        }
        finally
        {
            pending.Dispose();
        }

        if (failure is not null && !_disposed)
        {
            await ShowNoticeAsync(failure);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DropWait();
    }

    private async Task ShowNoticeAsync(string message)
    {
        try
        {
            await _dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.AnnotateCardTitle, message, () => done(true)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The card could not be shown; the markup was not made either way.
        }
    }

    private void OnWaitElapsed(int generation)
    {
        if (_disposed || generation != _generation || _waitTimer is null)
        {
            return;
        }

        _ = ApplyAsync(_waitTool);
    }

    private void StopTimer()
    {
        _generation++;
        _waitTimer?.Dispose();
        _waitTimer = null;
    }

    private void DropWait()
    {
        StopTimer();
        _waitPending?.Dispose();
        _waitPending = null;
    }

    private static TimeSpan DoubleClickTime()
    {
        var platform = Application.Current?.PlatformSettings;
        return platform is null ? DefaultDoubleClick : platform.GetDoubleTapTime(PointerType.Mouse);
    }

    private static AnnotationKind? ToKind(AnnotateTool tool) => tool switch
    {
        AnnotateTool.Highlight => AnnotationKind.Highlight,
        AnnotateTool.Underline => AnnotationKind.Underline,
        AnnotateTool.StrikeOut => AnnotationKind.StrikeOut,
        _ => null,
    };

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
