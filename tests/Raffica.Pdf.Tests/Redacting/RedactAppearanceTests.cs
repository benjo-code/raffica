using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Core.Redaction;
using Raffica.Core.Text;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Redacting;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

public sealed class RedactAppearanceTests
{
    private const string Cyrillic = "Hallo Привет";

    private static RedactionFill Hex(string text)
    {
        Assert.True(RedactionFill.TryParse(text, out var fill));
        return fill;
    }

    private static Guid Mark(RedactSession session, int page = 0, double top = 100)
    {
        var result = session.AddArea(page, new PageRect(50, top, 150, top + 20));
        Assert.True(result.IsDone, result.Problem?.ToString());
        return result.MarkId!.Value;
    }

    private static Guid TextMark(RedactSession session, string term)
    {
        var mark = new RedactionMark(Guid.NewGuid(), RedactionKind.Text, 0, [new PageRect(50, 300, 150, 320)], term, false);
        Assert.True(session.Replace(session.Plan.With(mark)).IsDone);
        return mark.Id;
    }

    private static void Press(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = target });

    private static void Commit(TextBox box, string text)
    {
        box.Text = text;
        Press(box, Key.Enter);
        ViewFixture.Pump();
    }

    [AvaloniaFact]
    public async Task FillAcceptsPresetsAndHexOnly()
    {
        using var f = await RedactFixture.OpenAsync();
        using var pane = new RedactPane(f.Session, f.View);
        Mark(f.Session);

        foreach (var preset in new[] { RedactionFill.Black, RedactionFill.White }.Concat(RedactionFill.Presets))
        {
            Assert.True(f.Session.SetFill(preset).IsDone);
            Assert.Equal(preset, f.Session.Plan.Fill);
        }

        Assert.False(RedactAppearance.TryParseFill("#12AB9z", out _));
        Assert.False(RedactAppearance.TryParseFill("12AB9F", out _));
        Assert.False(RedactAppearance.TryParseFill("#12AB9", out _));
        Assert.True(RedactAppearance.TryParseFill("  #12ab9f ", out var parsed));
        Assert.Equal(Hex("#12AB9F"), parsed);

        // The field: a bad value is refused with a note and changes nothing, a good one is the fill.
        var before = f.Session.Version;
        var fillBefore = f.Session.Plan.Fill;
        Commit(pane.FillBox, "#12AB9z");
        Assert.Equal(fillBefore, f.Session.Plan.Fill);
        Assert.Equal(before, f.Session.Version);
        Assert.Equal(PdfStrings.RedactRefusal(RedactProblem.FillNotAColour), pane.NoteText);
        Assert.Equal(fillBefore.ToHex(), pane.FillBox.Text);

        Commit(pane.FillBox, "#12ab9f");
        Assert.Equal(Hex("#12AB9F"), f.Session.Plan.Fill);

        // A preset button is the same step.
        Assert.Equal(RedactionFill.Presets.Count + 2, pane.FillPresets.Count);
        pane.FillPresets[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(RedactionFill.White, f.Session.Plan.Fill);
        Assert.Equal("#FFFFFF", pane.FillBox.Text);
    }

    [AvaloniaFact]
    public async Task LookChangesAreOneStepEachAndUndoReachesThePane()
    {
        using var f = await RedactFixture.OpenAsync();
        using var pane = new RedactPane(f.Session, f.View);
        Mark(f.Session);
        Assert.True(f.Session.SetFill(Hex("#1F3A5F")).IsDone);
        Assert.True(f.Session.SetImageTreatment(RedactionImageTreatment.Blur, RedactionImageStrength.VeryCoarse).IsDone);

        Assert.Equal(RedactionImageTreatment.Blur, f.Session.Plan.ImageTreatment);
        Assert.Equal(RedactionImageStrength.VeryCoarse, f.Session.Plan.ImageStrength);
        f.Session.Undo();
        Assert.Equal(RedactionImageTreatment.Solid, f.Session.Plan.ImageTreatment);
        Assert.Equal(0, pane.ImageBox.SelectedIndex);
        f.Session.Undo();
        Assert.Equal(RedactionFill.Black, f.Session.Plan.Fill);
        Assert.Equal("#000000", pane.FillBox.Text);
    }

    [AvaloniaFact]
    public async Task CautionShownForBlurAndPixelate()
    {
        using var f = await RedactFixture.OpenAsync();
        using var pane = new RedactPane(f.Session, f.View);
        Mark(f.Session);

        Assert.Equal(string.Empty, pane.CautionText);
        Assert.False(pane.StrengthBox.IsVisible);

        foreach (var treatment in new[] { RedactionImageTreatment.Blur, RedactionImageTreatment.Pixelate })
        {
            Assert.True(f.Session.SetImageTreatment(treatment, RedactionImageStrength.Coarse).IsDone);
            Assert.Equal(PdfStrings.RedactImageCaution, pane.CautionText);
            Assert.True(pane.StrengthBox.IsVisible);
        }

        Assert.True(f.Session.SetImageTreatment(RedactionImageTreatment.Solid, RedactionImageStrength.Coarse).IsDone);
        Assert.Equal(string.Empty, pane.CautionText);

        // Chosen in the box: the same.
        pane.ImageBox.SelectedIndex = 2;
        ViewFixture.Pump();
        Assert.Equal(RedactionImageTreatment.Blur, f.Session.Plan.ImageTreatment);
        Assert.NotEqual(string.Empty, pane.CautionText);

        Assert.True(RedactAppearance.NeedsCaution(RedactionImageTreatment.Pixelate));
        Assert.False(RedactAppearance.NeedsCaution(RedactionImageTreatment.Solid));
    }

    [AvaloniaFact]
    public async Task OverlayTextOutsideTheCharacterSetIsRefusedWhole()
    {
        using var f = await RedactFixture.OpenAsync();
        using var pane = new RedactPane(f.Session, f.View);
        var id = Mark(f.Session);
        var ids = new HashSet<Guid> { id };
        var style = f.Session.Plan.Overlay;

        Assert.True(f.Session.SetOverlay(ids, "Name € ä", style).IsDone);
        Assert.Equal("Name € ä", f.Session.Plan.Marks[0].OverlayText);
        var version = f.Session.Version;

        var cases = new (string Text, OverlayCharacterClass Class)[]
        {
            (Cyrillic, OverlayCharacterClass.Letters),
            ("Hallo 中", OverlayCharacterClass.Letters),
            ("Hallo \U0001F600", OverlayCharacterClass.Symbols),
            ("Hallo ☃", OverlayCharacterClass.Symbols),
            ("Zeile 1\nZeile 2", OverlayCharacterClass.Control),
            ("a\tb", OverlayCharacterClass.Control),
        };
        foreach (var (text, expected) in cases)
        {
            var result = f.Session.SetOverlay(ids, text, style);
            Assert.Equal(RedactProblem.OverlayCharactersRefused, result.Problem);
            Assert.Equal(expected, result.CharacterClass);
            // Refused whole: nothing changed, the earlier text is still there.
            Assert.Equal("Name € ä", f.Session.Plan.Marks[0].OverlayText);
            Assert.Equal(version, f.Session.Version);
        }

        // The note names the class in words and never carries the text.
        f.Session.Selection.Select(id);
        ViewFixture.Pump();
        Commit(pane.OverlayBox, Cyrillic);
        Assert.Equal(PdfStrings.RedactRefusal(f.Session.SetOverlay(ids, Cyrillic, style)), pane.NoteText);
        Assert.Equal(PdfStrings.Get("RedactRefusalOverlayLetters"), pane.NoteText);
        Assert.DoesNotContain("П", pane.NoteText, StringComparison.Ordinal);
        Assert.Equal("Name € ä", f.Session.Plan.Marks[0].OverlayText);

        // Every class has its own wording in both languages.
        var de = CultureInfo.GetCultureInfo("de");
        var words = Enum.GetValues<OverlayCharacterClass>().Select(c => PdfStrings.RedactRefusal(new RedactResult(RedactProblem.OverlayCharactersRefused, null, c))).ToList();
        Assert.Equal(words.Count, words.Distinct().Count());
        foreach (var key in new[] { "RedactRefusalOverlayControl", "RedactRefusalOverlayLetters", "RedactRefusalOverlaySymbols", "RedactRefusalOverlayCharacters" })
        {
            Assert.NotEqual(PdfStrings.Get(key), PdfStrings.GetIn(key, de));
        }
    }

    [AvaloniaFact]
    public async Task OverlayTextOver200CharactersIsRefused()
    {
        using var f = await RedactFixture.OpenAsync();
        var id = Mark(f.Session);
        var ids = new HashSet<Guid> { id };
        var style = f.Session.Plan.Overlay;

        Assert.True(f.Session.SetOverlay(ids, new string('x', 200), style).IsDone);
        var version = f.Session.Version;
        var result = f.Session.SetOverlay(ids, new string('x', 201), style);

        Assert.Equal(RedactProblem.OverlayTooLong, result.Problem);
        Assert.Equal(new string('x', 200), f.Session.Plan.Marks[0].OverlayText);
        Assert.Equal(version, f.Session.Version);
        Assert.Contains("200", PdfStrings.RedactRefusal(RedactProblem.OverlayTooLong), StringComparison.Ordinal);
        Assert.Contains("{0}", PdfStrings.GetIn("RedactRefusalOverlayTooLongFormat", CultureInfo.GetCultureInfo("de")), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task OverlayThatRepeatsAMarkedTermIsRefused()
    {
        using var f = await RedactFixture.OpenAsync();
        var area = Mark(f.Session);
        TextMark(f.Session, "Secret Value 4711");
        var ids = new HashSet<Guid> { area };
        var style = f.Session.Plan.Overlay;
        var version = f.Session.Version;

        var result = f.Session.SetOverlay(ids, "see secret value 4711 here", style);

        Assert.Equal(RedactProblem.OverlayRepeatsTerm, result.Problem);
        Assert.Null(f.Session.Plan.Marks.First(m => m.Id == area).OverlayText);
        Assert.Equal(version, f.Session.Version);
        Assert.True(f.Session.SetOverlay(ids, "redacted", style).IsDone);
        Assert.DoesNotContain("Secret", PdfStrings.RedactRefusal(RedactProblem.OverlayRepeatsTerm), StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task LowContrastTextColourWarnsButIsAllowed()
    {
        using var f = await RedactFixture.OpenAsync();
        using var pane = new RedactPane(f.Session, f.View);
        var id = Mark(f.Session);
        var ids = new HashSet<Guid> { id };
        Assert.Equal(string.Empty, pane.ContrastWarningText);

        // Dark grey on black: below 4.5:1, allowed, and said in the pane.
        var grey = Hex("#333333");
        Assert.True(RedactionOverlayColor.Contrast(grey, RedactionFill.Black) < 4.5);
        var result = f.Session.SetOverlay(ids, "x", new RedactionOverlayStyle(false, true, grey));
        Assert.True(result.IsDone);
        Assert.Equal(grey, f.Session.Plan.Overlay.TextColor);
        Assert.NotEqual(string.Empty, pane.ContrastWarningText);
        Assert.Contains("4.5", pane.ContrastWarningText, StringComparison.Ordinal);

        // White on black reads well: no warning. The automatic colour never warns.
        Assert.True(f.Session.SetOverlayStyle(new RedactionOverlayStyle(false, true, RedactionFill.White)).IsDone);
        Assert.Equal(string.Empty, pane.ContrastWarningText);
        Assert.True(f.Session.SetOverlayStyle(new RedactionOverlayStyle(false, true, null)).IsDone);
        Assert.Equal(string.Empty, pane.ContrastWarningText);
        Assert.Null(RedactAppearance.LowContrast(f.Session.Plan));

        // The colour field: valid hex is taken (and warns), junk is refused with a note, empty is the automatic colour.
        Commit(pane.TextColorBox, "#333333");
        Assert.Equal(grey, f.Session.Plan.Overlay.TextColor);
        Assert.NotEqual(string.Empty, pane.ContrastWarningText);
        Commit(pane.TextColorBox, "red");
        Assert.Equal(grey, f.Session.Plan.Overlay.TextColor);
        Assert.Equal(PdfStrings.RedactRefusal(RedactProblem.FillNotAColour), pane.NoteText);
        Commit(pane.TextColorBox, string.Empty);
        Assert.Null(f.Session.Plan.Overlay.TextColor);
    }

    [AvaloniaFact]
    public async Task RasterPageIsFreeToChooseAndDpiIsOneOfThree()
    {
        using var f = await RedactFixture.OpenAsync();
        using var pane = new RedactPane(f.Session, f.View);

        // No mark is needed; any page of the document may be chosen.
        Assert.Equal(200, f.Session.Plan.RasterDpi);
        Assert.True(f.Session.SetRasterPage(1, true).IsDone);
        Assert.Contains(1, f.Session.Plan.RasterPages);
        Assert.Empty(f.Session.Plan.Marks);
        Assert.True(f.Session.SetRasterPage(1, false).IsDone);
        Assert.Empty(f.Session.Plan.RasterPages);

        var pages = f.Tab.Session.Document!.PageCount;
        Assert.Equal(RedactProblem.OutOfRange, f.Session.SetRasterPage(-1, true).Problem);
        Assert.Equal(RedactProblem.OutOfRange, f.Session.SetRasterPage(pages, true).Problem);

        foreach (var bad in new[] { 72, 100, 199, 250, 301, 0, -150 })
        {
            Assert.Equal(RedactProblem.DpiNotOffered, f.Session.SetRasterDpi(bad).Problem);
            Assert.Equal(200, f.Session.Plan.RasterDpi);
        }

        Assert.Equal([150, 200, 300], RedactAppearance.Dpis);
        foreach (var good in RedactAppearance.Dpis)
        {
            Assert.True(f.Session.SetRasterDpi(good).IsDone);
            Assert.Equal(good, f.Session.Plan.RasterDpi);
        }

        Assert.True(f.Session.SetRasterDpi(200).IsDone);

        // The pane: the box of the current page toggles it, and the box of the resolution offers exactly the three.
        Assert.NotEqual(true, pane.RasterPageBox.IsChecked);
        pane.RasterPageBox.IsChecked = true;
        ViewFixture.Pump();
        Assert.Contains(f.View.CurrentPage, f.Session.Plan.RasterPages);
        Assert.Equal(3, pane.DpiBox.ItemCount);
        Assert.False(f.Session.Plan.IsEmpty);
        pane.DpiBox.SelectedIndex = 0;
        ViewFixture.Pump();
        Assert.Equal(150, f.Session.Plan.RasterDpi);
        pane.RasterPageBox.IsChecked = false;
        ViewFixture.Pump();
        Assert.Empty(f.Session.Plan.RasterPages);
    }

    [AvaloniaFact]
    public async Task PreviewShowsFillsOnlyAndSaysSo()
    {
        using var f = await RedactFixture.OpenAsync();
        using var pane = new RedactPane(f.Session, f.View);
        Mark(f.Session);

        Assert.Equal(PdfStrings.RedactPreviewNote, pane.PreviewNoteText);
        Assert.Contains("after applying", pane.PreviewNoteText, StringComparison.Ordinal);
        Assert.Contains("overlay text", pane.PreviewNoteText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pixels", pane.PreviewNoteText, StringComparison.Ordinal);
        Assert.NotEqual(PdfStrings.RedactPreviewNote, PdfStrings.GetIn("RedactPreviewNote", CultureInfo.GetCultureInfo("de")));
    }

    [AvaloniaFact]
    public async Task OverlayTextIsSetOnTheSelectedMarksOnEnterAndNeverNamedToAssistiveTechnology()
    {
        using var f = await RedactFixture.OpenAsync();
        using var pane = new RedactPane(f.Session, f.View);
        var a = Mark(f.Session, 0, 100);
        var b = Mark(f.Session, 0, 200);
        var c = Mark(f.Session, 1, 100);

        // Nothing selected: the field is off.
        f.Session.Selection.Clear();
        ViewFixture.Pump();
        Assert.False(pane.OverlayBox.IsEnabled);

        f.Session.Selection.Set([a, b]);
        ViewFixture.Pump();
        Assert.True(pane.OverlayBox.IsEnabled);
        pane.OverlayBox.Text = "REMOVED";
        Assert.Null(f.Session.Plan.Marks.First(m => m.Id == a).OverlayText);   // not while typing
        Press(pane.OverlayBox, Key.Enter);
        ViewFixture.Pump();

        Assert.Equal("REMOVED", f.Session.Plan.Marks.First(m => m.Id == a).OverlayText);
        Assert.Equal("REMOVED", f.Session.Plan.Marks.First(m => m.Id == b).OverlayText);
        Assert.Null(f.Session.Plan.Marks.First(m => m.Id == c).OverlayText);

        // Repeat and auto size are the plan's style.
        pane.RepeatBox.IsChecked = true;
        pane.AutoSizeBox.IsChecked = false;
        ViewFixture.Pump();
        Assert.True(f.Session.Plan.Overlay.Repeat);
        Assert.False(f.Session.Plan.Overlay.AutoSize);

        // Clearing the field clears the text.
        Commit(pane.OverlayBox, string.Empty);
        Assert.Null(f.Session.Plan.Marks.First(m => m.Id == a).OverlayText);

        // No name for assistive technology carries the text.
        Commit(pane.OverlayBox, "TOPSECRETWORDING");
        Assert.Equal("TOPSECRETWORDING", f.Session.Plan.Marks.First(m => m.Id == a).OverlayText);
        foreach (var control in pane.GetVisualDescendants().OfType<Control>())
        {
            Assert.DoesNotContain("TOPSECRETWORDING", AutomationProperties.GetName(control) ?? string.Empty, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("TOPSECRETWORDING", string.Join('|', pane.RowNames), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task KeysTypedIntoTheLookFieldsAreTheirsAndNeverReachTheMarks()
    {
        using var f = await RedactFixture.OpenAsync();
        using var pane = new RedactPane(f.Session, f.View);
        var id = Mark(f.Session);
        f.Session.Selection.Select(id);
        ViewFixture.Pump();

        foreach (var box in new[] { pane.OverlayBox, pane.FillBox, pane.TextColorBox })
        {
            foreach (var key in new[] { Key.Delete, Key.Back, Key.Left, Key.Escape })
            {
                // A key typed into the box goes up the pane's tree, never through the page view: the marks are not touched.
                Press(box, key);
            }

            Press(box, Key.Delete, KeyModifiers.Control);
        }

        ViewFixture.Pump();
        Assert.Single(f.Session.Plan.Marks);
        Assert.True(f.Session.IsActive);
        Assert.Contains(id, f.Session.Selection.Ids);
    }

    [Fact]
    public void LookWordingExistsInBothLanguagesAndNeverPromisesCompleteness()
    {
        var de = CultureInfo.GetCultureInfo("de");
        var forbidden = new Regex(@"\b(all found|error-free|safe|safely|complete|guaranteed|sicher|vollständig|garantiert|fehlerfrei)\b", RegexOptions.IgnoreCase);
        var keys = new[]
        {
            "RedactLookToggle", "RedactLookName", "RedactFillLabel", "RedactFillName", "RedactFillPresetFormat", "RedactImageLabel", "RedactImageSolid", "RedactImagePixelate",
            "RedactImageBlur", "RedactStrengthLabel", "RedactStrengthCoarse", "RedactStrengthVeryCoarse", "RedactImageCaution", "RedactOverlayLabel", "RedactOverlayName",
            "RedactOverlayPlaceholder", "RedactOverlayRepeat", "RedactOverlayAutoSize", "RedactTextColorLabel", "RedactTextColorName", "RedactContrastWarningFormat",
            "RedactRasterLabel", "RedactRasterPageFormat", "RedactRasterCountOne", "RedactRasterCountManyFormat", "RedactRasterDpiLabel", "RedactRasterDpiFormat",
            "RedactRasterHint", "RedactPreviewNote", "RedactRefusalFillNotColour", "RedactRefusalOverlayTooLongFormat", "RedactRefusalOverlayControl",
            "RedactRefusalOverlayLetters", "RedactRefusalOverlaySymbols", "RedactRefusalOverlayCharacters", "RedactRefusalOverlayRepeats", "RedactRefusalDpiNotOffered",
            "RedactRefusalTooManyRasterFormat",
        };
        foreach (var key in keys)
        {
            var en = PdfStrings.Get(key);
            var german = PdfStrings.GetIn(key, de);
            Assert.False(string.IsNullOrWhiteSpace(en), key);
            Assert.False(string.IsNullOrWhiteSpace(german), key);
            Assert.DoesNotMatch(forbidden, en);
            Assert.DoesNotMatch(forbidden, german);
        }

        // Every problem has its own wording.
        var all = Enum.GetValues<RedactProblem>();
        Assert.Equal(all.Length, all.Select(p => PdfStrings.RedactRefusal(p)).Distinct().Count());
        Assert.Contains("1000", PdfStrings.RedactRefusal(RedactProblem.TooManyRasterPages).Replace(",", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).Replace(".", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task RasterPageCommandTogglesTheCurrentPage()
    {
        using var f = WindowFixture.Create();
        var tab = await f.OpenAsync("pages-marked.pdf");
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(tab)?.FirstSharpPageShown == true);
        var commands = f.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).ToList();
        var mode = commands.Single(c => c.Id == "redact.mode");
        var raster = commands.Single(c => c.Id == "redact.rasterPage");

        mode.Run!();
        await ViewFixture.WaitAsync(() => tab.RedactIfCreated is { IsActive: true });
        var view = f.Window.ViewOf(tab)!;
        Assert.NotNull(raster.Run);
        raster.Run!();
        ViewFixture.Pump();
        Assert.Equal([view.CurrentPage], tab.Redact.Plan.RasterPages.ToArray());

        raster.Run!();
        ViewFixture.Pump();
        Assert.Empty(tab.Redact.Plan.RasterPages);
    }

    [AvaloniaFact]
    public async Task TextTypedForSelectedMarksStaysWithThemWhenAnotherRowIsChosenBeforeTheCommit()
    {
        using var f = await RedactFixture.OpenAsync();
        using var pane = new RedactPane(f.Session, f.View);
        var a = Mark(f.Session, 0, 100);
        var b = Mark(f.Session, 0, 200);
        f.Session.Selection.Select(a);
        ViewFixture.Pump();

        pane.OverlayBox.Text = "FOR A";
        f.Session.Selection.Select(b);
        ViewFixture.Pump();
        Assert.Equal("FOR A", pane.OverlayBox.Text);
        Press(pane.OverlayBox, Key.Enter);
        ViewFixture.Pump();

        Assert.Equal("FOR A", f.Session.Plan.Marks.First(m => m.Id == a).OverlayText);
        Assert.Null(f.Session.Plan.Marks.First(m => m.Id == b).OverlayText);
    }
}
