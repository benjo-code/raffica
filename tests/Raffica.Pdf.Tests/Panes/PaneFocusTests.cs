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

    private static ListBoxItem? FocusedItem(PdfWindow window) =>
        window.FocusManager?.GetFocusedElement() is Visual focused ? focused.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() : null;

    private static void Press(PdfWindow window, Key key, PhysicalKey physical)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        ViewFixture.Pump();
    }

    [AvaloniaFact]
    public async Task F6IntoThePagesPaneFocusesTheCurrentPageAndKeysActOnTheFocusedRow()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("pages-marked.pdf");
        var window = windows.Window;
        var view = window.ViewOf(tab)!;
        await ViewFixture.WaitAsync(() => view.FirstSharpPageShown);
        view.GoToPage(2);
        ViewFixture.Pump();
        var pane = (PagesPane)window.NavigationPane.Content!;

        F6(window);
        Assert.True(FocusIsIn(window, window.NavigationPane));
        var focused = FocusedItem(window);
        Assert.NotNull(focused);
        Assert.Equal(2, pane.List.IndexFromContainer(focused));
        Assert.Equal(2, pane.List.SelectedIndex);

        // Down moves from the focused row, Enter acts on the row that has the focus.
        Press(window, Key.Down, PhysicalKey.ArrowDown);
        Assert.Equal(3, pane.List.IndexFromContainer(FocusedItem(window)!));
        Press(window, Key.Enter, PhysicalKey.Enter);
        Assert.Equal(3, view.CurrentPage);
    }

    [AvaloniaFact]
    public async Task F6IntoTheOutlineFocusesAndSelectsTheFirstRowAndKeysActOnTheFocusedRow()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("outline.pdf");
        var window = windows.Window;
        var view = window.ViewOf(tab)!;
        await ViewFixture.WaitAsync(() => view.FirstSharpPageShown);
        window.ShowOutlinePane();
        var pane = (OutlinePane)window.NavigationPane.Content!;
        await pane.LoadTask;
        ViewFixture.Pump();
        view.GoToPage(2);
        ViewFixture.Pump();

        F6(window);
        var focused = FocusedItem(window);
        Assert.NotNull(focused);
        Assert.Equal(0, pane.List.IndexFromContainer(focused));
        Assert.Equal(0, pane.List.SelectedIndex);

        // Enter on the first row (Kapitel 1, page 1) jumps.
        Press(window, Key.Enter, PhysicalKey.Enter);
        Assert.Equal(0, view.CurrentPage);

        // Down to Abschnitt 1.1, Enter acts on that row.
        Press(window, Key.Down, PhysicalKey.ArrowDown);
        Assert.Equal(1, pane.List.IndexFromContainer(FocusedItem(window)!));
        Press(window, Key.Enter, PhysicalKey.Enter);
        Assert.Equal(1, view.CurrentPage);

        // Right on a closed entry opens the one that has the focus (Kapitel 2).
        var chapter2 = Enumerable.Range(0, pane.RowCount).First(i => pane.RowTitle(i) == "Kapitel 2");
        Assert.True(pane.ContainerOf(chapter2)!.Focus(NavigationMethod.Tab));
        var before = pane.RowCount;
        Press(window, Key.Right, PhysicalKey.ArrowRight);
        Assert.Equal(before + 1, pane.RowCount);
    }

    [AvaloniaFact]
    public async Task PaneJoinsTheF6RingWithFieldCode7()
    {
        using var windows = WindowFixture.Create(keyTips: true);
        var tab = await windows.OpenAsync("outline.pdf");
        var window = windows.Window;
        var view = window.ViewOf(tab)!;
        await ViewFixture.WaitAsync(() => view.FirstSharpPageShown);
        Assert.Equal("7", Raffica.Shell.Frame.FieldCodeBadge.GetCode(window.CommentsPane));

        // The marks pane is closed first, so it is passed over; once it is open it is the third stop.
        window.ToggleMarksPane();
        ViewFixture.Pump();
        Assert.True(window.CommentsPane.IsOpen);
        Assert.Equal(256, window.CommentsPane.Width);

        F6(window);
        Assert.True(FocusIsIn(window, window.NavigationPane));
        F6(window);
        Assert.True(FocusIsIn(window, window.DocumentPane));
        F6(window);
        Assert.True(FocusIsIn(window, window.CommentsPane), "third stop: the marks pane");
        Assert.True(window.CommentsPane.IsPaneFocused);
        F6(window);
        Assert.True(FocusIsIn(window, window.NavigationPane));
        F6(window, shift: true);
        Assert.True(FocusIsIn(window, window.CommentsPane));
    }

    [AvaloniaFact]
    public async Task ClosingTheMarksPaneWhileFocusedMovesToTheDocument()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("outline.pdf");
        var window = windows.Window;
        var view = window.ViewOf(tab)!;
        await ViewFixture.WaitAsync(() => view.FirstSharpPageShown);
        window.ToggleMarksPane();
        ViewFixture.Pump();
        F6(window);
        F6(window);
        F6(window);
        Assert.True(FocusIsIn(window, window.CommentsPane));

        window.ToggleMarksPane();
        ViewFixture.Pump();
        Assert.False(window.CommentsPane.IsOpen);
        Assert.True(FocusIsIn(window, window.DocumentPane));
    }
}
