using System.Globalization;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Raffica.Write;
using Raffica.Write.Documents;
using Xunit;

namespace Raffica.WriteApp.Tests;

/// <summary>
/// The start cost of the Home band (found in the final review of milestone 4.3b: the band had grown to about 3000 px, every start built and laid out the full band in a 1280 px window,
/// found it too wide and built the compact band again: 48 ms of the first page). The hint of the tab says it is wider than any window, so the compact band is built at once.
/// </summary>
public sealed class RibbonStartupTests
{
    private static Panel GroupsOf(RibbonView ribbon) =>
        (Panel)typeof(RibbonView).GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ribbon)!;

    private static int FullMeasuresOf(RibbonView ribbon) =>
        (int)typeof(RibbonView).GetProperty("FullMeasureCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ribbon)!;

    [AvaloniaFact]
    public async Task TheDefaultWindowStartsWithTheCompactHomeBandAndNeverMeasuresTheFullBand()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var full = GroupsOf(window.Ribbon).Children.SelectMany(c => c.GetVisualDescendants().Where(v => v is RibbonButton or RibbonPicker)).OfType<Control>().ToList();
        Assert.True(full.Count > 40, $"{full.Count} controls in the full Home band");

        _ = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        WindowFixture.Pump();
        window.UpdateLayout();
        WindowFixture.Pump();

        Assert.True(window.Ribbon.IsEffectivelyCompact);
        Assert.Equal(0, FullMeasuresOf(window.Ribbon));
        // The controls of the first, full build were never measured: the compact band replaced them before the first layout.
        Assert.All(full, b => Assert.True(b.Bounds.Width == 0 && b.DesiredSize.Width == 0, "a control of the full band was laid out"));
    }

    [AvaloniaFact]
    public async Task TheHintOfTheHomeBandIsNoWiderThanTheRealBandInEnglishAndGerman()
    {
        var widths = new List<double>();
        foreach (var language in new[] { "en", "de" })
        {
            var culture = CultureInfo.GetCultureInfo(language);
            ShellStrings.Culture = culture;
            WriteStrings.Culture = culture;
            using var fixture = new WindowFixture();
            var window = fixture.Show();
            window.Width = 4500;
            _ = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
            WindowFixture.Pump();
            window.UpdateLayout();
            WindowFixture.Pump();
            Assert.False(window.Ribbon.IsEffectivelyCompact);
            widths.Add(GroupsOf(window.Ribbon).DesiredSize.Width);
        }

        Assert.All(widths, w => Assert.True(w >= WriteRibbon.HomeFullWidthAtLeast, $"the full Home band measures {w:F0} px, the hint says at least {WriteRibbon.HomeFullWidthAtLeast}"));
    }

    [AvaloniaFact]
    public async Task TheDeferredRibbonIsShownAfterTheFirstPageAndItsBandIsEmptyUntilThen()
    {
        using var fixture = new WindowFixture();
        var window = fixture.ShowWith(null, null, deferRibbon: true);
        Assert.True(window.Ribbon.IsBandHeld);
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;

        // The tab is open and shown, the first page is not drawn yet: the band is an empty strip.
        Assert.True(window.Ribbon.IsBandHeld);
        Assert.Null(((Border)window.Ribbon.Band).Child);

        var drawn = false;
        window.FirstSharpPageDrawn += _ => drawn = true;
        await WindowFixture.WaitAsync(() => drawn || !window.Ribbon.IsBandHeld);
        await WindowFixture.WaitAsync(() => !window.Ribbon.IsBandHeld);
        WindowFixture.Pump();

        Assert.NotNull(((Border)window.Ribbon.Band).Child);
        Assert.True(window.Ribbon.IsEffectivelyCompact);
        Assert.NotNull(tab);
    }

    [AvaloniaFact]
    public async Task TheDeferredRibbonShowsAnywayAfterTheTimeoutWhenNoPageIsDrawn()
    {
        var saved = WriteWindow.RibbonReleaseTimeout;
        WriteWindow.RibbonReleaseTimeout = TimeSpan.FromMilliseconds(50);
        try
        {
            using var fixture = new WindowFixture();
            var window = fixture.ShowWith(null, null, deferRibbon: true);
            _ = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
            await WindowFixture.WaitAsync(() => !window.Ribbon.IsBandHeld);
            Assert.False(window.Ribbon.IsBandHeld);
        }
        finally
        {
            WriteWindow.RibbonReleaseTimeout = saved;
        }
    }

    [AvaloniaFact]
    public async Task ThePickerSearchAndTheKeyTipsReleaseTheDeferredRibbon()
    {
        using var fixture = new WindowFixture();
        var window = fixture.ShowWith(null, null, deferRibbon: true);
        _ = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        Assert.True(window.Ribbon.IsBandHeld);
        var font = window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).First(c => c.Picker is not null);

        window.ChooseFromSearch(font);

        Assert.False(window.Ribbon.IsBandHeld);

        var other = fixture.ShowWith(null, null, deferRibbon: true);
        _ = Assert.IsType<Opened>(await other.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        Assert.True(other.Ribbon.IsBandHeld);
        other.Ribbon.ShowFieldCodes = true;
        Assert.False(other.Ribbon.IsBandHeld);
    }
}
