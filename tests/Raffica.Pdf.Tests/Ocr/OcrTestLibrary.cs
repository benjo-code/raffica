using Raffica.Ocr;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Ocr;

/// <summary>
/// Where the PDF tests find the native OCR library and the language data. The library is built by <c>packaging/native/</c> (or fetched with
/// <c>tools/native-fetch.cs</c>) into <c>artifacts/native/&lt;rid&gt;/</c> with <c>tessdata/</c> beside it; <c>RAFFICA_TESSERACT_DIR</c> names another folder.
/// The program looks for the library next to itself only (the test project is built before the library is fetched in CI, so the build cannot copy it), so
/// <see cref="Require"/> copies the one file into the folder of the test program, once; the folder it came from is only read. The language data is
/// never copied: the window of a test is pointed at <see cref="Tessdata"/>. Without the library a test that needs it reports itself as skipped; with
/// <c>RAFFICA_REQUIRE_OCR=1</c> (the CI) a missing library is a failure.
/// </summary>
internal static class OcrTestLibrary
{
    private static readonly Lazy<string?> Folder = new(Find);
    private static readonly object Gate = new();

    /// <summary>Gets the folder with the library, or <see langword="null"/>.</summary>
    public static string? Directory => Folder.Value;

    /// <summary>Gets the folder with the language data.</summary>
    public static string Tessdata => Path.Combine(Directory ?? throw new InvalidOperationException("no OCR library"), "tessdata");

    /// <summary>Whether a test must fail instead of skipping when the library is missing.</summary>
    public static bool Required => Environment.GetEnvironmentVariable("RAFFICA_REQUIRE_OCR") == "1";

    /// <summary>Skips the test without the library (fails it with <c>RAFFICA_REQUIRE_OCR=1</c>); otherwise puts the library next to the test program.</summary>
    public static void Require()
    {
        if (Directory is null)
        {
            Assert.False(Required, "RAFFICA_REQUIRE_OCR=1, but no OCR library was found (artifacts/native or RAFFICA_TESSERACT_DIR)");
            Assert.Skip("The OCR library is not built (packaging/native/, tools/native-fetch.cs).");
        }

        lock (Gate)
        {
            var source = Path.Combine(Directory, TesseractLibrary.FileName);
            var target = Path.Combine(AppContext.BaseDirectory, TesseractLibrary.FileName);
            if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
            {
                File.Copy(source, target, overwrite: true);
            }
        }
    }

    private static string? Find()
    {
        var rid = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        var file = TesseractLibrary.FileName;
        var candidates = new[] { Environment.GetEnvironmentVariable("RAFFICA_TESSERACT_DIR"), Path.Combine(TestData.Root, "artifacts", "native", rid) };
        return candidates.FirstOrDefault(folder => !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, file)));
    }
}
