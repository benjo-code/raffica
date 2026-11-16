using System.Globalization;
using Avalonia.Headless.XUnit;
using Raffica.Hosting;
using Xunit;

namespace Raffica.WriteApp.Tests;

public sealed class MeasurementTests
{
    private static readonly string[] s_switches = ["--measure-startup", "--measure-second", "--measure-scroll", "--measure-memory", "--measure-typing", "--measure-typing-background"];

    /// <summary>Collects the lines and the exit codes of a <see cref="Measurement"/>.</summary>
    private sealed class Probe
    {
        public StringWriter Output { get; } = new() { NewLine = "\n" };

        public List<int> Exits { get; } = [];

        public string[] Lines => Output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static Measurement Create(WindowFixture f, Probe probe, string option, int scrollSteps = 600, int typingKeys = Measurement.TypingKeys)
    {
        var options = WriteOptions.Parse([option, "--settings-file", Path.Combine(f.Folder, "m.json"), WindowFixture.Thesis]);
        var measurement = new Measurement(options, f.Window, probe.Output, probe.Exits.Add, Timeout.InfiniteTimeSpan, scrollSteps, typingKeys: typingKeys);
        measurement.Start();
        return measurement;
    }

    [AvaloniaFact]
    public async Task FirstPageLineIsWrittenOnceAfterTheFirstSharpPage()
    {
        using var f = new WindowFixture();
        f.Show();
        var probe = new Probe();
        using var measurement = Create(f, probe, "--measure-startup");

        Assert.Empty(probe.Lines);
        await f.Window.OpenFilesAsync([WindowFixture.Thesis]);
        await WindowFixture.WaitAsync(() => probe.Exits.Count == 1);

        // A further file with its own first sharp page must not write a second line.
        await f.Window.OpenFilesAsync([WindowFixture.Poi]);
        await Task.Delay(300);
        WindowFixture.Pump();

        Assert.Equal(["first-page"], probe.Lines);
        Assert.Equal([0], probe.Exits);
    }

    [AvaloniaFact]
    public async Task TheSecondFileIsTimedFromTheArrivalOfTheHandover()
    {
        using var f = new WindowFixture();
        f.Show();
        var probe = new Probe();
        using var measurement = Create(f, probe, "--measure-second");

        await f.Window.OpenFilesAsync([WindowFixture.Thesis]);
        await WindowFixture.WaitAsync(() => probe.Lines.Contains("first-page"));
        Assert.Empty(probe.Exits);

        f.Window.OnHandover([WindowFixture.Poi]);
        await WindowFixture.WaitAsync(() => probe.Exits.Count == 1);

        Assert.Equal("first-page", probe.Lines[0]);
        Assert.Matches(@"^second-page ms=\d+\.\d\d$", probe.Lines[1]);
        Assert.Equal(2, probe.Lines.Length);
    }

    [AvaloniaFact]
    public async Task TheScrollRunWritesOneLineAndEnds()
    {
        using var f = new WindowFixture();
        f.Show();
        var probe = new Probe();
        using var measurement = Create(f, probe, "--measure-scroll", scrollSteps: 12);

        await f.Window.OpenFilesAsync([WindowFixture.Thesis]);
        await WindowFixture.WaitAsync(() => probe.Exits.Count == 1);

        Assert.Equal("first-page", probe.Lines[0]);
        Assert.Matches(@"^scroll p99-ms=\d+\.\d\d max-ms=\d+\.\d\d$", probe.Lines[1]);
        Assert.Equal(2, probe.Lines.Length);
        Assert.Equal([0], probe.Exits);
    }

    [AvaloniaFact]
    public async Task TheMemoryRunWritesTheBytesTwoSecondsAfterTheFirstPage()
    {
        using var f = new WindowFixture();
        f.Show();
        var probe = new Probe();
        using var measurement = Create(f, probe, "--measure-memory");

        await f.Window.OpenFilesAsync([WindowFixture.Thesis]);
        await WindowFixture.WaitAsync(() => probe.Lines.Contains("first-page"));
        var seen = System.Diagnostics.Stopwatch.StartNew();
        await WindowFixture.WaitAsync(() => probe.Exits.Count == 1);

        Assert.True(seen.Elapsed >= TimeSpan.FromSeconds(1.5), "the memory line must wait about two seconds");
        Assert.Equal("first-page", probe.Lines[0]);
        Assert.Matches(@"^memory-bytes [1-9]\d+$", probe.Lines[1]);
        Assert.Equal(2, probe.Lines.Length);
    }

    /// <summary>A writer that does something when a line is written (here: the document goes away between the first page and the first scroll step).</summary>
    private sealed class HookedWriter(Action<string> onLine) : StringWriter
    {
        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            if (value is not null)
            {
                onLine(value);
            }
        }
    }

