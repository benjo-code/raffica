using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Commands;

public sealed class ShortcutTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    private static void Press(PdfWindow window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        ViewFixture.Pump();
    }

    private static void Ctrl(PdfWindow window, Key key, PhysicalKey physical, bool shift = false) =>
        Press(window, key, physical, shift ? RawInputModifiers.Control | RawInputModifiers.Shift : RawInputModifiers.Control);

    private static void ClickCard(PdfWindow window, string name)
    {
        var footer = Assert.IsType<StackPanel>(window.Host.ActiveDialog!.Footer);
        footer.Children.OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
    }

    [AvaloniaFact]
    public async Task CtrlWClosesTheActiveTabThroughTheGuard()
    {
        using var windows = WindowFixture.Create();
        var first = await windows.OpenAsync("hello_world.pdf");
        var second = await windows.OpenAsync("text-basic.pdf");
        var window = windows.Window;
        Assert.Same(second, window.Workspace.Active);

        // A dirty tab asks first; Cancel keeps it.
        second.MarkDirty();
        Ctrl(window, Key.W, PhysicalKey.W);
        Assert.True(window.Host.IsOpen, "the unsaved card must ask");
        Assert.Equal(2, window.Workspace.Tabs.Count);
        ClickCard(window, "PART_Cancel");
        Assert.Equal(2, window.Workspace.Tabs.Count);

        // Don't save closes it.
        Ctrl(window, Key.W, PhysicalKey.W);
        ClickCard(window, "PART_DontSave");
        await ViewFixture.WaitAsync(() => window.Workspace.Tabs.Count == 1);
        Assert.Same(first, window.Workspace.Active);

        // A clean tab closes at once and the start view comes back.
        Ctrl(window, Key.W, PhysicalKey.W);
        await ViewFixture.WaitAsync(() => window.Workspace.Tabs.Count == 0);
        Assert.Same(window.Start, window.Frame.Document);
    }

    [AvaloniaFact]
    public async Task CtrlTabCyclesTabs()
    {
        using var windows = WindowFixture.Create();
        var a = await windows.OpenAsync("hello_world.pdf");
        var b = await windows.OpenAsync("text-basic.pdf");
        var c = await windows.OpenAsync("pages-marked.pdf");
        var window = windows.Window;
        Assert.Same(c, window.Workspace.Active);

        Ctrl(window, Key.Tab, PhysicalKey.Tab);
        Assert.Same(a, window.Workspace.Active);
        Ctrl(window, Key.Tab, PhysicalKey.Tab, shift: true);
        Assert.Same(c, window.Workspace.Active);
        Ctrl(window, Key.Tab, PhysicalKey.Tab, shift: true);
        Assert.Same(b, window.Workspace.Active);
    }

    [AvaloniaFact]
    public async Task ShortcutsAreIgnoredWhileADialogIsOpen()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("pages-marked.pdf");
        await windows.OpenAsync("text-basic.pdf");
        var window = windows.Window;
        window.Workspace.Select(tab);
        ViewFixture.Pump();
        var view = window.ViewOf(tab)!;
        view.SetZoom(1.0);

        var asking = window.GoToPageAsync();
        Assert.True(window.Host.IsOpen);
        var card = window.Host.ActiveDialog;

        Ctrl(window, Key.W, PhysicalKey.W);
        Ctrl(window, Key.Tab, PhysicalKey.Tab);
        Ctrl(window, Key.OemPlus, PhysicalKey.Equal);
        Ctrl(window, Key.D1, PhysicalKey.Digit1);
        Ctrl(window, Key.D2, PhysicalKey.Digit2, shift: true);
        Ctrl(window, Key.D, PhysicalKey.D, shift: true);
        Ctrl(window, Key.F, PhysicalKey.F);
        Press(window, Key.F1, PhysicalKey.F1);

        Assert.Same(card, window.Host.ActiveDialog);
        Assert.Equal(2, window.Workspace.Tabs.Count);
        Assert.Same(tab, window.Workspace.Active);
        Assert.Equal(1.0, view.Zoom);
        Assert.Equal(ZoomMode.Free, view.ZoomMode);
        Assert.Equal(PageTheme.Light, view.PageTheme);
        Assert.Equal(LeftPaneKind.Pages, window.LeftPane);
        Assert.Null(window.ShownSearchBar);

        ClickCard(window, "PART_Cancel");
        await asking;
    }

    [AvaloniaFact]
    public async Task CtrlPlusAndMinusStepTheZoomOnEveryKeyboard()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("text-rich.pdf");
        var window = windows.Window;
        var view = window.ViewOf(tab)!;
        view.SetZoom(1.0);
        view.Focus();

        Ctrl(window, Key.OemPlus, PhysicalKey.Equal);
        Assert.Equal(1.25, view.Zoom);
        Ctrl(window, Key.OemPlus, PhysicalKey.Equal, shift: true);
        Assert.Equal(1.5, view.Zoom);
        Ctrl(window, Key.OemMinus, PhysicalKey.Minus);
        Assert.Equal(1.25, view.Zoom);
        Ctrl(window, Key.Add, PhysicalKey.NumPadAdd);
        Assert.Equal(1.5, view.Zoom);
        Ctrl(window, Key.Subtract, PhysicalKey.NumPadSubtract);
        Assert.Equal(1.25, view.Zoom);

        // The key does not depend on where the focus is: from the search field of the title bar it zooms the same way.
        window.TitleSearch.FocusAndSelect();
        Ctrl(window, Key.OemPlus, PhysicalKey.Equal);
        Assert.Equal(1.5, view.Zoom);

        // Without Ctrl the keys do not zoom.
        Press(window, Key.OemPlus, PhysicalKey.Equal);
        Assert.Equal(1.5, view.Zoom);
    }

    [AvaloniaFact]
    public async Task ActualSizeFitWidthAndFitPageHaveTheirKeys()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("text-rich.pdf");
        var window = windows.Window;
        var view = window.ViewOf(tab)!;

        Ctrl(window, Key.D1, PhysicalKey.Digit1);
        Assert.Equal(ZoomMode.FitWidth, view.ZoomMode);
        Ctrl(window, Key.D2, PhysicalKey.Digit2);
        Assert.Equal(ZoomMode.FitPage, view.ZoomMode);
        Ctrl(window, Key.D0, PhysicalKey.Digit0);
        Assert.Equal(ZoomMode.ActualSize, view.ZoomMode);
        Assert.Equal(1.0, view.Zoom);
        Ctrl(window, Key.NumPad1, PhysicalKey.NumPad1);
        Assert.Equal(ZoomMode.FitWidth, view.ZoomMode);
    }

    [AvaloniaFact]
    public async Task CtrlShiftDTogglesTheDarkPageOfTheActiveTabOnly()
    {
        using var windows = WindowFixture.Create();
        var first = await windows.OpenAsync("text-rich.pdf");
        var second = await windows.OpenAsync("text-basic.pdf");
        var window = windows.Window;
        var firstView = window.ViewOf(first)!;
        var secondView = window.ViewOf(second)!;

        Ctrl(window, Key.D, PhysicalKey.D, shift: true);
        Assert.Equal(PageTheme.Dark, secondView.PageTheme);
        Assert.Equal(PageTheme.Light, firstView.PageTheme);

        window.Workspace.Select(first);
        ViewFixture.Pump();
        Assert.Equal(PageTheme.Light, firstView.PageTheme);
        Ctrl(window, Key.D, PhysicalKey.D, shift: true);
        Assert.Equal(PageTheme.Dark, firstView.PageTheme);
        Ctrl(window, Key.D, PhysicalKey.D, shift: true);
        Assert.Equal(PageTheme.Light, firstView.PageTheme);
        Assert.Equal(PageTheme.Dark, secondView.PageTheme);

        // A per-tab choice, no setting: nothing was written.
        Assert.False(File.Exists(windows.AppSettingsPath));
        Assert.False(File.Exists(Path.Combine(windows.Folder, "settings.json")));
    }

    [AvaloniaFact]
    public async Task FindGoToAndThePanesHaveTheirKeys()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("pages-marked.pdf");
        var window = windows.Window;

        Ctrl(window, Key.F, PhysicalKey.F);
        Assert.NotNull(window.ShownSearchBar);

        Ctrl(window, Key.G, PhysicalKey.G);
        Assert.True(window.Host.IsOpen);
        ClickCard(window, "PART_Cancel");

        Ctrl(window, Key.D2, PhysicalKey.Digit2, shift: true);
        Assert.Equal(LeftPaneKind.Outline, window.LeftPane);
        Ctrl(window, Key.D1, PhysicalKey.Digit1, shift: true);
        Assert.Equal(LeftPaneKind.Pages, window.LeftPane);
        Assert.NotNull(tab);
    }

    [AvaloniaFact]
    public async Task F3AndShiftF3MoveThroughTheHits()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("text-rich.pdf");
        var window = windows.Window;
        window.ShowSearchBar();
        ViewFixture.Pump();
        var session = window.SearchOf(tab)!;
        window.ShownSearchBar!.Query = "seite";
        ViewFixture.Pump();
        await session.Completion.WaitAsync(Limit);
        ViewFixture.Pump();
        Assert.Equal(2, session.Hits.Count);
        Assert.Equal(0, session.Current);

        Press(window, Key.F3, PhysicalKey.F3);
        Assert.Equal(1, session.Current);
        Press(window, Key.F3, PhysicalKey.F3, RawInputModifiers.Shift);
        Assert.Equal(0, session.Current);
    }

    [AvaloniaFact]
    public async Task CtrlKFocusesTheSearchFieldOfTheTitleBar()
    {
        using var windows = WindowFixture.Create();
        await windows.OpenAsync("text-basic.pdf");
        var window = windows.Window;

        Ctrl(window, Key.K, PhysicalKey.K);

        var focused = window.FocusManager?.GetFocusedElement() as Visual;
        Assert.NotNull(focused);
        Assert.True(ReferenceEquals(focused, window.TitleSearch) || focused.GetVisualAncestors().Contains(window.TitleSearch));
    }

    [AvaloniaFact]
    public async Task F1OpensTheHelpCardAndEscapeClosesIt()
    {
        using var windows = WindowFixture.Create();
        await windows.OpenAsync("text-basic.pdf");
        var window = windows.Window;

        Press(window, Key.F1, PhysicalKey.F1);
        Assert.True(window.Host.IsOpen);
        var texts = window.Host.ActiveDialog!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Ctrl+O", texts);
        Assert.Contains("Ctrl+Shift+D", texts);

        // A second F1 does not stack another card; Esc closes it.
        Press(window, Key.F1, PhysicalKey.F1);
        Assert.True(window.Host.IsOpen);
        Press(window, Key.Escape, PhysicalKey.Escape);
        Assert.False(window.Host.IsOpen);

        // The button closes it as well.
        Press(window, Key.F1, PhysicalKey.F1);
        Assert.True(window.Host.IsOpen);
        ClickCard(window, "PART_Ok");
        Assert.False(window.Host.IsOpen);
    }

    // A held key sends a key down again and again without a key up in between.
    private static void Hold(PdfWindow window, Key key, PhysicalKey physical, RawInputModifiers modifiers, int downs)
    {
        for (var i = 0; i < downs; i++)
        {
            window.KeyPress(key, modifiers, physical, null);
        }

        window.KeyRelease(key, modifiers, physical, null);
        ViewFixture.Pump();
    }

    [AvaloniaFact]
    public async Task AHeldKeyRunsAOneShotCommandOnlyOnce()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("pages-marked.pdf");
        var window = windows.Window;
        var view = window.ViewOf(tab)!;

        // Ctrl+Shift+D held for four key downs toggles once (an even number of toggles would end light again).
        Hold(window, Key.D, PhysicalKey.D, RawInputModifiers.Control | RawInputModifiers.Shift, 4);
        Assert.Equal(PageTheme.Dark, view.PageTheme);

        // Pressed and released again it is a new press.
        Hold(window, Key.D, PhysicalKey.D, RawInputModifiers.Control | RawInputModifiers.Shift, 1);
        Assert.Equal(PageTheme.Light, view.PageTheme);
    }

    [AvaloniaFact]
    public async Task AHeldZoomKeyKeepsZooming()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("text-rich.pdf");
        var window = windows.Window;
        var view = window.ViewOf(tab)!;
        view.SetZoom(1.0);

        Hold(window, Key.OemPlus, PhysicalKey.Equal, RawInputModifiers.Control, 3);
        Assert.Equal(ZoomSteps.Next(ZoomSteps.Next(ZoomSteps.Next(1.0))), view.Zoom);
    }

    [AvaloniaFact]
    public async Task AHeldCloseTabKeyClosesOneTab()
    {
        using var windows = WindowFixture.Create();
        await windows.OpenAsync("hello_world.pdf");
        await windows.OpenAsync("text-basic.pdf");
        var window = windows.Window;

        Hold(window, Key.W, PhysicalKey.W, RawInputModifiers.Control, 5);
        await ViewFixture.WaitAsync(() => window.Workspace.Tabs.Count == 1);
        ViewFixture.Pump();
        Assert.Single(window.Workspace.Tabs);
    }

    [AvaloniaFact]
    public void ASecondOpenWhileThePickerIsShowingIsIgnored()
    {
        using var windows = WindowFixture.Create();
        var window = windows.Window;
        var calls = 0;
        var answer = new TaskCompletionSource<IReadOnlyList<string>>();
        window.FilePicker = () =>
        {
            calls++;
            return answer.Task;
        };

        var first = window.OpenFromDialogAsync();
        var second = window.OpenFromDialogAsync();
        Ctrl(window, Key.O, PhysicalKey.O);
        Assert.Equal(1, calls);
        Assert.True(second.IsCompleted);
        Assert.False(first.IsCompleted);

        answer.SetResult([]);
        for (var i = 0; i < 200 && !first.IsCompleted; i++)
        {
            ViewFixture.Pump();
        }

        Assert.True(first.IsCompleted);
        var third = window.OpenFromDialogAsync();
        Assert.Equal(2, calls);
        Assert.True(third.IsCompleted);
    }

    [AvaloniaFact]
    public async Task CtrlCInATextBoxCopiesTheTextBoxAndNotTheDocumentSelection()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("text-rich.pdf");
        var window = windows.Window;
        var view = window.ViewOf(tab)!;
        await ViewFixture.WaitAsync(() => view.FirstSharpPageShown);
        var selection = window.TextOf(tab)!.Selection;
        for (var i = 0; i < 400 && !selection.HasSelection; i++)
        {
            selection.SelectAll();
            await Task.Delay(25);
            ViewFixture.Pump();
        }

        Assert.True(selection.HasSelection);

        window.ShowSearchBar();
        ViewFixture.Pump();
        var bar = window.ShownSearchBar!;
        bar.Query = "Zeichen";
        ViewFixture.Pump();
        bar.Input.Focus();
        bar.Input.SelectAll();
        var clipboard = window.Clipboard!;
        await clipboard.ClearAsync();

        Ctrl(window, Key.C, PhysicalKey.C);
        string? copied = null;
        for (var i = 0; i < 100 && copied is null; i++)
        {
            copied = await clipboard.TryGetTextAsync();
            if (copied is null)
            {
                await Task.Delay(10);
                ViewFixture.Pump();
            }
        }

        Assert.Equal("Zeichen", copied);
    }

    [AvaloniaFact]
    public async Task CtrlAAndCtrlCStayWithTheTextBoxThatHasTheFocus()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("text-rich.pdf");
        var window = windows.Window;
        await ViewFixture.WaitAsync(() => window.ViewOf(tab)!.FirstSharpPageShown);
        window.ShowSearchBar();
        ViewFixture.Pump();
        var bar = window.ShownSearchBar!;
        bar.Query = "seite";
        ViewFixture.Pump();
        bar.Input.Focus();
        bar.Input.SelectionStart = bar.Input.SelectionEnd = 0;

        Ctrl(window, Key.A, PhysicalKey.A);

        // The text box did its own select-all; the document selected nothing.
        Assert.Equal(0, bar.Input.SelectionStart);
        Assert.Equal(5, bar.Input.SelectionEnd);
        Assert.False(window.TextOf(tab)!.Selection.HasSelection);
    }

    [AvaloniaFact]
    public async Task CtrlAInTheDocumentSelectsTheShownText()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("text-rich.pdf");
        var window = windows.Window;
        var view = window.ViewOf(tab)!;
        await ViewFixture.WaitAsync(() => view.FirstSharpPageShown);
        view.Focus();
        var selection = window.TextOf(tab)!.Selection;
        Assert.False(selection.HasSelection);

        for (var i = 0; i < 400 && !selection.HasSelection; i++)
        {
            Ctrl(window, Key.A, PhysicalKey.A);
            if (!selection.HasSelection)
            {
                await Task.Delay(25);
            }
        }

        Assert.True(selection.HasSelection);
    }

    [AvaloniaFact]
    public async Task PrintAndSettingsAreInTheCatalogButDoNothingYet()
    {
        using var windows = WindowFixture.Create();
        await windows.OpenAsync("text-basic.pdf");
        var window = windows.Window;

        Ctrl(window, Key.P, PhysicalKey.P);
        Ctrl(window, Key.OemComma, PhysicalKey.Comma);

        Assert.False(window.Host.IsOpen);
        var commands = window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).ToList();
        Assert.False(commands.Single(c => c.Id == "file.print").IsEnabled);
        Assert.False(commands.Single(c => c.Id == "file.settings").IsEnabled);
    }

    [AvaloniaFact]
    public async Task TheRibbonFollowsTheOpenDocuments()
    {
        using var windows = WindowFixture.Create();
        var window = windows.Window;
        var commands = window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).ToList();

        // Without a document: Open and Help (Settings has no action yet, so it is disabled too).
        Assert.Equal(["file.open", "help.shortcuts"], commands.Where(c => c.IsEnabled).Select(c => c.Id).ToArray());

        await windows.OpenAsync("text-basic.pdf");
        var enabled = commands.Where(c => c.IsEnabled).Select(c => c.Id).ToList();
        Assert.Contains("zoom.in", enabled);
        Assert.Contains("find.search", enabled);
        Assert.Contains("file.close", enabled);
        Assert.DoesNotContain("tabs.next", enabled);
        Assert.DoesNotContain("file.print", enabled);

        await windows.OpenAsync("hello_world.pdf");
        Assert.Contains("tabs.next", commands.Where(c => c.IsEnabled).Select(c => c.Id));
    }

    [AvaloniaFact]
    public async Task ClosingThePaneWithTheFocusInsideMovesTheFocusToTheDocument()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("outline.pdf");
        var window = windows.Window;
        var view = window.ViewOf(tab)!;
        await ViewFixture.WaitAsync(() => view.FirstSharpPageShown);

        Press(window, Key.F6, PhysicalKey.F6);
        Assert.True(window.NavigationPane.IsPaneFocused);

        window.CloseLeftPane();
        ViewFixture.Pump();
        Assert.False(window.NavigationPane.IsOpen);
        Assert.Same(view, window.FocusManager?.GetFocusedElement());

        // Switching from one pane to the other keeps the focus out of the pane that left.
        window.ShowPagesPane();
        ViewFixture.Pump();
        Press(window, Key.F6, PhysicalKey.F6);
        Assert.True(window.NavigationPane.IsPaneFocused);
        window.ShowOutlinePane();
        ViewFixture.Pump();
        Assert.Same(view, window.FocusManager?.GetFocusedElement());
    }
}
