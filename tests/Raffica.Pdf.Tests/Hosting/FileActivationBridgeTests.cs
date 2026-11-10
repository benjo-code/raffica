using Raffica.Pdf.Hosting;
using Xunit;

namespace Raffica.Pdf.Tests.Hosting;

public sealed class FileActivationBridgeTests
{
    private sealed class FakeSource : IFileActivationSource
    {
        public event Action<IReadOnlyList<string>>? FilesActivated;

        public int Subscribers => FilesActivated?.GetInvocationList().Length ?? 0;

        public void Raise(params string[] paths) => FilesActivated?.Invoke(paths);
    }

    [Fact]
    public void BatchBeforeAttachIsDeliveredOnAttach()
    {
        var source = new FakeSource();
        using var bridge = new FileActivationBridge(source);
        source.Raise("a.pdf");
        var got = new List<string[]>();
        bridge.Attach(b => got.Add([.. b]));
        Assert.Single(got);
        Assert.Equal(["a.pdf"], got[0]);
    }

    [Fact]
    public void OrderIsKept()
    {
        var source = new FakeSource();
        using var bridge = new FileActivationBridge(source);
        source.Raise("a.pdf");
        source.Raise("b.pdf", "c.pdf");
        var got = new List<string>();
        bridge.Attach(b => got.AddRange(b));
        Assert.Equal(["a.pdf", "b.pdf", "c.pdf"], got);
    }

    [Fact]
    public void BatchAfterAttachIsForwardedDirectly()
    {
        var source = new FakeSource();
        using var bridge = new FileActivationBridge(source);
        var got = new List<string>();
        bridge.Attach(b => got.AddRange(b));
        Assert.Empty(got);
        source.Raise("late.pdf");
        Assert.Equal(["late.pdf"], got);
    }

    [Fact]
    public void NothingIsDeliveredAfterDispose()
    {
        var source = new FakeSource();
        var bridge = new FileActivationBridge(source);
        var got = new List<string>();
        bridge.Attach(b => got.AddRange(b));
        bridge.Dispose();
        source.Raise("x.pdf");
        Assert.Empty(got);
        Assert.Equal(0, source.Subscribers);
    }

    [Fact]
    public void SecondAttachThrows()
    {
        using var bridge = new FileActivationBridge(new FakeSource());
        bridge.Attach(_ => { });
        Assert.Throws<InvalidOperationException>(() => bridge.Attach(_ => { }));
    }

    [Fact]
    public async Task ConcurrentBatchesAreNeitherLostNorDuplicated()
    {
        var source = new FakeSource();
        using var bridge = new FileActivationBridge(source);
        var seen = new System.Collections.Concurrent.ConcurrentBag<string>();
        var go = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            go.Wait();
            for (var i = 0; i < 50; i++)
            {
                source.Raise($"{t}-{i}.pdf");
            }
        })).ToArray();
        go.Set();
        await Task.Delay(1, TestContext.Current.CancellationToken);
        bridge.Attach(b =>
        {
            foreach (var p in b)
            {
                seen.Add(p);
            }
        });
        await Task.WhenAll(tasks);

        Assert.Equal(200, seen.Count);
        Assert.Equal(200, seen.Distinct().Count());
    }

    [Fact]
    public async Task SinkIsNotCalledUnderTheLock()
    {
        var source = new FakeSource();
        using var bridge = new FileActivationBridge(source);
        var got = new List<string>();
        var ct = TestContext.Current.CancellationToken;
        var done = Task.Run(() => bridge.Attach(b =>
        {
            got.AddRange(b);
            if (got.Count == 1)
            {
                // A batch raised from inside the sink, and one from another thread while the sink runs, must not deadlock.
                source.Raise("inner.pdf");
                Task.Run(() => source.Raise("other.pdf"), ct).GetAwaiter().GetResult();
            }
        }), ct);
        source.Raise("first.pdf");
        await done.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal(3, got.Count);
    }
}
