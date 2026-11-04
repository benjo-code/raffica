using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Pdf;
using Raffica.Pdf.Viewing;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The facts about the open document: file name, folder, page count, page size, PDF version, file size and the metadata (title, author,
/// creation date). A value that is not known shows "Not set" instead of an empty cell. One button closes the card.
/// </summary>
internal static class DocumentInfoCard
{
    private const double ValueWidth = 340;

    /// <param name="info">What Core's <see cref="DocumentInfoReader"/> read.</param>
    /// <param name="done">Called when the user closes the card.</param>
    /// <param name="firstPageSize">The size of the first page in points, when it is known (<see cref="DocumentInfo"/> carries none).</param>
    public static Dialog Create(DocumentInfo info, Action done, PdfPageSize? firstPageSize = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(done);

        var culture = PdfStrings.Culture;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16, RowSpacing = 6 };
        var row = 0;
        void Add(string label, string? value, string name)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var caption = new TextBlock { Text = label, Name = name + "Label", VerticalAlignment = VerticalAlignment.Top };
            caption.Classes.Add("secondary");
            var text = new TextBlock
            {
                Name = name,
                Text = string.IsNullOrWhiteSpace(value) ? PdfStrings.InfoNotSet : value,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = ValueWidth,
            };
            Grid.SetRow(caption, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            grid.Children.Add(caption);
            grid.Children.Add(text);
            row++;
        }

        Add(PdfStrings.InfoFileName, info.FileName, "PART_FileName");
        Add(PdfStrings.InfoFolder, info.Folder, "PART_Folder");
        Add(PdfStrings.InfoPages, info.PageCount.ToString("0", culture), "PART_Pages");
        Add(PdfStrings.InfoPageSize, firstPageSize is { } size ? ViewCommands.PageSizeText(size, precise: true) : null, "PART_PageSize");
        Add(PdfStrings.InfoPdfVersion, info.PdfVersion, "PART_Version");
        Add(PdfStrings.InfoFileSize, info.FileSizeBytes > 0 ? FileSizeText(info.FileSizeBytes, culture) : null, "PART_FileSize");
        Add(PdfStrings.InfoDocumentTitle, info.Title, "PART_DocumentTitle");
        Add(PdfStrings.InfoAuthor, info.Author, "PART_Author");
        Add(PdfStrings.InfoCreated, info.Created?.ToString("d", culture), "PART_Created");

        var close = new Button { Name = "PART_Ok", Content = ShellStrings.Close };
        close.Classes.Add("primary");
        AutomationProperties.SetName(grid, PdfStrings.InfoTitle);
        var dialog = new Dialog
        {
            Title = PdfStrings.InfoTitle,
            Content = grid,
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { close } },
        };
        close.Click += (_, _) => done();
        dialog.CancelRequested += (_, _) => done();
        return dialog;
    }

    /// <summary>A file size in the largest unit that keeps it below 1024, with one decimal when that matters: <c>512 B</c>, <c>1.5 MB</c>, <c>3 MB</c>.</summary>
    internal static string FileSizeText(long bytes, CultureInfo culture)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString(unit == 0 ? "0" : "0.#", culture) + " " + units[unit];
    }
}
