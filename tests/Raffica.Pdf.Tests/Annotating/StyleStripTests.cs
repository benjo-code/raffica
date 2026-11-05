using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Raffica.Core.Annotations;
using Raffica.Core.Text;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Annotating;

/// <summary>The colour and width strip of the annotate mode: who sees it, what it changes, how it is named and what it leaves alone.</summary>
public sealed class StyleStripTests
{
    private static readonly AnnotationColor Red = new(220, 30, 30);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<(WindowFixture Windows, PdfTab Tab, StyleStrip Strip)> OpenAsync(string file = "text-rich.pdf")
    {
        var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync(file);
        await ViewFixture.WaitAsync(() => windows.Window.ViewOf(tab)!.FirstSharpPageShown);
        return (windows, tab, windows.Window.StyleStripOf(tab)!);
    }

    private static void Click(ToggleButton button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
    }

    private static Point At(WindowFixture windows, double x, double y)
    {
        var view = windows.Window.ActiveView!;
        return view.TranslatePoint(view.ToView(0, x, y)!.Value, windows.Window)!.Value;
    }

    private static void Drag(WindowFixture windows, Point from, Point to)
    {
        windows.Window.MouseDown(from, MouseButton.Left);
        for (var i = 1; i <= 6; i++)
        {
            windows.Window.MouseMove(new Point(from.X + ((to.X - from.X) * i / 6), from.Y + ((to.Y - from.Y) * i / 6)));
        }

        windows.Window.MouseUp(to, MouseButton.Left);
        ViewFixture.Pump();
    }

    private static async Task SettleAsync(WindowFixture windows, PdfTab tab)
    {
        var interaction = windows.Window.AnnotateOf(tab)!;
        for (var i = 0; i < 3; i++)
        {
            ViewFixture.Pump();
            await interaction.Drawing.Completion;
            await interaction.Select.Completion;
            await tab.Annotations.FlushAsync();
        }
    }

    private static async Task<IReadOnlyList<PdfAnnotation>> RectanglesAsync(PdfTab tab) =>
        (await tab.Session.Document!.GetAnnotationsAsync(0, Cancel)).Where(a => a.Kind is AnnotationKind.Rectangle or AnnotationKind.Ink).ToList();

    private static async Task SelectRectangleAsync(WindowFixture windows, PdfTab tab)
    {
        var made = (await tab.Annotations.Editor.ApplyDrawingAsync(0, DrawingGeometry.Rectangle(new PageRect(100, 300, 300, 400), 2), Red, null, null))!;
        windows.Window.Tools.Choose(AnnotateTool.Select);
        var select = windows.Window.AnnotateOf(tab)!.Select;
        await select.SelectAsync(new AnnotationLocator(0, made.Index, made.Name));
        await SettleAsync(windows, tab);
        Assert.Equal(made.Name, select.Selected?.Name);
    }

    [AvaloniaFact]
    public async Task TheStripShowsOnlyWhileAToolIsActiveAndOffersWhatTheToolHas()
    {
        var (windows, _, strip) = await OpenAsync();
        using var _ = windows;
        var tools = windows.Window.Tools;

        Assert.False(strip.IsVisible);

        // Text markup: five colours, no width.
        tools.Choose(AnnotateTool.Highlight);
        Assert.True(strip.IsVisible);
        Assert.Equal(5, strip.Swatches.Count);
        Assert.Empty(strip.WidthButtons);

        // A drawing tool: five colours and three widths. The text box tool: five colours and three sizes.
        tools.Choose(AnnotateTool.Freehand);
        Assert.Equal(5, strip.Swatches.Count);
        Assert.Equal(3, strip.WidthButtons.Count);
        tools.Choose(AnnotateTool.TextBox);
        Assert.Equal(5, strip.Swatches.Count);
        Assert.Equal(3, strip.WidthButtons.Count);

        // The select tool shows it only for a selected mark.
        tools.Choose(AnnotateTool.Select);
        Assert.False(strip.IsVisible);
        tools.End();
        Assert.False(strip.IsVisible);
    }

