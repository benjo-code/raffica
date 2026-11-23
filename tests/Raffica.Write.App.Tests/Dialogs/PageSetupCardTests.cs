using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Dialogs;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.WriteApp.Tests.Dialogs;

/// <summary>The Page setup card (milestone 4.4b, Task 12): its default scope, units by region, what it refuses and the parts it writes.</summary>
public sealed class PageSetupCardTests
{
    private static readonly SectionPageSetup A4 = new(11906, 16838, false, 1417, 1417, 1417, 1417, 1, 720, false);
    private static readonly SectionPageSetup Letter = new(12240, 15840, false, 1440, 1440, 1440, 1440, 1, 720, false);

    private static PageSetupContext Context(SectionPageSetup? current = null, ApplyScope scope = ApplyScope.WholeDocument, int sections = 1, bool unequal = false, bool inches = false) =>
        new(current ?? A4, scope, sections, unequal, inches);

    private static (CardWindow Window, List<PageSetupRequest?> Answers) Show(PageSetupContext context)
    {
        var answers = new List<PageSetupRequest?>();
        return (new CardWindow(PageSetupCard.Create(context, answers.Add)), answers);
    }

    private static void Choose(CardWindow card, string name, int index)
    {
        card.Find<SelectBox>(name).SelectedIndex = index;
        CardWindow.Pump();
    }

    [AvaloniaFact]
    public void TheDefaultScopeIsWholeDocumentWithOneSection()
    {
        var (card, answers) = Show(Context());
        using var _ = card;
        Assert.Equal(1, card.Find<SelectBox>("PART_Apply").SelectedIndex);
        Assert.Equal(["This section", "Whole document", "From here on"], card.Find<SelectBox>("PART_Apply").Items.Cast<string>().ToArray());
        Assert.Equal(WriteStrings.PageSetupTitle, card.Dialog.Title);

        // The fields show the section: A4, portrait, Normal margins, one column.
        Assert.Equal("A4", (string?)card.Find<SelectBox>("PART_Size").SelectedItem);
        Assert.Equal(0, card.Find<SelectBox>("PART_Orientation").SelectedIndex);
        Assert.Equal(0, card.Find<SelectBox>("PART_Margins").SelectedIndex);
        Assert.Equal("1", card.Find<TextBox>("PART_ColumnCount").Text);

        // Letter is only the size part, for the whole document.
        Choose(card, "PART_Size", 1);
        card.Click("PART_Ok");
        var request = Assert.Single(answers)!;
        Assert.Equal(ApplyScope.WholeDocument, request.Scope);
        Assert.Equal(PageSetupParts.Size, request.Change.Parts);
        Assert.Equal((12240, 15840), (request.Change.Value.WidthTwips, request.Change.Value.HeightTwips));
        Assert.False(request.Change.Value.Landscape);
    }

    [AvaloniaFact]
    public void ThisSectionWithSeveral()
    {
        var (card, answers) = Show(Context(scope: ApplyScope.ThisSection, sections: 3));
        using var _ = card;
        Assert.Equal(0, card.Find<SelectBox>("PART_Apply").SelectedIndex);
        Assert.False(card.Find<TextBlock>("PART_FromHereNote").IsVisible);

        Choose(card, "PART_Margins", 1);
        card.Click("PART_Ok");
        var request = Assert.Single(answers)!;
        Assert.Equal(ApplyScope.ThisSection, request.Scope);
        Assert.Equal(PageSetupParts.Margins, request.Change.Parts);
        Assert.Equal((720, 720, 720, 720), (request.Change.Value.TopTwips, request.Change.Value.BottomTwips, request.Change.Value.LeftTwips, request.Change.Value.RightTwips));
    }

