using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Shell.Controls;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Dialogs;
using Raffica.Write.Editing;
using Xunit;

namespace Raffica.WriteApp.Tests.Dialogs;

/// <summary>The Table of contents card (milestone 4.5b): the kind, the levels, the page numbers and their alignment and leader, Remove, the hint for a first level below the last, and the one-answer rule.</summary>
public sealed class TocCardTests
{
    private static (CardWindow Window, List<TocRequest?> Answers) Show(bool canRemove = false, params string[] labels)
    {
        var answers = new List<TocRequest?>();
        return (new CardWindow(TocCard.Create(new TocContext(labels.Length > 0 ? labels : ["Figure", "Table", "Equation"], canRemove), answers.Add)), answers);
    }

    // Whether the field (a label and its select) that holds the control is shown.
    private static bool Shown(Control control) => ((Control)control.Parent!).IsVisible;

    [AvaloniaFact]
    public void TheDefaultsAreLevelsOneToThreeWithRightAlignedDotLeadersAndPageNumbers()
    {
        var (card, answers) = Show();
        using var _ = card;
        Assert.Equal(WriteStrings.TocTitle, card.Dialog.Title);
        Assert.Equal(0, card.Find<SelectBox>("PART_Kind").SelectedIndex);
        Assert.Equal(0, card.Find<SelectBox>("PART_First").SelectedIndex);
        Assert.Equal(2, card.Find<SelectBox>("PART_Last").SelectedIndex);
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8", "9"], card.Find<SelectBox>("PART_Last").Items.Cast<string>().ToArray());

        card.Click("PART_Ok");

        Assert.Equal([new TocRequest(TocAction.Insert, new TocOptions(1, 3, true, true, TocLeader.Dots, null))], answers);
    }

    [AvaloniaFact]
    public void TheLevelsLeaderAndPageOptionsAreAnswered()
    {
        var (card, answers) = Show();
        using var _ = card;
        card.Find<SelectBox>("PART_First").SelectedIndex = 1;
        card.Find<SelectBox>("PART_Last").SelectedIndex = 8;
        card.Find<SelectBox>("PART_Leader").SelectedIndex = 2;
        CardWindow.Pump();

        card.Click("PART_Ok");

        Assert.Equal([new TocRequest(TocAction.Insert, new TocOptions(2, 9, true, true, TocLeader.Underscore, null))], answers);
    }

    [AvaloniaFact]
    public void WithoutPageNumbersTheAlignmentAndTheLeaderAreDimmed()
    {
        var (card, answers) = Show();
        using var _ = card;
        var pages = card.Find<CheckBox>("PART_Pages");
        var right = card.Find<CheckBox>("PART_Right");
        var leader = card.Find<SelectBox>("PART_Leader");
        Assert.True(right.IsEnabled);
        Assert.True(leader.IsEnabled);

        pages.IsChecked = false;
        CardWindow.Pump();
        Assert.False(right.IsEnabled);
        Assert.False(leader.IsEnabled);

        pages.IsChecked = true;
        right.IsChecked = false;
        CardWindow.Pump();
        Assert.True(right.IsEnabled);
        Assert.False(leader.IsEnabled);

        card.Click("PART_Ok");
        Assert.False(answers.Single()!.Options!.RightAlign);
    }

    [AvaloniaFact]
    public void AListOfFiguresOrTablesTakesTheLabelAndHidesTheLevels()
    {
        var (card, answers) = Show(false, "Figure", "Table", "Listing");
        using var _ = card;
        var label = card.Find<SelectBox>("PART_Label");
        Assert.False(Shown(label));
        Assert.True(Shown(card.Find<SelectBox>("PART_First")));

        card.Find<SelectBox>("PART_Kind").SelectedIndex = 1;
        CardWindow.Pump();
        Assert.True(Shown(label));
        Assert.False(Shown(card.Find<SelectBox>("PART_First")));
        Assert.False(Shown(card.Find<SelectBox>("PART_Last")));
        Assert.Equal(["Figure", "Table", "Listing"], label.Items.Cast<string>().ToArray());

        label.SelectedIndex = 2;
        CardWindow.Pump();
        card.Click("PART_Ok");

        var options = answers.Single()!.Options!;
        Assert.Equal("Listing", options.Label);
        Assert.True(options.PageNumbers);
    }

