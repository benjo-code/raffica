using Raffica.Write.Documents;
using Raffica.Write.Layout;
using Xunit;

namespace Raffica.WriteApp.Tests.Documents;

public sealed class DocumentOpenerTests
{
    [Fact]
    public void AnOrdinaryFileOpensAsAPrefixWithPageOneLaidOut()
    {
        var work = new OpenWork();
        var attempt = DocumentOpener.Open(RepoPaths.Corpus("thesis"), work, DocumentOpener.DefaultMaxFileLength, CancellationToken.None);

        using var document = attempt.Document!;
        Assert.NotNull(document);
        Assert.True(document.Docx.IsPrefix);
        Assert.Equal(DocumentOpener.DefaultPrefixBlocks, document.PrefixBlocks);
        Assert.Equal(1, document.Layout.LaidOutPages);
        Assert.True(document.Layout.FirstPageIsFinal);
        Assert.False(document.Layout.IsComplete);
        Assert.Same(work.Fonts(), document.Fonts);
        Assert.Equal(1, work.FilesOpened);
        Assert.Equal(1, work.LayoutsCreated);

        // The package the pictures are read from is the very snapshot of the file: nothing is held open.
        Assert.True(File.ReadAllBytes(RepoPaths.Corpus("thesis")).AsSpan().SequenceEqual(document.Docx.Package.Span));
    }

    [Fact]
    public async Task TheOpenDecodesPageOnesPicturesOnItsOwnThreadAndTheCacheNeverDecodesOnGet()
    {
        using var folder = new TempFolder();
        var path = Raffica.WriteApp.Tests.Viewing.PictureDocx.Write(folder.Path, pages: 3, perPage: 2);

        // Run on a pool thread, as the app does: page 1 has two pictures and both are decoded when the open returns.
        var attempt = await Task.Run(() => DocumentOpener.Open(path, new OpenWork(), DocumentOpener.DefaultMaxFileLength, CancellationToken.None), TestContext.Current.CancellationToken);
        using var document = attempt.Document!;
        var pageOne = document.Layout.Page(0).Items.OfType<ImageItem>().Select(i => i.Image).ToArray();
        Assert.Equal(2, pageOne.Length);
        Assert.Equal(2, document.Pictures.DecodeCount);
        Assert.All(pageOne, p => Assert.NotNull(document.Pictures.Get(p)));

        // A picture of another page is not decoded by Get (the render thread's call): it answers null and decodes nothing.
        Assert.Null(document.Pictures.Get(new ImageRef("document#rIdPic5")));
        Assert.Equal(2, document.Pictures.DecodeCount);
    }

    [Fact]
    public void ADocumentWithoutPicturesDecodesNothingInTheOpen()
    {
        using var document = DocumentOpener.Open(RepoPaths.Corpus("poi-test-document"), new OpenWork(), DocumentOpener.DefaultMaxFileLength, CancellationToken.None).Document!;
        Assert.Equal(0, document.Pictures.DecodeCount);
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("raffica-write-opener-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    [Fact]
    public void AShortFileIsOpenedWholeAndNeedsNoFurtherOpen()
    {
        var attempt = DocumentOpener.Open(RepoPaths.Corpus("poi-test-document"), new OpenWork(), DocumentOpener.DefaultMaxFileLength, CancellationToken.None);

        using var document = attempt.Document!;
        Assert.False(document.Docx.IsPrefix);
        Assert.Equal(0, document.PrefixBlocks);
        Assert.True(document.Layout.LaidOutPages >= 1);
    }

    [Fact]
    public void APrefixThatPageOneReachesTheEndOfGrowsUntilPageOneIsFinal()
    {
        var work = new OpenWork();

        // One block cannot hold a page of the thesis: the opener tries 1, 8, 64 blocks and stops when page 1 ends before the last block read.
        var attempt = DocumentOpener.Open(RepoPaths.Corpus("thesis"), work, DocumentOpener.DefaultMaxFileLength, CancellationToken.None, initialBlocks: 1);

        using var document = attempt.Document!;
        Assert.True(document.Layout.FirstPageIsFinal);
        Assert.True(document.PrefixBlocks > 1);
        Assert.True(work.LayoutsCreated > 1);
        Assert.Equal(1, work.FilesOpened);

        // Page 1 is the same page as the one of a layout over the whole file.
        using var whole = Raffica.Write.Docx.DocxFile.Open(RepoPaths.Corpus("thesis"));
        var full = DocumentLayout.Create(whole.Model, work.Fonts(), new LayoutOptions());
        full.LayOutFirstPage();
        Assert.Equal(full.Page(0).Text, document.Layout.Page(0).Text);
    }

    [Fact]
    public void ACancelledOpenEndsInCancellationAndHoldsNothing()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var work = new OpenWork();

        Assert.Throws<OperationCanceledException>(() => DocumentOpener.Open(RepoPaths.Corpus("thesis"), work, DocumentOpener.DefaultMaxFileLength, cancelled.Token));
        Assert.True(work.NothingStarted);
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException), OpenError.NotFound)]
    [InlineData(typeof(DirectoryNotFoundException), OpenError.NotFound)]
    [InlineData(typeof(UnauthorizedAccessException), OpenError.AccessDenied)]
    [InlineData(typeof(PathTooLongException), OpenError.Other)]
    [InlineData(typeof(IOException), OpenError.Other)]
    [InlineData(typeof(InvalidOperationException), OpenError.Other)]
    public void ExceptionsMapToReasonsWithoutTheirText(Type type, OpenError expected) =>
        Assert.Equal(expected, DocumentOpener.Map((Exception)Activator.CreateInstance(type, "secret document text")!));

    [Fact]
    public void ASharingViolationIsAccessDenied() =>
        Assert.Equal(OpenError.AccessDenied, DocumentOpener.Map(new IOException("in use", unchecked((int)0x80070020))));

    [Fact]
    public void AMissingFileIsNotFoundAndAnInvalidPathIsOther()
    {
        var work = new OpenWork();
        Assert.Equal(OpenError.NotFound, DocumentOpener.Open(Path.Combine(Path.GetTempPath(), "raffica-no-such-file.docx"), work, 1024, CancellationToken.None).Error);
        Assert.Null(DocumentOpener.Open(Path.Combine(Path.GetTempPath(), "raffica-no-such-file.docx"), work, 1024, CancellationToken.None).Document);
        Assert.True(work.NothingStarted);
    }
}
