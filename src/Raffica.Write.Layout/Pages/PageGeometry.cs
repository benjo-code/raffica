using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>The columns of a section: where each one starts and how wide it is, in points from the left edge of the page.</summary>
internal sealed class ColumnLayout
{
    public ColumnLayout(float[] lefts, float[] widths, bool separator)
    {
        Lefts = lefts;
        Widths = widths;
        Separator = separator;
    }

    public float[] Lefts { get; }

    public float[] Widths { get; }

    public bool Separator { get; }

    public int Count => Lefts.Length;
}

/// <summary>
/// Page size, margins and columns of one section, in points, after repair: whatever the file says, the text area is at least
/// <see cref="MinimumText"/> points in both directions and no value is NaN, zero or negative where that makes no sense.
/// </summary>
internal sealed class PageGeometry
{
    /// <summary>The smallest text area kept when a page size or its margins are absurd.</summary>
    public const float MinimumText = 36f;

    private const float MinimumPage = 72f;
    private const float MaximumPage = 1584f; // 22 inches, Word's own limit
    private const float MinimumColumn = 12f;
    private const int MaximumColumns = 45;
    private const float DefaultWidth = 595.3f; // A4 when a section has no w:pgSz
    private const float DefaultHeight = 841.9f;
    private const float DefaultMargin = 72f;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private PageGeometry(float width, float height, float left, float top, float right, float bottom, float header, float footer, ColumnLayout columns, bool isStrip)
    {
        Width = width;
        Height = height;
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
        HeaderDistance = header;
        FooterDistance = footer;
        Columns = columns;
        IsStrip = isStrip;
    }

    public float Width { get; }

    public float Height { get; }

    /// <summary>The margins (the gutter is part of <see cref="Left"/>).</summary>
    public float Left { get; }

    public float Top { get; }

    public float Right { get; }

    public float Bottom { get; }

    /// <summary>Distance from the top edge to the header, and from the bottom edge to the footer.</summary>
    public float HeaderDistance { get; }

    public float FooterDistance { get; }

    public ColumnLayout Columns { get; }

    public bool IsStrip { get; }

    public float TextWidth => Width - Left - Right;

    /// <summary>The y where the body text starts (the top margin).</summary>
    public float BodyTop => Top;

    /// <summary>The y where the body text must end (page height minus the bottom margin).</summary>
    public float BodyBottom => Height - Bottom;

    public bool SamePaper(PageGeometry other) => MathF.Abs(Width - other.Width) < 0.5f && MathF.Abs(Height - other.Height) < 0.5f;

    /// <summary>One column of the given width and no margins; the writing view cuts the text into strips of up to <paramref name="maxHeight"/>.</summary>
    public static PageGeometry ForStrips(float textWidth, float maxHeight) =>
        new(textWidth, maxHeight, 0f, 0f, 0f, 0f, 0f, 0f, new ColumnLayout([0f], [textWidth], false), isStrip: true);

    /// <summary>Reads the geometry from a <c>w:sectPr</c> (null: Word's defaults); repairs are appended to <paramref name="notes"/>.</summary>
    public static PageGeometry Read(SectionProperties? section, string where, List<string> notes)
    {
        var size = section?.Get(W + "pgSz");
        var width = Points(size, "w") ?? DefaultWidth;
        var height = Points(size, "h") ?? DefaultHeight;
        var sizeRepaired = false;
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0f || height <= 0f)
        {
            notes.Add($"{where}: the page size ({Format(width)} x {Format(height)} pt) is not usable; A4 is used instead.");
            width = DefaultWidth;
            height = DefaultHeight;
            sizeRepaired = true;
        }

        if (!sizeRepaired && (width is < MinimumPage or > MaximumPage || height is < MinimumPage or > MaximumPage))
        {
            var cw = Math.Clamp(width, MinimumPage, MaximumPage);
            var ch = Math.Clamp(height, MinimumPage, MaximumPage);
            notes.Add($"{where}: the page size ({Format(width)} x {Format(height)} pt) is outside the range Word allows; {Format(cw)} x {Format(ch)} pt is used.");
            width = cw;
            height = ch;
        }

