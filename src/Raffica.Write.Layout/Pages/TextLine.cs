using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>
/// One laid-out line of a paragraph, as the page keeps it next to its items: the link from what is drawn back to the model, for hit
/// testing and caret geometry. Records are in reading order on <see cref="LaidOutPage.TextLines"/> (header, body, notes, footer, each in
/// the order the page was filled).
/// <para>
/// <see cref="Start"/> and <see cref="End"/> are offsets into <see cref="Projection"/>'s text (model coordinates), half open. The layout's
/// own flat text differs from the projection in a few places (a float that has no room in the writing view, a table cell or a text box is
/// drawn as a leading box that the projection does not have; a float the page places has no layout character but a projection character;
/// a hidden break or box has a layout character but no projection character); <see cref="OffsetMap"/> translates, so these ranges are
/// always projection offsets. Of one paragraph the ranges are in order and contiguous: the first line starts at 0 and the last ends at the
/// length of the projection text (characters the layout gives no room at either end belong to the first or last line), a line that ends
/// in a manual page or column break ends where the continuation starts. The range includes trailing spaces and the break character that
/// ended the line; the hyphen of automatic hyphenation has an empty range and is part of <see cref="Runs"/> only.
/// </para>
/// <para>
/// <see cref="X"/> is the page x of the line's origin, the left edge of its text area: the runs' <see cref="GlyphRun.X"/> and
/// <see cref="GlyphRun.CaretX"/> are relative to it. <see cref="Top"/> and <see cref="Baseline"/> are page y values and
/// <see cref="Height"/> is the height of the line.
/// </para>
/// <para>
/// <see cref="Map"/> translates the layout offsets that <see cref="GlyphRun.CaretStops"/> and <see cref="GlyphRun.CaretX"/> count in into
/// projection offsets (after a break it is the map of the continuation, as the runs of its lines are). <see cref="Cell"/> is the identity
/// of the table cell the paragraph is in (null outside a table; compare by reference). <see cref="ContentLeft"/> and
/// <see cref="ContentRight"/> are the left and right edge of the line's content relative to <see cref="X"/> (inline pictures included,
/// trailing spaces not); the caret geometry uses them where a line has no glyph run at an edge.
/// </para>
/// </summary>
internal sealed record TextLine(StoryRef Story, Paragraph Paragraph, ParagraphProjection Projection, GlyphRun[] Runs, int Start, int End, float X, float Top, float Baseline, float Height, OffsetMap Map, object? Cell, float ContentLeft, float ContentRight, IProjectionHost? Host = null)
{
    /// <summary>The caret stops of the line, made on first use (see <see cref="LineStops"/>).</summary>
    internal LineStops Stops => _stops ??= LineStops.Build(this);

    // Built from immutable data, so two threads that build it at once make equal objects and either may win.
    private LineStops? _stops;
}

/// <summary>Where the lines of a <see cref="ParagraphInput"/> come from: the story and the paragraph of the model (a text box paragraph has none), and the cell format object that stands for the table cell the paragraph is in (null outside a table).</summary>
internal sealed record ParagraphSource(StoryRef Story, Paragraph Paragraph, object? Cell = null);

/// <summary>
/// Translates offsets of a paragraph's flat layout text into offsets of its <see cref="ParagraphProjection"/>. Piecewise: a linear
/// stretch advances one for one; a flat stretch (layout characters that have no projection counterpart) maps to one projection offset.
/// </summary>
internal sealed class OffsetMap
{
    /// <summary>Layout and projection agree everywhere.</summary>
    public static OffsetMap Identity { get; } = new([0], [0], [false]);

    private readonly int[] _layout;
    private readonly int[] _projection;
    private readonly bool[] _flat;

    private OffsetMap(int[] layout, int[] projection, bool[] flat)
    {
        _layout = layout;
        _projection = projection;
        _flat = flat;
    }

    /// <summary>The projection offset of a layout offset.</summary>
    public int Map(int layoutOffset)
    {
        var i = Array.BinarySearch(_layout, layoutOffset);
        if (i < 0)
        {
            i = Math.Max(0, ~i - 1);
        }

        return _flat[i] ? _projection[i] : _projection[i] + layoutOffset - _layout[i];
    }

    /// <summary>The map of the text that starts at layout offset <paramref name="layoutStart"/> (the continuation after a break): its offset 0 is that one.</summary>
    public OffsetMap Slice(int layoutStart)
    {
        var i = Array.BinarySearch(_layout, layoutStart);
        if (i < 0)
        {
            i = Math.Max(0, ~i - 1);
        }

        var layout = new List<int>(_layout.Length - i) { 0 };
        var projection = new List<int>(_layout.Length - i) { Map(layoutStart) };
        var flat = new List<bool>(_layout.Length - i) { _flat[i] };
        for (var k = i + 1; k < _layout.Length; k++)
        {
            layout.Add(_layout[k] - layoutStart);
            projection.Add(_projection[k]);
            flat.Add(_flat[k]);
        }

        return new OffsetMap([.. layout], [.. projection], [.. flat]);
    }

    /// <summary>Builds the map piece by piece, in the order of the projection.</summary>
    public sealed class Builder
    {
        // Allocated on the first step that is not the identity: until then the state is the single entry (0, 0, linear).
        private List<int>? _layout;
        private List<int>? _projection;
        private List<bool>? _flat;
        private int _at;

        /// <summary>The layout length that has been described so far.</summary>
        public int LayoutLength => _at;

        /// <summary>Adds the next piece: <paramref name="projectionLength"/> characters of the projection from <paramref name="projectionStart"/> that took <paramref name="layoutLength"/> characters of layout text.</summary>
        public void Add(int projectionStart, int projectionLength, int layoutLength)
        {
            if (projectionLength == layoutLength)
            {
                Linear(_at, projectionStart);
            }
            else if (layoutLength > 0)
            {
                Put(_at, projectionStart, true);
            }

            _at += layoutLength;
            if (projectionLength != layoutLength)
            {
                Linear(_at, projectionStart + projectionLength);
            }
        }

        /// <summary>Puts <paramref name="count"/> layout characters in front that the projection does not have: everything that was added moves right.</summary>
        public void Lead(int count)
        {
            if (count <= 0)
            {
                return;
            }

            Allocate();
            for (var i = 0; i < _layout!.Count; i++)
            {
                _layout[i] += count;
            }

            _layout.Insert(0, 0);
            _projection!.Insert(0, 0);
            _flat!.Insert(0, true);
            _at += count;
        }

        public OffsetMap Build() =>
            _layout is null || (_layout.Count == 1 && _layout[0] == 0 && _projection![0] == 0 && !_flat![0]) ? Identity : new OffsetMap([.. _layout], [.. _projection!], [.. _flat!]);

        private void Allocate()
        {
            _layout ??= [0];
            _projection ??= [0];
            _flat ??= [false];
        }

        private void Linear(int layout, int projection)
        {
            if (_layout is null)
            {
                if (projection == layout)
                {
                    return;
                }
            }
            else
            {
                var at = _layout.Count - 1;
                if (!_flat![at] && _projection![at] - _layout[at] == projection - layout)
                {
                    return;
                }
            }

            Put(layout, projection, false);
        }

        private void Put(int layout, int projection, bool flat)
        {
            Allocate();
            var last = _layout!.Count - 1;
            if (_layout[last] == layout)
            {
                _projection![last] = projection;
                _flat![last] = flat;
                return;
            }

            _layout.Add(layout);
            _projection!.Add(projection);
            _flat!.Add(flat);
        }
    }
}
