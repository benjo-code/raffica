using System.Diagnostics.CodeAnalysis;
using Raffica.Core.Text;
using Raffica.Pdf.Documents;

namespace Raffica.Pdf.Text;

/// <summary>Reads the text of a page that Core already loaded, without waiting and without a failure: the text that is not there is simply not there.</summary>
internal static class PageTexts
{
    /// <summary>The text source of the tab, or null when the document is not open (or already closed).</summary>
    public static PageTextSource? SourceOf(PdfTab tab)
    {
        try
        {
            return tab.Session.EnsurePageTexts();
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>The text of a page when it is in the cache of the text source.</summary>
    public static bool TryGet(PdfTab tab, int page, [NotNullWhen(true)] out PageText? text)
    {
        text = null;
        try
        {
            return SourceOf(tab) is { } source && source.TryGet(page, out text);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }
}
