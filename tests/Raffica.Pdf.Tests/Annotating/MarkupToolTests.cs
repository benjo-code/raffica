using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Annotations;
using Raffica.Core.Pdf;
using Raffica.Core.Settings;
using Raffica.Core.Text;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Annotating;

public sealed class MarkupToolTests
{
    private static async Task<(PageText Text, int Start, int End)> WordAsync(AnnotateFixture f, string word, int page = 0)
    {
        var text = await f.Text.TextOfAsync(page);
        var start = text.GetText(0, text.Chars.Count).IndexOf(word, StringComparison.Ordinal);
        Assert.True(start >= 0, word);
        return (text, start, start + word.Length);
    }

    [AvaloniaFact]
    public async Task ReleaseAfterDragAppliesTheMarkupAndClearsTheSelection()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var (text, start, end) = await WordAsync(f, "Handbuch");
        f.Tools.Choose(AnnotateTool.Highlight);

        f.Drag(f.Text.CharPoint(text, start, right: false), f.Text.CharPoint(text, end - 1, right: true));
        await f.SettleAsync();

        var marks = await f.AnnotationsAsync(0);
        var mark = Assert.Single(marks);
        Assert.Equal(AnnotationKind.Highlight, mark.Kind);
        Assert.Single(mark.QuadRects);
        Assert.False(f.Text.Text.Selection.HasSelection);
        Assert.True(f.Tab.IsDirty);
        Assert.True(f.Tab.Annotations.CanUndo);
        Assert.Empty(f.Text.Text.Selection.RectsOn(0));
    }

    [AvaloniaFact]
    public async Task DoubleClickMarksAWordAndTripleClickOneLine()
    {
        using (var f = await AnnotateFixture.OpenAsync("text-rich.pdf"))
        {
            var (text, start, end) = await WordAsync(f, "Handbuch");
            f.Tools.Choose(AnnotateTool.Highlight);
            var point = f.Text.CharPoint(text, start + 3, right: false);

            f.Click(point, 2);

            // The double click waits for a third click: nothing is made yet, the tab already counts as changed.
            Assert.True(f.Interaction.Markup.IsWaiting);
            Assert.Empty(await f.AnnotationsAsync(0));
            Assert.True(f.Tab.IsDirty);

            f.Time.Advance(10_000);
            await f.SettleAsync();

            var mark = Assert.Single(await f.AnnotationsAsync(0));
            var word = Assert.Single(text.RectsOf(start, end));
            Assert.Equal(word.Left, mark.QuadRects[0].Left, 1.0);
            Assert.Equal(word.Right, mark.QuadRects[0].Right, 1.0);
        }

        using (var f = await AnnotateFixture.OpenAsync("text-rich.pdf"))
        {
            var text = await f.Text.TextOfAsync(0);
            var all = text.GetText(0, text.Chars.Count);
            var inLine = all.IndexOf("Zweite Zeile", StringComparison.Ordinal);
            var (lineStart, lineEnd) = text.LineAt(inLine);
            f.Tools.Choose(AnnotateTool.Highlight);

            f.Click(f.Text.CharPoint(text, inLine + 2, right: false), 3);
            f.Time.Advance(10_000);
            await f.SettleAsync();

            // One markup for the line, not one for the word and one for the line.
            var mark = Assert.Single(await f.AnnotationsAsync(0));
            var rects = text.RectsOf(lineStart, lineEnd);
            Assert.Single(mark.QuadRects);
            Assert.Equal(rects.Min(r => r.Left), mark.QuadRects[0].Left, 1.0);
            Assert.False(f.Interaction.Markup.IsWaiting);
        }
    }

    [AvaloniaFact]
    public async Task ClickWithoutMovementDoesNothing()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var (text, start, _) = await WordAsync(f, "Handbuch");
        f.Tools.Choose(AnnotateTool.Highlight);
        var point = f.Text.CharPoint(text, start + 2, right: false);

        f.Click(point);

        // A press that moves 3 DIPs is a click too (somewhere else, so it is no second click of the first one).
        var other = await f.Text.TextOfAsync(0);
        var elsewhere = f.Text.CharPoint(other, other.GetText(0, other.Chars.Count).IndexOf("Zweite Zeile", StringComparison.Ordinal) + 2, right: false);
        f.Drag(elsewhere, new Point(elsewhere.X + 3, elsewhere.Y));
        f.Time.Advance(10_000);
        await f.SettleAsync();

        Assert.Empty(await f.AnnotationsAsync(0));
        Assert.False(f.Tab.IsDirty);
        Assert.False(f.Text.Text.Selection.HasSelection);
    }

    [AvaloniaFact]
    public async Task SelectionAcrossPagesCreatesOneMarkupPerPage()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf", 800, 900, zoom: 0.5);
        var first = await f.Text.TextOfAsync(0);
        var second = await f.Text.TextOfAsync(1);
        var from = first.GetText(0, first.Chars.Count).IndexOf("Weiter", StringComparison.Ordinal);
        var to = second.GetText(0, second.Chars.Count).IndexOf("Seite zwei", StringComparison.Ordinal) + "Seite zwei".Length;
        Assert.NotNull(f.View.PageBounds(1));
        f.Tools.Choose(AnnotateTool.Highlight);

        f.Drag(f.Text.CharPoint(first, from, right: false), f.Text.CharPoint(second, to - 1, right: true));
        await f.SettleAsync();

        Assert.Single(await f.AnnotationsAsync(0));
        Assert.Single(await f.AnnotationsAsync(1));
        Assert.False(f.Text.Text.Selection.HasSelection);
    }

    [AvaloniaFact]
    public async Task ColourComesFromTheToolStateAndAuthorIsSetWhenNotBlank()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf", settings: AppSettings.Default with { AuthorName = "Ada Lovelace" });
        var (text, start, end) = await WordAsync(f, "Handbuch");
        f.Tools.SetColor(AnnotateTool.Underline, AnnotationPalette.ColorOf(MarkupColor.Green));
        f.Tools.Choose(AnnotateTool.Underline);
        f.Drag(f.Text.CharPoint(text, start, right: false), f.Text.CharPoint(text, end - 1, right: true));
        await f.SettleAsync();

        var mark = Assert.Single(await f.AnnotationsAsync(0));
        Assert.Equal(AnnotationKind.Underline, mark.Kind);
        Assert.Equal(AnnotationPalette.ColorOf(MarkupColor.Green), mark.Color);
        Assert.Equal("Ada Lovelace", mark.Author);

        // A blank author name sets no author, and the highlight has the colour of its own tool.
        using var blank = await AnnotateFixture.OpenAsync("text-rich.pdf", settings: AppSettings.Default with { AuthorName = "  " });
        blank.Tools.Choose(AnnotateTool.StrikeOut);
        blank.Text.Text.Selection.SelectAll();
        await blank.Interaction.Markup.ApplyAsync(AnnotateTool.StrikeOut);
        await blank.SettleAsync();
        var struck = (await blank.AnnotationsAsync(0))[0];
        Assert.Null(struck.Author);
        Assert.Equal(blank.Tools.MarkupColor(AnnotateTool.StrikeOut), struck.Color);
    }

    [AvaloniaTheory]
    [InlineData("rotated-page.pdf")]
    [InlineData("cropbox.pdf")]
    [InlineData("redact-rotated.pdf")]
    public async Task RotatedPageAndCropBoxMarkupSurvivesReload(string file)
    {
        using var f = await AnnotateFixture.OpenAsync(file, 600, 700);
        f.Tools.Choose(AnnotateTool.Highlight);
        f.Text.Text.Selection.SelectAll();
        var expected = f.Text.Text.Selection.RectsOn(0);
        Assert.NotEmpty(expected);

        await f.Interaction.Markup.ApplyAsync(AnnotateTool.Highlight);
        await f.SettleAsync();
        var before = Assert.Single(await f.AnnotationsAsync(0)).QuadRects;
        Assert.Equal(expected.Count, before.Count);

        // The markup lies where the text is on the displayed page.
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Left, before[i].Left, 1.5);
            Assert.Equal(expected[i].Top, before[i].Top, 1.5);
            Assert.Equal(expected[i].Right, before[i].Right, 1.5);
            Assert.Equal(expected[i].Bottom, before[i].Bottom, 1.5);
        }

        var ct = TestContext.Current.CancellationToken;
        var saved = await f.Tab.Annotations.Editor.SaveAsync(new PdfEngine(), f.Path!, false, null, ct);
        await using (saved.Saved.Reopened)
        {
            var after = Assert.Single(await saved.Saved.Reopened.GetAnnotationsAsync(0, ct), a => a.Kind == AnnotationKind.Highlight).QuadRects;
            Assert.Equal(before.Count, after.Count);
            for (var i = 0; i < before.Count; i++)
            {
                Assert.Equal(before[i].Left, after[i].Left, 0.1);
                Assert.Equal(before[i].Top, after[i].Top, 0.1);
                Assert.Equal(before[i].Right, after[i].Right, 0.1);
                Assert.Equal(before[i].Bottom, after[i].Bottom, 0.1);
            }
        }
    }

    [AvaloniaFact]
    public async Task NothingIsCreatedBeforeTheFirstSharpPage()
    {
        // A view that was never drawn: no sharp page, so no text is read and nothing is made.
        var inner = await ViewFixture.OpenAsync("text-rich.pdf", show: false);
        var holder = AppSettings.Default;
        var tools = new ToolState(() => holder, s => holder = s);
        using var text = new Raffica.Pdf.Text.TextInteraction(inner.Tab, inner.View, new Raffica.Pdf.Dialogs.DialogQueue(new Raffica.Shell.Controls.DialogHost()), yield: () => true);
        using var interaction = new AnnotateInteraction(inner.Tab, inner.View, tools, text.Selection, new Raffica.Pdf.Dialogs.DialogQueue(new Raffica.Shell.Controls.DialogHost()));
        try
        {
            Assert.False(inner.View.FirstSharpPageShown);
            tools.Choose(AnnotateTool.Highlight);
            var point = new Point(200, 120);

            interaction.Markup.Press(point, 1);
            interaction.Markup.Move(new Point(300, 120));
            interaction.Markup.Release(new Point(300, 120));
            text.Selection.SelectAll();
            await interaction.Markup.ApplyAsync(AnnotateTool.Highlight);
            await inner.Tab.Annotations.FlushAsync();

            Assert.False(inner.Tab.IsDirty);
            Assert.False(inner.Tab.Annotations.CanUndo);
            Assert.False(text.Selection.HasSelection);
            Assert.False(Raffica.Pdf.Text.PageTexts.TryGet(inner.Tab, 0, out _));
            Assert.DoesNotContain(await inner.Tab.Session.Document!.GetAnnotationsAsync(0, TestContext.Current.CancellationToken), a => a.Kind == AnnotationKind.Highlight);
        }
        finally
        {
            inner.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task ApplyRegistersPendingUntilItCompletes()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        f.Tools.Choose(AnnotateTool.Highlight);
        f.Text.Text.Selection.SelectAll();
        Assert.False(f.Tab.IsDirty);

        var apply = f.Interaction.Markup.ApplyAsync(AnnotateTool.Highlight);

        // The handle was taken before the first await: a close right now would wait for it.
        Assert.True(f.Tab.Annotations.IsDirty);
        var flush = f.Tab.Annotations.FlushAsync();
        await apply.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await flush.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        ViewFixture.Pump();

        Assert.NotEmpty(await f.AnnotationsAsync(0));
        Assert.True(f.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task EscDropsTheSelectionBeforeItEndsTheMode()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var (text, start, end) = await WordAsync(f, "Handbuch");
        f.Tools.Choose(AnnotateTool.Highlight);
        f.Text.Text.Selection.Press(f.Text.CharPoint(text, start, right: false), 1);
        f.Text.Text.Selection.Drag(f.Text.CharPoint(text, end - 1, right: true));
        f.Text.Text.Selection.Release();
        Assert.True(f.Text.Text.Selection.HasSelection);
        f.View.Focus();
        ViewFixture.Pump();

        f.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        ViewFixture.Pump();
        Assert.False(f.Text.Text.Selection.HasSelection);
        Assert.Equal(AnnotateTool.Highlight, f.Tools.Tool);

        f.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        ViewFixture.Pump();
        Assert.Equal(AnnotateTool.None, f.Tools.Tool);
    }

    [AvaloniaFact]
    public async Task EndingTheModeWhileADoubleClickWaitsDropsItAndReleasesThePending()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var (text, start, _) = await WordAsync(f, "Handbuch");
        f.Tools.Choose(AnnotateTool.Highlight);
        f.Click(f.Text.CharPoint(text, start + 3, right: false), 2);
        Assert.True(f.Interaction.Markup.IsWaiting);

        f.Tools.End();
        f.Time.Advance(10_000);
        await f.SettleAsync();

        Assert.False(f.Interaction.Markup.IsWaiting);
        Assert.Empty(await f.AnnotationsAsync(0));
        Assert.False(f.Tab.Annotations.IsDirty);
    }

    [AvaloniaFact]
    public async Task OutsideTheModeTextSelectionAndLinksWorkAsIn21()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var (text, start, end) = await WordAsync(f, "Handbuch");

        // No tool: the drag selects text and makes no markup.
        f.Drag(f.Text.CharPoint(text, start, right: false), f.Text.CharPoint(text, end - 1, right: true));
        await f.SettleAsync();
        Assert.Equal("Handbuch", (await f.Text.Text.Selection.GetTextAsync())!.Plain);
        Assert.Empty(await f.AnnotationsAsync(0));
        Assert.False(f.Tab.IsDirty);

        // No tool: a click on a link opens its card.
        var rect = text.Links.First(l => l.Target is UriTarget).Rects[0];
        var point = f.View.ToView(0, (rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2)!.Value;
        f.Click(point);
        Assert.True(f.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task DragStartingOnALinkSelectsWhileAToolIsActive()
    {
        using var f = await AnnotateFixture.OpenAsync("text-rich.pdf");
        var text = await f.Text.TextOfAsync(0);
        var rect = text.Links.First(l => l.Target is UriTarget).Rects[0];
        var y = (rect.Top + rect.Bottom) / 2;
        f.Tools.Choose(AnnotateTool.Highlight);

        f.Drag(f.View.ToView(0, rect.Left + 1, y)!.Value, f.View.ToView(0, rect.Right - 1, y)!.Value);
        await f.SettleAsync();

        Assert.False(f.Host.IsOpen);
        Assert.Empty(f.Text.Launched);
        Assert.Single(await f.AnnotationsAsync(0));
    }

    [AvaloniaFact]
    public async Task TheModeIndicatorNamesTheToolAndHowToLeave()
    {
        using var window = WindowFixture.Create();
        var tab = await window.OpenAsync("text-rich.pdf");
        var indicator = window.Window.ModeIndicatorOf(tab)!;
        Assert.False(indicator.IsVisible);

        window.Window.Tools.Choose(AnnotateTool.Highlight);
        Assert.True(indicator.IsVisible);
        Assert.Equal("Highlight – Esc to finish", indicator.Message);

        window.Window.Tools.End();
        Assert.False(indicator.IsVisible);
        Assert.Contains("Esc", PdfStrings.GetIn("AnnotateModeFormat", System.Globalization.CultureInfo.GetCultureInfo("de")), StringComparison.Ordinal);
    }
}
