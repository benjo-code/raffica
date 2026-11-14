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
