using Raffica.Hosting;
using Raffica.Write.Recovery;
using Xunit;

namespace Raffica.WriteApp.Tests.Recovery;

public sealed class RecoveryStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("raffica-recovery-").FullName;
    private readonly DateTimeOffset _started = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private RecoveryStore Store => new(Path.Combine(_root, "recovery"));

    private RecoveryEntry EntryOf(string id, string title = "Untitled", int pid = 1234) =>
        new(id, title, null, _started.AddMinutes(5), 10, pid, _started);

    private async Task<string> AddPairAsync(RecoveryStore store)
    {
        var id = RecoveryStore.NewId();
        store.EnsureFolder();
        await File.WriteAllBytesAsync(store.DocxPathOf(id), [1, 2, 3], TestContext.Current.CancellationToken);
        await store.WriteSidecarAsync(EntryOf(id));
        return id;
    }

    private sealed class FakeProbe(DateTimeOffset? start) : IProcessProbe
    {
        public DateTimeOffset? StartTimeOf(int processId) => start;
    }

    [Fact]
    public void FolderFollowsTheSettingsFile()
    {
        var settings = Path.Combine(_root, "cfg", "settings.json");
        Assert.Equal(Path.Combine(Path.GetFullPath(Path.Combine(_root, "cfg")), "recovery"), RecoveryStore.FolderFor(settings));
        Assert.Equal(Path.Combine(AppFiles.ConfigDirectoryFor(null), "recovery"), RecoveryStore.FolderFor(null));
        Assert.False(Directory.Exists(Path.Combine(_root, "cfg")));
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void TheFolderIsMadeForItsOwnerOnly()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes exist on Linux and macOS only (CI runs this test there).");
        var store = Store;

        store.EnsureFolder();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(store.Folder));
    }

    [Fact]
    public async Task SidecarRoundTrips()
    {
        var store = Store;
        var id = RecoveryStore.NewId();
        Assert.Equal(32, id.Length);
        store.EnsureFolder();
        await File.WriteAllBytesAsync(store.DocxPathOf(id), [1], TestContext.Current.CancellationToken);
        var entry = new RecoveryEntry(id, "Thesis.docx", @"C:\x\Thesis.docx", _started, 1, 77, _started);
        await store.WriteSidecarAsync(entry);

        var listing = store.List();
        Assert.Equal(entry, Assert.Single(listing.Entries));
        Assert.Empty(listing.Problems);
        Assert.Empty(Directory.GetFiles(store.Folder, "*.tmp"));
    }

    [Fact]
    public async Task ListSeesOnlyCompletePairs()
    {
        var store = Store;
        var good = await AddPairAsync(store);
        var noSidecar = RecoveryStore.NewId();
        await File.WriteAllBytesAsync(store.DocxPathOf(noSidecar), [1], TestContext.Current.CancellationToken);

        var listing = store.List();
        Assert.Equal(good, Assert.Single(listing.Entries).Id);
        Assert.Empty(listing.Problems);
        Assert.True(File.Exists(store.DocxPathOf(noSidecar)));
    }

    [Fact]
    public void ListingAMissingFolderCreatesNothing()
    {
        var listing = Store.List();
        Assert.Empty(listing.Entries);
        Assert.False(Directory.Exists(Path.Combine(_root, "recovery")));
    }

    [Fact]
    public async Task GarbageInTheFolderIsIgnoredAndNeverDeleted()
    {
        var store = Store;
        var good = await AddPairAsync(store);
        var folder = store.Folder;
        var unrelated = Path.Combine(folder, "notes.txt");
        var nonJsonId = RecoveryStore.NewId();
        var truncatedId = RecoveryStore.NewId();
        var orphanId = RecoveryStore.NewId();
        await File.WriteAllTextAsync(unrelated, "keep", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(folder, "weird.json"), "{}", TestContext.Current.CancellationToken);
        foreach (var id in new[] { nonJsonId, truncatedId })
        {
            await File.WriteAllBytesAsync(store.DocxPathOf(id), [1], TestContext.Current.CancellationToken);
        }

        await File.WriteAllTextAsync(Path.Combine(folder, nonJsonId + ".json"), "not json at all", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(folder, truncatedId + ".json"), "{\"Id\":\"" + truncatedId + "\",\"Title\":\"Unt", TestContext.Current.CancellationToken);
        await store.WriteSidecarAsync(EntryOf(orphanId));

        var before = Directory.GetFiles(folder).Order().ToList();
        var listing = store.List();

        Assert.Equal(good, Assert.Single(listing.Entries).Id);
        Assert.Equal(new[] { nonJsonId, truncatedId, orphanId }.Order(), listing.Problems.Order());
        Assert.Equal(before, Directory.GetFiles(folder).Order().ToList());

        store.Delete(good);
        Assert.True(File.Exists(unrelated));
        Assert.Equal(before.Count - 2, Directory.GetFiles(folder).Length);
        store.Delete("..\\..\\evil");
        store.Delete(RecoveryStore.NewId());
        Assert.Throws<ArgumentException>(() => store.DocxPathOf("..\\x"));
    }

    [Fact]
    public async Task DeleteRemovesThePairOnly()
    {
        var store = Store;
        var a = await AddPairAsync(store);
        var b = await AddPairAsync(store);

        store.Delete(a);
        store.Delete(a);

        Assert.False(File.Exists(store.DocxPathOf(a)));
        Assert.Equal(b, Assert.Single(store.List().Entries).Id);
        Assert.True(File.Exists(store.DocxPathOf(b)));
    }

    [Fact]
    public void LivenessSkipsAnAliveForeignProcess()
    {
        var entry = EntryOf("0123456789abcdef0123456789abcdef", pid: 50);
        Assert.True(RecoveryLiveness.IsLive(entry, 1, new FakeProbe(_started.AddSeconds(1))));
        Assert.False(RecoveryLiveness.IsLive(entry, 1, new FakeProbe(null)));
        Assert.False(RecoveryLiveness.IsLive(entry, 1, new FakeProbe(_started.AddMinutes(3))));
        Assert.False(RecoveryLiveness.IsLive(entry, 50, new FakeProbe(_started)));
        Assert.True(RecoveryLiveness.IsLive(entry, 1, new FakeProbe(DateTimeOffset.MaxValue)));
        Assert.False(RecoveryLiveness.IsLive(entry, 50, new FakeProbe(DateTimeOffset.MaxValue)));
        Assert.NotNull(new SystemProcessProbe().StartTimeOf(Environment.ProcessId));
    }

    [Fact]
    public async Task NoDocumentTextInTheSidecar()
    {
        var store = Store;
        var id = RecoveryStore.NewId();
        store.EnsureFolder();
        await File.WriteAllBytesAsync(store.DocxPathOf(id), [1], TestContext.Current.CancellationToken);
        await store.WriteSidecarAsync(EntryOf(id));

        var json = await File.ReadAllTextAsync(Path.Combine(store.Folder, id + ".json"), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("zqxjkv", json, StringComparison.Ordinal);
        Assert.Contains("\"Untitled\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeleteThatCannotRemoveTheDocxKeepsTheEntryListedAndRetriable()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs a file lock that blocks deleting.");
        var store = Store;
        var id = await AddPairAsync(store);

        using (new FileStream(store.DocxPathOf(id), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            store.Delete(id);
            Assert.Equal(id, Assert.Single(store.List().Entries).Id);
        }

        store.Delete(id);
        Assert.Empty(store.List().Entries);
        Assert.Empty(store.List().Problems);
        Assert.Empty(Directory.GetFiles(store.Folder));
    }

    [Theory]
    [InlineData("{\"Id\":\"ID\"}")]
    [InlineData("null")]
    [InlineData("{\"Id\":\"ID\",\"Title\":\"x\",\"Bytes\":-1,\"OwnerProcessId\":5}")]
    public async Task ASidecarWithMissingFieldsIsAProblemNotAnEntry(string json)
    {
        var store = Store;
        var id = RecoveryStore.NewId();
        store.EnsureFolder();
        await File.WriteAllBytesAsync(store.DocxPathOf(id), [1], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(store.Folder, id + ".json"), json.Replace("ID", id, StringComparison.Ordinal), TestContext.Current.CancellationToken);

        var listing = store.List();
        Assert.Empty(listing.Entries);
        Assert.Equal(id, Assert.Single(listing.Problems));
        Assert.True(File.Exists(store.DocxPathOf(id)));
    }
}