    [AvaloniaFact]
    public async Task ChoosingASwatchChangesTheToolColorAndTheNextStrokeUsesIt()
    {
        var (windows, tab, strip) = await OpenAsync();
        using var _ = windows;
        var tools = windows.Window.Tools;
        tools.Choose(AnnotateTool.Freehand);
        Assert.Equal(DrawColor.Red, tools.StyleOf(AnnotateTool.Freehand).Color);

        var blue = strip.Swatches.Single(b => strip.ColorOf(b) == DrawPalette.ColorOf(DrawColor.Blue));
        Click(blue);

        Assert.Equal(DrawColor.Blue, tools.StyleOf(AnnotateTool.Freehand).Color);
        Assert.True(blue.IsChecked);
        Assert.Single(strip.Swatches, b => b.IsChecked == true);

        // It is kept in the settings (the app settings are the ones the window writes).
        Assert.Equal(DrawColor.Blue, windows.Window.Settings.App.StyleOf(AnnotationKind.Ink).Color);

        // The next stroke has that colour.
        Drag(windows, At(windows, 100, 300), At(windows, 220, 360));
        await SettleAsync(windows, tab);
        var stroke = Assert.Single(await RectanglesAsync(tab));
        Assert.Equal(DrawPalette.ColorOf(DrawColor.Blue), stroke.Color);
    }

    [AvaloniaFact]
    public async Task ChoosingAMarkupColorChangesTheNextMarkup()
    {
        var (windows, tab, strip) = await OpenAsync();
        using var _ = windows;
        var tools = windows.Window.Tools;
        tools.Choose(AnnotateTool.Highlight);

        Click(strip.Swatches.Single(b => strip.ColorOf(b) == AnnotationPalette.ColorOf(MarkupColor.Green)));

        Assert.Equal(AnnotationPalette.ColorOf(MarkupColor.Green), tools.MarkupColor(AnnotateTool.Highlight));
        Assert.Equal(AnnotationPalette.ColorOf(MarkupColor.Blue), tools.MarkupColor(AnnotateTool.Underline));
        Assert.Equal(1, strip.Swatches.Count(b => b.IsChecked == true));
        Assert.NotNull(tab);
    }

    [AvaloniaFact]
    public async Task WidthAndSizeButtonsSetWhatTheToolRemembers()
    {
        var (windows, tab, strip) = await OpenAsync();
        using var _ = windows;
        var tools = windows.Window.Tools;
        tools.Choose(AnnotateTool.Rectangle);
        Assert.Equal(StrokeWidth.Medium, tools.StyleOf(AnnotateTool.Rectangle).Width);

        Click(strip.WidthButtons[2]);
        Assert.Equal(StrokeWidth.Thick, tools.StyleOf(AnnotateTool.Rectangle).Width);
        Assert.Equal([false, false, true], strip.WidthButtons.Select(b => b.IsChecked == true));

        // The width of one drawing tool is not the width of another.
        tools.Choose(AnnotateTool.Ellipse);
        Assert.Equal(StrokeWidth.Medium, tools.StyleOf(AnnotateTool.Ellipse).Width);

        // The next rectangle is 4 pt wide.
        tools.Choose(AnnotateTool.Rectangle);
        Drag(windows, At(windows, 100, 300), At(windows, 220, 380));
        await SettleAsync(windows, tab);
        var made = Assert.Single(await RectanglesAsync(tab));
        Assert.Equal(4, made.Drawing!.Width, 0.01);

        // The text box tool: three sizes.
        tools.Choose(AnnotateTool.TextBox);
        Click(strip.WidthButtons[0]);
        Assert.Equal(StrokeWidth.Thin, tools.StyleOf(AnnotateTool.TextBox).Width);
        Assert.Equal(TextBoxSpec.ToolFontSizes[0], TextBoxSpec.FontSizeOf(tools.StyleOf(AnnotateTool.TextBox).Width));
    }

    [AvaloniaFact]
    public async Task WithASelectedMarkAClickChangesTheMarkInOneUndoStepEach()
    {
        var (windows, tab, strip) = await OpenAsync();
        using var _ = windows;
        await SelectRectangleAsync(windows, tab);
        Assert.True(strip.IsVisible);
        Assert.True(strip.ActsOnSelection);
        Assert.Equal(3, strip.WidthButtons.Count);

        // The mark's own colour and width are the ones shown as chosen.
        Assert.Equal(1, strip.Swatches.Count(b => b.IsChecked == true));
        Assert.True(strip.Swatches.Single(b => strip.ColorOf(b) == Red).IsChecked);
        Assert.True(strip.WidthButtons[1].IsChecked);

        Click(strip.Swatches.Single(b => strip.ColorOf(b) == DrawPalette.ColorOf(DrawColor.Blue)));
        await SettleAsync(windows, tab);
        var changed = Assert.Single(await RectanglesAsync(tab));
        Assert.Equal(DrawPalette.ColorOf(DrawColor.Blue), changed.Color);
        Assert.True(strip.Swatches.Single(b => strip.ColorOf(b) == DrawPalette.ColorOf(DrawColor.Blue)).IsChecked);

        Click(strip.WidthButtons[2]);
        await SettleAsync(windows, tab);
        Assert.Equal(4, Assert.Single(await RectanglesAsync(tab)).Drawing!.Width, 0.01);

        // The tool's own remembered style was not touched by changing a mark.
        Assert.Equal(DrawColor.Red, windows.Window.Tools.StyleOf(AnnotateTool.Freehand).Color);

        // One undo takes the width back, the next one the colour.
        await tab.Annotations.UndoAsync();
        await SettleAsync(windows, tab);
        var undone = Assert.Single(await RectanglesAsync(tab));
        Assert.Equal(2, undone.Drawing!.Width, 0.01);
        Assert.Equal(DrawPalette.ColorOf(DrawColor.Blue), undone.Color);
        await tab.Annotations.UndoAsync();
        await SettleAsync(windows, tab);
        Assert.Equal(Red, Assert.Single(await RectanglesAsync(tab)).Color);
    }

