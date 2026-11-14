using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.WriteApp.Tests;

/// <summary>What the Write window stores when it closes: the shared tracker of Raffica.Hosting (its logic is tested there) wired to the window.</summary>
public sealed class WindowPlacementTests
{
    private static readonly WindowPlacement Normal = new(100, 80, 1100, 700, false);

    private static WindowPlacement? Stored(WindowFixture fixture) => new SettingsStore(fixture.SettingsFile).Load().WindowPlacement;

    // Later than anything the shown window itself reports, so the seams below are the newest snapshots.
    private static long Later() => Environment.TickCount64 + 100_000;

    [AvaloniaFact]
    public void MaximizeThenCloseStoresThePreviousNormalBoundsAsMaximized()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var at = Later();
        window.Placement.NoteBounds(Normal, at);

        // Win32 raises the move to the maximized corner before the state changes.
        window.Placement.NoteBounds(new WindowPlacement(-8, -8, 1100, 700, false), at + 8_990);
        window.Placement.NoteState(WindowState.Maximized, at + 9_000);
        window.Close();
        WindowFixture.Pump();

        Assert.Equal(Normal with { Maximized = true }, Stored(fixture));
    }

    [AvaloniaFact]
    public void MinimizeThenCloseStoresThePreviousNormalBounds()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var at = Later();
        window.Placement.NoteBounds(Normal, at);

        window.Placement.NoteBounds(new WindowPlacement(-32000, -32000, 160, 28, false), at + 8_990);
        window.Placement.NoteState(WindowState.Minimized, at + 9_000);
        window.Close();
        WindowFixture.Pump();

        Assert.Equal(Normal, Stored(fixture));
    }

    [AvaloniaFact]
    public void ANormalResizeThenCloseStoresTheNewBounds()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var at = Later();
        window.Placement.NoteBounds(Normal, at);
        var resized = new WindowPlacement(100, 80, 1000, 640, false);
        window.Placement.NoteBounds(resized, at + 5_000);

        window.Close();
        WindowFixture.Pump();

        Assert.Equal(resized, Stored(fixture));
    }

    [AvaloniaFact]
    public void AStoredMaximizedPlacementStartsMaximizedAndIsStoredAgain()
    {
        using var fixture = new WindowFixture();
        new SettingsStore(fixture.SettingsFile).Save(new ShellSettings(WindowPlacement: Normal with { Maximized = true }));
        var window = fixture.Show();

        Assert.Equal(WindowState.Maximized, window.WindowState);
        Assert.True(window.Placement.Maximized);
        window.Close();
        WindowFixture.Pump();

        Assert.Equal(Normal with { Maximized = true }, Stored(fixture));
    }
}
