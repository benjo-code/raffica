using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Tests.Printing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Dialogs;

/// <summary>The page-range fields start with the "all" keyword of the UI language ("alle" in German) and their labels name it in quotes.</summary>
public sealed class RangeKeywordTests
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");

    private static void Show(Dialog dialog)
    {
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 700, Height = 800 };
        window.Show();
        host.Show(dialog);
        PrintHost.Pump();
    }

    private static T Find<T>(Dialog dialog, string name)
        where T : Control =>
        dialog.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [Fact]
    public void TheKeywordIsAllInEnglishAndAlleInGerman()
    {
        Assert.Equal("all", PdfStrings.GetIn("PageKeywordAll", CultureInfo.GetCultureInfo("en")));
        Assert.Equal("alle", PdfStrings.GetIn("PageKeywordAll", German));
    }

    [Theory]
    [InlineData("en", "InsertFileRangeLabel", "\"all\"")]
    [InlineData("en", "InsertRangeInvalid", "\"all\"")]
    [InlineData("en", "ExtractRangeLabel", "\"all\"")]
    [InlineData("en", "ExportRangeLabel", "\"all\"")]
    [InlineData("de", "InsertFileRangeLabel", "„alle“")]
    [InlineData("de", "InsertRangeInvalid", "„alle“")]
    [InlineData("de", "ExtractRangeLabel", "„alle“")]
    [InlineData("de", "ExportRangeLabel", "„alle“")]
    public void TheLabelsNameTheKeywordInQuotes(string language, string key, string quoted) =>
        Assert.Contains(quoted, PdfStrings.GetIn(key, CultureInfo.GetCultureInfo(language)), StringComparison.Ordinal);

    [AvaloniaFact]
    public void TheInsertFieldStartsWithTheKeywordOfTheLanguageAndInsertsAreEnabled()
    {
        PdfStrings.Culture = German;
        var dialog = InsertFileCard.Create("andere.pdf", 6, null, _ => { });
        Show(dialog);
        Assert.Equal("alle", Find<TextBox>(dialog, "PART_Range").Text);
        Assert.True(Find<Button>(dialog, "PART_Insert").IsEnabled);
    }

    [AvaloniaFact]
    public void TheExportFieldStartsWithTheKeywordOfTheLanguage()
    {
        PdfStrings.Culture = German;
        var dialog = ExportImagesCard.Create("doc.pdf", [new PdfPageSize(595, 842)], () => Task.FromResult<string?>(null), _ => { });
        Show(dialog);
        Assert.Equal("alle", Find<TextBox>(dialog, "PART_Range").Text);
    }

    [AvaloniaFact]
    public void TheSplitFieldStartsWithTheKeywordOfTheLanguage()
    {
        PdfStrings.Culture = German;
        var dialog = SplitCard.Create("doc.pdf", 6, false, (mode, every, ranges) => SplitFlow.PlanFor(6, mode, every, ranges, "doc", []), () => Task.FromResult<string?>(null), _ => { });
        Show(dialog);
        Assert.Equal("alle", Find<TextBox>(dialog, "PART_Ranges").Text);
    }
}
