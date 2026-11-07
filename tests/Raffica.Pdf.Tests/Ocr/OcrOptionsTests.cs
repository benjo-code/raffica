using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Ocr;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Ocr;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.Ocr;

public sealed class OcrOptionsTests
{
    private static CommandDefinition Command(PdfWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    private static void Put(string folder, string language)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, language + ".traineddata"), new byte[16]);
    }

    // A window whose own tessdata folder (next to the settings file) holds the given languages; nothing ships with the test program.
    private static async Task<(WindowFixture Fixture, PdfTab Tab)> OpenedAsync(params string[] languages)
    {
        var f = WindowFixture.Create(folder =>
        {
            foreach (var language in languages)
            {
                Put(Path.Combine(folder, "tessdata"), language);
            }
        });
        f.Window.ShippedTessdataDirectory = Path.Combine(f.Folder, "no-shipped");
        var tab = await f.OpenAsync("pages-marked.pdf");
        return (f, tab);
    }

    private static LanguageList ListOf(params string[] names) =>
        new([.. names.Select(n => new TessdataLanguage(n, TessdataOrigin.Shipped, 16, n + ".traineddata"))], "en", stored: null);

    private static Window Show(Dialog dialog)
    {
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 700, Height = 700 };
        window.Show();
        host.Show(dialog);
        PrintHost.Pump();
        return window;
    }

    private static T Find<T>(Dialog dialog, string name)
        where T : Control =>
        dialog.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static void Click(Dialog dialog, string name) => Find<Button>(dialog, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    // Every text a card shows: blocks, the content of boxes and buttons, the choices of a dropdown.
    private static IEnumerable<string> TextsOf(Dialog dialog)
    {
        yield return dialog.Title?.ToString() ?? string.Empty;
        foreach (var control in dialog.GetVisualDescendants())
        {
            switch (control)
            {
                case TextBlock block when !string.IsNullOrEmpty(block.Text):
                    yield return block.Text;
                    break;
                case ContentControl { Content: string text }:
                    yield return text;
                    break;
                case SelectBox select:
                    foreach (var item in select.ItemsSource?.Cast<object>() ?? [])
                    {
                        yield return item.ToString() ?? string.Empty;
                    }

                    break;
            }
        }
    }

    [AvaloniaFact]
    public async Task ChoiceIsStoredInSettings()
    {
        var (f, tab) = await OpenedAsync("deu", "eng", "fra");
        using var _ = f;
        var started = new List<(PdfTab Tab, OcrOptions Options)>();
        f.Window.OcrLibraryProbe = () => true;
        f.Window.OcrRunStarter = (t, o) => started.Add((t, o));

        var run = f.Window.RecognizeAsync();
        await PrintHost.UntilAsync(() => f.Window.Host.ActiveDialog is { } d && d.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "PART_Recognize"));
        var card = f.Window.Host.ActiveDialog!;

        // The folder that was read is the one next to the settings file: only the files made there are offered.
        Assert.Equal(["PART_Lang_deu", "PART_Lang_eng", "PART_Lang_fra"], card.GetVisualDescendants().OfType<CheckBox>().Select(c => c.Name!).ToArray());
        Assert.True(Find<CheckBox>(card, "PART_Lang_eng").IsChecked);
        Assert.False(Find<CheckBox>(card, "PART_Lang_fra").IsChecked);
        Find<CheckBox>(card, "PART_Lang_fra").IsChecked = true;
        Click(card, "PART_Recognize");
        await PrintHost.UntilAsync(() => run.IsCompleted);
        await f.Window.Settings.FlushAsync();

        var (startedTab, options) = Assert.Single(started);
        Assert.Same(tab, startedTab);
        Assert.Equal("eng+fra", options.Languages);
        var stored = new Raffica.Core.Settings.SettingsStore(f.AppSettingsPath).LoadReadOnly();
        Assert.Equal("eng+fra", stored.OcrLanguages);

        // The next card starts from the stored choice.
        run = f.Window.RecognizeAsync();
        await PrintHost.UntilAsync(() => f.Window.Host.ActiveDialog is { } d && d.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "PART_Cancel"));
        card = f.Window.Host.ActiveDialog!;
        Assert.True(Find<CheckBox>(card, "PART_Lang_eng").IsChecked);
        Assert.True(Find<CheckBox>(card, "PART_Lang_fra").IsChecked);
        Assert.False(Find<CheckBox>(card, "PART_Lang_deu").IsChecked);

        // Cancel starts nothing and stores nothing.
        Find<CheckBox>(card, "PART_Lang_deu").IsChecked = true;
        Click(card, "PART_Cancel");
        await PrintHost.UntilAsync(() => run.IsCompleted);
        await f.Window.Settings.FlushAsync();
        Assert.Single(started);
        Assert.Equal("eng+fra", new Raffica.Core.Settings.SettingsStore(f.AppSettingsPath).LoadReadOnly().OcrLanguages);
    }

    [AvaloniaFact]
    public void CardOffersAutomaticAnd150To400()
    {
        var results = new List<OcrOptions?>();
        var dialog = RecognizeCard.Create("scan.pdf", 6, ListOf("deu", "eng"), signedWithoutChangeRights: false, results.Add);
        var window = Show(dialog);
        var dpi = Find<SelectBox>(dialog, "PART_Dpi");
        Assert.Equal(["Automatic", "150 dpi", "200 dpi", "300 dpi", "400 dpi"], dpi.ItemsSource!.Cast<string>().ToArray());
        Assert.Equal(0, dpi.SelectedIndex);

        // The defaults: every page, skip pages with text, automatic resolution.
        Click(dialog, "PART_Recognize");
        var first = Assert.IsType<OcrOptions>(Assert.Single(results));
        Assert.Equal(new OcrOptions("eng", OcrMode.Skip, 0, null).Languages, first.Languages);
        Assert.Equal((OcrMode.Skip, 0, null), (first.Mode, first.Dpi, first.Pages));
        window.Close();

        // A choice of everything.
        results.Clear();
        dialog = RecognizeCard.Create("scan.pdf", 6, ListOf("deu", "eng"), signedWithoutChangeRights: false, results.Add);
        window = Show(dialog);
        Find<SelectBox>(dialog, "PART_Dpi").SelectedIndex = 3;
        Find<RadioButton>(dialog, "PART_ModeForce").IsChecked = true;
        Find<RadioButton>(dialog, "PART_PagesRange").IsChecked = true;
        Find<TextBox>(dialog, "PART_Range").Text = "2-3,6";
        Find<CheckBox>(dialog, "PART_Lang_deu").IsChecked = true;
        PrintHost.Pump();
        Click(dialog, "PART_Recognize");
        var second = Assert.IsType<OcrOptions>(Assert.Single(results));
        Assert.Equal("eng+deu", second.Languages);
        Assert.Equal(OcrMode.Force, second.Mode);
        Assert.Equal(300, second.Dpi);
        Assert.Equal(new HashSet<int> { 1, 2, 5 }, second.Pages);
        second.Validate();
        window.Close();

        // Redo, and the offered resolutions are the ones Core accepts.
        Assert.All(RecognizeCard.Resolutions, r => new OcrOptions("eng", Dpi: r).Validate());
        Assert.Equal(OcrResolution.MinDpi, RecognizeCard.Resolutions.Min());
        Assert.Equal(400, RecognizeCard.Resolutions.Max());
    }

    [AvaloniaFact]
    public void RangeAndLanguagesGateTheRecognizeButton()
    {
        var results = new List<OcrOptions?>();
        var dialog = RecognizeCard.Create("scan.pdf", 6, ListOf("deu", "eng", "fra", "spa", "ita", "por", "nld", "pol", "ces"), false, results.Add);
        var window = Show(dialog);
        var ok = Find<Button>(dialog, "PART_Recognize");
        Assert.True(ok.IsEnabled);

        // A bad range stops it, with the reason; Enter in the field does nothing then.
        Find<RadioButton>(dialog, "PART_PagesRange").IsChecked = true;
        var range = Find<TextBox>(dialog, "PART_Range");
        foreach (var bad in new[] { "", "0", "abc", "9" })
        {
            range.Text = bad;
            Assert.False(ok.IsEnabled, bad);
            Assert.True(Find<TextBlock>(dialog, "PART_RangeHint").IsVisible, bad);
            range.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Empty(results);
        }

        range.Text = "4";
        Assert.True(ok.IsEnabled);

        // No language chosen: nothing to start.
        Find<CheckBox>(dialog, "PART_Lang_eng").IsChecked = false;
        Assert.False(ok.IsEnabled);
        Find<CheckBox>(dialog, "PART_Lang_eng").IsChecked = true;

        // More than three: the warning, not a refusal. More than eight: refused with the reason, the box stays clear.
        var warning = Find<TextBlock>(dialog, "PART_SlowWarning");
        Assert.False(warning.IsVisible);
        foreach (var name in new[] { "deu", "fra", "spa" })
        {
            Find<CheckBox>(dialog, "PART_Lang_" + name).IsChecked = true;
        }

        Assert.True(warning.IsVisible);
        foreach (var name in new[] { "ita", "por", "nld", "pol" })
        {
            Find<CheckBox>(dialog, "PART_Lang_" + name).IsChecked = true;
        }

        Assert.Equal(8, dialog.GetVisualDescendants().OfType<CheckBox>().Count(c => c.IsChecked == true));
        Find<CheckBox>(dialog, "PART_Lang_ces").IsChecked = true;
        Assert.False(Find<CheckBox>(dialog, "PART_Lang_ces").IsChecked);
        Assert.Equal(PdfStrings.OcrLanguageRefusal(LanguageChoice.TooMany), Find<TextBlock>(dialog, "PART_LanguageHint").Text);
        Assert.True(Find<TextBlock>(dialog, "PART_LanguageHint").IsVisible);

        // Enter in the field confirms a valid choice; Esc gives nothing.
        range.Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        var chosen = Assert.IsType<OcrOptions>(Assert.Single(results));
        Assert.Equal(8, OcrLanguages.Split(chosen.Languages).Count);
        window.Close();

        results.Clear();
        dialog = RecognizeCard.Create("scan.pdf", 6, ListOf("eng"), false, results.Add);
        window = Show(dialog);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Equal<OcrOptions?>([null], results);
        window.Close();
    }

    [AvaloniaFact]
    public void CurrentPageIsTheOneOfOpeningTimeAndSkipMeansOnlyPagesWithoutText()
    {
        var results = new List<OcrOptions?>();
        var dialog = RecognizeCard.Create("scan.pdf", 6, ListOf("eng"), false, results.Add, currentPage: 3);
        var window = Show(dialog);
        var current = Find<RadioButton>(dialog, "PART_PagesCurrent");
        Assert.True(current.IsEnabled);
        Assert.Contains("4", (string)current.Content!, StringComparison.Ordinal);
        Assert.Contains("without text", (string)Find<RadioButton>(dialog, "PART_ModeSkip").Content!, StringComparison.Ordinal);
        Assert.Equal("Skip them, so only pages without text are recognized", Find<RadioButton>(dialog, "PART_ModeSkip").Content);

        // The range field stays for "Only these pages" and is off for the current page.
        current.IsChecked = true;
        PrintHost.Pump();
        Assert.False(Find<TextBox>(dialog, "PART_Range").IsEnabled);
        Click(dialog, "PART_Recognize");
        Assert.Equal(new HashSet<int> { 3 }, Assert.IsType<OcrOptions>(Assert.Single(results)).Pages);
        window.Close();

        // Without a page to name the option is off.
        dialog = RecognizeCard.Create("scan.pdf", 6, ListOf("eng"), false, _ => { });
        window = Show(dialog);
        Assert.False(Find<RadioButton>(dialog, "PART_PagesCurrent").IsEnabled);
        window.Close();

        // A page beyond the document is no page: the option is off.
        dialog = RecognizeCard.Create("scan.pdf", 6, ListOf("eng"), false, _ => { }, currentPage: 6);
        window = Show(dialog);
        Assert.False(Find<RadioButton>(dialog, "PART_PagesCurrent").IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public async Task CurrentPageDoesNotFollowTheViewAfterTheCardOpened()
    {
        var (f, tab) = await OpenedAsync("eng");
        using var _ = f;
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(tab)?.FirstSharpPageShown == true);
        var started = new List<OcrOptions>();
        f.Window.OcrLibraryProbe = () => true;
        f.Window.OcrRunStarter = (_, o) => started.Add(o);
        var view = f.Window.ViewOf(tab)!;
        view.GoToPage(1);
        ViewFixture.Pump();
        var atOpening = view.CurrentPage;

        var run = f.Window.RecognizeAsync();
        await PrintHost.UntilAsync(() => f.Window.Host.ActiveDialog is { } d && d.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "PART_Recognize"));
        var card = f.Window.Host.ActiveDialog!;
        Assert.Contains((atOpening + 1).ToString(CultureInfo.CurrentCulture), (string)Find<RadioButton>(card, "PART_PagesCurrent").Content!, StringComparison.Ordinal);
        Find<RadioButton>(card, "PART_PagesCurrent").IsChecked = true;
        view.GoToPage(0);
        ViewFixture.Pump();
        Click(card, "PART_Recognize");
        await PrintHost.UntilAsync(() => run.IsCompleted);

        Assert.Equal(new HashSet<int> { atOpening }, Assert.Single(started).Pages);
    }

    [Fact]
    public void LanguagesAreLockedWhileRecognitionRuns()
    {
        var running = false;
        var rules = new PdfCommandRules(() => true, () => 1, () => null, modes: new ModeExclusion(mode => mode == PdfMode.Ocr && running));
        Assert.True(rules.Allows("ocr.languages"));
        Assert.Null(rules.ReasonFor("ocr.languages"));

        running = true;
        Assert.False(rules.Allows("ocr.languages"));
        Assert.Equal(PdfStrings.ModeOcrReason, rules.ReasonFor("ocr.languages"));
        PdfStrings.Culture = CultureInfo.GetCultureInfo("de");
        Assert.Equal("Warten Sie, bis die Texterkennung fertig ist, oder brechen Sie sie ab.", rules.ReasonFor("ocr.languages"));

        // Another mode in the way does not lock it; without a document nothing is on.
        Assert.False(new PdfCommandRules(() => false, () => 0, () => null).Allows("ocr.languages"));
    }

    [AvaloniaFact]
    public void NoLanguageFilesMeansNothingToStart()
    {
        var results = new List<OcrOptions?>();
        var dialog = RecognizeCard.Create("scan.pdf", 6, ListOf(), false, results.Add);
        var window = Show(dialog);
        Assert.False(Find<Button>(dialog, "PART_Recognize").IsEnabled);
        Assert.Equal(PdfStrings.OcrNoLanguages, Find<TextBlock>(dialog, "PART_NoLanguages").Text);
        Click(dialog, "PART_Recognize");
        Assert.Empty(results);
        window.Close();
    }

    [AvaloniaFact]
    public void SignedFileWithoutChangeRightsGetsTheChangeNote()
    {
        foreach (var signed in new[] { true, false })
        {
            var dialog = RecognizeCard.Create("scan.pdf", 2, ListOf("eng"), signed, _ => { });
            var window = Show(dialog);
            Assert.Equal(signed, dialog.GetVisualDescendants().OfType<TextBlock>().Any(b => b.Name == "PART_SignedChange"));
            if (signed)
            {
                Assert.Equal("The recognized text counts as a change to the signed file.", Find<TextBlock>(dialog, "PART_SignedChange").Text);
            }

            // The sentence about accuracy is always there.
            Assert.StartsWith("Recognition is not error-free", Find<TextBlock>(dialog, "PART_Accuracy").Text, StringComparison.Ordinal);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task NoLibraryMeansCommandDisabledWithReason()
    {
        var (f, _) = await OpenedAsync("eng");
        using var _ = f;
        var window = f.Window;
        var command = Command(window, "ocr.run");

        // Library there and a starter wired: the command is on.
        window.OcrLibraryProbe = () => true;
        window.OcrRunStarter = (_, _) => { };
        Assert.True(command.IsEnabled);

        // The library is missing: disabled, with the reason, in both languages.
        window.OcrLibraryProbe = () => false;
        Assert.False(command.IsEnabled);
        Assert.Equal("Text recognition is not part of this installation.", command.CurrentDisabledReason);
        PdfStrings.Culture = CultureInfo.GetCultureInfo("de");
        Assert.Equal("Die Texterkennung ist nicht Teil dieser Installation.", command.CurrentDisabledReason);
        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");

        // Nothing starts it: the card is not offered, the command stays off, but the language files stay reachable.
        window.OcrLibraryProbe = () => true;
        window.OcrRunStarter = null;
        Assert.False(command.IsEnabled);
        Assert.True(Command(window, "ocr.languages").IsEnabled);

        // The rule itself, without a window.
        var rules = new PdfCommandRules(() => true, () => 1, () => null, ocrLibrary: () => false, ocrStarter: () => true);
        Assert.False(rules.Allows("ocr.run"));
        Assert.Equal(PdfStrings.OcrNoLibraryReason, rules.ReasonFor("ocr.run"));
        Assert.True(new PdfCommandRules(() => true, () => 1, () => null).Allows("ocr.run"));
    }

    [AvaloniaFact]
    public void TextOfTheCardNeverSaysCorrectOrComplete()
    {
        // Whole words only. "not error-free" is the one allowed form: the sentence is required, and it is the only place the word is used.
        var forbidden = new Regex(
            @"\b(correct|correctly|complete|completely|incomplete|exact|exactly|safe|safely|secure|guaranteed|guarantee|all found|error-free|fully|valid|legally binding"
            + @"|korrekt|richtig|vollständig|vollständige|komplett|sicher|garantiert|fehlerfrei|fehlerfreie|gültig|rechtsverbindlich|exakt)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var sentence = new Dictionary<string, string> { ["en"] = "Recognition is not error-free.", ["de"] = "Die Erkennung ist nicht fehlerfrei." };

        foreach (var language in new[] { "en", "de" })
        {
            PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
            var texts = new List<string>();
            foreach (var dialog in new[]
            {
                RecognizeCard.Create("scan.pdf", 2, ListOf("eng", "deu"), signedWithoutChangeRights: true, _ => { }),
                LanguageFilesCard.Create([new TessdataLanguage("eng", TessdataOrigin.Shipped, 1, "e"), new TessdataLanguage("fra", TessdataOrigin.Imported, 1, "f")], _ => { }),
                LanguageFilesCard.CreateReplace("fra", _ => { }),
            })
            {
                var window = Show(dialog);
                texts.AddRange(TextsOf(dialog));
                window.Close();
            }

            texts.AddRange(Enum.GetValues<TessdataImportError>().Select(e => PdfStrings.OcrImportMessage(e, "fra")));
            texts.AddRange([PdfStrings.OcrNoLibraryReason, PdfStrings.OcrNoLanguages, PdfStrings.OcrSlowWarning, PdfStrings.OcrLanguageRefusal(LanguageChoice.TooMany), PdfStrings.OcrLanguageRefusal(LanguageChoice.NotInstalled)]);

            Assert.Contains(texts, t => t.Contains(sentence[language], StringComparison.Ordinal));
            foreach (var text in texts)
            {
                var rest = text.Replace(sentence[language], string.Empty, StringComparison.Ordinal);
                Assert.DoesNotMatch(forbidden, rest);
            }

            // The sentence about accuracy is the one that carries the word, and it says "not".
            Assert.Single(texts, t => forbidden.IsMatch(t));
        }
    }
}
