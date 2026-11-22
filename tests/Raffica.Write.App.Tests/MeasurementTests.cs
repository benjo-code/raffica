using System.Globalization;
using Avalonia.Headless.XUnit;
using Raffica.Hosting;
using Xunit;

namespace Raffica.WriteApp.Tests;

public sealed class MeasurementTests
{
    private static readonly string[] s_switches = ["--measure-startup", "--measure-second", "--measure-scroll", "--measure-memory", "--measure-typing", "--measure-typing-background", "--measure-typing-pane", "--measure-format", "--measure-structure", "--measure-typing-snapshot"];

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
                Assert.Matches($@"^typing case={name} view={view} n=3 median-ms=\d+\.\d\d p95-ms=\d+\.\d\d max-ms=\d+\.\d\d handler-median-ms=\d+\.\d\d frame-median-ms=\d+\.\d\d frame-p95-ms=\d+\.\d\d render-median-ms=\d+\.\d\d render-p95-ms=\d+\.\d\d draw-median-ms=\d+\.\d\d draw-p95-ms=\d+\.\d\d( ph-[a-z-]+-(median|p95)-ms=\d+\.\d\d){{32}} alloc-median-kb=\d+\.\d\d alloc-p95-kb=\d+\.\d\d gc0=\d+ gc1=\d+ gc2=\d+ gc-pause-ms=\d+\.\d\d$", rows[index++]);
            }
        }
    }

    [Fact]
    public void MeasureFormatNeedsASettingsFileAndAFile()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-write-measure-format-").FullName;
        try
        {
            var ex = Assert.Throws<ArgumentException>(() => WriteOptions.Parse(["--measure-format", WindowFixture.Thesis]));
            Assert.Contains("--settings-file", ex.Message, StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() => WriteOptions.Parse(["--measure-format", "--settings-file", Path.Combine(folder, "s.json")]));
            Assert.Throws<ArgumentException>(() => WriteOptions.Parse(["--measure-format", "--measure-typing", "--settings-file", Path.Combine(folder, "s.json"), "a.docx"]));

            var options = WriteOptions.Parse(["--measure-format", "--settings-file", Path.Combine(folder, "s.json"), WindowFixture.Thesis]);
            Assert.True(options.IsMeasuring);
            Assert.Equal("--measure-format", options.MeasureSwitch);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheBenchAndTheAppUseTheSamePlaces()
    {
        // The bench keeps the same two lists (EditCases, pinned by the same test name in Raffica.Write.Tests): change both together.
        Assert.Equal(["middle", "page-break", "section-break", "chapter-end", "table-cell", "footnote", "header", "footer"], TypingPlaces.Names);
        Assert.Equal(["format-word", "format-paragraph"], TypingPlaces.FormatNames);
    }

    [AvaloniaFact]
    public async Task FormatMeasurementWritesOneRowPerKindPlaceAndView()
    {
        using var f = new WindowFixture();
        f.Show();
        var probe = new Probe();
        using var measurement = Create(f, probe, "--measure-format", typingKeys: 3);

        await f.Window.OpenFilesAsync([WindowFixture.Thesis]);
        for (var i = 0; i < 4800 && probe.Exits.Count == 0; i++)
        {
            await Task.Delay(25);
            WindowFixture.Pump();
        }

        Assert.Equal([0], probe.Exits);
        var rows = probe.Lines.Where(l => l.StartsWith("format case=", StringComparison.Ordinal)).ToArray();
        string[] places = ["middle", "page-break", "section-break", "chapter-end", "table-cell", "footnote", "header", "footer"];
        Assert.Equal(2 * 2 * places.Length, rows.Length);
        var index = 0;
        foreach (var view in new[] { "page", "writing" })
        {
            foreach (var kind in new[] { "format-word", "format-paragraph" })
            {
                foreach (var place in places)
                {
                    Assert.Matches($@"^format case={kind}/{place} view={view} n=3 median-ms=\d+\.\d\d p95-ms=\d+\.\d\d max-ms=\d+\.\d\d handler-median-ms=\d+\.\d\d frame-median-ms=\d+\.\d\d frame-p95-ms=\d+\.\d\d render-median-ms=\d+\.\d\d render-p95-ms=\d+\.\d\d draw-median-ms=\d+\.\d\d draw-p95-ms=\d+\.\d\d( ph-[a-z-]+-(median|p95)-ms=\d+\.\d\d){{32}} alloc-median-kb=\d+\.\d\d alloc-p95-kb=\d+\.\d\d gc0=\d+ gc1=\d+ gc2=\d+ gc-pause-ms=\d+\.\d\d$", rows[index++]);
                }
            }
        }
    }

    [AvaloniaFact]
    public async Task StructureMeasurementWritesOneRowPerCaseAndLeavesTheDocumentAsFound()
    {
        using var f = new WindowFixture();
        f.Show();
        var probe = new Probe();
        using var measurement = Create(f, probe, "--measure-structure", typingKeys: 2);

        await f.Window.OpenFilesAsync([WindowFixture.Thesis]);
        for (var i = 0; i < 4800 && probe.Exits.Count == 0; i++)
        {
            await Task.Delay(25);
            WindowFixture.Pump();
        }

        Assert.Equal([0], probe.Exits);
        Assert.Equal(["move-first-to-last", "move-middle", "move-last-to-first", "numbering-on", "numbering-off", "toggle-list"], StructurePlaces.Names);
        var rows = probe.Lines.Where(l => l.StartsWith("structure case=", StringComparison.Ordinal)).ToArray();
        Assert.Equal(StructurePlaces.Names.Length, rows.Length);
        for (var i = 0; i < rows.Length; i++)
        {
            Assert.Matches($@"^structure case={StructurePlaces.Names[i]} view=page n=2 median-ms=\d+\.\d\d p95-ms=\d+\.\d\d max-ms=\d+\.\d\d handler-median-ms=\d+\.\d\d frame-median-ms=\d+\.\d\d frame-p95-ms=\d+\.\d\d", rows[i]);
        }

        var refreshes = probe.Lines.Where(l => l.StartsWith("structure-refresh case=", StringComparison.Ordinal)).ToArray();
        Assert.Equal(StructurePlaces.Names.Length, refreshes.Length);
        Assert.All(refreshes, r => Assert.Matches(@"^structure-refresh case=[a-z-]+ view=page n=1 ms=\d+\.\d\d$", r));

        // Every run restores what it changed: the outline and the heading numbering are the file's again.
        using var original = Raffica.Write.Docx.DocxFile.Open(WindowFixture.Thesis);
        var model = f.Window.Workspace.Active!.Docx.Model;
        Assert.Equal(Raffica.Write.Panes.OutlineSource.Read(original.Model).Select(e => e.Text), Raffica.Write.Panes.OutlineSource.Read(model).Select(e => e.Text));
        Assert.Equal(Raffica.Write.Editing.HeadingNumbering.StateOf(original.Model), Raffica.Write.Editing.HeadingNumbering.StateOf(model));
        Assert.Equal(original.Model.Body.Blocks.Count, model.Body.Blocks.Count);
    }

    [AvaloniaFact]
    public async Task TypingWithTheFormattingPaneOpenIsMeasuredWithTheRealSubscribers()
    {
        using var f = new WindowFixture();
        f.Show();
        var probe = new Probe();
        using var measurement = Create(f, probe, "--measure-typing-pane", typingKeys: 3);

        await f.Window.OpenFilesAsync([WindowFixture.Thesis]);
        for (var i = 0; i < 4800 && probe.Exits.Count == 0; i++)
        {
            await Task.Delay(25);
            WindowFixture.Pump();
        }

        Assert.Equal([0], probe.Exits);
        Assert.True(f.Window.IsFormattingOpen, "the pane is open during the typing run");

        // The subscribers heard of the keys: the window's refresh made its timer, the pane followed the session and showed a state again after the first fill, and each job ended with a recorded refresh.
        Assert.True(f.Window.FormatRefresh?.TimerCreated == true, "the window's display refresh was requested");
        var pane = f.Window.FormattingOf(f.Window.Workspace.Active!);
        Assert.NotNull(pane);
        Assert.True(pane.RefreshCount > 1, "the pane read the selection again (a live session subscription)");
        Assert.True(pane.Refresh.TimerCreated, "the pane's own refresh was requested by the session");
        var refreshes = probe.Lines.Where(l => l.StartsWith("typing-refresh case=", StringComparison.Ordinal)).ToArray();
        Assert.Equal(18, refreshes.Length);
        Assert.All(refreshes, r => Assert.Matches(@"^typing-refresh case=[a-z-]+ view=(page|writing) n=1 ms=\d+\.\d\d window-ms=\d+\.\d\d pane-ms=\d+\.\d\d$", r));
        var rows = probe.Lines.Where(l => l.StartsWith("typing case=", StringComparison.Ordinal)).ToArray();
        Assert.Equal(16, rows.Length);
        Assert.All(rows, r => Assert.Matches(@"^typing case=[a-z-]+ view=(page|writing) n=3 median-ms=", r));
    }

    [Fact]
    public void OptionsAcceptTheNewSwitch()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-write-measure-snapshot-").FullName;
        try
        {
            var ex = Assert.Throws<ArgumentException>(() => WriteOptions.Parse(["--measure-typing-snapshot", WindowFixture.Thesis]));
            Assert.Contains("--settings-file", ex.Message, StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() => WriteOptions.Parse(["--measure-typing-snapshot", "--settings-file", Path.Combine(folder, "s.json")]));
            Assert.Throws<ArgumentException>(() => WriteOptions.Parse(["--measure-typing-snapshot", "--measure-typing", "--settings-file", Path.Combine(folder, "s.json"), "a.docx"]));

            var options = WriteOptions.Parse(["--measure-typing-snapshot", "--settings-file", Path.Combine(folder, "s.json"), WindowFixture.Thesis]);
            Assert.True(options.IsMeasuring);
            Assert.Equal("--measure-typing-snapshot", options.MeasureSwitch);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task TheSnapshotRunWritesTheSameRowsAsTyping()
    {
        using var f = new WindowFixture();
        f.Show();
        var probe = new Probe();
        using var measurement = Create(f, probe, "--measure-typing-snapshot", typingKeys: 2);

        await f.Window.OpenFilesAsync([WindowFixture.Thesis]);
        for (var i = 0; i < 4800 && probe.Exits.Count == 0; i++)
        {
            await Task.Delay(25);
            WindowFixture.Pump();
        }

        Assert.Equal([0], probe.Exits);
        var rows = probe.Lines.Where(l => l.StartsWith("typing case=", StringComparison.Ordinal)).ToArray();
        Assert.Equal(16, rows.Length);
        Assert.All(rows, r => Assert.Matches(@"^typing case=[a-z-]+ view=(page|writing) n=2 median-ms=\d+\.\d\d p95-ms=\d+\.\d\d max-ms=\d+\.\d\d handler-median-ms=", r));

        // The snapshot loop wrote copies meanwhile, into the folder that follows the settings file, and reports their lease and total times.
        var snapshot = Assert.Single(probe.Lines, l => l.StartsWith("snapshot n=", StringComparison.Ordinal));
        Assert.Matches(@"^snapshot n=[1-9]\d* lease-median-ms=\d+\.\d\d lease-max-ms=\d+\.\d\d total-median-ms=\d+\.\d\d total-max-ms=\d+\.\d\d blocks=\d+ blocks-written-median=\d+ blocks-written-max=\d+ whole-passes=\d+ gc0=\d+ gc1=\d+ gc2=\d+ gc-pause-ms=\d+$", snapshot);
        Assert.StartsWith(f.Folder, f.Window.RecoveryFolder, StringComparison.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(f.Window.RecoveryFolder, "*.docx"));
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
            Assert.Equal(
                "typing case=header view=writing n=3 median-ms=2.00 p95-ms=3.00 max-ms=3.00 handler-median-ms=0.50 frame-median-ms=17.00 frame-p95-ms=18.00 render-median-ms=0.25 render-p95-ms=0.50 draw-median-ms=1.25 draw-p95-ms=2.50",
                Measurement.TypingLine("header", "writing", [1.0, 2.0, 3.0], [0.5, 0.5, 0.5], [16.0, 17.0, 18.0], "typing", [0.25, 0.25, 0.5], [1.25, 1.25, 2.5]));
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