    [AvaloniaFact]
    public void FromHereOnSaysItStartsANewPageAndTurnsTheFirstPageOff()
    {
        var (card, answers) = Show(Context(scope: ApplyScope.ThisSection, sections: 2));
        using var _ = card;
        var note = card.Find<TextBlock>("PART_FromHereNote");
        Assert.False(note.IsVisible);
        Choose(card, "PART_Apply", 2);
        Assert.True(note.IsVisible);
        Assert.Contains("starts a new page", note.Text);
        Assert.Contains("new columns start on the same page", note.Text);
        Assert.Contains("different first page", note.Text);

        card.Type("PART_ColumnCount", "2");
        card.Click("PART_Ok");
        var request = Assert.Single(answers)!;
        Assert.Equal(ApplyScope.FromHere, request.Scope);
        Assert.Equal(PageSetupParts.Columns, request.Change.Parts);
        Assert.Equal(2, request.Change.Value.Columns);

        // Opened on From here on, the note is visible from the start.
        var (open, _) = Show(Context(scope: ApplyScope.FromHere, sections: 2));
        using (open)
        {
            Assert.True(open.Find<TextBlock>("PART_FromHereNote").IsVisible);
        }
    }

    [AvaloniaFact]
    public void ValuesAreInInchesInTheUS()
    {
        var (card, answers) = Show(Context(Letter, inches: true));
        using var _ = card;
        Assert.Equal("Letter", (string?)card.Find<SelectBox>("PART_Size").SelectedItem);
        Assert.Equal("Normal", (string?)card.Find<SelectBox>("PART_Margins").SelectedItem);
        Assert.Equal("1", card.Find<TextBox>("PART_Top").Text);
        Assert.Equal("8.5", card.Find<TextBox>("PART_Width").Text);
        Assert.Equal("11", card.Find<TextBox>("PART_Height").Text);
        Assert.Contains("(in)", WriteStrings.Fill(WriteStrings.PsTopFormat, WriteStrings.PsUnitInch));
        Assert.Equal(WriteStrings.Fill(WriteStrings.PsTopFormat, WriteStrings.PsUnitInch), Avalonia.Automation.AutomationProperties.GetName(card.Find<TextBox>("PART_Top")));

        // A custom margin is typed in inches: 0.75 inch is 1080 twips, with or without the unit and with a comma.
        Choose(card, "PART_Margins", 4);
        Assert.True(card.Find<TextBox>("PART_Top").IsEnabled);
        card.Type("PART_Top", "0.75in");
        card.Type("PART_Bottom", "0,5");
        card.Type("PART_Left", "1.25");
        card.Type("PART_Right", "1");
        card.Click("PART_Ok");
        var value = Assert.Single(answers)!.Change.Value;
        Assert.Equal((1080, 720, 1800, 1440), (value.TopTwips, value.BottomTwips, value.LeftTwips, value.RightTwips));
    }

    [AvaloniaFact]
    public void CentimetresInTheRestOfTheWorld()
    {
        var (card, answers) = Show(Context(A4));
        using var _ = card;
        // The preset's 1417 twips read 2.5 and stay exact when the text is left alone.
        Assert.Equal("2.5", card.Find<TextBox>("PART_Top").Text);
        Assert.Equal("21", card.Find<TextBox>("PART_Width").Text);
        Choose(card, "PART_Margins", 4);
        card.Type("PART_Top", "2");
        card.Click("PART_Ok");
        var request = Assert.Single(answers)!;
        Assert.Equal(PageSetupParts.Margins, request.Change.Parts);
        Assert.Equal((1134, 1417, 1417, 1417), (request.Change.Value.TopTwips, request.Change.Value.BottomTwips, request.Change.Value.LeftTwips, request.Change.Value.RightTwips));
    }

    [AvaloniaFact]
    public void ACustomSizeAndOrientationAreWrittenTogether()
    {
        var (card, answers) = Show(Context());
        using var _ = card;
        Choose(card, "PART_Size", 5);
        Assert.True(card.Find<TextBox>("PART_Width").IsEnabled);
        card.Type("PART_Width", "20");
        card.Type("PART_Height", "30");
        Choose(card, "PART_Orientation", 1);
        card.Click("PART_Ok");
        var request = Assert.Single(answers)!;
        Assert.Equal(PageSetupParts.Size | PageSetupParts.Orientation, request.Change.Parts);
        Assert.True(request.Change.Value.Landscape);
        Assert.Equal((17008, 11339), (request.Change.Value.WidthTwips, request.Change.Value.HeightTwips));
    }

