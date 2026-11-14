using Raffica.Write.Docx;
using Raffica.Write.Layout;

namespace WriteCorpus;

/// <summary>Write's own side of the page comparison: the pages Raffica Write's layout makes of a DOCX file.</summary>
internal static class WriteLayoutFacts
{
    /// <summary>
    /// Opens the file whole, lays it out with the default <see cref="LayoutOptions"/> (pages view, no picture bytes: every picture counts as
    /// drawable, which changes no text), waits for the layout to be complete (so the provisional NUMPAGES pages are final) and returns the page
    /// count and the text of each page from <see cref="LaidOutPage.Text"/>, normalised by <see cref="TextNormalizer"/>. A hyphen glyph of a
    /// hyphenated line end is not in <see cref="LaidOutPage.Text"/>.
    /// </summary>
    /// <param name="fonts">A catalog to reuse when many files are laid out in a row; null makes one for this file.</param>
    /// <exception cref="DocxFormatException">The file is not a readable DOCX.</exception>
    public static PageFacts Load(string docxPath, FontCatalog? fonts = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(docxPath);
        using var docx = DocxFile.Open(docxPath);
        var layout = DocumentLayout.Create(docx.Model, fonts ?? FontCatalog.CreateShipped(), new LayoutOptions());
        layout.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        if (!layout.IsComplete)
        {
            throw new InvalidOperationException("The layout did not complete.");
        }

        var texts = new List<string>(layout.LaidOutPages);
        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            texts.Add(TextNormalizer.Normalize(layout.Page(i).Text));
        }

        return new PageFacts(texts.Count, texts);
    }
}
