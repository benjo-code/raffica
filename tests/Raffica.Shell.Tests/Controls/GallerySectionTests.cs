using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Gallery;
using Raffica.Gallery.Sections;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;
using Xunit;

namespace Raffica.Shell.Tests.Controls;

public sealed class GallerySectionTests
{
    [AvaloniaTheory]
    [InlineData("en", "buttons", "Buttons and choices", "Boxed button")]
    [InlineData("de", "buttons", "Schaltflächen und Auswahl", "Umrandete Schaltfläche")]
    [InlineData("en", "inputs", "Input and select", "Text field with text")]
    [InlineData("de", "inputs", "Eingabe und Auswahl", "Textfeld mit Text")]
    [InlineData("en", "menus", "Menus and tooltips", "Button with menu")]
    [InlineData("de", "menus", "Menüs und Kurzinfos", "Schaltfläche mit Menü")]
    [InlineData("en", "dialogs", "Dialog and notification bars", "Open dialog")]
    [InlineData("de", "dialogs", "Dialog und Hinweisleisten", "Dialog öffnen")]
    public void EverySectionBuildsInTheChosenLanguage(string lang, string section, string title, string probe)
    {
        var previous = GalleryStrings.Culture;
        try
        {
            GalleryStrings.Culture = Languages.Resolve(lang, CultureInfo.InvariantCulture);
            ShellTheme.Apply(Avalonia.Application.Current!, ThemeMode.Light, AppInk.Write, forceHighContrast: false);

            var window = new GalleryWindow(GalleryOptions.Parse(["--lang", lang, "--section", section]));
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(title, texts);
            Assert.Contains(probe, texts);
            window.Close();
        }
        finally
        {
            GalleryStrings.Culture = previous;
        }
    }

    [AvaloniaFact]
    public void EveryInteractiveControlOfTheNewSectionsHasAnAutomationName()
    {
        ShellTheme.Apply(Avalonia.Application.Current!, ThemeMode.Light, AppInk.Write, forceHighContrast: false);
        foreach (var section in new[] { "buttons", "inputs", "menus", "dialogs" })
        {
            var window = new GalleryWindow(GalleryOptions.Parse(["--lang", "en", "--section", section]));
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var controls = window.GetVisualDescendants()
                .OfType<Control>()
                .Where(c => c is TextBox or ComboBox or Menu or SegmentedControl or NotificationBar);
            foreach (var control in controls)
            {
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)), $"{section}: {control.GetType().Name} has no automation name");
            }

            window.Close();
        }
    }

    [AvaloniaFact]
    public void OpenMenuOpensTheFlyoutWithItsSubmenu()
    {
        ShellTheme.Apply(Avalonia.Application.Current!, ThemeMode.Light, AppInk.Write, forceHighContrast: false);
        var window = new GalleryWindow(GalleryOptions.Parse(["--lang", "en", "--section", "menus", "--open-menu"]));
        Assert.False(window.IsMenuOpen);

        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Assert.True(window.IsMenuOpen);
        Assert.True(window.IsSubmenuOpen);

        var plain = new GalleryWindow(GalleryOptions.Parse(["--lang", "en", "--section", "menus"]));
        plain.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(plain.IsMenuOpen);
        plain.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void OpenDialogOpensTheSampleDialogOverTheScrim()
    {
        ShellTheme.Apply(Avalonia.Application.Current!, ThemeMode.Light, AppInk.Write, forceHighContrast: false);
        var window = new GalleryWindow(GalleryOptions.Parse(["--lang", "en", "--section", "dialogs", "--open-dialog"]));
        Assert.False(window.IsDialogOpen);

        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Assert.True(window.IsDialogOpen);
        Assert.Single(window.GetVisualDescendants().OfType<Dialog>());

        var plain = new GalleryWindow(GalleryOptions.Parse(["--lang", "en", "--section", "dialogs"]));
        plain.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(plain.IsDialogOpen);
        plain.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void TheInfoBarOfTheDialogsSectionHasTheCrashRecoveryActions()
    {
        var previous = (GalleryStrings.Culture, ShellStrings.Culture);
        try
        {
            GalleryStrings.Culture = ShellStrings.Culture = CultureInfo.GetCultureInfo("en");
            ShellTheme.Apply(Avalonia.Application.Current!, ThemeMode.Light, AppInk.Write, forceHighContrast: false);
            var window = new GalleryWindow(GalleryOptions.Parse(["--lang", "en", "--section", "dialogs"]));
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var bars = window.GetVisualDescendants().OfType<NotificationBar>().ToList();
            Assert.Equal(
                [NotificationSeverity.Info, NotificationSeverity.Warning, NotificationSeverity.Error],
                bars.Select(b => b.Severity));
            Assert.Equal(["Compare", "Close"], bars[0].Actions.Select(a => (string?)a.Content));
            Assert.All(bars, b => Assert.True(b.Actions.Count <= 2));
            window.Close();
        }
        finally
        {
            (GalleryStrings.Culture, ShellStrings.Culture) = previous;
        }
    }
}
