using Avalonia.Headless.XUnit;
using Raffica.Shell.Tests.Frame;
using Xunit;

namespace Raffica.Pdf.Tests;

/// <summary>
/// M2.7 Task 10 review: the key tips (Alt) of the ribbon of the app cover no text of any control of the band and stay inside the band,
/// on every tab, in the full and the compact band, in English and German.
/// </summary>
public sealed class PdfRibbonKeyTipTests
{
    public static TheoryData<string, bool> Cases => new()
    {
        { "en", false },
        { "en", true },
        { "de", false },
        { "de", true },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task NoKeyTipCoversTextOrLeavesTheBand(string language, bool compact)
    {
        using var f = await PdfRibbonLayoutTests.ShowWithDocumentAsync(language, 1920);
        var window = f.Window;
        window.SetCompact(compact);
        PdfRibbonLayoutTests.Layout(window);
        window.Ribbon.ShowFieldCodes = true;
        var offenders = new List<string>();
        foreach (var tab in window.Ribbon.Definition!.Tabs)
        {
            window.Ribbon.ActiveTab = tab;
            window.Ribbon.FieldCodeTabLetter = tab.Letter;
            PdfRibbonLayoutTests.Layout(window);
            Assert.Equal(compact, window.Ribbon.IsEffectivelyCompact);
            offenders.AddRange(BadgeFit.Find(window, window.Ribbon.Band).Select(o => $"{tab.Name}: {o}"));
        }

        Assert.True(offenders.Count == 0, $"[{language} compact {compact}]{Environment.NewLine}{string.Join(Environment.NewLine, offenders.Distinct())}");
    }
}
