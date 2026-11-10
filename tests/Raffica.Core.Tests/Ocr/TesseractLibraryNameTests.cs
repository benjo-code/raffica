using Raffica.Ocr;
using Xunit;

namespace Raffica.Core.Tests.Ocr;

public sealed class TesseractLibraryNameTests
{
    private static readonly string[] AllNames = ["tesseract.dll", "libtesseract.dylib", "libtesseract.so"];

    [Fact]
    public void WindowsName() => Assert.Equal("tesseract.dll", TesseractLibrary.FileNameFor(OperatingSystemKind.Windows));

    [Fact]
    public void MacOsName() => Assert.Equal("libtesseract.dylib", TesseractLibrary.FileNameFor(OperatingSystemKind.MacOS));

    [Fact]
    public void LinuxName() => Assert.Equal("libtesseract.so", TesseractLibrary.FileNameFor(OperatingSystemKind.Linux));

    [Fact]
    public void CurrentSystemNameIsOneOfThem() =>
        Assert.Contains(TesseractLibrary.FileName, AllNames);
}
