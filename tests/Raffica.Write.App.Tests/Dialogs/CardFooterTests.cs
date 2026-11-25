using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Raffica.Core.Saving;
using Raffica.Shell.Controls;
using Raffica.Write.Commands;
using Raffica.Write.Dialogs;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.WriteApp.Tests.Dialogs;

/// <summary>
/// The cards with more than one way out (the save cards of Task 19): every footer is built by <c>CardFooter.Build</c>, the primary action first (the default, Enter), the cancel last
/// (Esc). The single-button cards are walked by <see cref="OpenErrorWordingTests"/>; <see cref="OpenErrorWordingTests.EveryCardFactoryIsListed"/> asks both lists.
/// </summary>
public sealed class CardFooterTests
{
    internal static IEnumerable<(string Key, Func<Dialog> Make)> Entries()
    {
        yield return ("ChangedOriginalCard.Create", () => ChangedOriginalCard.Create("a.docx", _ => { }));
        yield return ("SaveErrorCard.Create", () => SaveErrorCard.Create("a.docx", new SaveProblem(SaveProblemKind.Failure, SaveFailureReason.NotWritable), () => { }));
        yield return ("UnsavedChangesCard.Create", () => UnsavedChangesCard.Create("a.docx", _ => { }));
        yield return ("RecoveryCard.Create", () => RecoveryCard.Create([], _ => { }));
        yield return ("NumberPagesCard.Create", () => NumberPagesCard.Create(new NumberPagesContext(new SectionFacts(true, 0, false, 0, 0, SectionReason.None), false), _ => { }));
        yield return ("PageSetupCard.Create", () => PageSetupCard.Create(new PageSetupContext(new SectionPageSetup(11906, 16838, false, 1417, 1417, 1417, 1417, 1, 720, false), ApplyScope.WholeDocument, 1, false, false), _ => { }));
        yield return ("TableInsertCard.Create", () => TableInsertCard.Create(_ => { }));
        yield return ("TableBordersCard.Create", () => TableBordersCard.Create(_ => { }));
        yield return ("TablePropertiesCard.Create", () => TablePropertiesCard.Create(new TablePropertiesContext(new TablePropertiesFacts([2000, 2000], 4000, false, 0, false, 0, 0), false), _ => { }));
    }

    // The button that Enter presses on each card: the safe answer, never Overwrite or Don't save.
    internal static readonly Dictionary<string, string> DefaultButtons = new(StringComparer.Ordinal)
    {
        ["ChangedOriginalCard.Create"] = "PART_SaveAs",
        ["SaveErrorCard.Create"] = "PART_Ok",
        ["UnsavedChangesCard.Create"] = "PART_Save",
        ["RecoveryCard.Create"] = "PART_Restore",
        ["NumberPagesCard.Create"] = "PART_Ok",
        ["PageSetupCard.Create"] = "PART_Ok",
        ["TableInsertCard.Create"] = "PART_Ok",
        ["TableBordersCard.Create"] = "PART_Ok",
        ["TablePropertiesCard.Create"] = "PART_Ok",
    };

    private static List<Button> Buttons(Dialog dialog) => [.. Assert.IsType<StackPanel>(dialog.Footer).Children.OfType<Button>()];

    [AvaloniaFact]
    public void EveryFooterHasItsDefaultFirstAndItsCancelLast()
    {
        foreach (var (key, make) in Entries())
        {
            var buttons = Buttons(make());
            Assert.Equal(DefaultButtons[key], Assert.Single(buttons, b => b.IsDefault).Name);
            Assert.Equal(DefaultButtons[key], buttons[0].Name);
            var cancel = Assert.Single(buttons, b => b.IsCancel);
            Assert.Same(buttons[^1], cancel);
        }
    }

    [AvaloniaFact]
    public void TheQuestionsHaveTheirAnswersInTheOrderOfThePdfApp()
    {
        Assert.Equal(["PART_SaveAs", "PART_Overwrite", "PART_Cancel"], Buttons(ChangedOriginalCard.Create("a.docx", _ => { })).Select(b => b.Name));
        Assert.Equal(["PART_Save", "PART_DontSave", "PART_Cancel"], Buttons(UnsavedChangesCard.Create("a.docx", _ => { })).Select(b => b.Name));
        var error = Buttons(SaveErrorCard.Create("a.docx", new SaveProblem(SaveProblemKind.Unexpected), () => { }));
        var only = Assert.Single(error);
        Assert.True(only.IsDefault && only.IsCancel);
    }

    private static void Click(Dialog dialog, string name) => Buttons(dialog).Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void EachButtonGivesItsAnswerOnce()
    {
        foreach (var (button, expected) in new[] { ("PART_SaveAs", ChangedChoice.SaveAs), ("PART_Overwrite", ChangedChoice.Overwrite), ("PART_Cancel", ChangedChoice.Cancel) })
        {
            var answers = new List<ChangedChoice>();
            var dialog = ChangedOriginalCard.Create("a.docx", answers.Add);
            Click(dialog, button);
            Click(dialog, "PART_Cancel");
            Assert.Equal([expected], answers);
        }

        foreach (var (button, expected) in new[] { ("PART_Save", CloseChoice.Save), ("PART_DontSave", CloseChoice.DontSave), ("PART_Cancel", CloseChoice.Cancel) })
        {
            var answers = new List<CloseChoice>();
            var dialog = UnsavedChangesCard.Create("a.docx", answers.Add);
            Click(dialog, button);
            Click(dialog, "PART_Save");
            Assert.Equal([expected], answers);
        }
    }

    [AvaloniaFact]
    public void TheCardsNameTheFileAndSayWhatHappenedWithoutTheTextOfAnException()
    {
        var unsaved = UnsavedChangesCard.Create("report.docx", _ => { });
        Assert.Equal(WriteStrings.UnsavedTitle, unsaved.Title);
        Assert.Equal(WriteStrings.Fill(WriteStrings.UnsavedMessageFormat, "report.docx"), Assert.IsType<TextBlock>(unsaved.Content).Text);

        var failed = SaveErrorCard.Create("report.docx", new SaveProblem(SaveProblemKind.Check, Check: Raffica.Write.Docx.DocxSaveReason.PartsChanged), () => { });
        var texts = Assert.IsType<StackPanel>(failed.Content).Children.OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Equal(["report.docx", WriteStrings.ForSaveCheck(Raffica.Write.Docx.DocxSaveReason.PartsChanged), WriteStrings.SaveErrorKept], texts);
    }
}
