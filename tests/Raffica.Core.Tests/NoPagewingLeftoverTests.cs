using Raffica.TestSupport;
using Xunit;

namespace Raffica.Core.Tests;

/// <summary>
/// The code was imported from Pagewing and renamed. Text files must not mention the old name, except where the fixtures
/// do: PDFs and the signed update test vector are binary or signed and cannot be renamed, so the tests that read the
/// text drawn into them, the word lists that describe them and the tool that fetches a pinned library from Pagewing's
/// release still say "Pagewing". Documentation (<c>Docs</c>, <c>README.md</c>, <c>CLAUDE.md</c>) is not scanned.
/// </summary>
public sealed class NoPagewingLeftoverTests
{
    private static readonly string[] Roots = ["src", "tests", "tools", "testdata", "third-party", ".github"];

    private static readonly string[] SkippedFolders = ["bin", "obj", "artifacts", ".superpowers"];

    private static readonly string[] BinaryExtensions = [".pdf", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".webp", ".zip", ".7z", ".gz", ".ttf", ".otf", ".woff", ".woff2", ".dll", ".exe", ".so", ".dylib", ".bin", ".sig"];

    /// <summary>Files that may still contain the old name. Keep this list as short as the fixtures allow.</summary>
    private static readonly string[] Allowed =
    [
        "testdata/pdfs/ocr-words.tsv",
        "testdata/pdfs/text-edit-runs.tsv",
        "testdata/updates/update-manifest.json",
        "tests/Raffica.Core.Tests/Annotations/AnnotationEditorTests.cs",
        "tests/Raffica.Core.Tests/Annotations/InteropSampleTests.cs",
        "tests/Raffica.Core.Tests/Annotations/MarkupRoundTripTests.cs",
        "tests/Raffica.Core.Tests/Cli/CliTests.cs",
        "tests/Raffica.Core.Tests/Cli/SearchCliTests.cs",
        "tests/Raffica.Core.Tests/Ocr/OcrEngineTests.cs",
        "tests/Raffica.Core.Tests/Ocr/OcrInteractionTests.cs",
        "tests/Raffica.Core.Tests/Ocr/OcrLayerTests.cs",
        "tests/Raffica.Core.Tests/Pdf/DocumentInfoTests.cs",
        "tests/Raffica.Core.Tests/Pdf/OpenTests.cs",
        "tests/Raffica.Core.Tests/Pdf/PrintSupportTests.cs",
        "tests/Raffica.Core.Tests/Pdf/TextTests.cs",
        "tests/Raffica.Core.Tests/Pdfium/DarkPageDrawingTests.cs",
        "tests/Raffica.Core.Tests/Pdfium/DarkPageRenderTests.cs",
        "tests/Raffica.Core.Tests/Pdfium/PageEditSpikeTests.cs",
        "tests/Raffica.Core.Tests/Pdfium/PdfiumDocumentTests.cs",
        "tests/Raffica.Core.Tests/Pdfium/PdfiumPageTextTests.cs",
        "tests/Raffica.Core.Tests/Pdfium/PdfiumTextPageTests.cs",
        "tests/Raffica.Core.Tests/Redaction/RedactionSearchTests.cs",
        "tests/Raffica.Core.Tests/Redaction/RedactionVerifierTests.cs",
        "tests/Raffica.Core.Tests/Text/PageLinksTests.cs",
        "tests/Raffica.Core.Tests/Text/PageTextSourceTests.cs",
        "tests/Raffica.Core.Tests/Text/PageTextTests.cs",
        "tests/Raffica.Core.Tests/Text/SearchPageTests.cs",
        "tests/Raffica.Core.Tests/Text/TextFlowDocumentTests.cs",
        "tests/Raffica.Core.Tests/TextEdit/PdfDocumentTextEditTests.cs",
        "tests/Raffica.Core.Tests/TextEdit/TextEditSpikeTests.cs",
        "tests/Raffica.Core.Tests/Updates/ReleaseToolTests.cs",
        "tests/Raffica.Core.Tests/Updates/UpdateTrustTests.cs",
        "third-party/geist/README.md",
        "third-party/tesseract/README.md",
        "tools/native-fetch.cs",
    ];

    [Fact]
    public void TextFilesMentionPagewingOnlyWhereTheFixturesDo()
    {
        var found = Roots
            .Select(root => Path.Combine(TestData.Root, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(TestData.Root, path).Replace('\\', '/'))
            .Where(relative => !relative.Split('/').Any(part => SkippedFolders.Contains(part)))
            .Where(relative => !BinaryExtensions.Contains(Path.GetExtension(relative).ToLowerInvariant()))
            .Where(relative => !relative.EndsWith("NoPagewingLeftoverTests.cs", StringComparison.Ordinal))
            .Where(relative => IsTextWithOldName(Path.Combine(TestData.Root, relative)))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(Allowed.Order(StringComparer.Ordinal), found);
    }

    private static bool IsTextWithOldName(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 8000)) >= 0)
        {
            return false;
        }

        return System.Text.Encoding.UTF8.GetString(bytes).Contains("pagewing", StringComparison.OrdinalIgnoreCase);
    }
}
