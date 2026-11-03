using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Raffica.Shell.Tests;

public sealed class HeadlessSmokeTests
{
    [AvaloniaFact]
    public void AWindowWithTheShellStylesOpensInTheHeadlessPlatform()
    {
        var button = new Button { Content = "OK" };
        var window = new Window { Width = 300, Height = 200, Content = button };

        window.Show();

        Assert.True(window.IsVisible);
        Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
    }
}
