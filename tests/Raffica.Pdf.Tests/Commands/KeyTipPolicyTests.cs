using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Commands;

public sealed class KeyTipPolicyTests
{
    [Fact]
    public void KeyTipsAreOfferedExceptOnMacOs()
    {
        Assert.True(KeyTips.Offered(false));
        Assert.False(KeyTips.Offered(true));
    }

    [Fact]
    public void NoKeyTipLineInTheHelpOnMac()
    {
        Assert.DoesNotContain(NavigationHelp.Entries(true), e => e.Name == PdfStrings.HelpNavFieldCodes);
        Assert.Contains(NavigationHelp.Entries(false), e => e.Name == PdfStrings.HelpNavFieldCodes);
    }

    [AvaloniaFact]
    public async Task AWindowWithoutKeyTipsDoesNotEnterTheFieldCodeMode()
    {
        using var windows = WindowFixture.Create(keyTips: false);
        await windows.OpenAsync("text-rich.pdf");
        var window = windows.Window;
        bool? handled = null;
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => handled = e.Handled, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

        window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        ViewFixture.Pump();

        Assert.False(window.HasKeyTips);
        Assert.False(window.Ribbon.ShowFieldCodes);
        Assert.False(handled);
    }

    [AvaloniaFact]
    public async Task AWindowWithKeyTipsStillEntersIt()
    {
        using var windows = WindowFixture.Create(keyTips: true);
        await windows.OpenAsync("text-rich.pdf");
        var window = windows.Window;

        window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        ViewFixture.Pump();

        Assert.True(window.HasKeyTips);
        Assert.True(window.Ribbon.ShowFieldCodes);
    }
}
