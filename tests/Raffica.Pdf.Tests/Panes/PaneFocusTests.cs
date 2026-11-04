using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Panes;

public sealed class PaneFocusTests
{
    private static void F6(PdfWindow window, bool shift = false)
    {
        window.KeyPress(Key.F6, shift ? RawInputModifiers.Shift : RawInputModifiers.None, PhysicalKey.F6, null);
        ViewFixture.Pump();
    }

    private static bool FocusIsIn(PdfWindow window, Control pane) =>
        window.FocusManager?.GetFocusedElement() is Visual focused && (ReferenceEquals(focused, pane) || focused.GetVisualAncestors().Contains(pane));

    [AvaloniaFact]
    public async Task F6VisitsNavigationThenDocument()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("outline.pdf");
        var view = windows.Window.ViewOf(tab)!;
        await ViewFixture.WaitAsync(() => view.FirstSharpPageShown);
        var window = windows.Window;
        Assert.Equal(LeftPaneKind.Pages, window.LeftPane);
        Assert.True(window.NavigationPane.IsOpen);

        F6(window);
        Assert.True(FocusIsIn(window, window.NavigationPane), "first stop: the navigation pane with the pages");
        Assert.True(window.NavigationPane.IsPaneFocused);

        F6(window);
        Assert.True(FocusIsIn(window, window.DocumentPane), "second stop: the document");
        Assert.False(window.NavigationPane.IsPaneFocused);

        // The comments pane is closed, so the ring wraps to the navigation pane; the outline takes the same stop.
        F6(window);
        Assert.True(FocusIsIn(window, window.NavigationPane));
        window.ShowOutlinePane();
        ViewFixture.Pump();
        Assert.Equal(LeftPaneKind.Outline, window.LeftPane);
        F6(window, shift: true);
        Assert.True(FocusIsIn(window, window.DocumentPane));
        F6(window, shift: true);
        Assert.True(FocusIsIn(window, window.NavigationPane));
    }

    [AvaloniaFact]
    public async Task ClosedPanesAreSkipped()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("outline.pdf");
        var view = windows.Window.ViewOf(tab)!;
        await ViewFixture.WaitAsync(() => view.FirstSharpPageShown);
        var window = windows.Window;

        window.CloseLeftPane();
        ViewFixture.Pump();
        Assert.Equal(LeftPaneKind.None, window.LeftPane);
        Assert.False(window.NavigationPane.IsOpen);

        F6(window);
        Assert.True(FocusIsIn(window, window.DocumentPane));
        F6(window);
        Assert.True(FocusIsIn(window, window.DocumentPane), "only the document is left in the ring");
        Assert.False(window.NavigationPane.IsPaneFocused);

        window.ShowPagesPane();
        ViewFixture.Pump();
        F6(window);
        Assert.True(FocusIsIn(window, window.NavigationPane));
    }
}
