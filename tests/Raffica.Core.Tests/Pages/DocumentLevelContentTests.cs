using System.Text;
using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Core.Tests.Annotations;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Core.Tests.Pages;

/// <summary>
/// <see cref="DocumentLevelContent"/>: what every part of an extract or split keeps because it is a working copy of the document
/// minus the pages that are not wanted (attached files, scripts, the values of fields and the bookmarks of the other pages).
/// </summary>
public sealed class DocumentLevelContentTests
{
    private readonly PdfEngine engine = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // redact-everywhere-nopattern.pdf has an attachment in the names tree, a text field and a bookmark on page 1 and nothing on page 2.
    // A document script is added to its catalog (the offsets of the cross-reference table move, which PDFium repairs on opening).
    private static string WithScript(TempFolder folder)
    {
        var text = Encoding.Latin1.GetString(File.ReadAllBytes(TestData.Pdf("redact-everywhere-nopattern.pdf")));
        const string key = "/PageMode /UseOutlines";
        Assert.Contains(key, text, StringComparison.Ordinal);
        var path = Path.Combine(folder.Path, "with-script.pdf");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(text.Replace(key, key + " /OpenAction << /S /JavaScript /JS (var x = 1;) >>", StringComparison.Ordinal)));
        return path;
    }

    private async Task<DocumentLevelContent> ReadAsync(string path, params int[][] parts)
    {
        await using var document = await engine.OpenAsync(path, cancellationToken: Ct);
        var list = parts.Select((pages, i) => new SplitPart("part" + i, pages)).ToList();
        return await DocumentLevelContent.ReadAsync(document, path, list, Ct);
    }

    [Fact]
    public async Task ExtractingTheOtherPageKeepsEverythingOfTheDocumentLevel()
    {
        using var folder = new TempFolder();

        var content = await ReadAsync(WithScript(folder), [1]);

        Assert.Equal(new DocumentLevelContent(1, true, 1, 1), content);
        Assert.False(content.IsEmpty);
    }

    [Fact]
    public async Task APartThatHoldsTheFieldAndTheBookmarkKeepsNoneOfThemFromOtherPages()
    {
        var content = await ReadAsync(TestData.Pdf("redact-everywhere-nopattern.pdf"), [0]);

        Assert.Equal(new DocumentLevelContent(1, false, 0, 0), content);
    }

    [Fact]
    public async Task ASplitReportsTheWorstPart()
    {
        // Part 1 holds the field and the bookmark, part 2 does not: the second one keeps both as entries of other pages.
        var content = await ReadAsync(TestData.Pdf("redact-everywhere-nopattern.pdf"), [0], [1]);

        Assert.Equal(new DocumentLevelContent(1, false, 1, 1), content);
    }

    [Fact]
    public async Task ADocumentWithoutAnyOfItReportsNothing()
    {
        var content = await ReadAsync(TestData.Pdf("hello_world.pdf"), [0]);

        Assert.True(content.IsEmpty);
        Assert.Equal(DocumentLevelContent.None, content);
    }
}
