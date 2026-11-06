using System.Text;
using Avalonia.Headless.XUnit;
using Raffica.Core.Pdf;
using Raffica.Core.Redaction;
using Raffica.Core.Text;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

/// <summary>
/// The acceptance of the milestone through the window, the way a user does it: mark (by search in the Find panel, or an area), Apply, Save as into a
/// temp folder, then read the written file with Core as an independent reader. The removed text is in the extracted text (invisible text included) zero
/// times and in the bytes of the file zero times (every stream unpacked, <see cref="RawContentScanner"/>); the pages are right; the original is byte
/// for byte as it was; the result card has its "Checked" section. A file that Core refuses (page keys it cannot remove) or fails at the check writes
/// nothing, leaves the original as it was, keeps plan and mode and says why on a card: that is the other way a redaction may end, never a file that still
/// holds the text.
/// </summary>
public sealed class RedactUiAcceptanceTests
{
    private const string Term = "Zephyrstraße 17";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>One fixture: what the user marks, how it must end and what must be gone.</summary>
    internal sealed record Case
    {
        public required string File { get; init; }

        /// <summary>The term the user searches and marks everywhere; null: none.</summary>
        public string? Search { get; init; }

        /// <summary>Areas the user drags (page, rectangle in points from the top left), for what the search cannot find.</summary>
        public (int Page, PageRect Area)[] Areas { get; init; } = [];

        /// <summary>What must be in the text of the fixture before and gone from the extracted text after.</summary>
        public string[] GoneText { get; init; } = [];

        /// <summary>What must be in the bytes of the fixture before and gone from the bytes after.</summary>
        public string[] GoneBytes { get; init; } = [];

        /// <summary>What stays in the bytes by the report's own account ("Not cleaned": the file of a paperclip note).</summary>
        public string[] StaysInBytes { get; init; } = [];

        public ApplyOutcomeKind Outcome { get; init; } = ApplyOutcomeKind.Written;

        /// <summary>The pages that cannot be redacted in place and are turned into images with a tick on the Apply card (0-based).</summary>
        public int[] Rescued { get; init; } = [];

        /// <summary>A text of the page that must still be there (the redaction takes no more than the marks).</summary>
        public string? Kept { get; init; }

        public override string ToString() => File;
    }

    private static readonly Case[] Cases =
    [
        new() { File = "redact-control.pdf", Areas = [(0, new PageRect(15, 40, 200, 80))], Kept = "Public line stays" },
        new() { File = "redact-covered.pdf", Search = Term, GoneText = [Term], GoneBytes = [Term], Kept = "Public line stays visible" },
        new()
        {
            File = "redact-attachments.pdf",
            Search = Term,
            GoneText = [Term],
            GoneBytes = [Term, "CATALOGFILE-7421"],
            StaysInBytes = ["ANNOTFILE-9183"],
            Kept = "Public line stays visible",
        },
        new() { File = "redact-revisions.pdf", Areas = [(0, new PageRect(15, 60, 280, 90))], GoneBytes = [Term] },
        new() { File = "redact-fonts.pdf", Search = Term, Areas = [(0, new PageRect(15, 130, 300, 160))], GoneText = [Term], GoneBytes = [Term] },
        new() { File = "redact-ligature.pdf", Search = "Zephyr", GoneText = ["Zephyr"], Kept = "Ligature fixture" },
        new() { File = "redact-rotated.pdf", Search = Term, GoneText = [Term], GoneBytes = [Term], Kept = "Top line" },
        new() { File = "redact-hidden.pdf", Areas = [(0, new PageRect(15, 15, 200, 75))], GoneBytes = [Term], Rescued = [0] },
        new() { File = "redact-everywhere.pdf", Search = Term, GoneText = [Term], GoneBytes = [Term], Rescued = [0] },
        new() { File = "redact-structure.pdf", Search = Term, Outcome = ApplyOutcomeKind.Refused },
        new() { File = "redact-objstm.pdf", Areas = [(0, new PageRect(15, 15, 200, 60))], Outcome = ApplyOutcomeKind.Refused },
        new() { File = "redact-image.pdf", Areas = [(0, new PageRect(10, 10, 100, 100))] },
    ];

