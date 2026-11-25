using System.Globalization;
using System.Xml.Linq;

namespace Raffica.Write.Model;

/// <summary>How the text treats a picture: it sits in the line (<see cref="Inline"/>) or floats and the text goes around it, above and below it, or past it (in front of the text or behind it).</summary>
public enum PictureWrap
{
    /// <summary>A <c>wp:inline</c> drawing: one character of the line.</summary>
    Inline,

    /// <summary>Floating, the text runs around the rectangle (<c>wp:wrapSquare</c>; tight and through are read as this).</summary>
    Square,

    /// <summary>Floating, no text beside it (<c>wp:wrapTopAndBottom</c>).</summary>
    TopAndBottom,

    /// <summary>Floating in front of the text (<c>wp:wrapNone</c>, not behind the document).</summary>
    InFront,

    /// <summary>Floating behind the text (<c>wp:wrapNone</c> with <c>behindDoc</c>).</summary>
    Behind,
}

/// <summary>The crop of a picture (<c>a:srcRect</c>) in thousandths of a percent of the picture (100000 is all of it), each side from the edge.</summary>
public readonly record struct PictureCrop(int Left, int Top, int Right, int Bottom)
{
    /// <summary>True when no side is cropped.</summary>
    public bool IsNone => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
}

/// <summary>The size of a picture's frame in EMU (914400 to the inch, 12700 to the point).</summary>
public readonly record struct EmuSize(long Width, long Height);

/// <summary>Where a floating picture is placed: what each axis is measured from, and the offset in EMU (or null for an alignment keyword, which the offset then does not hold).</summary>
public sealed record PictureAnchorInfo(string HorizontalFrom, long? HorizontalOffsetEmu, string VerticalFrom, long? VerticalOffsetEmu, long RelativeHeight, bool BehindDocument);

/// <summary>What the model reads from the XML of a <see cref="Picture"/> that is a plain picture (<c>pic:pic</c>); every member is read from the XML on the call, so none can disagree with what is written.</summary>
public sealed record PictureInfo(EmuSize ExtentEmu, long Rotation, PictureCrop? Crop, PictureWrap Wrap, PictureAnchorInfo? Anchor, string? AltText, string? Media)
{
    /// <summary>True for a picture turned by any angle (the frame's <c>a:xfrm</c> has a non-zero <c>rot</c>): its size is not edited.</summary>
    public bool IsRotated => Rotation != 0;

    /// <summary>True when the picture floats.</summary>
    public bool IsFloating => Wrap != PictureWrap.Inline;
}

/// <summary>Typed reading of the XML of a <see cref="Picture"/>, and the one definition of which drawings are pictures: a <c>w:drawing</c> with a <c>wp:inline</c> or <c>wp:anchor</c> whose graphic holds a <c>pic:pic</c>. Never throws on content.</summary>
public static class PictureView
{
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>The picture at <paramref name="content"/> of the run, or null when it is out of range, no drawing, or no plain picture (a text box, a shape, a chart, a group).</summary>
    public static PictureInfo? Of(Run run, int content)
    {
        ArgumentNullException.ThrowIfNull(run);
        return content >= 0 && content < run.Content.Count && run.Content[content] is Picture picture ? Of(picture) : null;
    }

    /// <summary>The picture of a drawing, or null when the drawing is not a plain picture.</summary>
    public static PictureInfo? Of(Picture picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        if (!IsPlain(picture))
        {
            return null;
        }

        var container = ContainerOf(picture)!;
        var pic = PicOf(container)!;
        var extent = container.Element(Wp + "extent");
        var size = new EmuSize(Long(extent, "cx"), Long(extent, "cy"));
        var spPr = pic.Element(Pic + "spPr");
        var rotation = Long(spPr?.Element(A + "xfrm"), "rot");
        var crop = Crop(pic.Element(Pic + "blipFill")?.Element(A + "srcRect"));
        var blip = pic.Element(Pic + "blipFill")?.Element(A + "blip");
        return new PictureInfo(size, rotation, crop, WrapOf(container), AnchorOf(container), (string?)container.Element(Wp + "docPr")?.Attribute("descr"), (string?)blip?.Attribute(R + "embed"));
    }

    /// <summary>True when the drawing is a plain picture: an inline or anchored drawing whose graphic holds a <c>pic:pic</c> with a frame.</summary>
    public static bool IsPlain(Picture picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        return ContainerOf(picture) is { } container && container.Element(Wp + "extent") is not null && PicOf(container) is not null;
    }

    /// <summary>The <c>wp:inline</c> or <c>wp:anchor</c> of a drawing, or null.</summary>
    public static XElement? ContainerOf(Picture picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        return picture.Raw.Element(Wp + "inline") ?? picture.Raw.Element(Wp + "anchor");
    }

    /// <summary>The <c>pic:pic</c> of a drawing container, or null.</summary>
    public static XElement? PicOf(XElement container)
    {
        ArgumentNullException.ThrowIfNull(container);
        var data = container.Element(A + "graphic")?.Element(A + "graphicData");
        return data?.Element(Pic + "pic");
    }

    private static PictureWrap WrapOf(XElement container)
    {
        if (container.Name == Wp + "inline")
        {
            return PictureWrap.Inline;
        }

        foreach (var child in container.Elements())
        {
            if (child.Name.Namespace != Wp)
            {
                continue;
            }

            switch (child.Name.LocalName)
            {
                case "wrapSquare" or "wrapTight" or "wrapThrough":
                    return PictureWrap.Square;
                case "wrapTopAndBottom":
                    return PictureWrap.TopAndBottom;
                case "wrapNone":
                    return IsTrue((string?)container.Attribute("behindDoc")) ? PictureWrap.Behind : PictureWrap.InFront;
                default:
                    break;
            }
        }

        return IsTrue((string?)container.Attribute("behindDoc")) ? PictureWrap.Behind : PictureWrap.InFront;
    }

    private static PictureAnchorInfo? AnchorOf(XElement container)
    {
        if (container.Name != Wp + "anchor")
        {
            return null;
        }

        var h = container.Element(Wp + "positionH");
        var v = container.Element(Wp + "positionV");
        return new PictureAnchorInfo(
            (string?)h?.Attribute("relativeFrom") ?? "column",
            Offset(h),
            (string?)v?.Attribute("relativeFrom") ?? "paragraph",
            Offset(v),
            Long(container, "relativeHeight"),
            IsTrue((string?)container.Attribute("behindDoc")));
    }

    private static long? Offset(XElement? position) =>
        position?.Element(Wp + "posOffset") is { } offset && long.TryParse(offset.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static PictureCrop? Crop(XElement? rect)
    {
        if (rect is null)
        {
            return null;
        }

        var crop = new PictureCrop(Side(rect, "l"), Side(rect, "t"), Side(rect, "r"), Side(rect, "b"));
        return crop.IsNone ? null : crop;
    }

    private static int Side(XElement rect, string name) =>
        int.TryParse((string?)rect.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static long Long(XElement? element, string attribute) =>
        element is not null && long.TryParse((string?)element.Attribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static bool IsTrue(string? value) => value is "1" or "true" or "on";
}
