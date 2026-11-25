using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Raffica.Shell.Controls;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Dialogs;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.WriteApp.Tests.Dialogs;

/// <summary>The two picture cards (milestone 4.5a, Task 12): Insert picture (one optional description) and Picture properties (size, proportions, original size, description, wrap).</summary>
public sealed class PictureCardsTests
{
    // 5 cm by 2.5 cm.
    private static PicturePropertiesContext Context(string alt = "A cat", string wrap = "inline", bool rotated = false, bool canFloat = true, bool inches = false) =>
        new(2835, 1417, alt, wrap, rotated, canFloat, inches);

    private static (CardWindow Window, List<string?> Answers) ShowInsert()
    {
        var answers = new List<string?>();
        return (new CardWindow(PictureInsertCard.Create(answers.Add)), answers);
    }

    private static (CardWindow Window, List<PictureProperties?> Answers) ShowProperties(PicturePropertiesContext? context = null)
    {
        var answers = new List<PictureProperties?>();
        return (new CardWindow(PicturePropertiesCard.Create(context ?? Context(), answers.Add)), answers);
    }

    private static void Leave(CardWindow card, string name)
    {
        // Focus goes elsewhere, as a Tab key would take it: the box loses it for real.
        card.Find<TextBox>(name).Focus();
        CardWindow.Pump();
        card.Find<TextBox>("PART_Alt").Focus();
        CardWindow.Pump();
    }

    // ---- Insert picture ------------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void TheInsertCardTakesAnEmptyDescriptionAndAnswersOnce()
    {
        var (card, answers) = ShowInsert();
        using var _ = card;
        Assert.Equal(WriteStrings.PiTitle, card.Dialog.Title);
        Assert.Equal(string.Empty, card.Find<TextBox>("PART_Alt").Text ?? string.Empty);

        card.Click("PART_Ok");
        card.Click("PART_Ok");
        Assert.Equal([string.Empty], answers);
    }

    [AvaloniaFact]
    public void TheInsertCardGivesTheDescriptionAndEnterConfirms()
    {
        var (card, answers) = ShowInsert();
        using var _ = card;
        card.Type("PART_Alt", "A green square");
        card.Press(card.Find<TextBox>("PART_Alt"), PhysicalKey.Enter, Key.Enter);
        Assert.Equal(["A green square"], answers);
    }

    [AvaloniaFact]
    public void ADescriptionThatIsTooLongKeepsTheCardOpenWithAHint()
    {
        var (card, answers) = ShowInsert();
        using var _ = card;
        card.Type("PART_Alt", new string('x', PictureRules.MaxAltTextLength + 1));
        card.Click("PART_Ok");
        Assert.Empty(answers);
        Assert.True(card.Find<TextBlock>("PART_Hint").IsVisible);
        Assert.Equal(WriteStrings.PiAltTooLong, card.Find<TextBlock>("PART_Hint").Text);

        card.Type("PART_Alt", new string('x', PictureRules.MaxAltTextLength));
        card.Click("PART_Ok");
        Assert.Single(answers);
    }

    [AvaloniaFact]
    public void CancelAndEscapeInsertNothing()
    {
        var (card, answers) = ShowInsert();
        using (card)
        {
            card.Type("PART_Alt", "text");
            card.Click("PART_Cancel");
            Assert.Equal([null], answers);
        }

        var (second, escaped) = ShowInsert();
        using (second)
        {
            second.Press(second.Find<TextBox>("PART_Alt"), PhysicalKey.Escape, Key.Escape);
            Assert.Equal([null], escaped);
        }
    }

