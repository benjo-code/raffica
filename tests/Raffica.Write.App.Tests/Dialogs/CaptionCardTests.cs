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

/// <summary>The Caption card (milestone 4.5b): its labels, the place that follows the label, the chapter number, the preview, the validity hints and the one-answer rule.</summary>
public sealed class CaptionCardTests
{
    private static CaptionContext Context(bool chapter = true, string defaultLabel = "Figure", params string[] extra) =>
        new(["Figure", "Table", "Equation", .. extra], defaultLabel, chapter, label => label switch
        {
            "Figure" => "Abbildung",
            _ => label,
        });

    private static (CardWindow Window, List<CaptionRequest?> Answers) Show(CaptionContext context)
    {
        var answers = new List<CaptionRequest?>();
        return (new CardWindow(CaptionCard.Create(context, answers.Add)), answers);
    }

    [AvaloniaFact]
    public void TheLabelsAreTheFileLabelsAndTheDefaultIsSelected()
    {
        var (card, _) = Show(Context(true, "Table", "Listing"));
        using (card)
        {
            var label = card.Find<SelectBox>("PART_Label");
            Assert.Equal(["Figure", "Table", "Equation", "Listing"], label.Items.Cast<string>().ToArray());
            Assert.Equal("Table", label.SelectedItem);
            Assert.Equal(WriteStrings.CaptionTitle, card.Dialog.Title);

            // A table is captioned above: the place followed the label.
            Assert.Equal(0, card.Find<SelectBox>("PART_Place").SelectedIndex);
        }

        var (figure, _) = Show(Context());
        using (figure)
        {
            Assert.Equal(1, figure.Find<SelectBox>("PART_Place").SelectedIndex);
        }
    }

    [AvaloniaFact]
    public void ThePlaceFollowsTheLabelUntilTheUserChoosesOne()
    {
        var (card, answers) = Show(Context());
        using var _ = card;
        var label = card.Find<SelectBox>("PART_Label");
        var place = card.Find<SelectBox>("PART_Place");

        label.SelectedIndex = 1;
        CardWindow.Pump();
        Assert.Equal(0, place.SelectedIndex);
        label.SelectedIndex = 0;
        CardWindow.Pump();
        Assert.Equal(1, place.SelectedIndex);

        // Once the user has chosen, the label no longer moves it.
        place.SelectedIndex = 0;
        CardWindow.Pump();
        label.SelectedIndex = 2;
        CardWindow.Pump();
        Assert.Equal(0, place.SelectedIndex);

        card.Click("PART_Ok");
        Assert.Equal([new CaptionRequest("Equation", string.Empty, CaptionPlace.Above, ChapterNumber: false)], answers);
    }

    [AvaloniaFact]
    public void ANewLabelBeatsTheListAndTheTextAndTheChapterNumberAreAnswered()
    {
        var (card, answers) = Show(Context());
        using var _ = card;
        card.Type("PART_NewLabel", "Scheme_2");
        card.Type("PART_Text", ": a title");
        card.Find<CheckBox>("PART_Chapter").IsChecked = true;
        CardWindow.Pump();

        card.Click("PART_Ok");

        Assert.Equal([new CaptionRequest("Scheme_2", ": a title", CaptionPlace.Below, ChapterNumber: true)], answers);
        card.Click("PART_Cancel");
        Assert.Single(answers);
    }

    [AvaloniaFact]
    public void TheChapterNumberNeedsNumberedHeadingsAndSaysSo()
    {
        var (card, answers) = Show(Context(chapter: false));
        using var _ = card;
        var box = card.Find<CheckBox>("PART_Chapter");
        Assert.False(box.IsEnabled);
        Assert.True(card.Find<TextBlock>("PART_ChapterHint").IsVisible);
        Assert.Equal(WriteStrings.CaptionChapterUnavailable, card.Find<TextBlock>("PART_ChapterHint").Text);

        card.Click("PART_Ok");

        Assert.False(answers.Single()!.ChapterNumber);

        var (possible, _) = Show(Context());
        using (possible)
        {
            Assert.True(possible.Find<CheckBox>("PART_Chapter").IsEnabled);
            Assert.False(possible.Find<TextBlock>("PART_ChapterHint").IsVisible);
        }
    }

