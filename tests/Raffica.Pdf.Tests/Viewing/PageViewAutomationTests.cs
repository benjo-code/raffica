using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Pdf.Viewing;
using Raffica.Shell;
using Raffica.Shell.Theming;
using Xunit;

namespace Raffica.Pdf.Tests.Viewing;

public sealed class PageViewAutomationTests
{
    [AvaloniaFact]
    public async Task PageViewHasANameWithTheDocumentTitle()
    {
        using var english = await ViewFixture.OpenAsync("text-basic.pdf");
        Assert.Equal("text-basic.pdf, PDF document", AutomationProperties.GetName(english.View));

        // The name follows the tab: another tab gives another name, no tab gives none.
        english.View.Tab = null;
        Assert.True(string.IsNullOrEmpty(AutomationProperties.GetName(english.View)));
        english.View.Tab = english.Tab;
        Assert.Equal("text-basic.pdf, PDF document", AutomationProperties.GetName(english.View));
    }

    [AvaloniaFact]
    public async Task PageViewNameIsGermanInGerman()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("de");
        using var german = await ViewFixture.OpenAsync("text-basic.pdf");
        Assert.Equal("text-basic.pdf, PDF-Dokument", AutomationProperties.GetName(german.View));
    }

    // The window gives the focus to the view when it opens (without a ring); the ring is about focus that arrives later, so the focus
    // is moved to another control first.
    private static Button FocusAway(ViewFixture fixture)
    {
        ShellTheme.Apply(Application.Current!, ThemeMode.Light, AppInk.Pdf, forceHighContrast: false);
        var other = new Button { Content = "Other" };
        fixture.Window.Content = null;
        fixture.Window.Content = new DockPanel { Children = { other, fixture.View } };
        DockPanel.SetDock(other, Dock.Top);
        ViewFixture.Pump();
        other.Focus();
        ViewFixture.Pump();
        Assert.False(fixture.View.IsFocused);
        return other;
    }

    // The shell focus ring: 2 px, in the ink of the app (set on the application by ShellTheme.Apply).
    private static bool HasFocusRing(PageView view)
    {
        var app = Application.Current!;
        Assert.True(app.TryGetResource("Raffica.Brush.Ink", app.ActualThemeVariant, out var ink));
        var layer = AdornerLayer.GetAdornerLayer(view);
        return layer is not null && layer.Children.OfType<Border>().Any(b =>
            b.BorderThickness == new Thickness(2) && b.BorderBrush is ISolidColorBrush ring && ring.Color == ((ISolidColorBrush)ink!).Color);
    }

    [AvaloniaFact]
    public async Task FocusRingAppearsOnKeyboardFocus()
    {
        using var fixture = await ViewFixture.OpenAsync("text-basic.pdf");

        FocusAway(fixture);
        fixture.View.Focus(NavigationMethod.Tab);
        ViewFixture.Pump();
        fixture.Window.UpdateLayout();

        Assert.True(fixture.View.IsFocused);
        Assert.True(HasFocusRing(fixture.View));
    }

    [AvaloniaFact]
    public async Task FocusRingDoesNotAppearOnPointerFocus()
    {
        using var fixture = await ViewFixture.OpenAsync("text-basic.pdf");

        FocusAway(fixture);
        fixture.View.Focus(NavigationMethod.Pointer);
        ViewFixture.Pump();
        fixture.Window.UpdateLayout();

        Assert.True(fixture.View.IsFocused);
        Assert.False(HasFocusRing(fixture.View));
    }
}