    [AvaloniaFact]
    public async Task AScrollRunWithoutADocumentFailsInsteadOfWritingAPerfectResult()
    {
        using var f = new WindowFixture();
        f.Show();
        var exits = new List<int>();
        var error = new StringWriter();
        var output = new HookedWriter(line =>
        {
            if (line == "first-page")
            {
                // The tab closes before the first scroll step (a clean tab closes at once).
                f.Window.Workspace.Close(f.Window.Workspace.Active!);
            }
        });
        var options = WriteOptions.Parse(["--measure-scroll", "--settings-file", Path.Combine(f.Folder, "m.json"), WindowFixture.Thesis]);
        using var measurement = new Measurement(options, f.Window, output, exits.Add, Timeout.InfiniteTimeSpan, 12, error);
        measurement.Start();

        await f.Window.OpenFilesAsync([WindowFixture.Thesis]);
        await WindowFixture.WaitAsync(() => exits.Count == 1);

        Assert.Equal([Measurement.Failed], exits);
        Assert.DoesNotContain("scroll", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("error:", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("no document", error.ToString(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void TheWatchdogEndsAStuckRunWithExitCode3AndNamesTheTimeout()
    {
        using var f = new WindowFixture();
        f.Show();
        var exits = new List<int>();
        var error = new StringWriter();
        var options = WriteOptions.Parse(["--measure-startup", "--settings-file", Path.Combine(f.Folder, "m.json"), WindowFixture.Thesis]);
        using var measurement = new Measurement(options, f.Window, new StringWriter(), _ => { }, TimeSpan.FromMilliseconds(100), error: error, exitProcess: exits.Add);
        measurement.Start();

        // No file is ever opened, so no first page: the timer thread ends the run.
        SpinWait.SpinUntil(() => exits.Count > 0, TimeSpan.FromSeconds(10));

        Assert.Equal([Measurement.TimedOut], exits);
        Assert.Equal(3, Measurement.TimedOut);
        Assert.Contains("no result within 0.1 s", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TypingMeasurementNeedsSettingsFileAndFile()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-write-measure-typing-").FullName;
        try
        {
            var ex = Assert.Throws<ArgumentException>(() => WriteOptions.Parse(["--measure-typing", WindowFixture.Thesis]));
            Assert.Contains("--settings-file", ex.Message, StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() => WriteOptions.Parse(["--measure-typing", "--settings-file", Path.Combine(folder, "s.json")]));
            Assert.Throws<ArgumentException>(() => WriteOptions.Parse(["--measure-typing", "--measure-scroll", "--settings-file", Path.Combine(folder, "s.json"), "a.docx"]));

            var options = WriteOptions.Parse(["--measure-typing", "--settings-file", Path.Combine(folder, "s.json"), WindowFixture.Thesis]);
            Assert.True(options.IsMeasuring);
            Assert.Equal("--measure-typing", options.MeasureSwitch);
            Assert.Equal(folder, Path.GetDirectoryName(AppFiles.Write.RecentPathFor(options.SettingsFile)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task TypingMeasurementWritesOneRowPerCase()
    {
        using var f = new WindowFixture();
        f.Show();
        var probe = new Probe();
        using var measurement = Create(f, probe, "--measure-typing", typingKeys: 3);

        await f.Window.OpenFilesAsync([WindowFixture.Thesis]);
        for (var i = 0; i < 4800 && probe.Exits.Count == 0; i++)
        {
            await Task.Delay(25);
            WindowFixture.Pump();
        }

        Assert.Equal([0], probe.Exits);
        var lines = probe.Lines;
        Assert.Equal("first-page", lines[0]);

        // Two first-edit rows (no limit), then one row per case and view.
        var first = lines.Where(l => l.StartsWith("typing-first ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, first.Length);
        Assert.Matches(@"^typing-first when=after-open view=page ms=\d+\.\d\d$", first[0]);
        Assert.Matches(@"^typing-first when=after-switch view=writing ms=\d+\.\d\d$", first[1]);

        var rows = lines.Where(l => l.StartsWith("typing case=", StringComparison.Ordinal)).ToArray();
        string[] cases = ["middle", "page-break", "section-break", "chapter-end", "table-cell", "footnote", "header", "footer"];
        Assert.Equal(cases.Length * 2, rows.Length);
        var index = 0;
        foreach (var view in new[] { "page", "writing" })
        {
            foreach (var name in cases)
            {
                Assert.Matches($@"^typing case={name} view={view} n=3 median-ms=\d+\.\d\d p95-ms=\d+\.\d\d max-ms=\d+\.\d\d handler-median-ms=\d+\.\d\d frame-median-ms=\d+\.\d\d frame-p95-ms=\d+\.\d\d$", rows[index++]);
            }
        }
    }

    [AvaloniaFact]
    public async Task TypingBackgroundMeasurementWritesOneRowWithTheLeaseWait()
    {
        using var f = new WindowFixture();
        f.Show();
        var probe = new Probe();
        using var measurement = Create(f, probe, "--measure-typing-background", typingKeys: 6);

        await f.Window.OpenFilesAsync([WindowFixture.Thesis]);
        for (var i = 0; i < 2400 && probe.Exits.Count == 0; i++)
        {
            await Task.Delay(25);
            WindowFixture.Pump();
        }

        Assert.Equal([0], probe.Exits);
        Assert.Equal("first-page", probe.Lines[0]);
        Assert.Equal(2, probe.Lines.Length);
        Assert.Matches(@"^typing-background n=6 incomplete=\d+ median-ms=\d+\.\d\d p95-ms=\d+\.\d\d max-ms=\d+\.\d\d lease-median-ms=\d+\.\d\d lease-p95-ms=\d+\.\d\d lease-max-ms=\d+\.\d\d incomplete-median-ms=\d+\.\d\d incomplete-max-ms=\d+\.\d\d$", probe.Lines[1]);
    }

    [Fact]
    public void TypingBackgroundLineSeparatesTheLeaseAndTheKeysThatStartedIncomplete()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal(
                "typing-background n=4 incomplete=2 median-ms=2.50 p95-ms=9.00 max-ms=9.00 lease-median-ms=0.75 lease-p95-ms=4.00 lease-max-ms=4.00 incomplete-median-ms=6.50 incomplete-max-ms=9.00",
                Measurement.TypingBackgroundLine([1.0, 2.0, 3.0, 9.0], [0.5, 0.5, 1.0, 4.0], [4.0, 9.0]));
            Assert.Equal(
                "typing-background n=1 incomplete=0 median-ms=1.00 p95-ms=1.00 max-ms=1.00 lease-median-ms=0.00 lease-p95-ms=0.00 lease-max-ms=0.00 incomplete-median-ms=0.00 incomplete-max-ms=0.00",
                Measurement.TypingBackgroundLine([1.0], [0.0], []));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void TypingLineUsesTheInvariantCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("typing case=middle view=page n=5 median-ms=2.00 p95-ms=4.00 max-ms=4.00", Measurement.TypingLine("middle", "page", [1.0, 1.5, 2.0, 3.0, 4.0]));
            Assert.Equal("typing-first when=after-open view=page ms=12.35", Measurement.TypingFirstLine("after-open", "page", 12.3456));
            Assert.Equal(
                "typing case=header view=writing n=3 median-ms=2.00 p95-ms=3.00 max-ms=3.00 handler-median-ms=0.50 frame-median-ms=17.00 frame-p95-ms=18.00",
                Measurement.TypingLine("header", "writing", [1.0, 2.0, 3.0], [0.5, 0.5, 0.5], [16.0, 17.0, 18.0]));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void ScrollLineHasP99AndMax()
    {
        var gaps = Enumerable.Range(1, 100).Select(i => (double)i).Reverse().ToArray();

        // Nearest rank: ceil(0.99 x 100) = 99th value of the sorted gaps.
        Assert.Equal("scroll p99-ms=99.00 max-ms=100.00", Measurement.ScrollLine(gaps));
        Assert.Equal("scroll p99-ms=16.70 max-ms=16.70", Measurement.ScrollLine([16.7]));
    }

    [Fact]
    public void ResultLinesUseTheInvariantCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("scroll p99-ms=12.50 max-ms=40.25", Measurement.ScrollLine([.. Enumerable.Repeat(1.0, 98), 12.5, 40.25]));
            Assert.Equal("second-page ms=1234.57", Measurement.SecondLine(1234.5678));
            Assert.Equal("memory-bytes 123456789", Measurement.MemoryLine(123456789));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void MeasurementNeverReadsOrWritesTheUsersFiles()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-write-measure-options-").FullName;
        try
        {
            foreach (var name in s_switches)
            {
                // A measurement without its own settings file would meet the user's settings, recent files and instance pipe.
                var ex = Assert.Throws<ArgumentException>(() => WriteOptions.Parse([name, WindowFixture.Thesis]));
                Assert.Contains("--settings-file", ex.Message, StringComparison.Ordinal);

                var options = WriteOptions.Parse([name, "--settings-file", Path.Combine(folder, "s.json"), WindowFixture.Thesis]);
                Assert.True(options.IsMeasuring);
                Assert.Equal(Path.Combine(folder, "s.json"), AppFiles.Write.ShellPathFor(options.SettingsFile));
                Assert.Equal(folder, Path.GetDirectoryName(AppFiles.Write.AppSettingsPathFor(options.SettingsFile)));
                Assert.Equal(folder, Path.GetDirectoryName(AppFiles.Write.RecentPathFor(options.SettingsFile)));
            }

            // One measure switch per run, and a file to open.
            Assert.Throws<ArgumentException>(() => WriteOptions.Parse(["--measure-startup", "--measure-memory", "--settings-file", Path.Combine(folder, "s.json"), "a.docx"]));
            Assert.Throws<ArgumentException>(() => WriteOptions.Parse(["--measure-startup", "--settings-file", Path.Combine(folder, "s.json")]));
            Assert.False(WriteOptions.Parse(["a.docx"]).IsMeasuring);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
