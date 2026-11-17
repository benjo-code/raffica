using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Docx;
using Xunit;

namespace Raffica.WriteApp.Tests.Commands;

/// <summary>The New document command (Task 3 of milestone 4.3c): Ctrl+N (Cmd+N), an untitled tab, its own icon, a reason while the window closes, nothing built at start.</summary>
public sealed class NewDocumentCommandTests
{
    [AvaloniaFact]
    public async Task NewDocumentOpensAnUntitledTabAndSelectsIt()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show(files: WindowFixture.Poi);
        await WindowFixture.WaitAsync(() => window.Workspace.Tabs.Count == 1);
        var papers = 0;

        var tab = await window.NewDocumentAsync(() =>
        {
            papers++;
            return PaperSize.Letter;
        });
        WindowFixture.Pump();

        Assert.NotNull(tab);
        Assert.Equal(1, papers);
        Assert.Equal(2, window.Workspace.Tabs.Count);
        Assert.Same(tab, window.Workspace.Active);
        Assert.True(tab.IsUntitled);
        Assert.Equal("Untitled", tab.Title);
    }

    [AvaloniaFact]
    public async Task CtrlNRunsIt()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        window.PaperSource = () => PaperSize.A4;
        var command = (RawInputModifiers)(int)Avalonia.Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers;

        window.KeyPressQwerty(PhysicalKey.N, command);
        window.KeyReleaseQwerty(PhysicalKey.N, command);
        await WindowFixture.WaitAsync(() => window.Workspace.Tabs.Count == 1);

        Assert.Equal("Untitled", window.Workspace.Active!.Title);
        Assert.Equal(WriteCommandIds.New, WriteCommandCatalog.Find(Key.N, KeyModifiers.Control, KeyModifiers.Control, macOs: false)?.Id);
        Assert.Equal(WriteCommandIds.New, WriteCommandCatalog.Find(Key.N, KeyModifiers.Meta, KeyModifiers.Meta, macOs: true)?.Id);
    }

    [Fact]
    public void ItIsDisabledWhileTheWindowClosesWithAReason()
    {
        var closing = false;
        var rules = new WriteCommandRules(() => true, () => 1, isClosing: () => closing);
        Assert.True(rules.Allows(WriteCommandIds.New));
        Assert.Null(rules.ReasonFor(WriteCommandIds.New));

        closing = true;
        Assert.False(rules.Allows(WriteCommandIds.New));
        Assert.False(string.IsNullOrWhiteSpace(rules.ReasonFor(WriteCommandIds.New)));

        // Without a document (the start view) it is enabled as Open is.
        Assert.True(new WriteCommandRules(() => false, () => 1).Allows(WriteCommandIds.New));
    }

    [Fact]
    public void TheCommandHasItsOwnIconAndAHomeFieldCode()
    {
        var command = WriteCommandCatalog.Commands.Single(c => c.Id == WriteCommandIds.New);
        Assert.Equal("FilePlus", command.Icon);
        Assert.Single(WriteCommandCatalog.Commands, c => c.Icon == command.Icon);
        Assert.Single(WriteCommandCatalog.Commands, c => c.FieldCode == command.FieldCode && c.Tab == command.Tab);
    }

    [AvaloniaFact]
    public async Task StartupCreatesNoBlankDocumentWork()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var made = 0;
        var original = window.Flow.BlankOpener;
        window.Flow.BlankOpener = (bytes, work, token) =>
        {
            made++;
            return original(bytes, work, token);
        };

        await window.LoadRecentAsync();
        WindowFixture.Pump();
        Assert.IsType<Raffica.Write.Documents.Opened>(await window.Flow.OpenAsync(WindowFixture.Poi));
        WindowFixture.Pump();
        Assert.Equal(0, made);

        _ = await window.NewDocumentAsync(() => PaperSize.A4);
        Assert.Equal(1, made);
    }
}
