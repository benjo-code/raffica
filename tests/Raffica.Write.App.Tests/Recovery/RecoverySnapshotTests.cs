using Raffica.Core.Saving;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Model;
using Raffica.Write.Recovery;
using Raffica.WriteApp.Tests.Documents;
using Xunit;

namespace Raffica.WriteApp.Tests.Recovery;

public sealed class RecoverySnapshotTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("raffica-snapshot-").FullName;
    private readonly List<DocxFile> _files = [];

    public void Dispose()
    {
        foreach (var file in _files)
        {
            file.Dispose();
        }

        Directory.Delete(_root, recursive: true);
    }

    private RecoveryStore Store => new(Path.Combine(_root, "recovery"));

    private (Subject Subject, string Origin) OpenSubject()
    {
        var origin = Path.Combine(_root, "doc.docx");
        File.Copy(WindowFixture.Poi, origin, overwrite: true);
        var docx = DocxFile.Open(origin);
        _files.Add(docx);
        return (new Subject(docx), origin);
    }

    private static void Edit(DocxFile docx, string append)
    {
        var text = docx.Model.Body.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).SelectMany(r => r.Content).OfType<TextContent>().First();
        text.Text += append;
        docx.Model.Edits.Raise();
    }

    private static string TextOfModel(DocxFile docx) =>
        string.Concat(docx.Model.Body.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).SelectMany(r => r.Content).OfType<TextContent>().Select(t => t.Text));

    private sealed class Subject(DocxFile docx) : IRecoverySubject
    {
        public object Gate { get; } = new();

        public string Id { get; } = RecoveryStore.NewId();

        public string Title => "Untitled";

        public string? OriginalPath => null;

        public DocxFile Docx { get; } = docx;

        public bool RevisionReadInsideLease { get; private set; }

        public int Revision
        {
            get
            {
                RevisionReadInsideLease = Monitor.IsEntered(Gate);
                return Docx.Model.Edits.Revision;
            }
        }

        public bool IsDirty => Docx.Model.Edits.IsDirty;

        public SnapshotSkip? CannotSnapshotNow { get; set; }

        public Action? AfterTaken { get; set; }

        public int Taken { get; private set; }

        public int Released { get; private set; }

        public IDisposable TakeModelLease()
        {
            Monitor.Enter(Gate);
            Taken++;
            AfterTaken?.Invoke();
            return new Release(this);
        }

        private sealed class Release(Subject owner) : IDisposable
        {
            public void Dispose()
            {
                owner.Released++;
                Monitor.Exit(owner.Gate);
            }
        }
    }

    [Fact]
    public async Task ASnapshotWritesADocxAndASidecarThatOpen()
    {
        var (subject, _) = OpenSubject();
        Edit(subject.Docx, "SNAP");
        var store = Store;

        var result = Assert.IsType<SnapshotResult.Written>(await RecoverySnapshot.WriteAsync(subject, store, TestContext.Current.CancellationToken));

        Assert.Equal(subject.Docx.Model.Edits.Revision, result.Revision);
        Assert.True(result.LeaseHeld <= result.Total);
        var entry = Assert.Single(store.List().Entries);
        Assert.Equal(subject.Id, entry.Id);
        Assert.Equal(new FileInfo(store.DocxPathOf(subject.Id)).Length, entry.Bytes);
        Assert.Equal(Environment.ProcessId, entry.OwnerProcessId);
        Assert.Equal(TextOfModel(subject.Docx), SaveHarness.TextOf(store.DocxPathOf(subject.Id)));
        Assert.Equal(1, subject.Taken);
        Assert.Equal(1, subject.Released);
    }

    [Fact]
    public async Task SavingACopyLeavesOriginStampAndSnapshotUntouched()
    {
        var (subject, origin) = OpenSubject();
        var docx = subject.Docx;
        Edit(docx, "SNAP");
        var edits = docx.Model.Edits;
        var savedBefore = edits.SavedRevision;
        var originPathBefore = docx.OriginPath;

        Assert.IsType<SnapshotResult.Written>(await RecoverySnapshot.WriteAsync(subject, Store, TestContext.Current.CancellationToken));

        Assert.Equal(originPathBefore, docx.OriginPath);
        Assert.Equal(savedBefore, edits.SavedRevision);
        Assert.True(edits.IsDirty);
        Assert.True(subject.IsDirty);

        // The stamp of the original is as it was: a save over it is not refused as "changed".
        var report = await docx.SaveAsync(origin, overwriteChangedOriginal: false, TestContext.Current.CancellationToken);
        Assert.Equal(origin, report.Path);
        Assert.Equal(TextOfModel(docx), SaveHarness.TextOf(origin));
    }

    [Fact]
    public async Task TheRevisionIsReadInsideTheLease()
    {
        var (subject, _) = OpenSubject();
        var docx = subject.Docx;
        Edit(docx, "BEFORE");
        var revisionAtStart = docx.Model.Edits.Revision;
        var textAtStart = TextOfModel(docx);
        Thread? editor = null;

        // While the lease is held, another thread tries to edit; it can only get in after the lease is back, so the copy and the recorded revision are both from before it.
        subject.AfterTaken = () =>
        {
            editor = new Thread(() =>
            {
                lock (subject.Gate)
                {
                    Edit(docx, "AFTER");
                }
            });
            editor.Start();
            Thread.Sleep(150);
        };
        var store = Store;

        var result = Assert.IsType<SnapshotResult.Written>(await RecoverySnapshot.WriteAsync(subject, store, TestContext.Current.CancellationToken));
        editor!.Join();

        Assert.True(subject.RevisionReadInsideLease, "the revision is read while the lease is held");
        Assert.Equal(revisionAtStart, result.Revision);
        Assert.Equal(textAtStart, SaveHarness.TextOf(store.DocxPathOf(subject.Id)));
        Assert.NotEqual(result.Revision, docx.Model.Edits.Revision);
        Assert.Contains("AFTER", TextOfModel(docx), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllowedSnapshotsOnlyWhenSkipReasonIsNull()
    {
        var (subject, _) = OpenSubject();
        var store = Store;
        foreach (var reason in Enum.GetValues<SnapshotSkip>())
        {
            subject.CannotSnapshotNow = reason;
            Assert.Equal(new SnapshotResult.Skipped(reason), await RecoverySnapshot.WriteAsync(subject, store, TestContext.Current.CancellationToken));
        }

        Assert.Equal(0, subject.Taken);
        Assert.False(Directory.Exists(store.Folder));

        subject.CannotSnapshotNow = null;
        Assert.IsType<SnapshotResult.Written>(await RecoverySnapshot.WriteAsync(subject, store, TestContext.Current.CancellationToken));
        Assert.Single(store.List().Entries);
    }

    [Fact]
    public async Task FailedSnapshotLeavesThePreviousCopyAndIsSilent()
    {
        var (subject, _) = OpenSubject();
        var docx = subject.Docx;
        var store = Store;
        Edit(docx, "FIRST");
        var firstText = TextOfModel(docx);
        Assert.IsType<SnapshotResult.Written>(await RecoverySnapshot.WriteAsync(subject, store, TestContext.Current.CancellationToken));
        var docxPath = store.DocxPathOf(subject.Id);
        var firstBytes = await File.ReadAllBytesAsync(docxPath, TestContext.Current.CancellationToken);
        var firstEntry = Assert.Single(store.List().Entries);

        Edit(docx, "SECOND");
        docx.BeforeReplace = () => throw new IOException("disk full");
        var result = await RecoverySnapshot.WriteAsync(subject, store, TestContext.Current.CancellationToken);
        docx.BeforeReplace = null;

        var failed = Assert.IsType<SnapshotResult.Failed>(result);
        Assert.IsType<IOException>(Assert.IsType<SaveFailureException>(failed.Error).InnerException);
        Assert.Equal(firstBytes, await File.ReadAllBytesAsync(docxPath, TestContext.Current.CancellationToken));
        Assert.Equal(firstText, SaveHarness.TextOf(docxPath));
        Assert.Equal(firstEntry, Assert.Single(store.List().Entries));
        Assert.Equal([subject.Id + ".docx", subject.Id + ".json"], SaveHarness.Names(store.Folder).Order(StringComparer.Ordinal));
        Assert.Equal(subject.Taken, subject.Released);

        // The next snapshot works again and holds the later text.
        Assert.IsType<SnapshotResult.Written>(await RecoverySnapshot.WriteAsync(subject, store, TestContext.Current.CancellationToken));
        Assert.Equal(TextOfModel(docx), SaveHarness.TextOf(docxPath));
    }

    [Fact]
    public async Task AnUnwritableFolderIsAFailureNotAnException()
    {
        var (subject, _) = OpenSubject();
        var blocker = Path.Combine(_root, "blocker");
        await File.WriteAllTextAsync(blocker, "a file where the folder should be", TestContext.Current.CancellationToken);

        var result = await RecoverySnapshot.WriteAsync(subject, new RecoveryStore(Path.Combine(blocker, "recovery")), TestContext.Current.CancellationToken);

        Assert.IsType<SnapshotResult.Failed>(result);
        Assert.Equal(0, subject.Taken);
    }

    [Fact]
    public async Task ModelReadLeaseReleasesOnEveryFailure()
    {
        var (subject, origin) = OpenSubject();
        var docx = subject.Docx;
        var path = Path.Combine(_root, "out.docx");
        var token = TestContext.Current.CancellationToken;

        // A hook after the take throws: the lease is released at once and the save fails.
        using (ModelReadLease.Attach(docx, subject.TakeModelLease, () => throw new InvalidOperationException("after take")))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => docx.SaveAsync(path, token));
        }

        Assert.Equal(1, subject.Taken);
        Assert.Equal(1, subject.Released);

        // The read itself is fine and a later step fails: AfterModelRead already gave the lease back.
        using (ModelReadLease.Attach(docx, subject.TakeModelLease))
        {
            docx.BeforeReplace = () => throw new IOException("later");
            await Assert.ThrowsAsync<SaveFailureException>(() => docx.SaveAsync(path, token));
            docx.BeforeReplace = null;
        }

        Assert.Equal(2, subject.Taken);
        Assert.Equal(2, subject.Released);

        // The previous hooks come back on dispose.
        var marker = 0;
        docx.BeforeModelRead = () => marker++;
        using (ModelReadLease.Attach(docx, subject.TakeModelLease))
        {
            await docx.SaveAsync(path, token);
        }

        Assert.Equal(1, marker);
        Assert.Equal(3, subject.Released);
        docx.BeforeModelRead!.Invoke();
        Assert.Equal(2, marker);
        Assert.Equal(3, subject.Taken);
        Assert.True(File.Exists(origin));
    }
}
