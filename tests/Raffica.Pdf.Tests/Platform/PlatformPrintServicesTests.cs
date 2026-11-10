using System.Runtime.Versioning;
using Raffica.Pdf.Platform;
using Raffica.Pdf.Platform.Linux;
using Xunit;

namespace Raffica.Pdf.Tests.Platform;

public sealed class PlatformPrintServicesTests
{
    [Fact]
    public void ChooseGivesCupsOnLinuxAndMacOs()
    {
        Assert.Equal(nameof(CupsPrintService), PlatformPrintServices.Choose(false, true, false).GetType().Name);
        Assert.Equal(nameof(CupsPrintService), PlatformPrintServices.Choose(false, false, true).GetType().Name);
        Assert.Equal("WindowsPrintService", PlatformPrintServices.Choose(true, false, false).GetType().Name);
        Assert.Equal(nameof(NoPrintService), PlatformPrintServices.Choose(false, false, false).GetType().Name);
    }

    [Fact]
    [SupportedOSPlatform("macos")]
    public async Task CupsLibraryLoadsOnMacOs()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only: libcups.2.dylib");

        Assert.True(CupsNative.IsAvailable);
        var printers = await new CupsPrintService().GetPrintersAsync(CancellationToken.None);
        Assert.NotNull(printers);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public void CupsLibraryNameIsPerSystem()
    {
        Assert.Equal("libcups.so.2", CupsNative.LibraryNameFor(true, false));
        Assert.Equal("libcups.2.dylib", CupsNative.LibraryNameFor(false, true));
        Assert.Null(CupsNative.LibraryNameFor(false, false));
    }
}
