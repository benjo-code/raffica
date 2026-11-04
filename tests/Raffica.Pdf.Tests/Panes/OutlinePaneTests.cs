using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Pdf;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Panes;

public sealed class OutlinePaneTests
{
    private static async Task<PaneFixture<OutlinePane>> OpenAsync(string file, Func<CancellationToken, Task<IReadOnlyList<PdfOutlineItem>>>? read = null)
    {
        var f = await PaneFixture<OutlinePane>.OpenAsync(file, (tab, view) => read is null ? new OutlinePane(tab, view) : new OutlinePane(view, read));
        await f.FirstSharpAsync();
        await WaitLoadedAsync(f.Pane);
        return f;
    }

    private static async Task WaitLoadedAsync(OutlinePane pane)
    {
        var done = await Task.WhenAny(pane.LoadTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(pane.LoadTask, done);
        await pane.LoadTask;
        ViewFixture.Pump();
    }

    // Selects a row and gives its list item the keyboard focus, as the arrow keys or F6 would.
    private static void FocusRow(OutlinePane pane, int row)
    {
        pane.List.SelectedIndex = row;
        ViewFixture.Pump();
        Assert.True(pane.ContainerOf(row)!.Focus());
    }

    private static int RowOf(OutlinePane pane, string title)
    {
        for (var i = 0; i < pane.RowCount; i++)
        {
            if (pane.RowTitle(i) == title)
            {
                return i;
            }
        }

        throw new InvalidOperationException(title + " not found");
    }

    [AvaloniaFact]
    public async Task ClickingAnEntryJumpsToItsPage()
    {
        using var f = await OpenAsync("outline.pdf");
        Assert.False(f.Pane.IsEmptyHintShown);
        Assert.Equal(1, f.Pane.ReadCount);

        // Kapitel 1 is open: its children are in the list; Kapitel 2 is closed.
        var row = RowOf(f.Pane, "Abschnitt 1.1");
        f.Click(f.Pane.ContainerOf(row)!);
        Assert.Equal(1, f.View.CurrentPage);

        f.Click(f.Pane.ContainerOf(RowOf(f.Pane, "Kapitel 1"))!);
        Assert.Equal(0, f.View.CurrentPage);
    }

    [AvaloniaFact]
    public async Task EntryWithoutAPageDoesNotJump()
    {
        using var f = await OpenAsync("outline.pdf");
        f.View.GoToPage(2);
        ViewFixture.Pump();

        var noPage = RowOf(f.Pane, "Abschnitt 1.2");
        var container = f.Pane.ContainerOf(noPage)!;
        Assert.Contains("no page", AutomationProperties.GetName(container));
        Assert.False(f.Pane.CanJump(noPage));
        f.Click(container);
        FocusRow(f.Pane, noPage);
        f.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        ViewFixture.Pump();
        Assert.Equal(2, f.View.CurrentPage);

        // A target past the last page (the entry "Fern") cannot be followed either.
        Assert.False(f.Pane.CanJump(RowOf(f.Pane, "Fern")));
        Assert.True(f.Pane.CanJump(RowOf(f.Pane, "Abschnitt 1.1")));
    }

    [AvaloniaFact]
    public async Task RightAndLeftOpenAndCloseAndMoveInTheTree()
    {
        using var f = await OpenAsync("outline.pdf");
        var chapter2 = RowOf(f.Pane, "Kapitel 2");
        var before = f.Pane.RowCount;
        FocusRow(f.Pane, chapter2);

        f.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        ViewFixture.Pump();
        Assert.Equal(before + 1, f.Pane.RowCount);
        Assert.Equal(chapter2, f.Pane.List.SelectedIndex);
        Assert.Contains("expanded", AutomationProperties.GetName(f.Pane.ContainerOf(chapter2)!));

        // Right on an open entry goes to its first child, Left to the parent, Left again closes.
        f.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        ViewFixture.Pump();
        Assert.Equal(chapter2 + 1, f.Pane.List.SelectedIndex);
        f.Window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        ViewFixture.Pump();
        Assert.Equal(chapter2, f.Pane.List.SelectedIndex);
        f.Window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        ViewFixture.Pump();
        Assert.Equal(before, f.Pane.RowCount);
        Assert.Contains("collapsed", AutomationProperties.GetName(f.Pane.ContainerOf(chapter2)!));
    }

    [AvaloniaFact]
    public async Task KeyboardReachesEveryEntryOfALargeOutline()
    {
        // 40 chapters open, each with 30 sections: 1240 entries.
        var items = new List<PdfOutlineItem>();
        for (var c = 0; c < 40; c++)
        {
            var sections = Enumerable.Range(0, 30).Select(s => new PdfOutlineItem($"{c}.{s}", 0, false, [])).ToList();
            items.Add(new PdfOutlineItem($"Chapter {c}", 0, true, sections));
        }

        using var f = await OpenAsync("pages-marked.pdf", _ => Task.FromResult<IReadOnlyList<PdfOutlineItem>>(items));
        Assert.Equal(1240, f.Pane.RowCount);
        FocusRow(f.Pane, 0);
        for (var i = 1; i < 1240; i++)
        {
            f.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            if (f.Pane.List.SelectedIndex != i)
            {
                Assert.Equal(i, f.Pane.List.SelectedIndex);
            }
        }

        ViewFixture.Pump();
        var last = f.Pane.ContainerOf(1239);
        Assert.NotNull(last);
        Assert.Equal("39.29", AutomationProperties.GetName(last!)!.Split(',')[0]);
        Assert.True(f.Pane.RealizedCount < 100, "the list is virtualised");
    }

    [AvaloniaFact]
    public async Task AHugeOutlineIsBuiltWithoutFreezing()
    {
        var items = Enumerable.Range(0, PdfDocument.MaxOutlineItems)
            .Select(i => new PdfOutlineItem("Entry " + i, 0, false, []))
            .ToList();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var f = await OpenAsync("pages-marked.pdf", _ => Task.FromResult<IReadOnlyList<PdfOutlineItem>>(items));
        Assert.Equal(PdfDocument.MaxOutlineItems, f.Pane.RowCount);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), clock.Elapsed.ToString());
        Assert.True(f.Pane.RealizedCount < 100);
    }

    [AvaloniaFact]
    public async Task CyclicOutlineDoesNotHang()
    {
        // The real file: Core cuts the cycle of /Next.
        using (var real = await OpenAsync("outline-cycle.pdf"))
        {
            Assert.InRange(real.Pane.RowCount, 3, PdfDocument.MaxOutlineItems);
        }

        // A tree that points back to itself, such as no reader gives but a double can: every entry once.
        var children = new List<PdfOutlineItem>();
        var loop = new PdfOutlineItem("Loop", 0, true, children);
        children.Add(loop);
        using var f = await OpenAsync("pages-marked.pdf", _ => Task.FromResult<IReadOnlyList<PdfOutlineItem>>([loop]));
        Assert.Equal(1, f.Pane.RowCount);
        FocusRow(f.Pane, 0);
        f.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        ViewFixture.Pump();
        Assert.True(f.Pane.RowCount <= 2);
    }

    [AvaloniaFact]
    public async Task NoOutlineShowsTheEmptyHint()
    {
        using var f = await OpenAsync("text-basic.pdf");
        Assert.True(f.Pane.IsEmptyHintShown);
        Assert.Equal(0, f.Pane.RowCount);
        Assert.Equal("This document has no outline.", f.Pane.Hint.Text);
        Assert.Equal("This document has no outline.", AutomationProperties.GetName(f.Pane.Hint));
    }

    [AvaloniaFact]
    public async Task AFailedReadShowsTheEmptyHintAndNeverThrows()
    {
        using var f = await OpenAsync("pages-marked.pdf", _ => throw new InvalidOperationException("bad"));
        Assert.True(f.Pane.IsEmptyHintShown);
        using var g = await OpenAsync("pages-marked.pdf", _ => Task.FromException<IReadOnlyList<PdfOutlineItem>>(new PdfRenderException("bad")));
        Assert.True(g.Pane.IsEmptyHintShown);
    }

    [AvaloniaFact]
    public async Task TheOutlineIsReadAfterTheFirstSharpPageAndOnlyOnce()
    {
        var reads = 0;
        var f = await PaneFixture<OutlinePane>.OpenAsync("pages-marked.pdf", (tab, view) => new OutlinePane(view, _ =>
        {
            reads++;
            return Task.FromResult<IReadOnlyList<PdfOutlineItem>>([]);
        }), showView: false);
        using (f)
        {
            for (var i = 0; i < 8; i++)
            {
                await Task.Delay(25);
                ViewFixture.Pump();
            }

            Assert.Equal(0, reads);

            var second = new Window { Width = 800, Height = 600, Content = f.View };
            second.Show();
            try
            {
                await f.FirstSharpAsync();
                await WaitLoadedAsync(f.Pane);
                Assert.Equal(1, reads);
                f.Pane.Dispose();
                Assert.Equal(1, reads);
            }
            finally
            {
                second.Content = null;
                second.Close();
            }
        }
    }

    [AvaloniaFact]
    public async Task TheNamesAreGermanInGerman()
    {
        PdfStrings.Culture = System.Globalization.CultureInfo.GetCultureInfo("de");
        using var f = await OpenAsync("outline.pdf");
        Assert.Contains("ohne Seite", AutomationProperties.GetName(f.Pane.ContainerOf(RowOf(f.Pane, "Abschnitt 1.2"))!));
        Assert.Contains("aufgeklappt", AutomationProperties.GetName(f.Pane.ContainerOf(RowOf(f.Pane, "Kapitel 1"))!));
        Assert.Contains("zugeklappt", AutomationProperties.GetName(f.Pane.ContainerOf(RowOf(f.Pane, "Kapitel 2"))!));
    }
}
