namespace Raffica.Write.Layout;

/// <summary>A floating object at its place on a page: its rectangle in points from the top left of the page, and what it draws.</summary>
internal sealed class FloatBox
{
    public FloatBox(FloatSpec spec, float x, float y, float width, float height, IReadOnlyList<(IBlockFlow Flow, float Y)>? lines, float textLeft)
    {
        Spec = spec;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Lines = lines;
        TextLeft = textLeft;
    }

    public FloatSpec Spec { get; }

    public float X { get; }

    public float Y { get; }

    public float Width { get; }

    public float Height { get; }

    /// <summary>The laid-out paragraphs of a text box with their y offsets from the top of the box; null for the other kinds.</summary>
    public IReadOnlyList<(IBlockFlow Flow, float Y)>? Lines { get; }

    /// <summary>The x of the text inside a text box.</summary>
    public float TextLeft { get; }

    /// <summary>The top of the room the text keeps clear (the rectangle with its distance).</summary>
    public float ClearTop => Y - Spec.DistanceTop;

    public float ClearBottom => Y + Height + Spec.DistanceBottom;

    public float ClearLeft => X - Spec.DistanceLeft;

    public float ClearRight => X + Width + Spec.DistanceRight;

    /// <summary>True when text has to keep out of this object.</summary>
    public bool Wraps => Spec.Wrap != FloatWrap.None;

    /// <summary>Adds the items of the object. A picture behind the text goes to the region painted before the body.</summary>
    public void Emit(PageBuilder page)
    {
        ArgumentNullException.ThrowIfNull(page);
        using (Spec.Behind ? page.PaintBehind() : null)
        {
            switch (Spec.Kind)
            {
                case FloatKind.Image when Spec.Image is { } image:
                    page.Add(new ImageItem(image, X, Y, Width, Height));
                    page.AddPicture(Spec.Anchor, X, Y, Width, Height, Spec.Behind);
                    break;
                case FloatKind.TextBox:
                    EmitTextBox(page);
                    break;
                default:
                    page.Add(new PlaceholderItem(X, Y, Width, Height));
                    break;
            }
        }
    }

    private void EmitTextBox(PageBuilder page)
    {
        if (Spec.Look is { } look)
        {
            if (look.Fill is { } fill)
            {
                page.Add(new FillItem(X, Y, Width, Height, fill));
            }

            if (look.Line is { } line)
            {
                var t = look.LineWidth;
                page.Add(new RuleItem(X, Y, Width, t, line));
                page.Add(new RuleItem(X, Y + Height - t, Width, t, line));
                page.Add(new RuleItem(X, Y + t, t, Math.Max(0f, Height - (2f * t)), line));
                page.Add(new RuleItem(X + Width - t, Y + t, t, Math.Max(0f, Height - (2f * t)), line));
            }
        }

        if (Lines is null)
        {
            return;
        }

        foreach (var (flow, y) in Lines)
        {
            flow.Emit(page, TextLeft, Y + y);
        }
    }
}

/// <summary>The floating objects placed on one page so far: what the text of later paragraphs has to keep clear of.</summary>
internal sealed class PageFloats
{
    private readonly List<FloatBox> _boxes = [];

    public IReadOnlyList<FloatBox> Boxes => _boxes;

    public void Add(IEnumerable<FloatBox> boxes)
    {
        foreach (var box in boxes)
        {
            if (box.Wraps)
            {
                _boxes.Add(box);
            }
        }
    }

    /// <summary>True when an object that text wraps around reaches into the vertical range.</summary>
    public bool Reaches(float top, float bottom)
    {
        foreach (var box in _boxes)
        {
            if (box.ClearTop < bottom && box.ClearBottom > top)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The room of each line of a paragraph around the objects of a page: a square object takes the side of the line where there is
/// more room (or the side its wrapping asks for), a top-and-bottom object takes the whole line. It is built for one placement of a
/// paragraph (its top y and its column) and holds no state beyond that. Lines that start at or below <c>clip</c> (the bottom of the
/// room the paragraph is placed in) are never narrowed: they will be laid out on another page, where these objects are not.
/// </summary>
internal sealed class WrapSource : IWrapSource
{
    private const float UsableWidth = 20f;

    private readonly IReadOnlyList<FloatBox> _boxes;
    private readonly float _columnLeft;
    private readonly float _top;
    private readonly float _clip;

    public WrapSource(IReadOnlyList<FloatBox> boxes, float columnLeft, float paragraphTop, float clip)
    {
        _boxes = boxes;
        _columnLeft = columnLeft;
        _top = paragraphTop;
        _clip = clip;
    }

    public LineBounds BoundsAt(float y, float lineHeight, float left, float right)
    {
        var top = _top + y;
        var bottom = top + Math.Max(0f, lineHeight);
        if (top >= _clip)
        {
            return new LineBounds(left, right);
        }

        foreach (var box in _boxes)
        {
            if (!box.Wraps || box.ClearBottom <= top || box.ClearTop >= bottom)
            {
                continue;
            }

            if (box.Spec.Wrap == FloatWrap.TopBottom)
            {
                return new LineBounds(right, right);
            }

            var boxLeft = box.ClearLeft - _columnLeft;
            var boxRight = box.ClearRight - _columnLeft;
            if (boxRight <= left || boxLeft >= right)
            {
                continue;
            }

            var leftRoom = Math.Max(0f, boxLeft - left);
            var rightRoom = Math.Max(0f, right - boxRight);
            var useLeft = box.Spec.Side switch
            {
                WrapSide.Left => true,
                WrapSide.Right => false,
                _ => leftRoom > rightRoom,
            };
            var room = useLeft ? leftRoom : rightRoom;
            if (room < Math.Min(UsableWidth, (right - left) / 2f))
            {
                // Too narrow for text: the line moves below the object.
                return new LineBounds(right, right);
            }

            if (useLeft)
            {
                right = Math.Min(right, boxLeft);
            }
            else
            {
                left = Math.Max(left, boxRight);
            }
        }

        return new LineBounds(left, right);
    }
}
