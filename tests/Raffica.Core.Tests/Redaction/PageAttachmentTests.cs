using Raffica.Core.Pdf;
using Raffica.Core.Redaction;
using Raffica.Core.Saving;
using Raffica.Core.Tests.Annotations;
using Raffica.Core.Text;
using Raffica.Core.Viewing;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Core.Tests.Redaction;

/// <summary>
/// Files and threads that hang off a page (milestone 2.7, task 2): the cleaning rebuild copies every page key except /Type and /Parent, so a
/// page's /AF (associated files with embedded streams) and /B (article beads to a thread with a title and an author) would survive it. They are
/// page keys like /Thumb now (refused, or gone with a page that becomes an image), and the report only says "cleaned" for what the read-back of
/// the written file confirmed.
/// </summary>
public sealed class PageAttachmentTests : IDisposable
{
    private const string FileSecret = "ATTACHEDSECRETDATA";
    private const string TitleSecret = "THREADTITLESECRET";
    private const string AuthorSecret = "THREADAUTHORSECRET";

    private readonly TempFolder folder = new();
    private readonly PdfEngine engine = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => folder.Dispose();

    private static readonly string[] Secrets = [FileSecret, TitleSecret, AuthorSecret];

    /// <summary>A one-page file whose page has /AF (one embedded file) and /B (one bead of a thread with title and author).</summary>
    private string AfAndBeadsFile()
    {
        var file = new VerifierPdf("BT /F1 12 Tf 20 150 Td (secret value) Tj ET BT /F1 12 Tf 20 100 Td (public line) Tj ET");
        var stream = file.AddStream("/Type /EmbeddedFile", VerifierPdf.Latin1(FileSecret));
        var spec = file.Add($"<< /Type /Filespec /F (a.txt) /AFRelationship /Data /EF << /F {stream} 0 R >> >>");
        var thread = file.Add("<< /Type /Thread /F " + (spec + 2) + " 0 R /I << /Title (" + TitleSecret + ") /Author (" + AuthorSecret + ") >> >>");
        var bead = file.Add($"<< /T {thread} 0 R /N {thread + 1} 0 R /V {thread + 1} 0 R /P 4 0 R /R [0 0 100 100] >>");
        Assert.Equal(thread + 1, bead);
        file.PageEntries = $"/AF [{spec} 0 R] /B [{bead} 0 R]";
        return Write("af-b.pdf", file.Build());
    }

