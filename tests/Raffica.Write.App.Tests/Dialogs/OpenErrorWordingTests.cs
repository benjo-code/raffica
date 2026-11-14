using System.Globalization;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Raffica.Write.Dialogs;
using Raffica.Write.Documents;
using Xunit;

namespace Raffica.WriteApp.Tests.Dialogs;

/// <summary>The words of the open errors (every member has a text in both languages and no text is the text of an exception) and the guard over every card factory of the app.</summary>
public sealed class OpenErrorWordingTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");

    public static TheoryData<OpenError> Members => [.. Enum.GetValues<OpenError>()];

    [Theory]
    [MemberData(nameof(Members))]
    public void EveryErrorHasWordingInBothLanguages(OpenError error)
    {
        var key = WriteStrings.OpenErrorKey(error);
        var english = WriteStrings.GetIn(key, English);
        var german = WriteStrings.GetIn(key, German);
        Assert.False(string.IsNullOrWhiteSpace(english));
        Assert.False(string.IsNullOrWhiteSpace(german));
        Assert.NotEqual(english, german);
        Assert.EndsWith(".", english, StringComparison.Ordinal);
        Assert.EndsWith(".", german, StringComparison.Ordinal);
    }

    [Fact]
    public void TheErrorsHaveDistinctWordsAndAnUnknownValueHasNone()
    {
        var all = Enum.GetValues<OpenError>().Select(e => WriteStrings.GetIn(WriteStrings.OpenErrorKey(e), English)).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(6, all.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => WriteStrings.OpenErrorKey((OpenError)99));
    }

    [Fact]
    public void TheWordingNeverNamesTheFileSystemOrTheLibrary()
    {
        foreach (var error in Enum.GetValues<OpenError>())
        {
            foreach (var culture in new[] { English, German })
            {
                var text = WriteStrings.GetIn(WriteStrings.OpenErrorKey(error), culture);
                Assert.DoesNotContain("Exception", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("HRESULT", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("0x", text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // Every card has its footer in one order (primary first, Cancel last, one button for a card with a single way out); a factory that is not listed fails.
    internal static IEnumerable<(string Key, Func<Dialog> Make)> Entries()
    {
        yield return ("OpenErrorCard.Create", () => OpenErrorCard.Create("a.docx", OpenError.NotADocx, () => { }));
        yield return ("OpenFailuresCard.Create", () => OpenFailuresCard.Create(["a.docx", "b.docx"], () => { }));
    }

    [Fact]
    public void EveryCardFactoryIsListed()
    {
        var listed = Entries().Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var factories = typeof(OpenErrorCard).Assembly.GetTypes()
            .Where(t => t.Namespace == "Raffica.Write.Dialogs" && t.IsAbstract && t.IsSealed)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.ReturnType == typeof(Dialog))
                .Select(m => $"{t.Name}.{m.Name}"))
            .ToList();

        Assert.NotEmpty(factories);
        Assert.All(factories, f => Assert.Contains(f, listed));
    }

    [AvaloniaFact]
    public void EveryCardHasOneOkThatIsBothDefaultAndCancel()
    {
        foreach (var (key, make) in Entries())
        {
            var dialog = make();
            var footer = Assert.IsType<StackPanel>(dialog.Footer);
            var buttons = footer.Children.OfType<Button>().ToList();
            var button = Assert.Single(buttons);
            Assert.True(button.IsDefault, key);
            Assert.True(button.IsCancel, key);
            Assert.Equal("PART_Ok", button.Name);
        }
    }

    [AvaloniaFact]
    public void TheCardNamesTheFileAndTheReasonAndOkAndEscapeDismissIt()
    {
        var done = 0;
        var dialog = OpenErrorCard.Create("report.docx", OpenError.Corrupt, () => done++);
        var texts = dialog.Content is StackPanel panel ? panel.Children.OfType<TextBlock>().Select(t => t.Text).ToList() : [];
        Assert.Equal(["report.docx", WriteStrings.ForOpenError(OpenError.Corrupt)], texts);
        Assert.Equal(WriteStrings.ErrorTitle, dialog.Title);

        ((Button)((StackPanel)dialog.Footer!).Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, done);
    }

    [Fact]
    public void TheFailuresCardListsFiveNamesThenTheRest()
    {
        var names = Enumerable.Range(1, 8).Select(i => $"f{i}.docx").ToList();
        var lines = OpenFailuresCard.Lines(names);
        Assert.Equal(["f1.docx", "f2.docx", "f3.docx", "f4.docx", "f5.docx", "and 3 more"], lines);
        Assert.Equal(["f1.docx", "f2.docx"], OpenFailuresCard.Lines(["f1.docx", "f2.docx"]));
    }
}