    public static TheoryData<string> Names() => [.. Cases.Select(c => c.File)];

    /// <summary>The independent read: the extracted text of every page, the page count, and the occurrences of a text in the bytes.</summary>
    internal sealed record Reading(int Pages, IReadOnlyList<string> PageTexts, byte[] Bytes)
    {
        public string AllText => string.Join('\n', PageTexts);

        /// <summary>The places the scanner finds the text in the bytes (strings decoded, every stream unpacked), plus the plain byte matches in four encodings.</summary>
        public int InBytes(string term)
        {
            using var stream = new MemoryStream(Bytes);
            var scanned = RawContentScanner.Count(stream, term, RawScanLimits.Default, out _, Ct);
            var plain = new[] { Encoding.Latin1, Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode }.Sum(e => Count(Bytes, e.GetBytes(term)));
            return scanned + plain;
        }

        private static int Count(byte[] haystack, byte[] needle)
        {
            var count = 0;
            var from = 0;
            while (from <= haystack.Length - needle.Length)
            {
                var at = haystack.AsSpan(from).IndexOf(needle);
                if (at < 0)
                {
                    break;
                }

                count++;
                from += at + 1;
            }

            return count;
        }
    }

    internal static async Task<Reading> ReadAsync(string path)
    {
        await using var document = await new PdfEngine().OpenAsync(path, null, Ct);
        var texts = new string[document.PageCount];
        for (var i = 0; i < texts.Length; i++)
        {
            // GetTextAsync reads the text objects, invisible text (render mode 3) included.
            texts[i] = await document.GetTextAsync(i, Ct);
        }

        return new Reading(document.PageCount, texts, await File.ReadAllBytesAsync(path, Ct));
    }

