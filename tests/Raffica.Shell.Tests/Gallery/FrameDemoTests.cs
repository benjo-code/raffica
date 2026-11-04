using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Raffica.Gallery;
using Raffica.Gallery.FrameDemo;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Gallery;

public sealed class FrameDemoTests
{
    /// <summary>A frame demo window on a temporary settings file, in the given language (the previous cultures come back on dispose).</summary>
    private sealed class Demo : IDisposable
    {
        private readonly (CultureInfo Gallery, CultureInfo Shell) _previous = (GalleryStrings.Culture, ShellStrings.Culture);

        public Demo(string lang = "en", ShellSettings? stored = null, params string[] extra)
        {
            GalleryStrings.Culture = ShellStrings.Culture = CultureInfo.GetCultureInfo(lang);
            SettingsPath = Path.Combine(Path.GetTempPath(), "raffica-frame-demo-" + Guid.NewGuid().ToString("N") + ".json");
            if (stored is not null)
            {
                new SettingsStore(SettingsPath).Save(stored);
            }

            ShellTheme.Apply(Application.Current!, ThemeMode.Light, AppInk.Write, forceHighContrast: false);
            Window = new FrameDemoWindow(GalleryOptions.Parse(["--view", "frame", "--lang", lang, "--settings-file", SettingsPath, .. extra]));
            Window.Show();
            ControlHarness.Settle(Window);
        }

        public FrameDemoWindow Window { get; }

        public string SettingsPath { get; }

        public void Settle() => ControlHarness.Settle(Window);

        public void Click(Control control)
        {
            var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)!.Value;
            Window.MouseDown(at, MouseButton.Left);
            Window.MouseUp(at, MouseButton.Left);
            Settle();
        }

        public void CloseAllTabs()
        {
            while (Window.Tabs.Items.Count > 0)
            {
                var count = Window.Tabs.Items.Count;
                var view = Window.Tabs.GetVisualDescendants().OfType<DocumentTabItem>().First();
                Click(ControlHarness.Part<Button>(view, "PART_Close"));
                Assert.True(Window.Tabs.Items.Count < count, "the tab did not close");
            }
        }

