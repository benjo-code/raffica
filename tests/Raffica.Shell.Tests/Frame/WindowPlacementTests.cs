using Avalonia;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class WindowPlacementTests : IDisposable
{
    private static readonly Size Default = new(1280, 860);
    private readonly string _directory = Directory.CreateTempSubdirectory("raffica-placement-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void WithoutAStoredPlacementTheDefaultSizeIsCentredOnABigWorkArea()
    {
        var work = new PixelRect(0, 0, 1920, 1032);

        var placement = WindowPlacement.FitTo(work, 1.0, null, Default);

        Assert.Equal(new WindowPlacement(320, 86, 1280, 860, false), placement);
    }

    [Fact]
    public void WithoutAStoredPlacementASmallWorkAreaShrinksTheWindowByAMarginOnEverySide()
    {
        var work = new PixelRect(0, 0, 1366, 728);

        var placement = WindowPlacement.FitTo(work, 1.0, null, Default);

        Assert.Equal(1280, placement.Width);
        Assert.Equal(728 - (2 * WindowPlacement.Margin), placement.Height);
        Assert.Equal(WindowPlacement.Margin, placement.Y);
        Assert.Equal(43, placement.X);
        Assert.False(placement.Maximized);
    }

    [Fact]
    public void TheMarginAndTheSizeAreCountedInDipsOnAScaledScreen()
    {
        // 2560 x 1392 pixels at 200 percent are 1280 x 696 dips.
        var work = new PixelRect(0, 0, 2560, 1392);

        var placement = WindowPlacement.FitTo(work, 2.0, null, Default);

        Assert.Equal(1232, placement.Width);
        Assert.Equal(696 - (2 * WindowPlacement.Margin), placement.Height);
        Assert.Equal(24 * 2, placement.Y);
        Assert.Equal(24 * 2, placement.X);
    }

    [Fact]
    public void AStoredPlacementOutsideTheWorkAreaIsMovedInside()
    {
        var work = new PixelRect(0, 0, 1920, 1032);
        var stored = new WindowPlacement(5000, -900, 1000, 700, false);

        var placement = WindowPlacement.FitTo(work, 1.0, stored, Default);

        Assert.Equal(new WindowPlacement(920, 0, 1000, 700, false), placement);
    }

    [Fact]
    public void AStoredPlacementInsideTheWorkAreaStaysAndTheMaximizedFlagIsKept()
    {
        var work = new PixelRect(0, 0, 1920, 1032);
        var stored = new WindowPlacement(100, 120, 1100, 640, true);

        Assert.Equal(stored, WindowPlacement.FitTo(work, 1.0, stored, Default));
    }

    [Fact]
    public void AStoredPlacementBiggerThanTheWorkAreaShrinks()
    {
        var work = new PixelRect(0, 0, 1366, 728);
        var stored = new WindowPlacement(0, 0, 3000, 2000, false);

        var placement = WindowPlacement.FitTo(work, 1.0, stored, Default);

        Assert.Equal(1366 - (2 * WindowPlacement.Margin), placement.Width);
        Assert.Equal(728 - (2 * WindowPlacement.Margin), placement.Height);
        Assert.True(placement.X >= 0 && placement.X + placement.Width <= 1366);
        Assert.True(placement.Y >= 0 && placement.Y + placement.Height <= 728);
    }

    [Fact]
    public void AWorkAreaThatStartsAwayFromTheOriginKeepsTheWindowInsideIt()
    {
        var work = new PixelRect(-1920, 0, 1920, 1032);
        var stored = new WindowPlacement(500, 500, 800, 600, false);

        var placement = WindowPlacement.FitTo(work, 1.0, stored, Default);

        Assert.True(placement.X >= -1920 && placement.X + placement.Width <= 0);
    }

    [Theory]
    [InlineData(double.NaN, 100)]
    [InlineData(100, double.PositiveInfinity)]
    public void AStoredPlacementWithoutRealNumbersCountsAsNotStored(double x, double width)
    {
        var work = new PixelRect(0, 0, 1920, 1032);

        var placement = WindowPlacement.FitTo(work, 1.0, new WindowPlacement(x, 0, width, 500, false), Default);

        Assert.Equal(WindowPlacement.FitTo(work, 1.0, null, Default), placement);
    }

    [Fact]
    public void ThePlacementSurvivesTheSettingsFile()
    {
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
        var settings = new ShellSettings(ThemeMode.Dark, "de", WindowPlacement: new WindowPlacement(10, 20, 1111, 777, true));

        Assert.True(store.TrySave(settings));

        Assert.Equal(settings, store.Load());
    }

    [Fact]
    public void AFileWithoutAPlacementAndOneWithAnUnusableOneGiveNone()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new SettingsStore(path);

        File.WriteAllText(path, """{"Theme": "Dark"}""");
        Assert.Null(store.Load().WindowPlacement);

        File.WriteAllText(path, """{"Theme": "Dark", "WindowPlacement": {"X": 1, "Y": 2, "Width": -5, "Height": 0, "Maximized": false}}""");
        Assert.Null(store.Load().WindowPlacement);
        Assert.Equal(ThemeMode.Dark, store.Load().Theme);
    }
}
