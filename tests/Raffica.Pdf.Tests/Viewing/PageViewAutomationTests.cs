using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
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
    private static AutomationPeer PeerOf(PageView view) => ControlAutomationPeer.CreatePeerForElement(view);

    [AvaloniaFact]
    public async Task PageViewIsADocumentWithTheFileAndThePage()
    {
        using var english = await ViewFixture.OpenAsync("pages-marked.pdf");
        var count = english.View.PageCount;
        Assert.True(count >= 4);

        var peer = PeerOf(english.View);
        Assert.Equal(AutomationControlType.Document, peer.GetAutomationControlType());
        Assert.True(peer.IsControlElement());
        Assert.True(peer.IsContentElement());
        Assert.True(peer.IsKeyboardFocusable());
        Assert.Equal($"pages-marked.pdf, page 1 of {count}", peer.GetName());
        Assert.Equal(peer.GetName(), AutomationProperties.GetName(english.View));

        // The name follows the tab: no tab, no name; the tab again, the name again.
        english.View.Tab = null;
        Assert.True(string.IsNullOrEmpty(AutomationProperties.GetName(english.View)));
        english.View.Tab = english.Tab;
        await ViewFixture.WaitAsync(() => AutomationProperties.GetName(english.View) == $"pages-marked.pdf, page 1 of {count}");
    }

    [AvaloniaFact]
    public async Task TheNameFollowsTheCurrentPageAndAnnouncesTheChange()
    {
        using var english = await ViewFixture.OpenAsync("pages-marked.pdf");
        var count = english.View.PageCount;
        var peer = PeerOf(english.View);
        var before = NameChangesOf(peer);

        english.View.GoToPage(2);
        ViewFixture.Pump();

        Assert.Equal($"pages-marked.pdf, page 3 of {count}", peer.GetName());
        Assert.True(NameChangesOf(peer) > before, "the change of the page is announced as a change of the name");

        // Scrolling inside the same page changes nothing and announces nothing.
        var steady = NameChangesOf(peer);
        english.View.Offset = new Vector(english.View.Offset.X, english.View.Offset.Y + 1);
        ViewFixture.Pump();
        Assert.Equal(steady, NameChangesOf(peer));
    }

    // The peer counts the announcements it was asked to raise (the platform bridge is not there in a headless run).
    private static int NameChangesOf(AutomationPeer peer) =>
        (int)peer.GetType().GetProperty("NameChanges", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(peer)!;

    [AvaloniaFact]
    public async Task APageFieldThatHasTheFormFocusNamesTheViewAndTheDocumentNameComesBack()
    {
        using var english = await ViewFixture.OpenAsync("pages-marked.pdf");
        var count = english.View.PageCount;

        english.View.FocusedFieldName = "Name";
        Assert.Equal("Name", PeerOf(english.View).GetName());
        english.View.FocusedFieldName = null;
        Assert.Equal($"pages-marked.pdf, page 1 of {count}", PeerOf(english.View).GetName());
    }

    [AvaloniaFact]
    public async Task PageViewNameIsGermanInGerman()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("de");
        using var german = await ViewFixture.OpenAsync("pages-marked.pdf");
        var count = german.View.PageCount;
        Assert.Equal($"pages-marked.pdf, Seite 1 von {count}", PeerOf(german.View).GetName());

        german.View.GoToPage(3);
        ViewFixture.Pump();
        Assert.Equal($"pages-marked.pdf, Seite 4 von {count}", PeerOf(german.View).GetName());
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
