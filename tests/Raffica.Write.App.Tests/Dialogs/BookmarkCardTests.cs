using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Dialogs;
using Xunit;

namespace Raffica.WriteApp.Tests.Dialogs;

/// <summary>The Bookmarks card (milestone 4.5b): its list, the three answers, the validity hint for a name and the one-answer rule.</summary>
public sealed class BookmarkCardTests
{
    private static BookmarkContext Context(params string[] names) => new(names, [.. names, "_Ref77"]);

    private static (CardWindow Window, List<BookmarkRequest?> Answers) Show(BookmarkContext context)
    {
        var answers = new List<BookmarkRequest?>();
        return (new CardWindow(BookmarkCard.Create(context, answers.Add)), answers);
    }

    [AvaloniaFact]
    public void TheListHoldsTheVisibleNamesAndAnEmptyDocumentSaysSo()
    {
        var (card, _) = Show(Context("Intro", "Summary"));
        using (card)
        {
            Assert.Equal(["Intro", "Summary"], card.Find<ListBox>("PART_List").Items.Cast<string>().ToArray());
            Assert.False(card.Find<TextBlock>("PART_Empty").IsVisible);
            Assert.Equal(WriteStrings.BookmarkTitle, card.Dialog.Title);
            Assert.False(card.Find<Button>("PART_GoTo").IsEnabled);
            Assert.False(card.Find<Button>("PART_Delete").IsEnabled);
        }

        var (none, _) = Show(Context());
        using (none)
        {
            Assert.False(none.Find<ListBox>("PART_List").IsVisible);
            Assert.True(none.Find<TextBlock>("PART_Empty").IsVisible);
            Assert.Equal(WriteStrings.BookmarkNone, none.Find<TextBlock>("PART_Empty").Text);
        }
    }

    [AvaloniaFact]
    public void AddAnswersTheTypedNameOnce()
    {
        var (card, answers) = Show(Context("Intro"));
        using var _ = card;
        card.Type("PART_Name", "Summary");

        card.Click("PART_Ok");

        Assert.Equal([new BookmarkRequest(BookmarkAction.Add, "Summary")], answers);
        card.Click("PART_Cancel");
        Assert.Single(answers);
    }

    [AvaloniaFact]
    public void ABadNameShowsItsReasonAndKeepsTheCardOpen()
    {
        (string Name, string Hint)[] cases =
        [
            (string.Empty, WriteStrings.BookmarkProblemEmpty),
            (new string('a', 41), WriteStrings.BookmarkProblemTooLong),
            ("1a", WriteStrings.BookmarkProblemBadStart),
            ("_x", WriteStrings.BookmarkProblemBadStart),
            ("a b", WriteStrings.BookmarkProblemBadCharacter),
            ("a-b", WriteStrings.BookmarkProblemBadCharacter),
            ("INTRO", WriteStrings.BookmarkProblemDuplicate),
            ("_Ref77", WriteStrings.BookmarkProblemBadStart),
        ];
        foreach (var (name, hint) in cases)
        {
            var (card, answers) = Show(Context("Intro"));
            using var _ = card;
            card.Type("PART_Name", name);

            card.Click("PART_Ok");

            Assert.Empty(answers);
            var shown = card.Find<TextBlock>("PART_Hint");
            Assert.True(shown.IsVisible, name);
            Assert.Equal(hint, shown.Text);
        }
    }

    [AvaloniaFact]
    public void ADuplicateOfAHiddenNameIsRefusedToo()
    {
        var (card, answers) = Show(new BookmarkContext(["Intro"], ["Intro", "Hidden"]));
        using var _ = card;
        card.Type("PART_Name", "hidden");

        card.Click("PART_Ok");

        Assert.Empty(answers);
        Assert.Equal(WriteStrings.BookmarkProblemDuplicate, card.Find<TextBlock>("PART_Hint").Text);
    }

    [AvaloniaFact]
    public void GoToAndDeleteActOnTheSelectedRowOrTheNameTyped()
    {
        var (card, answers) = Show(Context("Intro", "Summary"));
        using (card)
        {
            card.Find<ListBox>("PART_List").SelectedIndex = 1;
            CardWindow.Pump();
            Assert.Equal("Summary", card.Find<TextBox>("PART_Name").Text);
            Assert.True(card.Find<Button>("PART_GoTo").IsEnabled);

            card.Click("PART_GoTo");
            Assert.Equal([new BookmarkRequest(BookmarkAction.GoTo, "Summary")], answers);
        }

        var (second, deleted) = Show(Context("Intro", "Summary"));
        using (second)
        {
            second.Type("PART_Name", "intro");
            Assert.True(second.Find<Button>("PART_Delete").IsEnabled);

            second.Click("PART_Delete");

            // The answer carries the name as the document spells it.
            Assert.Equal([new BookmarkRequest(BookmarkAction.Delete, "Intro")], deleted);
        }

        var (third, none) = Show(Context("Intro"));
        using (third)
        {
            third.Type("PART_Name", "Other");
            Assert.False(third.Find<Button>("PART_GoTo").IsEnabled);
            Assert.False(third.Find<Button>("PART_Delete").IsEnabled);
            third.Click("PART_GoTo");
            Assert.Empty(none);
        }
    }

    [AvaloniaFact]
    public void CloseAndEscapeAnswerNull()
    {
        var (card, answers) = Show(Context("Intro"));
        using (card)
        {
            card.Click("PART_Cancel");
            Assert.Equal([null], answers);
        }

        var (escaped, again) = Show(Context("Intro"));
        using (escaped)
        {
            escaped.Press(escaped.Find<TextBox>("PART_Name"), PhysicalKey.Escape, Key.Escape);
            Assert.Equal([null], again);
        }
    }

    [AvaloniaFact]
    public void EnterInTheNameBoxAdds()
    {
        var (card, answers) = Show(Context("Intro"));
        using var _ = card;
        card.Type("PART_Name", "Summary");

        card.Press(card.Find<TextBox>("PART_Name"), PhysicalKey.Enter, Key.Enter);

        Assert.Equal([new BookmarkRequest(BookmarkAction.Add, "Summary")], answers);
    }

    [Fact]
    public void ARequestIsWellFormedOnlyWithAValidNameForAdd()
    {
        Assert.True(new BookmarkRequest(BookmarkAction.Add, "Intro").IsValid);
        Assert.False(new BookmarkRequest(BookmarkAction.Add, "1x").IsValid);
        Assert.False(new BookmarkRequest(BookmarkAction.Add, string.Empty).IsValid);
        Assert.True(new BookmarkRequest(BookmarkAction.GoTo, "_Ref1").IsValid);
        Assert.False(new BookmarkRequest((BookmarkAction)9, "x").IsValid);
    }
}
