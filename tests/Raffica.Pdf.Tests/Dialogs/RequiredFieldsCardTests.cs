using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Pdf.Dialogs;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Dialogs;

public sealed class RequiredFieldsCardTests
{
    private sealed class Shown(Window window, Dialog card) : IDisposable
    {
        public Dialog Card { get; } = card;

        public IReadOnlyList<string> ButtonNames =>
            Card.GetVisualDescendants().OfType<Button>().Select(b => b.Name!).Where(n => n.StartsWith("PART_", StringComparison.Ordinal) && n != "PART_Close").ToList();

        public string AllText => string.Join("\n", Card.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text));

        public Button Button(string name) => Card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

        public void Click(string name) => Button(name).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

        public void Escape()
        {
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump();
        }

        public void Dispose() => window.Close();
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Shown Show(string culture, IReadOnlyList<string> names, Action<RequiredChoice> done)
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo(culture);
        var card = RequiredFieldsCard.Create(names, done);
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 700, Height = 400 };
        window.Show();
        host.Show(card);
        Pump();
        return new Shown(window, card);
    }

    [AvaloniaFact]
    public void RequiredCardOffersGoToFirstAsDefaultAndSaveAnywayNeverBlocks()
    {
        var results = new List<RequiredChoice>();
        using (var s = Show("en", ["Pflicht", "Zustimmung"], results.Add))
        {
            Assert.Equal(["PART_GoToFirst", "PART_SaveAnyway"], s.ButtonNames);
            Assert.Equal(PdfStrings.RequiredGoToFirst, s.Button("PART_GoToFirst").Content);
            Assert.Contains("primary", s.Button("PART_GoToFirst").Classes);
            Assert.DoesNotContain("primary", s.Button("PART_SaveAnyway").Classes);

            // The card never blocks: Save anyway works at once (no arming delay, no manual clock).
            Assert.True(s.Button("PART_SaveAnyway").IsEnabled);
            s.Click("PART_SaveAnyway");
        }

        Assert.Equal([RequiredChoice.SaveAnyway], results);
    }

    [AvaloniaFact]
    public void EscMeansGoToFirstAndGoToFirstButtonToo()
    {
        var results = new List<RequiredChoice>();
        using (var s = Show("en", ["A"], results.Add))
        {
            s.Escape();
        }

        using (var s = Show("en", ["A"], results.Add))
        {
            s.Click("PART_GoToFirst");
            s.Click("PART_SaveAnyway");
        }

        // Only the first answer counts per card.
        Assert.Equal([RequiredChoice.GoToFirst, RequiredChoice.GoToFirst], results);
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public void ListsAtMostFiveNamesAndTheCountAndNeverSaysInvalid(string culture)
    {
        var names = Enumerable.Range(1, 8).Select(i => "Feld" + i).ToList();
        using var s = Show(culture, names, _ => { });
        var text = s.AllText;
        for (var i = 1; i <= 5; i++)
        {
            Assert.Contains("Feld" + i, text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Feld6", text, StringComparison.Ordinal);
        Assert.Contains("8", text, StringComparison.Ordinal);
        Assert.Contains("3", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\b(invalid|ungültig|ungueltig|corrupt|defekt|beschädigt)\b", RegexOptions.IgnoreCase), text);
    }

    [AvaloniaFact]
    public void OneNameReadsAsOneField()
    {
        using var s = Show("en", ["Pflicht"], _ => { });
        Assert.Contains("Pflicht", s.AllText, StringComparison.Ordinal);
        Assert.Contains("One required field", s.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public void NoNamesIsRefused()
    {
        Assert.Throws<ArgumentException>(() => RequiredFieldsCard.Create([], _ => { }));
    }
}
