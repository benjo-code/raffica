using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class TitleBarInsetTests
{
    [AvaloniaFact]
    public void FullScreenOnMacRemovesTheInset() =>
        Assert.Equal(0, TitleBar.EffectiveInset(80, WindowState.FullScreen, true));

    [AvaloniaFact]
    public void NormalOnMacKeepsIt()
    {
        Assert.Equal(80, TitleBar.EffectiveInset(80, WindowState.Normal, true));
        Assert.Equal(80, TitleBar.EffectiveInset(80, WindowState.Maximized, true));
    }

    [AvaloniaFact]
    public void FullScreenElsewhereChangesNothing()
    {
        Assert.Equal(0, TitleBar.EffectiveInset(0, WindowState.FullScreen, false));
        Assert.Equal(80, TitleBar.EffectiveInset(80, WindowState.FullScreen, false));
    }
}
