using Avalonia.Controls;
using Avalonia.Layout;
using Raffica.Core.Redaction;
using Raffica.Pdf.Redacting;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The card after the redactions were written and passed the check: what was removed, what was cleaned (read back from the written file), what was lost,
/// what is not cleaned, what the check covered ("Checked", shown only for a result that passed) and what it did not ("Not checked", always shown, so
/// the report never promises more than the check did). The sentences come from the tables of the UI language, never from the English text of Core;
/// no sentence contains the redacted text or a value of the document.
/// </summary>
internal static class RedactResultCard
{
    /// <param name="fileName">The name of the written file.</param>
    /// <param name="analysis">The analysis made before applying.</param>
    /// <param name="check">The check the written file passed.</param>
    /// <param name="done">Raised when the card is closed.</param>
    public static Dialog Create(string fileName, RedactionAnalysis analysis, RedactionCheck check, Action done)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(done);

        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(RedactCardParts.Name(fileName));
        body.Children.Add(new TextBlock { Name = "PART_Intro", Text = PdfStrings.Fill(PdfStrings.RedactResultIntroFormat, fileName), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var report = new StackPanel { Name = "PART_Report", Spacing = 4 };
        RedactCardParts.AddSection(report, "PART_Removed", PdfStrings.RedactResRemovedHeading, [.. RedactReportLines.Removed(analysis), .. RedactReportLines.RemovedWhole(analysis), .. RedactReportLines.RasterPages(analysis)]);
        RedactCardParts.AddSection(report, "PART_Images", PdfStrings.RedactApplyImageHeading, RedactReportLines.ImageAreas(analysis));
        RedactCardParts.AddSection(report, "PART_Overlay", PdfStrings.RedactApplyOverlayHeading, RedactReportLines.Overlay(analysis));

        // "Cleaned" and "Checked" say what the read-back confirmed: a result with findings never gets here, and neither section is built for one.
        if (check.IsClean)
        {
            RedactCardParts.AddSection(report, "PART_Cleaned", PdfStrings.RedactResCleanedHeading, RedactReportLines.Cleaned(analysis, confirmed: true));
        }

        RedactCardParts.AddSection(report, "PART_Lost", PdfStrings.RedactResLostHeading, RedactReportLines.Lost(analysis));
        RedactCardParts.AddSection(report, "PART_NotCleaned", PdfStrings.RedactApplyNotCleanedHeading, RedactReportLines.NotCleaned(analysis));
        if (check.IsClean)
        {
            RedactCardParts.AddSection(report, "PART_Checked", PdfStrings.RedactResCheckedHeading, RedactReportLines.Checked(analysis));
        }

        RedactCardParts.AddSection(report, "PART_NotChecked", PdfStrings.RedactResNotCheckedHeading, RedactReportLines.NotChecked(analysis, check));
        body.Children.Add(RedactCardParts.Scroll(report));

        var ok = new Button { Name = "PART_Ok", Content = ShellStrings.Ok };
        ok.Classes.Add("primary");
        var dialog = new Dialog
        {
            Title = PdfStrings.RedactResultTitle,
            Content = body,
            Footer = CardFooter.Only(ok),
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
