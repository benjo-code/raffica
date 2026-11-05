using Raffica.Core.Pdf;
using Raffica.Pdfium;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Core.Tests.Forms;

public sealed class MayFillFormsTests
{
    /// <summary>User password of the encrypted PDFium test files (see <c>testdata/pdfs/LICENSES.md</c>).</summary>
    private const string UserPassword = "hôtel";

    private readonly PdfEngine engine = new();

    [Fact]
    public async Task UnencryptedDocumentMayFill()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var document = await engine.OpenAsync(TestData.Pdf("hello_world.pdf"), cancellationToken: ct);

        Assert.True(await document.MayFillFormsAsync(ct));
    }

    [Fact]
    public async Task OwnerPasswordMayFill()
    {
        // The r3 file forbids annotating and filling to the user password (/P -3904); its owner password opens it with full rights.
        // The fixtures list gives no owner password for it, so the r6 file (/P -4, everything allowed) stands in for "may fill".
        var ct = TestContext.Current.CancellationToken;
        await using var document = await engine.OpenAsync(TestData.Pdf("encrypted_hello_world_r6.pdf"), UserPassword, ct);

        Assert.True(await document.MayFillFormsAsync(ct));
    }

    [Fact]
    public async Task AnnotateBitAlsoAllowsFilling()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var document = await engine.OpenAsync(TestData.Pdf("encrypted_hello_world_r6.pdf"), UserPassword, ct);

        Assert.True(await document.MayAnnotateAsync(ct));
        Assert.True(await document.MayFillFormsAsync(ct));
    }

    [Fact]
    public async Task NeitherBitForbidsFilling()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var document = await engine.OpenAsync(TestData.Pdf("encrypted_hello_world_r3.pdf"), UserPassword, ct);

        Assert.False(await document.MayAnnotateAsync(ct));
        Assert.False(await document.MayFillFormsAsync(ct));
    }

    // No fixture carries the fill-only permission (bit 9 without bit 6), so the bit logic is covered through the pure function the check calls.
    [Theory]
    [InlineData(0xFFFFFFFCu, true)]
    [InlineData(0u, false)]
    [InlineData(256u, true)]
    [InlineData(32u, true)]
    [InlineData(0xFFFFFEDFu, false)]
    [InlineData(4u, false)]
    public void FillingNeedsBitNineOrBitSix(uint permissions, bool expected)
    {
        Assert.Equal(expected, PdfiumDocument.AllowsFillingForms(permissions));
    }
}
