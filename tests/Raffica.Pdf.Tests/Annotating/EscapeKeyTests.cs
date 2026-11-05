using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Annotating;

public sealed class EscapeKeyTests
{
    private static void RunCommand(WindowFixture window, string id) =>
        window.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id).Run!.Invoke();

    private static void Escape(WindowFixture window)
    {
        window.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        ViewFixture.Pump();
    }

    [AvaloniaFact]
    public async Task EscEndsTheModeWhenFocusIsOnTheRibbon()
    {
        using var window = WindowFixture.Create();
        await window.OpenAsync("text-rich.pdf");
        RunCommand(window, "annotate.highlight");
        Assert.Equal(AnnotateTool.Highlight, window.Window.Tools.Tool);

        // The focus is on a control of the frame (the ribbon after a click on a tool), not on the page view.
        var button = window.Window.GetVisualDescendants().OfType<InputElement>().First(
            b => b.Focusable && b.IsEffectivelyVisible && b is not TextBox && b is not Raffica.Pdf.Viewing.PageView && !b.GetVisualAncestors().OfType<Raffica.Pdf.Viewing.PageView>().Any());
        button.Focus();
        ViewFixture.Pump();
        Assert.NotNull(window.Window.FocusManager!.GetFocusedElement());
        Assert.IsNotType<Raffica.Pdf.Viewing.PageView>(window.Window.FocusManager.GetFocusedElement());

        Escape(window);

        Assert.Equal(AnnotateTool.None, window.Window.Tools.Tool);
    }

    [AvaloniaFact]
    public async Task EscDoesNotEndTheModeWhileASearchFieldHasFocus()
    {
        using var window = WindowFixture.Create();
        await window.OpenAsync("text-rich.pdf");
        RunCommand(window, "annotate.highlight");
        window.Window.ShowSearchBar();
        ViewFixture.Pump();
        var bar = window.Window.ShownSearchBar!;
        var field = bar.GetVisualDescendants().OfType<TextBox>().First();
        field.Focus();
        ViewFixture.Pump();
        Assert.Same(field, window.Window.FocusManager!.GetFocusedElement());

        Escape(window);

        // The search bar took the key (it closes itself); the mode stays.
        Assert.Equal(AnnotateTool.Highlight, window.Window.Tools.Tool);
    }

    [AvaloniaFact]
    public async Task EscDoesNotEndTheModeWhileACardIsOpen()
    {
        using var window = WindowFixture.Create();
        await window.OpenAsync("text-rich.pdf");
        RunCommand(window, "annotate.highlight");
        _ = window.Window.ShowHelpAsync();
        ViewFixture.Pump();
        Assert.True(window.Window.Host.IsOpen);

        Escape(window);

        Assert.Equal(AnnotateTool.Highlight, window.Window.Tools.Tool);
    }
}