    [AvaloniaFact]
    public void OrientationAloneTurnsThePage()
    {
        var (card, answers) = Show(Context());
        using var _ = card;
        Choose(card, "PART_Orientation", 1);
        card.Click("PART_Ok");
        var request = Assert.Single(answers)!;
        Assert.Equal(PageSetupParts.Orientation, request.Change.Parts);
        Assert.Equal((16838, 11906, true), (request.Change.Value.WidthTwips, request.Change.Value.HeightTwips, request.Change.Value.Landscape));

        // A landscape section shows its upright size: A4 stays A4.
        var (landscape, _) = Show(Context(A4.WithLandscape(true)));
        using (landscape)
        {
            Assert.Equal("A4", (string?)landscape.Find<SelectBox>("PART_Size").SelectedItem);
            Assert.Equal(1, landscape.Find<SelectBox>("PART_Orientation").SelectedIndex);
        }
    }

    [AvaloniaFact]
    public void TheColumnFieldsWriteTheColumnsPart()
    {
        var (card, answers) = Show(Context());
        using var _ = card;
        card.Type("PART_ColumnCount", "3");
        card.Type("PART_ColumnSpacing", "1");
        card.Find<CheckBox>("PART_ColumnLine").IsChecked = true;
        card.Click("PART_Ok");
        var value = Assert.Single(answers)!.Change;
        Assert.Equal(PageSetupParts.Columns, value.Parts);
        Assert.Equal((3, 567, true), (value.Value.Columns, value.Value.ColumnSpaceTwips, value.Value.ColumnLine));
    }

    [AvaloniaFact]
    public void ASpacingOrLineChangeOfUnequalColumnsIsNotOffered()
    {
        var unequal = A4 with { Columns = 2 };
        var (card, answers) = Show(Context(unequal, unequal: true));
        using var _ = card;

        // The spacing and the line are off (a write would make the columns equal), the note says why, the count is still on offer.
        Assert.False(card.Find<TextBox>("PART_ColumnSpacing").IsEnabled);
        Assert.False(card.Find<CheckBox>("PART_ColumnLine").IsEnabled);
        Assert.True(card.Find<TextBox>("PART_ColumnCount").IsEnabled);
        Assert.True(card.Find<TextBlock>("PART_ColumnsNote").IsVisible);
        Assert.Equal(WriteStrings.PsColumnsUnequal, card.Find<TextBlock>("PART_ColumnsNote").Text);

        // Nothing else changed: the card closes with no answer and no Columns part is written.
        card.Click("PART_Ok");
        Assert.Equal([null], answers);

        // Another margin leaves the columns alone.
        var (other, otherAnswers) = Show(Context(unequal, unequal: true));
        using (other)
        {
            Choose(other, "PART_Margins", 1);
            other.Click("PART_Ok");
            Assert.Equal(PageSetupParts.Margins, Assert.Single(otherAnswers)!.Change.Parts);
        }

        // A new count makes them equal and is written.
        var (count, countAnswers) = Show(Context(unequal, unequal: true));
        using (count)
        {
            count.Type("PART_ColumnCount", "3");
            count.Click("PART_Ok");
            var change = Assert.Single(countAnswers)!.Change;
            Assert.Equal(PageSetupParts.Columns, change.Parts);
            Assert.Equal(3, change.Value.Columns);
        }

        // Equal columns have no note and every field.
        var (equal, _) = Show(Context());
        using (equal)
        {
            Assert.False(equal.Find<TextBlock>("PART_ColumnsNote").IsVisible);
            Assert.True(equal.Find<TextBox>("PART_ColumnSpacing").IsEnabled);
        }
    }

