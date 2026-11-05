using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Dialogs;

/// <summary>What an extract or a split ended with.</summary>
/// <param name="Title">The title of the card.</param>
/// <param name="Status">How many files were written ("3 files were written." or "Nothing was written.").</param>
/// <param name="Reason">Why it stopped (a failure, a cancel), or null when everything was written.</param>
/// <param name="Files">The full paths of the files that were written, in order.</param>
/// <param name="Folder">The folder of the files, shown as text to copy (null: none).</param>
/// <param name="CanOpen">Whether the card offers to open the one file (a single-file extract that was written).</param>
internal sealed record PartsReport(string Title, string Status, string? Reason, IReadOnlyList<string> Files, string? Folder, bool CanOpen);

/// <summary>
/// The card after an extract or a split: the count, the reason it stopped when it did, the written files (at most ten, then the count of the rest),
/// the folder as selectable text and OK; after a single-file extract also "Open". Never colour alone: every state is a sentence.
/// </summary>
internal static class PartsResultCard
{
    private const int FilesShown = 10;

    /// <param name="report">What happened.</param>
    /// <param name="done">True when the user chose to open the file, false for OK or Esc.</param>
    public static Dialog Create(PartsReport report, Action<bool> done)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(done);

        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Name = "PART_Count", Text = report.Status, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
        if (report.Reason is not null)
        {
            body.Children.Add(new TextBlock { Name = "PART_Reason", Text = report.Reason, TextWrapping = TextWrapping.Wrap });
            if (report.Files.Count > 0)
            {
                body.Children.Add(new TextBlock { Text = PdfStrings.OutputWrittenBefore, TextWrapping = TextWrapping.Wrap });
            }
        }

        if (report.Files.Count > 0)
        {
            var list = new StackPanel { Name = "PART_Files", Spacing = 2 };
            foreach (var path in report.Files.Take(FilesShown))
            {
                list.Children.Add(new TextBlock { Name = "PART_File", Text = Path.GetFileName(path), TextWrapping = TextWrapping.Wrap });
            }

            if (report.Files.Count > FilesShown)
            {
                list.Children.Add(new TextBlock { Name = "PART_FilesMore", Text = PdfStrings.Fill(PdfStrings.OutputMoreFormat, report.Files.Count - FilesShown) });
            }

            body.Children.Add(new ScrollViewer { MaxHeight = 220, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = list });
        }

        if (report.Folder is not null)
        {
            body.Children.Add(new TextBlock { Text = PdfStrings.OutputFolderLabel, Margin = new Thickness(0, 4, 0, 0) });
            var folder = new TextBox { Name = "PART_Folder", Text = report.Folder, IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetName(folder, PdfStrings.OutputFolderLabel);
            body.Children.Add(folder);
        }

        var ok = new Button { Name = "PART_Ok", Content = Raffica.Shell.Localization.ShellStrings.Ok };
        ok.Classes.Add(report.CanOpen ? "boxed" : "primary");
        ok.Click += (_, _) => done(false);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (report.CanOpen)
        {
            var open = new Button { Name = "PART_Open", Content = PdfStrings.OutputOpen };
            open.Classes.Add("primary");
            open.Click += (_, _) => done(true);
            buttons.Children.Add(open);
        }

        buttons.Children.Add(ok);
        var dialog = new Dialog { Title = report.Title, Content = body, Footer = buttons };
        dialog.CancelRequested += (_, _) => done(false);
        return dialog;
    }
}