    // ---- Picture properties --------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void ThePropertiesCardShowsThePictureAsItIsAndNothingChangedGivesNoAnswer()
    {
        var (card, answers) = ShowProperties();
        using var _ = card;
        Assert.Equal(WriteStrings.PpTitle, card.Dialog.Title);
        Assert.Equal("5", card.Find<TextBox>("PART_Width").Text);
        Assert.Equal("2.5", card.Find<TextBox>("PART_Height").Text);
        Assert.Equal("A cat", card.Find<TextBox>("PART_Alt").Text);
        Assert.True(card.Find<CheckBox>("PART_LockRatio").IsChecked);
        Assert.False(card.Find<CheckBox>("PART_Reset").IsChecked);
        Assert.Equal(0, card.Find<SelectBox>("PART_Wrap").SelectedIndex);

        card.Click("PART_Ok");
        Assert.Equal([null], answers);
    }

    [AvaloniaFact]
    public void AWidthKeepsTheProportionsWhileTheyAreLockedAndOnlyTheChangedPartsComeBack()
    {
        var (card, answers) = ShowProperties();
        using var _ = card;
        card.Type("PART_Width", "10");
        Leave(card, "PART_Width");
        Assert.Equal("5", card.Find<TextBox>("PART_Height").Text);

        card.Click("PART_Ok");
        var answer = Assert.Single(answers)!;
        Assert.Equal(5669L * 635, answer.WidthEmu);
        Assert.Equal(2834L * 635, answer.HeightEmu);
        Assert.False(answer.ResetSize);
        Assert.Null(answer.AltText);
        Assert.Null(answer.Wrap);
    }

    [AvaloniaFact]
    public void WithTheProportionsFreeTheHeightStaysWhereItIs()
    {
        var (card, answers) = ShowProperties();
        using var _ = card;
        card.Find<CheckBox>("PART_LockRatio").IsChecked = false;
        card.Type("PART_Width", "10");
        Leave(card, "PART_Width");
        Assert.Equal("2.5", card.Find<TextBox>("PART_Height").Text);
        card.Click("PART_Ok");
        var answer = Assert.Single(answers)!;
        Assert.Equal(5669L * 635, answer.WidthEmu);
        Assert.Equal(1417L * 635, answer.HeightEmu);
    }

    [AvaloniaFact]
    public void EnterWithoutLeavingTheBoxStillKeepsTheProportions()
    {
        var (card, answers) = ShowProperties();
        using var _ = card;
        card.Type("PART_Width", "10");
        card.Press(card.Find<TextBox>("PART_Width"), PhysicalKey.Enter, Key.Enter);

        // The box was never left (no blur), and the picture is still not distorted: the height followed the width at OK.
        var answer = Assert.Single(answers)!;
        Assert.Equal(5669L * 635, answer.WidthEmu);
        Assert.Equal(2834L * 635, answer.HeightEmu);
        Assert.Equal("5", card.Find<TextBox>("PART_Height").Text);
    }

    [AvaloniaFact]
    public void TypingAWidthAndTypingTheOldOneBackGivesTheOriginalPair()
    {
        var (card, answers) = ShowProperties(new PicturePropertiesContext(2880, 1440, string.Empty, "inline", false, true, false));
        using var _ = card;
        card.Type("PART_Width", "5.29");
        Leave(card, "PART_Width");
        Assert.NotEqual("2.54", card.Find<TextBox>("PART_Height").Text);

        card.Type("PART_Width", "5.08");
        Leave(card, "PART_Width");
        Assert.Equal("2.54", card.Find<TextBox>("PART_Height").Text);
        card.Click("PART_Ok");

        // 5.08 cm is 2880 twips again: the sizes are the original pair, so nothing is written.
        Assert.Equal([null], answers);
    }

    [AvaloniaFact]
    public void TheSideEditedLastDecidesAndTheLockFollowsWhenItIsSwitchedOn()
    {
        var (card, answers) = ShowProperties(new PicturePropertiesContext(2880, 1440, string.Empty, "inline", false, true, false));
        using var _ = card;

        // The height first, then the width: the width is the last word, the height is derived from it.
        card.Type("PART_Height", "4");
        card.Type("PART_Width", "10.16");
        card.Press(card.Find<TextBox>("PART_Width"), PhysicalKey.Enter, Key.Enter);
        var answer = Assert.Single(answers)!;
        Assert.Equal(5760L * 635, answer.WidthEmu);
        Assert.Equal(2880L * 635, answer.HeightEmu);
    }