    [AvaloniaTheory]
    [MemberData(nameof(Names))]
    public async Task EveryFixtureLeavesNoTermInTextOrBytes(string file)
    {
        var item = Cases.Single(c => c.File == file);
        using var rig = await RedactUiRig.OpenAsync(file);
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        var before = await ReadAsync(rig.Path);

        // The fixture holds what the test says it removes, so "zero after" is not zero because it was never there.
        foreach (var text in item.GoneText)
        {
            Assert.Contains(text, before.AllText, StringComparison.Ordinal);
        }

        foreach (var bytes in item.GoneBytes.Concat(item.StaysInBytes))
        {
            Assert.True(before.InBytes(bytes) > 0, $"{file}: the fixture must hold {bytes} in its bytes before");
        }

        await rig.EnterModeAsync();
        if (item.Search is { } search)
        {
            Assert.True(await rig.MarkTermAsync(search) > 0);
        }

        foreach (var (page, area) in item.Areas)
        {
            rig.MarkArea(page, area);
        }

        var output = Path.Combine(rig.Folder, "redacted-" + file);
        IReadOnlyList<string> applyCard = [];
        var outcome = await rig.ApplyAsync(output, onApplyCard: card => applyCard = RedactUiRig.NamesOf(card));

        // The original is as it was, whatever happened.
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.True(item.Outcome == outcome.Kind, $"{file}: expected {item.Outcome} but {outcome.Kind} ({rig.Marked}): {outcome.All}");

        // Cards show the analysis, never the marked text or a value of the document.
        foreach (var secret in new[] { "Zephyr", "Quellmann", "CATALOGFILE", "ANNOTFILE" })
        {
            Assert.DoesNotContain(secret, outcome.All, StringComparison.Ordinal);
        }

        // A page that cannot be redacted in place is offered as an image, with a tick each; nothing else is.
        Assert.Equal(item.Rescued.Select(p => "PART_Rescue_" + p).Order(), applyCard.Where(n => n.StartsWith("PART_Rescue_", StringComparison.Ordinal)).Order());

        if (outcome.Kind != ApplyOutcomeKind.Written)
        {
            Assert.False(File.Exists(output), $"{file}: a refused or failed redaction must write nothing");
            Assert.True(rig.Tab.RedactIfCreated is { IsActive: true }, "the plan and the mode stay");
            Assert.Contains(outcome.Names, n => n == "PART_Reason");
            return;
        }

        Assert.Contains("PART_Checked", outcome.Names);
        Assert.Contains("PART_NotChecked", outcome.Names);
        var after = await ReadAsync(output);
        Assert.Equal(before.Pages, after.Pages);
        foreach (var text in item.GoneText)
        {
            Assert.DoesNotContain(text, after.AllText, StringComparison.Ordinal);
        }

        foreach (var bytes in item.GoneBytes)
        {
            Assert.Equal(0, after.InBytes(bytes));
        }

        // What the report says stays does stay, and the report says so ("Not cleaned"), so the test does not hide a leak behind a wrong expectation.
        foreach (var stays in item.StaysInBytes)
        {
            Assert.True(after.InBytes(stays) > 0, $"{file}: {stays} is documented to stay");
        }

        if (item.StaysInBytes.Length > 0)
        {
            Assert.Contains(outcome.Texts, t => t.Contains(PdfStrings.Fill(PdfStrings.RedactNotCleanedAttachFormat, 1), StringComparison.Ordinal));
        }

        if (item.Kept is { } kept)
        {
            Assert.Contains(kept, after.AllText, StringComparison.Ordinal);
        }

        // The tab follows the written file and the mode is over.
        Assert.Equal(output, rig.Tab.Path);
        Assert.Null(rig.Tab.RedactIfCreated);
    }
    [AvaloniaTheory]
    [InlineData(RedactionPatternKind.Email)]
    [InlineData(RedactionPatternKind.Iban)]
    [InlineData(RedactionPatternKind.Phone)]
    [InlineData(RedactionPatternKind.Card)]
    public async Task EveryPatternKindLeavesNoMatchInTextOrBytes(RedactionPatternKind kind)
    {
        using var rig = await RedactUiRig.OpenAsync("redact-patterns.pdf");
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        await rig.EnterModeAsync();
        var matches = await rig.MarkPatternAsync(kind);

        var output = Path.Combine(rig.Folder, "redacted-patterns.pdf");
        var outcome = await rig.ApplyAsync(output);

        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.True(outcome.Kind == ApplyOutcomeKind.Written, $"{kind}: {outcome.Kind} ({matches.Count} matches): {outcome.All}");
        Assert.Contains("PART_Checked", outcome.Names);
        var after = await ReadAsync(output);
        foreach (var match in matches.Distinct())
        {
            Assert.DoesNotContain(match, after.AllText, StringComparison.Ordinal);
            Assert.Equal(0, after.InBytes(match));
            if (RemovedText.RawFormOf(match) is { } raw)
            {
                Assert.Equal(0, after.InBytes(raw));
            }
        }
    }
    [AvaloniaFact]
    public async Task APageTurnedIntoAnImageIsOneImageAndNoText()
    {
        using var rig = await RedactUiRig.OpenAsync("redact-paths.pdf");
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        var before = await ReadAsync(rig.Path);
        Assert.Contains(Term, before.AllText, StringComparison.Ordinal);
        Assert.True(before.InBytes(Term) > 0);
        await rig.EnterModeAsync();

        // The ribbon command of the Look group turns the page the view shows into an image; no mark is needed.
        rig.Command("redact.rasterPage").Run!();
        Assert.Equal([0], rig.Session.Plan.RasterPages.Order());
        var output = Path.Combine(rig.Folder, "redacted-paths.pdf");
        var outcome = await rig.ApplyAsync(output);

        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.True(outcome.Kind == ApplyOutcomeKind.Written, outcome.All);
        Assert.Contains(PdfStrings.Fill(PdfStrings.RedactChkRasterFormat, "1"), outcome.Texts.Select(t => t.TrimStart('•', ' ')));
        var after = await ReadAsync(output);
        Assert.Equal(before.Pages, after.Pages);
        Assert.Equal(string.Empty, after.AllText.Trim());
        Assert.Equal(0, after.InBytes(Term));
        await using var document = await new PdfEngine().OpenAsync(output, null, Ct);
        Assert.Equal(new PageObjectSummary(Images: 1, Paths: 0, Texts: 0, Others: 0, Annotations: 0), await document.GetPageObjectSummaryAsync(0, Ct));
    }
}
