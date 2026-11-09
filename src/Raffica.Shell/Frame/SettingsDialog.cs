using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Shell.Frame;

/// <summary>
/// The settings dialog (section 6 of the design spec): theme, language, compact command bar and autosave, and nothing else. It
/// changes nothing live: OK hands the new <see cref="ShellSettings"/> to the app, which stores them; a language choice applies
/// after a restart and the dialog says so whenever the choice differs from the language that is running. Show it in a
/// <see cref="DialogHost"/>; the dialog never closes itself, the app closes the host in both callbacks.
/// </summary>
public static class SettingsDialog
{
    private static readonly ThemeMode[] Themes = [ThemeMode.Light, ThemeMode.Dark, ThemeMode.System];
    private static readonly string[] LanguageCodes = ["de", "en"];

    /// <param name="current">The stored settings the controls start from.</param>
    /// <param name="onOk">Called with the new settings when the user confirms.</param>
    /// <param name="onCancel">Called by Cancel, the close button and Esc.</param>
    /// <param name="options">Optional app adjustments (hidden autosave row, extra rows); <see langword="null"/> keeps the default dialog.</param>
    public static Dialog Create(ShellSettings current, Action<ShellSettings> onOk, Action onCancel, SettingsDialogOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(onOk);
        ArgumentNullException.ThrowIfNull(onCancel);

        var running = ShellStrings.Culture.TwoLetterISOLanguageName == "de" ? "de" : "en";

        var theme = new SegmentedControl
        {
            Name = "PART_Theme",
            ItemsSource = new[] { ShellStrings.SettingsThemeLight, ShellStrings.SettingsThemeDark, ShellStrings.SettingsThemeSystem },
            SelectedIndex = Math.Max(0, Array.IndexOf(Themes, current.Theme)),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(theme, ShellStrings.SettingsTheme);

        var language = new SelectBox
        {
            Name = "PART_Language",
            ItemsSource = new[] { "Deutsch", "English" },
            SelectedIndex = Math.Max(0, Array.IndexOf(LanguageCodes, current.Language ?? running)),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 160,
        };
        AutomationProperties.SetName(language, ShellStrings.SettingsLanguage);

        var note = new TextBlock { Name = "PART_RestartNote", Text = ShellStrings.SettingsRestartNote };
        note.Classes.Add("settings-note");
        void UpdateNote() => note.IsVisible = LanguageCodes[Math.Max(0, language.SelectedIndex)] != running;
        UpdateNote();
        language.SelectionChanged += (_, _) => UpdateNote();

        var compact = new CheckBox { Name = "PART_Compact", Content = ShellStrings.SettingsCompact, IsChecked = current.CompactCommandBar };
        var showAutosave = options?.ShowAutosave ?? true;
        var autosave = new CheckBox { Name = "PART_Autosave", Content = ShellStrings.SettingsAutosave, IsChecked = current.Autosave };

        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(Field(ShellStrings.SettingsTheme, theme));
        body.Children.Add(Field(ShellStrings.SettingsLanguage, language, note));
        var checks = new StackPanel { Spacing = 8, Children = { compact } };
        if (showAutosave)
        {
            checks.Children.Add(autosave);
        }

        body.Children.Add(checks);
        foreach (var row in options?.ExtraRows ?? [])
        {
            body.Children.Add(Field(row.Label, row.Editor));
        }

        var cancel = new Button { Name = "PART_Cancel", Content = ShellStrings.Cancel };
        cancel.Classes.Add("boxed");
        var ok = new Button { Name = "PART_Ok", Content = ShellStrings.Ok };
        ok.Classes.Add("primary");

        var dialog = new Dialog
        {
            Title = ShellStrings.SettingsTitle,
            Content = body,
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, ok } },
        };
        dialog.CancelRequested += (_, _) => onCancel();
        cancel.Click += (_, _) => onCancel();
        ok.Click += (_, _) =>
        {
            var selected = LanguageCodes[Math.Max(0, language.SelectedIndex)];
            onOk(current with
            {
                Theme = Themes[Math.Max(0, theme.SelectedIndex)],

                // An untouched first-start choice stays "follow the system" instead of being pinned.
                Language = current.Language is null && selected == running ? null : selected,
                CompactCommandBar = compact.IsChecked == true,
                Autosave = showAutosave ? autosave.IsChecked == true : current.Autosave,
            });
        };
        return dialog;
    }

    private static StackPanel Field(string label, params Control[] controls)
    {
        var text = new TextBlock { Text = label };
        text.Classes.Add("secondary");
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(text);
        foreach (var control in controls)
        {
            panel.Children.Add(control);
        }

        return panel;
    }
}