        var margins = section?.Get(W + "pgMar");
        var left = Math.Abs(Points(margins, "left") ?? DefaultMargin);
        var right = Math.Abs(Points(margins, "right") ?? DefaultMargin);
        var top = Math.Abs(Points(margins, "top") ?? DefaultMargin);
        var bottom = Math.Abs(Points(margins, "bottom") ?? DefaultMargin);
        var gutter = Math.Abs(Points(margins, "gutter") ?? 0f);
        var header = Math.Abs(Points(margins, "header") ?? 36f);
        var footer = Math.Abs(Points(margins, "footer") ?? 36f);
        left += gutter;

        var horizontal = left + right;
        if (width - horizontal < MinimumText)
        {
            var keep = Math.Max(0f, width - MinimumText);
            var factor = horizontal > 0f ? keep / horizontal : 0f;
            notes.Add($"{where}: the left and right margins ({Format(horizontal)} pt) leave less than {Format(MinimumText)} pt of text width on a {Format(width)} pt page; they are reduced.");
            left *= factor;
            right *= factor;
        }

        var vertical = top + bottom;
        if (height - vertical < MinimumText)
        {
            var keep = Math.Max(0f, height - MinimumText);
            var factor = vertical > 0f ? keep / vertical : 0f;
            notes.Add($"{where}: the top and bottom margins ({Format(vertical)} pt) leave less than {Format(MinimumText)} pt of text height on a {Format(height)} pt page; they are reduced.");
            top *= factor;
            bottom *= factor;
        }

        var columns = ReadColumns(section?.Get(W + "cols"), left, width - left - right, where, notes);
        return new PageGeometry(width, height, left, top, right, bottom, header, footer, columns, isStrip: false);
    }

    private static ColumnLayout ReadColumns(XElement? cols, float left, float textWidth, string where, List<string> notes)
    {
        if (cols is null)
        {
            return new ColumnLayout([left], [textWidth], false);
        }

        var separator = Flag(cols, "sep");
        var space = Points(cols, "space") ?? 36f;
        var explicitColumns = cols.Elements(W + "col").ToList();
        var equal = Flag(cols, "equalWidth", defaultValue: true);
        if (!equal && explicitColumns.Count > 0)
        {
            var count = Math.Min(explicitColumns.Count, MaximumColumns);
            var widths = new float[count];
            var gaps = new float[count];
            for (var i = 0; i < count; i++)
            {
                widths[i] = Math.Max(MinimumColumn, Points(explicitColumns[i], "w") ?? MinimumColumn);
                gaps[i] = Math.Max(0f, Points(explicitColumns[i], "space") ?? space);
            }

            var total = widths.Sum() + gaps.Take(count - 1).Sum();
            var scale = total > textWidth ? textWidth / total : 1f;
            if (scale < 1f)
            {
                notes.Add($"{where}: the columns are wider than the text area; they are scaled down to fit.");
            }

            var lefts = new float[count];
            var x = left;
            for (var i = 0; i < count; i++)
            {
                widths[i] *= scale;
                lefts[i] = x;
                x += widths[i] + (gaps[i] * scale);
            }

            return new ColumnLayout(lefts, widths, separator && count > 1);
        }

        var number = int.TryParse((string?)cols.Attribute(W + "num"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 1;
        number = Math.Clamp(number, 1, MaximumColumns);
        space = Math.Max(0f, space);
        var reduced = number;
        while (reduced > 1 && (textWidth - ((reduced - 1) * space)) / reduced < MinimumColumn)
        {
            reduced--;
        }

        if (reduced != number)
        {
            notes.Add($"{where}: {number} columns do not fit the text area; {reduced} are used.");
        }

        number = reduced;
        var columnWidth = (textWidth - ((number - 1) * space)) / number;
        var positions = new float[number];
        var sizes = new float[number];
        for (var i = 0; i < number; i++)
        {
            positions[i] = left + (i * (columnWidth + space));
            sizes[i] = columnWidth;
        }

        return new ColumnLayout(positions, sizes, separator && number > 1);
    }

    private static bool Flag(XElement element, string name, bool defaultValue = false)
    {
        var value = (string?)element.Attribute(W + name);
        return value is null ? defaultValue : value is not ("0" or "false" or "off");
    }

    private static float? Points(XElement? element, string name)
    {
        var text = (string?)element?.Attribute(W + name);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var twips) ? (float)(twips / 20.0) : null;
    }

    private static string Format(float value) => float.IsFinite(value) ? value.ToString("0.#", CultureInfo.InvariantCulture) : "NaN";
}
