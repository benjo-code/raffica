using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Redaction;
using Raffica.Pdf.Redacting;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Dialogs;

/// <summary>What the user chose on the redaction Apply card: the way out and the pages they agreed to turn into images (the pages that cannot be redacted in place).</summary>
/// <param name="Choice">Back, Save as or the original file.</param>
/// <param name="TurnIntoImages">The 0-based pages that were ticked; empty when the card offered none.</param>
internal readonly record struct RedactApplyDecision(ApplyChoice Choice, IReadOnlyList<int> TurnIntoImages);

/// <summary>
/// The card before the redactions are written: how many marks on how many pages, what is removed (counts per page, what was removed as a whole instead of
/// precisely, the pages that become images), what the cleanup removes from the whole file every time (the information fields by name, never their text),
/// what the rewrite loses, what is not cleaned, the one sentence that a rewrite cannot be undone and that no backup copy is made, and the way out:
/// "Save as…" (the default, with the focus), "Save in the original file" (locked for 400 ms, never the default) and "Back". Pages that cannot be redacted
/// in place (<paramref name="rescue"/>) are listed with a tick each; the way out stays blocked until every one is ticked. The card shows the analysis,
/// and nothing of the document: no marked text, no value of an information field.
/// </summary>
internal static class RedactApplyCard
{
    /// <param name="fileName">The name of the open file.</param>
    /// <param name="plan">The plan that is analysed (for the summary line).</param>
    /// <param name="analysis">What the plan does (not refused; for a plan with <paramref name="rescue"/> pages it is the analysis with those pages as images).</param>
    /// <param name="rescue">The pages that cannot be redacted in place and what turning them into images does; empty for none.</param>
    /// <param name="target">Which ways out are offered.</param>
    /// <param name="done">The decision.</param>
    /// <param name="time">The clock of the arming delay (tests move it).</param>
    public static Dialog Create(string fileName, RedactionPlan plan, RedactionAnalysis analysis, IReadOnlyList<RasterPageInfo> rescue, ApplyTarget target, Action<RedactApplyDecision> done, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(rescue);
        ArgumentNullException.ThrowIfNull(done);

        var finished = false;
        ArmingLock? arming = null;
        var ticked = new HashSet<int>();
        bool AllTicked() => rescue.All(info => ticked.Contains(info.Page));
        void Finish(ApplyChoice choice)
        {
            // A click that gets here around the disabled buttons (a script, assistive technology) is no more able to apply early than the pointer.
            if (finished || (choice != ApplyChoice.Back && !AllTicked()))
            {
                return;
            }

            finished = true;
            done(new RedactApplyDecision(choice, choice == ApplyChoice.Back ? [] : [.. ticked.Order()]));
        }

        var body = new StackPanel { Spacing = 6 };
        var summary = PdfStrings.RedactCounter(plan.Marks.Count, plan.Pages.Count);
        var summaryBlock = new TextBlock { Name = "PART_Summary", Text = summary, TextWrapping = TextWrapping.Wrap };
        var report = new StackPanel { Name = "PART_Report", Spacing = 4 };
        RedactCardParts.AddSection(report, "PART_Removed", PdfStrings.RedactApplyRemovedHeading, RedactReportLines.Removed(analysis));
        RedactCardParts.AddSection(report, "PART_Whole", PdfStrings.RedactApplyWholeHeading, RedactReportLines.RemovedWhole(analysis));
        RedactCardParts.AddSection(report, "PART_Raster", PdfStrings.RedactApplyRasterHeading, RedactReportLines.RasterPages(analysis));
        RedactCardParts.AddSection(report, "PART_Images", PdfStrings.RedactApplyImageHeading, RedactReportLines.ImageAreas(analysis));
        RedactCardParts.AddSection(report, "PART_Overlay", PdfStrings.RedactApplyOverlayHeading, RedactReportLines.Overlay(analysis));
        RedactCardParts.AddSection(report, "PART_Cleaned", PdfStrings.RedactApplyCleanedHeading, RedactReportLines.Cleaned(analysis, confirmed: false));
        RedactCardParts.AddSection(report, "PART_Lost", PdfStrings.RedactApplyLostHeading, RedactReportLines.Lost(analysis));
        RedactCardParts.AddSection(report, "PART_NotCleaned", PdfStrings.RedactApplyNotCleanedHeading, RedactReportLines.NotCleaned(analysis));

        var saveAs = new Button { Name = "PART_SaveAs", Content = PdfStrings.ApplySaveAs };
        saveAs.Classes.Add("primary");
        saveAs.Click += (_, _) => Finish(ApplyChoice.SaveAs);
        var back = new Button { Name = "PART_Back", Content = PdfStrings.ApplyBack };
        back.Classes.Add("boxed");
        back.Click += (_, _) => Finish(ApplyChoice.Back);

        Button? original = null;
        if (target == ApplyTarget.Choose)
        {
            original = new Button { Name = "PART_Original", Content = PdfStrings.ApplyOriginal };
            original.Classes.Add("boxed");
        }

        // The ways out of the card (Save as and the original) come first, the cancel last; the ways are locked together until every rescue page is ticked.
        var buttons = CardFooter.Build(saveAs, original is null ? null : [original], back);
        var ways = new StackPanel { Name = "PART_Ways", Orientation = Orientation.Horizontal, Spacing = CardFooter.Spacing };
        buttons.Children.Remove(back);
        foreach (var way in buttons.Children.ToList())
        {
            buttons.Children.Remove(way);
            ways.Children.Add(way);
        }

        buttons.Children.Add(ways);
        buttons.Children.Add(back);

        // The pages that cannot be redacted in place: a tick each, and the way out waits for all of them.
        var blocked = new TextBlock { Name = "PART_RescueBlocked", Text = PdfStrings.RedactRescueBlocked, TextWrapping = TextWrapping.Wrap };
        if (rescue.Count > 0)
        {
            var offer = new StackPanel { Name = "PART_Rescue", Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
            offer.Children.Add(new TextBlock { Text = PdfStrings.RedactRescueHeading, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            offer.Children.Add(new TextBlock { Text = PdfStrings.RedactRescueIntro, TextWrapping = TextWrapping.Wrap });
            foreach (var info in rescue)
            {
                var megabytes = Math.Max(1, (info.EstimatedBytes + (1024 * 1024) - 1) / (1024 * 1024));
                var label = RedactReportLines.Fill(
                    PdfStrings.RedactRescueTickFormat,
                    (info.Page + 1).ToString(System.Globalization.CultureInfo.CurrentCulture),
                    info.Dpi.ToString(System.Globalization.CultureInfo.CurrentCulture),
                    info.WidthPx.ToString(System.Globalization.CultureInfo.CurrentCulture),
                    info.HeightPx.ToString(System.Globalization.CultureInfo.CurrentCulture),
                    megabytes.ToString(System.Globalization.CultureInfo.CurrentCulture));
                var box = new CheckBox { Name = "PART_Rescue_" + info.Page.ToString(System.Globalization.CultureInfo.InvariantCulture), Content = label };
                AutomationProperties.SetName(box, label);
                box.IsCheckedChanged += (_, _) =>
                {
                    if (box.IsChecked == true)
                    {
                        ticked.Add(info.Page);
                    }
                    else
                    {
                        ticked.Remove(info.Page);
                    }

                    var all = AllTicked();
                    if (all && !ways.IsEnabled)
                    {
                        // The original is reachable only now: its delay starts now, not when the card was made.
                        arming?.Rearm();
                    }

                    ways.IsEnabled = all;
                    blocked.IsVisible = !AllTicked();
                };
                offer.Children.Add(box);
            }

            ways.IsEnabled = false;
            body.Children.Add(summaryBlock);
            body.Children.Add(offer);
            body.Children.Add(blocked);
        }
        else
        {
            body.Children.Add(summaryBlock);
        }

        body.Children.Add(RedactCardParts.Scroll(report));
        body.Children.Insert(0, RedactCardParts.Name(fileName));
        body.Children.Add(new TextBlock { Name = "PART_Warning", Text = PdfStrings.ApplyWarning, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) });

        var dialog = new Dialog { Title = PdfStrings.RedactApplyTitle, Content = body, Footer = buttons };
        if (original is not null)
        {
            var lockNow = new ArmingLock(time, dialog, original);
            arming = lockNow;
            original.Click += (_, _) =>
            {
                if (lockNow.IsArmed && AllTicked())
                {
                    Finish(ApplyChoice.Original);
                }
            };
        }

        dialog.CancelRequested += (_, _) => Finish(ApplyChoice.Back);
        return dialog;
    }
}
