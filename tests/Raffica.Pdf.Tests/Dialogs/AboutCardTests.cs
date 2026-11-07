using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
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

        // The card puts that same text into its box.
        using var s = Show("en", "1.2.3", null, text, () => { });
        Assert.Equal(text, s.Find<TextBox>("PART_Notices").Text);
    }

    [AvaloniaFact]
    public void TheCardIsMadeOfShellTokensOnly()
    {
        var source = File.ReadAllText(RepoPaths.Combine("src", "Raffica.Pdf", "Dialogs", "AboutCard.cs"));
        Assert.DoesNotContain("#", source.Replace("#region", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
