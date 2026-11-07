using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Settings;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell;
using Xunit;

namespace Raffica.Pdf.Tests.Updating;

public sealed class SettingsUpdateRowTests
{
    private static void RestoreTheme() => ShellTheme.Apply(Application.Current!, ThemeMode.System, AppInk.Pdf, null);

    private static T Find<T>(WindowFixture f, string name) where T : Control =>
        f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static async Task SetAsync(WindowFixture f, bool on)
    {
        f.Window.Settings.Open();
        ViewFixture.Pump();
        Find<CheckBox>(f, "PART_UpdateCheck").IsChecked = on;
        Find<Button>(f, "PART_Ok").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
        await f.Window.Settings.FlushAsync();
    }

    [AvaloniaFact]
    public async Task TheRowWritesUpdateCheckAndTheDialogStillHidesAutosave()
    {
        try
        {
            using var f = WindowFixture.Create();
            f.Window.Settings.Open();
            ViewFixture.Pump();

            // The row is there with the setting in force (on by default), labelled in words, and the autosave switch stays hidden.
            var box = Find<CheckBox>(f, "PART_UpdateCheck");
            Assert.True(box.IsChecked);
            Assert.Equal(PdfStrings.SettingsUpdateCheck, box.Content);
            Assert.Equal("Check for updates automatically", box.Content);
            Assert.Contains(PdfStrings.SettingsUpdates, f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.DoesNotContain("PART_Autosave", f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<Control>().Select(c => c.Name));
            Find<Button>(f, "PART_Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ViewFixture.Pump();

            // Off and OK: in force at once and written to pdf-settings.json; the shell file's autosave stays what it was.
            await SetAsync(f, on: false);
            Assert.False(f.Window.Settings.App.UpdateCheck);
            Assert.False(new SettingsStore(f.AppSettingsPath).LoadReadOnly().UpdateCheck);

            // The dialog shows what is in force, and on again is written back.
            f.Window.Settings.Open();
            ViewFixture.Pump();
            Assert.False(Find<CheckBox>(f, "PART_UpdateCheck").IsChecked);
            Find<Button>(f, "PART_Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ViewFixture.Pump();
            await SetAsync(f, on: true);
            Assert.True(new SettingsStore(f.AppSettingsPath).LoadReadOnly().UpdateCheck);
        }
        finally
        {
            RestoreTheme();
        }
    }

    [AvaloniaFact]
    public async Task TurningTheSwitchOffStopsLaterChecksInTheRunningApp()
    {
        try
        {
            using var kit = new UpdateKit();
            kit.Release("0.2.0");
            using var f = WindowFixture.Create();
            Raffica.Pdf.Updating.UpdateFlow? made = null;

            // The factory gets the question "is the background check on" and answers it from the setting in force, as the app's factory does.
            f.Window.UpdateFlows = enabled => made = kit.Flow(enabled);

            // On: a check that is due runs (the first one).
            await f.Window.CheckForUpdateAtStartAsync();
            Assert.Equal(1, kit.Source.FetchCalls);
            Assert.NotNull(made);

            // Another day: due again, and with the switch still on it would run.
            kit.Clock.Advance(TimeSpan.FromHours(25));
            await made.CheckOnStartAsync(CancellationToken.None);
            Assert.Equal(2, kit.Source.FetchCalls);
            kit.Clock.Advance(TimeSpan.FromHours(25));

            // Switched off in the settings dialog while the app runs: the flow that exists asks the setting in force and checks nothing.
            await SetAsync(f, on: false);
            await made.CheckOnStartAsync(CancellationToken.None);
            Assert.Equal(2, kit.Source.FetchCalls);

            // And a new window-level start check does nothing either (it also never makes a flow).
            await f.Window.CheckForUpdateAtStartAsync();
            Assert.Equal(2, kit.Source.FetchCalls);

            // The command is the user asking: it still works with the switch off (it shows the offer that is known).
            var asked = f.Window.CheckForUpdatesAsync();
            await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen);
            Find<Button>(f, "PART_Later").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await asked;
        }
        finally
        {
            RestoreTheme();
        }
    }
}