    [AvaloniaFact]
    public void TheHeightEditedLastDrivesTheWidthAndSwitchingTheLockOnFollowsAtOnce()
    {
        var (card, answers) = ShowProperties(new PicturePropertiesContext(2880, 1440, string.Empty, "inline", false, true, false));
        using var _ = card;
        card.Find<CheckBox>("PART_LockRatio").IsChecked = false;
        card.Type("PART_Width", "10.16");
        Assert.Equal("2.54", card.Find<TextBox>("PART_Height").Text);

        // Locked again: the width was edited last, so the height follows it now, before OK.
        card.Find<CheckBox>("PART_LockRatio").IsChecked = true;
        CardWindow.Pump();
        Assert.Equal("5.08", card.Find<TextBox>("PART_Height").Text);
        card.Type("PART_Height", "1.27");
        card.Press(card.Find<TextBox>("PART_Height"), PhysicalKey.Enter, Key.Enter);
        var answer = Assert.Single(answers)!;
        Assert.Equal(1440L * 635, answer.WidthEmu);
        Assert.Equal(720L * 635, answer.HeightEmu);
    }

    [AvaloniaFact]
    public void WithTheLockOffBothSidesAreWrittenAsTyped()
    {
        var (card, answers) = ShowProperties(new PicturePropertiesContext(2880, 1440, string.Empty, "inline", false, true, false));
        using var _ = card;
        card.Find<CheckBox>("PART_LockRatio").IsChecked = false;
        card.Type("PART_Width", "10.16");
        card.Type("PART_Height", "1.27");
        card.Press(card.Find<TextBox>("PART_Height"), PhysicalKey.Enter, Key.Enter);
        var answer = Assert.Single(answers)!;
        Assert.Equal(5760L * 635, answer.WidthEmu);
        Assert.Equal(720L * 635, answer.HeightEmu);
    }

    [AvaloniaFact]
    public void TheDescriptionAndTheWrapAreGivenOnlyWhenChanged()
    {
        var (card, answers) = ShowProperties();
        using var _ = card;
        card.Type("PART_Alt", string.Empty);
        card.Find<SelectBox>("PART_Wrap").SelectedIndex = PicturePayloads.Wraps.ToList().IndexOf("topAndBottom");
        CardWindow.Pump();
        card.Click("PART_Ok");
        var answer = Assert.Single(answers)!;
        Assert.Null(answer.WidthEmu);
        Assert.Null(answer.HeightEmu);
        Assert.Equal(string.Empty, answer.AltText);
        Assert.Equal(PictureWrap.TopAndBottom, answer.Wrap);
    }

    [AvaloniaFact]
    public void TheOriginalSizeSwitchAsksForTheResetAndTakesTheSizeFieldsOutOfPlay()
    {
        var (card, answers) = ShowProperties();
        using var _ = card;
        card.Type("PART_Width", "10");
        card.Find<CheckBox>("PART_Reset").IsChecked = true;
        CardWindow.Pump();
        Assert.False(card.Find<TextBox>("PART_Width").IsEnabled);
        Assert.False(card.Find<TextBox>("PART_Height").IsEnabled);
        Assert.False(card.Find<CheckBox>("PART_LockRatio").IsEnabled);

        card.Click("PART_Ok");
        var answer = Assert.Single(answers)!;
        Assert.True(answer.ResetSize);
        Assert.Null(answer.WidthEmu);
        Assert.Null(answer.HeightEmu);
    }