    /// <summary>A one-page file with one annotation that carries an embedded file; <paramref name="paperclip"/> makes it a paperclip.</summary>
    private string AnnotationFile(bool paperclip)
    {
        var file = new VerifierPdf("BT /F1 12 Tf 20 150 Td (secret value) Tj ET BT /F1 12 Tf 20 100 Td (public line) Tj ET");
        var stream = file.AddStream("/Type /EmbeddedFile", VerifierPdf.Latin1(FileSecret));
        var spec = file.Add($"<< /Type /Filespec /F (a.txt) /AFRelationship /Data /EF << /F {stream} 0 R >> >>");
        var annotation = paperclip
            ? file.Add($"<< /Type /Annot /Subtype /FileAttachment /Rect [250 10 270 30] /Name /Paperclip /FS {spec} 0 R /AF [{spec} 0 R] >>")
            : file.Add($"<< /Type /Annot /Subtype /Link /Rect [250 10 270 30] /Border [0 0 0] /AF [{spec} 0 R] >>");
        file.PageEntries = $"/Annots [{annotation} 0 R]";
        return Write(paperclip ? "paperclip-af.pdf" : "link-af.pdf", file.Build());
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(folder.Path, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string Target() => Path.Combine(folder.Path, "redacted.pdf");

    private static RedactionPlan Plan() =>
        RedactionPlan.Empty.With(new RedactionMark(Guid.NewGuid(), RedactionKind.Area, 0, [new PageRect(15, 35, 120, 60)], null, false));

    [Fact]
    public async Task APageWithAssociatedFilesAndArticleBeadsIsRefusedAndNothingIsWritten()
    {
        var path = AfAndBeadsFile();
        var before = await File.ReadAllBytesAsync(path, Ct);
        foreach (var secret in Secrets)
        {
            Assert.True(RawPdf.ContainsAnywhere(before, secret));
        }

        using var session = new ViewerSession(engine);
        await session.OpenAsync(path);

        var analysis = await new RedactionExecutor(engine).AnalyzeAsync(Plan(), path, null, Ct);
        Assert.True(analysis.IsRefused);
        var listed = Assert.Single(analysis.Refusals);
        Assert.Equal(RedactionRefusalKind.UnremovablePageKeys, listed.Kind);
        Assert.Equal([0], listed.Pages);
        Assert.Contains("/AF on page 1", listed.Reason, StringComparison.Ordinal);
        Assert.Contains("/B on page 1", listed.Reason, StringComparison.Ordinal);

        var refused = await Assert.ThrowsAsync<RedactionRefusedException>(() => RedactionApplier.ApplyAsync(session, engine, new RedactionApplyRequest(Plan(), Target()), null, Ct));
        Assert.Equal(RedactionRefusalKind.UnremovablePageKeys, Assert.Single(refused.Refusals).Kind);
        Assert.False(File.Exists(Target()));
    }

    [Fact]
    public async Task APageThatBecomesAnImageStaysRefusedLikeAnyOtherPageKeyAndNoSecretIsWritten()
    {
        // The rule of milestone 14 for /Thumb applies unchanged: a page key keeps the file refused even if its page becomes an image.
        var path = AfAndBeadsFile();
        using var session = new ViewerSession(engine);
        await session.OpenAsync(path);
        var plan = Plan().WithRasterPage(0, on: true);

        var refused = await Assert.ThrowsAsync<RedactionRefusedException>(() => RedactionApplier.ApplyAsync(session, engine, new RedactionApplyRequest(plan, Target()), null, Ct));

        Assert.Equal(RedactionRefusalKind.UnremovablePageKeys, Assert.Single(refused.Refusals).Kind);
        Assert.False(File.Exists(Target()));
    }

    [Fact]
    public async Task AFileOnlyOnAPageNeverCountsAsACleanedAttachment()
    {
        var path = AfAndBeadsFile();
        using var session = new ViewerSession(engine);
        await session.OpenAsync(path);

        var analysis = await new RedactionExecutor(engine).AnalyzeAsync(Plan(), path, null, Ct);

        Assert.DoesNotContain(analysis.Cleanup, static item => item.Kind == CleanupKind.Attachments);
        Assert.DoesNotContain("Attached files", RedactionReportText.Of(analysis), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAnnotationFileOnAnUnmarkedPageIsListedAsNotCleanedAndStays()
    {
        var path = AnnotationFile(paperclip: true);
        using var session = new ViewerSession(engine);
        await session.OpenAsync(path);

        var analysis = await new RedactionExecutor(engine).AnalyzeAsync(Plan(), session.Document!, path, null, Ct);
        Assert.False(analysis.IsRefused, RedactionRefusalText.Of(analysis.Refusals));
        Assert.Equal(1, analysis.AttachmentAnnotations);
        Assert.DoesNotContain(analysis.Cleanup, static item => item.Kind == CleanupKind.Attachments);

        var result = await RedactionApplier.ApplyAsync(session, engine, new RedactionApplyRequest(Plan(), Target()), null, Ct);

        Assert.True(result.Check.IsClean, RedactionRefusalText.Of(result.Check));
        Assert.True(RawPdf.ContainsAnywhere(await File.ReadAllBytesAsync(Target(), Ct), FileSecret));
        var report = RedactionReportText.Of(analysis, result.Check);
        Assert.DoesNotContain("Attached files", report, StringComparison.Ordinal);
        Assert.Contains("File attachments of annotations", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmbeddedFileThatIsNoPaperclipAndStaysInTheOutputFailsTheCleanupReadBack()
    {
        var path = AnnotationFile(paperclip: false);
        using var session = new ViewerSession(engine);
        await session.OpenAsync(path);

        var failure = await Assert.ThrowsAsync<SaveFailureException>(() => RedactionApplier.ApplyAsync(session, engine, new RedactionApplyRequest(Plan(), Target()), null, Ct));

        Assert.Equal(SaveFailureReason.RedactionFailed, failure.Reason);
        var check = Assert.IsType<RedactionCheck>(failure.Details);
        var finding = Assert.Single(check.Findings, static f => f.Kind == RedactionFindingKind.CleanupItemStillPresent);
        Assert.Contains("embedded file", finding.Detail, StringComparison.Ordinal);
        Assert.False(File.Exists(Target()));
    }

    [Fact]
    public async Task AFileWithAMovieOrSoundAnnotationListsMediaAsNotCleaned()
    {
        var file = new VerifierPdf("BT /F1 12 Tf 20 150 Td (secret value) Tj ET");
        var sound = file.Add("<< /Type /Annot /Subtype /Sound /Rect [250 10 270 30] >>");
        file.PageEntries = $"/Annots [{sound} 0 R]";
        var path = Write("media.pdf", file.Build());

        var analysis = await new RedactionExecutor(engine).AnalyzeAsync(Plan(), path, null, Ct);

        Assert.Equal(1, analysis.MediaAnnotations);
        Assert.Contains("Sound and video annotations", RedactionReportText.Of(analysis), StringComparison.Ordinal);
    }
}
