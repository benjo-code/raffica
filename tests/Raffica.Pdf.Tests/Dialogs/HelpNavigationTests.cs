using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Dialogs;

/// <summary>The fixed "Navigation" section of the help card: the keys that belong to no command, written for the platform and the language.</summary>
public sealed class HelpNavigationTests : IDisposable
{
    public void Dispose() => PdfStrings.Culture = CultureInfo.GetCultureInfo("en");

    private static string Command => OperatingSystem.IsMacOS() ? "Meta" : "Ctrl";

    [Fact]
    public void TheSectionListsEveryKeyThatBelongsToNoCommand()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        var navigation = PdfCommandCatalog.HelpEntries().Where(e => e.Group == "Navigation").ToList();
        var keys = navigation.ToDictionary(e => e.Name, e => e.Shortcut);

        // The window-wide keys, the search field, the page organizer, the forms and the text fields.
        string Written(string shortcut) => Raffica.Shell.Localization.KeyNames.Format(shortcut, PdfStrings.Culture);
        Assert.Equal(Written(Command + "+K"), keys["Go to the search field"]);
        Assert.Equal(Written("F6") + " / " + Written("Shift+F6"), keys["Move between the panes"]);
        Assert.Equal("Alt", keys["Field codes: show them, then type one"]);
        Assert.Equal(Written(Command + "+Y"), keys["Redo"]);
        Assert.Equal(Written("Enter"), keys["Search field: next match"]);
        Assert.Equal(Written("Shift+Enter"), keys["Search field: previous match"]);
        Assert.Equal(Written("Alt+↑") + " / " + Written("Alt+↓"), keys["Organize pages: move the selected pages by one"]);
        Assert.Equal(Written("Alt+Home") + " / " + Written("Alt+End"), keys["Organize pages: move to the start or the end"]);
        Assert.Equal("[ / ]", keys["Organize pages: turn left or right"]);
        Assert.Equal(Written("Ctrl+D"), keys["Organize pages: duplicate"]);
        Assert.Equal(Written("Ctrl+A"), keys["Organize pages: select all pages"]);
        Assert.Equal(Written("Delete"), keys["Organize pages: delete"]);
        Assert.Equal(Written("Tab") + " / " + Written("Shift+Tab"), keys["Forms: next or previous field"]);
        Assert.Equal(Written("Enter") + " / Space", keys["Forms: fill in the field with the focus"]);
        Assert.Equal(Written("Esc"), keys["Forms: leave the field"]);
        Assert.Equal(Written(Command + "+Enter"), keys["Finish a text box or a comment"]);
        Assert.Equal(Written(Command + "+Shift+G"), keys["Edit text: whole line in the replacement font"]);
        Assert.Equal(17, navigation.Count);
    }

    [Fact]
    public void TheKeysAreWrittenForTheLanguage()
    {
        var german = NavigationHelp.EntriesIn(CultureInfo.GetCultureInfo("de"));
        Assert.All(german, e => Assert.Equal("Navigation", e.Group));
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Contains(german, e => e.Name == "Zum Suchfeld springen" && e.Shortcut == "Strg+K");
            Assert.Contains(german, e => e.Name.StartsWith("Formulare: das Feld mit", StringComparison.Ordinal) && e.Shortcut == "Eingabe / Leertaste");
            Assert.Contains(german, e => e.Name.EndsWith("an den Anfang oder ans Ende verschieben", StringComparison.Ordinal) && e.Shortcut == "Alt+Pos1 / Alt+Ende");
            Assert.Contains(german, e => e.Name == "Wiederholen" && e.Shortcut == "Strg+Y");
        }

        Assert.All(german, e => Assert.False(string.IsNullOrWhiteSpace(e.Name) || string.IsNullOrWhiteSpace(e.Shortcut)));
    }

    [AvaloniaFact]
    public void TheHelpCardShowsTheSectionAfterTheCommands()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        var entries = PdfCommandCatalog.HelpEntries();
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Width = 800, Height = 700, Content = host };
        window.Show();
        host.Show(HelpCard.Create(entries, () => { }));
        ViewFixture.Pump();

        var texts = host.ActiveDialog!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Navigation", texts);
        Assert.True(entries.Select(e => e.Group).Distinct().Last() == "Navigation");
        Assert.Contains("Go to the search field", texts);
        window.Close();
    }
}
