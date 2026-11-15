using System.Diagnostics;
using System.Globalization;
using Avalonia.Input;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.Write;

/// <summary>
/// The typing run (<c>--measure-typing</c>) of <see cref="Measurement"/>. Once the whole model is there and the layout is complete, it types at seven places
/// of the document (<see cref="TypingPlaces"/>), first in the page view and then in the writing view, one letter per key. Every letter is a
/// <see cref="TextInputEventArgs"/> raised on the page view, so it takes the path of the keyboard: <c>PageView.OnTextInput</c>, <c>EditSession.Apply</c>, the editor and the layout.
/// The key is raised inside an animation frame callback and the next key follows only after the frame that shows this one was drawn, so one key never overlaps the frame of the one before.
/// The frame that shows a key is the first render pass after it, and its pages are the ones that pass handed to the render thread (the page with the caret and any page visible with it);
/// the frame is drawn when the render thread has drawn the last of them. Two times are taken from the key event (a monotonic clock):
/// <list type="bullet">
/// <item><c>frame</c>: to the end of that draw. A key raised at the start of a frame is shown in the next frame, so this number holds the idle wait for the next frame of the display
/// (up to one frame period, 16.7 ms at 60 Hz), which no code of the app can shorten: it is never below about one frame period and is reported for the record only.</item>
/// <item>the judged time (<c>median-ms</c>, <c>p95-ms</c>, <c>max-ms</c>): the work the key causes, that is the time of the key handler (from the raise to the return of the raise: the
/// editor, the layout, the caret and the view) plus the duration of the render pass that shows it (<c>PageView.Render</c> on the UI thread) plus the sum of the durations of the page draws
/// of that frame on the render thread. It leaves out the idle waits (for the next frame, and the render thread waiting for the display), the layout pass of the view and the compositor, which
/// no key-dependent code of the app runs in.</item>
/// </list>
/// Rows: <c>typing case=&lt;case&gt; view=&lt;page|writing&gt; n=&lt;keys&gt; median-ms=… p95-ms=… max-ms=… handler-median-ms=… frame-median-ms=… frame-p95-ms=…</c> for every case and view, and
/// <c>typing-first when=&lt;after-open|after-switch&gt; view=… ms=…</c> (the judged time) for the first key after the open and the first key after the switch to the writing view (no limit).
/// The median of an even count is the mean of the two middle values, the 95th percentile is the nearest rank. A run with no frame within 5 s after a key, or without a document,
/// ends with exit code 1.
/// </summary>
internal sealed partial class Measurement
{
    /// <summary>The keys typed per case and view.</summary>
    public const int TypingKeys = 100;

    private const int Seed = 4242;
    private static readonly TimeSpan s_typingTimeout = TimeSpan.FromSeconds(240);
    private static readonly TimeSpan s_noFrameAfterKey = TimeSpan.FromSeconds(5);

    private sealed record Job(string Case, WriteView View, string? FirstWhen, TypingPlace Place, int Keys);

    private readonly int _typingKeys;
    private readonly List<double> _samples = [];
    private readonly List<double> _handlerSamples = [];
    private readonly List<double> _frameSamples = [];
    private List<Job>? _jobs;
    private Job? _job;
    private int _jobIndex;
    private int _keyInJob;
    private int _keySequence;
    private Random _random = new(Seed);
    private int _placeStage;
    private bool _awaiting;
    private long _keyAt;
    private long _handlerEnd;
    private long _renderStart;

    // The probes of the page view: the first render pass after the key (UI thread) and the page draws (render thread).
    private bool _renderSeen;
    private int _expectedDraws;
    private int _drawsAtRender;
    private long _renderStamp;
    private int _draws;
    private long _drawTicks;
    private long _drawTicksAtRender;
    private long _lastDrawStamp;
    private PageView? _probed;

    /// <summary>The line <c>typing case=… view=… n=… median-ms=… p95-ms=… max-ms=…</c> for the times of one case in milliseconds.</summary>
    public static string TypingLine(string name, string view, IReadOnlyList<double> milliseconds, IReadOnlyList<double>? handlerMilliseconds = null, IReadOnlyList<double>? frameMilliseconds = null)
    {
        var sorted = milliseconds.Order().ToArray();
        var line = string.Create(CultureInfo.InvariantCulture, $"typing case={name} view={view} n={sorted.Length} median-ms={Median(sorted):0.00} p95-ms={Percentile95(sorted):0.00} max-ms={sorted[^1]:0.00}");
        if (handlerMilliseconds is not null && frameMilliseconds is not null)
        {
            var handler = handlerMilliseconds.Order().ToArray();
            var frame = frameMilliseconds.Order().ToArray();
            line += string.Create(CultureInfo.InvariantCulture, $" handler-median-ms={Median(handler):0.00} frame-median-ms={Median(frame):0.00} frame-p95-ms={Percentile95(frame):0.00}");
        }

        return line;
    }