    [AvaloniaFact]
    public void ThePreviewShowsTheWordTheNumberAndTheText()
    {
        var (card, _) = Show(Context());
        using var _ = card;
        var preview = card.Find<TextBlock>("PART_Preview");
        Assert.Equal("Abbildung 1", preview.Text);

        card.Type("PART_Text", ": Results");
        Assert.Equal("Abbildung 1: Results", preview.Text);

        card.Find<CheckBox>("PART_Chapter").IsChecked = true;
        CardWindow.Pump();
        Assert.Equal("Abbildung 2-1: Results", preview.Text);

        card.Find<SelectBox>("PART_Label").SelectedIndex = 1;
        CardWindow.Pump();
        Assert.Equal("Table 2-1: Results", preview.Text);
    }

    [AvaloniaFact]
    public void ABadLabelOrTextShowsItsReasonAndKeepsTheCardOpen()
    {
        (string Label, string Text, string Hint)[] cases =
        [
            ("1st", string.Empty, WriteStrings.CaptionProblemLabel),
            ("two words", string.Empty, WriteStrings.CaptionProblemLabel),
            (new string('a', 41), string.Empty, WriteStrings.CaptionProblemLabel),
            (string.Empty, new string('x', 1001), WriteStrings.CaptionProblemText),
            (string.Empty, "tab\there", WriteStrings.CaptionProblemText),
        ];
        foreach (var (label, text, hint) in cases)
        {
            var (card, answers) = Show(Context());
            using var _ = card;
            card.Type("PART_NewLabel", label);
            card.Type("PART_Text", text);

            card.Click("PART_Ok");

            Assert.Empty(answers);
            var shown = card.Find<TextBlock>("PART_Hint");
            Assert.True(shown.IsVisible, label + text);
            Assert.Equal(hint, shown.Text);
        }
    }

    [AvaloniaFact]
    public void CloseAndEscapeAnswerNullAndEnterInTheTextBoxInserts()
    {
        var (card, answers) = Show(Context());
        using (card)
        {
            card.Click("PART_Cancel");
            Assert.Equal([null], answers);
        }

        var (escaped, again) = Show(Context());
        using (escaped)
        {
            escaped.Press(escaped.Find<TextBox>("PART_Text"), PhysicalKey.Escape, Key.Escape);
            Assert.Equal([null], again);
        }

        var (entered, taken) = Show(Context());
        using (entered)
        {
            entered.Type("PART_Text", "x");
            entered.Press(entered.Find<TextBox>("PART_Text"), PhysicalKey.Enter, Key.Enter);
            Assert.Equal([new CaptionRequest("Figure", "x", CaptionPlace.Below, ChapterNumber: false)], taken);
        }
    }

    [Fact]
    public void ARequestIsWellFormedOnlyWithAnIdentifierAndALine()
    {
        Assert.True(new CaptionRequest("Figure", ": a", CaptionPlace.Above, false).IsValid);
        // The shape only: whether a label is new (strict) or the file's own is decided against the document when the request is run.
        Assert.True(new CaptionRequest("Équation", string.Empty, CaptionPlace.Above, false).IsValid);
        Assert.False(new CaptionRequest("two words", string.Empty, CaptionPlace.Above, false).IsValid);
        Assert.False(new CaptionRequest("Figure", "a\nb", CaptionPlace.Above, false).IsValid);
        Assert.False(new CaptionRequest("Figure", string.Empty, (CaptionPlace)7, false).IsValid);
    }

    [AvaloniaFact]
    public void AFileLabelOfAnotherLanguageIsAnsweredFromTheListButATypedOneMustBeAscii()
    {
        var (card, answers) = Show(Context(true, "Équation", "Équation"));
        using (card)
        {
            card.Click("PART_Ok");
            Assert.Equal([new CaptionRequest("Équation", string.Empty, CaptionPlace.Below, ChapterNumber: false)], answers);
        }

        var (typed, refused) = Show(Context());
        using (typed)
        {
            typed.Type("PART_NewLabel", "Équation");
            typed.Click("PART_Ok");
            Assert.Empty(refused);
            Assert.Equal(WriteStrings.CaptionProblemLabel, typed.Find<TextBlock>("PART_Hint").Text);
        }
    }
}
