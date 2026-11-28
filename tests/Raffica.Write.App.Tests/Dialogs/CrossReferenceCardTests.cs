using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Shell.Controls;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Dialogs;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.WriteApp.Tests.Dialogs;

/// <summary>The Cross-reference card (milestone 4.5b): the types, the list of targets of a type, the choices a type offers, the answer, the empty list, Enter, Esc and the one-answer rule.</summary>
public sealed class CrossReferenceCardTests
{
    private static CrossRefTarget Target(CrossRefTargetKind kind, string display) => new(kind, new TextPosition(StoryRef.Body, new Paragraph(), 0), display);

    private static readonly Dictionary<CrossRefTargetKind, CrossRefTarget[]> s_targets = new()
    {
        [CrossRefTargetKind.Heading] = [Target(CrossRefTargetKind.Heading, "1 Introduction"), Target(CrossRefTargetKind.Heading, "2 Method")],
        [CrossRefTargetKind.Caption] = [Target(CrossRefTargetKind.Caption, "Figure 1: Results")],
        [CrossRefTargetKind.Footnote] = [Target(CrossRefTargetKind.Footnote, "1 A note")],
        [CrossRefTargetKind.Endnote] = [],
        [CrossRefTargetKind.Bookmark] = [Target(CrossRefTargetKind.Bookmark, "Quick")],
    };

    private static (CardWindow Window, List<CrossRefRequest?> Answers, List<CrossRefTargetKind> Asked) Show(CrossRefTargetKind start = CrossRefTargetKind.Heading)
    {
        var answers = new List<CrossRefRequest?>();
        var asked = new List<CrossRefTargetKind>();
        var context = new CrossRefContext(
            kind =>
            {
                asked.Add(kind);
                return s_targets[kind];
            },
            start);
        return (new CardWindow(CrossReferenceCard.Create(context, answers.Add)), answers, asked);
    }

    [AvaloniaFact]
    public void TheTypesAreTheFiveKindsAndTheFirstTargetIsSelected()
    {
        var (card, answers, asked) = Show();
        using var _ = card;
        var type = card.Find<SelectBox>("PART_Type");
        Assert.Equal([WriteStrings.CrossRefTypeHeading, WriteStrings.CrossRefTypeCaption, WriteStrings.CrossRefTypeFootnote, WriteStrings.CrossRefTypeEndnote, WriteStrings.CrossRefTypeBookmark], type.Items.Cast<string>().ToArray());
        Assert.Equal(0, type.SelectedIndex);
        Assert.Equal(WriteStrings.CrossRefTitle, card.Dialog.Title);

        // The targets of the starting type were read once, when the card opened; the first one is selected and the choices are the ones a heading offers.
        Assert.Equal([CrossRefTargetKind.Heading], asked);
        var list = card.Find<ListBox>("PART_List");
        Assert.Equal(["1 Introduction", "2 Method"], list.Items.Cast<string>().ToArray());
        Assert.Equal(0, list.SelectedIndex);
        Assert.Equal([WriteStrings.CrossRefKindNumber, WriteStrings.CrossRefKindText, WriteStrings.CrossRefKindPage], card.Find<SelectBox>("PART_Kind").Items.Cast<string>().ToArray());

        card.Click("PART_Ok");

        Assert.Equal([new CrossRefRequest(s_targets[CrossRefTargetKind.Heading][0], CrossRefKind.NumberOnly)], answers);
    }

    [AvaloniaFact]
    public void ChoosingATypeLoadsItsTargetsAndItsChoices()
    {
        var (card, answers, asked) = Show();
        using var _ = card;
        var type = card.Find<SelectBox>("PART_Type");
        var list = card.Find<ListBox>("PART_List");
        var kind = card.Find<SelectBox>("PART_Kind");

        type.SelectedIndex = 1;
        CardWindow.Pump();
        Assert.Equal(["Figure 1: Results"], list.Items.Cast<string>().ToArray());
        Assert.Equal([WriteStrings.CrossRefKindLabelAndNumber, WriteStrings.CrossRefKindNumber, WriteStrings.CrossRefKindText, WriteStrings.CrossRefKindPage], kind.Items.Cast<string>().ToArray());

        // A choice the new type offers too stays chosen: Page number of a caption, then a footnote (number or page).
        kind.SelectedIndex = 3;
        type.SelectedIndex = 2;
        CardWindow.Pump();
        Assert.Equal([WriteStrings.CrossRefKindNumber, WriteStrings.CrossRefKindPage], kind.Items.Cast<string>().ToArray());
        Assert.Equal(1, kind.SelectedIndex);
        Assert.Equal([CrossRefTargetKind.Heading, CrossRefTargetKind.Caption, CrossRefTargetKind.Footnote], asked);

        card.Click("PART_Ok");

        Assert.Equal([new CrossRefRequest(s_targets[CrossRefTargetKind.Footnote][0], CrossRefKind.Page)], answers);
    }

