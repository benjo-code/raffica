using Avalonia.Headless.XUnit;
using Raffica.Core.Pdf;
using Raffica.Core.Redaction;
using Raffica.Core.Text;
using Raffica.Pdf.Redacting;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

public sealed class RedactOverlayTests
{
    private const string Term = "SECRET-TERM-42";

    private static double Luminance(byte r, byte g, byte b)
    {
        static double Channel(byte v)
        {
            var c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(r)) + (0.7152 * Channel(g)) + (0.0722 * Channel(b));
    }

    private static double Contrast(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);

    [AvaloniaFact]
    public async Task MarkIsHatchedAndNamedWithoutTheTerm()
    {
        using var f = await RedactFixture.OpenAsync("pages-marked.pdf");
        var mark = new RedactionMark(Guid.NewGuid(), RedactionKind.Text, 2, [new PageRect(50, 100, 150, 120)], Term, false);

        var name = RedactOverlay.NameOf(mark);

        Assert.Equal("Mark for redaction, page 3", name);
        Assert.DoesNotContain(Term, name, StringComparison.Ordinal);

        // The picture: the area is hatched (stripes of ink on the page, not a flat fill) and has an edge.
        var (plain, stride) = f.Text.CaptureView();
        var area = new PageRect(60, 100, 260, 180);
        Assert.True(f.Session.AddArea(0, area).IsDone);
        f.Session.Selection.Clear();
        ViewFixture.Pump();
        var (marked, _) = f.Text.CaptureView();
        var rect = f.View.ToView(0, area)!.Value;

        // Stripes: the interior holds at least two colours, and no one colour fills it (a flat tint would be one colour, a solid bar too).
        var colours = new Dictionary<int, int>();
        var total = 0;
        for (var y = (int)rect.Top + 4; y < (int)rect.Bottom - 4; y++)
        {
            for (var x = (int)rect.Left + 4; x < (int)rect.Right - 4; x++)
            {
                total++;
                var i = (y * stride) + (x * 4);
                var key = marked[i] | (marked[i + 1] << 8) | (marked[i + 2] << 16);
                colours[key] = colours.GetValueOrDefault(key) + 1;
            }
        }

        Assert.True(colours.Count >= 2, "the area holds stripes");
        Assert.InRange(colours.Values.Max() / (double)total, 0.3, 0.95);
        Assert.NotEqual(plain, marked);
        Assert.True(HasEdge(plain, marked, stride, (int)rect.Left, (int)rect.Top, (int)rect.Bottom), "the left edge is drawn");
    }

    private static bool HasEdge(byte[] plain, byte[] marked, int stride, int x, int top, int bottom)
    {
        var edge = 0;
        for (var y = top + 4; y < bottom - 4; y++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                var i = (y * stride) + ((x + dx) * 4);
                if (plain[i] != marked[i] || plain[i + 1] != marked[i + 1])
                {
                    edge++;
                    break;
                }
            }
        }

        return edge >= (bottom - top - 8) * 0.9;
    }

    [AvaloniaTheory]
    [InlineData(PageTheme.Light)]
    [InlineData(PageTheme.Dark)]
    public async Task TheEdgeReadsAtThreeToOneOnLightAndDarkPages(PageTheme theme)
    {
        using var f = await RedactFixture.OpenAsync();
        f.View.PageTheme = theme;
        ViewFixture.Pump();
        await Task.Delay(300, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        var area = new PageRect(60, 250, 260, 330);
        var (before, stride) = f.Text.CaptureView();
        f.Session.AddArea(0, area);
        f.Session.Selection.Clear();
        ViewFixture.Pump();
        var (after, _) = f.Text.CaptureView();
        var rect = f.View.ToView(0, area)!.Value;

        // On the middle row of the left edge some pixel of the edge has 3:1 against the page beside it.
        var y = (int)((rect.Top + rect.Bottom) / 2);
        var page = (y * stride) + (((int)rect.Left - 8) * 4);
        var pageLuminance = Luminance(before[page + 2], before[page + 1], before[page]);
        var best = 0.0;
        for (var dx = -3; dx <= 3; dx++)
        {
            var i = (y * stride) + (((int)rect.Left + dx) * 4);
            best = Math.Max(best, Contrast(pageLuminance, Luminance(after[i + 2], after[i + 1], after[i])));
        }

        Assert.True(best >= 3.0, "contrast " + best.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " on the " + theme + " page");
    }

    [AvaloniaFact]
    public async Task AnAreaOnAnotherPageIsNotDrawnHere()
    {
        using var f = await RedactFixture.OpenAsync("pages-marked.pdf");
        var (before, _) = f.Text.CaptureView();
        f.Session.AddArea(4, new PageRect(60, 100, 260, 180));
        ViewFixture.Pump();
        var (after, _) = f.Text.CaptureView();
        Assert.Equal(before, after);
    }
}
