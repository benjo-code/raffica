using System.Globalization;
using System.Text.Json;
using Avalonia;
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
        public Window Window => window;

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

    private static Shown Show(string culture, string version, string? note, string notices, Action done, Action? openProject = null)
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo(culture);
        var card = AboutCard.Create(version, note, notices, done, openProject);
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
        Assert.Equal("https://github.com/benjo-code/raffica", ((TextBlock)s.Find<Button>("PART_ProjectLink").Content!).Text);
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

    // 08-UI-09: the notices box was 560 wide in a 520 card, the intro did not wrap, the section toggles took the checked fill of a toggle.
    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public void TheNoticesFitTheCardWidthAndTheIntroWraps(string culture)
    {
        using var s = Show(culture, "1.2.3", null, ThirdPartyNotices.Read(), () => { });
        var scroll = s.Find<ScrollViewer>("PART_NoticesScroll");
        var list = (Control)scroll.Content!;
        Assert.True(list.Bounds.Width <= scroll.Viewport.Width + 0.5, $"list {list.Bounds.Width}, viewport {scroll.Viewport.Width}");

        var origin = ((Visual)scroll).TranslatePoint(default, s.Card)!.Value;
        var contentRight = s.Card.Bounds.Width - s.Card.Padding.Right - s.Card.BorderThickness.Right;
        Assert.True(origin.X + scroll.Bounds.Width <= contentRight + 0.5, $"scroll right {origin.X + scroll.Bounds.Width}, card content right {contentRight}");

        Assert.Equal(Avalonia.Media.TextWrapping.Wrap, s.Find<TextBox>("PART_Notices").TextWrapping);
        Assert.True(s.Find<TextBox>("PART_Notices").Bounds.Width <= scroll.Viewport.Width + 0.5);

        // Every section row fits the list and wraps its title instead of cutting it; an opened section's box fits too.
        s.Find<ToggleButton>("PART_NoticeToggle0").IsChecked = true;
        Pump();
        foreach (var toggle in s.Card.GetVisualDescendants().OfType<ToggleButton>())
        {
            Assert.True(toggle.Bounds.Width <= scroll.Viewport.Width + 0.5, toggle.Name);
            var title = toggle.GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.Equal(Avalonia.Media.TextWrapping.Wrap, title.TextWrapping);
            Assert.True(title.Bounds.Right <= toggle.Bounds.Width + 0.5, title.Text);
        }

        Assert.True(s.Find<TextBox>("PART_Notice0").Bounds.Width <= scroll.Viewport.Width + 0.5);

        // Everything outside the scrolling list: no text is clipped (the rows inside it are checked above).
        scroll.IsVisible = false;
        Pump();
        Assert.Empty(Raffica.Shell.Tests.Frame.LabelFit.FindClipped(s.Window));
    }

    [AvaloniaFact]
    public void TheSectionRowsAreDisclosureRows()
    {
        using var s = Show("en", "1.2.3", null, ThirdPartyNotices.Read(), () => { });
        var toggle = s.Find<ToggleButton>("PART_NoticeToggle1");
        toggle.IsChecked = true;
        Pump();
        Assert.Contains("disclosure", toggle.Classes);
    }

    [AvaloniaTheory]
    [InlineData("A 1.0\nMIT\n\ntext", "en", "A 1.0 - MIT")]
    [InlineData("A 1.0\nB 2.0\nC 3.0\nApache-2.0\n\n--- f ---", "en", "A 1.0 and 2 more - Apache-2.0")]
    [InlineData("A 1.0\nB 2.0\nApache-2.0\n\n--- f ---", "de", "A 1.0 und 1 weitere - Apache-2.0")]
    public void ASectionTitleNamesTheComponentAndItsLicense(string section, string culture, string expected)
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo(culture);
        Assert.Equal(expected, AboutCard.SectionTitle(section));
    }

    [AvaloniaFact]
    public void EveryShippedSectionTitleEndsWithItsLicense()
    {
        var (_, sections) = AboutCard.SplitNotices(ThirdPartyNotices.Read());
        Assert.NotEmpty(sections);
        foreach (var section in sections)
        {
            var lines = section.Split('\n');
            var license = lines.TakeWhile(l => l.Length > 0).Last();
            Assert.EndsWith(" - " + license, AboutCard.SectionTitle(section), StringComparison.Ordinal);
        }
    }

    [AvaloniaFact]
    public void TheNoticesSayEnthaeltWithTheUmlaut()
    {
        var text = ThirdPartyNotices.Read();
        Assert.Contains("Es enth\u00E4lt oder nutzt", text, StringComparison.Ordinal);
        Assert.DoesNotContain("enthaelt", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', text);

        // The file is UTF-8 without a BOM, so the umlaut is the two bytes C3 A4 on every platform.
        var bytes = File.ReadAllBytes(RepoPaths.Combine("src", "Raffica.Pdf", "Resources", "ThirdPartyNotices.txt"));
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.True(bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes("enth\u00E4lt")) >= 0);
    }

    [AvaloniaFact]
    public void TheProjectAddressIsALinkThatClosesTheCardAndAsksTheWindow()
    {
        var closed = 0;
        var opened = 0;
        using var s = Show("en", "1.2.3", null, "x", () => closed++, () => opened++);
        var link = s.Find<Button>("PART_ProjectLink");
        Assert.Equal(AboutCard.ProjectAddress, ((TextBlock)link.Content!).Text);
        Assert.Equal("Project: " + AboutCard.ProjectAddress, Avalonia.Automation.AutomationProperties.GetName(link));
        Assert.Contains("link", link.Classes);
        link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, closed);
        Assert.Equal(1, opened);
    }

    // The link goes through the card that shows the address in full (the one of a link in a document); only its Open button, after the delay, launches it.
    [AvaloniaFact]
    public async Task TheProjectLinkOpensOnlyAfterTheConfirmationCard()
    {
        var launched = new List<Uri>();
        var time = new Raffica.Pdf.Tests.Text.ManualTime();
        using var f = Raffica.Pdf.Tests.Viewing.WindowFixture.Create(launcher: launched.Add, time: time);
        f.Window.NoticesSource = () => "NOTICES";
        _ = f.Window.ShowAboutAsync();
        await Raffica.Pdf.Tests.Viewing.ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.AboutTitle);

        f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_ProjectLink")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Raffica.Pdf.Tests.Viewing.ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title != PdfStrings.AboutTitle);
        Assert.Empty(launched);
        var card = f.Window.Host.ActiveDialog!;
        Assert.Contains(AboutCard.ProjectAddress, card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

        time.Advance(LinkCard.ArmingDelayMilliseconds);
        card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Open").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Raffica.Pdf.Tests.Viewing.ViewFixture.WaitAsync(() => launched.Count > 0);
        Assert.False(f.Window.Host.IsOpen);
        Assert.Equal(AboutCard.ProjectAddress, Assert.Single(launched).OriginalString);
    }

    // Assistive technology activates the address through the Invoke pattern: the link is a button (its text is only the content), not a text.
    [AvaloniaFact]
    public void TheProjectLinkIsAButtonThatCanBeInvoked()
    {
        using var s = Show("de", "1.2.3", null, "NOTICES", () => { });
        var link = s.Find<Button>("PART_ProjectLink");

        var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(link);
        Assert.Equal(Avalonia.Automation.Peers.AutomationControlType.Button, peer.GetAutomationControlType());
        Assert.IsAssignableFrom<Avalonia.Automation.Provider.IInvokeProvider>(peer);
    }

    // Declining the question about the address leads back to the About card instead of to the document.
    [AvaloniaFact]
    public async Task DecliningTheLinkQuestionBringsTheAboutCardBack()
    {
        var launched = new List<Uri>();
        using var f = Raffica.Pdf.Tests.Viewing.WindowFixture.Create(launcher: launched.Add, time: new Raffica.Pdf.Tests.Text.ManualTime());
        f.Window.NoticesSource = () => "NOTICES";
        _ = f.Window.ShowAboutAsync();
        await Raffica.Pdf.Tests.Viewing.ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.AboutTitle);

        f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_ProjectLink")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Raffica.Pdf.Tests.Viewing.ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title != PdfStrings.AboutTitle);
        f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Cancel")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await Raffica.Pdf.Tests.Viewing.ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.AboutTitle);
        Assert.Empty(launched);
    }
}
