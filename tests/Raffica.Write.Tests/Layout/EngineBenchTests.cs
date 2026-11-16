using System.Globalization;
using Raffica.TestSupport;
using SkiaSharp;
using WriteLayoutBench;
using Xunit;

namespace Raffica.Write.Tests.Layout;

public sealed class EngineBenchTests
{
    private static string Thesis => Path.Combine(TestData.Root, "testdata", "write-corpus", "thesis", "thesis.docx");

    private static (int Code, string Out, string Error) Run(params string[] args)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var code = Program.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void CheckPassesOnTheThesis()
    {
        var (code, output, error) = Run("check", "--file", Thesis);
        Assert.True(code == 0, error + output);
        Assert.Contains("pages=", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckAssertsTheBudgetOnlyWhenAskedFor()
    {
        var (okCode, _, okError) = Run("check", "--file", Thesis, "--budget-ms", "600000");
        Assert.True(okCode == 0, okError);

        var (code, output, error) = Run("check", "--file", Thesis, "--budget-ms", "0");
        Assert.Equal(1, code);
        Assert.Contains("pages=", output, StringComparison.Ordinal);
        Assert.Contains("budget of 0 ms", error, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckRefusesAMissingFile()
    {
        var (code, _, error) = Run("check", "--file", Path.Combine(TestData.Root, "no-such.docx"));
        Assert.Equal(2, code);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void RunRefusesAJitBuild()
    {
        var (code, output, error) = Run("run", "--file", Thesis, "--runs", "1", "--warmup", "0");
        Assert.Equal(2, code);
        Assert.Contains("Native AOT", error, StringComparison.Ordinal);
        Assert.DoesNotContain("median", output, StringComparison.Ordinal);
    }

    [Fact]
    public void PhasesRefusesAJitBuild()
    {
        var (code, output, error) = Run("phases", "--file", Thesis, "--runs", "1");
        Assert.Equal(2, code);
        Assert.Contains("Native AOT", error, StringComparison.Ordinal);
        Assert.DoesNotContain("median", output, StringComparison.Ordinal);
    }

    [Fact]
    public void EditRefusesAJitBuild()
    {
        var (code, output, error) = Run("edit", "--file", Thesis, "--runs", "1");
        Assert.Equal(2, code);
        Assert.Contains("Native AOT", error, StringComparison.Ordinal);
        Assert.DoesNotContain("median", output, StringComparison.Ordinal);
    }

    [Fact]
    public void EditTypesAtEveryCaseOfTheThesisInBothViews()
    {
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var rows = EditCommand.Measure(Thesis, 4, error);

        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(["after-open", "after-switch"], rows.Where(r => r.IsFirst).Select(r => r.When));
        Assert.All(rows.Where(r => r.IsFirst), r => Assert.Single(r.Millis));
        foreach (var view in new[] { "page", "writing" })
        {
            Assert.Equal(EditCases.Names, rows.Where(r => !r.IsFirst && r.View == view).Select(r => r.Case));
        }

        Assert.All(rows.Where(r => !r.IsFirst), r =>
        {
            Assert.Equal(4, r.Millis.Length);
            Assert.All(r.Millis, ms => Assert.True(ms >= 0 && double.IsFinite(ms)));
            Assert.Equal(4, r.Edits);
            Assert.InRange(r.Restarts, 0, r.Edits);
            Assert.True(r.P95 >= r.Median && r.Max >= r.P95);
        });

        // The same seed gives the same keystrokes (the table is comparable between runs).
        var a = new Random(4242);
        var b = new Random(4242);
        Assert.Equal(Enumerable.Range(0, 30).Select(i => EditCases.KeyText(a, i)), Enumerable.Range(0, 30).Select(i => EditCases.KeyText(b, i)));
    }

    [Fact]
    public void FormatCasesExistInTheBenchAndRun()
    {
        Assert.Equal(["format-word", "format-paragraph"], EditCases.FormatNames);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var report = EditCommand.MeasureFormat(Thesis, 3, error);

        Assert.Equal(string.Empty, error.ToString());
        foreach (var view in new[] { "page", "writing" })
        {
            foreach (var kind in EditCases.FormatNames)
            {
                // The same places as the typing cases, in the same order.
                Assert.Equal(EditCases.Names, report.Rows.Where(r => r.View == view && r.Case.StartsWith(kind + "/", StringComparison.Ordinal)).Select(r => r.Case[(kind.Length + 1)..]));
            }
        }

        Assert.All(report.Rows, r =>
        {
            Assert.Equal(3, r.Millis.Length);
            Assert.All(r.Millis, ms => Assert.True(ms >= 0 && double.IsFinite(ms)));
            Assert.True(r.P95 >= r.Median && r.Max >= r.P95);
        });

        // The style update is recorded, not judged: the editor call, the time to the pages in view and to the full layout, one value per repetition.
        Assert.NotNull(report.StyleUpdate);
        Assert.Equal(3, report.StyleUpdate.EditorMillis.Length);
        Assert.Equal(3, report.StyleUpdate.ViewMillis.Length);
        Assert.Equal(3, report.StyleUpdate.FullMillis.Length);
        Assert.All(report.StyleUpdate.FullMillis.Zip(report.StyleUpdate.ViewMillis), p => Assert.True(p.First >= p.Second));
    }

    [Fact]
    public void SelectAllBoldIsMeasuredOnTheThesisAndUndone()
    {
        var result = EditCommand.MeasureSelectAll(Thesis);

        Assert.True(result.PlainToggleMs >= 0 && result.PlainUndoMs >= 0 && result.LayoutToggleMs >= 0 && result.LayoutApplyMs >= 0 && result.LayoutUndoMs >= 0 && result.LayoutUndoApplyMs >= 0);
        Assert.Contains("select-all", EditCommand.SelectAllTable(result), StringComparison.Ordinal);
    }

    [Fact]
    public void TheBenchAndTheAppUseTheSamePlaces()
    {
        // The app keeps a near copy (TypingPlaces in the Write app, its test pins the same two lists): change both together.
        Assert.Equal(["middle", "page-break", "section-break", "chapter-end", "table-cell", "footnote", "header", "footer"], EditCases.Names);
        Assert.Equal(["format-word", "format-paragraph"], EditCases.FormatNames);
    }

    [Fact]
    public void EditBackgroundRefusesAJitBuild()
    {
        var (code, output, error) = Run("edit", "--background", "--file", Thesis, "--runs", "1");
        Assert.Equal(2, code);
        Assert.Contains("Native AOT", error, StringComparison.Ordinal);
        Assert.DoesNotContain("background", output, StringComparison.Ordinal);
    }

    [Fact]
    public void EditBackgroundTypesWhileTheLayoutRunsAndSeparatesTheLeaseWait()
    {
        var result = EditCommand.MeasureBackground(Thesis, 6);

        Assert.Equal(6, result.Millis.Length);
        Assert.Equal(6, result.LeaseMillis.Length);
        Assert.All(result.Millis, ms => Assert.True(ms >= 0 && double.IsFinite(ms)));
        Assert.All(result.LeaseMillis, ms => Assert.True(ms >= 0 && double.IsFinite(ms)));
        for (var i = 0; i < 6; i++)
        {
            // The lease wait is part of the keystroke.
            Assert.True(result.LeaseMillis[i] <= result.Millis[i] + 0.001);
        }

        Assert.InRange(result.IncompleteKeys, 0, 6);
        Assert.Contains("background", EditCommand.BackgroundTable(result), StringComparison.Ordinal);
    }

    [Fact]
    public void EditPercentilesAreNearestRankAndTheMeanOfTheMiddleForTheMedian()
    {
        Assert.Equal(2.5, EditRow.Percentile([4, 1, 3, 2], 0.5));
        Assert.Equal(3, EditRow.Percentile([5, 1, 3, 2, 4], 0.5));
        Assert.Equal(19, EditRow.Percentile([.. Enumerable.Range(1, 20).Select(i => (double)i)], 0.95));
    }

    [Fact]
    public void FirstPageChildPrintsDone()
    {
        using (var result = FirstPage.Draw(Thesis, new PhaseTimes()))
        {
            Assert.True(result.Milliseconds > 0);
            Assert.InRange(result.Bitmap.Width, 700, 900);
            Assert.InRange(result.Bitmap.Height, 1000, 1200);
            var white = (uint)SKColors.White;
            var inked = false;
            for (var y = 0; y < result.Bitmap.Height && !inked; y++)
            {
                for (var x = 0; x < result.Bitmap.Width; x++)
                {
                    if ((uint)result.Bitmap.GetPixel(x, y) != white)
                    {
                        inked = true;
                        break;
                    }
                }
            }

            Assert.True(inked, "page 1 is blank");
        }

        var (code, output, error) = Run("first-page", "--file", Thesis);
        Assert.True(code == 0, error);
        Assert.Matches(@"^done ms=\d+(\.\d+)?\r?\n$", output);
    }

    [Fact]
    public void PhasesNamesEveryPhase()
    {
        var times = new PhaseTimes();
        using var result = FirstPage.Draw(Thesis, times);
        var text = Phases.Format([times]);
        foreach (var name in PhaseTimes.Names)
        {
            Assert.Contains(name, text, StringComparison.Ordinal);
        }

        Assert.NotNull(PhaseTimes.Parse(times.Serialize()));
    }

    [Fact]
    public void RenderWritesAPng()
    {
        var target = Path.Combine(Path.GetTempPath(), "wlb-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var (code, _, error) = Run("render", "3", target, "--file", Thesis);
            Assert.True(code == 0, error);
            using var image = SKBitmap.Decode(target);
            Assert.NotNull(image);
            Assert.InRange(image.Width, 700, 900);
        }
        finally
        {
            File.Delete(target);
        }
    }
}
