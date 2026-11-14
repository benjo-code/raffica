using System.Text;
using Raffica.Write.Docx.Xml;
using Raffica.Write.Model;

namespace Raffica.Write.Tests.Layout;

/// <summary>Small document models for the layout tests: raw WordprocessingML parts read through the real part readers.</summary>
internal static class LayoutModel
{
    public const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static MemoryStream Utf8(string xml) => new(Encoding.UTF8.GetBytes(xml));

    public static string StylesPart(string inner) => $"<w:styles xmlns:w=\"{W}\">{inner}</w:styles>";

    public static string NumberingPart(string inner) => $"<w:numbering xmlns:w=\"{W}\">{inner}</w:numbering>";

    public static WordDocument Document(string body, string? styles = null, string? numbering = null)
    {
        using var stream = Utf8($"<w:document xmlns:w=\"{W}\"><w:body>{body}</w:body></w:document>");
        var document = new WordDocument(DocumentXml.Read(stream));
        if (styles is not null)
        {
            using var s = Utf8(styles);
            document.Styles = StylesXml.Read(s);
        }

        if (numbering is not null)
        {
            using var n = Utf8(numbering);
            document.Numbering = NumberingXml.Read(n);
        }

        return document;
    }

    public static Paragraph Para(WordDocument document, int index) => (Paragraph)document.Body.Blocks[index];
}