    [AvaloniaFact]
    public void ATurnedPictureOffersNoSizeAndAPictureInACellNoFloatingWrap()
    {
        var (card, answers) = ShowProperties(Context(rotated: true, canFloat: false));
        using var _ = card;
        Assert.False(card.Find<TextBox>("PART_Width").IsEnabled);
        Assert.False(card.Find<TextBox>("PART_Height").IsEnabled);
        Assert.False(card.Find<CheckBox>("PART_Reset").IsEnabled);
        Assert.False(card.Find<SelectBox>("PART_Wrap").IsEnabled);
        card.Type("PART_Alt", "Turned");
        card.Click("PART_Ok");
        var answer = Assert.Single(answers)!;
        Assert.Equal("Turned", answer.AltText);
        Assert.Null(answer.WidthEmu);
        Assert.False(answer.ResetSize);
        Assert.Null(answer.Wrap);
    }

    [AvaloniaFact]
    public void AWrongEntryShowsAHintAndKeepsTheCardOpen()
    {
        var (card, answers) = ShowProperties();
        using var _ = card;
        card.Type("PART_Width", "wide");
        card.Click("PART_Ok");
        Assert.Empty(answers);
        Assert.True(card.Find<TextBlock>("PART_Hint").IsVisible);

        card.Find<CheckBox>("PART_LockRatio").IsChecked = false;
        card.Type("PART_Width", "0.01");
        card.Click("PART_Ok");
        Assert.Empty(answers);
        Assert.StartsWith(WriteStrings.Fill(WriteStrings.PpWidthFormat, WriteStrings.PsUnitCm), card.Find<TextBlock>("PART_Hint").Text ?? string.Empty, StringComparison.Ordinal);

        card.Type("PART_Width", "999");
        card.Click("PART_Ok");
        Assert.Empty(answers);

        card.Type("PART_Width", "5");
        card.Type("PART_Alt", new string('y', PictureRules.MaxAltTextLength + 1));
        card.Click("PART_Ok");
        Assert.Empty(answers);
        Assert.Equal(WriteStrings.PiAltTooLong, card.Find<TextBlock>("PART_Hint").Text);

        card.Click("PART_Cancel");
        Assert.Equal([null], answers);
    }

    [AvaloniaFact]
    public void LengthsAreInInchesWhenTheRegionSaysSo()
    {
        var (card, answers) = ShowProperties(new PicturePropertiesContext(2880, 1440, string.Empty, "square", false, true, true));
        using var _ = card;
        Assert.Equal("2", card.Find<TextBox>("PART_Width").Text);
        Assert.Equal("1", card.Find<TextBox>("PART_Height").Text);
        Assert.Equal(PicturePayloads.Wraps.ToList().IndexOf("square"), card.Find<SelectBox>("PART_Wrap").SelectedIndex);
        card.Type("PART_Width", "3");
        Leave(card, "PART_Width");
        Assert.Equal("1.5", card.Find<TextBox>("PART_Height").Text);
        card.Click("PART_Ok");
        Assert.Equal(4320L * 635, Assert.Single(answers)!.WidthEmu);
    }

    [AvaloniaFact]
    public void TheCardsAreBuiltByCardFooterWithTheirSafeAnswerFirstAndCancelLast()
    {
        foreach (var (key, make) in CardFooterTests.Entries().Where(e => e.Key.StartsWith("Picture", StringComparison.Ordinal)))
        {
            var footer = Assert.IsType<StackPanel>(make().Footer);
            var buttons = footer.Children.OfType<Button>().ToList();
            Assert.Equal(CardFooterTests.DefaultButtons[key], Assert.Single(buttons, b => b.IsDefault).Name);
            Assert.Same(buttons[0], Assert.Single(buttons, b => b.IsDefault));
            Assert.Same(buttons[^1], Assert.Single(buttons, b => b.IsCancel));
            Assert.Equal(Raffica.Shell.Controls.CardFooter.Spacing, footer.Spacing);
        }

        Assert.Equal(2, CardFooterTests.Entries().Count(e => e.Key.StartsWith("Picture", StringComparison.Ordinal)));
    }
}
