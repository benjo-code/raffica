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
using Xunit;

namespace Raffica.WriteApp.Tests.Dialogs;

/// <summary>The Number pages from here card (milestone 4.4b, Task 12): its defaults, what it refuses, the lines about the place and the sections that follow.</summary>
public sealed class NumberPagesCardTests
{
    private static SectionFacts Facts(int section = 0, bool atStart = false, int following = 0, int own = 0) =>
        new(true, section, atStart, following, own, SectionReason.None);

    private static (CardWindow Window, List<PageNumbering?> Answers) Show(NumberPagesContext context)
    {
        var answers = new List<PageNumbering?>();
        return (new CardWindow(NumberPagesCard.Create(context, answers.Add)), answers);
    }

    [AvaloniaFact]
    public void TheDefaultIsStartAt1Decimal()
    {
        var (card, answers) = Show(new NumberPagesContext(Facts(), false));
        using var _ = card;
        Assert.Equal("1", card.Find<TextBox>("PART_Start").Text);
        Assert.Equal(0, card.Find<SelectBox>("PART_Format").SelectedIndex);
        Assert.Equal(string.Empty, card.Find<TextBox>("PART_Prefix").Text ?? string.Empty);
        Assert.False(card.Find<CheckBox>("PART_Continue").IsChecked);
        Assert.Equal(WriteStrings.NumPagesTitle, card.Dialog.Title);

        card.Click("PART_Ok");
        Assert.Equal([new PageNumbering(1, PageNumberFormat.Arabic, string.Empty)], answers);

        // The answer is given once.
        card.Click("PART_Cancel");
        Assert.Single(answers);
    }

    [AvaloniaFact]
    public void EveryFormatAndThePrefixAreAnswered()
    {
        var formats = new (int Index, PageNumberFormat Format)[]
        {
            (0, PageNumberFormat.Arabic), (1, PageNumberFormat.LowerRoman), (2, PageNumberFormat.UpperRoman), (3, PageNumberFormat.LowerLetter), (4, PageNumberFormat.UpperLetter),
        };
        var box = Show(new NumberPagesContext(Facts(), false)).Window;
        using (box)
        {
            Assert.Equal(["1 2 3", "i ii iii", "I II III", "a b c", "A B C"], box.Find<SelectBox>("PART_Format").Items.Cast<string>().ToArray());
        }

        foreach (var (index, format) in formats)
        {
            var (card, answers) = Show(new NumberPagesContext(Facts(), false));
            using var _ = card;
            card.Type("PART_Start", "7");
            card.Type("PART_Prefix", "A-");
            card.Find<SelectBox>("PART_Format").SelectedIndex = index;
            card.Click("PART_Ok");
            Assert.Equal([new PageNumbering(7, format, "A-")], answers);
        }
    }

    [AvaloniaFact]
    public void ContinueGivesNoStartAndTurnsTheStartFieldOff()
    {
        var (card, answers) = Show(new NumberPagesContext(Facts(), false));
        using var _ = card;
        card.Find<CheckBox>("PART_Continue").IsChecked = true;
        CardWindow.Pump();
        Assert.False(card.Find<TextBox>("PART_Start").IsEnabled);

        // A start that would be wrong is not read while the count continues.
        card.Type("PART_Start", "wrong");
        card.Click("PART_Ok");
        Assert.Equal([new PageNumbering(null, PageNumberFormat.Arabic, string.Empty)], answers);
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("100000")]
    [InlineData("99999999999")]
    public void AnInvalidStartIsRefusedWithAHint(string start)
    {
        var (card, answers) = Show(new NumberPagesContext(Facts(), false));
        using var _ = card;
        var hint = card.Find<TextBlock>("PART_Hint");
        Assert.False(hint.IsVisible);
        card.Type("PART_Start", start);
        card.Click("PART_Ok");

        // The card stays open, the hint is visible and says what is allowed; nothing was answered.
        Assert.Empty(answers);
        Assert.True(hint.IsVisible);
        Assert.Equal(WriteStrings.NumPagesInvalidStart, hint.Text);

        // The edge values are right.
        card.Type("PART_Start", "0");
        card.Click("PART_Ok");
        Assert.Equal(0, Assert.Single(answers)!.Start);
    }

