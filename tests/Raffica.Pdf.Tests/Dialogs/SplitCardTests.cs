using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Tests.Printing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Dialogs;

public sealed class SplitCardTests
{
    private static void Show(Dialog dialog)
    {
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 700, Height = 700 };
        window.Show();
        host.Show(dialog);
        PrintHost.Pump();
    }

    private static T Find<T>(Dialog dialog, string name)
        where T : Control =>
        dialog.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static Dialog Make(int pages, bool bookmarks, string? folder, List<SplitChoice?> results) =>
        SplitCard.Create("doc.pdf", pages, bookmarks, (mode, every, ranges) => SplitFlow.PlanFor(pages, mode, every, ranges, "doc", []), () => Task.FromResult(folder), results.Add);

    [AvaloniaFact]
    public async Task SplitNeedsAFolderAndAValidModeAndAnswersTheChoice()
    {
        var results = new List<SplitChoice?>();
        var dialog = Make(6, false, @"C:\temp\x", results);
        Show(dialog);
        var split = Find<Button>(dialog, "PART_Split");
        Assert.False(split.IsEnabled);

        Find<Button>(dialog, "PART_ChooseFolder").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await PrintHost.UntilAsync(() => split.IsEnabled);
        Assert.Equal(@"C:\temp\x", Find<TextBlock>(dialog, "PART_Folder").Text);

        Find<TextBox>(dialog, "PART_Every").Text = "0";
        Assert.False(split.IsEnabled);
        Assert.False(string.IsNullOrWhiteSpace(Find<TextBlock>(dialog, "PART_PreviewProblem").Text));
        Find<TextBox>(dialog, "PART_Every").Text = "4";
        Assert.True(split.IsEnabled);

        split.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var choice = Assert.Single(results)!.Value;
        Assert.Equal(SplitModeChoice.Every, choice.Mode);
        Assert.Equal("4", choice.Every);
        Assert.Equal(@"C:\temp\x", choice.Folder);
    }

    [AvaloniaFact]
    public void BookmarkModeIsDisabledWithItsReason()
    {
        var dialog = Make(6, false, null, []);
        Show(dialog);
        Assert.False(Find<RadioButton>(dialog, "PART_ModeBookmarks").IsEnabled);
        Assert.Equal("This file has no bookmarks", Find<TextBlock>(dialog, "PART_BookmarksReason").Text);

        var enabled = Make(6, true, null, []);
        Show(enabled);
        Assert.True(Find<RadioButton>(enabled, "PART_ModeBookmarks").IsEnabled);
        Assert.False(Find<TextBlock>(enabled, "PART_BookmarksReason").IsVisible);
    }

    [AvaloniaFact]
    public void PreviewShowsTheFirstTenPartsAndTheRest()
    {
        var dialog = Make(100, false, null, []);
        Show(dialog);
        Find<TextBox>(dialog, "PART_Every").Text = "5";
        var items = dialog.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "PART_PreviewItem").ToList();
        Assert.Equal(10, items.Count);
        Assert.Equal("doc-p001-005.pdf (pages 1-5)", items[0].Text);
        Assert.Equal("and 10 more", Find<TextBlock>(dialog, "PART_PreviewMore").Text);

        Find<TextBox>(dialog, "PART_Every").Text = "50";
        Assert.Equal(2, dialog.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Name == "PART_PreviewItem"));
        Assert.False(Find<TextBlock>(dialog, "PART_PreviewMore").IsVisible);
    }

    [AvaloniaFact]
    public void ConfirmCardListsTheLossesAndAnswers()
    {
        var results = new List<bool>();
        var dialog = SplitCard.CreateConfirm(3, ["Same loss for all."], results.Add);
        Show(dialog);
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "PART_Loss" && t.Text == "• Same loss for all.");
        Find<Button>(dialog, "PART_Go").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal([true], results);
    }
}
