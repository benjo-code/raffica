using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Shell.Controls;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Dialogs;
using Raffica.Write.Editing;
using Xunit;

namespace Raffica.WriteApp.Tests.Dialogs;

/// <summary>The three table cards (milestone 4.5a, Task 8): Insert table (columns 1 to 63, rows 1 to 500), Borders (preset or sides, style, width, colour) and Table properties (widths, row height, header row).</summary>
public sealed class TableCardsTests
{
    private static (CardWindow Window, List<TableSize?> Answers) ShowInsert()
    {
        var answers = new List<TableSize?>();
        return (new CardWindow(TableInsertCard.Create(answers.Add)), answers);
    }

    private static (CardWindow Window, List<BorderSpec?> Answers) ShowBorders()
    {
        var answers = new List<BorderSpec?>();
        return (new CardWindow(TableBordersCard.Create(answers.Add)), answers);
    }

    private static TablePropertiesFacts Facts(bool header = false, int rowHeight = 0, bool exact = false) =>
        new([2000, 2000, 2000], 6000, header, rowHeight, exact, 1, 2);

    private static (CardWindow Window, List<TableProperties?> Answers) ShowProperties(TablePropertiesFacts? facts = null, bool inches = false)
    {
        var answers = new List<TableProperties?>();
        return (new CardWindow(TablePropertiesCard.Create(new TablePropertiesContext(facts ?? Facts(), inches), answers.Add)), answers);
    }

    private static void Choose(CardWindow card, string name, int index)
    {
        card.Find<SelectBox>(name).SelectedIndex = index;
        CardWindow.Pump();
    }

    // ---- footers ---------------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void TheCardsAreBuiltByCardFooterWithTheirSafeAnswerFirstAndCancelLast()
    {
        foreach (var (key, make) in CardFooterTests.Entries().Where(e => e.Key.StartsWith("Table", StringComparison.Ordinal)))
        {
            var footer = Assert.IsType<StackPanel>(make().Footer);
            var buttons = footer.Children.OfType<Button>().ToList();
            Assert.Equal(CardFooterTests.DefaultButtons[key], Assert.Single(buttons, b => b.IsDefault).Name);
            Assert.Same(buttons[^1], Assert.Single(buttons, b => b.IsCancel));
            Assert.Equal(Raffica.Shell.Controls.CardFooter.Spacing, footer.Spacing);
        }

        Assert.Equal(3, CardFooterTests.Entries().Count(e => e.Key.StartsWith("Table", StringComparison.Ordinal)));
    }

    // ---- Insert table -----------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void InsertStartsWithThreeByThreeAndGivesTheSizeOnce()
    {
        var (card, answers) = ShowInsert();
        using var _ = card;
        Assert.Equal(WriteStrings.TiTitle, card.Dialog.Title);
        Assert.Equal(("3", "3"), (card.Find<TextBox>("PART_Columns").Text, card.Find<TextBox>("PART_Rows").Text));

        card.Type("PART_Columns", "63");
        card.Type("PART_Rows", "500");
        card.Click("PART_Ok");
        card.Click("PART_Ok");
        card.Click("PART_Cancel");
        Assert.Equal([new TableSize(63, 500)], answers);
    }

    [AvaloniaTheory]
    [InlineData("0", "3", "PART_Columns")]
    [InlineData("64", "3", "PART_Columns")]
    [InlineData("-1", "3", "PART_Columns")]
    [InlineData("abc", "3", "PART_Columns")]
    [InlineData("1e1", "3", "PART_Columns")]
    [InlineData("", "3", "PART_Columns")]
    [InlineData("3", "0", "PART_Rows")]
    [InlineData("3", "501", "PART_Rows")]
    [InlineData("3", "2.5", "PART_Rows")]
    public void InsertRefusesWrongSizesWithAHintAndStaysOpen(string columns, string rows, string wrong)
    {
        var (card, answers) = ShowInsert();
        using var _ = card;
        card.Type("PART_Columns", columns);
        card.Type("PART_Rows", rows);
        card.Click("PART_Ok");

        Assert.Empty(answers);
        var hint = card.Find<TextBlock>("PART_Hint");
        Assert.True(hint.IsVisible);
        Assert.Equal(wrong == "PART_Columns" ? WriteStrings.TiColumnsInvalid : WriteStrings.TiRowsInvalid, hint.Text);

        // Corrected, it answers.
        card.Type("PART_Columns", "2");
        card.Type("PART_Rows", "2");
        card.Click("PART_Ok");
        Assert.Equal([new TableSize(2, 2)], answers);
    }

