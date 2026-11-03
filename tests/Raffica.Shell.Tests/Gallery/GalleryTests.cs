using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Gallery;
using Raffica.Gallery.Sections;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;
using Raffica.Shell.Tests.Controls;
using Raffica.Shell.Tests.Tokens;
using Xunit;

namespace Raffica.Shell.Tests.Gallery;

public sealed class GalleryTests
{
    private static GalleryWindow Open(params string[] args)
    {
        var window = new GalleryWindow(GalleryOptions.Parse(args));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    private static UserControl SectionOf(GalleryWindow window) =>
        window.GetVisualDescendants().OfType<UserControl>().Single(u => u.GetType().Namespace == "Raffica.Gallery.Sections");

    [AvaloniaFact]
    public void EverySectionBuildsInEveryThemeAndInkAndLanguage()
    {
        var previous = (GalleryStrings.Culture, ShellStrings.Culture);
        try
        {
            foreach (var lang in new[] { "en", "de" })
            {
                GalleryStrings.Culture = ShellStrings.Culture = Languages.Resolve(lang, CultureInfo.InvariantCulture);
                foreach (var ink in Enum.GetValues<AppInk>())
                {
                    // The three theme modes, then Windows high contrast forced on.
                    foreach (var (mode, highContrast) in new[] { (ThemeMode.Light, false), (ThemeMode.Dark, false), (ThemeMode.System, false), (ThemeMode.Dark, true) })
                    {
                        ShellTheme.Apply(Avalonia.Application.Current!, mode, ink, highContrast);
                        foreach (var section in GallerySections.Names)
                        {
                            var where = $"{section}/{lang}/{ink}/{mode}/{highContrast}";
                            var window = Open("--lang", lang, "--section", section);
                            Assert.True(window.Bounds.Width > 0 && window.Bounds.Height > 0, $"{where}: empty window");
                            var content = SectionOf(window);
                            Assert.True(content.Bounds.Width > 0 && content.Bounds.Height > 0, $"{where}: empty section");
                            window.Close();
                        }
                    }
                }
            }
        }
        finally
        {
            (GalleryStrings.Culture, ShellStrings.Culture) = previous;
        }
    }

    [AvaloniaFact]
    public void TheGalleryShowsEveryControlTheMilestoneNames()
    {
        ShellTheme.Apply(Avalonia.Application.Current!, ThemeMode.Light, AppInk.Write, forceHighContrast: false);
        var seen = new HashSet<string>();
        foreach (var section in GallerySections.Names)
        {
            var window = Open("--lang", "en", "--section", section, "--open-dialog");
            foreach (var visual in ControlHarness.Descendants(window))
            {
                switch (visual)
                {
                    case CheckBox:
                        seen.Add("checkbox");
                        break;
                    case RadioButton:
                        seen.Add("radio");
                        break;
                    case ToggleButton:
                        seen.Add("toggle");
                        break;
                    case Button:
                        seen.Add("button");
                        break;
                    case TextBox:
                        seen.Add("input");
                        break;
                    case ComboBox:
                        seen.Add("select");
                        break;
                    case SegmentedControl:
                        seen.Add("segmented");
                        break;
                    case Menu:
                    case MenuItem:
                        seen.Add("menu");
                        break;
                    case NotificationBar:
                        seen.Add("notification");
                        break;
                    case Dialog:
                        seen.Add("dialog");
                        break;
                }

                if (visual is Control c)
                {
                    if (c.ContextMenu is not null || c.ContextFlyout is not null)
                    {
                        seen.Add("context menu");
                    }

                    if (ToolTip.GetTip(c) is not null)
                    {
                        seen.Add("tooltip");
                    }
                }
            }

            window.Close();
        }

        string[] expected = ["button", "toggle", "input", "select", "checkbox", "radio", "segmented", "menu", "context menu", "tooltip", "dialog", "notification"];
        Assert.Empty(expected.Except(seen));
    }

    [AvaloniaFact]
    public void TheToolbarSwitchesThemeInkAndHighContrastLive()
    {
        ShellTheme.Apply(Avalonia.Application.Current!, ThemeMode.Light, AppInk.Write, forceHighContrast: false);
        var window = Open("--lang", "en", "--theme", "light");
        var app = Avalonia.Application.Current!;

        window.SelectTheme(ThemeMode.Dark);
        Assert.Equal(ThemeVariant.Dark, app.ActualThemeVariant);

        window.SelectInk(AppInk.Pdf);
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Ink.Pdf"), ControlHarness.Token("Raffica.Brush.Focus.Ring"));

        window.SetHighContrast(true);
        Assert.Equal(ShellTheme.HighContrastVariant, app.ActualThemeVariant);
        window.SetHighContrast(false);
        Assert.Equal(ThemeVariant.Dark, app.ActualThemeVariant);
        window.Close();
    }

    [AvaloniaFact]
    public void TheNavigationListsEverySectionAndSwitchesThem()
    {
        ShellTheme.Apply(Avalonia.Application.Current!, ThemeMode.Light, AppInk.Write, forceHighContrast: false);
        var window = Open("--lang", "en");

        Assert.Equal(GallerySections.Names.Count, window.NavigationCount);
        Assert.Equal(GallerySections.Names[0], window.CurrentSection);
        foreach (var name in GallerySections.Names)
        {
            window.ShowSection(name);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(name, window.CurrentSection);
            Assert.NotNull(SectionOf(window));
        }

        window.Close();
    }

    [AvaloniaFact]
    public void TheFoundationsShowEveryColourTokenAndTheIconsAtBothSizes()
    {
        ShellTheme.Apply(Avalonia.Application.Current!, ThemeMode.Light, AppInk.Write, forceHighContrast: false);
        var window = Open("--lang", "en", "--section", "foundations");

        var swatches = window.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "Swatch").ToList();
        Assert.Equal(TokenFile.LoadBrushes("Light").Count, swatches.Count);
        Assert.Equal(TokenFile.LoadBrushes("Light").Keys.Order(), FoundationsSection.Tokens.Order());
        Assert.All(swatches, s => Assert.IsAssignableFrom<ISolidColorBrush>(s.Background));

        var icons = window.GetVisualDescendants().OfType<StrokeIcon>().ToList();
        Assert.Contains(icons, i => i.Size == 16);
        Assert.Contains(icons, i => i.Size == 20);
        Assert.All(icons, i => Assert.NotNull(i.Data));
        window.Close();
    }
}