    [AvaloniaFact]
    public void TheNoteNamesTheSectionsWhoseCustomColumnWidthsAColumnChangeWouldMakeEqual()
    {
        // The caret is in section 0 (equal columns); section 1 has its own widths. A column change at Whole document or From here on reaches it; This section does not.
        PageSetupContext Make(ApplyScope scope, params int[] unequal) => new(A4, scope, 3, false, false, 0, unequal);

        var (whole, _) = Show(Make(ApplyScope.WholeDocument, 1));
        using (whole)
        {
            var note = whole.Find<TextBlock>("PART_EqualizeNote");
            Assert.False(note.IsVisible);
            whole.Type("PART_ColumnCount", "2");
            Assert.True(note.IsVisible);
            Assert.Equal("The custom column widths of 1 section will be made equal.", note.Text);

            // The scope is read live: This section reaches no other section, From here on reaches section 1.
            Choose(whole, "PART_Apply", 0);
            Assert.False(note.IsVisible);
            Choose(whole, "PART_Apply", 2);
            Assert.True(note.IsVisible);

            // Back to the old count: nothing is written, so nothing is made equal.
            whole.Type("PART_ColumnCount", "1");
            Assert.False(note.IsVisible);
        }

        var (several, _) = Show(Make(ApplyScope.WholeDocument, 1, 2));
        using (several)
        {
            several.Find<CheckBox>("PART_ColumnLine").IsChecked = true;
            CardWindow.Pump();
            Assert.Equal("The custom column widths of 2 sections will be made equal.", several.Find<TextBlock>("PART_EqualizeNote").Text);
        }

        // Only a section behind the caret is outside From here on: here section 0 is the unequal one and the caret is in section 1.
        var (behind, _) = Show(new PageSetupContext(A4, ApplyScope.FromHere, 3, false, false, 1, [0]));
        using (behind)
        {
            behind.Type("PART_ColumnCount", "2");
            Assert.False(behind.Find<TextBlock>("PART_EqualizeNote").IsVisible);
        }

        // This section with equal columns of its own and an unequal section elsewhere: no note.
        var (own, _) = Show(Make(ApplyScope.ThisSection, 2));
        using (own)
        {
            own.Type("PART_ColumnCount", "3");
            Assert.False(own.Find<TextBlock>("PART_EqualizeNote").IsVisible);
        }

        // The caret's own section has custom widths and the scope reaches it: the same words, now with its count.
        var (caret, _) = Show(new PageSetupContext(A4 with { Columns = 2 }, ApplyScope.WholeDocument, 2, true, false, 0, [0]));
        using (caret)
        {
            caret.Type("PART_ColumnCount", "3");
            Assert.Equal("The custom column widths of 1 section will be made equal.", caret.Find<TextBlock>("PART_EqualizeNote").Text);
        }
    }

    [AvaloniaFact]
    public void ASizeWithinOneTwipOfThePageIsNotAChange()
    {
        var almost = A4 with { WidthTwips = 11905, HeightTwips = 16837 };
        var (card, answers) = Show(Context(almost));
        using var _ = card;
        Assert.Equal("A4", (string?)card.Find<SelectBox>("PART_Size").SelectedItem);
        card.Click("PART_Ok");
        Assert.Equal([null], answers);

        // A custom size that is the same within a twip is not a change either.
        var (custom, customAnswers) = Show(Context(almost));
        using (custom)
        {
            Choose(custom, "PART_Size", 5);
            custom.Click("PART_Ok");
            Assert.Equal([null], customAnswers);
        }
    }

    [AvaloniaFact]
    public void MarginsThatAlreadyLeaveNoRoomDoNotBlockAnotherChange()
    {
        // A foreign file: 6000 twips at each side of an A4 page leaves 0 text width. Another change must still go through; only a change of the margins is checked.
        var squeezed = A4 with { LeftTwips = 6000, RightTwips = 6000 };
        var (card, answers) = Show(Context(squeezed));
        using var _ = card;
        Assert.Equal(4, card.Find<SelectBox>("PART_Margins").SelectedIndex);
        Choose(card, "PART_Orientation", 1);
        card.Click("PART_Ok");
        var request = Assert.Single(answers)!;
        Assert.Equal(PageSetupParts.Orientation, request.Change.Parts);
        Assert.Equal((6000, 6000), (request.Change.Value.LeftTwips, request.Change.Value.RightTwips));

        // Touching the margins is checked.
        var (touched, touchedAnswers) = Show(Context(squeezed));
        using (touched)
        {
            touched.Type("PART_Left", "10");
            touched.Click("PART_Ok");
            Assert.Empty(touchedAnswers);
            Assert.Equal(WriteStrings.PsNoRoomForText, touched.Find<TextBlock>("PART_Hint").Text);
        }
    }

