using Avalonia.Headless.XUnit;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Model;
using Raffica.Write.Recovery;
using Raffica.WriteApp.Tests.Documents;
using Xunit;

namespace Raffica.WriteApp.Tests.Recovery;

/// <summary>The scheduler of the recovery copies (Task 6 of milestone 4.3c) with a clock the test ticks and tabs that are small stand-ins over real files.</summary>
public sealed class AutosaveSchedulerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("raffica-autosave-").FullName;
    private readonly List<DocxFile> _files = [];
    private readonly ManualRecoveryClock _clock = new();
    private readonly List<AutosaveScheduler> _schedulers = [];
    private int _concurrent;
    private int _maxConcurrent;

    public void Dispose()
    {
        foreach (var scheduler in _schedulers)
        {
            scheduler.Dispose();
        }

        foreach (var file in _files)
        {
            file.Dispose();
        }

        Directory.Delete(_root, recursive: true);
    }

    private RecoveryStore Store => new(Path.Combine(_root, "recovery"));

    private AutosaveScheduler NewScheduler(RecoveryStore? store = null, Func<bool>? enabled = null)
    {
        var scheduler = new AutosaveScheduler(store ?? Store, _clock, enabled ?? (() => true));
        _schedulers.Add(scheduler);
        return scheduler;
    }

    private Subject NewSubject()
    {
        var origin = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".docx");
        File.Copy(WindowFixture.Poi, origin, overwrite: true);
        var docx = DocxFile.Open(origin);
        _files.Add(docx);
        return new Subject(docx, this);
    }

    private static async Task IdleAsync(AutosaveScheduler scheduler)
    {
        var task = scheduler.WhenIdleAsync();
        await SaveHarness.AwaitAsync(task);
    }

    private static void Edit(Subject subject)
    {
        var text = subject.Docx.Model.Body.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).SelectMany(r => r.Content).OfType<TextContent>().First();
        text.Text += "x";
        subject.Docx.Model.Edits.Raise();
    }

    private sealed class Subject(DocxFile docx, AutosaveSchedulerTests owner) : IRecoverySubject
    {
        private readonly object _gate = new();

        public string Id { get; } = RecoveryStore.NewId();

        public string Title => "Untitled";

        public string? OriginalPath => null;

        public DocxFile Docx { get; } = docx;

        public int Revision => Docx.Model.Edits.Revision;

        public bool IsDirty => Docx.Model.Edits.IsDirty;

        public SnapshotSkip? CannotSnapshotNow { get; set; }

        public int Taken { get; private set; }

        public IDisposable TakeModelLease()
        {
            Monitor.Enter(_gate);
            Taken++;
            var now = Interlocked.Increment(ref owner._concurrent);
            int seen;
            while (now > (seen = Volatile.Read(ref owner._maxConcurrent)) && Interlocked.CompareExchange(ref owner._maxConcurrent, now, seen) != seen)
            {
            }

            // Long enough that a second snapshot started at the same time would overlap this one.
            Thread.Sleep(60);
            return new Release(this);
        }

        private sealed class Release(Subject subject) : IDisposable
        {
            public void Dispose()
            {
                Interlocked.Decrement(ref subject.Owner._concurrent);
                Monitor.Exit(subject._gate);
            }
        }

        private AutosaveSchedulerTests Owner => owner;
    }

    [AvaloniaFact]
    public void NothingStartsBeforeTheFirstTrack()
    {
        var scheduler = NewScheduler();
        Assert.Equal(0, _clock.Started);
        Assert.False(scheduler.IsClockRunning);
        Assert.False(Directory.Exists(Store.Folder));
        scheduler.TickNow();
        Assert.False(Directory.Exists(Store.Folder));
    }

    [AvaloniaFact]
    public void FirstDirtyTabStartsTheClock()
    {
        var scheduler = NewScheduler();
        var first = NewSubject();
        var second = NewSubject();

        scheduler.Track(first);
        scheduler.Track(second);
        scheduler.Track(first);

        Assert.Equal(1, _clock.Started);
        Assert.Equal(TimeSpan.FromSeconds(30), _clock.Interval);
        Assert.Equal(2, scheduler.TrackedCount);
    }

    [AvaloniaFact]
    public async Task TickWritesOneCopyPerChangedTab()
    {
        var scheduler = NewScheduler();
        var a = NewSubject();
        var b = NewSubject();
        Edit(a);
        Edit(b);
        scheduler.Track(a);
        scheduler.Track(b);

        _clock.Fire();
        await IdleAsync(scheduler);
        Assert.Equal(2, Store.List().Entries.Count);
        Assert.Equal([1, 1], new[] { a.Taken, b.Taken });

        Edit(b);
        _clock.Fire();
        await IdleAsync(scheduler);
        Assert.Equal([1, 2], new[] { a.Taken, b.Taken });
        Assert.Equal(2, Store.List().Entries.Count);
        Assert.Equal(0, scheduler.FailureCount);
    }

    [AvaloniaFact]
    public async Task TickSkipsWhatCannotBeSnapshotted()
    {
        foreach (var reason in new[] { SnapshotSkip.Prefix, SnapshotSkip.Faulted, SnapshotSkip.Saving, SnapshotSkip.Disposed })
        {
            var scheduler = NewScheduler(new RecoveryStore(Path.Combine(_root, "recovery-" + reason)));
            var subject = NewSubject();
            Edit(subject);
            subject.CannotSnapshotNow = reason;
            scheduler.Track(subject);

            _clock.Fire();
            await IdleAsync(scheduler);

            Assert.Equal(0, subject.Taken);
            Assert.Empty(new RecoveryStore(Path.Combine(_root, "recovery-" + reason)).List().Entries);
            Assert.Equal(0, scheduler.FailureCount);
        }
    }

    [AvaloniaFact]
    public async Task ASkippedTabIsCopiedOnceItCanBe()
    {
        var scheduler = NewScheduler();
        var subject = NewSubject();
        Edit(subject);
        subject.CannotSnapshotNow = SnapshotSkip.Saving;
        scheduler.Track(subject);
        _clock.Fire();
        await IdleAsync(scheduler);
        Assert.Equal(0, subject.Taken);

        subject.CannotSnapshotNow = null;
        _clock.Fire();
        await IdleAsync(scheduler);

        Assert.Equal(1, subject.Taken);
    }

    [AvaloniaFact]
    public async Task TickSkipsAnUnchangedRevision()
    {
        var scheduler = NewScheduler();
        var subject = NewSubject();
        Edit(subject);
        scheduler.Track(subject);

        _clock.Fire();
        await IdleAsync(scheduler);
        _clock.Fire();
        await IdleAsync(scheduler);
        _clock.Fire();
        await IdleAsync(scheduler);

        Assert.Equal(1, subject.Taken);
        Edit(subject);
        _clock.Fire();
        await IdleAsync(scheduler);
        Assert.Equal(2, subject.Taken);
    }

    [AvaloniaFact]
    public async Task OneSnapshotAtATime()
    {
        var scheduler = NewScheduler();
        var a = NewSubject();
        var b = NewSubject();
        Edit(a);
        Edit(b);
        scheduler.Track(a);
        scheduler.Track(b);

        // A second request while the pass runs does not start a second pass beside it.
        _clock.Fire();
        _clock.Fire();
        scheduler.TickNow();
        await IdleAsync(scheduler);

        Assert.Equal(1, _maxConcurrent);
        Assert.Equal(2, Store.List().Entries.Count);
    }

    [AvaloniaFact]
    public async Task ARequestDuringAPassRunsAgainWhenItEnds()
    {
        var scheduler = NewScheduler();
        var a = NewSubject();
        var b = NewSubject();
        Edit(a);
        Edit(b);
        scheduler.Track(a);
        scheduler.Track(b);
        _clock.Fire();

        // The pass has copied a and is on b (its lease is slow); a changes and a deactivation asks for a copy.
        await WindowFixture.WaitAsync(() => b.Taken == 1);
        Edit(a);
        scheduler.TickNow();
        await IdleAsync(scheduler);
        await IdleAsync(scheduler);

        Assert.Equal([2, 1], new[] { a.Taken, b.Taken });
        Assert.Equal(1, _maxConcurrent);
    }

    [AvaloniaFact]
    public async Task DeactivationWritesNow()
    {
        var scheduler = NewScheduler();
        var subject = NewSubject();
        Edit(subject);
        scheduler.Track(subject);

        scheduler.TickNow();
        await IdleAsync(scheduler);

        Assert.Single(Store.List().Entries);
    }

    [AvaloniaFact]
    public async Task DisabledSettingWritesNothing()
    {
        var on = false;
        var scheduler = NewScheduler(enabled: () => on);
        var subject = NewSubject();
        Edit(subject);
        scheduler.Track(subject);

        _clock.Fire();
        scheduler.TickNow();
        await IdleAsync(scheduler);
        Assert.Equal(0, subject.Taken);
        Assert.False(Directory.Exists(Store.Folder));

        on = true;
        _clock.Fire();
        await IdleAsync(scheduler);
        Assert.Equal(1, subject.Taken);
        Assert.Single(Store.List().Entries);
    }

    [AvaloniaFact]
    public async Task FailedSnapshotIsSilentAndRetriedNextTick()
    {
        // The folder cannot be made: a file stands where it should be.
        var blocked = Path.Combine(_root, "blocked");
        File.WriteAllText(blocked, "not a folder");
        var scheduler = NewScheduler(new RecoveryStore(blocked));
        var subject = NewSubject();
        Edit(subject);
        scheduler.Track(subject);

        _clock.Fire();
        await IdleAsync(scheduler);
        Assert.Equal(1, scheduler.FailureCount);
        Assert.Equal(0, subject.Taken);

        File.Delete(blocked);
        _clock.Fire();
        await IdleAsync(scheduler);

        Assert.Equal(1, scheduler.FailureCount);
        Assert.Equal(1, subject.Taken);
        Assert.Single(new RecoveryStore(blocked).List().Entries);
    }

    [AvaloniaFact]
    public async Task ACleanTabsCopyIsDeleted()
    {
        var scheduler = NewScheduler();
        var subject = NewSubject();
        var edits = subject.Docx.Model.Edits;
        Edit(subject);
        scheduler.Track(subject);
        _clock.Fire();
        await IdleAsync(scheduler);
        Assert.Single(Store.List().Entries);

        // Saved: the tab is clean and its copy goes.
        edits.MarkSaved();
        scheduler.Cleaned(subject);
        Assert.Empty(Store.List().Entries);
        Assert.False(File.Exists(Store.DocxPathOf(subject.Id)));

        // Dirty again, copied again, then undone to the saved revision: the tick deletes it.
        Edit(subject);
        _clock.Fire();
        await IdleAsync(scheduler);
        Assert.Single(Store.List().Entries);
        edits.Restore(edits.SavedRevision);
        Assert.False(subject.IsDirty);
        _clock.Fire();
        await IdleAsync(scheduler);
        Assert.Empty(Store.List().Entries);
    }

    [AvaloniaFact]
    public async Task ReleaseDeletesTheCopyButADisposeDoesNot()
    {
        var disposed = NewScheduler();
        var kept = NewSubject();
        Edit(kept);
        disposed.Track(kept);
        _clock.Fire();
        await IdleAsync(disposed);
        disposed.Dispose();
        Assert.True(_clock.Stopped);
        Assert.Single(Store.List().Entries);

        var released = new AutosaveScheduler(Store, new ManualRecoveryClock(), () => true);
        _schedulers.Add(released);
        var adopted = NewSubject();
        Edit(adopted);
        released.Track(adopted);
        released.TickNow();
        await IdleAsync(released);
        Assert.Equal(2, Store.List().Entries.Count);

        released.Release(adopted);
        var left = Assert.Single(Store.List().Entries);
        Assert.Equal(kept.Id, left.Id);
    }

    [AvaloniaFact]
    public async Task AReleaseDuringASnapshotLeavesNoCopy()
    {
        var scheduler = NewScheduler();
        var subject = NewSubject();
        Edit(subject);
        scheduler.Track(subject);
        _clock.Fire();

        // The snapshot is running (the lease is slow): Release now, then let it end.
        Assert.True(RecoverySnapshot.IsRunning(subject.Docx));
        scheduler.Release(subject);
        await IdleAsync(scheduler);

        Assert.Empty(Store.List().Entries);
        Assert.False(File.Exists(Store.DocxPathOf(subject.Id)));
    }

    [AvaloniaFact]
    public async Task AnAdoptedCopyIsReplacedAndReleasedWithItsId()
    {
        var scheduler = NewScheduler();
        var subject = NewSubject();
        Edit(subject);
        scheduler.Adopt(subject, subject.Id);
        Assert.Equal(1, _clock.Started);
        Assert.Throws<ArgumentException>(() => scheduler.Adopt(subject, RecoveryStore.NewId()));

        scheduler.TickNow();
        await IdleAsync(scheduler);
        Assert.Equal(subject.Id, Assert.Single(Store.List().Entries).Id);

        scheduler.Release(subject);
        Assert.Empty(Store.List().Entries);
    }

    [AvaloniaFact]
    public async Task TheFolderIsTheStoresAndNeverTheUsers()
    {
        var folder = Path.Combine(_root, "somewhere", "recovery");
        var scheduler = NewScheduler(new RecoveryStore(folder));
        var subject = NewSubject();
        Edit(subject);
        scheduler.Track(subject);

        _clock.Fire();
        await IdleAsync(scheduler);

        Assert.True(File.Exists(Path.Combine(folder, subject.Id + ".docx")));
        Assert.False(Directory.Exists(Store.Folder));
    }

    [AvaloniaFact]
    public async Task ACleanAdoptedCopyStaysUntilItsTabHasBeenDirty()
    {
        // A copy of work that was lost in a crash: a first scheduler wrote it for a dirty tab.
        var subject = NewSubject();
        Edit(subject);
        var first = NewScheduler();
        first.Track(subject);
        first.TickNow();
        await IdleAsync(first);
        first.Dispose();
        Assert.Single(Store.List().Entries);

        // The restored tab is adopted while it is clean (for any reason): the only copy stays.
        var edits = subject.Docx.Model.Edits;
        edits.MarkSaved();
        var second = new AutosaveScheduler(Store, new ManualRecoveryClock(), () => true);
        _schedulers.Add(second);
        second.Adopt(subject, subject.Id);
        second.TickNow();
        await IdleAsync(second);
        second.Cleaned(subject);
        Assert.Single(Store.List().Entries);

        // Seen dirty, copied, and clean again: now it goes.
        Edit(subject);
        second.TickNow();
        await IdleAsync(second);
        edits.MarkSaved();
        second.TickNow();
        await IdleAsync(second);
        Assert.Empty(Store.List().Entries);
    }
}
