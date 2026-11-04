using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.Commands;

public sealed class FieldCodeIntegrationTests
{
    private static void Alt(PdfWindow window)
    {
        window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        ViewFixture.Pump();
    }

    private static void Code(PdfWindow window, string code)
    {
        foreach (var c in code)
        {
            var (key, physical) = c switch
            {
                >= 'A' and <= 'Z' => ((Key)((int)Key.A + (c - 'A')), (PhysicalKey)((int)PhysicalKey.A + (c - 'A'))),
                _ => ((Key)((int)Key.D0 + (c - '0')), (PhysicalKey)((int)PhysicalKey.Digit0 + (c - '0'))),
            };
            var text = c.ToString().ToLowerInvariant();
            window.KeyPress(key, RawInputModifiers.None, physical, text);
            window.KeyTextInput(text);
            window.KeyRelease(key, RawInputModifiers.None, physical, text);
            ViewFixture.Pump();
        }
    }

    [AvaloniaFact]
    public async Task AltHVRunsZoomInAndOpensTheViewTab()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("text-rich.pdf");
        var window = windows.Window;
        var view = window.ViewOf(tab)!;
        view.SetZoom(1.0);
        Assert.Equal(PdfRibbon.HomeLetter, window.Ribbon.ActiveTab!.Letter);

        Alt(window);
        Code(window, "V");
        Assert.Equal(PdfRibbon.ViewLetter, window.Ribbon.ActiveTab!.Letter);
        Code(window, "11");

        Assert.Equal(1.25, view.Zoom);
    }

    [AvaloniaFact]
    public async Task AltHRunsFindAndAnEnabledCodeOfEveryGroupWorks()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("pages-marked.pdf");
        var window = windows.Window;
        var view = window.ViewOf(tab)!;

        Alt(window);
        Code(window, "H21");
        Assert.NotNull(window.ShownSearchBar);

        Alt(window);
        Code(window, "H44");
        Assert.Equal(view.PageCount - 1, view.CurrentPage);
        Alt(window);
        Code(window, "H43");
        Assert.Equal(0, view.CurrentPage);

        Alt(window);
        Code(window, "V42");
        Assert.Equal(Raffica.Pdf.Panes.LeftPaneKind.Outline, window.LeftPane);
    }

    [AvaloniaFact]
    public async Task ADisabledCodeDoesNothing()
    {
        using var windows = WindowFixture.Create();
        await windows.OpenAsync("text-basic.pdf");
        var window = windows.Window;

        // Next tab (H51) is disabled with one tab, so its code does nothing and does not throw.
        Alt(window);
        Code(window, "H51");

        Assert.False(window.Host.IsOpen);
        Assert.Single(window.Workspace.Tabs);
    }

    [AvaloniaFact]
    public void FieldCodesInTheStartViewDoNothingAndDoNotThrow()
    {
        using var windows = WindowFixture.Create();
        var window = windows.Window;
        Assert.Same(window.Start, window.Frame.Document);

        Alt(window);
        Code(window, "H");
        Code(window, "11");
        Code(window, "6");
        Alt(window);
        Code(window, "V11");
        window.KeyPress(Key.F10, RawInputModifiers.None, PhysicalKey.F10, null);
        window.KeyRelease(Key.F10, RawInputModifiers.None, PhysicalKey.F10, null);
        Code(window, "V");
        Code(window, "42");

        Assert.False(window.Host.IsOpen);
        Assert.Empty(window.Workspace.Tabs);
        Assert.Same(window.Start, window.Frame.Document);
    }
}