    [AvaloniaFact]
    public void TheGermanFromHereNoteIsAProperSentence()
    {
        Assert.StartsWith("Ab hier beginnt eine neue Seite", WriteStrings.GetIn("PsFromHereNote", CultureInfo.GetCultureInfo("de")));
        Assert.DoesNotContain("hier beginnt hier", WriteStrings.GetIn("PsFromHereNote", CultureInfo.GetCultureInfo("de")));
    }

    [AvaloniaFact]
    public void NothingChangedClosesWithoutAnAnswerAndEscapeCancels()
    {
        var (card, answers) = Show(Context());
        using (card)
        {
            card.Click("PART_Ok");
            Assert.Equal([null], answers);
        }

        var (second, escaped) = Show(Context());
        using (second)
        {
            second.Press(second.Find<TextBox>("PART_ColumnCount"), PhysicalKey.Escape, Key.Escape);
            Assert.Equal([null], escaped);
        }

        var (third, entered) = Show(Context());
        using (third)
        {
            Choose(third, "PART_Size", 2);
            third.Press(third.Find<TextBox>("PART_ColumnCount"), PhysicalKey.Enter, Key.Enter);
            Assert.Equal(PageSetupParts.Size, Assert.Single(entered)!.Change.Parts);
        }
    }

    [AvaloniaTheory]
    [InlineData("PART_Width", "abc")]
    [InlineData("PART_Width", "0.1")]
    [InlineData("PART_Height", "99")]
    [InlineData("PART_Height", "")]
    public void AnInvalidSizeIsRefused(string field, string text)
    {
        var (card, answers) = Show(Context());
        using var _ = card;
        Choose(card, "PART_Size", 5);
        card.Type(field, text);
        card.Click("PART_Ok");
        Assert.Empty(answers);
        Assert.True(card.Find<TextBlock>("PART_Hint").IsVisible);
        Assert.False(string.IsNullOrWhiteSpace(card.Find<TextBlock>("PART_Hint").Text));
    }

    [AvaloniaFact]
    public void AnInvalidValueIsRefused()
    {
        var cases = new (string Field, string Text, string Expected)[]
        {
            ("PART_Top", "-1", "Top (cm): enter a number, for example 2.5."),
            ("PART_Top", "x", "Top (cm): enter a number, for example 2.5."),
            ("PART_Left", "1e3", "Left (cm): enter a number, for example 2.5."),
            ("PART_Right", "99999", "Right (cm): this value does not fit the page."),
            ("PART_ColumnSpacing", "abc", "Spacing (cm): enter a number, for example 2.5."),
            ("PART_ColumnCount", "0", "Number of columns: enter a whole number from 1 to 45."),
            ("PART_ColumnCount", "46", "Number of columns: enter a whole number from 1 to 45."),
            ("PART_ColumnCount", "two", "Number of columns: enter a whole number from 1 to 45."),
        };
        foreach (var (field, text, expected) in cases)
        {
            var (card, answers) = Show(Context());
            using var _ = card;
            Choose(card, "PART_Margins", 4);
            card.Type(field, text);
            card.Click("PART_Ok");
            Assert.Empty(answers);
            var hint = card.Find<TextBlock>("PART_Hint");
            Assert.True(hint.IsVisible, field + " " + text);
            Assert.Equal(expected, hint.Text);
        }
    }

    [AvaloniaFact]
    public void MarginsThatLeaveNoRoomAndColumnsThatDoNotFitAreRefused()
    {
        var (card, answers) = Show(Context());
        using var _ = card;
        Choose(card, "PART_Margins", 4);
        card.Type("PART_Left", "10");
        card.Type("PART_Right", "10");
        card.Click("PART_Ok");
        Assert.Empty(answers);
        Assert.Equal(WriteStrings.PsNoRoomForText, card.Find<TextBlock>("PART_Hint").Text);

        card.Type("PART_Left", "2.5");
        card.Type("PART_Right", "2.5");
        card.Type("PART_Top", "20");
        card.Type("PART_Bottom", "9");
        card.Click("PART_Ok");
        Assert.Empty(answers);
        Assert.Equal(WriteStrings.PsNoRoomForText, card.Find<TextBlock>("PART_Hint").Text);

        card.Type("PART_Top", "2.5");
        card.Type("PART_Bottom", "2.5");
        card.Type("PART_ColumnCount", "40");
        card.Click("PART_Ok");
        Assert.Empty(answers);
        Assert.Equal(WriteStrings.PsColumnsDoNotFit, card.Find<TextBlock>("PART_Hint").Text);
    }

