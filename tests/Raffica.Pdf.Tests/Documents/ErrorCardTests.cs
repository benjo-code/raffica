using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Pdf.Dialogs;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Documents;

public sealed class ErrorCardTests
{
    private static readonly PdfOpenError[] AllErrors = Enum.GetValues<PdfOpenError>();

    [Fact]
    public void EveryOpenErrorHasADistinctEnglishAndGermanText()
    {
        Assert.NotEmpty(AllErrors);
        foreach (var culture in new[] { "en", "de" })
        {
            PdfStrings.Culture = CultureInfo.GetCultureInfo(culture);
            var texts = AllErrors.Select(PdfStrings.OpenError).ToList();
            Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t)));
            Assert.Equal(texts.Count, texts.Distinct().Count());

            // No raw exception text or internals: nothing in code spelling, no type or enum names.
            Assert.All(texts, t => Assert.DoesNotContain("Exception", t, StringComparison.Ordinal));
            Assert.All(AllErrors, e => Assert.DoesNotContain(e.ToString(), PdfStrings.OpenError(e), StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AnUndefinedOpenErrorThrows() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfStrings.OpenError((PdfOpenError)(-1)));

    [AvaloniaFact]
    public void TheCardShowsTheFileTheReasonAndOneButton()
    {
        var done = 0;
        var dialog = ErrorCard.Create("broken.pdf", PdfOpenError.NotAPdf, () => done++);
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 600, Height = 400 };
        window.Show();
        host.Show(dialog);
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }

        var texts = dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("broken.pdf", texts);
        Assert.Contains(PdfStrings.OpenError(PdfOpenError.NotAPdf), texts);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(dialog)));

        var ok = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Ok");
        Assert.True(ok.IsFocused);
        ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, done);
        window.Close();
    }
}