    [AvaloniaFact]
    public void InsertCancelChangesNothing()
    {
        var (card, answers) = ShowInsert();
        using var _ = card;
        card.Click("PART_Cancel");
        Assert.Equal([null], answers);
    }

    // ---- Borders ---------------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void BordersStartWithAllAndTheLineIsSingleHalfAPointAutomatic()
    {
        var (card, answers) = ShowBorders();
        using var _ = card;
        Assert.Equal(WriteStrings.TbTitle, card.Dialog.Title);
        card.Click("PART_Ok");
        Assert.Equal(BorderSpec.All(), Assert.Single(answers));
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EachPresetGivesItsSpecWithTheStyleWidthAndColourOfTheCard(int preset)
    {
        var (card, answers) = ShowBorders();
        using var _ = card;
        Choose(card, "PART_Preset", preset);
        Choose(card, "PART_Style", 1);
        Choose(card, "PART_Width", 3);
        Choose(card, "PART_Color", 11);
        card.Click("PART_Ok");

        var argb = ColorChoices.TextColors[10].Argb;
        var expected = preset switch
        {
            0 => BorderSpec.All(TableBorderStyle.DoubleLine, 8, argb),
            1 => BorderSpec.Outside(TableBorderStyle.DoubleLine, 8, argb),
            2 => BorderSpec.Box(TableBorderStyle.DoubleLine, 8, argb),
            _ => BorderSpec.None(),
        };
        Assert.Equal(expected, Assert.Single(answers));
        Assert.True(TablePayloads.IsValid(Assert.Single(answers)!));
    }

    [AvaloniaFact]
    public void SingleSidesNeedASideAndCanBeTurnedOff()
    {
        var (card, answers) = ShowBorders();
        using var _ = card;
        Assert.False(card.Find<CheckBox>("PART_Top").IsEnabled);
        Choose(card, "PART_Preset", 4);
        Assert.True(card.Find<CheckBox>("PART_Top").IsEnabled);

        card.Click("PART_Ok");
        Assert.Empty(answers);
        Assert.Equal(WriteStrings.TbNoSide, card.Find<TextBlock>("PART_Hint").Text);

        card.Find<CheckBox>("PART_Top").IsChecked = true;
        card.Find<CheckBox>("PART_InsideV").IsChecked = true;
        Choose(card, "PART_Style", 2);
        card.Click("PART_Ok");
        Assert.Equal(BorderSpec.Sides(BorderSides.Top | BorderSides.InsideVertical, TableBorderStyle.Dashed), Assert.Single(answers));
    }

    [AvaloniaFact]
    public void TurningSidesOffDisablesTheLineFieldsAndGivesAnOffSpec()
    {
        var (card, answers) = ShowBorders();
        using var _ = card;
        Choose(card, "PART_Preset", 4);
        card.Find<CheckBox>("PART_Left").IsChecked = true;
        card.Find<CheckBox>("PART_TurnOff").IsChecked = true;
        CardWindow.Pump();
        Assert.False(card.Find<SelectBox>("PART_Style").IsEnabled);
        Assert.False(card.Find<SelectBox>("PART_Width").IsEnabled);
        Assert.False(card.Find<SelectBox>("PART_Color").IsEnabled);

        card.Click("PART_Ok");
        Assert.Equal(BorderSpec.Off(BorderSides.Left), Assert.Single(answers));

        // No borders disables them too.
        var (second, _) = ShowBorders();
        using var __ = second;
        Choose(second, "PART_Preset", 3);
        Assert.False(second.Find<SelectBox>("PART_Style").IsEnabled);
    }

    [Fact]
    public void ThePresetsMeanWhatTheirWordsSay()
    {
        // Outside draws the outline and touches no line inside; Box draws the outline and turns the lines inside off (the decision of Task 7, worded in the picker and the card).
        Assert.Equal(BorderSides.Outer, TablePayloads.Preset("outside")!.Set);
        Assert.Equal(BorderSides.None, TablePayloads.Preset("outside")!.Cleared);
        Assert.Equal(BorderSides.Outer, TablePayloads.Preset("box")!.Set);
        Assert.Equal(BorderSides.Inside, TablePayloads.Preset("box")!.Cleared);
        Assert.Equal(BorderSides.All, TablePayloads.Preset("all")!.Set);
        Assert.Equal(BorderSides.All, TablePayloads.Preset("none")!.Cleared);
        Assert.Null(TablePayloads.Preset("sparkles"));
        Assert.All(TablePayloads.BorderPresets, id => Assert.NotNull(TablePayloads.Preset(id)));
        Assert.All(TablePayloads.BorderPresets, id => Assert.True(TablePayloads.IsValid(TablePayloads.Preset(id)!)));
    }

    // ---- Table properties -------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void PropertiesShowTheTableAndNothingChangedGivesNoAnswer()
    {
        var (card, answers) = ShowProperties(Facts(header: true, rowHeight: 600, exact: true));
        using var _ = card;
        Assert.Equal(WriteStrings.TpTitle, card.Dialog.Title);
        Assert.Equal("10.58", card.Find<TextBox>("PART_TableWidth").Text);
        foreach (var name in new[] { "PART_Column1", "PART_Column2", "PART_Column3" })
        {
            Assert.Equal("3.53", card.Find<TextBox>(name).Text);
        }
        Assert.Equal(2, card.Find<SelectBox>("PART_RowRule").SelectedIndex);
        Assert.Equal("1.06", card.Find<TextBox>("PART_RowHeight").Text);
        Assert.True(card.Find<CheckBox>("PART_HeaderRow").IsChecked);

        card.Click("PART_Ok");
        Assert.Equal([null], answers);
    }

    [AvaloniaFact]
    public void AColumnOverwritesTheWholeSetAndATableWidthAloneKeepsTheProportions()
    {
        var (card, answers) = ShowProperties();
        using var _ = card;
        card.Type("PART_Column2", "5");
        card.Click("PART_Ok");
        var changed = Assert.Single(answers)!;
        Assert.Equal([2000, 2835, 2000], changed.ColumnTwips!.ToArray());
        Assert.Null(changed.TableWidthTwips);
        Assert.Null(changed.RowHeight);
        Assert.Null(changed.HeaderRow);

        var (second, again) = ShowProperties();
        using var __ = second;
        second.Type("PART_TableWidth", "8");
        second.Click("PART_Ok");
        var width = Assert.Single(again)!;
        Assert.Equal(4535, width.TableWidthTwips);
        Assert.Null(width.ColumnTwips);
    }

    [AvaloniaFact]
    public void TheRowHeightAndTheHeaderRowAreTheirOwnParts()
    {
        var (card, answers) = ShowProperties();
        using var _ = card;
        Assert.False(card.Find<TextBox>("PART_RowHeight").IsEnabled);
        Choose(card, "PART_RowRule", 1);
        Assert.True(card.Find<TextBox>("PART_RowHeight").IsEnabled);
        card.Type("PART_RowHeight", "2");
        card.Find<CheckBox>("PART_HeaderRow").IsChecked = true;
        card.Click("PART_Ok");
        var result = Assert.Single(answers)!;
        Assert.Equal(new TableRowHeight(1, 2, 1134, false), result.RowHeight);
        Assert.True(result.HeaderRow);
        Assert.Null(result.ColumnTwips);
        Assert.Null(result.TableWidthTwips);

        // Back to automatic removes the height.
        var (second, other) = ShowProperties(Facts(rowHeight: 600));
        using var __ = second;
        Choose(second, "PART_RowRule", 0);
        second.Click("PART_Ok");
        Assert.Equal(new TableRowHeight(1, 2, 0, false), Assert.Single(other)!.RowHeight);
    }

    [AvaloniaTheory]
    [InlineData("PART_Column1", "abc")]
    [InlineData("PART_Column1", "0.01")]
    [InlineData("PART_Column3", "2000")]
    [InlineData("PART_TableWidth", "")]
    public void PropertiesRefuseWrongLengthsWithAHintAndStayOpen(string field, string text)
    {
        var (card, answers) = ShowProperties();
        using var _ = card;
        card.Type(field, text);
        card.Click("PART_Ok");
        Assert.Empty(answers);
        Assert.True(card.Find<TextBlock>("PART_Hint").IsVisible);
        card.Click("PART_Cancel");
        Assert.Equal([null], answers);
    }

    [AvaloniaFact]
    public void AnExactRowHeightNeedsAHeightAndLengthsAreInInchesByRegion()
    {
        var (card, answers) = ShowProperties(inches: true);
        using var _ = card;
        Assert.Equal("4.17", card.Find<TextBox>("PART_TableWidth").Text);
        Choose(card, "PART_RowRule", 2);
        card.Type("PART_RowHeight", string.Empty);
        card.Click("PART_Ok");
        Assert.Empty(answers);
        card.Type("PART_RowHeight", "1");
        card.Click("PART_Ok");
        Assert.Equal(new TableRowHeight(1, 2, 1440, true), Assert.Single(answers)!.RowHeight);
    }
}