        public List<Button> Tiles() =>
            Window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("start-tile")).ToList();

        public void Dispose()
        {
            Window.Close();
            GalleryStrings.Culture = _previous.Gallery;
            ShellStrings.Culture = _previous.Shell;
            File.Delete(SettingsPath);
        }
    }

    [AvaloniaFact]
    public void OpensWithEveryFramePartPresent()
    {
        using var demo = new Demo();
        var window = demo.Window;
        var frame = window.Frame;

        Assert.Equal(36, frame.TitleBar!.Bounds.Height, 0.01);
        Assert.Equal(32, frame.TabRow!.Bounds.Height, 0.01);
        Assert.Equal(82, frame.Band!.Bounds.Height, 0.01);
        Assert.Equal(30, frame.StatusBar!.Bounds.Height, 0.01);
        Assert.Equal(3, window.Tabs.Items.Count);
        Assert.NotNull(frame.LeftPane);
        Assert.NotNull(frame.RightPane);
        Assert.NotNull(frame.Document);
        Assert.Equal(["Home", "Insert", "Review", "View"], window.Ribbon.Definition!.Tabs.Where(t => t.Letter != "F").Select(t => t.Name));
        Assert.Same(window.Ribbon.TabRow, frame.TabRow);
        Assert.Same(window.Ribbon.Band, frame.Band);
        Assert.True(window.InputTarget.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void TheDrawnTitleBarOwnsTheWindowChrome()
    {
        using var demo = new Demo();

        if (!OperatingSystem.IsMacOS())
        {
            Assert.Equal(WindowDecorations.BorderOnly, demo.Window.WindowDecorations);
            Assert.Equal(3, demo.Window.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("caption-button")));
        }

        // The app mark opens the menu with the settings entry and is an input area of the bar, not a drag area.
        var mark = demo.Window.AppMarkButton;
        Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(mark));
    }

    [AvaloniaFact]
    public void CompactOptionGivesA40pxBand()
    {
        using var demo = new Demo(extra: "--compact");

        Assert.Equal(40, demo.Window.Frame.Band!.Bounds.Height, 0.01);
        Assert.True(demo.Window.Frame.IsCompact);
        Assert.True(demo.Window.Ribbon.IsCompact);
    }

    [AvaloniaFact]
    public void TheStoredCompactSettingStartsCompactToo()
    {
        using var demo = new Demo(stored: new ShellSettings(CompactCommandBar: true));

        Assert.Equal(40, demo.Window.Frame.Band!.Bounds.Height, 0.01);
        Assert.True(demo.Window.Ribbon.IsCompact);
    }

    [AvaloniaFact]
    public void ClosingTheLastTabShowsTheStartView()
    {
        using var demo = new Demo();
        var frame = demo.Window.Frame;
        Assert.IsNotType<StartView>(frame.Document);

        demo.CloseAllTabs();

        var start = Assert.IsType<StartView>(frame.Document);
        Assert.True(start.IsEffectivelyVisible);

        // The start view sits right under the title bar: the tab row and the band are gone, not blank.
        Assert.Equal(36, start.TranslatePoint(new Point(0, 0), demo.Window)!.Value.Y, 0.01);
        Assert.Null(frame.StatusBar);
        Assert.Null(frame.LeftPane);
        Assert.Null(frame.RightPane);
        Assert.False(demo.Window.NavigationPane.IsOpen);
        Assert.False(demo.Window.DocumentPane.IsOpen);
        Assert.Equal(4, demo.Tiles().Count);

        // The "+" of the title bar opens a document again: the frame is back.
        demo.Click(ControlHarness.Part<Button>(demo.Window.Tabs, "PART_New"));
        Assert.IsNotType<StartView>(frame.Document);
        Assert.NotNull(frame.StatusBar);
        Assert.Equal(30, frame.StatusBar!.Bounds.Height, 0.01);
        Assert.Single(demo.Window.Tabs.Items);
    }

    [AvaloniaFact]
    public void TheFileTabShowsTheStartViewAndAnotherTabLeavesIt()
    {
        using var demo = new Demo();
        var tabs = demo.Window.Ribbon.Definition!.Tabs;

        demo.Window.Ribbon.ActiveTab = tabs.Single(t => t.Letter == "F");
        demo.Settle();
        Assert.IsType<StartView>(demo.Window.Frame.Document);
        Assert.Null(demo.Window.Frame.StatusBar);
        Assert.Equal(3, demo.Window.Tabs.Items.Count);

        demo.Window.Ribbon.ActiveTab = tabs.Single(t => t.Name == "Insert");
        demo.Settle();
        Assert.IsNotType<StartView>(demo.Window.Frame.Document);
        Assert.NotNull(demo.Window.Frame.StatusBar);
    }

    [AvaloniaTheory]
    [InlineData("en", 1, "Requested: new Sheets document")]
    [InlineData("de", 1, "Angefordert: neues Sheets-Dokument")]
    [InlineData("en", 3, "Requested: open a PDF file")]
    public void StartTileRecordsItsRequest(string lang, int tile, string expected)
    {
        using var demo = new Demo(lang);
        demo.CloseAllTabs();

        demo.Click(demo.Tiles()[tile]);

        var bar = Assert.IsType<NotificationBar>(demo.Window.Frame.Notification);
        Assert.Equal(expected, bar.Message);
        Assert.True(bar.IsEffectivelyVisible);

        // Nothing was opened.
        Assert.Empty(demo.Window.Tabs.Items);
        Assert.IsType<StartView>(demo.Window.Frame.Document);
    }

    [AvaloniaFact]
    public void BrowseAndRecentRowsRecordTheirRequestToo()
    {
        using var demo = new Demo();
        demo.CloseAllTabs();

        demo.Click(ControlHarness.Part<Button>(demo.Window, "PART_Browse"));
        Assert.Equal("Requested: file dialog", Assert.IsType<NotificationBar>(demo.Window.Frame.Notification).Message);

        var row = demo.Window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("start-row"));
        demo.Click(row);
        Assert.Equal("Requested: open Quarterly report.docx", Assert.IsType<NotificationBar>(demo.Window.Frame.Notification).Message);
        Assert.Empty(demo.Window.Tabs.Items);
    }

    [AvaloniaFact]
    public void RecentFilesCarryTheLocalOffsetSoTheirDatesAreLocal()
    {
        var recent = FrameDemoWindow.CreateRecent();

        Assert.NotEmpty(recent);
        Assert.All(recent, file => Assert.Equal(DateTimeOffset.Now.Offset, file.Modified.Offset));
    }

    [AvaloniaFact]
    public void RunningACommandRaisesTheNotification()
    {
        using var demo = new Demo();
        var bold = demo.Window.Ribbon.Definition!.Tabs.Single(t => t.Name == "Home").Groups.SelectMany(g => g.Commands).Single(c => c.Id == "bold");

        bold.Run!();

        Assert.Equal("Ran: Bold", Assert.IsType<NotificationBar>(demo.Window.Frame.Notification).Message);
    }

    [AvaloniaFact]
    public void CtrlKFocusesTheSearchField()
    {
        using var demo = new Demo();
        demo.Window.InputTarget.Focus();
        demo.Settle();

        demo.Window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        demo.Settle();

        var focused = (Visual)demo.Window.FocusManager!.GetFocusedElement()!;
        Assert.True(demo.Window.TitleSearch.IsVisualAncestorOf(focused));
    }

    [AvaloniaFact]
    public void F6WalksTheThreePanesAndMarksThem()
    {
        using var demo = new Demo();
        var panes = new[] { demo.Window.NavigationPane, demo.Window.DocumentPane, demo.Window.CommentsPane };

        var visited = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            demo.Window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None);
            demo.Settle();
            visited.Add(Array.FindIndex(panes, p => p.IsPaneFocused));
        }

        Assert.Equal([0, 1, 2], visited);
        Assert.Equal("Focus: Comments", demo.Window.Status.FocusPane);
    }

    [AvaloniaFact]
    public void TheAltFieldCodesWorkAndThePanesCarryTheirCodes()
    {
        using var demo = new Demo();
        Assert.Equal("6", FieldCodeBadge.GetCode(demo.Window.NavigationPane));
        Assert.Equal("7", FieldCodeBadge.GetCode(demo.Window.CommentsPane));
        Assert.Equal("Q", FieldCodeBadge.GetCode(demo.Window.TitleSearch));

        demo.Window.KeyPressQwerty(PhysicalKey.AltLeft, RawInputModifiers.Alt);
        demo.Window.KeyReleaseQwerty(PhysicalKey.AltLeft, RawInputModifiers.None);
        demo.Settle();

        Assert.True(demo.Window.Ribbon.ShowFieldCodes);
        Assert.Equal(ShellStrings.FieldCodeHint, demo.Window.Status.Hint);
    }

    [AvaloniaFact]
    public void CompactStaysInSyncBetweenFrameAndRibbon()
    {
        using var demo = new Demo();

        demo.Window.SetCompact(true);
        demo.Settle();
        Assert.True(demo.Window.Frame.IsCompact);
        Assert.True(demo.Window.Ribbon.IsCompact);
        Assert.Equal(40, demo.Window.Frame.Band!.Bounds.Height, 0.01);

        demo.Window.SetCompact(false);
        demo.Settle();
        Assert.False(demo.Window.Frame.IsCompact);
        Assert.False(demo.Window.Ribbon.IsCompact);
        Assert.Equal(82, demo.Window.Frame.Band!.Bounds.Height, 0.01);
    }

    [AvaloniaFact]
    public void SettingsOkAppliesThemeNowAndStoresTheLanguageForTheNextStart()
    {
        using var demo = new Demo("en");
        var window = demo.Window;
        var app = Application.Current!;

        window.OpenSettings();
        demo.Settle();
        Assert.True(window.Host.IsOpen);
        ControlHarness.Part<SegmentedControl>(window, "PART_Theme").SelectedIndex = 1;
        ControlHarness.Part<SelectBox>(window, "PART_Language").SelectedIndex = 0;
        ControlHarness.Part<CheckBox>(window, "PART_Compact").IsChecked = true;
        ControlHarness.Part<CheckBox>(window, "PART_Autosave").IsChecked = false;
        demo.Settle();
        demo.Click(ControlHarness.Part<Button>(window, "PART_Ok"));

        // Theme and compact bar apply now; the language only waits in the file.
        Assert.False(window.Host.IsOpen);
        Assert.Equal(ThemeVariant.Dark, app.ActualThemeVariant);
        Assert.Equal(40, window.Frame.Band!.Bounds.Height, 0.01);
        Assert.True(window.Ribbon.IsCompact);
        Assert.Equal("en", ShellStrings.Culture.TwoLetterISOLanguageName);
        Assert.Equal("en", GalleryStrings.Culture.TwoLetterISOLanguageName);

        var stored = new SettingsStore(demo.SettingsPath).Load();
        Assert.Equal(new ShellSettings(ThemeMode.Dark, "de", true, false), stored);

        ShellTheme.Apply(app, ThemeMode.Light, AppInk.Write, forceHighContrast: false);
    }

    [AvaloniaFact]
    public void SettingsCancelChangesAndStoresNothing()
    {
        using var demo = new Demo("en");

        demo.Window.OpenSettings();
        demo.Settle();
        ControlHarness.Part<CheckBox>(demo.Window, "PART_Compact").IsChecked = true;
        demo.Click(ControlHarness.Part<Button>(demo.Window, "PART_Cancel"));

        Assert.False(demo.Window.Host.IsOpen);
        Assert.Equal(82, demo.Window.Frame.Band!.Bounds.Height, 0.01);
        Assert.False(File.Exists(demo.SettingsPath));
    }

    [AvaloniaFact]
    public void NoUserSettingsAreReadWithSettingsFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "raffica-explicit-" + Guid.NewGuid().ToString("N") + ".json");

        Assert.Equal(path, FrameDemoSettings.StorePath(GalleryOptions.Parse(["--view", "frame", "--settings-file", path])));
        Assert.Equal(path, FrameDemoSettings.StorePath(GalleryOptions.Parse(["--view", "frame", "--settings-file", path, "--measure-startup"])));

        // A measurement run without a settings file touches no file at all.
        var measured = FrameDemoSettings.Open(GalleryOptions.Parse(["--view", "frame", "--measure-startup"]));
        Assert.Null(measured.Store);
        Assert.Equal(new ShellSettings(), measured.Settings);

        // The profile of the user is only the location of an ordinary run.
        Assert.Equal(SettingsStore.DefaultPath("gallery"), FrameDemoSettings.StorePath(GalleryOptions.Parse(["--view", "frame"])));

        // An explicit file is read and nothing else.
        new SettingsStore(path).Save(new ShellSettings(Language: "de"));
        try
        {
            Assert.Equal("de", FrameDemoSettings.Open(GalleryOptions.Parse(["--view", "frame", "--settings-file", path])).Settings.Language);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void StartUsesTheStoredLanguageUnlessLangIsGiven()
    {
        var german = new ShellSettings(Language: "de");
        var english = new ShellSettings(Language: "en");
        var none = new ShellSettings();
        var system = Languages.Resolve(null, CultureInfo.CurrentUICulture).TwoLetterISOLanguageName;

        // --lang wins over the stored setting, the stored setting over the operating system.
        Assert.Equal("en", FrameDemoSettings.ResolveLanguage(GalleryOptions.Parse(["--view", "frame", "--lang", "en"]), german));
        Assert.Equal("de", FrameDemoSettings.ResolveLanguage(GalleryOptions.Parse(["--view", "frame", "--lang", "de"]), english));
        Assert.Equal("de", FrameDemoSettings.ResolveLanguage(GalleryOptions.Parse(["--view", "frame"]), german));
        Assert.Equal("en", FrameDemoSettings.ResolveLanguage(GalleryOptions.Parse(["--view", "frame"]), english));
        Assert.Equal(system, FrameDemoSettings.ResolveLanguage(GalleryOptions.Parse(["--view", "frame"]), none));
    }

    [AvaloniaFact]
    public void StartUsesTheStoredThemeUnlessThemeIsGiven()
    {
        var dark = new ShellSettings(Theme: ThemeMode.Dark);

        Assert.Equal(ThemeMode.Dark, FrameDemoSettings.ResolveTheme(GalleryOptions.Parse(["--view", "frame"]), dark));
        Assert.Equal(ThemeMode.Light, FrameDemoSettings.ResolveTheme(GalleryOptions.Parse(["--view", "frame", "--theme", "light"]), dark));
    }

    [AvaloniaFact]
    public void EveryDemoCommandHasANameAndAUniqueCodePerTab()
    {
        foreach (var lang in new[] { "en", "de" })
        {
            using var demo = new Demo(lang);
            var ribbon = demo.Window.Ribbon.Definition!;

            ribbon.Validate();
            foreach (var tab in ribbon.Tabs)
            {
                foreach (var command in tab.Groups.SelectMany(g => g.Commands).SelectMany(Flatten))
                {
                    Assert.False(string.IsNullOrWhiteSpace(command.Name), $"{lang}/{tab.Name}/{command.Id}: no name");
                    Assert.StartsWith("Raffica.Icon.", command.IconKey);
                    Assert.True(Application.Current!.TryGetResource(command.IconKey, null, out _), $"{command.IconKey}: no such icon");
                }
            }
        }
    }

    [AvaloniaFact]
    public void TheGermanRibbonHasRealGermanNamesAsLongAsTheSpecShows()
    {
        using var demo = new Demo("de");
        var ribbon = demo.Window.Ribbon.Definition!;
        var names = ribbon.Tabs.SelectMany(t => t.Groups.SelectMany(g => g.Commands)).SelectMany(Flatten).Select(c => c.Name).ToList();

        Assert.Equal(["Datei", "Start", "Einfügen", "Überprüfen", "Ansicht"], ribbon.Tabs.Select(t => t.Name));
        Assert.Contains("Endgültig anwenden", names);
        Assert.Contains("Kompakte Befehlsleiste", names);
        Assert.True(names.Max(n => n.Length) >= "Kompakte Befehlsleiste".Length);

        // The same ribbon in English differs in nearly every name: nothing was left untranslated.
        using var english = new Demo("en");
        var englishNames = english.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups.SelectMany(g => g.Commands)).SelectMany(Flatten).Select(c => c.Name).ToList();
        Assert.Equal(englishNames.Count, names.Count);
        Assert.True(names.Zip(englishNames).Count(p => p.First == p.Second) <= 6);
    }

    [AvaloniaFact]
    public void TheRibbonHasMenuCommandsCompactPlacementsAndADestructiveGroup()
    {
        using var demo = new Demo();
        var tabs = demo.Window.Ribbon.Definition!.Tabs;
        var commands = tabs.SelectMany(t => t.Groups.SelectMany(g => g.Commands)).ToList();

        Assert.Contains(commands, c => c.Items is { Count: > 0 });
        Assert.Contains(commands, c => c.Compact == CompactPlacement.Inline);
        Assert.Contains(commands, c => c.Compact == CompactPlacement.Overflow);
        Assert.Contains(tabs.SelectMany(t => t.Groups), g => g.Destructive);
    }

    [AvaloniaFact]
    public void AutomationNamesExistOnEveryControlOfTheFrame()
    {
        using var demo = new Demo();
        Assert.Empty(AutomationNameTests.Unnamed(demo.Window));

        demo.Window.OpenSettings();
        demo.Settle();
        Assert.Empty(AutomationNameTests.Unnamed(demo.Window));
        demo.Window.Host.Close();

        demo.CloseAllTabs();
        Assert.IsType<StartView>(demo.Window.Frame.Document);
        Assert.Empty(AutomationNameTests.Unnamed(demo.Window));
    }

    [AvaloniaFact]
    public void TheInputTargetIsTheTextBoxOfTheDocumentPage()
    {
        using var demo = new Demo();

        Assert.Equal("InputTarget", demo.Window.InputTarget.Name);
        Assert.True(demo.Window.DocumentPane.IsVisualAncestorOf(demo.Window.InputTarget));
        Assert.True(demo.Window.InputTarget.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void TheDocumentParagraphIsSetInCarlito()
    {
        using var demo = new Demo();

        var blocks = demo.Window.DocumentPane.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text?.StartsWith("The third quarter", StringComparison.Ordinal) == true).ToList();
        Assert.Single(blocks);
        Assert.Equal("Carlito", blocks[0].FontFamily.Name);
    }

    private static IEnumerable<CommandDefinition> Flatten(CommandDefinition command) =>
        command.Items is null ? [command] : [command, .. command.Items.SelectMany(Flatten)];
}
