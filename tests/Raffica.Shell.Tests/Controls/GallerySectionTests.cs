using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Gallery;
using Raffica.Gallery.Sections;
using Raffica.Shell.Localization;
using Xunit;

namespace Raffica.Shell.Tests.Controls;

public sealed class GallerySectionTests
{
    [AvaloniaTheory]
    [InlineData("en", "Buttons and choices", "Boxed button")]
    [InlineData("de", "Schaltflächen und Auswahl", "Umrandete Schaltfläche")]
    public void EverySectionBuildsInTheChosenLanguage(string lang, string title, string boxedRow)
    {
        var previous = GalleryStrings.Culture;
        try
        {
            GalleryStrings.Culture = Languages.Resolve(lang, CultureInfo.InvariantCulture);
            ShellTheme.Apply(Avalonia.Application.Current!, ThemeMode.Light, AppInk.Write, forceHighContrast: false);

            foreach (var name in GallerySections.Names)
            {
                var window = new GalleryWindow(GalleryOptions.Parse(["--lang", lang, "--section", name]));
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Contains(title, texts);
                Assert.Contains(boxedRow, texts);
                window.Close();
            }
        }
        finally
        {
            GalleryStrings.Culture = previous;
        }
    }
}
