using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Dialogs;

/// <summary>A validation hint of a card (text in the danger color that appears when an entry is wrong) is read out when it appears.</summary>
public sealed class CardHintTests : IDisposable
{
    public void Dispose() => PdfStrings.Culture = CultureInfo.GetCultureInfo("en");

    [AvaloniaFact]
    public void EveryDangerTextOfEveryCardIsAnAssertiveLiveRegion()
    {
        var problems = new List<string>();
        var hints = new List<string>();
        foreach (var (key, make) in CardFooterTests.Entries())
        {
            var host = new DialogHost { Content = new Border() };
            var window = new Window { Content = host, Width = 800, Height = 700 };
            window.Show();
            var dialog = make();
            host.Show(dialog);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.TryFindResource("Raffica.Brush.Danger", window.ActualThemeVariant, out var danger), "the danger brush is not found");
            var color = ((ISolidColorBrush)danger!).Color;
            foreach (var text in dialog.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Foreground is ISolidColorBrush b && b.Color == color))
            {
                hints.Add($"{key}/{text.Name}");
                if (AutomationProperties.GetLiveSetting(text) != AutomationLiveSetting.Assertive)
                {
                    problems.Add($"{key}: {text.Name} is {AutomationProperties.GetLiveSetting(text)}");
                }
            }

            window.Close();
        }

        // The cards with a validation hint the finding names are among them.
        foreach (var card in new[] { "GoToPageCard", "ExportImagesCard", "SplitCard", "ExtractCard", "PasswordCard", "InsertFileCard", "RecognizeCard", "PrintCard", "NewSignatureCard", "SignaturePickerCard" })
        {
            Assert.Contains(hints, h => h.StartsWith(card, StringComparison.Ordinal));
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
