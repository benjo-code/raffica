using System.Globalization;
using Avalonia.Headless.XUnit;
using Raffica.Shell.Tests.Frame;
using Xunit;

namespace Raffica.Pdf.Tests;

/// <summary>M2.7 Task 10 step 3: the ribbon labels of the app in both languages (short German names, no clipped or cut label on any tab).</summary>
public sealed class PdfRibbonLabelTests
{
    public static TheoryData<string, string> GermanLabels => new()
    {
        { "CmdAnnotateMode", "Anmerken" },
        { "CmdApplyPages", "Änderungen anwenden" },
        { "CmdFormsHighlight", "Felder hervorheben" },
        { "CmdPagesRedo", "Wiederholen" },
        { "CmdPagesUndo", "Rückgängig" },
        { "CmdDiscardPages", "Verwerfen" },
        { "CmdExportImages", "Als Bilder exportieren …" },
        { "CmdInsertFile", "Aus Datei einfügen …" },
    };

    public static TheoryData<string, int> LanguagesAndWidths => new()
    {
        { "en", 1920 },
        { "de", 1920 },
        { "en", 1280 },
        { "de", 1280 },
        { "de", 1024 },
    };

    [Theory]
    [MemberData(nameof(GermanLabels))]
    public void TheGermanLabelIsTheShortOne(string key, string expected) =>
        Assert.Equal(expected, PdfStrings.GetIn(key, CultureInfo.GetCultureInfo("de")));

    [Fact]
    public void TheFormsHighlightCommandAndTheBadgeSwitchSayTheSameInGerman()
    {
        var german = CultureInfo.GetCultureInfo("de");
        Assert.Equal(PdfStrings.GetIn("FormHighlightToggle", german), PdfStrings.GetIn("CmdFormsHighlight", german));
    }

    [AvaloniaTheory]
    [MemberData(nameof(LanguagesAndWidths))]
    public async Task NoLabelOfAnyTabIsClippedOrCutInTheMiddleOfAWord(string language, int width)
    {
        using var f = await PdfRibbonLayoutTests.ShowWithDocumentAsync(language, width);
        var window = f.Window;
        var clipped = new List<string>();
        foreach (var tab in window.Ribbon.Definition!.Tabs)
        {
            window.Ribbon.ActiveTab = tab;
            PdfRibbonLayoutTests.Layout(window);
            clipped.AddRange(LabelFit.FindClipped(window).Where(c => c.Contains("(ribbon", StringComparison.Ordinal)).Select(c => $"{tab.Name}: {c}"));
        }

        Assert.True(clipped.Count == 0, $"[{language} {width}] clipped:{Environment.NewLine}{string.Join(Environment.NewLine, clipped)}");
    }
}
