using Raffica.Pdf.Settings;
using Xunit;

namespace Raffica.Pdf.Tests.Settings;

public sealed class CoalescingWriterTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task AWriteRunsOnAnotherThreadAndFlushWaitsForIt()
    {
        var writer = new CoalescingWriter();
        var caller = Environment.CurrentManagedThreadId;
        var ranOn = -1;

        writer.Post(() =>
        {
            Thread.Sleep(50);
            ranOn = Environment.CurrentManagedThreadId;
        });
        await writer.FlushAsync().WaitAsync(Limit, TestContext.Current.CancellationToken);

        Assert.NotEqual(-1, ranOn);
        Assert.NotEqual(caller, ranOn);
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
