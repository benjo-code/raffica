using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell;
using Raffica.Shell.Theming;
using Xunit;

namespace Raffica.Pdf.Tests.Panes;

/// <summary>07-UI-08: the selected item of a navigation list (organize pages, outline) is marked by a 2 px edge bar in text.primary, not by the tint alone.</summary>
public sealed class NavListSelectionTests
{
    [AvaloniaTheory]
    [InlineData(ThemeMode.Light, false)]
    [InlineData(ThemeMode.Dark, false)]
    [InlineData(ThemeMode.Dark, true)]
    public void TheSelectedItemHasATwoPixelEdgeInTextPrimary(ThemeMode mode, bool highContrast)
    {
        try
        {
            ShellTheme.Apply(Application.Current!, mode, AppInk.Pdf, highContrast);
            var list = new ListBox { Width = 200, Height = 200, ItemsSource = new[] { "One", "Two", "Three" }, SelectedIndex = 1 };
            list.Classes.Add("nav-list");
            var window = new Window { Content = list };
            window.Show();
            ViewFixture.Pump();

            var presenters = list.GetVisualDescendants().OfType<ContentPresenter>().Where(p => p.Name == "PART_ContentPresenter" && p.Parent is ListBoxItem).ToList();
            Assert.Equal(3, presenters.Count);
            Assert.True(Application.Current!.TryGetResource("Raffica.Brush.Text.Primary", Application.Current.ActualThemeVariant, out var resource));
            var ink = ((ISolidColorBrush)resource!).Color;
            for (var i = 0; i < presenters.Count; i++)
            {
                Assert.Equal(2, presenters[i].BorderThickness.Left);
                var edge = ((ISolidColorBrush)presenters[i].BorderBrush!).Color;
                if (i == 1)
                {
                    Assert.Equal(ink, edge);
                }
                else
                {
                    Assert.Equal(Colors.Transparent, edge);
                }
            }

            window.Close();
        }
        finally
        {
            ShellTheme.Apply(Application.Current!, ThemeMode.Light, AppInk.Pdf, forceHighContrast: false);
        }
    }
}
