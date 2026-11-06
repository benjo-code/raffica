using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Pages;
using Raffica.Pdf.Organizing;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Dialogs;

/// <summary>What the user chose on the Apply card.</summary>
internal enum ApplyChoice
{
    /// <summary>Do not apply (Back, Esc, the close button); the plan stays.</summary>
    Back,

    /// <summary>Write a new file.</summary>
    SaveAs,

    /// <summary>Replace the open file.</summary>
    Original,
}

/// <summary>Which ways out the Apply card offers.</summary>
internal enum ApplyTarget
{
    /// <summary>Save as and Save in the original file (Save, the Apply command).</summary>
    Choose,

    /// <summary>Only Save as (the user asked for Save as).</summary>
    SaveAs,
}

/// <summary>
/// The card before a page plan is written: what the plan does (the summary), what the rewrite loses or changes (one sentence per loss of the
/// report), the one sentence that a rewrite cannot be undone and that no backup copy is made, and the way out: "Save as..." (the default, with the
/// focus), "Save in the original file" (locked for 400 ms like the other destructive choices, never the default) and "Back". A report that is
/// refused shows only its reason and "Back": nothing is written.
/// </summary>
internal static class ApplyCard
{
    // The list of losses scrolls beyond this height; the buttons stay in view.
    private const double LossesMaxHeight = 260;

    /// <param name="fileName">The name of the open file.</param>
    /// <param name="summary">What the plan does.</param>
    /// <param name="report">What the rewrite loses.</param>
    /// <param name="sourceName">The display name of an inserted file by its number.</param>
    /// <param name="target">Which ways out are offered.</param>
    /// <param name="done">The choice.</param>
    /// <param name="time">The clock of the arming delay (tests move it).</param>
    public static Dialog Create(string fileName, PlanSummary summary, PageEditReport report, Func<int, string?> sourceName, ApplyTarget target, Action<ApplyChoice> done, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(sourceName);
        ArgumentNullException.ThrowIfNull(done);

        var finished = false;
        void Finish(ApplyChoice choice)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            done(choice);
        }

        var back = new Button { Name = "PART_Back", Content = PdfStrings.ApplyBack };
        back.Classes.Add("boxed");
        back.Click += (_, _) => Finish(ApplyChoice.Back);

        Dialog dialog;
        if (report.IsRefused)
        {
            var intro = new TextBlock { Name = "PART_Refusal", Text = PdfStrings.ApplyRefusedIntro, TextWrapping = TextWrapping.Wrap };
            var reasons = new TextBlock { Name = "PART_Reason", Text = PageLossWording.RefusalOf(report, sourceName), TextWrapping = TextWrapping.Wrap };
            back.Classes.Remove("boxed");
            back.Classes.Add("primary");
            dialog = new Dialog
            {
                Title = PdfStrings.ApplyRefusedTitle,
                Content = new StackPanel { Spacing = 8, Children = { Name(fileName), intro, reasons } },
                Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { back } },
            };
        }
        else
        {
            var summaryText = new TextBlock { Name = "PART_Summary", Text = SummaryText(summary), TextWrapping = TextWrapping.Wrap };
            var body = new StackPanel { Spacing = 8, Children = { Name(fileName), summaryText } };

            if (report.Losses.Count == 0)
            {
                body.Children.Add(new TextBlock { Name = "PART_NoLosses", Text = PdfStrings.ApplyNoLosses, TextWrapping = TextWrapping.Wrap });
            }
            else
            {
                var losses = new StackPanel { Name = "PART_Losses", Spacing = 6 };
                body.Children.Add(new TextBlock { Text = PdfStrings.ApplyLossesHeading, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
                foreach (var loss in report.Losses)
                {
                    // A bullet and the sentence: a loss is text, never colour alone.
                    var sentence = PageLossWording.Describe(loss, sourceName);
                    var line = new TextBlock { Name = "PART_Loss", Text = "• " + sentence, TextWrapping = TextWrapping.Wrap };
                    AutomationProperties.SetName(line, sentence);
                    losses.Children.Add(line);
                }

                body.Children.Add(new ScrollViewer
                {
                    MaxHeight = LossesMaxHeight,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = losses,
                });
            }

            body.Children.Add(new TextBlock { Name = "PART_Warning", Text = PdfStrings.ApplyWarning, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) });

            var saveAs = new Button { Name = "PART_SaveAs", Content = PdfStrings.ApplySaveAs };
            saveAs.Classes.Add("primary");
            saveAs.Click += (_, _) => Finish(ApplyChoice.SaveAs);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { saveAs } };
            Button? original = null;
            if (target == ApplyTarget.Choose)
            {
                original = new Button { Name = "PART_Original", Content = PdfStrings.ApplyOriginal };
                original.Classes.Add("boxed");
                buttons.Children.Add(original);
            }

            buttons.Children.Add(back);
            dialog = new Dialog { Title = PdfStrings.ApplyTitle, Content = body, Footer = buttons };
            if (original is not null)
            {
                var arming = new ArmingLock(time, dialog, original);
                original.Click += (_, _) =>
                {
                    if (arming.IsArmed)
                    {
                        Finish(ApplyChoice.Original);
                    }
                };
            }
        }

        dialog.CancelRequested += (_, _) => Finish(ApplyChoice.Back);
        return dialog;
    }

    /// <summary>The summary line: "12 → 10 pages, 3 turned, 1 file inserted".</summary>
    public static string SummaryText(PlanSummary summary)
    {
        var parts = new List<string> { PdfStrings.Fill(PdfStrings.ApplySummaryPagesFormat, summary.PagesBefore, summary.PagesAfter) };
        if (summary.Turned > 0)
        {
            parts.Add(PdfStrings.Fill(PdfStrings.ApplySummaryTurnedFormat, summary.Turned));
        }

        if (summary.InsertedFiles > 0)
        {
            parts.Add(summary.InsertedFiles == 1 ? PdfStrings.ApplySummaryFileOne : PdfStrings.Fill(PdfStrings.ApplySummaryFilesFormat, summary.InsertedFiles));
        }

        if (summary.BlankPages > 0)
        {
            parts.Add(summary.BlankPages == 1 ? PdfStrings.ApplySummaryBlankOne : PdfStrings.Fill(PdfStrings.ApplySummaryBlankFormat, summary.BlankPages));
        }

        if (summary.Duplicated > 0)
        {
            parts.Add(PdfStrings.Fill(PdfStrings.ApplySummaryDuplicatedFormat, summary.Duplicated));
        }

        return string.Join(", ", parts);
    }

    private static TextBlock Name(string fileName) => new() { Text = fileName, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
}
