using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Pages;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Organizing;

/// <summary>One pending change of a page as the item shows it: an icon of the shell's set and a text (never a colour alone).</summary>
/// <param name="Icon">The name in the icon set (<c>Raffica.Icon.&lt;name&gt;</c>).</param>
/// <param name="Text">The words.</param>
internal readonly record struct PendingMark(string Icon, string Text);

/// <summary>One slot of the plan as the list holds it: its position, the slot, whether it is a further copy and, for an inserted page, the file name.</summary>
/// <param name="Position">The zero-based position in the plan.</param>
/// <param name="Slot">The slot.</param>
/// <param name="IsCopy">The page of a source that appears more than once, from the second on.</param>
/// <param name="FileName">The name of the file an inserted page comes from, else <see langword="null"/>.</param>
internal sealed record OrganizeItem(int Position, PageSlot Slot, bool IsCopy, string? FileName)
{
    private const double MillimetresPerPoint = 25.4 / 72;

    /// <summary>The pending changes of the page, in the UI language: turned, new or inserted, copy. Empty for a page that is as it was.</summary>
    public IReadOnlyList<PendingMark> Marks
    {
        get
        {
            var marks = new List<PendingMark>(3);
            if (Slot.ExtraRotation != 0)
            {
                marks.Add(new PendingMark("Redo", PdfStrings.Fill(PdfStrings.OrganizeTurnedFormat, Slot.ExtraRotation)));
            }

            if (Slot.Blank is { } blank)
            {
                var size = PdfStrings.Fill(PdfStrings.OrganizeBlankSizeFormat, Math.Round(blank.Width * MillimetresPerPoint), Math.Round(blank.Height * MillimetresPerPoint));
                marks.Add(new PendingMark("Plus", PdfStrings.OrganizeBlank + " (" + size + ")"));
            }
            else if (Slot.Source.Value != 0)
            {
                marks.Add(new PendingMark("ChevronDown", PdfStrings.Fill(PdfStrings.OrganizeFromFileFormat, FileName ?? string.Empty, Slot.SourceIndex + 1)));
            }

            if (IsCopy)
            {
                marks.Add(new PendingMark("Info", PdfStrings.OrganizeCopy));
            }

            return marks;
        }
    }

    /// <summary>The name a screen reader gets: the page and what is pending for it.</summary>
    public string AutomationName =>
        string.Join(", ", Marks.Select(m => m.Text).Prepend(PdfStrings.PageThumbnailName(Position + 1, current: false)));
}

/// <summary>The content of one item of the organize list: the picture of the page, its position number and one line of icon plus text per pending change.</summary>
internal sealed class OrganizeItemView : StackPanel
{
    public OrganizeItemView(OrganizeThumbnails thumbnails, OrganizeItem item)
    {
        Orientation = Orientation.Vertical;
        Spacing = 4;
        HorizontalAlignment = HorizontalAlignment.Center;
        Children.Add(new OrganizeThumbnail(thumbnails, item.Slot));
        var number = new TextBlock { Text = PdfStrings.Fill("{0}", item.Position + 1), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 };
        number.Classes.Add("thumbnail-number");
        Children.Add(number);
        foreach (var mark in item.Marks)
        {
            Children.Add(MarkRow(mark));
        }
    }

    private static Grid MarkRow(PendingMark mark)
    {
        var icon = new StrokeIcon { Size = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
        icon.Bind(StrokeIcon.DataProperty, icon.GetResourceObservable("Raffica.Icon." + mark.Icon, value => value as Geometry));
        var text = new TextBlock
        {
            Text = mark.Text,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            Margin = new Thickness(4, 0, 0, 0),
        };
        text.Classes.Add("thumbnail-number");
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), MaxWidth = ThumbnailWidth };
        row.Children.Add(icon);
        row.Children.Add(text);
        Grid.SetColumn(text, 1);
        return row;
    }

    private static double ThumbnailWidth => Raffica.Core.Viewing.ThumbnailPlanner.Width;
}