    private static double Median(double[] sorted)
    {
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static double Percentile95(double[] sorted) => sorted[(int)Math.Ceiling(0.95 * sorted.Length) - 1];

    /// <summary>The line <c>typing-first when=… view=… ms=…</c> for the first key after the open or after the switch.</summary>
    public static string TypingFirstLine(string when, string view, double milliseconds) =>
        string.Create(CultureInfo.InvariantCulture, $"typing-first when={when} view={view} ms={milliseconds:0.00}");

    private static string ViewName(WriteView view) => view == WriteView.Page ? "page" : "writing";

    private void StartTyping() => _window.RequestAnimationFrame(_ => TypingFrame());

    private void NextTypingFrame() => _window.RequestAnimationFrame(_ => TypingFrame());

    private void FailTyping(string message)
    {
        _error.WriteLine("error: " + message);
        Finish(Failed);
    }

    private void Probe(PageView view)
    {
        if (ReferenceEquals(_probed, view))
        {
            return;
        }

        if (_probed is { } old)
        {
            old.RenderProbe = null;
            old.DrawProbe = null;
        }

        _probed = view;
        view.RenderProbe = handed =>
        {
            if (!_awaiting || _renderSeen)
            {
                return;
            }

            if (handed < 0)
            {
                // The start of the first render pass after the key.
                if (_renderStart == 0)
                {
                    _renderStart = Stopwatch.GetTimestamp();
                }

                return;
            }

            _renderSeen = true;
            _expectedDraws = handed;
            _drawsAtRender = Volatile.Read(ref _draws);
            _drawTicksAtRender = Volatile.Read(ref _drawTicks);
            _renderStamp = Stopwatch.GetTimestamp();
        };
        view.DrawProbe = (start, end) =>
        {
            Interlocked.Add(ref _drawTicks, end - start);
            Volatile.Write(ref _lastDrawStamp, end);
            Interlocked.Increment(ref _draws);
        };
    }

    private void ReleaseProbe()
    {
        if (_probed is { } view)
        {
            view.RenderProbe = null;
            view.DrawProbe = null;
            _probed = null;
        }
    }

    // One animation frame of the typing run.
    private void TypingFrame()
    {
        if (_finished)
        {
            return;
        }

        if (_window.ActiveView is not { } view)
        {
            FailTyping("no document is shown, nothing to type into");
            return;
        }

        if (view.Runner.IsFaulted)
        {
            FailTyping("the layout failed");
            return;
        }

        if (_jobs is null)
        {
            if (!view.Runner.FullModelReady || !view.IsLayoutComplete)
            {
                NextTypingFrame();
                return;
            }

            _jobs = Plan(view);
            if (_jobs.Count == 0)
            {
                FailTyping("the document has no place to type at");
                return;
            }

            Probe(view);
        }

        if (_awaiting)
        {
            var drawn = Volatile.Read(ref _draws) >= _drawsAtRender + _expectedDraws;
            if (!_renderSeen || !drawn)
            {
                if (Stopwatch.GetElapsedTime(_keyAt) > s_noFrameAfterKey)
                {
                    FailTyping("no frame followed a key within 5 s");
                    return;
                }

                NextTypingFrame();
                return;
            }

            var end = _expectedDraws == 0 ? _renderStamp : Math.Max(_renderStamp, Volatile.Read(ref _lastDrawStamp));
            var handler = Stopwatch.GetElapsedTime(_keyAt, _handlerEnd).TotalMilliseconds;
            _handlerSamples.Add(handler);
            _frameSamples.Add(Stopwatch.GetElapsedTime(_keyAt, end).TotalMilliseconds);
            var drawing = Stopwatch.GetElapsedTime(0, Volatile.Read(ref _drawTicks) - _drawTicksAtRender).TotalMilliseconds;
            _samples.Add(handler + Stopwatch.GetElapsedTime(_renderStart, _renderStamp).TotalMilliseconds + drawing);
            _awaiting = false;
            _keyInJob++;
        }

        if (_job is null || _keyInJob >= _job.Keys)
        {
            if (_job is not null)
            {
                Write(_job.FirstWhen is { } when ? TypingFirstLine(when, ViewName(_job.View), _samples[0]) : TypingLine(_job.Case, ViewName(_job.View), _samples, _handlerSamples, _frameSamples));
            }

            if (_jobIndex >= _jobs.Count)
            {
                ReleaseProbe();
                Finish(Success);
                return;
            }

            _job = _jobs[_jobIndex++];
            _keyInJob = 0;
            _samples.Clear();
            _handlerSamples.Clear();
            _frameSamples.Clear();
            _placeStage = 0;
        }

        if (view.Mode != _job.View)
        {
            // The switch of the view; the first key after it is its own row.
            view.Mode = _job.View;
            NextTypingFrame();
            return;
        }

        if (!view.IsLayoutComplete)
        {
            NextTypingFrame();
            return;
        }

        if (_placeStage < 2)
        {
            // The view scrolls to the block of the place (the caret is looked for near the page in view), the caret goes there, and one frame later the view brings the caret itself
            // into view; a frame passes before the first key so that the scroll is drawn.
            if (_placeStage == 0)
            {
                var session = view.Tab.EnsureEditor();
                _ = session.Selection;
                var position = new TextPosition(_job.Place.Story, _job.Place.Paragraph, _job.Place.Offset);
                session.SetSelection(new TextRange(position, position));
                view.ScrollToBlock(_job.Place.HintBlock);
            }
            else
            {
                view.EnsureCaretVisible();
            }

            _placeStage++;
            NextTypingFrame();
            return;
        }

        var text = TypingPlaces.KeyText(_random, _keySequence++);
        _renderSeen = false;
        _renderStart = 0;
        _awaiting = true;
        _keyAt = Stopwatch.GetTimestamp();
        view.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = view });
        _handlerEnd = Stopwatch.GetTimestamp();
        NextTypingFrame();
    }

    private List<Job> Plan(PageView view)
    {
        var layout = view.Runner.PageLayout;
        var places = TypingPlaces.Pick(view.Tab.Docx.Model, layout);
        var jobs = new List<Job>();
        foreach (var (mode, when) in new[] { (WriteView.Page, "after-open"), (WriteView.Writing, "after-switch") })
        {
            if (places.Count == 0)
            {
                break;
            }

            jobs.Add(new Job("first", mode, when, places[0], 1));
            foreach (var place in places)
            {
                jobs.Add(new Job(place.Name, mode, null, place, _typingKeys));
            }
        }

        return jobs;
    }
}

