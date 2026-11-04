using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Gallery;
using Raffica.Gallery.FrameDemo;
using Raffica.Shell.Localization;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

/// <summary>
/// The frame in German at the full width (1440 px, full ribbon) and at the compact width (1024 px, compact command bar): no ribbon
/// label, document tab or status cell text may be clipped (design spec: the layout holds for the longest language, German).
/// </summary>
public sealed class GermanLayoutTests
{
    public static TheoryData<int, bool> Sizes => new() { { 1440, false }, { 1024, true } };

    public static TheoryData<int, bool, ThemeMode> SizesAndThemes
    {
        get
        {
            var data = new TheoryData<int, bool, ThemeMode>();
            foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
            {
                data.Add(1440, false, mode);
                data.Add(1024, true, mode);
            }

            return data;
        }
    }

    // Shows the frame demo in a language and size; the previous cultures come back on dispose.
    private sealed class Demo : IDisposable
    {
        private readonly (CultureInfo Gallery, CultureInfo Shell) _previous = (GalleryStrings.Culture, ShellStrings.Culture);

        public Demo(string lang, int width, bool compact, ThemeMode mode)
        {
            GalleryStrings.Culture = ShellStrings.Culture = CultureInfo.GetCultureInfo(lang);
            ShellTheme.Apply(Application.Current!, mode, AppInk.Write, forceHighContrast: false);
            SettingsPath = Path.Combine(Path.GetTempPath(), "raffica-german-layout-" + Guid.NewGuid().ToString("N") + ".json");
            Window = new FrameDemoWindow(GalleryOptions.Parse(["--view", "frame", "--lang", lang, "--settings-file", SettingsPath]))
            {
                Width = width,
                Height = 700,
            };
            Window.SetCompact(compact);
            Window.Show();
            ControlHarness.Settle(Window);
        }

        public FrameDemoWindow Window { get; }

        private string SettingsPath { get; }

        public void Dispose()
        {
            Window.Close();
            GalleryStrings.Culture = _previous.Gallery;
            ShellStrings.Culture = _previous.Shell;
            File.Delete(SettingsPath);
        }
    }

    private static IEnumerable<string> ClippedOnEveryTab(FrameDemoWindow window)
    {
        foreach (var tab in window.Ribbon.Definition!.Tabs.Where(t => t.Letter != DemoRibbon.FileLetter))
        {
            window.Ribbon.ActiveTab = tab;
            ControlHarness.Settle(window);
            foreach (var clipped in LabelFit.FindClipped(window))
            {
                yield return $"{tab.Name}: {clipped}";
            }
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(SizesAndThemes))]
    public void NoRibbonLabelIsClippedInGerman(int width, bool compact, ThemeMode mode)
    {
        using var demo = new Demo("de", width, compact, mode);

        var clipped = ClippedOnEveryTab(demo.Window).ToList();

        Assert.True(clipped.Count == 0, $"Clipped at {width} px (compact {compact}, {mode}):{Environment.NewLine}{string.Join(Environment.NewLine, clipped)}");
    }

    [AvaloniaTheory]
    [MemberData(nameof(Sizes))]
    public void EnglishFitsToo(int width, bool compact)
    {
        using var demo = new Demo("en", width, compact, ThemeMode.Light);

        var clipped = ClippedOnEveryTab(demo.Window).ToList();

        Assert.True(clipped.Count == 0, $"Clipped at {width} px (compact {compact}):{Environment.NewLine}{string.Join(Environment.NewLine, clipped)}");
    }

    [AvaloniaTheory]
    [MemberData(nameof(Sizes))]
    public void NoDocumentTabOrStatusCellIsClippedInGerman(int width, bool compact) => AssertTabsAndStatusFit("de", width, compact);

    [AvaloniaTheory]
    [MemberData(nameof(Sizes))]
    public void TabsAndStatusCellsFitInEnglishToo(int width, bool compact) => AssertTabsAndStatusFit("en", width, compact);

    private static void AssertTabsAndStatusFit(string lang, int width, bool compact)
    {
        using var demo = new Demo(lang, width, compact, ThemeMode.Light);
        var status = demo.Window.Status;
        var texts = new List<string>();

        // The cells in turn: the focus cell shows the F6 text, then the Alt hint takes its slot.
        status.Language = lang == "de" ? "Englisch (US)" : "German (DE)";
        status.FocusPane = ShellStrings.FocusPane(ShellStrings.PaneComments);
        ControlHarness.Settle(demo.Window);
        texts.AddRange(LabelFit.FindClipped(demo.Window));

        status.Hint = ShellStrings.FieldCodeHint;
        ControlHarness.Settle(demo.Window);
        texts.AddRange(LabelFit.FindClipped(demo.Window));

        // A document tab with the longest sample name and a new, numbered one.
        demo.Window.Tabs.Items.Add(new Raffica.Shell.Frame.DocumentTab(GalleryStrings.DocTabNew(12)));
        ControlHarness.Settle(demo.Window);
        texts.AddRange(LabelFit.FindClipped(demo.Window));

        Assert.True(texts.Count == 0, $"Clipped at {width} px ({lang}):{Environment.NewLine}{string.Join(Environment.NewLine, texts.Distinct())}");
    }

    [Fact]
    public void TheGermanDemoNamesAreRealGermanAndAsLongAsTheSpecShows()
    {
        var previous = (GalleryStrings.Culture, ShellStrings.Culture);
        try
        {
            GalleryStrings.Culture = ShellStrings.Culture = CultureInfo.GetCultureInfo("en");
            var english = (GalleryStrings.CmdApply, GalleryStrings.CmdCompact, GalleryStrings.CmdCut);
            GalleryStrings.Culture = ShellStrings.Culture = CultureInfo.GetCultureInfo("de");

            Assert.True(GalleryStrings.CmdApply.Length >= "Endgültig anwenden".Length);
            Assert.True(GalleryStrings.CmdCompact.Length >= "Kompakte Befehlsleiste".Length);
            Assert.Equal("Ausschneiden", GalleryStrings.CmdCut.Length >= 8 ? GalleryStrings.CmdCut : null);
            Assert.NotEqual(english.CmdApply, GalleryStrings.CmdApply);
            Assert.NotEqual(english.CmdCompact, GalleryStrings.CmdCompact);
            Assert.Equal("Start", GalleryStrings.TabHome);
        }
        finally
        {
            (GalleryStrings.Culture, ShellStrings.Culture) = previous;
        }
    }
}
