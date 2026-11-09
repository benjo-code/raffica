using Avalonia.Headless.XUnit;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Localization;
using Raffica.Shell.Tests.Frame;
using Xunit;

namespace Raffica.Pdf.Tests;

/// <summary>
/// M2.7 Task 11 step 1: the status bar of the real window never cuts a cell. The unsaved cell, the page count with the page size and the
/// largest position all show in full at the default width and at 1920, in both languages (the ribbon labels are Task 10's tests).
/// </summary>
public sealed class StatusBarFitTests
{
    public static TheoryData<string, int> LanguagesAndWidths => new()
    {
        { "en", 1280 },
        { "en", 1920 },
        { "de", 1280 },
        { "de", 1920 },
    };

    [AvaloniaTheory]
    [MemberData(nameof(LanguagesAndWidths))]
    public async Task NoStatusCellOfTheWindowIsClipped(string language, int width)
    {
        using var f = await PdfRibbonLayoutTests.ShowWithDocumentAsync(language, width);
        var window = f.Window;
        window.Workspace.Active!.MarkDirty();
        PdfRibbonLayoutTests.Layout(window);

        var shown = new[] { window.Status.SaveState, window.Status.Position, window.Status.Count };
        Assert.Equal(ShellStrings.StatusUnsaved, shown[0]);
        var clipped = Clipped(window);

        // The longest texts the cells can show: a page count and size of a long document and the last of a thousand pages.
        window.Status.Position = language == "de" ? "Seite 1000 von 1000" : "Page 1000 of 1000";
        window.Status.Count = language == "de" ? "128 Seiten · 210 × 297 mm" : "128 pages · 210 × 297 mm";
        PdfRibbonLayoutTests.Layout(window);
        clipped.AddRange(Clipped(window));

        Assert.True(clipped.Count == 0, $"[{language} {width}] clipped:{Environment.NewLine}{string.Join(Environment.NewLine, clipped.Distinct())}");
    }

    private static List<string> Clipped(PdfWindow window) =>
        LabelFit.FindClipped(window).Where(c => c.Contains("(status bar", StringComparison.Ordinal)).ToList();
}
