using System.Globalization;
using Avalonia.Headless.XUnit;
using Raffica.Pdf.Settings;
using Raffica.Pdf.Tests.Viewing;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests;

public sealed class MeasurementTests
{
    private static readonly string[] s_switches = ["--measure-startup", "--measure-second", "--measure-scroll", "--measure-memory"];

    /// <summary>Collects the lines and the exit codes of a <see cref="Measurement"/>.</summary>
    private sealed class Probe
    {
        public StringWriter Output { get; } = new() { NewLine = "\n" };

        public List<int> Exits { get; } = [];

        public string[] Lines => Output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static Measurement Create(WindowFixture f, Probe probe, string option, int scrollSteps = 600)
    {
        var options = PdfOptions.Parse([option, "--settings-file", Path.Combine(f.Folder, "m.json"), TestData.Pdf("hello_world.pdf")]);
        var measurement = new Measurement(options, f.Window, probe.Output, probe.Exits.Add, Timeout.InfiniteTimeSpan, scrollSteps);
        measurement.Start();
        return measurement;
    }

    [AvaloniaFact]
    public async Task FirstPageLineIsWrittenOnceAfterTheFirstSharpTile()
    {
        using var f = WindowFixture.Create();
        var probe = new Probe();
        using var measurement = Create(f, probe, "--measure-startup");

        Assert.Empty(probe.Lines);
        await f.OpenAsync("hello_world.pdf");
        await ViewFixture.WaitAsync(() => probe.Exits.Count == 1);

        // A further file with its own first sharp page must not write a second line.
        await f.OpenAsync("outline.pdf");
        await Task.Delay(300);
        ViewFixture.Pump();

        Assert.Equal(["first-page"], probe.Lines);
        Assert.Equal([0], probe.Exits);
    }

    [AvaloniaFact]
    public async Task TheSecondFileIsTimedFromTheArrivalOfTheHandover()
    {
        using var f = WindowFixture.Create();
        var probe = new Probe();
        using var measurement = Create(f, probe, "--measure-second");

        await f.OpenAsync("hello_world.pdf");
        await ViewFixture.WaitAsync(() => probe.Lines.Contains("first-page"));
        Assert.Empty(probe.Exits);

        f.Window.OnHandover([TestData.Pdf("outline.pdf")]);
        await ViewFixture.WaitAsync(() => probe.Exits.Count == 1);

        Assert.Equal("first-page", probe.Lines[0]);
        Assert.Matches(@"^second-page ms=\d+\.\d\d$", probe.Lines[1]);
        Assert.Equal(2, probe.Lines.Length);
    }

    [AvaloniaFact]
    public async Task TheScrollRunWritesOneLineAndEnds()
    {
        using var f = WindowFixture.Create();
        var probe = new Probe();
        using var measurement = Create(f, probe, "--measure-scroll", scrollSteps: 12);

        await f.OpenAsync("thousand-pages.pdf");
        await ViewFixture.WaitAsync(() => probe.Exits.Count == 1);

        Assert.Equal("first-page", probe.Lines[0]);
        Assert.Matches(@"^scroll p99-ms=\d+\.\d\d max-ms=\d+\.\d\d$", probe.Lines[1]);
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
        using var f = WindowFixture.Create();
        var exits = new List<int>();
        var error = new StringWriter();
        var output = new HookedWriter(line =>
        {
            if (line == "first-page")
            {
                // The tab closes before the first scroll step (a clean tab closes at once).
                Assert.True(f.Window.Workspace.CloseAsync(f.Window.Workspace.Active!).IsCompletedSuccessfully);
            }
        });
        var options = PdfOptions.Parse(["--measure-scroll", "--settings-file", Path.Combine(f.Folder, "m.json"), TestData.Pdf("hello_world.pdf")]);
        using var measurement = new Measurement(options, f.Window, output, exits.Add, Timeout.InfiniteTimeSpan, 12, error);
        measurement.Start();

        await f.OpenAsync("hello_world.pdf");
        await ViewFixture.WaitAsync(() => exits.Count == 1);

        Assert.Equal([Measurement.Failed], exits);
        Assert.DoesNotContain("scroll", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("error:", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("no document", error.ToString(), StringComparison.Ordinal);
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
    public void MeasurementNeverReadsOrWritesTheUsersSettings()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-measure-options-").FullName;
        try
        {
            foreach (var name in s_switches)
            {
                // A measurement without its own settings file would meet the user's settings, recent files and instance pipe.
                var ex = Assert.Throws<ArgumentException>(() => PdfOptions.Parse([name, TestData.Pdf("hello_world.pdf")]));
                Assert.Contains("--settings-file", ex.Message, StringComparison.Ordinal);

                var options = PdfOptions.Parse([name, "--settings-file", Path.Combine(folder, "s.json"), TestData.Pdf("hello_world.pdf")]);
                Assert.True(options.IsMeasuring);
                Assert.Equal(Path.Combine(folder, "s.json"), PdfSettingsFiles.ShellPathFor(options.SettingsFile));
                Assert.Equal(folder, Path.GetDirectoryName(PdfSettingsFiles.PdfSettingsPathFor(options.SettingsFile)));
                Assert.Equal(folder, Path.GetDirectoryName(PdfSettingsFiles.RecentPathFor(options.SettingsFile)));
            }

            // One measure switch per run, and a file to open.
            Assert.Throws<ArgumentException>(() => PdfOptions.Parse(["--measure-startup", "--measure-memory", "--settings-file", Path.Combine(folder, "s.json"), "a.pdf"]));
            Assert.Throws<ArgumentException>(() => PdfOptions.Parse(["--measure-startup", "--settings-file", Path.Combine(folder, "s.json")]));
            Assert.False(PdfOptions.Parse(["a.pdf"]).IsMeasuring);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