    [AvaloniaFact]
    public void ABookmarkOffersItsTextAndPage()
    {
        var (card, answers, _) = Show();
        using var _ = card;
        card.Find<SelectBox>("PART_Type").SelectedIndex = 4;
        CardWindow.Pump();
        Assert.Equal([WriteStrings.CrossRefKindText, WriteStrings.CrossRefKindPage], card.Find<SelectBox>("PART_Kind").Items.Cast<string>().ToArray());

        card.Click("PART_Ok");

        Assert.Equal([new CrossRefRequest(s_targets[CrossRefTargetKind.Bookmark][0], CrossRefKind.Text)], answers);
    }

    [AvaloniaFact]
    public void ASecondRowIsAnsweredWhenItIsSelected()
    {
        var (card, answers, _) = Show();
        using var _ = card;
        card.Find<ListBox>("PART_List").SelectedIndex = 1;
        card.Find<SelectBox>("PART_Kind").SelectedIndex = 1;
        CardWindow.Pump();

        card.Click("PART_Ok");

        Assert.Equal([new CrossRefRequest(s_targets[CrossRefTargetKind.Heading][1], CrossRefKind.Text)], answers);
    }

    [AvaloniaFact]
    public void ATypeWithNoTargetsSaysSoAndCannotBeInserted()
    {
        var (card, answers, _) = Show(CrossRefTargetKind.Endnote);
        using var _ = card;
        Assert.True(card.Find<TextBlock>("PART_Empty").IsVisible);
        Assert.Equal(WriteStrings.CrossRefNone, card.Find<TextBlock>("PART_Empty").Text);
        Assert.Equal(-1, card.Find<ListBox>("PART_List").SelectedIndex);
        Assert.False(card.Find<Button>("PART_Ok").IsEnabled);

        card.Click("PART_Ok");

        Assert.Empty(answers);

        card.Find<SelectBox>("PART_Type").SelectedIndex = 0;
        CardWindow.Pump();
        Assert.False(card.Find<TextBlock>("PART_Empty").IsVisible);
        Assert.True(card.Find<Button>("PART_Ok").IsEnabled);
    }

    [AvaloniaFact]
    public void CancelEscapeAndEnterAnswerOnce()
    {
        var (card, answers, _) = Show();
        using (card)
        {
            card.Click("PART_Cancel");
            card.Click("PART_Ok");
            Assert.Equal([null], answers);
        }

        var (escaped, again, _) = Show();
        using (escaped)
        {
            escaped.Press(escaped.Find<ListBox>("PART_List"), PhysicalKey.Escape, Key.Escape);
            Assert.Equal([null], again);
        }

        var (entered, taken, _) = Show();
        using (entered)
        {
            entered.Press(entered.Find<ListBox>("PART_List"), PhysicalKey.Enter, Key.Enter);
            Assert.Equal([new CrossRefRequest(s_targets[CrossRefTargetKind.Heading][0], CrossRefKind.NumberOnly)], taken);
        }
    }

    [Fact]
    public void ARequestIsWellFormedOnlyWhenTheTargetOffersTheChoice()
    {
        var heading = Target(CrossRefTargetKind.Heading, "x");
        Assert.True(new CrossRefRequest(heading, CrossRefKind.Text).IsValid);
        Assert.False(new CrossRefRequest(heading, CrossRefKind.LabelAndNumber).IsValid);
        Assert.False(new CrossRefRequest(Target(CrossRefTargetKind.Footnote, "1"), CrossRefKind.Text).IsValid);
        Assert.False(new CrossRefRequest(heading, (CrossRefKind)9).IsValid);
        Assert.False(new CrossRefRequest(Target((CrossRefTargetKind)9, "x"), CrossRefKind.Text).IsValid);
    }
}