/// <summary>One place to type at: a paragraph of a story, an offset in its projected text and the index of the top-level body block on whose page the place is (where the view looks for it first).</summary>
internal sealed record TypingPlace(string Name, StoryRef Story, Paragraph Paragraph, int Offset, int HintBlock);

/// <summary>
/// The places of the typing run, found from the model and the pages layout alone: the middle of a long chapter, the end of a paragraph that ends a page (its growth moves the page break),
/// the paragraph before a section break, the end of a chapter, a table cell, a footnote and a header. A near copy of <c>EditCases</c> of <c>bench/write-layout</c> (the bench cannot reference
/// the app and the app must not carry the bench); change both together.
/// </summary>
internal static class TypingPlaces
{
    public static readonly string[] Names = ["middle", "page-break", "section-break", "chapter-end", "table-cell", "footnote", "header"];

    /// <summary>The text of the keystroke <paramref name="index"/>: lower case letters with a space at every sixth one, from a seeded generator.</summary>
    public static string KeyText(Random random, int index) => index % 6 == 5 ? " " : ((char)('a' + random.Next(26))).ToString();

    public static List<TypingPlace> Pick(WordDocument document, DocumentLayout pages)
    {
        var found = new List<TypingPlace>();
        var blocks = document.Body.Blocks;
        var resolver = new StyleResolver(document.Styles, document.Settings, document.Numbering);

        int Length(StoryRef story, Paragraph paragraph) => ParagraphProjection.Build(paragraph, pages.ProjectionHost(story, paragraph)).Text.Length;

        var texts = new List<(int Index, Paragraph Paragraph, int Length)>();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph p && p.SectionBreak is null)
            {
                texts.Add((i, p, Length(StoryRef.Body, p)));
            }
        }

        var middleIndex = blocks.Count / 2;
        Paragraph? middle = null;
        foreach (var t in texts.Where(t => t.Length >= 200).OrderBy(t => Math.Abs(t.Index - middleIndex)))
        {
            middle = t.Paragraph;
            found.Add(new TypingPlace("middle", StoryRef.Body, t.Paragraph, t.Length / 2, t.Index));
            break;
        }

        foreach (var t in texts.Where(t => t.Length >= 1).OrderBy(t => Math.Abs(t.Index - middleIndex)))
        {
            if (t.Index + 1 < blocks.Count
                && !ReferenceEquals(middle, t.Paragraph)
                && pages.Caret(new TextPosition(StoryRef.Body, t.Paragraph, t.Length), 0) is { } end
                && pages.Locate(t.Index + 1) is { } next
                && next.Page == end.Page + 1)
            {
                found.Add(new TypingPlace("page-break", StoryRef.Body, t.Paragraph, t.Length, t.Index));
                break;
            }
        }

        foreach (var i in Enumerable.Range(1, Math.Max(0, blocks.Count - 1)).Where(i => blocks[i] is Paragraph { SectionBreak: not null }).OrderBy(i => Math.Abs(i - middleIndex)))
        {
            if (blocks[i - 1] is Paragraph before && before.SectionBreak is null && Length(StoryRef.Body, before) > 0)
            {
                found.Add(new TypingPlace("section-break", StoryRef.Body, before, Length(StoryRef.Body, before), i - 1));
                break;
            }
        }

        var headings = Enumerable.Range(0, blocks.Count).Where(i => blocks[i] is Paragraph h && resolver.ForParagraph(h).OutlineLevel == 0).ToList();
        if (headings.Count > 1)
        {
            var heading = headings[Math.Min(headings.Count - 1, headings.Count * 3 / 4)];
            var last = texts.LastOrDefault(t => t.Index < heading && t.Length >= 20);
            if (last.Paragraph is not null && !found.Any(f => ReferenceEquals(f.Paragraph, last.Paragraph)))
            {
                found.Add(new TypingPlace("chapter-end", StoryRef.Body, last.Paragraph, last.Length, last.Index));
            }
        }

        foreach (var i in Enumerable.Range(0, blocks.Count).Where(i => blocks[i] is Table).OrderBy(i => Math.Abs(i - middleIndex)))
        {
            var cell = FirstRowParagraphs((Table)blocks[i]).ToList();
            var chosen = cell.FirstOrDefault(p => Length(StoryRef.Body, p) > 0) ?? cell.FirstOrDefault();
            if (chosen is not null)
            {
                found.Add(new TypingPlace("table-cell", StoryRef.Body, chosen, Length(StoryRef.Body, chosen) / 2, i));
                break;
            }
        }

        var citations = new List<(int Index, int Id)>();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph p)
            {
                citations.AddRange(FootnoteIds(p.Inlines).Select(id => (i, id)));
            }
        }

        foreach (var (citedAt, id) in citations.OrderBy(c => Math.Abs(c.Index - middleIndex)))
        {
            var story = new StoryRef(StoryKind.Note, id.ToString(CultureInfo.InvariantCulture));
            var paragraph = document.Footnotes?.Find(id)?.Content.Blocks.OfType<Paragraph>().FirstOrDefault(p => Length(story, p) > 0);
            if (paragraph is not null)
            {
                found.Add(new TypingPlace("footnote", story, paragraph, Length(story, paragraph), citedAt));
                break;
            }
        }

        foreach (var key in document.HeadersFooters.Keys.Order(StringComparer.Ordinal))
        {
            var part = document.HeadersFooters[key];
            if (part.Kind != HeaderFooterKind.Header)
            {
                continue;
            }

            var story = new StoryRef(StoryKind.HeaderFooter, key);
            var paragraph = part.Content.Blocks.OfType<Paragraph>().FirstOrDefault(p => Length(story, p) > 0);
            if (paragraph is not null)
            {
                found.Add(new TypingPlace("header", story, paragraph, Length(story, paragraph), middleIndex));
                break;
            }
        }

        return [.. Names.SelectMany(name => found.Where(f => f.Name == name))];
    }

    private static IEnumerable<Paragraph> FirstRowParagraphs(Table table)
    {
        foreach (var row in table.Rows)
        {
            if (row is not TableRow tableRow)
            {
                continue;
            }

            foreach (var item in tableRow.Items)
            {
                if (item is TableCell cell)
                {
                    foreach (var block in cell.Blocks)
                    {
                        if (block is Paragraph paragraph)
                        {
                            yield return paragraph;
                        }
                    }
                }
            }

            yield break;
        }
    }

    private static IEnumerable<int> FootnoteIds(IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    foreach (var content in run.Content)
                    {
                        if (content is FootnoteReference reference)
                        {
                            yield return reference.Id;
                        }
                    }

                    break;
                case Hyperlink link:
                    foreach (var id in FootnoteIds(link.Inlines))
                    {
                        yield return id;
                    }

                    break;
            }
        }
    }
}
