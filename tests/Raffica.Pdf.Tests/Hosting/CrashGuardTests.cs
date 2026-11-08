using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Raffica.Pdf.Hosting;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Hosting;

public sealed class CrashGuardTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-crash-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset Mono { get; set; } = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => Now;

        public override long GetTimestamp() => Mono.Ticks;

        // Time moves on for both clocks; the wall clock alone may jump (see TheWindowOfTenSecondsUsesTheMonotonicClock).
        public void Advance(TimeSpan span)
        {
            Now += span;
            Mono += span;
        }
    }

    private sealed class Probe
    {
        public int Cards { get; set; }

        public int Exits { get; set; }
    }

    private CrashGuard Make(Probe probe, Clock? clock = null) =>
        new(_folder, clock ?? new Clock(), _ => probe.Cards++, () => probe.Exits++);

    private string Log => Path.Combine(_folder, "crash.log");

    private static InvalidOperationException Throw(string message, Exception? inner = null)
    {
        try
        {
            throw new InvalidOperationException(message, inner);
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    [Fact]
    public void AUiExceptionIsLoggedWithoutPathsAndShowsOneCard()
    {
        var probe = new Probe();
        var guard = Make(probe);
        var secret = Throw(@"fail at 'C:\Users\Jane Doe\Secret\tax.pdf' and /home/jane/tax.pdf and \\server\share\a.pdf and /Users/jane/b.pdf end",
            new IOException(@"Could not find file 'D:\docs\my report.pdf'."));

        guard.OnUiException(secret, out var handled);

        Assert.True(handled);
        Assert.Equal(1, probe.Cards);
        var text = File.ReadAllText(Log);
        Assert.Contains("2026-11-09", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException", text, StringComparison.Ordinal);
        Assert.Contains("System.IO.IOException", text, StringComparison.Ordinal);
        Assert.Contains("CrashGuardTests", text, StringComparison.Ordinal);
        foreach (var leak in new[] { "Jane", "Secret", "tax.pdf", "jane", "server", "share", "my report", "docs", "a.pdf", "b.pdf" })
        {
            Assert.DoesNotContain(leak, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheFourthUiExceptionWithinTenSecondsEndsTheApp()
    {
        var probe = new Probe();
        var clock = new Clock();
        var guard = Make(probe, clock);

        for (var i = 0; i < 3; i++)
        {
            guard.OnUiException(Throw("x"), out var handled);
            guard.CardClosed();
            Assert.True(handled);
            clock.Advance(TimeSpan.FromSeconds(2));
        }

        Assert.Equal(0, probe.Exits);
        Assert.Equal(3, probe.Cards);

        guard.OnUiException(Throw("fourth"), out _);
        guard.OnUiException(Throw("fifth"), out var handledAfterExit);

        Assert.Equal(1, probe.Exits);
        Assert.Equal(3, probe.Cards);
        Assert.True(handledAfterExit);
        guard.CardClosed();
        guard.OnUiException(Throw("sixth"), out _);
        Assert.Equal(3, probe.Cards);
        Assert.Contains("fourth", File.ReadAllText(Log), StringComparison.Ordinal);
    }

    [Fact]
    public void ExceptionsSpreadOverTimeNeverEndTheApp()
    {
        var probe = new Probe();
        var clock = new Clock();
        var guard = Make(probe, clock);

        for (var i = 0; i < 8; i++)
        {
            guard.OnUiException(Throw("x"), out _);
            guard.CardClosed();
            clock.Advance(TimeSpan.FromSeconds(4));
        }

        Assert.Equal(0, probe.Exits);
        Assert.Equal(8, probe.Cards);
    }

    [Fact]
    public void RepeatsWhileACardShowsAreOnlyLoggedAndANewCardComesAfterTheDismiss()
    {
        var probe = new Probe();
        var clock = new Clock();
        var guard = Make(probe, clock);

        guard.OnUiException(Throw("first"), out _);
        clock.Advance(TimeSpan.FromSeconds(1));
        guard.OnUiException(Throw("second"), out var handled);

        Assert.True(handled);
        Assert.Equal(1, probe.Cards);
        Assert.Contains("second", File.ReadAllText(Log), StringComparison.Ordinal);

        guard.CardClosed();
        clock.Advance(TimeSpan.FromSeconds(20));
        guard.OnUiException(Throw("third"), out _);

        Assert.Equal(2, probe.Cards);
    }

    [Fact]
    public void ACardThatCannotBeShownDoesNotBlockTheNextOne()
    {
        var shown = 0;
        var fail = true;
        var guard = new CrashGuard(_folder, new Clock(), _ => { if (fail) { throw new InvalidOperationException("no host"); } shown++; }, () => { });
        guard.OnUiException(Throw("a"), out var handled);
        Assert.True(handled);

        fail = false;
        guard.OnUiException(Throw("b"), out _);
        Assert.Equal(1, shown);
    }

    [Fact]
    public void TheWindowOfTenSecondsUsesTheMonotonicClock()
    {
        var probe = new Probe();
        var clock = new Clock();
        var guard = Make(probe, clock);

        for (var i = 0; i < 3; i++)
        {
            guard.OnUiException(Throw("x"), out _);
            guard.CardClosed();

            // The wall clock jumps an hour forward each time; the monotonic clock moves one second.
            clock.Now += TimeSpan.FromHours(1);
            clock.Mono += TimeSpan.FromSeconds(1);
        }

        guard.OnUiException(Throw("fourth"), out _);

        Assert.Equal(1, probe.Exits);
    }

    [Theory]
    [InlineData(@"gone: 'C:\Users\Jane\O'Brien\secret.pdf' end", "Brien")]
    [InlineData(@"gone: 'C:\Users\Jane\O'Brien\secret.pdf' end", "secret")]
    [InlineData("see ~/Documents/secret.pdf now", "secret")]
    [InlineData(@"see ..\..\secret.pdf now", "secret")]
    [InlineData("see ../../secret.pdf now", "secret")]
    [InlineData("in /opt/app/secret.pdf", "secret")]
    [InlineData("in /srv/x/secret.pdf", "secret")]
    [InlineData("in /data/x/secret.pdf", "secret")]
    [InlineData("in /mnt/x/secret.pdf", "secret")]
    [InlineData("in /media/x/secret.pdf", "secret")]
    [InlineData("in /var/x/secret.pdf", "secret")]
    [InlineData("in /tmp/x/secret.pdf", "secret")]
    [InlineData("in /root/secret.pdf", "secret")]
    [InlineData(@"in \\host\share\secret.pdf", "secret")]
    public void PathsAreScrubbed(string message, string leak)
    {
        Assert.DoesNotContain(leak, CrashGuard.Scrub(message), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fetch http://host/x/y failed")]
    [InlineData("resource avares://RafficaPdf/Styles/a.axaml missing")]
    [InlineData("Page 3 of 5: file")]
    public void AddressesAndPlainTextAreNotMangled(string message)
    {
        Assert.Equal(message, CrashGuard.Scrub(message));
    }

    [Fact]
    public void ALongMessageIsCutAndOneEntryNeverPassesTheCap()
    {
        var probe = new Probe();
        var guard = Make(probe);

        guard.OnBackgroundException(Throw(new string('m', 2_000_000)));

        var text = File.ReadAllText(Log);
        Assert.True(text.Length < 70_000, "length " + text.Length);
        Assert.Contains("truncated", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ABackgroundExceptionIsLoggedWithoutACard()
    {
        var probe = new Probe();
        var guard = Make(probe);

        guard.OnBackgroundException(Throw("from a pool thread"));

        Assert.Equal(0, probe.Cards);
        Assert.Equal(0, probe.Exits);
        Assert.Contains("from a pool thread", File.ReadAllText(Log), StringComparison.Ordinal);
    }

    [Fact]
    public void OutOfMemoryIsLoggedButNotHandled()
    {
        var probe = new Probe();
        var guard = Make(probe);

#pragma warning disable CA2201 // the guard must treat the reserved type specially
        guard.OnUiException(new OutOfMemoryException("no memory"), out var handled);
#pragma warning restore CA2201

        Assert.False(handled);
        Assert.Equal(0, probe.Cards);
        Assert.Contains("OutOfMemoryException", File.ReadAllText(Log), StringComparison.Ordinal);
    }

    [Fact]
    public void NothingIsCreatedBeforeTheFirstException()
    {
        _ = Make(new Probe());

        Assert.False(File.Exists(Log));
    }

    [Fact]
    public void TheLogIsCappedAtOneMebibyteAndKeepsTheNewestEntries()
    {
        var probe = new Probe();
        var clock = new Clock();
        var guard = Make(probe, clock);
        var big = new string('x', 20_000);

        for (var i = 0; i < 300; i++)
        {
            guard.OnBackgroundException(Throw("entry-" + i + " " + big));
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        var length = new FileInfo(Log).Length;
        Assert.True(length <= 1024 * 1024, "length " + length);
        var text = File.ReadAllText(Log);
        Assert.Contains("entry-299", text, StringComparison.Ordinal);
        Assert.DoesNotContain("entry-0 ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailingLogFolderNeverThrowsOutOfTheGuard()
    {
        var blocker = Path.Combine(_folder, "file");
        File.WriteAllText(blocker, "x");
        var probe = new Probe();
        var guard = new CrashGuard(Path.Combine(blocker, "sub"), new Clock(), _ => probe.Cards++, () => probe.Exits++);

        guard.OnUiException(Throw("x"), out var handled);
        guard.OnBackgroundException(Throw("y"));

        Assert.True(handled);
        Assert.Equal(1, probe.Cards);
    }

    [Fact]
    public void TheFolderLauncherGetsOnlyTheDirectoryAsOneArgument()
    {
        Assert.Null(FolderLauncher.CreateStartInfo(Path.Combine(_folder, "missing")));
        var info = FolderLauncher.CreateStartInfo(_folder);

        Assert.NotNull(info);
        Assert.False(info.UseShellExecute);
        Assert.True(!OperatingSystem.IsWindows() || Path.IsPathRooted(info.FileName));
        Assert.Equal([Path.GetFullPath(_folder)], info.ArgumentList);
    }

    [AvaloniaFact]
    public async Task AFailingUiThreadDelegateLeavesTheWindowOpenTheOtherTabDirtyAndShowsOneCard()
    {
        using var window = WindowFixture.Create();
        var dirty = await window.OpenAsync("hello_world.pdf");
        dirty.MarkDirty();
        await window.OpenAsync("outline.pdf");
        var probe = new Probe();
        var ui = Dispatcher.UIThread;
        CrashGuard? guard = null;
        guard = new CrashGuard(
            _folder,
            new Clock(),
            _ =>
            {
                probe.Cards++;
                ui.Post(() => window.Window.ShowCrashCard(_folder, _ => { }, () => guard!.CardClosed()));
            },
            () => probe.Exits++);
        using var attached = CrashGuardHost.AttachUi(guard, ui);

        // The path a ribbon click takes: a delegate the dispatcher runs. No ribbon command throws on demand, so the delegate fails by itself.
        ui.Post(() => throw new InvalidOperationException(@"command failed for C:\Users\x\y.pdf"));
        ui.Post(() => throw new InvalidOperationException("second failure"));
        ViewFixture.Pump();

        Assert.True(window.Window.IsVisible);
        Assert.True(dirty.IsDirty);
        Assert.True(window.Window.Host.IsOpen);
        Assert.Equal(1, probe.Cards);
        Assert.Equal(2, window.Window.Workspace.Tabs.Count);
        var log = File.ReadAllText(Log);
        Assert.DoesNotContain("y.pdf", log, StringComparison.Ordinal);
        Assert.Contains("second failure", log, StringComparison.Ordinal);
        Assert.Equal(0, probe.Exits);
    }
}
