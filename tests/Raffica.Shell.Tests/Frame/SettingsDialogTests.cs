using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class SettingsDialogTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly CultureInfo _previous = ShellStrings.Culture;

        public Fixture(ShellSettings current, string running = "en", SettingsDialogOptions? options = null, int width = 800, int height = 600)
        {
            ShellStrings.Culture = CultureInfo.GetCultureInfo(running);
            Opener = new Button { Content = "Settings" };
            Host = new DialogHost { Content = new StackPanel { Margin = new Thickness(20), Children = { Opener } } };
            Dialog = SettingsDialog.Create(current, ok => { Saved.Add(ok); Host.Close(); }, () => { Cancels++; Host.Close(); }, options);
            Window = ControlHarness.Show(Host, ThemeMode.Light);
            Window.Width = width;
            Window.Height = height;
            ControlHarness.Settle(Window);
            Opener.Focus(NavigationMethod.Tab);
            ControlHarness.Settle(Window);
            Host.Show(Dialog);
            ControlHarness.Settle(Window);
        }

        public Window Window { get; }

        public DialogHost Host { get; }

        public Dialog Dialog { get; }

        public Button Opener { get; }

        public List<ShellSettings> Saved { get; } = [];

        public int Cancels { get; private set; }

        public T Part<T>(string name)
            where T : Control => ControlHarness.Part<T>(Dialog, name);

        public void Click(string buttonName)
        {
            var button = Part<Button>(buttonName);
            var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), Window)!.Value;
            Window.MouseDown(at, MouseButton.Left);
            Window.MouseUp(at, MouseButton.Left);
            ControlHarness.Settle(Window);
        }

        public void Dispose() => ShellStrings.Culture = _previous;
    }

    [AvaloniaFact]
    public void ShowsThemeLanguageCompactAndAutosave()
    {
        using var f = new Fixture(new ShellSettings(ThemeMode.Dark, "en", CompactCommandBar: true, Autosave: false));

        Assert.Equal("Settings", f.Dialog.Title);
        Assert.Equal(520, f.Dialog.Bounds.Width, 0.01);
        var theme = f.Part<SegmentedControl>("PART_Theme");
        Assert.Equal(["Light", "Dark", "Same as system"], theme.Items.Cast<string>());
        Assert.Equal(1, theme.SelectedIndex);
        var language = f.Part<SelectBox>("PART_Language");
        Assert.Equal(["Deutsch", "English"], language.Items.Cast<string>());
        Assert.Equal(1, language.SelectedIndex);
        Assert.True(f.Part<CheckBox>("PART_Compact").IsChecked);
        Assert.False(f.Part<CheckBox>("PART_Autosave").IsChecked);
        Assert.Equal("Cancel", f.Part<Button>("PART_Cancel").Content);
        Assert.Equal("OK", f.Part<Button>("PART_Ok").Content);
        Assert.False(f.Part<TextBlock>("PART_RestartNote").IsVisible);
        Assert.False(ControlHarness.HasShadow(f.Dialog));
    }

    [AvaloniaFact]
    public void OkPassesTheNewRecord()
    {
        using var f = new Fixture(new ShellSettings(ThemeMode.Light, "en", CompactCommandBar: false, Autosave: true));

        f.Part<SegmentedControl>("PART_Theme").SelectedIndex = 2;
        f.Part<CheckBox>("PART_Compact").IsChecked = true;
        f.Part<CheckBox>("PART_Autosave").IsChecked = false;
        ControlHarness.Settle(f.Window);
        f.Click("PART_Ok");

        Assert.Equal([new ShellSettings(ThemeMode.System, "en", CompactCommandBar: true, Autosave: false)], f.Saved);
        Assert.Equal(0, f.Cancels);
        Assert.False(f.Host.IsOpen);
    }

    [AvaloniaFact]
    public void CancelAndEscKeepTheOldSettings()
    {
        using var f = new Fixture(new ShellSettings());
        f.Part<SegmentedControl>("PART_Theme").SelectedIndex = 1;
        f.Part<CheckBox>("PART_Autosave").IsChecked = false;
        ControlHarness.Settle(f.Window);

        f.Click("PART_Cancel");
        Assert.Equal(1, f.Cancels);
        Assert.Empty(f.Saved);

        // Esc raises the same cancel.
        f.Host.Show(f.Dialog);
        ControlHarness.Settle(f.Window);
        f.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        f.Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        ControlHarness.Settle(f.Window);
        Assert.Equal(2, f.Cancels);
        Assert.Empty(f.Saved);
    }

    [AvaloniaFact]
    public void LanguageChangeIsOnlyStoredNotApplied()
    {
        using var f = new Fixture(new ShellSettings(), running: "en");
        var note = f.Part<TextBlock>("PART_RestartNote");
        Assert.False(note.IsVisible);
        Assert.Equal("Takes effect after a restart", note.Text);

        // Choosing the running language again shows no note; another language does, and it goes away when the choice is undone.
        var language = f.Part<SelectBox>("PART_Language");
        language.SelectedIndex = 0;
        ControlHarness.Settle(f.Window);
        Assert.True(note.IsVisible);
        language.SelectedIndex = 1;
        Assert.False(note.IsVisible);
        language.SelectedIndex = 0;
        ControlHarness.Settle(f.Window);
        Assert.True(note.IsVisible);

        var running = ShellStrings.Culture;
        var title = f.Dialog.Title;
        f.Click("PART_Ok");

        // The choice is handed to the app (which stores it); nothing changed live.
        Assert.Equal("de", Assert.Single(f.Saved).Language);
        Assert.Same(running, ShellStrings.Culture);
        Assert.Equal("en", ShellStrings.Culture.TwoLetterISOLanguageName);
        Assert.Equal("Settings", title);
        Assert.Equal("Settings", ShellStrings.SettingsTitle);
    }

    [AvaloniaFact]
    public void TheNoteTracksTheRunningLanguageNotTheStoredOne()
    {
        // Stored English but German is running (the file changed behind the app): choosing English differs from what runs.
        using var f = new Fixture(new ShellSettings(Language: "en"), running: "de");
        Assert.True(f.Part<TextBlock>("PART_RestartNote").IsVisible);
        Assert.Equal("Wird nach einem Neustart wirksam", f.Part<TextBlock>("PART_RestartNote").Text);
    }

    [AvaloniaFact]
    public void AnUntouchedFirstStartLanguageStaysUnset()
    {
        using var f = new Fixture(new ShellSettings(), running: "de");
        Assert.Equal(0, f.Part<SelectBox>("PART_Language").SelectedIndex);

        f.Click("PART_Ok");

        Assert.Null(Assert.Single(f.Saved).Language);
    }

    [AvaloniaFact]
    public void DefaultOptionsKeepTheDialogAsItWas()
    {
        using var f = new Fixture(new ShellSettings(), options: new SettingsDialogOptions());

        Assert.NotNull(f.Part<CheckBox>("PART_Autosave"));
        var body = (StackPanel)f.Dialog.Content!;
        Assert.Equal(3, body.Children.Count);
    }

    [AvaloniaFact]
    public void AutosaveRowCanBeHidden()
    {
        using var f = new Fixture(new ShellSettings(), options: new SettingsDialogOptions(ShowAutosave: false));

        Assert.DoesNotContain(f.Dialog.GetVisualDescendants().OfType<CheckBox>(), c => c.Name == "PART_Autosave");
        Assert.NotNull(f.Part<CheckBox>("PART_Compact"));
    }

    [AvaloniaFact]
    public void HiddenAutosaveKeepsTheStoredValueOnOk()
    {
        using var f = new Fixture(new ShellSettings(Autosave: false), options: new SettingsDialogOptions(ShowAutosave: false));

        f.Click("PART_Ok");

        Assert.False(Assert.Single(f.Saved).Autosave);
    }

    [AvaloniaFact]
    public void ExtraRowsAppearAfterTheCompactRowInOrder()
    {
        var first = new TextBox { Name = "PART_ExtraOne" };
        var second = new CheckBox { Name = "PART_ExtraTwo" };
        using var f = new Fixture(
            new ShellSettings(),
            options: new SettingsDialogOptions(ExtraRows: [new SettingsExtraRow("First extra", first), new SettingsExtraRow("Second extra", second)]));

        var body = (StackPanel)f.Dialog.Content!;
        Assert.Equal(5, body.Children.Count);
        var one = Assert.IsType<StackPanel>(body.Children[3]);
        var two = Assert.IsType<StackPanel>(body.Children[4]);
        var label = Assert.IsType<TextBlock>(one.Children[0]);
        Assert.Equal("First extra", label.Text);
        Assert.Contains("secondary", label.Classes);
        Assert.Same(first, one.Children[1]);
        Assert.Equal("Second extra", Assert.IsType<TextBlock>(two.Children[0]).Text);
        Assert.Same(second, two.Children[1]);
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public void InASmallHostTheBodyScrollsAndTabReachesTheLastRow(string lang)
    {
        // The shape of the PDF app's dialog: the shared rows plus five extra rows, the last one "Updates" (M2.7 Task 11 step 2).
        var rows = new List<SettingsExtraRow>();
        foreach (var name in new[] { "Page appearance", "Default layout", "Default zoom", "Author name", "Default app" })
        {
            rows.Add(new SettingsExtraRow(name, new CheckBox { Content = name }));
        }

        var updates = new CheckBox { Name = "PART_UpdateCheck", Content = "Check for updates once a day" };
        rows.Add(new SettingsExtraRow("Updates", updates));
        using var f = new Fixture(new ShellSettings(), lang, new SettingsDialogOptions(ShowAutosave: false, ExtraRows: rows), width: 1024, height: 480);

        var scroll = f.Dialog.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "PART_BodyScroll");
        Assert.True(f.Dialog.Bounds.Height <= 480 - 48 + 0.5, $"the card is {f.Dialog.Bounds.Height} DIP high in a 480 DIP host");
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "the body is taller than its viewport, so it must scroll");

        for (var i = 0; i < 30 && !ReferenceEquals(f.Window.FocusManager!.GetFocusedElement(), updates); i++)
        {
            f.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            f.Window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            ControlHarness.Settle(f.Window);
        }

        Assert.Same(updates, f.Window.FocusManager!.GetFocusedElement());
        var row = updates.TranslatePoint(default, scroll)!.Value;
        Assert.True(row.Y >= -0.5 && row.Y + updates.Bounds.Height <= scroll.Viewport.Height + 0.5, $"the Updates row sits at {row.Y}..{row.Y + updates.Bounds.Height} of a {scroll.Viewport.Height} DIP viewport");
        Assert.True(scroll.Offset.Y > 0, "the body scrolled");

        // The footer stays in view: the buttons are inside the card, which is inside the host.
        var ok = f.Part<Button>("PART_Ok");
        var at = ok.TranslatePoint(default, f.Window)!.Value;
        Assert.True(at.Y + ok.Bounds.Height <= 480 + 0.5, "OK stays in the window");
    }

    [AvaloniaFact]
    public void ExtraEditorsKeepTheirStateWhenOkIsPressed()
    {
        var box = new TextBox { Name = "PART_ExtraBox" };
        using var f = new Fixture(
            new ShellSettings(),
            options: new SettingsDialogOptions(ExtraRows: [new SettingsExtraRow("Extra", box)]));

        box.Text = "typed";
        f.Click("PART_Ok");

        Assert.Single(f.Saved);
        Assert.Equal("typed", box.Text);
    }

    [AvaloniaFact]
    public void PrimaryButtonIsTheDarkNeutralFillNotInk()
    {
        using var f = new Fixture(new ShellSettings());
        var ok = f.Part<Button>("PART_Ok");
        var presenter = ControlHarness.Presenter(ok);

        ControlHarness.AssertToken("Raffica.Brush.Button.Primary.Fill", presenter.Background, "OK fill");
        Assert.NotEqual(ControlHarness.Token("Raffica.Brush.Ink"), ControlHarness.Strict(presenter.Background));
        Assert.Contains("primary", ok.Classes);
        Assert.DoesNotContain("primary", f.Part<Button>("PART_Cancel").Classes);
    }

    [AvaloniaFact]
    public void RestoresFocusToTheOpener()
    {
        using var f = new Fixture(new ShellSettings());
        Assert.True(f.Dialog.IsVisualAncestorOf((Visual)f.Window.FocusManager!.GetFocusedElement()!));

        f.Click("PART_Cancel");

        Assert.Same(f.Opener, f.Window.FocusManager!.GetFocusedElement());
    }
}
