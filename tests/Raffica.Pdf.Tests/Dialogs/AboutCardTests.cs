using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Packaging;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Dialogs;

public sealed class AboutCardTests : IDisposable
{
    public void Dispose() => PdfStrings.Culture = CultureInfo.GetCultureInfo("en");

    private sealed class Shown(Window window, Dialog card) : IDisposable
    {
        public Dialog Card { get; } = card;

        public T Find<T>(string name) where T : Control => Card.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

        public bool Has(string name) => Card.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name);

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

    private static Shown Show(string culture, string version, string? note, string notices, Action done)
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo(culture);
        var card = AboutCard.Create(version, note, notices, done);
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 800, Height = 600 };
        window.Show();
        host.Show(card);
        Pump();
        return new Shown(window, card);
    }

    // The licenses.json of the repository lists what is shipped; the notices must name every one of those.
    private static List<string> ShippedComponents()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoPaths.Combine("third-party", "licenses.json")));
        return doc.RootElement.GetProperty("entries").EnumerateArray()
            .Where(e => !e.TryGetProperty("shipped", out var shipped) || shipped.ValueKind != JsonValueKind.False)
            .Select(e => e.GetProperty("id").GetString()!)
            .ToList();
    }

    [AvaloniaFact]
    public void ShowsVersionLicenseAndNotices()
    {
        var closed = 0;
        using var s = Show("en", "1.2.3", null, "NOTICES TEXT\nline two", () => closed++);

        Assert.Equal("Raffica PDF", s.Find<TextBlock>("PART_Name").Text);
        Assert.Equal("Version 1.2.3", s.Find<TextBlock>("PART_Version").Text);
        Assert.Contains("GPL-3.0-or-later", s.Find<TextBlock>("PART_License").Text);
        Assert.Contains("https://github.com/benjo-code/raffica", s.Find<TextBlock>("PART_Project").Text);
        Assert.Equal("NOTICES TEXT\nline two", s.Find<TextBox>("PART_Notices").Text);
        Assert.True(s.Find<TextBox>("PART_Notices").IsReadOnly);

        s.Find<Button>("PART_Ok").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, closed);
    }

    [AvaloniaFact]
    public void EscapeClosesTheCard()
    {
        var closed = 0;
        using var s = Show("de", "1.2.3", null, "x", () => closed++);
        Assert.Equal("Über Raffica PDF", s.Card.Title);
        Assert.Equal("Version 1.2.3", s.Find<TextBlock>("PART_Version").Text);
        Assert.StartsWith("Lizenz:", s.Find<TextBlock>("PART_License").Text);
        s.Escape();
        Assert.Equal(1, closed);
    }

    [AvaloniaFact]
    public void TestBuildNoteShownOnlyWhenPresent()
    {
        using (var release = Show("en", "1.2.3", null, "x", () => { }))
        {
            Assert.Equal("Version 1.2.3", release.Find<TextBlock>("PART_Version").Text);
        }

        using (var test = Show("en", "1.2.3", "(test build)", "x", () => { }))
        {
            Assert.Equal("Version 1.2.3 (test build)", test.Find<TextBlock>("PART_Version").Text);
        }
    }

    [AvaloniaFact]
    public void NoticesNameEveryShippedComponent()
    {
        var text = ThirdPartyNotices.Read();
        var components = ShippedComponents();
        Assert.NotEmpty(components);
        foreach (var component in components)
        {
            Assert.Contains(component, text, StringComparison.Ordinal);
        }

        // The ones the brief names, as licenses.cs lists them.
        foreach (var name in new[]
        {
            "bblanchon.PDFium.Win32", "Tesseract OCR", "Leptonica", "Avalonia", "SkiaSharp", "HarfBuzzSharp", "Geist",
            "Public Sans", "Barlow Semi Condensed", "Lucide", "Carlito", "tessdata_fast",
        })
        {
            Assert.Contains(name, text, StringComparison.Ordinal);
        }

        Assert.Contains("GPL-3.0-or-later", text, StringComparison.Ordinal);

        // The card shows that same text: the part before the first separator in its first box, each component in its own section once opened.
        using var s = Show("en", "1.2.3", null, text, () => { });
        var (intro, sections) = AboutCard.SplitNotices(text);
        Assert.Equal(intro, s.Find<TextBox>("PART_Notices").Text);
        Assert.True(sections.Count >= 20, sections.Count.ToString(CultureInfo.InvariantCulture));
        var rebuilt = new System.Text.StringBuilder(intro);
        for (var i = 0; i < sections.Count; i++)
        {
            s.Find<ToggleButton>("PART_NoticeToggle" + i).IsChecked = true;
            Pump();
            rebuilt.Append("====\n").Append(s.Find<TextBox>("PART_Notice" + i).Text);
        }

        rebuilt.Append("====\n");
        Assert.Equal(text, rebuilt.ToString());
    }

    // The reason for the sections: a box with all of the text freezes the card for a second. Nothing of a section is laid out before it is opened.
    [AvaloniaFact]
    public void OnlyAnOpenedSectionHoldsItsText()
    {
        using var s = Show("en", "1.2.3", null, ThirdPartyNotices.Read(), () => { });
        Assert.False(s.Has("PART_Notice0"));
        Assert.False(s.Find<ContentControl>("PART_NoticeBody0").IsVisible);
        Assert.True(s.Find<TextBox>("PART_Notices").Text!.Length < 1000);

        var toggle = s.Find<ToggleButton>("PART_NoticeToggle1");
        toggle.IsChecked = true;
        Pump();
        Assert.True(s.Find<ContentControl>("PART_NoticeBody1").IsVisible);
        Assert.False(string.IsNullOrWhiteSpace(s.Find<TextBox>("PART_Notice1").Text));
        Assert.False(s.Has("PART_Notice0"));
        Assert.False(s.Has("PART_Notice2"));

        // Closing hides it again; opening again does not make a second box.
        toggle.IsChecked = false;
        Pump();
        Assert.False(s.Find<ContentControl>("PART_NoticeBody1").IsVisible);
        toggle.IsChecked = true;
        Pump();
        Assert.Single(s.Card.GetVisualDescendants().OfType<TextBox>(), b => b.Name == "PART_Notice1");
    }

    [AvaloniaFact]
    public void SplittingKeepsTheTextAndIgnoresLongerRulesInsideALicence()
    {
        var (intro, sections) = AboutCard.SplitNotices("head\n====\nA\nMIT\n\n=============================\nstill A\n====\nB\nISC\n====\n");
        Assert.Equal("head\n", intro);
        Assert.Equal(["A\nMIT\n\n=============================\nstill A\n", "B\nISC\n"], sections);

        // Plain text is all intro.
        var plain = AboutCard.SplitNotices("NOTICES TEXT\nline two");
        Assert.Equal("NOTICES TEXT\nline two", plain.Intro);
        Assert.Empty(plain.Sections);
    }

    [AvaloniaFact]
    public void TheCardIsMadeOfShellTokensOnly()
    {
        var source = File.ReadAllText(RepoPaths.Combine("src", "Raffica.Pdf", "Dialogs", "AboutCard.cs"));
        Assert.DoesNotContain("#", source.Replace("#region", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
