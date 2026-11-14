using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Core.Saving;
using Raffica.Hosting;
using Raffica.Shell.Frame;
using Raffica.Write.Documents;
using Xunit;

namespace Raffica.WriteApp.Tests;

public sealed class WriteWindowTests
{
    private static Button[] ButtonsOf(Avalonia.Visual root, string cls) =>
        [.. root.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains(cls))];

    [AvaloniaFact]
    public async Task StartupCreatesNoLayoutOrFontWork()
    {
        using var fixture = new WindowFixture();
        var work = new OpenWork();
        var window = fixture.Show(work);

        // The window has been shown and has gone idle (the recent list loads then): still nothing of the open path exists.
        await window.LoadRecentAsync();
        WindowFixture.Pump();
        Assert.Same(window.Start, window.Frame.Document);
        Assert.True(work.NothingStarted);
        Assert.False(work.FontsCreated);
        Assert.Equal(0, work.FilesOpened);
        Assert.Equal(0, work.LayoutsCreated);

        // The first file starts all of it, once; a second file makes no second catalog.
        var result = await window.Flow.OpenAsync(WindowFixture.Poi);
        Assert.IsType<Opened>(result);
        Assert.True(work.FontsCreated);
        Assert.Equal(1, work.FilesOpened);
        Assert.Equal(1, work.LayoutsCreated);
        var catalog = work.Fonts();
        Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis));
        Assert.Same(catalog, work.Fonts());
        Assert.Equal(2, work.FilesOpened);
    }

    [AvaloniaFact]
    public void TheStartViewHasOneTileOpenDocument()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var start = Assert.IsType<StartView>(window.Frame.Document);
        Assert.Equal(StartApp.Write, start.OwnApp);
        Assert.Null(window.Frame.StatusBar);
        var tile = Assert.Single(ButtonsOf(window.Start, "start-tile"));
        Assert.Equal("Open document", Avalonia.Automation.AutomationProperties.GetName(tile));
    }

    [AvaloniaFact]
    public async Task TheTileAndTheBrowseButtonOpenThePickerOnce()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var calls = 0;
        var gate = new TaskCompletionSource<IReadOnlyList<string>>();
        window.FilePicker = () =>
        {
            calls++;
            return gate.Task;
        };

        ButtonsOf(window.Start, "start-tile")[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        WindowFixture.Pump();
        Assert.Equal(1, calls);

        // A second request while the picker is showing does nothing.
        ButtonsOf(window.Start, "start-tile")[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        WindowFixture.Pump();
        Assert.Equal(1, calls);

        gate.SetResult([WindowFixture.Poi]);
        await WindowFixture.WaitAsync(() => window.Workspace.Tabs.Count == 1);
    }

    [AvaloniaFact]
    public async Task AnOpenedFileIsATabWithItsNameAndAPageView()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();

        var result = await window.Flow.OpenAsync(WindowFixture.Poi);
        WindowFixture.Pump();

        var tab = Assert.IsType<Opened>(result).Tab;
        Assert.Same(tab, window.Workspace.Active);
        Assert.Same(tab.Tab, window.Tabs.Selected);
        Assert.Equal("poi-test-document.docx", tab.Title);
        Assert.Equal("poi-test-document.docx", Assert.Single(window.Tabs.Items).Title);
        Assert.Same(window.DocumentPane, window.Frame.Document);
        Assert.Same(window.Status, window.Frame.StatusBar);
        var view = Assert.IsType<Raffica.Write.Viewing.PageView>(window.DocumentPane.Content);
        Assert.Same(tab, view.Tab);
        Assert.StartsWith("Document: poi-test-document.docx", Avalonia.Automation.AutomationProperties.GetName(view), StringComparison.Ordinal);
        Assert.True(tab.Layout.LaidOutPages >= 1);
        Assert.False(tab.Token.IsCancellationRequested);

        // Closing the only tab goes back to the start view and lets go of the tab.
        window.Workspace.Close(tab);
        WindowFixture.Pump();
        Assert.Same(window.Start, window.Frame.Document);
        Assert.Null(window.Frame.StatusBar);
        Assert.Empty(window.Tabs.Items);
        Assert.True(tab.IsDisposed);
        Assert.True(tab.Token.IsCancellationRequested);
    }

    [AvaloniaFact]
    public async Task ClosingTheWindowCancelsAndDisposesEveryTab()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var first = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Poi)).Tab;
        var second = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;

        window.Close();
        WindowFixture.Pump();

        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
        Assert.True(first.Token.IsCancellationRequested);
        Assert.True(second.Token.IsCancellationRequested);
        Assert.False(window.QuitRequested);
        Assert.True(window.IsClosing);
    }

    [AvaloniaFact]
    public async Task OpenSameFileTwiceFocusesTheTab()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var path = fixture.Copy(WindowFixture.Poi, "one.docx");
        Directory.CreateDirectory(Path.Combine(fixture.Folder, "sub"));

        var first = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;

        // The same file, spelled another way: a dot-dot segment, and (where case does not matter) another case.
        var other = Path.Combine(fixture.Folder, "sub", "..", "one.docx");
        Assert.Same(first, Assert.IsType<Opened>(await window.Flow.OpenAsync(other)).Tab);
        if (PathRules.IsCaseInsensitive)
        {
            Assert.Same(first, Assert.IsType<Opened>(await window.Flow.OpenAsync(path.ToUpperInvariant())).Tab);
        }

        Assert.Single(window.Workspace.Tabs);

        // Another file is a second tab; the first file again selects the first tab and makes no third.
        var second = Assert.IsType<Opened>(await window.Flow.OpenAsync(fixture.Copy(WindowFixture.Poi, "two.docx"))).Tab;
        Assert.Same(second, window.Workspace.Active);
        Assert.Same(first, Assert.IsType<Opened>(await window.Flow.OpenAsync(other)).Tab);
        Assert.Same(first, window.Workspace.Active);
        Assert.Equal(2, window.Workspace.Tabs.Count);
        Assert.Equal(2, window.Tabs.Items.Count);
    }

    [AvaloniaFact]
    public async Task TheSameFileDroppedTwiceInOneCallBecomesOneTab()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var path = fixture.Copy(WindowFixture.Poi, "one.docx");

        await window.OpenFilesAsync([path, path]);

        Assert.Single(window.Workspace.Tabs);
        Assert.False(window.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task TheFilesOfTheCommandLineOpenWhenTheWindowOpens()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show(null, WindowFixture.Poi);

        await WindowFixture.WaitAsync(() => window.Workspace.Tabs.Count == 1);
        Assert.Equal("poi-test-document.docx", window.Workspace.Active!.Title);
        await window.LoadRecentAsync();
        await WindowFixture.WaitAsync(() => window.Start.Recent.Count == 1);
    }

    [AvaloniaFact]
    public async Task StartViewShowsRecentFiles()
    {
        using var fixture = new WindowFixture();
        fixture.SeedRecent(fixture.Copy(WindowFixture.Poi, "older.docx"), fixture.Copy(WindowFixture.Poi, "newer.docx"));

        var window = fixture.Show();
        await WindowFixture.WaitAsync(() => window.Start.Recent.Count == 2);

        // The newest first, name and folder.
        Assert.Equal(["newer.docx", "older.docx"], window.Start.Recent.Select(r => r.Name).ToArray());
        Assert.All(window.Start.Recent, r => Assert.Equal(fixture.Folder, r.Folder));
        var rows = ButtonsOf(window.Start, "start-row");
        Assert.Equal(2, rows.Length);

        // A click opens the file.
        rows[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await WindowFixture.WaitAsync(() => window.Workspace.Tabs.Count == 1);
        Assert.Equal("newer.docx", window.Workspace.Active!.Title);
    }

    [AvaloniaFact]
    public async Task HandoverOpensFilesInTheRunningWindow()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var path = fixture.Copy(WindowFixture.Poi, "one.docx");
        var missing = Path.Combine(fixture.Folder, "missing.docx");
        var arrived = 0;
        window.HandoverArrived += () => arrived++;

        // As the pipe calls it: on a pool thread, then the UI thread opens the file and shows the card for the odd path.
        await Task.Run(() => window.OnHandover([path, missing, fixture.Folder]), TestContext.Current.CancellationToken);

        await WindowFixture.WaitAsync(() => window.Workspace.Tabs.Count == 1);
        Assert.Equal(path, window.Workspace.Tabs[0].Path);
        Assert.Equal(1, arrived);
        Assert.True(window.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task AHandedOverFileWaitsBehindACardAndOpensWhenItIsGone()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        await window.OpenFilesAsync([Path.Combine(fixture.Folder, "missing.docx")]);
        Assert.True(window.Host.IsOpen);
        var path = fixture.Copy(WindowFixture.Poi, "one.docx");

        await Task.Run(() => window.OnHandover([path]), TestContext.Current.CancellationToken);
        await WindowFixture.WaitAsync(() => window.PendingHandover.Count == 1);
        Assert.Empty(window.Workspace.Tabs);

        window.Host.Close();
        await WindowFixture.WaitAsync(() => window.Workspace.Tabs.Count == 1);
        Assert.Empty(window.PendingHandover);
    }

    [AvaloniaFact]
    public async Task FilesThatArriveOnceTheCloseIsConfirmedAreHandedBack()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var path = fixture.Copy(WindowFixture.Poi, "one.docx");
        window.Close();
        WindowFixture.Pump();

        await Task.Run(() => window.OnHandover([path]), TestContext.Current.CancellationToken);

        Assert.Equal([path], window.TakeUndelivered());
        Assert.Empty(window.TakeUndelivered());
    }

    [AvaloniaFact]
    public void SearchShortcutIsAltQ()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var input = window.TitleSearch.GetVisualDescendants().OfType<TextBox>().Single();
        window.Start.FocusFirstTile();
        WindowFixture.Pump();
        Assert.False(input.IsFocused);

        // AltGr+Q (Windows reports Ctrl+Alt) types a character on some layouts and never focuses the search.
        window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.Control | RawInputModifiers.Alt);
        window.KeyReleaseQwerty(PhysicalKey.Q, RawInputModifiers.Control | RawInputModifiers.Alt);
        WindowFixture.Pump();
        Assert.False(input.IsFocused);

        // Ctrl+K is the key of the PDF app; here it does nothing to the search.
        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.K, RawInputModifiers.Control);
        WindowFixture.Pump();
        Assert.False(input.IsFocused);

        // Alt+Q focuses it (macOS keeps Cmd+K for this option).
        var modifiers = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Alt;
        var key = OperatingSystem.IsMacOS() ? PhysicalKey.K : PhysicalKey.Q;
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        WindowFixture.Pump();
        Assert.True(input.IsFocused);
        Assert.Equal(SearchShortcut.AltQ, window.TitleSearch.Shortcut);
    }

    [AvaloniaFact]
    public async Task CtrlOOpensThePickerAndCtrlWClosesTheTab()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var calls = 0;
        window.FilePicker = () =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<string>>([WindowFixture.Poi]);
        };
        var command = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

        window.KeyPressQwerty(PhysicalKey.O, command);
        window.KeyReleaseQwerty(PhysicalKey.O, command);
        await WindowFixture.WaitAsync(() => window.Workspace.Tabs.Count == 1);
        Assert.Equal(1, calls);

        window.KeyPressQwerty(PhysicalKey.W, command);
        window.KeyReleaseQwerty(PhysicalKey.W, command);
        WindowFixture.Pump();
        Assert.Empty(window.Workspace.Tabs);
        Assert.Same(window.Start, window.Frame.Document);
    }

    [AvaloniaFact]
    public async Task ControlTabSwitchesTabs()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        await window.OpenFilesAsync([fixture.Copy(WindowFixture.Poi, "a.docx"), fixture.Copy(WindowFixture.Poi, "b.docx")]);
        Assert.Equal("b.docx", window.Workspace.Active!.Title);

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        WindowFixture.Pump();
        Assert.Equal("a.docx", window.Workspace.Active!.Title);

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        WindowFixture.Pump();
        Assert.Equal("b.docx", window.Workspace.Active!.Title);
    }

    [AvaloniaFact]
    public void TheSettingsAreReadOnceAndOpeningATabReadsNoFile()
    {
        using var fixture = new WindowFixture();
        File.WriteAllText(Path.Combine(fixture.Folder, "write-settings.json"), "{ \"DefaultView\": \"Writing\", \"DefaultZoomMode\": \"FitPage\" }");
        var window = fixture.Show();

        // The window reads no settings file itself (the app hands them over): the defaults are in force until the user confirms the dialog.
        Assert.Equal(Raffica.Write.Settings.WriteSettings.Default, window.Settings.App);
        window.Settings.Open();
        WindowFixture.Pump();
        Assert.True(window.Host.IsOpen);
    }
}