    [AvaloniaFact]
    public void TheHintIsALiveRegionInTheDangerColourAndTheFooterIsOneOkAndCancel()
    {
        var (card, _) = Show(Context());
        using var _ = card;
        var hint = card.Find<TextBlock>("PART_Hint");
        Assert.Equal(Avalonia.Automation.AutomationLiveSetting.Assertive, Avalonia.Automation.AutomationProperties.GetLiveSetting(hint));
        var buttons = Assert.IsType<StackPanel>(card.Dialog.Footer).Children.OfType<Button>().ToList();
        Assert.Equal(["PART_Ok", "PART_Cancel"], buttons.Select(b => b.Name ?? string.Empty).ToArray());
        Assert.True(buttons[0].IsDefault);
        Assert.True(buttons[1].IsCancel);

        // Every field has a name for a screen reader.
        foreach (var control in card.Dialog.GetVisualDescendants().OfType<Control>().Where(c => c is TextBox or SelectBox or CheckBox && c.Name?.StartsWith("PART_", StringComparison.Ordinal) == true))
        {
            Assert.False(string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(control)) && control is not CheckBox, control.Name);
        }
    }

    [AvaloniaFact]
    public void TheGermanCardFitsItsWidthAndShowsItsUnitsAndNotes()
    {
        var saved = WriteStrings.Culture;
        try
        {
            WriteStrings.Culture = CultureInfo.GetCultureInfo("de");
            var (card, _) = Show(Context(A4 with { Columns = 2 }, ApplyScope.FromHere, sections: 3, unequal: true));
            using var _ = card;
            var texts = card.Texts();
            Assert.Contains("Seite einrichten", card.Dialog.Title);
            Assert.Contains(WriteStrings.PsColumnsUnequal, texts);
            Assert.Contains(WriteStrings.PsFromHereNote, texts);
            Assert.Equal("2,5", card.Find<TextBox>("PART_Top").Text);
            Assert.Contains("Breite (cm)", card.Texts());

            // The longest strings wrap inside the width the card has (640), and nothing reaches past the card.
            card.Dialog.Measure(new Avalonia.Size(640, double.PositiveInfinity));
            Assert.True(card.Dialog.DesiredSize.Width <= 640, card.Dialog.DesiredSize.ToString());
            Assert.All(card.Dialog.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible), t => Assert.True(t.Bounds.Right <= card.Dialog.Bounds.Width + 0.5, t.Text));

            // The decimal comma is read in the German card.
            Assert.True(PageSetupCard.Length.TryParse("1,5", false, out var twips));
            Assert.Equal(850, twips);
        }
        finally
        {
            WriteStrings.Culture = saved;
        }
    }

    [AvaloniaFact]
    public void ALengthIsReadFromAnyOfItsSpellingsAndNothingElse()
    {
        Assert.True(PageSetupCard.Length.TryParse("2.5", false, out var a));
        Assert.Equal(1417, a);
        Assert.True(PageSetupCard.Length.TryParse("2.5 cm", true, out var b));
        Assert.Equal(1417, b);
        Assert.True(PageSetupCard.Length.TryParse("1\"", false, out var c));
        Assert.Equal(1440, c);
        Assert.True(PageSetupCard.Length.TryParse("0", false, out var zero));
        Assert.Equal(0, zero);
        foreach (var bad in new[] { string.Empty, "-1", "1e3", "NaN", "Infinity", "1.2.3", "99999999999999", "cm", "1 mm" })
        {
            Assert.False(PageSetupCard.Length.TryParse(bad, false, out _), bad);
        }
    }
}
