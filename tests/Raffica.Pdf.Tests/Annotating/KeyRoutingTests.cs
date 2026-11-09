using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Annotations;
using Raffica.Core.Text;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Annotating;

/// <summary>While the Select tool is active, only the keys that arrive from inside the document belong to the tool (Esc excepted).</summary>
public sealed class KeyRoutingTests
{
    private static readonly AnnotationColor Red = new(220, 38, 38);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<(PdfTab Tab, PdfAnnotation First)> OpenWithTwoMarksAsync(WindowFixture windows)
    {
        var tab = await windows.OpenAsync("text-rich.pdf");
        var window = windows.Window;
        await ViewFixture.WaitAsync(() => window.ViewOf(tab)!.FirstSharpPageShown);
        var first = (await tab.Annotations.Editor.ApplyDrawingAsync(0, DrawingGeometry.Rectangle(new PageRect(100, 200, 300, 300), 2), Red, null, null))!;
        await tab.Annotations.Editor.ApplyDrawingAsync(0, DrawingGeometry.Rectangle(new PageRect(100, 400, 300, 500), 2), Red, null, null);
        window.Tools.Choose(AnnotateTool.Select);
        var select = window.AnnotateOf(tab)!.Select;
        await select.SelectAsync(new AnnotationLocator(0, first.Index, first.Name));
        for (var i = 0; i < 3; i++)
        {
            ViewFixture.Pump();
            await select.Completion;
            await tab.Annotations.FlushAsync();
        }

        Assert.Equal(first.Name, select.Selected?.Name);
        return (tab, first);
    }

    // The key goes to the control as the keyboard would send it (down the tree, then up); the result says whether anybody took it.
    private static bool Press(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = target };
        target.RaiseEvent(e);
        ViewFixture.Pump();
        return e.Handled;
    }

    private static Button RibbonButton(PdfWindow window) =>
        window.GetVisualDescendants().OfType<Button>().First(
            b => b.Focusable && b.IsEffectivelyVisible && !b.GetVisualAncestors().OfType<PageView>().Any() && !b.GetVisualAncestors().Contains(window.NavigationPane));

    // The keys the Select tool would use are sent from a control outside the document; the mark must be as it was (selected, in place, not
    // deleted, not nudged: a nudge is made after a pause, so the clock of the window is moved past it).
    private static async Task AssertKeysAreLeftAloneAsync(ManualTimers time, PdfTab tab, PdfAnnotation first, PdfWindow window, Control focus)
    {
        var select = window.AnnotateOf(tab)!.Select;
        focus.Focus();
        ViewFixture.Pump();
        foreach (var (key, modifiers) in new[] { (Key.Tab, KeyModifiers.None), (Key.Tab, KeyModifiers.Shift), (Key.Right, KeyModifiers.None), (Key.Down, KeyModifiers.Shift), (Key.Delete, KeyModifiers.None), (Key.Back, KeyModifiers.None), (Key.Enter, KeyModifiers.None) })
        {
            Press(focus, key, modifiers);
            Assert.Equal(first.Name, select.Selected?.Name);
            focus.Focus();
            ViewFixture.Pump();
        }

        time.Advance((int)SelectController.NudgeDelay.TotalMilliseconds + 100);
        for (var i = 0; i < 3; i++)
        {
            ViewFixture.Pump();
            await select.Completion;
            await tab.Annotations.FlushAsync();
        }

        var marks = (await tab.Session.Document!.GetAnnotationsAsync(0, Cancel)).Where(a => a.Kind == AnnotationKind.Rectangle).ToList();
        Assert.Equal(2, marks.Count);
        var box = marks.Single(a => a.Name == first.Name).Drawing!.Box;
        Assert.Equal(100, box.Left, 0.5);
        Assert.Equal(200, box.Top, 0.5);
    }

    [AvaloniaFact]
    public async Task KeysFromTheRibbonStayWithTheRibbon()
    {
        var time = new ManualTimers();
        using var windows = WindowFixture.Create(time: time);
        var (tab, first) = await OpenWithTwoMarksAsync(windows);

        await AssertKeysAreLeftAloneAsync(time, tab, first, windows.Window, RibbonButton(windows.Window));
    }

    [AvaloniaFact]
    public async Task KeysFromThePagesPaneAndTheOutlineStayThere()
    {
        var time = new ManualTimers();
        using var windows = WindowFixture.Create(time: time);
        var (tab, first) = await OpenWithTwoMarksAsync(windows);
        var window = windows.Window;

        ViewFixture.Pump();
        var thumbnail = window.NavigationPane.GetVisualDescendants().OfType<Control>().First(c => c.Focusable && c.IsEffectivelyVisible);
        await AssertKeysAreLeftAloneAsync(time, tab, first, window, thumbnail);

        window.ShowOutlinePane();
        ViewFixture.Pump();
        var outline = window.NavigationPane.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Focusable && c.IsEffectivelyVisible);
        if (outline is not null)
        {
            await AssertKeysAreLeftAloneAsync(time, tab, first, window, outline);
        }
    }

    [AvaloniaFact]
    public async Task TheDocumentStillGetsItsToolKeys()
    {
        using var windows = WindowFixture.Create();
        var (tab, first) = await OpenWithTwoMarksAsync(windows);
        var window = windows.Window;
        var view = window.ViewOf(tab)!;
        view.Focus();
        ViewFixture.Pump();

        // Tab inside the document steps to the next mark: the tool takes it, once.
        Assert.True(Press(view, Key.Tab));
        Assert.NotEqual(first.Name, window.AnnotateOf(tab)!.Select.Selected?.Name);
        Assert.True(Press(view, Key.Delete));
        await window.AnnotateOf(tab)!.Select.Completion;
        var marks = (await tab.Session.Document!.GetAnnotationsAsync(0, Cancel)).Where(a => a.Kind == AnnotationKind.Rectangle).ToList();
        Assert.Single(marks);
    }

    [AvaloniaFact]
    public async Task EscFromAnywhereStillEndsTheMode()
    {
        using var windows = WindowFixture.Create();
        var (tab, first) = await OpenWithTwoMarksAsync(windows);
        var window = windows.Window;
        var button = RibbonButton(window);
        button.Focus();
        ViewFixture.Pump();

        // The first Esc drops the selection, the second ends the mode.
        Assert.True(Press(button, Key.Escape));
        Assert.False(window.AnnotateOf(tab)!.Select.HasSelection);
        Assert.Equal(AnnotateTool.Select, window.Tools.Tool);
        Assert.True(Press(button, Key.Escape));
        Assert.Equal(AnnotateTool.None, window.Tools.Tool);
        Assert.NotNull(first);
    }
}
