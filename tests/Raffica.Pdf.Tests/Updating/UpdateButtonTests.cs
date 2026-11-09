using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Raffica.Core.Updates;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Xunit;

namespace Raffica.Pdf.Tests.Updating;

public sealed class UpdateButtonTests : IDisposable
{
    public void Dispose()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        ShellStrings.Culture = CultureInfo.GetCultureInfo("en");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
    }

    private static Button Find(WindowFixture f, string name) =>
        f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    // Checks now (the command) and closes whatever card it shows, so the state of the flow is what the title bar button follows.
    private static async Task CheckAndCloseAsync(WindowFixture f, string closeWith)
    {
        var check = f.Window.CheckForUpdatesAsync();
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen);
        Click(Find(f, closeWith));
        await check;
        await ViewFixture.WaitAsync(() => !f.Window.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task HiddenWhenNothingIsNewer()
    {
        using var kit = new UpdateKit();
        kit.Release(UpdateKit.Running);
        using var f = WindowFixture.Create();
        f.Window.UpdateFlows = _ => kit.Flow();

        // Nothing exists before the first check.
        Assert.Null(f.Window.UpdateButton);

        // Up to date: the card says so, and the title bar shows no button.
        await CheckAndCloseAsync(f, "PART_Ok");
        Assert.Equal(UpdateStage.None, f.Window.UpdateState.Stage);
        Assert.False(f.Window.UpdateButton?.IsVisible ?? false);

        // A newer release shows it.
        kit.Release("0.2.0");
        await CheckAndCloseAsync(f, "PART_Later");
        Assert.True(f.Window.UpdateButton!.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData("en", "Update available: Raffica PDF 0.2.0")]
    [InlineData("de", "Update verfügbar: Raffica PDF 0.2.0")]
    public async Task ShowsTheVersionInItsAutomationName(string language, string expectedName)
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
        using var kit = new UpdateKit();
        kit.Release("0.2.0");
        using var f = WindowFixture.Create();
        f.Window.UpdateFlows = _ => kit.Flow();

        await CheckAndCloseAsync(f, "PART_Later");

        var button = f.Window.UpdateButton!;
        Assert.True(button.IsVisible);
        Assert.Equal(expectedName, AutomationProperties.GetName(button));
        Assert.Equal(expectedName, ToolTip.GetTip(button));
        Assert.Equal(PdfStrings.UpdateButton, button.Content);

        // It sits in the trailing slot of the title bar and takes clicks (a user element of the window decorations, not part of the drag area).
        var bar = f.Window.TitleSearch.GetVisualAncestors().OfType<TitleBar>().First();
        Assert.Same(button, bar.Trailing);
        Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(button));
    }

    [AvaloniaTheory]
    [InlineData("en", false)]
    [InlineData("de", false)]
    [InlineData("de", true)]
    public async Task TheButtonFitsTheTitleBarAt1024InBothLanguages(string language, bool dark)
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
        ShellStrings.Culture = PdfStrings.Culture;
        using var kit = new UpdateKit();
        kit.Release("10.20.30");
        using var f = WindowFixture.Create(width: 1024);
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        f.Window.UpdateFlows = _ => kit.Flow();
        await CheckAndCloseAsync(f, "PART_Later");
        ViewFixture.Pump();

        var bar = f.Window.TitleSearch.GetVisualAncestors().OfType<TitleBar>().First();
        var button = f.Window.UpdateButton!;
        var text = button.GetVisualDescendants().OfType<TextBlock>().Single();
        var left = button.TranslatePoint(default, bar)!.Value.X;
        var searchRight = f.Window.TitleSearch.TranslatePoint(default, bar)!.Value.X + f.Window.TitleSearch.Bounds.Width;
        // macOS draws no caption buttons (the system's buttons sit on the left), so the bar's right edge is the limit.
        var minimizeLeft = bar.ShowCaptionButtons
            ? bar.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_CaptionMinimize").TranslatePoint(default, bar)!.Value.X
            : bar.Bounds.Width;
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.True(left >= searchRight - 0.5, "the button sits right of the search field");
        Assert.True(left + button.Bounds.Width <= minimizeLeft + 0.5, "the button sits left of the caption buttons");
        Assert.True(button.Bounds.Height <= bar.Bounds.Height, "the button fits the bar");
        Assert.True(text.Bounds.Width >= text.DesiredSize.Width - 0.5, "the label is not clipped");
        Assert.True(bar.Search.Bounds.Width >= 200 - 0.5, "the search field keeps its minimum");
    }
}
