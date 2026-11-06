using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Pdf.Ocr;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The card after a text recognition: the counts first, then one line each for what was written, every page that was not recognized with its reason, the notes on
/// pages and, plainly, whether the run can be undone. The text comes from <see cref="OcrWording"/>; it never contains the recognized words and never says that the
/// result is right. Never colour alone: every state is a sentence. OK (primary) closes it.
/// </summary>
internal static class OcrReportCard
{
    /// <param name="report">What the run says.</param>
    /// <param name="done">Called when the card is closed.</param>
    public static Dialog Create(OcrReportText report, Action done)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(done);

        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Name = "PART_Summary", Text = report.Summary, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
        var lines = new StackPanel { Name = "PART_Lines", Spacing = 4 };
        foreach (var line in report.Lines)
        {
            lines.Children.Add(new TextBlock { Name = "PART_Line", Text = line, TextWrapping = TextWrapping.Wrap });
        }

        var scroll = new ScrollViewer { MaxHeight = 280, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = lines };
        AutomationProperties.SetName(scroll, report.Title);
        body.Children.Add(scroll);

        var ok = new Button { Name = "PART_Ok", Content = ShellStrings.Ok };
        ok.Classes.Add("primary");
        var dialog = new Dialog
        {
            Title = report.Title,
            Content = body,
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ok } },
        };
        var finished = false;
        void Finish()
        {
            if (!finished)
            {
                finished = true;
                done();
            }
        }

        ok.Click += (_, _) => Finish();
        dialog.CancelRequested += (_, _) => Finish();
        return dialog;
    }
}
