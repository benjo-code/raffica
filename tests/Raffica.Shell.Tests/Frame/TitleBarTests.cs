using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Raffica.Gallery;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class TitleBarTests
{
    private static (Window Window, TitleBar Bar) Show(Action<TitleBar>? configure = null, ThemeMode mode = ThemeMode.Light)
    {
        var bar = new TitleBar { VerticalAlignment = VerticalAlignment.Top };
        configure?.Invoke(bar);
        var window = ControlHarness.Show(bar, mode);
        window.Width = 1200;
        ControlHarness.Settle(window);
        return (window, bar);
    }

    private static Button Caption(TitleBar bar, string name) => ControlHarness.Part<Button>(bar, "PART_Caption" + name);

    [AvaloniaFact]
    public void TitleBarIs36High()
    {
        var (_, bar) = Show();

        Assert.Equal(36, bar.Bounds.Height, 0.01);
    }

    [AvaloniaFact]
    public void CaptionButtonsAre46By36()
    {
        // macOS has the system's window buttons instead (ShowCaptionButtons is false there by default).
        var (window, bar) = Show(b => b.ShowCaptionButtons = true);

        foreach (var name in new[] { "Minimize", "Maximize", "Close" })
        {
            var button = Caption(bar, name);
            Assert.Equal(46, button.Bounds.Width, 0.01);
            Assert.Equal(36, button.Bounds.Height, 0.01);
            Assert.Equal(new Avalonia.CornerRadius(0), button.CornerRadius);
        }

        Assert.Equal(ShellStrings.Minimize, Avalonia.Automation.AutomationProperties.GetName(Caption(bar, "Minimize")));
        Assert.Equal(ShellStrings.Maximize, Avalonia.Automation.AutomationProperties.GetName(Caption(bar, "Maximize")));

        // The window decorations are the borderless-with-resize mode and the buttons carry their roles.
        // (macOS keeps the system's decorations: PrepareWindow leaves them alone there.)
        Assert.Equal(OperatingSystem.IsMacOS() ? WindowDecorations.Full : WindowDecorations.BorderOnly, window.WindowDecorations);
        Assert.Equal(WindowDecorationsElementRole.CloseButton, WindowDecorationProperties.GetElementRole(Caption(bar, "Close")));
        Assert.Equal(WindowDecorationsElementRole.MinimizeButton, WindowDecorationProperties.GetElementRole(Caption(bar, "Minimize")));
        Assert.Equal(WindowDecorationsElementRole.MaximizeButton, WindowDecorationProperties.GetElementRole(Caption(bar, "Maximize")));
        Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(ControlHarness.Part<Border>(bar, "PART_Host")));
        Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(bar.Search));
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void CloseButtonHoverIsNotRed(ThemeMode mode)
    {
        var (window, bar) = Show(b => b.ShowCaptionButtons = true, mode);
        var close = Caption(bar, "Close");
        var normal = ControlHarness.Presenter(close);
        Assert.Equal(Avalonia.Media.Colors.Transparent, ControlHarness.ColorOf(normal.Background));

        StateMatrix.Force(close, ":pointerover");
        ControlHarness.Settle(window);

        var hover = ControlHarness.Strict(ControlHarness.Presenter(close).Background);
        Assert.Equal(ControlHarness.Token("Raffica.Brush.State.Hover"), hover);
        Assert.NotEqual(ControlHarness.Token("Raffica.Brush.Danger"), hover);
        Assert.True(hover.R <= hover.B + 8, "a neutral hover is never red");
    }

    [AvaloniaFact]
    public void ButtonsAreHiddenWhenShowCaptionButtonsIsFalse()
    {
        var (_, bar) = Show(b => b.ShowCaptionButtons = false);

        foreach (var name in new[] { "Minimize", "Maximize", "Close" })
        {
            Assert.False(Caption(bar, name).IsEffectivelyVisible);
            Assert.Equal(0, Caption(bar, name).Bounds.Width);
        }

        // The search field then reaches the right edge region (nothing reserved for the buttons).
        var right = bar.Search.TranslatePoint(new Avalonia.Point(bar.Search.Bounds.Width, 0), bar)!.Value.X;
        Assert.True(right > 1200 - 46 * 3);
    }

    [AvaloniaFact]
    public void LeadingInsetMovesTheContentRightForTheSystemButtons()
    {
        var (_, plain) = Show(b =>
        {
            b.Mark = new TextBlock { Text = "R" };
            b.LeadingInset = 0;
        });
        var (_, inset) = Show(b =>
        {
            b.Mark = new TextBlock { Text = "R" };
            b.LeadingInset = 80;
        });

        double MarkX(TitleBar bar) => bar.Mark is Control mark ? mark.TranslatePoint(default, bar)!.Value.X : -1;
        Assert.Equal(80, MarkX(inset) - MarkX(plain), 0.01);
        // The default follows the platform: 80 px on macOS (system buttons), none elsewhere.
        Assert.Equal(OperatingSystem.IsMacOS() ? 80 : 0, new TitleBar().LeadingInset);
        Assert.Equal(!OperatingSystem.IsMacOS(), new TitleBar().ShowCaptionButtons);
    }
}