    [AvaloniaFact]
    public void ThePageNumberTopEdgeIsAccepted()
    {
        var (card, answers) = Show(new NumberPagesContext(Facts(), false));
        using var _ = card;
        card.Type("PART_Start", "99999");
        card.Click("PART_Ok");
        Assert.Equal(99999, Assert.Single(answers)!.Start);
    }

    [AvaloniaTheory]
    [InlineData("\u0001")]
    [InlineData("A\tB")]
    [InlineData("123456789012345678901234567890123")]
    public void AnInvalidPrefixIsRefusedWithAHintBeforeTheEditorCould(string prefix)
    {
        Assert.False(PageNumbering.IsValidPrefix(prefix));
        var (card, answers) = Show(new NumberPagesContext(Facts(), false));
        using var _ = card;
        card.Type("PART_Prefix", prefix);
        card.Click("PART_Ok");
        Assert.Empty(answers);
        var hint = card.Find<TextBlock>("PART_Hint");
        Assert.True(hint.IsVisible);
        Assert.Equal(WriteStrings.NumPagesInvalidPrefix, hint.Text);

        // 32 characters are fine.
        card.Type("PART_Prefix", new string('x', 32));
        card.Click("PART_Ok");
        Assert.Equal(32, Assert.Single(answers)!.Prefix.Length);
    }

    [AvaloniaFact]
    public void ALoneSurrogateIsRefusedAndAValidPairStays()
    {
        // Built in code: a test-data attribute cannot carry a lone surrogate.
        var lone = "A" + (char)0xD800;
        Assert.False(PageNumbering.IsValidPrefix(lone));
        Assert.False(PageNumbering.IsValidPrefix("A" + (char)0xFFFE));
        Assert.False(PageNumbering.IsValidPrefix("A" + (char)0xFFFF));
        Assert.False(PageNumbering.IsValidPrefix((char)0xDC00 + "A"));
        Assert.True(PageNumbering.IsValidPrefix("A" + char.ConvertFromUtf32(0x1F600)));
        var (card, answers) = Show(new NumberPagesContext(Facts(), false));
        using var _ = card;
        card.Type("PART_Prefix", lone);
        card.Click("PART_Ok");
        Assert.Empty(answers);
        Assert.Equal(WriteStrings.NumPagesInvalidPrefix, card.Find<TextBlock>("PART_Hint").Text);
    }

    [AvaloniaFact]
    public void ThePageStartLineShowsOnlyWhenTheCaretIsMidPage()
    {
        var (mid, _) = Show(new NumberPagesContext(Facts(), true));
        using (mid)
        {
            Assert.Equal(WriteStrings.NumPagesPageStartLine, mid.Find<TextBlock>("PART_PageStartLine").Text);
            Assert.Equal("This starts a new page here", WriteStrings.NumPagesPageStartLine);
        }

        var (top, _) = Show(new NumberPagesContext(Facts(), false));
        using (top)
        {
            Assert.Null(top.FindOrNull<TextBlock>("PART_PageStartLine"));
            Assert.DoesNotContain(WriteStrings.NumPagesPageStartLine, top.Texts());
        }
    }

    [AvaloniaFact]
    public void TheFollowingSectionsLineNamesTheCount()
    {
        // The caret is in section 1 (counted from 0) in the middle of it: a new section 2 starts here, so sections 3 and 4 follow it.
        var (card, _) = Show(new NumberPagesContext(Facts(section: 0, following: 2), true));
        using (card)
        {
            Assert.Contains("Sections 3 and 4 continue from here.", card.Texts());
        }

        // Three or more are a range, one is named alone.
        var (range, _) = Show(new NumberPagesContext(Facts(section: 0, following: 3), true));
        using (range)
        {
            Assert.Contains("Sections 3 to 5 continue from here.", range.Texts());
        }

        var (single, _) = Show(new NumberPagesContext(Facts(section: 1, following: 1), true));
        using (single)
        {
            Assert.Contains("Section 4 continues from here.", single.Texts());
        }

        // A caret at the start of a section edits that section: the sections after it start one number earlier.
        var (atStart, _) = Show(new NumberPagesContext(Facts(section: 1, atStart: true, following: 2), false));
        using (atStart)
        {
            Assert.Contains("Sections 3 and 4 continue from here.", atStart.Texts());
        }

        // No later section, no line.
        var (none, _) = Show(new NumberPagesContext(Facts(following: 0), true));
        using (none)
        {
            Assert.Null(none.FindOrNull<StackPanel>("PART_Following"));
        }
    }

