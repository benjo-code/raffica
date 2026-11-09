using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// Tells the user that several files of one open could not be opened: the names of the first <see cref="MaxNames"/> (one text block each), then "and N more", one OK button.
/// Twenty files that are no PDFs are one card, not twenty.
/// </summary>
internal static class OpenFailuresCard
{
    /// <summary>How many file names the card lists.</summary>
    internal const int MaxNames = 5;

    /// <summary>The lines of the list: the first names, then the line "and N more" when there are more.</summary>
    internal static IReadOnlyList<string> Lines(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var lines = names.Take(MaxNames).ToList();
        if (names.Count > MaxNames)
        {
            lines.Add(PdfStrings.Fill(PdfStrings.OpenFailuresMoreFormat, names.Count - MaxNames));
        }

        return lines;
    }

    public static Dialog Create(IReadOnlyList<string> names, Action done)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(done);

        var list = new StackPanel { Spacing = 2 };
        var shown = Math.Min(names.Count, MaxNames);
        var lines = Lines(names);
        for (var i = 0; i < lines.Count; i++)
        {
            list.Children.Add(new TextBlock { Text = lines[i], FontWeight = i < shown ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap });
        }

        var message = new TextBlock { Text = PdfStrings.OpenFailuresMessage, TextWrapping = TextWrapping.Wrap };
        var ok = new Button { Name = "PART_Ok", Content = ShellStrings.Ok };
        ok.Classes.Add("primary");

        var dialog = new Dialog
        {
            Title = PdfStrings.OpenFailuresTitle,
            Content = new StackPanel { Spacing = 8, Children = { message, list } },
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ok } },
        };
        ok.Click += (_, _) => done();
        dialog.CancelRequested += (_, _) => done();
        return dialog;
    }
}