    [AvaloniaFact]
    public void AFirstLevelBelowTheLastShowsItsReasonAndKeepsTheCardOpenUnlessItIsAList()
    {
        var (card, answers) = Show();
        using var _ = card;
        card.Find<SelectBox>("PART_First").SelectedIndex = 4;
        card.Find<SelectBox>("PART_Last").SelectedIndex = 1;
        CardWindow.Pump();

        card.Click("PART_Ok");

        Assert.Empty(answers);
        var hint = card.Find<TextBlock>("PART_Hint");
        Assert.True(hint.IsVisible);
        Assert.Equal(WriteStrings.TocProblemLevels, hint.Text);

        // The reason goes away with the next choice; a list does not use the levels at all.
        card.Find<SelectBox>("PART_Last").SelectedIndex = 6;
        CardWindow.Pump();
        Assert.False(hint.IsVisible);
        card.Find<SelectBox>("PART_Last").SelectedIndex = 1;
        card.Find<SelectBox>("PART_Kind").SelectedIndex = 1;
        CardWindow.Pump();
        card.Click("PART_Ok");
        Assert.Equal("Figure", answers.Single()!.Options!.Label);
    }

    [AvaloniaFact]
    public void RemoveIsOfferedOnlyInATableOfContentsAndAnswersRemove()
    {
        var (without, _) = Show(canRemove: false);
        using (without)
        {
            Assert.Null(without.FindOrNull<Button>("PART_Remove"));
        }

        var (card, answers) = Show(canRemove: true);
        using var _ = card;
        Assert.NotNull(card.FindOrNull<Button>("PART_Remove"));

        card.Click("PART_Remove");
        card.Click("PART_Ok");

        Assert.Equal([new TocRequest(TocAction.Remove, null)], answers);
    }

    [AvaloniaFact]
    public void CloseAndEscapeAnswerNull()
    {
        var (card, answers) = Show();
        using (card)
        {
            card.Click("PART_Cancel");
            card.Click("PART_Ok");
            Assert.Equal([null], answers);
        }

        var (escaped, again) = Show();
        using (escaped)
        {
            escaped.Press(escaped.Find<SelectBox>("PART_Kind"), PhysicalKey.Escape, Key.Escape);
            Assert.Equal([null], again);
        }
    }

    [Fact]
    public void ARequestIsWellFormedOnlyWithAKnownActionAndOptions()
    {
        Assert.True(new TocRequest(TocAction.Insert, new TocOptions(1, 3, true, true, TocLeader.Dots, null)).IsValid);
        Assert.True(new TocRequest(TocAction.Insert, new TocOptions(1, 3, false, false, TocLeader.None, "Figure")).IsValid);
        Assert.True(new TocRequest(TocAction.Remove, null).IsValid);

        // The shape only: the order of the levels is the editor's to refuse, with a reason.
        Assert.True(new TocRequest(TocAction.Insert, new TocOptions(5, 2, true, true, TocLeader.Dots, null)).IsValid);
        Assert.False(new TocRequest(TocAction.Insert, null).IsValid);
        Assert.False(new TocRequest(TocAction.Insert, new TocOptions(1, 3, true, true, (TocLeader)9, null)).IsValid);
        Assert.False(new TocRequest(TocAction.Insert, new TocOptions(1, 3, true, true, TocLeader.Dots, "two words")).IsValid);
        Assert.False(new TocRequest((TocAction)5, null).IsValid);
    }

    [AvaloniaFact]
    public void TheCardHoldsNoTextOfTheDocumentAndEveryControlHasAName()
    {
        var (card, _) = Show(true, "Figure");
        using var _ = card;
        foreach (var name in new[] { "PART_Kind", "PART_First", "PART_Last", "PART_Leader" })
        {
            Assert.False(string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(card.Find<SelectBox>(name))), name);
        }

        Assert.False(string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(card.Find<CheckBox>("PART_Pages"))));
        Assert.False(string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(card.Find<CheckBox>("PART_Right"))));
    }
}
