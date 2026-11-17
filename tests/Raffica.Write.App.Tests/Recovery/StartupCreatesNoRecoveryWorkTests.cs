using Avalonia.Headless.XUnit;
using Raffica.Write.Documents;
using Raffica.Write.Recovery;
using Raffica.WriteApp.Tests.Documents;
using Xunit;

namespace Raffica.WriteApp.Tests.Recovery;

/// <summary>Start-cost guard of milestone 4.3c: a window that opens and views a file creates nothing of the recovery copies.</summary>
public sealed class StartupCreatesNoRecoveryWorkTests
{
    [AvaloniaFact]
    public async Task NothingExistsBeforeTheFirstEdit()
    {
        using var fixture = new WindowFixture();
        var made = 0;
        var window = fixture.Show();
        window.RecoveryClockFactory = () =>
        {
            made++;
            return new ManualRecoveryClock();
        };

        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => window.ViewOf(tab) is not null);
        window.ActiveView!.ScrollByWheel(-3);
        WindowFixture.Pump();

        Assert.Null(window.Autosave);
        Assert.Equal(0, made);
        Assert.False(Directory.Exists(window.RecoveryFolder));
        Assert.Null(tab.Editor);
        Assert.False(tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task OpeningAndClosingACleanTabCreatesNothing()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);

        await SaveHarness.AwaitAsync(window.CloseTabAsync(tab));

        Assert.Null(window.Autosave);
        Assert.False(Directory.Exists(window.RecoveryFolder));
    }
}
