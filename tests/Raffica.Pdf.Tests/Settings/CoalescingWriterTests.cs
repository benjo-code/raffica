using Raffica.Pdf.Settings;
using Xunit;

namespace Raffica.Pdf.Tests.Settings;

public sealed class CoalescingWriterTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task PostReturnsWhileTheWriteIsStillPendingAndFlushWaitsForIt()
    {
        var writer = new CoalescingWriter();
        var gate = new ManualResetEventSlim();
        var started = new ManualResetEventSlim();
        var done = false;

        // The write blocks on the gate. If Post performed it on the caller, Post would not return before the gate opens (the gate is
        // opened only below), so reaching the next line proves the caller never waits for the disk. Which thread runs the write is not
        // part of the contract.
        writer.Post(() =>
        {
            started.Set();
            gate.Wait(Limit, TestContext.Current.CancellationToken);
            done = true;
        });

        Assert.False(done);
        Assert.True(started.Wait(Limit, TestContext.Current.CancellationToken));
        var flush = writer.FlushAsync();
        Assert.False(flush.IsCompleted);

        gate.Set();
        await flush.WaitAsync(Limit, TestContext.Current.CancellationToken);
        Assert.True(done);
        Assert.True(writer.FlushAsync().IsCompleted);
    }

    [Fact]
    public async Task TheNewestWriteWinsAndTwoWritesNeverOverlap()
    {
        var writer = new CoalescingWriter();
        var gate = new ManualResetEventSlim();
        var started = new ManualResetEventSlim();
        var running = 0;
        var overlapped = false;
        var written = new List<int>();

        void Write(int value)
        {
            if (Interlocked.Increment(ref running) > 1)
            {
                overlapped = true;
            }

            started.Set();
            gate.Wait(Limit, TestContext.Current.CancellationToken);
            written.Add(value);
            Interlocked.Decrement(ref running);
        }

        // The first write holds the writer; the ones behind it replace each other.
        writer.Post(() => Write(0));
        Assert.True(started.Wait(Limit, TestContext.Current.CancellationToken));
        for (var i = 1; i <= 50; i++)
        {
            var value = i;
            writer.Post(() => Write(value));
        }

        gate.Set();
        await writer.FlushAsync().WaitAsync(Limit, TestContext.Current.CancellationToken);

        Assert.False(overlapped);
        Assert.Equal([0, 50], written);
    }

    [Fact]
    public async Task AFailingWriteIsSilentAndTheNextOneStillRuns()
    {
        var writer = new CoalescingWriter();
        var gate = new ManualResetEventSlim();
        var started = new ManualResetEventSlim();
        var done = false;

        writer.Post(() =>
        {
            started.Set();
            gate.Wait(Limit, TestContext.Current.CancellationToken);
            throw new IOException("disk full");
        });
        Assert.True(started.Wait(Limit, TestContext.Current.CancellationToken));
        writer.Post(() => done = true);
        gate.Set();
        await writer.FlushAsync().WaitAsync(Limit, TestContext.Current.CancellationToken);

        Assert.True(done);
        Assert.True(writer.Flush(Limit));
    }

    [Fact]
    public void FlushWithNothingPostedReturnsAtOnceAndAShortTimeoutGivesUp()
    {
        var writer = new CoalescingWriter();
        Assert.True(writer.Flush(TimeSpan.Zero));

        var gate = new ManualResetEventSlim();
        writer.Post(() => gate.Wait(Limit, TestContext.Current.CancellationToken));
        Assert.False(writer.Flush(TimeSpan.FromMilliseconds(20)));
        gate.Set();
        Assert.True(writer.Flush(Limit));
    }
}