    [AvaloniaFact]
    public void ALaterSectionWithItsOwnSettingIsSaidHonestly()
    {
        var (one, _) = Show(new NumberPagesContext(Facts(following: 3, own: 1), true));
        using (one)
        {
            Assert.Contains("Sections 3 to 5 continue from here.", one.Texts());
            Assert.Contains("Of these, 1 keeps its own numbering or footer.", one.Texts());
        }

        var (many, _) = Show(new NumberPagesContext(Facts(following: 3, own: 2), true));
        using (many)
        {
            Assert.Contains("Of these, 2 keep their own numbering or footer.", many.Texts());
        }
    }

    [AvaloniaFact]
    public void EnterConfirmsAndEscapeCancels()
    {
        var (card, answers) = Show(new NumberPagesContext(Facts(), false));
        using (card)
        {
            card.Type("PART_Start", "5");
            card.Press(card.Find<TextBox>("PART_Start"), PhysicalKey.Enter, Key.Enter);
            Assert.Equal([new PageNumbering(5, PageNumberFormat.Arabic, string.Empty)], answers);
        }

        var (second, escaped) = Show(new NumberPagesContext(Facts(), false));
        using (second)
        {
            second.Press(second.Find<TextBox>("PART_Start"), PhysicalKey.Escape, Key.Escape);
            Assert.Equal([null], escaped);
        }

        var (third, cancelled) = Show(new NumberPagesContext(Facts(), false));
        using (third)
        {
            third.Click("PART_Cancel");
            Assert.Equal([null], cancelled);
        }
    }

    [AvaloniaFact]
    public void TheFieldsHaveNamesForAScreenReaderAndTheFooterIsOneOkAndCancel()
    {
        var (card, _) = Show(new NumberPagesContext(Facts(), false));
        using var _ = card;
        foreach (var (name, label) in new[] { ("PART_Start", WriteStrings.NumPagesStartLabel), ("PART_Format", WriteStrings.NumPagesFormatLabel), ("PART_Prefix", WriteStrings.NumPagesPrefixLabel) })
        {
            Assert.Equal(label, Avalonia.Automation.AutomationProperties.GetName(card.Find<Control>(name)));
        }

        var buttons = Assert.IsType<StackPanel>(card.Dialog.Footer).Children.OfType<Button>().ToList();
        Assert.Equal(["PART_Ok", "PART_Cancel"], buttons.Select(b => b.Name ?? string.Empty).ToArray());
        Assert.True(buttons[0].IsDefault);
        Assert.True(buttons[1].IsCancel);
    }

    [AvaloniaFact]
    public void TheGermanCardFitsItsWidthAndIsInTheSieForm()
    {
        var saved = WriteStrings.Culture;
        try
        {
            WriteStrings.Culture = CultureInfo.GetCultureInfo("de");
            var (card, _) = Show(new NumberPagesContext(Facts(following: 3, own: 2), true));
            using var _ = card;
            var texts = card.Texts();
            Assert.Contains("Hier beginnt eine neue Seite", texts);
            Assert.Contains("Die Abschnitte 3 bis 5 folgen ab hier der neuen Nummerierung.", texts);
            Assert.Contains("Davon behalten 2 Abschnitte die eigene Nummerierung oder Fußzeile.", texts);

            // Nothing is cut off at the width the card has (640): every text block wraps inside it.
            card.Dialog.Measure(new Avalonia.Size(640, double.PositiveInfinity));
            Assert.True(card.Dialog.DesiredSize.Width <= 640, card.Dialog.DesiredSize.ToString());
            Assert.True(card.Dialog.DesiredSize.Height < 900, card.Dialog.DesiredSize.ToString());
            Assert.All(card.Dialog.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible), t => Assert.True(t.Bounds.Right <= card.Dialog.Bounds.Width + 0.5, t.Text));
        }
        finally
        {
            WriteStrings.Culture = saved;
        }
    }
}
