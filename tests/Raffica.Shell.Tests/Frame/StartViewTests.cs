using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class StartViewTests
{
    private static readonly RecentFile[] Files =
    [
        new("Quarterly report.docx", @"C:\Users\anna\Documents", new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero)),
        new("Budget.xlsx", @"C:\Users\anna\Finance", new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero)),
    ];

    private static (Window Window, StartView View) Show(IReadOnlyList<RecentFile>? recent = null, StartApp own = StartApp.Write)
    {
        var view = new StartView { Tiles = StartTile.Standard(), Recent = recent ?? Files, OwnApp = own };
        var window = ControlHarness.Show(view, ThemeMode.Light);
        window.Width = 960;
        window.Height = 640;
        ControlHarness.Settle(window);
        return (window, view);
    }

    private static List<Button> TileButtons(StartView view) =>
        view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("start-tile")).ToList();

    private static List<Button> RowButtons(StartView view) =>
        view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("start-row")).ToList();

    private static void Click(Window window, Control control)
    {
        var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        ControlHarness.Settle(window);
    }

    [AvaloniaFact]
    public void ShowsFourTilesAndTheOwnAppOpensInPlace()
    {
        var previous = ShellStrings.Culture;
        try
        {
            ShellStrings.Culture = CultureInfo.GetCultureInfo("en");
            var (window, view) = Show(own: StartApp.Sheets);
            var requests = new List<StartApp>();
            view.NewRequested += requests.Add;

            var tiles = TileButtons(view);
            Assert.Equal(4, tiles.Count);
            Assert.Equal(["Blank document", "Blank spreadsheet", "Blank presentation", "Open PDF"], tiles.Select(t => AutomationProperties.GetName(t)));

            // The own tile (Sheets) and another one both raise NewRequested with their app; the host decides in place versus start.
            Click(window, tiles[1]);
            Click(window, tiles[3]);
            Assert.Equal([StartApp.Sheets, StartApp.Pdf], requests);
            Assert.Equal(StartApp.Sheets, view.OwnApp);
        }
        finally
        {
            ShellStrings.Culture = previous;
        }
    }

    [AvaloniaFact]
    public void BrowseRaisesBrowseRequested()
    {
        var (window, view) = Show();
        var count = 0;
        view.BrowseRequested += () => count++;

        Click(window, ControlHarness.Part<Button>(view, "PART_Browse"));

        Assert.Equal(1, count);
    }

    [AvaloniaFact]
    public void RecentRowsAre40HighWithLocalizedDates()
    {
        var previous = ShellStrings.Culture;
        try
        {
            ShellStrings.Culture = CultureInfo.GetCultureInfo("de");
            var (window, view) = Show();
            var rows = RowButtons(view);
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal(40, r.Bounds.Height, 0.01));

            var texts = rows[0].GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Equal(["Quarterly report.docx", @"C:\Users\anna\Documents", "03.11.2026"], texts);

            var opened = new List<RecentFile>();
            view.OpenRequested += opened.Add;
            Click(window, rows[1]);
            Assert.Equal([Files[1]], opened);
            window.Close();

            ShellStrings.Culture = CultureInfo.GetCultureInfo("en-US");
            var (_, english) = Show();
            var englishTexts = RowButtons(english)[0].GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Equal("11/3/2026", englishTexts[2]);
        }
        finally
        {
            ShellStrings.Culture = previous;
        }
    }

    [AvaloniaFact]
    public void DateTextFollowsTheGivenCulture()
    {
        var file = Files[0];
        Assert.Equal("03.11.2026", file.DateText(CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal("11/3/2026", file.DateText(CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("03/11/2026", file.DateText(CultureInfo.GetCultureInfo("en-GB")));
    }

    [AvaloniaFact]
    public void EmptyRecentShowsOneLine()
    {
        var (_, view) = Show(recent: []);
        var empty = ControlHarness.Part<TextBlock>(view, "PART_RecentEmpty");

        Assert.True(empty.IsVisible);
        Assert.Equal(ShellStrings.StartRecentEmpty, empty.Text);
        Assert.Empty(RowButtons(view));

        // With files the line is gone.
        view.Recent = Files;
        ControlHarness.Settle((Window)TopLevel.GetTopLevel(view)!);
        Assert.False(empty.IsVisible);
        Assert.Equal(2, RowButtons(view).Count);
    }

    [AvaloniaFact]
    public void TilesAreTwoPerRowIn336Px()
    {
        var (_, view) = Show();
        var column = ControlHarness.Part<Grid>(view, "PART_Tiles").FindAncestorOfType<Border>()!;
        var tiles = TileButtons(view);
        var at = tiles.Select(t => t.TranslatePoint(default, view)!.Value).ToList();

        Assert.All(tiles, t => Assert.Equal(150, t.Bounds.Height, 0.01));
        // Two per row: the first two share a top edge, the third starts the next row under the first.
        Assert.Equal(at[0].Y, at[1].Y, 0.01);
        Assert.Equal(at[2].Y, at[3].Y, 0.01);
        Assert.True(at[1].X > at[0].X);
        Assert.Equal(at[0].X, at[2].X, 0.01);
        Assert.True(at[2].Y >= at[0].Y + 150);
        // Both tiles of a row fit into the 336 px column.
        Assert.Equal(336, column.Bounds.Width, 0.01);
        Assert.True(at[1].X + tiles[1].Bounds.Width <= 336);
        Assert.Equal(12, at[1].X - (at[0].X + tiles[0].Bounds.Width), 0.01);
        // The app mark is 11 px and sits in the tile.
        var mark = tiles[0].GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("start-mark"));
        Assert.Equal(11, mark.FontSize);
    }

    [AvaloniaFact]
    public void EveryTileAndRowHasAnAutomationName()
    {
        var (_, view) = Show();
        foreach (var button in TileButtons(view).Concat(RowButtons(view)).Append(ControlHarness.Part<Button>(view, "PART_Browse")))
        {
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
        }
    }

    [AvaloniaFact]
    public void HasNoStatusBarAndNoShadow()
    {
        var (_, view) = Show();
        Assert.Empty(view.GetVisualDescendants().OfType<StatusBar>());
        Assert.False(ControlHarness.HasShadow(view));
    }
}