    [AvaloniaFact]
    public async Task AMarkThatCannotBeChangedDisablesTheStripWithTheReason()
    {
        var (windows, tab, strip) = await OpenAsync("drawings-foreign.pdf");
        using var _ = windows;
        var locked = (await tab.Session.Document!.GetAnnotationsAsync(0, Cancel)).First(a => a.Drawing is not null && !a.GeometryEditable && a.Editable);
        windows.Window.Tools.Choose(AnnotateTool.Select);
        var select = windows.Window.AnnotateOf(tab)!.Select;
        await select.SelectAsync(new AnnotationLocator(0, locked.Index, locked.Name));
        await SettleAsync(windows, tab);
        Assert.True(select.HasSelection);

        Assert.True(strip.IsVisible);
        Assert.All(strip.Swatches, b => Assert.False(b.IsEnabled));
        Assert.All(strip.WidthButtons, b => Assert.False(b.IsEnabled));
        Assert.Equal(PdfStrings.SelectLockedReason, strip.DisabledReason);
        Assert.All(strip.Swatches, b => Assert.Contains(PdfStrings.SelectLockedReason, ToolTip.GetTip(b)?.ToString()));

        // A click on a disabled button changes nothing.
        Click(strip.Swatches[0]);
        await SettleAsync(windows, tab);
        Assert.False(tab.Annotations.CanUndo);
    }

    [AvaloniaFact]
    public async Task EveryButtonHasAUniqueNameAndCanTakeTheFocus()
    {
        var (windows, tab, strip) = await OpenAsync();
        using var _ = windows;
        foreach (var tool in new[] { AnnotateTool.Highlight, AnnotateTool.Freehand, AnnotateTool.TextBox })
        {
            windows.Window.Tools.Choose(tool);
            var buttons = strip.Swatches.Concat(strip.WidthButtons).ToList();
            var names = buttons.Select(AutomationProperties.GetName).ToList();
            Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
            Assert.Equal(names.Count, names.Distinct().Count());
            Assert.All(buttons, b => Assert.True(b.Focusable && b.IsTabStop));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(strip)));
            windows.Window.Tools.End();
        }

        windows.Window.Tools.Choose(AnnotateTool.Freehand);
        Assert.Equal(["Red", "Black", "Blue", "Green", "Orange", "Thin, 1 pt", "Medium, 2 pt", "Thick, 4 pt"],
            strip.Swatches.Concat(strip.WidthButtons).Select(AutomationProperties.GetName));
        Assert.NotNull(tab);
    }

    [AvaloniaFact]
    public async Task KeysOnTheStripStayOnTheStripAndEscStillEndsTheMode()
    {
        var (windows, tab, strip) = await OpenAsync();
        using var _ = windows;
        await SelectRectangleAsync(windows, tab);
        var select = windows.Window.AnnotateOf(tab)!.Select;
        var swatch = strip.Swatches[2];
        swatch.Focus();
        ViewFixture.Pump();

        foreach (var key in new[] { Key.Delete, Key.Back, Key.Right, Key.Tab })
        {
            swatch.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = swatch });
            ViewFixture.Pump();
            Assert.True(select.HasSelection, key.ToString());
            swatch.Focus();
        }

        await SettleAsync(windows, tab);
        Assert.Single(await RectanglesAsync(tab));

        // Esc from the strip: the first press drops the selection, the second ends the mode.
        swatch.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = swatch });
        ViewFixture.Pump();
        Assert.False(select.HasSelection);
        swatch.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = swatch });
        ViewFixture.Pump();
        Assert.Equal(AnnotateTool.None, windows.Window.Tools.Tool);
        Assert.False(strip.IsVisible);
    }
}
