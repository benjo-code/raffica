using Raffica.Write.Docx;
using Raffica.Write.Layout;
using Xunit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Layout;

public sealed class BlankDocumentLayoutTests
{
    [Fact]
    public async Task ABlankDocumentLaysOutAsOneEmptyPage()
    {
        using var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var layout = DocumentLayout.Create(file.Model, Fonts, new LayoutOptions());
        await layout.LayOutRestAsync(null, TestContext.Current.CancellationToken);
        Assert.True(layout.IsComplete);
        Assert.Equal(1, layout.LaidOutPages);
    }
}
