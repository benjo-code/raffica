using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// Makes the XML of a new picture: a <c>w:drawing</c> with a <c>wp:inline</c> as Word writes one (locked aspect ratio, a rectangle frame, no effects, no extension list). It produces model XML only (no package type): the
/// relationship id names a picture of the <see cref="MediaStore"/>. The picture is named <c>Picture N</c> after its id, never after a file; the alt text goes in through the XML API, so it needs no escaping by hand.
/// </summary>
internal static class DrawingBuilder
{
    internal static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    internal static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    internal static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    internal static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    internal static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>A <c>w:drawing</c> holding an inline picture of <paramref name="width"/> by <paramref name="height"/> EMU that shows the picture <paramref name="relationshipId"/>, with the unique <c>wp:docPr</c> id <paramref name="docPrId"/>.</summary>
    public static XElement Inline(string relationshipId, long width, long height, long docPrId, string? altText)
    {
        ArgumentException.ThrowIfNullOrEmpty(relationshipId);
        var id = docPrId.ToString(CultureInfo.InvariantCulture);
        var name = "Picture " + id;
        var cx = width.ToString(CultureInfo.InvariantCulture);
        var cy = height.ToString(CultureInfo.InvariantCulture);
        var docPr = new XElement(Wp + "docPr", new XAttribute("id", id), new XAttribute("name", name));
        var cNvPr = new XElement(Pic + "cNvPr", new XAttribute("id", "0"), new XAttribute("name", name));
        if (!string.IsNullOrEmpty(altText))
        {
            docPr.SetAttributeValue("descr", altText);
            cNvPr.SetAttributeValue("descr", altText);
        }

        return new XElement(
            W + "drawing",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "pic", Pic.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
            new XElement(
                Wp + "inline",
                new XAttribute("distT", "0"),
                new XAttribute("distB", "0"),
                new XAttribute("distL", "0"),
                new XAttribute("distR", "0"),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent", new XAttribute("l", "0"), new XAttribute("t", "0"), new XAttribute("r", "0"), new XAttribute("b", "0")),
                docPr,
                new XElement(Wp + "cNvGraphicFramePr", new XElement(A + "graphicFrameLocks", new XAttribute("noChangeAspect", "1"))),
                new XElement(
                    A + "graphic",
                    new XElement(
                        A + "graphicData",
                        new XAttribute("uri", Pic.NamespaceName),
                        new XElement(
                            Pic + "pic",
                            new XElement(Pic + "nvPicPr", cNvPr, new XElement(Pic + "cNvPicPr")),
                            new XElement(Pic + "blipFill", new XElement(A + "blip", new XAttribute(R + "embed", relationshipId)), new XElement(A + "stretch", new XElement(A + "fillRect"))),
                            new XElement(
                                Pic + "spPr",
                                new XElement(A + "xfrm", new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")), new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                                new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst"))))))));
    }
}
